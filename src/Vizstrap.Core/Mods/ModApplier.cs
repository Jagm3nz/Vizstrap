using System.Text.Json;
using System.Text.Json.Nodes;
using Vizstrap.Core.Install;
using Vizstrap.Core.Logging;
using Vizstrap.Core.Roblox;
using Vizstrap.Core.Storage;

namespace Vizstrap.Core.Mods;

/// <summary>What happened while applying mods; <see cref="Failed"/> lists files that couldn't be written.</summary>
public sealed record ModResult(int Applied, int Restored, IReadOnlyList<string> Failed)
{
    public bool Success => Failed.Count == 0;
}

/// <summary>
/// Mirrors the Modifications folder onto the installed Roblox version, like Bloxstrap: every file there
/// replaces the Roblox file at the same relative path, and when a mod file disappears the original is
/// restored from the cached package it came from. Mod packages' files come underneath: the player's own
/// Modifications win over them.
/// </summary>
public sealed class ModApplier
{
    public const string CustomFontAsset = "rbxasset://fonts/CustomFont.ttf";

    private const string LogSource = nameof(ModApplier);

    private static readonly JsonSerializerOptions IndentedJson = new() { WriteIndented = true };

    private readonly VizstrapPaths _paths;
    private readonly JsonStore<State> _state;

    public ModApplier(VizstrapPaths paths, JsonStore<State> state)
    {
        _paths = paths;
        _state = state;
    }

    /// <param name="includeFastFlags">
    /// False leaves the Fast Flag file out, which also removes flags applied earlier from Roblox.
    /// </param>
    /// <param name="packageFiles">Switched-on mod packages' "files" folders; a later one wins over an earlier one.</param>
    public ModResult Apply(string versionDirectory, bool includeFastFlags = true, IReadOnlyList<string>? packageFiles = null)
    {
        // package hashes and the previous manifest must be current (the updater may just have saved)
        _state.Load();
        Directory.CreateDirectory(_paths.Modifications);

        PrepareCustomFont(versionDirectory);

        // relative path → the file that goes there: packages first, then the player's own mods over them
        var sources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (string layer in (packageFiles ?? []).Where(Directory.Exists).Append(_paths.Modifications))
        {
            foreach (string file in Directory.GetFiles(layer, "*", SearchOption.AllDirectories))
                sources[Path.GetRelativePath(layer, file)] = file;
        }

        var modFiles = sources.Keys
            .Where(relative => !relative.EndsWith(".lock", StringComparison.OrdinalIgnoreCase))
            .Where(relative => includeFastFlags || !string.Equals(relative, FastFlags.FastFlagFile.RelativePath, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var failed = new List<string>();
        int applied = 0;

        foreach (string relative in modFiles)
        {
            string source = sources[relative];
            string target = Path.Combine(versionDirectory, relative);

            try
            {
                if (File.Exists(target) && SameContent(source, target))
                    continue;

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                ClearReadOnly(target);
                File.Copy(source, target, overwrite: true);
                applied++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // usually Roblox running from this folder and holding the file
                Log.Warn(LogSource, $"Could not apply {relative}: {ex.Message}");
                failed.Add(relative);
            }
        }

        var current = modFiles.ToHashSet(StringComparer.OrdinalIgnoreCase);
        int restored = 0;

        foreach (string relative in _state.Value.ModManifest.Where(previous => !current.Contains(previous)).ToList())
        {
            try
            {
                if (RestoreOriginal(versionDirectory, relative))
                    restored++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                Log.Warn(LogSource, $"Could not restore {relative}: {ex.Message}");
                failed.Add(relative);
                current.Add(relative); // keep tracking it so the next launch tries again
            }
        }

        _state.Value.ModManifest = current.Order(StringComparer.OrdinalIgnoreCase).ToList();
        _state.Save();

        Log.Info(LogSource, $"Mods: {modFiles.Count} active, {applied} applied, {restored} restored, {failed.Count} failed");
        return new ModResult(applied, restored, failed);
    }

    /// <summary>
    /// Puts the original file back from the package it belongs to, or deletes it when no package has it
    /// (the mod added a new file).
    /// </summary>
    private bool RestoreOriginal(string versionDirectory, string relative)
    {
        string target = Path.Combine(versionDirectory, relative);
        ClearReadOnly(target);

        foreach (var (package, directory) in CandidatePackages(relative))
        {
            if (!_state.Value.PackageHashes.TryGetValue(package, out string? signature))
                continue;

            string zip = Path.Combine(_paths.Downloads, signature);

            if (!File.Exists(zip))
            {
                Log.Warn(LogSource, $"{package} is no longer cached, {relative} stays modded until Roblox is reinstalled");
                return false;
            }

            if (PackageExtractor.ExtractEntry(zip, relative[directory.Length..], target))
            {
                Log.Info(LogSource, $"Restored {relative} from {package}");
                return true;
            }
        }

        if (File.Exists(target))
            File.Delete(target);

        Log.Info(LogSource, $"Removed {relative} (not part of Roblox)");
        return true;
    }

    /// <summary>Packages whose folder contains the file, most specific first; root packages are searched last.</summary>
    internal static IEnumerable<(string Package, string Directory)> CandidatePackages(string relative) =>
        PackageMap.Player
            .Where(pair => relative.StartsWith(pair.Value, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(pair => pair.Value.Length)
            .Select(pair => (pair.Key, pair.Value));

    /// <summary>
    /// With a CustomFont.ttf among the mods, every font family description in the Roblox folder gets a
    /// copy in the mods folder that points all its faces at the custom font. Without one, those generated
    /// descriptions are removed again. (Same approach as Bloxstrap.)
    /// </summary>
    private void PrepareCustomFont(string versionDirectory)
    {
        string modFamilies = Path.Combine(_paths.Modifications, "content", "fonts", "families");

        if (!File.Exists(_paths.CustomFont))
        {
            if (Directory.Exists(modFamilies))
                Directory.Delete(modFamilies, recursive: true);

            return;
        }

        string robloxFamilies = Path.Combine(versionDirectory, "content", "fonts", "families");

        if (!Directory.Exists(robloxFamilies))
            return;

        Directory.CreateDirectory(modFamilies);

        foreach (string familyFile in Directory.GetFiles(robloxFamilies, "*.json"))
        {
            string modFamily = Path.Combine(modFamilies, Path.GetFileName(familyFile));

            if (File.Exists(modFamily))
                continue;

            try
            {
                var family = JsonNode.Parse(File.ReadAllText(familyFile))!;

                foreach (var face in family["faces"]?.AsArray() ?? [])
                    face!["assetId"] = CustomFontAsset;

                File.WriteAllText(modFamily, family.ToJsonString(IndentedJson));
            }
            catch (JsonException ex)
            {
                Log.Warn(LogSource, $"Skipped font family {Path.GetFileName(familyFile)}: {ex.Message}");
            }
        }
    }

    private static bool SameContent(string first, string second)
    {
        if (new FileInfo(first).Length != new FileInfo(second).Length)
            return false;

        using var a = File.OpenRead(first);
        using var b = File.OpenRead(second);
        return System.Security.Cryptography.MD5.HashData(a).AsSpan().SequenceEqual(System.Security.Cryptography.MD5.HashData(b));
    }

    internal static void ClearReadOnly(string path)
    {
        if (File.Exists(path) && File.GetAttributes(path).HasFlag(FileAttributes.ReadOnly))
            File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly);
    }
}
