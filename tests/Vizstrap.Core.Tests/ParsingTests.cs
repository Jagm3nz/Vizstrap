using System.Globalization;
using Vizstrap.Core.Launch;
using Vizstrap.Core.Localization;
using Vizstrap.Core.Roblox;

namespace Vizstrap.Core.Tests;

public class PackageManifestTests
{
    [Fact]
    public void Parse_ReadsRecords_AndNormalisesSignatureCase()
    {
        const string text = "v0\r\nRobloxApp.zip\r\n76B0F3CC57052788E18608F53D737E51\r\n137452762\r\n176337451\r\n" +
                            "ssl.zip\r\n1d53f45baa6803288ed713e1de59a91c\r\n130569\r\n228725\r\n";

        var packages = PackageManifest.Parse(text);

        Assert.Equal(
            [
                new Package("RobloxApp.zip", "76b0f3cc57052788e18608f53d737e51", 137452762, 176337451),
                new Package("ssl.zip", "1d53f45baa6803288ed713e1de59a91c", 130569, 228725),
            ],
            packages);
    }

    [Fact]
    public void Parse_KeepsNonZipEntries_ForTheUpdaterToFilter()
    {
        var packages = PackageManifest.Parse("v0\nRobloxPlayerInstaller.exe\nabc\n1\n2\n");

        Assert.Equal("RobloxPlayerInstaller.exe", Assert.Single(packages).Name);
    }

    [Fact]
    public void Parse_RejectsUnknownFormat()
    {
        Assert.Throws<FormatException>(() => PackageManifest.Parse("v1\nfoo.zip\nabc\n1\n2\n"));
    }

    [Fact]
    public void Parse_StopsAtIncompleteRecord()
    {
        var packages = PackageManifest.Parse("v0\na.zip\nabc\n1\n2\nb.zip\nabc\n");

        Assert.Equal("a.zip", Assert.Single(packages).Name);
    }
}

public class LaunchArgsTests
{
    private const string Uri = "roblox-player:1+launchmode:play+gameinfo:abc+placelauncherurl:https%3A%2F%2Fassetgame.roblox.com";

    [Fact]
    public void NoArguments_IsNone() => Assert.Equal(new LaunchArgs(LaunchCommand.None), LaunchArgs.Parse([]));

    [Fact]
    public void PlayerWithUri() => Assert.Equal(new LaunchArgs(LaunchCommand.Player, Uri), LaunchArgs.Parse(["-player", Uri]));

    [Fact]
    public void PlayerWithoutUri_OpensRobloxHome() =>
        Assert.Equal(new LaunchArgs(LaunchCommand.Player), LaunchArgs.Parse(["-player"]));

    [Fact]
    public void PlayerIgnoresNonRobloxUri() =>
        Assert.Equal(new LaunchArgs(LaunchCommand.Player), LaunchArgs.Parse(["-player", "https://evil.example"]));

    [Fact]
    public void BareRobloxUri_IsPlayer() =>
        Assert.Equal(new LaunchArgs(LaunchCommand.Player, "roblox://experiences/start?placeId=1"),
            LaunchArgs.Parse(["roblox://experiences/start?placeId=1"]));

    [Fact]
    public void NoLaunchFlag() =>
        Assert.Equal(new LaunchArgs(LaunchCommand.Player, Uri, NoLaunch: true), LaunchArgs.Parse(["-player", Uri, "-nolaunch"]));

    [Fact]
    public void SettingsWithPage() =>
        Assert.Equal(new LaunchArgs(LaunchCommand.Settings, SettingsPage: "about"), LaunchArgs.Parse(["-settings", "about"]));

    [Theory]
    [InlineData("-settings", LaunchCommand.Settings)]
    [InlineData("-UNINSTALL", LaunchCommand.Uninstall)]
    [InlineData("-backgroundupdate", LaunchCommand.BackgroundUpdate)]
    [InlineData("-welcome", LaunchCommand.Welcome)]
    [InlineData("-reinstall", LaunchCommand.Reinstall)]
    [InlineData("-effectstest", LaunchCommand.EffectsTest)]
    public void Commands(string arg, LaunchCommand expected) => Assert.Equal(expected, LaunchArgs.Parse([arg]).Command);
}

public class SupportedLanguagesTests
{
    [Theory]
    [InlineData("pl", "en-US", "pl")]
    [InlineData("PT-br", "en-US", "pt-BR")]
    [InlineData(null, "pl-PL", "pl")]
    [InlineData(null, "de-AT", "de")]
    [InlineData(null, "pt-PT", "pt-BR")]
    [InlineData(null, "zh-CN", "zh-Hans")]
    [InlineData(null, "zh-TW", "zh-Hans")]
    [InlineData(null, "ja-JP", "en")]
    [InlineData("xx", "fr-CA", "fr")]
    public void Resolve(string? preferred, string system, string expected) =>
        Assert.Equal(expected, SupportedLanguages.Resolve(preferred, new CultureInfo(system)));

    [Fact]
    public void Resolve_InvariantCulture_FallsBackToEnglish() =>
        Assert.Equal("en", SupportedLanguages.Resolve(null, CultureInfo.InvariantCulture));
}

public class ByteSizeTests
{
    [Theory]
    [InlineData(512, "512 B")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(13002342.4, "12.4 MB")]
    [InlineData(3221225472, "3.0 GB")]
    public void Format(double bytes, string expected) =>
        Assert.Equal(expected, ByteSize.Format(bytes, CultureInfo.InvariantCulture));

    [Fact]
    public void Format_UsesCultureDecimalSeparator() =>
        Assert.Equal("12,4 MB", ByteSize.Format(13002342.4, new CultureInfo("pl-PL")));
}
