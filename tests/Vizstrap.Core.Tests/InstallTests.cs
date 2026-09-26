using System.Net;
using System.Text;
using Vizstrap.Core.Install;
using Vizstrap.Core.Roblox;
using Vizstrap.Core.Tests.TestSupport;

namespace Vizstrap.Core.Tests;

public class PackageExtractorTests
{
    [Fact]
    public void Extract_HandlesLeadingSlashEntries_LikeRealRobloxZips()
    {
        using var temp = new TempDirectory();
        string zip = temp.Combine("configs.zip");
        File.WriteAllBytes(zip, TestZip.Create(("/", ""), ("/Nested/", ""), ("Nested/a.json", "{}"), ("root.txt", "hi")));

        PackageExtractor.Extract(zip, temp.Combine("out", "content", "configs"));

        Assert.Equal("{}", File.ReadAllText(temp.Combine("out", "content", "configs", "Nested", "a.json")));
        Assert.Equal("hi", File.ReadAllText(temp.Combine("out", "content", "configs", "root.txt")));
    }

    [Fact]
    public void Extract_BackslashEntries()
    {
        using var temp = new TempDirectory();
        string zip = temp.Combine("p.zip");
        File.WriteAllBytes(zip, TestZip.Create((@"dir\file.txt", "x")));

        PackageExtractor.Extract(zip, temp.Combine("out"));

        Assert.True(File.Exists(temp.Combine("out", "dir", "file.txt")));
    }

    [Theory]
    [InlineData("../escape.txt")]
    [InlineData("a/../../escape.txt")]
    [InlineData("C:/Windows/escape.txt")]
    public void Extract_RefusesEntriesOutsideTarget(string entryName)
    {
        using var temp = new TempDirectory();
        string zip = temp.Combine("evil.zip");
        File.WriteAllBytes(zip, TestZip.Create((entryName, "x")));

        Assert.Throws<InvalidDataException>(() => PackageExtractor.Extract(zip, temp.Combine("out")));
        Assert.False(File.Exists(temp.Combine("escape.txt")));
    }
}

public class PackageDownloaderTests
{
    private static readonly byte[] Bytes = Encoding.UTF8.GetBytes("package body");
    private static readonly Package Package = new("a.zip", TestZip.Md5(Bytes), Bytes.Length, 100);
    private const string Url = "https://setup.rbxcdn.com/channel/common/version-x-a.zip";

    private static PackageDownloader Create(FakeHttpHandler http, TempDirectory temp, int attempts = 3) =>
        new(new HttpClient(http), temp.Combine("Downloads"), temp.Combine("RobloxDownloads"), attempts, TimeSpan.Zero);

    [Fact]
    public async Task Downloads_Verifies_AndReportsBytes()
    {
        using var temp = new TempDirectory();
        var http = new FakeHttpHandler();
        http.MapBytes(Url, Bytes);
        long reported = 0;

        var result = await Create(http, temp).DownloadAsync(Package, Url, bytes => reported += bytes, default);

        Assert.False(result.FromCache);
        Assert.Equal(Bytes, File.ReadAllBytes(result.Path));
        Assert.Equal(temp.Combine("Downloads", Package.Signature), result.Path);
        Assert.Equal(Bytes.Length, reported);
    }

    [Fact]
    public async Task UsesOwnCache_WithoutNetwork()
    {
        using var temp = new TempDirectory();
        Directory.CreateDirectory(temp.Combine("Downloads"));
        File.WriteAllBytes(temp.Combine("Downloads", Package.Signature), Bytes);
        var http = new FakeHttpHandler();

        var result = await Create(http, temp).DownloadAsync(Package, Url, _ => { }, default);

        Assert.True(result.FromCache);
        Assert.Empty(http.Requests);
    }

    [Fact]
    public async Task CorruptedCache_IsDownloadedAgain()
    {
        using var temp = new TempDirectory();
        Directory.CreateDirectory(temp.Combine("Downloads"));
        File.WriteAllText(temp.Combine("Downloads", Package.Signature), "garbage");
        var http = new FakeHttpHandler();
        http.MapBytes(Url, Bytes);

        var result = await Create(http, temp).DownloadAsync(Package, Url, _ => { }, default);

        Assert.False(result.FromCache);
        Assert.Equal(Bytes, File.ReadAllBytes(result.Path));
    }

    [Fact]
    public async Task ReusesOfficialRobloxCache_WhenValid()
    {
        using var temp = new TempDirectory();
        Directory.CreateDirectory(temp.Combine("RobloxDownloads"));
        File.WriteAllBytes(temp.Combine("RobloxDownloads", Package.Signature), Bytes);
        var http = new FakeHttpHandler();

        var result = await Create(http, temp).DownloadAsync(Package, Url, _ => { }, default);

        Assert.True(result.FromCache);
        Assert.Empty(http.Requests);
        Assert.True(File.Exists(temp.Combine("RobloxDownloads", Package.Signature)), "Roblox's own cache must be left alone");
    }

    [Fact]
    public async Task IgnoresInvalidOfficialRobloxCache()
    {
        using var temp = new TempDirectory();
        Directory.CreateDirectory(temp.Combine("RobloxDownloads"));
        File.WriteAllText(temp.Combine("RobloxDownloads", Package.Signature), "tampered");
        var http = new FakeHttpHandler();
        http.MapBytes(Url, Bytes);

        var result = await Create(http, temp).DownloadAsync(Package, Url, _ => { }, default);

        Assert.False(result.FromCache);
        Assert.Equal(Bytes, File.ReadAllBytes(result.Path));
    }

    [Fact]
    public async Task RetriesAfterFailure_AndRollsBackReportedBytes()
    {
        using var temp = new TempDirectory();
        var http = new FakeHttpHandler();
        int calls = 0;
        http.Map(Url, () => ++calls == 1
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("wrong body") }
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Bytes) });
        long reported = 0;

        await Create(http, temp).DownloadAsync(Package, Url, bytes => reported += bytes, default);

        Assert.Equal(2, calls);
        Assert.Equal(Bytes.Length, reported);
    }

    [Fact]
    public async Task GivesUpAfterMaxAttempts_AndLeavesNoPartialFile()
    {
        using var temp = new TempDirectory();
        var http = new FakeHttpHandler();
        http.MapFailure(Url);

        var error = await Assert.ThrowsAsync<PackageDownloadException>(
            () => Create(http, temp, attempts: 3).DownloadAsync(Package, Url, _ => { }, default));

        Assert.Equal("a.zip", error.PackageName);
        Assert.Equal(3, http.CountRequests(Url));
        Assert.Empty(Directory.GetFiles(temp.Combine("Downloads")));
    }
}

public class RobloxDeploymentTests
{
    [Fact]
    public async Task SelectMirror_SkipsUnhealthyMirrors()
    {
        var http = new FakeHttpHandler();
        http.MapText("https://setup.rbxcdn.com/versionStudio", "<html>captive portal</html>");
        http.MapText("https://setup-ak.rbxcdn.com/versionStudio", RobloxDeployment.VersionStudioHash);
        var deployment = new RobloxDeployment(new HttpClient(http), TimeSpan.Zero);

        string mirror = await deployment.SelectMirrorAsync(default);

        Assert.Equal("https://setup-ak.rbxcdn.com", mirror);
        Assert.Equal("https://setup-ak.rbxcdn.com/channel/common/version-1-a.zip", deployment.GetFileUrl("version-1", "a.zip"));
    }

    [Fact]
    public async Task SelectMirror_AllFailing_ThrowsConnectionError()
    {
        var deployment = new RobloxDeployment(new HttpClient(new FakeHttpHandler()), TimeSpan.Zero);

        var error = await Assert.ThrowsAsync<RobloxConnectionException>(() => deployment.SelectMirrorAsync(default));

        Assert.Equal(RobloxDeployment.Mirrors.Count, error.Attempts.Count);
    }

    [Fact]
    public async Task GetLatestVersion_FallsBackToSecondHost()
    {
        var http = new FakeHttpHandler();
        http.MapFailure("https://clientsettingscdn.roblox.com/v2/client-version/WindowsPlayer");
        http.MapText("https://clientsettings.roblox.com/v2/client-version/WindowsPlayer",
            "{\"version\":\"0.740.0.7400927\",\"clientVersionUpload\":\"version-2366ba214ec740ca\",\"bootstrapperVersion\":\"\"}");

        var version = await new RobloxDeployment(new HttpClient(http)).GetLatestVersionAsync(default);

        Assert.Equal(new ClientVersion("0.740.0.7400927", "version-2366ba214ec740ca"), version);
    }
}

public class BackgroundUpdatesTests
{
    private static readonly Version Installed = new(0, 740);

    [Theory]
    [InlineData("0.740.0.7400999", true)]   // same release, newer build
    [InlineData("0.741.0.7410001", true)]   // the next release
    [InlineData("0.742.0.7420001", false)]  // two releases on: Roblox may refuse the old client
    [InlineData("0.739.0.7390001", false)]  // a rollback installs normally
    [InlineData("1.741.0.1", false)]
    [InlineData("nonsense", false)]
    public void Only_small_steps_can_wait(string latest, bool expected) =>
        Assert.Equal(expected, BackgroundUpdates.CanDefer(Installed, latest, long.MaxValue, forceReinstall: false, out _));

    [Fact]
    public void A_forced_reinstall_little_space_or_an_unknown_version_update_right_away()
    {
        Assert.False(BackgroundUpdates.CanDefer(Installed, "0.741.0.1", long.MaxValue, forceReinstall: true, out string forced));
        Assert.False(BackgroundUpdates.CanDefer(Installed, "0.741.0.1", BackgroundUpdates.MinimumFreeSpace - 1, forceReinstall: false, out string space));
        Assert.False(BackgroundUpdates.CanDefer(null, "0.741.0.1", long.MaxValue, forceReinstall: false, out string unknown));

        Assert.All([forced, space, unknown], reason => Assert.NotEmpty(reason));
    }

    [Fact]
    public void Files_without_version_information_have_no_release()
    {
        using var directory = new TempDirectory();
        string fake = directory.Combine("RobloxPlayerBeta.exe");
        File.WriteAllText(fake, "not a program");

        Assert.Null(BackgroundUpdates.ReadRelease(fake));
        Assert.Null(BackgroundUpdates.ReadRelease(directory.Combine("missing.exe")));
    }

    [Fact]
    public void A_real_program_has_its_release_read()
    {
        // any Windows program works: its file version plays the part of Roblox's "0.740"
        string notepad = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "notepad.exe");

        Assert.NotNull(BackgroundUpdates.ReadRelease(notepad));
    }

    [Fact]
    public async Task Nothing_running_means_nothing_to_stop()
    {
        Assert.False(BackgroundUpdates.IsRunning());
        Assert.True(await BackgroundUpdates.StopAsync(TimeSpan.FromSeconds(1)));
    }
}
