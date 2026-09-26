using Vizstrap.Core.Install;
using Vizstrap.Core.Mods;
using Vizstrap.Core.Roblox;
using Vizstrap.Core.Storage;
using Vizstrap.Core.Tests.TestSupport;

namespace Vizstrap.Core.Tests;

/// <summary>
/// Downloads the real current Roblox Player (~250 MB) without launching it. Opt-in:
/// set VIZSTRAP_LIVE_TESTS=1 (and optionally VIZSTRAP_LIVE_DIR to keep the result).
/// </summary>
public class LiveInstallTests
{
    private static bool Enabled => Environment.GetEnvironmentVariable("VIZSTRAP_LIVE_TESTS") == "1";

    [Fact]
    [Trait("Category", "Live")]
    public async Task PackageMap_CoversEveryCurrentPackage()
    {
        if (!Enabled)
            return;

        var deployment = new RobloxDeployment(new HttpClient());
        await deployment.SelectMirrorAsync(default);
        var version = await deployment.GetLatestVersionAsync(default);
        var manifest = await deployment.GetPackageManifestAsync(version.VersionGuid, default);

        var unknown = manifest
            .Where(package => package.Name.EndsWith(".zip") && !PackageMap.Ignored.Contains(package.Name))
            .Where(package => !PackageMap.Player.ContainsKey(package.Name))
            .Select(package => package.Name);

        Assert.Empty(unknown);
    }

    [Fact]
    [Trait("Category", "Live")]
    public async Task InstallsLatestRoblox()
    {
        if (!Enabled)
            return;

        string? keepDirectory = Environment.GetEnvironmentVariable("VIZSTRAP_LIVE_DIR");
        using var temp = new TempDirectory();
        var paths = new VizstrapPaths(keepDirectory ?? temp.Path);

        var http = new HttpClient();
        var updater = new RobloxUpdater(
            paths,
            new JsonStore<State>(paths.StateFile),
            new RobloxDeployment(http),
            new PackageDownloader(http, paths.Downloads, robloxCacheDirectory: null));

        var installed = await updater.EnsureLatestAsync(null, default);

        Assert.True(File.Exists(installed.ExecutablePath));
        Assert.True(Directory.Exists(Path.Combine(installed.Directory, "content", "sounds")));
        Assert.True(Directory.Exists(Path.Combine(installed.Directory, "PlatformContent", "pc", "textures")));
        Assert.True(Directory.Exists(Path.Combine(installed.Directory, "ExtraContent", "LuaPackages")));
    }

    [Theory]
    [Trait("Category", "Live")]
    [InlineData(EmojiStyle.Catmoji)]
    [InlineData(EmojiStyle.OpenMoji)]
    [InlineData(EmojiStyle.EmojiTwo)]
    public async Task EmojiFont_DownloadsAndIsRecognised(EmojiStyle style)
    {
        if (!Enabled)
            return;

        using var temp = new TempDirectory();
        var presets = new ModPresets(new VizstrapPaths(temp.Path));

        await presets.SetEmojiAsync(style, new HttpClient());

        Assert.Equal(style, presets.Emoji);
    }
}
