using System.Security.Cryptography;
using Vizstrap.Core.Logging;

namespace Vizstrap.Core.Effects;

/// <summary>
/// The depth AI: Depth Anything V2 Small (Apache-2.0), the ONNX export from onnx-community in half
/// precision, pinned to one revision and checked by SHA-256. Downloaded on first use (about 50 MB).
/// </summary>
public static class DepthModel
{
    public const string FileName = "depth-anything-v2-small-fp16.onnx";

    public const string Url =
        "https://huggingface.co/onnx-community/depth-anything-v2-small/resolve/4472b7362082ad9968fee890ca0f1e5aca36b93d/onnx/model_fp16.onnx";

    public const string Sha256 = "2df6223f206b5164e21f664ace61dabeb9bb6a49b8b5a3e00510b4807d0f5b04";

    public const long Size = 49_642_442;

    private const string LogSource = nameof(DepthModel);

    public static string PathIn(string folder) => Path.Combine(folder, FileName);

    /// <summary>Downloaded and whole (checked by size; the hash was checked when it was downloaded).</summary>
    public static bool IsReady(string folder) =>
        new FileInfo(PathIn(folder)) is { Exists: true, Length: Size };

    /// <summary>Downloads the model unless it's there; returns its path.</summary>
    /// <param name="progress">0–1 while downloading.</param>
    public static async Task<string> EnsureAsync(
        HttpClient http, string folder, IProgress<double>? progress, CancellationToken cancellationToken,
        string url = Url, string sha256 = Sha256, long size = Size)
    {
        string path = PathIn(folder);

        if (new FileInfo(path) is { Exists: true } existing && existing.Length == size)
            return path;

        Directory.CreateDirectory(folder);
        string partial = path + ".part";

        using (var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
        {
            response.EnsureSuccessStatusCode();

            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var target = File.Create(partial);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[81920];
            long received = 0;
            int read;

            while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                hash.AppendData(buffer, 0, read);
                received += read;
                progress?.Report(Math.Min(1, (double)received / size));
            }

            string actual = Convert.ToHexStringLower(hash.GetHashAndReset());

            if (received != size || actual != sha256)
            {
                target.Close();
                File.Delete(partial);
                throw new InvalidDataException($"The depth model download is damaged ({received} bytes, SHA-256 {actual}).");
            }
        }

        File.Move(partial, path, overwrite: true);
        Log.Info(LogSource, $"Downloaded the depth model to {path}");
        return path;
    }
}
