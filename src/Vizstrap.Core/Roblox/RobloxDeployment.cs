using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Vizstrap.Core.Logging;

namespace Vizstrap.Core.Roblox;

/// <param name="Version">Human-readable version, e.g. "0.740.0.7400927".</param>
/// <param name="VersionGuid">Deployment id, e.g. "version-2366ba214ec740ca".</param>
public sealed record ClientVersion(string Version, string VersionGuid);

/// <summary>Raised when no Roblox deployment mirror could be reached.</summary>
public sealed class RobloxConnectionException(IReadOnlyList<Exception> attempts)
    : Exception("Could not connect to any Roblox deployment mirror.", attempts.FirstOrDefault())
{
    public IReadOnlyList<Exception> Attempts { get; } = attempts;
}

/// <summary>
/// Talks to Roblox's deployment servers: picks a working setup mirror, asks for the current
/// WindowsPlayer version and reads that version's package manifest.
/// </summary>
public sealed class RobloxDeployment
{
    /// <summary>Every healthy mirror returns this for "/versionStudio" (the last MFC Studio build).</summary>
    public const string VersionStudioHash = "version-012732894899482c";

    public const string BinaryType = "WindowsPlayer";

    /// <summary>Mirrors with a priority; priority N starts testing after N × priority step.</summary>
    public static IReadOnlyList<(string Url, int Priority)> Mirrors { get; } =
    [
        ("https://setup.rbxcdn.com", 0),
        ("https://setup-aws.rbxcdn.com", 2),
        ("https://setup-ak.rbxcdn.com", 2),
        ("https://roblox-setup.cachefly.net", 2),
        ("https://s3.amazonaws.com/setup.roblox.com", 4),
    ];

    public static IReadOnlyList<string> ClientSettingsHosts { get; } =
    [
        "https://clientsettingscdn.roblox.com",
        "https://clientsettings.roblox.com",
    ];

    private const string LogSource = nameof(RobloxDeployment);

    private readonly HttpClient _http;
    private readonly TimeSpan _priorityStep;
    private readonly TimeSpan _mirrorTimeout;

    public RobloxDeployment(HttpClient http, TimeSpan? priorityStep = null, TimeSpan? mirrorTimeout = null)
    {
        _http = http;
        _priorityStep = priorityStep ?? TimeSpan.FromSeconds(1);
        _mirrorTimeout = mirrorTimeout ?? TimeSpan.FromSeconds(10);
    }

    /// <summary>The mirror chosen by <see cref="SelectMirrorAsync"/>.</summary>
    public string? Mirror { get; private set; }

    /// <summary>
    /// Tests all mirrors (higher priority ones get a head start) and keeps the first healthy one.
    /// Doubles as the internet connectivity check.
    /// </summary>
    public async Task<string> SelectMirrorAsync(CancellationToken cancellationToken)
    {
        if (Mirror is not null)
            return Mirror;

        using var raceCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var pending = Mirrors.Select(mirror => TestMirrorAsync(mirror.Url, mirror.Priority, raceCancellation.Token)).ToList();
        var failures = new List<Exception>();

        try
        {
            while (pending.Count > 0)
            {
                var finished = await Task.WhenAny(pending);
                pending.Remove(finished);

                if (finished.IsCompletedSuccessfully)
                {
                    Mirror = finished.Result;
                    Log.Info(LogSource, $"Using mirror {Mirror}");
                    return Mirror;
                }

                if (finished.Exception is not null)
                    failures.Add(finished.Exception.InnerException ?? finished.Exception);
            }
        }
        finally
        {
            raceCancellation.Cancel();
        }

        cancellationToken.ThrowIfCancellationRequested();
        throw new RobloxConnectionException(failures);
    }

    private async Task<string> TestMirrorAsync(string url, int priority, CancellationToken cancellationToken)
    {
        await Task.Delay(_priorityStep * priority, cancellationToken);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_mirrorTimeout);

        try
        {
            string body = await _http.GetStringAsync($"{url}/versionStudio", timeout.Token);

            if (body.Trim() != VersionStudioHash)
                throw new HttpRequestException($"{url} returned an unexpected versionStudio response.");

            return url;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // a cancelled task would not be reported as a failure, so surface the timeout as one
            Log.Warn(LogSource, $"Mirror {url} timed out");
            throw new TimeoutException($"{url} did not respond within {_mirrorTimeout.TotalSeconds:0} s.");
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            Log.Warn(LogSource, $"Mirror {url} failed: {ex.Message}");
            throw;
        }
    }

    public async Task<ClientVersion> GetLatestVersionAsync(CancellationToken cancellationToken)
    {
        Exception? lastError = null;

        foreach (string host in ClientSettingsHosts)
        {
            try
            {
                var response = await _http.GetFromJsonAsync<ClientVersionResponse>(
                    $"{host}/v2/client-version/{BinaryType}", cancellationToken);

                if (response is null || string.IsNullOrEmpty(response.ClientVersionUpload))
                    throw new HttpRequestException($"{host} returned an empty client version.");

                Log.Info(LogSource, $"Latest version is {response.Version} ({response.ClientVersionUpload}) from {host}");
                return new ClientVersion(response.Version ?? "", response.ClientVersionUpload);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                Log.Warn(LogSource, $"Version lookup via {host} failed: {ex.Message}");
                lastError = ex;
            }
        }

        throw new RobloxConnectionException([lastError!]);
    }

    public async Task<IReadOnlyList<Package>> GetPackageManifestAsync(string versionGuid, CancellationToken cancellationToken)
    {
        string manifest = await _http.GetStringAsync(GetFileUrl(versionGuid, "rbxPkgManifest.txt"), cancellationToken);
        return PackageManifest.Parse(manifest);
    }

    public string GetFileUrl(string versionGuid, string fileName)
    {
        if (Mirror is null)
            throw new InvalidOperationException("Select a mirror first.");

        return $"{Mirror}/channel/common/{versionGuid}-{fileName}";
    }

    private sealed class ClientVersionResponse
    {
        [JsonPropertyName("version")]
        public string? Version { get; set; }

        [JsonPropertyName("clientVersionUpload")]
        public string? ClientVersionUpload { get; set; }
    }
}
