using System.IO.Compression;
using Vizstrap.Core.Logging;

namespace Vizstrap.Core.Packages;

/// <summary>
/// Mod packages made by players: a .vzmod (a zip) or a folder with vizmod.json at the top. Installed
/// ones live in their own folders here, named by id; installing the same id again replaces it (an update).
/// </summary>
public sealed class PackageStore(string folder)
{
    public const string Extension = ".vzmod";

    /// <summary>Limits against a package that's a zip bomb or plain huge.</summary>
    public const long MaxSize = 500L * 1024 * 1024;
    public const int MaxFiles = 20_000;

    private const string LogSource = nameof(PackageStore);

    public string Folder { get; } = folder;

    public IReadOnlyList<InstalledPackage> List()
    {
        if (!Directory.Exists(Folder))
            return [];

        var packages = new List<InstalledPackage>();

        foreach (string directory in Directory.GetDirectories(Folder))
        {
            string manifest = Path.Combine(directory, PackageManifest.FileName);

            if (!File.Exists(manifest))
                continue;

            try
            {
                packages.Add(new InstalledPackage(PackageManifest.Parse(File.ReadAllText(manifest)), directory));
            }
            catch (InvalidDataException ex)
            {
                Log.Warn(LogSource, $"Skipping {directory}: {ex.Message}");
            }
        }

        return [.. packages.OrderBy(package => package.Manifest.Name, StringComparer.CurrentCultureIgnoreCase)];
    }

    public InstalledPackage? Find(string id) => List().FirstOrDefault(package => package.Id == id);

    /// <summary>Installs a .vzmod/.zip file or a folder; throws <see cref="InvalidDataException"/> saying what's wrong.</summary>
    public InstalledPackage Install(string source)
    {
        string staging = Path.Combine(Folder, ".installing-" + Guid.NewGuid().ToString("N")[..8]);

        try
        {
            if (Directory.Exists(source))
                CopyFolder(source, staging);
            else
                ExtractZip(source, staging);

            string root = FindRoot(staging);
            var manifest = PackageManifest.Parse(File.ReadAllText(Path.Combine(root, PackageManifest.FileName)));
            string target = Path.Combine(Folder, manifest.Id);

            if (Directory.Exists(target))
                Directory.Delete(target, recursive: true);

            Directory.Move(root, target);
            Log.Info(LogSource, $"Installed {manifest.Id} {manifest.Version} from {source}");
            return new InstalledPackage(manifest, target);
        }
        finally
        {
            if (Directory.Exists(staging))
                Directory.Delete(staging, recursive: true);
        }
    }

    public void Remove(string id)
    {
        if (!PackageManifest.IsValidId(id))
            throw new ArgumentException($"Not a package id: {id}", nameof(id));

        string target = Path.Combine(Folder, id);

        if (Directory.Exists(target))
            Directory.Delete(target, recursive: true);

        Log.Info(LogSource, $"Removed {id}");
    }

    /// <summary>Zips a package folder into a .vzmod to share, after checking its manifest.</summary>
    public static void Pack(string sourceFolder, string packagePath)
    {
        PackageManifest.Parse(File.ReadAllText(Path.Combine(sourceFolder, PackageManifest.FileName)));

        if (File.Exists(packagePath))
            File.Delete(packagePath);

        ZipFile.CreateFromDirectory(sourceFolder, packagePath, CompressionLevel.Optimal, includeBaseDirectory: false);
    }

    /// <summary>A starting point for making a package: a manifest, an example effect, an example plugin and the guide.</summary>
    public static void CreateTemplate(string targetFolder)
    {
        Directory.CreateDirectory(targetFolder);
        Directory.CreateDirectory(Path.Combine(targetFolder, InstalledPackage.FilesFolder));
        Directory.CreateDirectory(Path.Combine(targetFolder, InstalledPackage.EffectsFolder));
        Directory.CreateDirectory(Path.Combine(targetFolder, InstalledPackage.LoadingThemesFolder));
        Directory.CreateDirectory(Path.Combine(targetFolder, "plugin"));

        var manifest = new PackageManifest("your-name.my-mod", "My mod", "Your name", "1.0.0",
            "What the mod does, in one or two sentences.",
            new PluginSpec("powershell", ["-NoProfile", "-ExecutionPolicy", "Bypass", "-File", @"plugin\plugin.ps1"]));

        File.WriteAllText(Path.Combine(targetFolder, PackageManifest.FileName), manifest.ToJson());
        File.WriteAllText(Path.Combine(targetFolder, "README.md"), TemplateTexts.Guide);
        File.WriteAllText(Path.Combine(targetFolder, InstalledPackage.EffectsFolder, "scanlines.hlsl"), TemplateTexts.ScanlinesEffect);
        File.WriteAllText(Path.Combine(targetFolder, InstalledPackage.EffectsFolder, "scanlines.json"), TemplateTexts.ScanlinesDescription);
        File.WriteAllText(Path.Combine(targetFolder, "plugin", "plugin.ps1"), TemplateTexts.PowerShellPlugin);
        File.WriteAllText(Path.Combine(targetFolder, "plugin", "plugin.py"), TemplateTexts.PythonPlugin);
        File.WriteAllText(Path.Combine(targetFolder, "plugin", "plugin.cpp"), TemplateTexts.CppPlugin);
    }

    // ---- installing

    private static void ExtractZip(string zipPath, string target)
    {
        try
        {
            using var archive = ZipFile.OpenRead(zipPath);
            var entries = archive.Entries.Where(entry => entry.Name.Length > 0).ToList();

            if (entries.Count > MaxFiles || entries.Sum(entry => entry.Length) > MaxSize)
                throw new InvalidDataException($"The package is too big (at most {MaxFiles} files and {MaxSize / 1024 / 1024} MB).");

            string root = Path.GetFullPath(target) + Path.DirectorySeparatorChar;

            foreach (var entry in entries)
            {
                string destination = Path.GetFullPath(Path.Combine(target, entry.FullName));

                // no "..\..\" or absolute paths out of the package's folder
                if (!destination.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"The package has a file outside its folder: {entry.FullName}");

                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                entry.ExtractToFile(destination, overwrite: true);
            }
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            throw new InvalidDataException($"The package can't be opened: {ex.Message}", ex);
        }
    }

    private static void CopyFolder(string source, string target)
    {
        var files = Directory.GetFiles(source, "*", SearchOption.AllDirectories);

        if (files.Length > MaxFiles || files.Sum(file => new FileInfo(file).Length) > MaxSize)
            throw new InvalidDataException($"The package is too big (at most {MaxFiles} files and {MaxSize / 1024 / 1024} MB).");

        foreach (string file in files)
        {
            string destination = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination);
        }
    }

    /// <summary>vizmod.json at the top, or inside a single folder (zips made by right-clicking a folder).</summary>
    private static string FindRoot(string extracted)
    {
        if (File.Exists(Path.Combine(extracted, PackageManifest.FileName)))
            return extracted;

        var folders = Directory.GetDirectories(extracted);

        if (folders.Length == 1 && File.Exists(Path.Combine(folders[0], PackageManifest.FileName)))
            return folders[0];

        throw new InvalidDataException($"The package has no {PackageManifest.FileName}.");
    }
}
