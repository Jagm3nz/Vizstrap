using System.Text.RegularExpressions;
using System.Xml.Linq;
using Vizstrap.Core.Localization;

namespace Vizstrap.Core.Tests;

public partial class LocalizationTests
{
    private static readonly string LocalizationFolder = Path.Combine(FindRepositoryRoot(), "src", "Vizstrap", "Localization");

    private static Dictionary<string, string> ReadResx(string code)
    {
        string file = Path.Combine(LocalizationFolder, code == SupportedLanguages.Fallback ? "Strings.resx" : $"Strings.{code}.resx");

        return XDocument.Load(file).Root!
            .Elements("data")
            .ToDictionary(data => (string)data.Attribute("name")!, data => (string)data.Element("value")!);
    }

    public static TheoryData<string> Translations() =>
        new(SupportedLanguages.All.Select(language => language.Code).Where(code => code != SupportedLanguages.Fallback));

    [Theory]
    [MemberData(nameof(Translations))]
    public void Translation_HasExactlyTheEnglishKeys(string code)
    {
        var english = ReadResx(SupportedLanguages.Fallback);
        var translated = ReadResx(code);

        Assert.Equal(english.Keys.Order(StringComparer.Ordinal), translated.Keys.Order(StringComparer.Ordinal));
    }

    [Theory]
    [MemberData(nameof(Translations))]
    public void Translation_KeepsFormatPlaceholders(string code)
    {
        var english = ReadResx(SupportedLanguages.Fallback);
        var translated = ReadResx(code);

        foreach (var (key, text) in english)
        {
            var expected = Placeholders().Matches(text).Select(match => match.Value).Order(StringComparer.Ordinal);
            var actual = Placeholders().Matches(translated[key]).Select(match => match.Value).Order(StringComparer.Ordinal);

            Assert.True(expected.SequenceEqual(actual), $"{code}/{key} placeholders differ: \"{translated[key]}\"");
        }
    }

    [Fact]
    public void EverySupportedLanguage_HasAFile()
    {
        foreach (var language in SupportedLanguages.All)
            Assert.NotEmpty(ReadResx(language.Code));
    }

    [GeneratedRegex(@"\{\d+\}")]
    private static partial Regex Placeholders();

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Vizstrap.slnx")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not find the repository root (Vizstrap.slnx).");
    }
}
