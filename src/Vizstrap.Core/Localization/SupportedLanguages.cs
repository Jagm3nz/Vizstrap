using System.Globalization;

namespace Vizstrap.Core.Localization;

/// <param name="Code">Culture name used for the .resx file, e.g. "pt-BR".</param>
/// <param name="NativeName">Name shown in the language picker, in that language.</param>
public sealed record Language(string Code, string NativeName);

public static class SupportedLanguages
{
    public const string Fallback = "en";

    public static IReadOnlyList<Language> All { get; } =
    [
        new("en", "English"),
        new("pl", "Polski"),
        new("de", "Deutsch"),
        new("fr", "Français"),
        new("es", "Español"),
        new("pt-BR", "Português (Brasil)"),
        new("ru", "Русский"),
        new("zh-Hans", "简体中文"),
    ];

    /// <summary>
    /// Picks the UI language: the saved choice when it is supported, otherwise the closest match to the
    /// system culture (de-AT → de, pt-PT → pt-BR, zh-TW → zh-Hans), otherwise English.
    /// </summary>
    public static string Resolve(string? preferred, CultureInfo systemCulture)
    {
        var chosen = Find(preferred);

        if (chosen is not null)
            return chosen.Code;

        for (var culture = systemCulture; !string.IsNullOrEmpty(culture.Name); culture = culture.Parent)
        {
            var exact = Find(culture.Name);

            if (exact is not null)
                return exact.Code;
        }

        string neutral = systemCulture.TwoLetterISOLanguageName;

        return neutral switch
        {
            "zh" => "zh-Hans",
            "pt" => "pt-BR",
            _ => Find(neutral)?.Code ?? Fallback,
        };
    }

    private static Language? Find(string? code) =>
        string.IsNullOrWhiteSpace(code)
            ? null
            : All.FirstOrDefault(language => string.Equals(language.Code, code, StringComparison.OrdinalIgnoreCase));
}
