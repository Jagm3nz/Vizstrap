using System.IO.Compression;

namespace Vizstrap.Core.Install;

/// <summary>
/// Extracts a Roblox package. Roblox zips contain entries such as "/" and "/content/" with a leading
/// slash, which <see cref="ZipFile.ExtractToDirectory(string, string)"/> rejects as escaping the target,
/// so entries are normalised here while still refusing anything that would land outside the target.
/// </summary>
public static class PackageExtractor
{
    /// <summary>Extracts one file (path relative to the package root) to <paramref name="destinationFile"/>; false if the package lacks it.</summary>
    public static bool ExtractEntry(string zipPath, string relativePath, string destinationFile)
    {
        string wanted = Normalize(relativePath);

        using var archive = ZipFile.OpenRead(zipPath);
        var entry = archive.Entries.FirstOrDefault(entry =>
            string.Equals(Normalize(entry.FullName), wanted, StringComparison.OrdinalIgnoreCase));

        if (entry is null)
            return false;

        Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!);
        entry.ExtractToFile(destinationFile, overwrite: true);
        return true;
    }

    private static string Normalize(string entryName) => entryName.Replace('\\', '/').TrimStart('/');

    public static void Extract(string zipPath, string destinationDirectory, CancellationToken cancellationToken = default)
    {
        string root = Path.GetFullPath(destinationDirectory);
        string rootWithSeparator = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;

        Directory.CreateDirectory(root);

        using var archive = ZipFile.OpenRead(zipPath);

        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string relative = entry.FullName.Replace('\\', '/').TrimStart('/');

            if (relative.Length == 0)
                continue;

            string target = Path.GetFullPath(Path.Combine(root, relative));

            if (!target.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Entry '{entry.FullName}' in {Path.GetFileName(zipPath)} points outside the target folder.");

            if (relative.EndsWith('/'))
            {
                Directory.CreateDirectory(target);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);
        }
    }
}
