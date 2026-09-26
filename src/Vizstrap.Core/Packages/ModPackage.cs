using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Vizstrap.Core.Packages;

/// <summary>A program a package runs next to Roblox (any language), talking JSON lines on stdin/stdout.</summary>
/// <param name="Run">The program: a file in the package (plugin\my.exe) or one on the PATH (python, powershell).</param>
public sealed record PluginSpec(string Run, IReadOnlyList<string>? Args = null);

/// <summary>A package's vizmod.json.</summary>
public sealed partial record PackageManifest(
    string Id,
    string Name,
    string? Author = null,
    string? Version = null,
    string? Description = null,
    PluginSpec? Plugin = null)
{
    public const string FileName = "vizmod.json";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Reads and checks a manifest; throws <see cref="InvalidDataException"/> saying what's wrong.</summary>
    public static PackageManifest Parse(string json)
    {
        PackageManifest? manifest;

        try
        {
            manifest = JsonSerializer.Deserialize<PackageManifest>(json, Json);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"{FileName} isn't valid JSON: {ex.Message}", ex);
        }

        if (manifest is null)
            throw new InvalidDataException($"{FileName} is empty.");

        if (!IsValidId(manifest.Id))
            throw new InvalidDataException($"\"id\" must be 2–64 lowercase letters, digits, dots, dashes or underscores (like \"name.my-mod\"), not \"{manifest.Id}\".");

        if (string.IsNullOrWhiteSpace(manifest.Name))
            throw new InvalidDataException("\"name\" is missing.");

        if (manifest.Plugin is { } plugin && string.IsNullOrWhiteSpace(plugin.Run))
            throw new InvalidDataException("\"plugin\" needs \"run\": the program to start.");

        return manifest;
    }

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    public static bool IsValidId(string? id) => id is not null && IdPattern().IsMatch(id);

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{1,63}$")]
    private static partial Regex IdPattern();
}

/// <summary>A custom picture effect from a package: effects\name.hlsl with its sliders in effects\name.json.</summary>
/// <param name="Id">"packageId/name", what the settings keep.</param>
public sealed record CustomEffect(string Id, string Name, string SourcePath, IReadOnlyList<EffectParameter> Parameters)
{
    /// <summary>At most this many sliders per effect (Param0 … Param7 in the shader).</summary>
    public const int MaxParameters = 8;
}

public sealed record EffectParameter(string Name, float Min = 0, float Max = 1, float Default = 0.5f);

/// <summary>An installed package: its manifest and what its folders hold.</summary>
public sealed record InstalledPackage(PackageManifest Manifest, string Directory)
{
    public const string FilesFolder = "files";
    public const string EffectsFolder = "effects";
    public const string LoadingThemesFolder = "loading-themes";

    public string Id => Manifest.Id;

    /// <summary>Roblox files (the same layout as the Modifications folder), or null when there are none.</summary>
    public string? FilesDirectory =>
        System.IO.Directory.Exists(Path.Combine(Directory, FilesFolder)) &&
        System.IO.Directory.EnumerateFiles(Path.Combine(Directory, FilesFolder), "*", SearchOption.AllDirectories).Any()
            ? Path.Combine(Directory, FilesFolder)
            : null;

    public bool HasPlugin => Manifest.Plugin is not null;

    /// <summary>The loading themes' folders (each with a Theme.xml).</summary>
    public IReadOnlyList<string> LoadingThemes => System.IO.Directory.Exists(Path.Combine(Directory, LoadingThemesFolder))
        ? [.. System.IO.Directory.GetDirectories(Path.Combine(Directory, LoadingThemesFolder))
            .Where(folder => File.Exists(Path.Combine(folder, Appearance.XmlThemes.ThemeFile)))
            .Order(StringComparer.OrdinalIgnoreCase)]
        : [];

    /// <summary>The picture effects; an effect without its .json gets no sliders.</summary>
    public IReadOnlyList<CustomEffect> Effects
    {
        get
        {
            string folder = Path.Combine(Directory, EffectsFolder);

            if (!System.IO.Directory.Exists(folder))
                return [];

            var effects = new List<CustomEffect>();

            foreach (string source in System.IO.Directory.GetFiles(folder, "*.hlsl").Order(StringComparer.OrdinalIgnoreCase))
            {
                string name = Path.GetFileNameWithoutExtension(source);
                var description = EffectDescription.Read(Path.ChangeExtension(source, ".json"));
                effects.Add(new CustomEffect($"{Id}/{name}", description?.Name ?? name, source,
                    [.. (description?.Parameters ?? []).Take(CustomEffect.MaxParameters)]));
            }

            return effects;
        }
    }

    /// <summary>What's inside, for the list in the settings.</summary>
    public IReadOnlyList<string> Contents()
    {
        var contents = new List<string>();

        if (FilesDirectory is not null)
            contents.Add(FilesFolder);
        if (Effects.Count > 0)
            contents.Add(EffectsFolder);
        if (LoadingThemes.Count > 0)
            contents.Add(LoadingThemesFolder);
        if (HasPlugin)
            contents.Add("plugin");

        return contents;
    }

    /// <summary>effects\name.json: the effect's name and its sliders.</summary>
    private sealed record EffectDescription(string? Name, IReadOnlyList<EffectParameter>? Parameters)
    {
        public static EffectDescription? Read(string path)
        {
            try
            {
                return File.Exists(path)
                    ? JsonSerializer.Deserialize<EffectDescription>(File.ReadAllText(path), new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                        ReadCommentHandling = JsonCommentHandling.Skip,
                        AllowTrailingCommas = true,
                    })
                    : null;
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }
}
