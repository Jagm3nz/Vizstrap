using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Vizstrap.Core.FastFlags;

public enum FlagType
{
    Bool,
    Int,
    String,
    Log,
}

/// <summary>
/// ClientAppSettings.json in the Modifications folder (ClientSettings\), so the mod mechanism copies it
/// into Roblox like any other mod. Values are stored as strings, bools as "True"/"False", exactly like
/// Bloxstrap, so a Bloxstrap flag file can be dropped in as-is.
/// </summary>
public sealed partial class FastFlagFile
{
    public const string RelativePath = @"ClientSettings\ClientAppSettings.json";

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public FastFlagFile(VizstrapPaths paths)
    {
        FilePath = Path.Combine(paths.Modifications, RelativePath);
    }

    public string FilePath { get; }

    public Dictionary<string, string> Load()
    {
        if (!File.Exists(FilePath))
            return new Dictionary<string, string>(StringComparer.Ordinal);

        try
        {
            return Parse(File.ReadAllText(FilePath));
        }
        catch (Exception ex) when (ex is JsonException or FormatException)
        {
            // a hand-edited file that isn't valid JSON: start empty rather than refuse to open settings
            Logging.Log.Warn(nameof(FastFlagFile), $"Ignoring unreadable {FilePath}: {ex.Message}");
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    /// <summary>Writes the flags; with none left the file is removed so Roblox gets its own defaults back.</summary>
    public void Save(IReadOnlyDictionary<string, string> flags)
    {
        if (flags.Count == 0)
        {
            if (File.Exists(FilePath))
                File.Delete(FilePath);

            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var ordered = flags.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToDictionary(pair => pair.Key, pair => pair.Value);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(ordered, Indented));
    }

    /// <summary>Reads a JSON object of flags (e.g. pasted into the importer); any value type becomes a string.</summary>
    public static Dictionary<string, string> Parse(string json)
    {
        var root = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip })
            as JsonObject ?? throw new FormatException("Fast Flags must be a JSON object: { \"FlagName\": value }.");

        var flags = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (name, value) in root)
        {
            flags[name] = value switch
            {
                null => "",
                JsonValue v when v.TryGetValue(out bool b) => b ? "True" : "False",
                JsonValue v when v.TryGetValue(out string? s) => s!,
                _ => value.ToJsonString(),
            };
        }

        return flags;
    }

    /// <summary>The flag's kind from its prefix (FFlag, DFInt, FString, FLog…), or null for a name Roblox wouldn't accept.</summary>
    public static FlagType? TypeOf(string name)
    {
        var match = FlagName().Match(name);

        if (!match.Success)
            return null;

        return match.Groups["type"].Value switch
        {
            "Flag" => FlagType.Bool,
            "Int" => FlagType.Int,
            "String" => FlagType.String,
            _ => FlagType.Log,
        };
    }

    /// <summary>Why a value doesn't suit the flag (null when it's fine).</summary>
    public static string? ValueProblem(string name, string value) => TypeOf(name) switch
    {
        null => "name",
        FlagType.Bool when !bool.TryParse(value, out _) => "bool",
        FlagType.Int or FlagType.Log when !long.TryParse(value, out _) => "int",
        _ => null,
    };

    /// <summary>Values as Roblox and Bloxstrap write them: "True"/"False" for bools.</summary>
    public static string Normalize(string name, string value) =>
        TypeOf(name) == FlagType.Bool && bool.TryParse(value, out bool parsed) ? (parsed ? "True" : "False") : value.Trim();

    [GeneratedRegex("^(D|S)?F(?<type>Flag|Int|String|Log)[A-Za-z0-9_]+$")]
    private static partial Regex FlagName();
}
