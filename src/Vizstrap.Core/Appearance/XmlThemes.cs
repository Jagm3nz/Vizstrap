using System.IO.Compression;
using Vizstrap.Core.Logging;

namespace Vizstrap.Core.Appearance;

/// <summary>
/// Loading window themes in Bloxstrap's XML format ("custom bootstrapper themes"): one folder per theme
/// with a Theme.xml and the pictures or fonts it uses. Bloxstrap's own themes work as they are.
/// </summary>
public sealed class XmlThemes(string folder)
{
    private const string LogSource = nameof(XmlThemes);

    public const string ThemeFile = "Theme.xml";

    /// <summary>Bloxstrap's examples, a good start for writing one.</summary>
    public const string ExamplesUrl = "https://github.com/bloxstraplabs/custom-bootstrapper-examples";

    /// <summary>Bloxstrap's starter theme: icon, status, bar and cancel button (its "Simple" template).</summary>
    public const string SimpleTemplate = """
        <BloxstrapCustomBootstrapper Version="1" Height="320" Width="520" IgnoreTitleBarInset="True" Theme="Default" Margin="30">
            <!-- Bloxstrap's theme format; more examples: https://github.com/bloxstraplabs/custom-bootstrapper-examples -->
            <TitleBar Title="" ShowMinimize="False" ShowClose="False" />

            <Image Source="{Icon}" Height="100" Width="100" HorizontalAlignment="Center" Margin="0,15,0,0" />
            <TextBlock HorizontalAlignment="Center" Name="StatusText" FontSize="20" Margin="0,170,0,0" />
            <ProgressBar Width="450" Height="12" Name="PrimaryProgressBar" HorizontalAlignment="Center" Margin="0,200,0,0" />
            <Button Content="{Common.Cancel}" Name="CancelButton" HorizontalAlignment="Center" Margin="0,225,0,0" Height="30" Width="100" />
        </BloxstrapCustomBootstrapper>
        """;

    public string Folder { get; } = folder;

    /// <summary>Theme names (folder names) that have a Theme.xml, alphabetically.</summary>
    public IReadOnlyList<string> List() => !Directory.Exists(Folder)
        ? []
        : Directory.GetDirectories(Folder)
            .Where(directory => File.Exists(Path.Combine(directory, ThemeFile)))
            .Select(directory => Path.GetFileName(directory))
            .Order(StringComparer.CurrentCultureIgnoreCase)
            .ToList()!;

    public string DirectoryOf(string name) => Path.Combine(Folder, name);

    public string FileOf(string name) => Path.Combine(Folder, name, ThemeFile);

    /// <summary>A new theme from the starter template, or from <paramref name="content"/>; returns its name ("Theme", "Theme 2"…).</summary>
    public string Create(string baseName, string content = SimpleTemplate)
    {
        string name = FreeName(baseName);
        Directory.CreateDirectory(DirectoryOf(name));
        File.WriteAllText(FileOf(name), content);
        return name;
    }

    /// <summary>
    /// Imports a theme from a zip, as Bloxstrap exports them: Theme.xml at the root or inside a single
    /// folder. Returns the new theme's name.
    /// </summary>
    public string ImportZip(string zipPath)
    {
        using var archive = ZipFile.OpenRead(zipPath);

        var themeEntry = archive.Entries
            .Where(entry => string.Equals(entry.Name, ThemeFile, StringComparison.OrdinalIgnoreCase))
            .OrderBy(entry => entry.FullName.Count(c => c is '/' or '\\'))
            .FirstOrDefault() ?? throw new InvalidDataException($"The zip has no {ThemeFile}.");

        // everything next to Theme.xml belongs to the theme
        string root = themeEntry.FullName[..^themeEntry.Name.Length];
        string name = FreeName(Path.GetFileNameWithoutExtension(zipPath));
        string target = Path.GetFullPath(DirectoryOf(name));
        Directory.CreateDirectory(target);

        try
        {
            foreach (var entry in archive.Entries.Where(entry => entry.FullName.StartsWith(root, StringComparison.Ordinal) && entry.Name.Length > 0))
            {
                string destination = Path.GetFullPath(Path.Combine(target, entry.FullName[root.Length..]));

                // a zip entry must not climb out of the theme's folder
                if (!destination.StartsWith(target + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Unsafe path in the zip: {entry.FullName}");

                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                entry.ExtractToFile(destination, overwrite: true);
            }
        }
        catch
        {
            Directory.Delete(target, recursive: true);
            throw;
        }

        Log.Info(LogSource, $"Imported theme '{name}' from {zipPath}");
        return name;
    }

    /// <summary>Copies every theme from another folder (Bloxstrap's CustomThemes); returns the names added.</summary>
    public IReadOnlyList<string> ImportFolder(string otherFolder)
    {
        var imported = new List<string>();

        foreach (string source in new XmlThemes(otherFolder).List().Select(name => Path.Combine(otherFolder, name)))
        {
            string name = FreeName(Path.GetFileName(source));
            CopyDirectory(source, DirectoryOf(name));
            imported.Add(name);
        }

        Log.Info(LogSource, $"Imported {imported.Count} themes from {otherFolder}");
        return imported;
    }

    /// <summary>A copy of one theme folder (a mod package's) among the player's own; returns its name.</summary>
    public string ImportTheme(string themeDirectory)
    {
        string name = FreeName(Path.GetFileName(themeDirectory.TrimEnd(Path.DirectorySeparatorChar)));
        CopyDirectory(themeDirectory, DirectoryOf(name));
        return name;
    }

    public void Delete(string name)
    {
        string directory = DirectoryOf(name);

        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }

    /// <summary>The name itself if no theme has it yet, else "name 2", "name 3"…; unsafe characters removed.</summary>
    internal string FreeName(string baseName)
    {
        string clean = string.Concat(baseName.Where(c => !Path.GetInvalidFileNameChars().Contains(c))).Trim();

        if (clean.Length == 0)
            clean = "Theme";

        string name = clean;

        for (int number = 2; Directory.Exists(DirectoryOf(name)); number++)
            name = $"{clean} {number}";

        return name;
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);

        foreach (string file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: true);

        foreach (string directory in Directory.GetDirectories(source))
            CopyDirectory(directory, Path.Combine(target, Path.GetFileName(directory)));
    }
}
