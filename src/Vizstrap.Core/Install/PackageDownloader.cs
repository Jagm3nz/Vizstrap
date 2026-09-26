using System.Security.Cryptography;
using Vizstrap.Core.Logging;
using Vizstrap.Core.Roblox;

namespace Vizstrap.Core.Install;

/// <summary>Raised when a package could not be downloaded intact after all attempts.</summary>
public sealed class PackageDownloadException(string packageName, Exception inner)
    : Exception($"Could not download {packageName}.", inner)
{
    public string PackageName { get; } = packageName;
}

/// <param name="Path">Verified package file in the download cache.</param>
/// <param name="FromCache">True when nothing was downloaded.</param>
public sealed record DownloadResult(string Path, bool FromCache);

/// <summary>
/// Puts a verified copy of a package into the download cache (files named by MD5):
/// reuses Vizstrap's cache, then the official bootstrapper's cache, and only then downloads.
/// </summary>
public sealed class PackageDownloader
{
    private const string LogSource = nameof(PackageDownloader);

    private readonly HttpClient _http;
    private readonly string _cacheDirectory;
    private readonly string? _robloxCacheDirectory;
    private readonly int _maxAttempts;
    private readonly TimeSpan _retryDelay;
    private readonly TimeSpan _stallTimeout;

    public PackageDownloader(
        HttpClient http,
        string cacheDirectory,
        string? robloxCacheDirectory,
        int maxAttempts = 5,
        TimeSpan? retryDelay = null,
        TimeSpan? stallTimeout = null)
    {
        _http = http;
        _cacheDirectory = cacheDirectory;
        _robloxCacheDirectory = robloxCacheDirectory;
        _maxAttempts = maxAttempts;
        _retryDelay = retryDelay ?? TimeSpan.FromSeconds(1);
        _stallTimeout = stallTimeout ?? TimeSpan.FromSeconds(30);
    }

    /// <param name="onNetworkBytes">Called with bytes received; negative when a failed attempt is rolled back.</param>
    public async Task<DownloadResult> DownloadAsync(
        Package package, string url, Action<long> onNetworkBytes, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_cacheDirectory);

        string target = Path.Combine(_cacheDirectory, package.Signature);

        if (File.Exists(target))
        {
            if (await ComputeMd5Async(target, cancellationToken) == package.Signature)
                return new DownloadResult(target, FromCache: true);

            Log.Warn(LogSource, $"Cached {package.Name} is corrupted, downloading again");
            File.Delete(target);
        }

        if (await TryCopyFromRobloxCacheAsync(package, target, cancellationToken))
            return new DownloadResult(target, FromCache: true);

        for (int attempt = 1; ; attempt++)
        {
            long received = 0;
            string partial = target + ".part";

            try
            {
                await DownloadToFileAsync(package, url, partial, bytes =>
                {
                    received += bytes;
                    onNetworkBytes(bytes);
                }, cancellationToken);

                File.Move(partial, target, overwrite: true);
                Log.Info(LogSource, $"Downloaded {package.Name} ({received} bytes)");
                return new DownloadResult(target, FromCache: false);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                Log.Warn(LogSource, $"Attempt {attempt}/{_maxAttempts} for {package.Name} failed: {ex.Message}");

                TryDelete(partial);
                onNetworkBytes(-received);

                if (attempt >= _maxAttempts)
                    throw new PackageDownloadException(package.Name, ex);

                await Task.Delay(_retryDelay * attempt, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                TryDelete(partial);
                throw;
            }
        }
    }

    private async Task DownloadToFileAsync(
        Package package, string url, string destination, Action<long> onBytes, CancellationToken cancellationToken)
    {
        // HttpClient.Timeout stops applying once headers arrive, so guard against a stalled body ourselves
        using var stall = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        stall.CancelAfter(_stallTimeout);

        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, stall.Token);
        response.EnsureSuccessStatusCode();

        await using var source = await response.Content.ReadAsStreamAsync(stall.Token);
        await using var file = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        using var md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5);

        var buffer = new byte[81920];

        try
        {
            while (true)
            {
                int read = await source.ReadAsync(buffer, stall.Token);

                if (read == 0)
                    break;

                stall.CancelAfter(_stallTimeout);

                await file.WriteAsync(buffer.AsMemory(0, read), stall.Token);
                md5.AppendData(buffer, 0, read);

                onBytes(read);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Download of {package.Name} stalled.");
        }

        string hash = Convert.ToHexStringLower(md5.GetHashAndReset());

        if (hash != package.Signature)
            throw new InvalidDataException($"MD5 mismatch for {package.Name}: expected {package.Signature}, got {hash}.");
    }

    private async Task<bool> TryCopyFromRobloxCacheAsync(Package package, string target, CancellationToken cancellationToken)
    {
        if (_robloxCacheDirectory is null)
            return false;

        string robloxCopy = Path.Combine(_robloxCacheDirectory, package.Signature);

        if (!File.Exists(robloxCopy))
            return false;

        string partial = target + ".part";

        try
        {
            File.Copy(robloxCopy, partial, overwrite: true);

            if (await ComputeMd5Async(partial, cancellationToken) != package.Signature)
            {
                Log.Warn(LogSource, $"Roblox's cached copy of {package.Name} does not match, ignoring it");
                TryDelete(partial);
                return false;
            }

            File.Move(partial, target, overwrite: true);
            Log.Info(LogSource, $"Reused {package.Name} from Roblox's download cache");
            return true;
        }
        catch (IOException ex)
        {
            Log.Warn(LogSource, $"Could not reuse Roblox's copy of {package.Name}: {ex.Message}");
            TryDelete(partial);
            return false;
        }
    }

    internal static async Task<string> ComputeMd5Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        return Convert.ToHexStringLower(await MD5.HashDataAsync(stream, cancellationToken));
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
