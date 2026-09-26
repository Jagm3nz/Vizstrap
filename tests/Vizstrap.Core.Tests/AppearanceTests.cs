using Vizstrap.Core.Appearance;
using Vizstrap.Core.Storage;
using Vizstrap.Core.Tests.TestSupport;

namespace Vizstrap.Core.Tests;

public class AppearanceTests
{
    [Theory]
    [InlineData(AppTheme.Dark, true, true)]
    [InlineData(AppTheme.Dark, false, true)]
    [InlineData(AppTheme.Light, false, false)]
    [InlineData(AppTheme.System, true, false)]
    [InlineData(AppTheme.System, false, true)]
    public void ResolvesTheme(AppTheme theme, bool systemLight, bool expectedDark) =>
        Assert.Equal(expectedDark, ThemeResolver.IsDark(theme, () => systemLight));

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    public void ReadsWindowsAppMode(int value, bool expectedLight)
    {
        using var registry = new TestRegistry();
        using (var key = registry.Root.CreateSubKey(ThemeResolver.PersonalizeKeyPath))
            key.SetValue("AppsUseLightTheme", value);

        Assert.Equal(expectedLight, ThemeResolver.SystemUsesLightApps(registry.Root));
    }

    [Fact]
    public void MissingAppMode_MeansLight()
    {
        using var registry = new TestRegistry();

        Assert.True(ThemeResolver.SystemUsesLightApps(registry.Root));
    }

    [Fact]
    public void OnlyWindowsImitations_IgnoreTheme()
    {
        var ignoring = Enum.GetValues<LoadingStyle>().Where(LoadingStyles.IgnoresTheme);

        Assert.Equal([LoadingStyle.Legacy2011, LoadingStyle.Legacy2008, LoadingStyle.Vista], ignoring);
    }

    [Fact]
    public void Settings_DefaultToNeon()
    {
        var settings = new Settings();

        Assert.Equal((AppTheme.Dark, LoadingStyle.Neon, LoadingIcon.Vizstrap, "Vizstrap"),
            (settings.Theme, settings.LoadingStyle, settings.LoadingIcon, settings.LoadingTitle));
    }

    [Fact]
    public void Settings_RoundTripAppearance_AsReadableNames()
    {
        using var temp = new TempDirectory();
        var store = new JsonStore<Settings>(temp.Combine("Settings.json"));
        store.Value.Theme = AppTheme.System;
        store.Value.LoadingStyle = LoadingStyle.Byfron;
        store.Value.LoadingIcon = LoadingIcon.Custom;
        store.Value.LoadingTitle = "Mój launcher";
        store.Value.CustomIconPath = @"C:\icons\mine.ico";
        store.Save();

        Assert.Contains("\"Byfron\"", File.ReadAllText(store.FilePath));

        var reloaded = new JsonStore<Settings>(store.FilePath);
        reloaded.Load();
        Assert.Equal((AppTheme.System, LoadingStyle.Byfron, LoadingIcon.Custom, "Mój launcher", @"C:\icons\mine.ico"),
            (reloaded.Value.Theme, reloaded.Value.LoadingStyle, reloaded.Value.LoadingIcon, reloaded.Value.LoadingTitle, reloaded.Value.CustomIconPath));
    }

    [Fact]
    public void Settings_FromStage1_KeepLanguage_AndGetAppearanceDefaults()
    {
        using var temp = new TempDirectory();
        string file = temp.Combine("Settings.json");
        File.WriteAllText(file, "{ \"Language\": \"de\" }");

        var store = new JsonStore<Settings>(file);
        store.Load();

        Assert.Equal(("de", LoadingStyle.Neon, AppTheme.Dark), (store.Value.Language, store.Value.LoadingStyle, store.Value.Theme));
    }
}

public class AccentPaletteTests
{
    [Fact]
    public void Violet_leaves_the_Neon_palette_exactly_as_designed()
    {
        Assert.Equal(((byte)0x7F, (byte)0x77, (byte)0xDD), AccentPalette.Tint(0x7F, 0x77, 0xDD, AccentColor.Violet));
        Assert.Equal(((byte)0x15, (byte)0x12, (byte)0x2A), AccentPalette.Tint(0x15, 0x12, 0x2A, AccentColor.Violet));
    }

    [Theory]
    [InlineData(AccentColor.Blue)]
    [InlineData(AccentColor.Teal)]
    [InlineData(AccentColor.Green)]
    [InlineData(AccentColor.Pink)]
    [InlineData(AccentColor.Red)]
    [InlineData(AccentColor.Orange)]
    public void Other_accents_turn_the_hue_and_keep_saturation_and_lightness(AccentColor accent)
    {
        var violet = AccentPalette.ToHsl(0x7F, 0x77, 0xDD);
        var (r, g, b) = AccentPalette.Tint(0x7F, 0x77, 0xDD, accent);
        var tinted = AccentPalette.ToHsl(r, g, b);

        double hueDistance = Math.Abs((tinted.Hue - AccentPalette.HueOf(accent) + 540) % 360 - 180);
        Assert.True(hueDistance < 2, $"hue {tinted.Hue}");
        Assert.Equal(violet.Saturation, tinted.Saturation, 0.02);
        Assert.Equal(violet.Lightness, tinted.Lightness, 0.01);
    }

    [Fact]
    public void Greys_and_white_stay_as_they_are()
    {
        Assert.Equal(((byte)255, (byte)255, (byte)255), AccentPalette.Tint(255, 255, 255, AccentColor.Green));
        Assert.Equal(((byte)40, (byte)40, (byte)40), AccentPalette.Tint(40, 40, 40, AccentColor.Red));
    }

    [Theory]
    [InlineData(0x7F, 0x77, 0xDD)]
    [InlineData(0x15, 0x12, 0x2A)]
    [InlineData(0xF4, 0xF2, 0xFC)]
    [InlineData(0xE5, 0x48, 0x4D)]
    public void Hsl_round_trips(byte r, byte g, byte b)
    {
        var (hue, saturation, lightness) = AccentPalette.ToHsl(r, g, b);

        Assert.Equal((r, g, b), AccentPalette.FromHsl(hue, saturation, lightness));
    }
}

public class XmlThemesTests
{
    [Fact]
    public void New_themes_start_from_the_template_with_free_names()
    {
        using var directory = new TempDirectory();
        var themes = new XmlThemes(directory.Combine("CustomThemes"));

        Assert.Empty(themes.List());

        Assert.Equal("Mój motyw", themes.Create("Mój motyw"));
        Assert.Equal("Mój motyw 2", themes.Create("Mój motyw"));
        Assert.Equal("Theme", themes.Create("<>:"));

        Assert.Equal(["Mój motyw", "Mój motyw 2", "Theme"], themes.List());
        Assert.Contains("BloxstrapCustomBootstrapper", File.ReadAllText(themes.FileOf("Mój motyw")));

        themes.Delete("Mój motyw 2");
        Assert.Equal(["Mój motyw", "Theme"], themes.List());
    }

    [Fact]
    public void A_zip_is_imported_from_its_Theme_xml_down()
    {
        using var directory = new TempDirectory();
        string zip = directory.Combine("Neon Night.zip");

        using (var archive = System.IO.Compression.ZipFile.Open(zip, System.IO.Compression.ZipArchiveMode.Create))
        {
            void Add(string name, string content)
            {
                using var writer = new StreamWriter(archive.CreateEntry(name).Open());
                writer.Write(content);
            }

            Add("Neon Night/Theme.xml", "<BloxstrapCustomBootstrapper Version=\"1\" />");
            Add("Neon Night/images/bg.png", "png");
            Add("readme.txt", "not part of the theme");
        }

        var themes = new XmlThemes(directory.Combine("CustomThemes"));
        string name = themes.ImportZip(zip);

        Assert.Equal("Neon Night", name);
        Assert.True(File.Exists(themes.FileOf(name)));
        Assert.Equal("png", File.ReadAllText(Path.Combine(themes.DirectoryOf(name), "images", "bg.png")));
        Assert.False(File.Exists(Path.Combine(themes.DirectoryOf(name), "readme.txt")));
    }

    [Fact]
    public void Zips_without_a_theme_or_with_escaping_paths_are_refused()
    {
        using var directory = new TempDirectory();
        var themes = new XmlThemes(directory.Combine("CustomThemes"));

        string empty = directory.Combine("empty.zip");
        using (System.IO.Compression.ZipFile.Open(empty, System.IO.Compression.ZipArchiveMode.Create)) { }
        Assert.Throws<InvalidDataException>(() => themes.ImportZip(empty));

        string evil = directory.Combine("evil.zip");
        using (var archive = System.IO.Compression.ZipFile.Open(evil, System.IO.Compression.ZipArchiveMode.Create))
        {
            using (var writer = new StreamWriter(archive.CreateEntry("Theme.xml").Open())) writer.Write("<x/>");
            using (var writer = new StreamWriter(archive.CreateEntry("../../outside.txt").Open())) writer.Write("!");
        }

        Assert.Throws<InvalidDataException>(() => themes.ImportZip(evil));
        Assert.False(File.Exists(directory.Combine("outside.txt")));
        Assert.Empty(themes.List());
    }

    [Fact]
    public void Bloxstrap_themes_are_copied_over()
    {
        using var directory = new TempDirectory();
        var bloxstrap = new XmlThemes(directory.Combine("Bloxstrap"));
        bloxstrap.Create("Blue");
        File.WriteAllText(Path.Combine(bloxstrap.DirectoryOf("Blue"), "bg.png"), "png");
        var themes = new XmlThemes(directory.Combine("Vizstrap"));
        themes.Create("Blue");

        var imported = themes.ImportFolder(bloxstrap.Folder);

        Assert.Equal(["Blue 2"], imported);
        Assert.True(File.Exists(Path.Combine(themes.DirectoryOf("Blue 2"), "bg.png")));
    }
}

public class CustomLoadingThemeTests
{
    [Theory]
    [InlineData("#15122A", true)]
    [InlineData("#8015122A", true)]
    [InlineData("15122A", false)]
    [InlineData("#12345", false)]
    [InlineData("#GGGGGG", false)]
    [InlineData(null, false)]
    public void Colours_are_hex(string? text, bool expected) => Assert.Equal(expected, CustomLoadingTheme.IsColor(text));

    [Fact]
    public void Clones_compare_by_value()
    {
        var theme = new CustomLoadingTheme { Layout = ThemeLayout.Side };
        var clone = theme.Clone();

        Assert.True(clone.SameAs(theme));
        clone.Dim = 10;
        Assert.False(clone.SameAs(theme));
    }
}

public class BuiltinThemesTests
{
    public static TheoryData<string> Keys => [.. BuiltinThemes.Keys];

    [Theory]
    [MemberData(nameof(Keys))]
    public void Each_theme_is_a_Bloxstrap_theme_wired_to_the_launch(string key)
    {
        var xml = System.Xml.Linq.XElement.Parse(BuiltinThemes.Read(key, AccentColor.Violet));
        var names = xml.Descendants().Select(element => (string?)element.Attribute("Name")).OfType<string>().ToList();

        Assert.Equal("BloxstrapCustomBootstrapper", xml.Name.LocalName);
        Assert.Equal("1", (string?)xml.Attribute("Version"));
        Assert.True(xml.Descendants().Count() <= 100);
        Assert.Contains("StatusText", names);
        Assert.Contains("PrimaryProgressBar", names);
        Assert.Contains("CancelButton", names);
    }

    [Fact]
    public void Colours_follow_the_accent_like_the_palette()
    {
        const string xml = """<X A="#7F77DD" B="#FFFFFF" C="#807F77DD" D="#7F77DD80x" />""";
        var (r, g, b) = AccentPalette.Tint(0x7F, 0x77, 0xDD, AccentColor.Green);

        string green = BuiltinThemes.Tint(xml, AccentColor.Green);

        Assert.Equal(xml, BuiltinThemes.Tint(xml, AccentColor.Violet));
        Assert.Contains($"A=\"#{r:X2}{g:X2}{b:X2}\"", green);
        Assert.Contains("B=\"#FFFFFF\"", green);
        Assert.Contains($"C=\"#80{r:X2}{g:X2}{b:X2}\"", green);
        Assert.Contains("D=\"#7F77DD80x\"", green);
        Assert.DoesNotContain("#7F77DD\"", BuiltinThemes.Read("Neon", AccentColor.Green));
    }

    [Fact]
    public void Ids_never_clash_with_theme_folders()
    {
        Assert.Equal("builtin:Neon", BuiltinThemes.IdOf("Neon"));
        Assert.Equal("Neon", BuiltinThemes.KeyOf("builtin:Neon"));
        Assert.Null(BuiltinThemes.KeyOf("builtin:Gone"));
        Assert.Null(BuiltinThemes.KeyOf("Neon"));
        Assert.Null(BuiltinThemes.KeyOf(null));
        Assert.Contains(':', Path.GetInvalidFileNameChars());
    }

    [Fact]
    public void A_theme_is_written_out_in_the_accent_and_can_become_the_players_own()
    {
        using var directory = new TempDirectory();

        string folder = BuiltinThemes.WriteTo(directory.Combine("BuiltinThemes"), "Split", AccentColor.Blue);
        Assert.Equal(BuiltinThemes.Read("Split", AccentColor.Blue), File.ReadAllText(Path.Combine(folder, XmlThemes.ThemeFile)));

        var themes = new XmlThemes(directory.Combine("CustomThemes"));
        string copy = themes.Create(BuiltinThemes.NameOf("Split"), BuiltinThemes.Read("Split", AccentColor.Blue));

        Assert.Equal("Vizstrap Split", copy);
        Assert.Equal(BuiltinThemes.Read("Split", AccentColor.Blue), File.ReadAllText(themes.FileOf(copy)));
    }
}
