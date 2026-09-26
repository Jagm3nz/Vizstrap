using System.Globalization;
using System.Text.RegularExpressions;

namespace Vizstrap.Core.Appearance;

/// <summary>
/// XML loading themes that ship with Vizstrap, in Bloxstrap's format and Neon's palette. They follow the
/// accent: every colour in them is turned to the accent's hue like the rest of the palette. In the
/// settings they're "builtin:Neon" and so on; a folder name can't hold a colon, so they never clash with
/// the player's own themes.
/// </summary>
public static partial class BuiltinThemes
{
    public const string Prefix = "builtin:";

    public static IReadOnlyList<string> Keys { get; } = ["Neon", "Minimal", "Split", "Terminal"];

    public static bool IsBuiltin(string? id) => id?.StartsWith(Prefix, StringComparison.Ordinal) == true;

    public static string IdOf(string key) => Prefix + key;

    /// <summary>The key of a built-in theme's id, or null for anything else (a player's theme, an old key).</summary>
    public static string? KeyOf(string? id) =>
        IsBuiltin(id) && Keys.Contains(id![Prefix.Length..], StringComparer.Ordinal) ? id[Prefix.Length..] : null;

    public static string NameOf(string key) => $"Vizstrap {key}";

    /// <summary>The theme's Theme.xml in the accent's colours.</summary>
    public static string Read(string key, AccentColor accent)
    {
        if (!Keys.Contains(key, StringComparer.Ordinal))
            throw new ArgumentException($"No built-in theme {key}.", nameof(key));

        using var stream = typeof(BuiltinThemes).Assembly.GetManifestResourceStream($@"LoadingThemes\{key}.xml")
            ?? throw new InvalidOperationException($"The built-in theme {key} is missing from the build.");
        using var reader = new StreamReader(stream);

        return Tint(reader.ReadToEnd(), accent);
    }

    /// <summary>
    /// Writes the theme in the accent's colours to its own folder under <paramref name="folder"/> and returns
    /// that folder, which loads like any theme folder. Rewritten each time, so a new accent shows up.
    /// </summary>
    public static string WriteTo(string folder, string key, AccentColor accent)
    {
        string directory = Path.Combine(folder, key);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, XmlThemes.ThemeFile), Read(key, accent));
        return directory;
    }

    /// <summary>Every colour attribute ("#RRGGBB" or "#AARRGGBB") in the accent's hue; alpha stays.</summary>
    public static string Tint(string xml, AccentColor accent) => ColorAttribute().Replace(xml, match =>
    {
        string hex = match.Groups[1].Value;
        string alpha = hex.Length == 8 ? hex[..2] : "";
        string rgb = hex[alpha.Length..];

        var (r, g, b) = AccentPalette.Tint(
            byte.Parse(rgb[..2], NumberStyles.HexNumber),
            byte.Parse(rgb[2..4], NumberStyles.HexNumber),
            byte.Parse(rgb[4..], NumberStyles.HexNumber),
            accent);

        return $"\"#{alpha}{r:X2}{g:X2}{b:X2}\"";
    });

    [GeneratedRegex("\"#([0-9A-Fa-f]{8}|[0-9A-Fa-f]{6})\"")]
    private static partial Regex ColorAttribute();
}
