namespace Vizstrap.Core.Roblox;

/// <param name="Name">File name on the CDN, e.g. "RobloxApp.zip".</param>
/// <param name="Signature">Lower-case MD5 of the packed file.</param>
/// <param name="PackedSize">Download size in bytes.</param>
/// <param name="Size">Unpacked size in bytes.</param>
public sealed record Package(string Name, string Signature, long PackedSize, long Size);

/// <summary>
/// Parser for "{version}-rbxPkgManifest.txt": a "v0" header followed by 4-line records
/// (name, md5, packed size, unpacked size).
/// </summary>
public static class PackageManifest
{
    public static IReadOnlyList<Package> Parse(string text)
    {
        using var reader = new StringReader(text);

        string? header = reader.ReadLine()?.Trim();

        if (header != "v0")
            throw new FormatException($"Unsupported package manifest version '{header}' (expected v0).");

        var packages = new List<Package>();

        while (true)
        {
            string? name = reader.ReadLine()?.Trim();
            string? signature = reader.ReadLine()?.Trim();
            string? packedSize = reader.ReadLine()?.Trim();
            string? size = reader.ReadLine()?.Trim();

            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(signature) ||
                string.IsNullOrEmpty(packedSize) || string.IsNullOrEmpty(size))
                break;

            packages.Add(new Package(name, signature.ToLowerInvariant(), long.Parse(packedSize), long.Parse(size)));
        }

        return packages;
    }
}
