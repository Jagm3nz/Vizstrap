using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Vizstrap.Core.Install;
using Vizstrap.Core.Mods;
using Vizstrap.Core.Platform;
using Vizstrap.Core.Storage;
using Vizstrap.Core.Tests.TestSupport;

namespace Vizstrap.Core.Tests;

public sealed class ModApplierTests : IDisposable
{
    private const string ArialFamily = "{\"name\":\"Arial\",\"faces\":[{\"name\":\"Regular\",\"weight\":400,\"style\":\"normal\",\"assetId\":\"rbxasset://fonts/arial.ttf\"}]}";

    private readonly TempDirectory _temp = new();
    private readonly VizstrapPaths _paths;
    private readonly JsonStore<State> _state;
    private readonly string _version;

    public ModApplierTests()
    {
        _paths = new VizstrapPaths(_temp.Combine("Vizstrap"));
        _state = new JsonStore<State>(_paths.StateFile);
        _version = _paths.VersionDirectory("version-1");

        // an installed version whose packages are in the download cache, as after a real install
        CachePackage("content-sounds.zip", ("action_jump.mp3", "original jump"));
        CachePackage("content-fonts.zip", ("families/Arial.json", ArialFamily));
        CachePackage("RobloxApp.zip", ("RobloxPlayerBeta.exe", "exe"), ("root.txt", "original root"));
        _state.Save();

        WriteFile(Path.Combine(_version, "content", "sounds", "action_jump.mp3"), "original jump");
        WriteFile(Path.Combine(_version, "content", "fonts", "families", "Arial.json"), ArialFamily);
        WriteFile(Path.Combine(_version, "root.txt"), "original root");
    }

    public void Dispose() => _temp.Dispose();

    private ModApplier Applier => new(_paths, _state);

    private void CachePackage(string name, params (string, string)[] entries)
    {
        byte[] zip = TestZip.Create(entries);
        string md5 = TestZip.Md5(zip);
        Directory.CreateDirectory(_paths.Downloads);
        File.WriteAllBytes(Path.Combine(_paths.Downloads, md5), zip);
        _state.Value.PackageHashes[name] = md5;
    }

    private static void WriteFile(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private string Mod(params string[] parts) => Path.Combine([_paths.Modifications, .. parts]);

    private string Installed(params string[] parts) => Path.Combine([_version, .. parts]);

    [Fact]
    public void Apply_CopiesMods_AndRecordsThem()
    {
        WriteFile(Mod("content", "sounds", "action_jump.mp3"), "modded jump");
        WriteFile(Mod("content", "new", "extra.txt"), "brand new");

        var result = Applier.Apply(_version);

        Assert.Equal(2, result.Applied);
        Assert.Equal("modded jump", File.ReadAllText(Installed("content", "sounds", "action_jump.mp3")));
        Assert.Equal("brand new", File.ReadAllText(Installed("content", "new", "extra.txt")));
        Assert.Equal([@"content\new\extra.txt", @"content\sounds\action_jump.mp3"], _state.Value.ModManifest);
    }

    [Fact]
    public void Package_files_go_under_the_players_own_mods_and_come_off_with_the_package()
    {
        string package = _temp.Combine("package-files");
        WriteFile(Path.Combine(package, "content", "sounds", "action_jump.mp3"), "package jump");
        WriteFile(Path.Combine(package, "content", "new", "shared.txt"), "from the package");
        WriteFile(Mod("content", "new", "shared.txt"), "the player's own");

        Applier.Apply(_version, packageFiles: [package]);

        Assert.Equal("package jump", File.ReadAllText(Installed("content", "sounds", "action_jump.mp3")));
        Assert.Equal("the player's own", File.ReadAllText(Installed("content", "new", "shared.txt")));

        // the package switched off
        Applier.Apply(_version);

        Assert.Equal("original jump", File.ReadAllText(Installed("content", "sounds", "action_jump.mp3")));
        Assert.Equal("the player's own", File.ReadAllText(Installed("content", "new", "shared.txt")));
    }

    [Fact]
    public void Apply_SkipsFilesAlreadyInPlace()
    {
        WriteFile(Mod("content", "sounds", "action_jump.mp3"), "modded jump");
        Applier.Apply(_version);

        var second = Applier.Apply(_version);

        Assert.Equal(0, second.Applied);
    }

    [Fact]
    public void RemovedMod_IsRestoredFromItsPackage()
    {
        WriteFile(Mod("content", "sounds", "action_jump.mp3"), "modded jump");
        Applier.Apply(_version);
        File.Delete(Mod("content", "sounds", "action_jump.mp3"));

        var result = Applier.Apply(_version);

        Assert.Equal(1, result.Restored);
        Assert.Equal("original jump", File.ReadAllText(Installed("content", "sounds", "action_jump.mp3")));
        Assert.Empty(_state.Value.ModManifest);
    }

    [Fact]
    public void RemovedMod_OfARootFile_IsRestoredFromTheAppPackage()
    {
        WriteFile(Mod("root.txt"), "modded root");
        Applier.Apply(_version);
        File.Delete(Mod("root.txt"));

        Applier.Apply(_version);

        Assert.Equal("original root", File.ReadAllText(Installed("root.txt")));
    }

    [Fact]
    public void RemovedMod_ThatRobloxDoesNotHave_IsDeleted()
    {
        WriteFile(Mod("content", "new", "extra.txt"), "brand new");
        Applier.Apply(_version);
        File.Delete(Mod("content", "new", "extra.txt"));

        Applier.Apply(_version);

        Assert.False(File.Exists(Installed("content", "new", "extra.txt")));
    }

    [Fact]
    public void ReadOnlyRobloxFile_IsStillReplaced()
    {
        File.SetAttributes(Installed("content", "sounds", "action_jump.mp3"), FileAttributes.ReadOnly);
        WriteFile(Mod("content", "sounds", "action_jump.mp3"), "modded jump");

        Applier.Apply(_version);

        Assert.Equal("modded jump", File.ReadAllText(Installed("content", "sounds", "action_jump.mp3")));
    }

    [Fact]
    public void LockedRobloxFile_IsReported_NotFatal()
    {
        WriteFile(Mod("content", "sounds", "action_jump.mp3"), "modded jump");
        WriteFile(Mod("content", "new", "extra.txt"), "brand new");

        ModResult result;
        using (File.Open(Installed("content", "sounds", "action_jump.mp3"), FileMode.Open, FileAccess.Read, FileShare.None))
            result = Applier.Apply(_version);

        Assert.Equal([@"content\sounds\action_jump.mp3"], result.Failed);
        Assert.True(File.Exists(Installed("content", "new", "extra.txt")));
    }

    [Fact]
    public void CustomFont_RedirectsEveryFamily_AndUndoesItWhenRemoved()
    {
        WriteFile(_paths.CustomFont, "font bytes");

        Applier.Apply(_version);

        var family = JsonNode.Parse(File.ReadAllText(Installed("content", "fonts", "families", "Arial.json")))!;
        Assert.Equal(ModApplier.CustomFontAsset, (string?)family["faces"]![0]!["assetId"]);
        Assert.Equal("font bytes", File.ReadAllText(Installed("content", "fonts", "CustomFont.ttf")));

        File.Delete(_paths.CustomFont);
        Applier.Apply(_version);

        Assert.False(Directory.Exists(Mod("content", "fonts", "families")));
        Assert.Equal(ArialFamily, File.ReadAllText(Installed("content", "fonts", "families", "Arial.json")));
        Assert.False(File.Exists(Installed("content", "fonts", "CustomFont.ttf")));
    }

    [Theory]
    [InlineData(@"content\sounds\x.mp3", "content-sounds.zip")]
    [InlineData(@"PlatformContent\pc\textures\a.png", "content-textures3.zip")]
    [InlineData(@"ExtraContent\places\Mobile.rbxl", "extracontent-places.zip")]
    public void CandidatePackages_PreferTheMostSpecificFolder(string file, string expected) =>
        Assert.Equal(expected, ModApplier.CandidatePackages(file).First().Package);
}

public sealed class ModPresetTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly VizstrapPaths _paths;
    private readonly ModPresets _presets;

    public ModPresetTests()
    {
        _paths = new VizstrapPaths(_temp.Combine("Vizstrap"));
        _presets = new ModPresets(_paths);
    }

    public void Dispose() => _temp.Dispose();

    private string Mod(string relative) => Path.Combine(_paths.Modifications, relative);

    private const string ArrowCursor = @"content\textures\Cursors\KeyboardMouse\ArrowCursor.png";

    [Fact]
    public void Cursor_SwitchesBetweenStyles()
    {
        Assert.Equal(CursorStyle.Default, _presets.Cursor);

        _presets.SetCursor(CursorStyle.From2006);
        Assert.Equal(CursorStyle.From2006, _presets.Cursor);

        _presets.SetCursor(CursorStyle.From2013);
        Assert.Equal(CursorStyle.From2013, _presets.Cursor);

        _presets.SetCursor(CursorStyle.Default);
        Assert.Equal(CursorStyle.Default, _presets.Cursor);
        Assert.False(File.Exists(Mod(ArrowCursor)));
    }

    [Fact]
    public void Default_KeepsTheUsersOwnCursor()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Mod(ArrowCursor))!);
        File.WriteAllText(Mod(ArrowCursor), "my own cursor");

        _presets.SetCursor(CursorStyle.Default);

        Assert.Equal("my own cursor", File.ReadAllText(Mod(ArrowCursor)));
    }

    [Fact]
    public void OnOffPresets_WriteAndRemoveTheirFiles()
    {
        _presets.SetOldAvatarBackground(true);
        _presets.SetOldCharacterSounds(true);

        Assert.True(_presets.OldAvatarBackground);
        Assert.True(_presets.OldCharacterSounds);
        Assert.True(File.Exists(Mod(@"ExtraContent\places\Mobile.rbxl")));
        Assert.True(File.Exists(Mod(@"content\sounds\impact_water.mp3")));

        _presets.SetOldCharacterSounds(false);

        Assert.False(_presets.OldCharacterSounds);
        Assert.True(_presets.OldAvatarBackground);
        Assert.False(File.Exists(Mod(@"content\sounds\action_jump.mp3")));
    }

    [Fact]
    public void Emoji_EveryStyleHasAFixedDownloadAndItsChecksum()
    {
        Assert.Equal(Enum.GetValues<EmojiStyle>().Where(style => style != EmojiStyle.Default), ModPresets.EmojiFonts.Keys.Order());
        Assert.All(ModPresets.EmojiFonts.Values, font =>
        {
            Assert.StartsWith("https://", font.Url);
            Assert.EndsWith(".ttf", font.Url);
            Assert.Matches("^[0-9a-f]{32}$", font.Md5);
        });
        Assert.Equal(ModPresets.EmojiFonts.Count, ModPresets.EmojiFonts.Values.Select(font => font.Md5).Distinct().Count());
    }

    [Fact]
    public async Task Emoji_RejectsAFileThatIsNotTheExpectedFont()
    {
        var http = new FakeHttpHandler();
        http.MapBytes(ModPresets.EmojiUrl(EmojiStyle.Catmoji), Encoding.UTF8.GetBytes("not the font"));

        await Assert.ThrowsAsync<EmojiDownloadException>(() => _presets.SetEmojiAsync(EmojiStyle.Catmoji, new HttpClient(http)));

        Assert.Equal(EmojiStyle.Default, _presets.Emoji);
        Assert.False(File.Exists(Mod(@"content\fonts\TwemojiMozilla.ttf")));
    }

    [Fact]
    public async Task Emoji_ReportsNetworkFailure()
    {
        var http = new FakeHttpHandler();
        http.MapFailure(ModPresets.EmojiUrl(EmojiStyle.Windows11), HttpStatusCode.NotFound);

        await Assert.ThrowsAsync<EmojiDownloadException>(() => _presets.SetEmojiAsync(EmojiStyle.Windows11, new HttpClient(http)));
    }

    [Fact]
    public async Task Emoji_Default_LeavesAnUnknownFontAlone()
    {
        string file = Mod(@"content\fonts\TwemojiMozilla.ttf");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, "a font the user put there");

        await _presets.SetEmojiAsync(EmojiStyle.Default, new HttpClient(new FakeHttpHandler()));

        Assert.True(File.Exists(file));
    }

    [Fact]
    public void CustomFont_AcceptsFontsOnly()
    {
        string ttf = _temp.Combine("font.ttf");
        File.WriteAllBytes(ttf, [0x00, 0x01, 0x00, 0x00, 0x42, 0x42]);
        string fake = _temp.Combine("fake.ttf");
        File.WriteAllText(fake, "hello");

        Assert.True(ModPresets.IsFontFile(ttf));
        Assert.False(ModPresets.IsFontFile(fake));
        Assert.Throws<InvalidDataException>(() => _presets.SetCustomFont(fake));

        _presets.SetCustomFont(ttf);
        Assert.True(_presets.HasCustomFont);

        _presets.SetCustomFont(null);
        Assert.False(_presets.HasCustomFont);
    }

    private string Png(string name)
    {
        string path = _temp.Combine(name);
        File.WriteAllBytes(path, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, (byte)name.Length]);
        return path;
    }

    [Fact]
    public void CustomCursor_GoesToBothCursorFilesAndIsReplacedByOtherChoices()
    {
        string png = Png("mine.png");

        _presets.SetCursor(CursorStyle.Custom, png);

        Assert.Equal(CursorStyle.Custom, _presets.Cursor);
        Assert.Equal(File.ReadAllBytes(png), File.ReadAllBytes(Mod(ArrowCursor)));
        Assert.Equal(File.ReadAllBytes(png), File.ReadAllBytes(Mod(@"content\textures\Cursors\KeyboardMouse\ArrowFarCursor.png")));

        _presets.SetCursor(CursorStyle.Custom); // no new file: stays
        Assert.Equal(CursorStyle.Custom, _presets.Cursor);

        _presets.SetCursor(CursorStyle.From2013);
        Assert.Equal(CursorStyle.From2013, _presets.Cursor);

        _presets.SetCursor(CursorStyle.Custom, png);
        _presets.SetCursor(CursorStyle.Default);
        Assert.Equal(CursorStyle.Default, _presets.Cursor);
        Assert.False(File.Exists(Mod(ArrowCursor)));
    }

    [Fact]
    public void CustomCursor_AcceptsPngOnly()
    {
        string fake = _temp.Combine("cursor.png");
        File.WriteAllText(fake, "not a picture");

        Assert.False(ModPresets.IsPngFile(fake));
        Assert.Throws<InvalidDataException>(() => _presets.SetCursor(CursorStyle.Custom, fake));
        Assert.Equal(CursorStyle.Default, _presets.Cursor);
    }

    private const string DeathSoundFile = @"content\sounds\oof.ogg";

    [Fact]
    public void DeathSound_CanBeMutedReplacedAndRestored()
    {
        Assert.Equal(DeathSound.Default, _presets.DeathSound);

        _presets.SetDeathSound(DeathSound.Muted);
        Assert.Equal(DeathSound.Muted, _presets.DeathSound);

        string ogg = _temp.Combine("bonk.ogg");
        File.WriteAllBytes(ogg, [.. "OggS"u8, 0x00, 0x02, 0x00]);
        _presets.SetDeathSound(DeathSound.Custom, ogg);
        Assert.Equal(DeathSound.Custom, _presets.DeathSound);
        Assert.Equal(File.ReadAllBytes(ogg), File.ReadAllBytes(Mod(DeathSoundFile)));

        _presets.SetDeathSound(DeathSound.Custom); // no new file: stays
        Assert.Equal(DeathSound.Custom, _presets.DeathSound);

        _presets.SetDeathSound(DeathSound.Default);
        Assert.Equal(DeathSound.Default, _presets.DeathSound);
        Assert.False(File.Exists(Mod(DeathSoundFile)));
    }

    /// <summary>A valid 1×1 PNG.</summary>
    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    [Fact]
    public void PngText_IsStoredAndReadBack()
    {
        byte[] labelled = PngText.Add(TinyPng, ModPresets.ThemeMarker, "Green");

        Assert.Equal("Green", PngText.Read(labelled, ModPresets.ThemeMarker));
        Assert.Null(PngText.Read(labelled, "Other"));
        Assert.Null(PngText.Read(TinyPng, ModPresets.ThemeMarker));
        Assert.Null(PngText.Read("not a png"u8, ModPresets.ThemeMarker));
        Assert.EndsWith("IEND®B`\u0082", System.Text.Encoding.Latin1.GetString(labelled)); // still ends properly
    }

    [Fact]
    public void RobloxTheme_WritesThePicturesAndRemovesOnlyItsOwn()
    {
        var pictures = ModPresets.RobloxThemeTargets.ToDictionary(target => target, _ => PngText.Add(TinyPng, ModPresets.ThemeMarker, "Violet"));

        _presets.SetRobloxTheme(pictures);
        Assert.Equal("Violet", _presets.RobloxThemeAccent);

        // one picture replaced by the user: the theme no longer counts as fully Vizstrap's, and that file stays
        string own = Mod(ModPresets.RobloxThemeTargets[0]);
        File.WriteAllBytes(own, TinyPng);
        Assert.Null(_presets.RobloxThemeAccent);

        _presets.SetRobloxTheme(null);

        Assert.True(File.Exists(own));
        Assert.All(ModPresets.RobloxThemeTargets.Skip(1), target => Assert.False(File.Exists(Mod(target))));
    }

    [Theory]
    [InlineData(new byte[] { 0x4F, 0x67, 0x67, 0x53, 0, 2 }, true)]                                   // OggS
    [InlineData(new byte[] { 0x49, 0x44, 0x33, 4, 0, 0 }, true)]                                     // ID3 (MP3)
    [InlineData(new byte[] { 0xFF, 0xFB, 0x90, 0x64 }, true)]                                        // MPEG frame (MP3)
    [InlineData(new byte[] { 0x52, 0x49, 0x46, 0x46, 1, 0, 0, 0, 0x57, 0x41, 0x56, 0x45 }, true)]    // RIFF….WAVE
    [InlineData(new byte[] { 0x52, 0x49, 0x46, 0x46, 1, 0, 0, 0, 0x41, 0x56, 0x49, 0x20 }, false)]   // RIFF….AVI
    [InlineData(new byte[] { 0x68, 0x65, 0x6C, 0x6C, 0x6F }, false)]                                 // "hello"
    public void DeathSound_AcceptsOggMp3AndWav(byte[] header, bool expected)
    {
        string file = _temp.Combine("sound.bin");
        File.WriteAllBytes(file, header);

        Assert.Equal(expected, ModPresets.IsSoundFile(file));

        if (!expected)
            Assert.Throws<InvalidDataException>(() => _presets.SetDeathSound(DeathSound.Custom, file));
    }
}

public class CompatibilityFlagsTests
{
    [Theory]
    [InlineData("pl-PL", "Zgodność")]
    [InlineData("en-GB", "Compatibility")]
    [InlineData("de-AT", "Kompatibilität")]
    [InlineData("pt-BR", "Compatibilidade")]
    [InlineData("zh-CN", "兼容性")]
    [InlineData("fi-FI", "Compatibility")]
    public void CompatibilityTab_IsNamedInWindowsLanguage(string culture, string expected) =>
        Assert.Equal(expected, ShellProperties.CompatibilityTabName(new System.Globalization.CultureInfo(culture)));

    [Fact]
    public void Migrate_MovesSettingsToTheNewExecutable()
    {
        using var registry = new TestRegistry();
        using (var key = registry.Root.CreateSubKey(CompatibilityFlags.LayersKeyPath))
            key.SetValue(@"C:\v1\RobloxPlayerBeta.exe", "~ HIGHDPIAWARE DISABLEDXMAXIMIZEDWINDOWEDMODE");

        CompatibilityFlags.Migrate(@"C:\v1\RobloxPlayerBeta.exe", @"C:\v2\RobloxPlayerBeta.exe", registry.Root);

        using var layers = registry.Root.OpenSubKey(CompatibilityFlags.LayersKeyPath)!;
        Assert.Equal("~ HIGHDPIAWARE DISABLEDXMAXIMIZEDWINDOWEDMODE", layers.GetValue(@"C:\v2\RobloxPlayerBeta.exe"));
        Assert.Null(layers.GetValue(@"C:\v1\RobloxPlayerBeta.exe"));
    }

    [Fact]
    public void Migrate_WithoutSettings_DoesNothing()
    {
        using var registry = new TestRegistry();

        CompatibilityFlags.Migrate(@"C:\v1\a.exe", @"C:\v2\a.exe", registry.Root);

        Assert.Null(registry.Root.OpenSubKey(CompatibilityFlags.LayersKeyPath));
    }
}
