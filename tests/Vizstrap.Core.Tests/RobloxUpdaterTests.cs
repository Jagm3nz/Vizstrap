using System.Text;
using Vizstrap.Core.Install;
using Vizstrap.Core.Roblox;
using Vizstrap.Core.Storage;
using Vizstrap.Core.Tests.TestSupport;

namespace Vizstrap.Core.Tests;

public sealed class RobloxUpdaterTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly TestRegistry _registry = new();
    private readonly FakeHttpHandler _http = new();
    private readonly FakeRoblox _roblox;
    private readonly VizstrapPaths _paths;

    public RobloxUpdaterTests()
    {
        _roblox = new FakeRoblox(_http);
        _paths = new VizstrapPaths(_temp.Combine("Vizstrap"));
    }

    public void Dispose()
    {
        _registry.Dispose();
        _temp.Dispose();
    }

    private RobloxUpdater CreateUpdater(long freeSpace = long.MaxValue)
    {
        var client = new HttpClient(_http);
        var state = new JsonStore<State>(_paths.StateFile);
        var deployment = new RobloxDeployment(client, TimeSpan.Zero);
        var downloader = new PackageDownloader(client, _paths.Downloads, _temp.Combine("RobloxDownloads"), 2, TimeSpan.Zero);

        return new RobloxUpdater(_paths, state, deployment, downloader, _ => freeSpace, _registry.Root);
    }

    private State ReadState()
    {
        var state = new JsonStore<State>(_paths.StateFile);
        state.Load();
        return state.Value;
    }

    [Fact]
    public async Task FreshInstall_ExtractsMappedPackages_AndCommitsState()
    {
        var progress = new List<UpdateProgress>();

        var installed = await CreateUpdater().EnsureLatestAsync(new SyncProgress<UpdateProgress>(progress.Add), default);

        string versionDirectory = _paths.VersionDirectory(_roblox.VersionGuid);
        Assert.Equal(Path.Combine(versionDirectory, "RobloxPlayerBeta.exe"), installed.ExecutablePath);
        Assert.Equal($"player {_roblox.VersionGuid}", File.ReadAllText(installed.ExecutablePath));
        Assert.Equal("oof", File.ReadAllText(Path.Combine(versionDirectory, "content", "sounds", "sfx", "ouch.ogg")));
        Assert.Contains("<ContentFolder>content</ContentFolder>", File.ReadAllText(Path.Combine(versionDirectory, "AppSettings.xml")));

        var state = ReadState();
        Assert.Equal(_roblox.VersionGuid, state.PlayerVersion);
        Assert.Equal(["RobloxApp.zip", "content-sounds.zip"], state.PackageHashes.Keys.Order(StringComparer.Ordinal));

        Assert.True(progress.First(p => p.Stage == UpdateStage.Downloading).IsFreshInstall);
        var lastDownload = progress.Last(p => p.Stage == UpdateStage.Downloading);
        Assert.Equal((2, 2), (lastDownload.PackagesDone, lastDownload.PackagesTotal));
        Assert.Equal(lastDownload.BytesTotal, lastDownload.BytesDone);
    }

    [Fact]
    public async Task SkipsInstallerExe_IgnoredAndUnknownPackages()
    {
        await CreateUpdater().EnsureLatestAsync(null, default);

        Assert.Equal(0, _http.CountRequests(_roblox.PackageUrl("RobloxPlayerInstaller.exe")));
        Assert.Equal(0, _http.CountRequests(_roblox.PackageUrl("WebView2RuntimeInstaller.zip")));
        Assert.Equal(0, _http.CountRequests(_roblox.PackageUrl("content-brand-new.zip")));
    }

    [Fact]
    public async Task UpToDate_DownloadsNothing()
    {
        await CreateUpdater().EnsureLatestAsync(null, default);
        int requestsAfterInstall = _http.Requests.Count;

        var installed = await CreateUpdater().EnsureLatestAsync(null, default);

        Assert.Equal(_roblox.VersionGuid, installed.VersionGuid);
        var newRequests = _http.Requests.Skip(requestsAfterInstall).ToList();
        Assert.DoesNotContain(newRequests, url => url.Contains("/channel/common/"));
    }

    [Fact]
    public async Task Update_ReplacesOldVersion_AndPrunesCache()
    {
        await CreateUpdater().EnsureLatestAsync(null, default);
        string oldVersion = _roblox.VersionGuid;
        string oldAppSignature = ReadState().PackageHashes["RobloxApp.zip"];

        _roblox.Publish("version-bbbb444455556666", new Dictionary<string, byte[]>
        {
            ["RobloxApp.zip"] = TestZip.Create(("RobloxPlayerBeta.exe", "player v2")),
            ["content-sounds.zip"] = _roblox.Packages["content-sounds.zip"],
        });

        var installed = await CreateUpdater().EnsureLatestAsync(null, default);

        Assert.Equal("player v2", File.ReadAllText(installed.ExecutablePath));
        Assert.False(Directory.Exists(_paths.VersionDirectory(oldVersion)));
        Assert.False(File.Exists(Path.Combine(_paths.Downloads, oldAppSignature)));
        Assert.Equal(1, _http.CountRequests(_roblox.PackageUrl("RobloxApp.zip")));
        Assert.Equal(0, _http.CountRequests(_roblox.PackageUrl("content-sounds.zip")));
    }

    [Fact]
    public async Task Update_CarriesCompatibilitySettingsToTheNewExecutable()
    {
        var old = await CreateUpdater().EnsureLatestAsync(null, default);
        using (var key = _registry.Root.CreateSubKey(Vizstrap.Core.Platform.CompatibilityFlags.LayersKeyPath))
            key.SetValue(old.ExecutablePath, "~ HIGHDPIAWARE");

        _roblox.Publish("version-dddd000011112222", new Dictionary<string, byte[]>
        {
            ["RobloxApp.zip"] = TestZip.Create(("RobloxPlayerBeta.exe", "player v4")),
        });

        var updated = await CreateUpdater().EnsureLatestAsync(null, default);

        using var layers = _registry.Root.OpenSubKey(Vizstrap.Core.Platform.CompatibilityFlags.LayersKeyPath)!;
        Assert.Equal("~ HIGHDPIAWARE", layers.GetValue(updated.ExecutablePath));
        Assert.Null(layers.GetValue(old.ExecutablePath));
    }

    [Fact]
    public async Task FailedUpdate_KeepsPreviousInstall()
    {
        await CreateUpdater().EnsureLatestAsync(null, default);
        string oldVersion = _roblox.VersionGuid;

        _roblox.Publish("version-cccc777788889999", new Dictionary<string, byte[]>
        {
            ["RobloxApp.zip"] = TestZip.Create(("RobloxPlayerBeta.exe", "player v3")),
        });
        _http.MapFailure(_roblox.PackageUrl("RobloxApp.zip"));

        await Assert.ThrowsAsync<PackageDownloadException>(() => CreateUpdater().EnsureLatestAsync(null, default));

        Assert.Equal(oldVersion, ReadState().PlayerVersion);
        Assert.True(File.Exists(Path.Combine(_paths.VersionDirectory(oldVersion), "RobloxPlayerBeta.exe")));
    }

    [Fact]
    public async Task Cancelled_CommitsNothing()
    {
        using var cancellation = new CancellationTokenSource();
        var progress = new SyncProgress<UpdateProgress>(p =>
        {
            if (p.Stage == UpdateStage.Downloading && p.PackagesDone == 1)
                cancellation.Cancel();
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateUpdater().EnsureLatestAsync(progress, cancellation.Token));

        Assert.Null(ReadState().PlayerVersion);
    }

    [Fact]
    public async Task Offline_LaunchesInstalledVersion()
    {
        await CreateUpdater().EnsureLatestAsync(null, default);
        _http.MapFailure($"{FakeRoblox.Mirror}/versionStudio");

        var installed = await CreateUpdater().EnsureLatestAsync(null, default);

        Assert.Equal(_roblox.VersionGuid, installed.VersionGuid);
    }

    [Fact]
    public async Task Offline_WithoutInstall_Throws()
    {
        _http.MapFailure($"{FakeRoblox.Mirror}/versionStudio");

        await Assert.ThrowsAsync<RobloxConnectionException>(() => CreateUpdater().EnsureLatestAsync(null, default));
    }

    [Fact]
    public async Task NotEnoughDiskSpace_Throws_BeforeDownloading()
    {
        var error = await Assert.ThrowsAsync<NotEnoughDiskSpaceException>(() => CreateUpdater(freeSpace: 10).EnsureLatestAsync(null, default));

        Assert.Equal(10, error.AvailableBytes);
        Assert.DoesNotContain(_http.Requests, url => url.EndsWith(".zip"));
    }

    [Fact]
    public async Task ForceReinstall_RebuildsFolder_AndClearsFlag()
    {
        var installed = await CreateUpdater().EnsureLatestAsync(null, default);
        string stray = Path.Combine(installed.Directory, "stray.txt");
        File.WriteAllText(stray, "left over");

        var state = new JsonStore<State>(_paths.StateFile);
        state.Load();
        state.Value.ForceReinstall = true;
        state.Save();

        await CreateUpdater().EnsureLatestAsync(null, default);

        Assert.False(File.Exists(stray));
        Assert.False(ReadState().ForceReinstall);
    }

    private void PublishSecondVersion() => _roblox.Publish("version-bbbb444455556666", new Dictionary<string, byte[]>
    {
        ["RobloxApp.zip"] = TestZip.Create(("RobloxPlayerBeta.exe", "player v2")),
        ["content-sounds.zip"] = _roblox.Packages["content-sounds.zip"],
    }, version: "0.741.0.7410001");

    [Fact]
    public async Task DeferredUpdate_LaunchesTheInstalledVersionAndDownloadsNothing()
    {
        await CreateUpdater().EnsureLatestAsync(null, default);
        string oldVersion = _roblox.VersionGuid;
        PublishSecondVersion();
        int requestsBefore = _http.Requests.Count;
        ClientVersion? offered = null;

        var result = await CreateUpdater().EnsureLatestAsync((installed, latest, _) =>
        {
            offered = latest;
            return Task.FromResult(true);
        }, null, default);

        Assert.Equal(oldVersion, result.Installed.VersionGuid);
        Assert.Equal("version-bbbb444455556666", result.Deferred?.VersionGuid);
        Assert.Equal("0.741.0.7410001", offered?.Version);
        Assert.Equal(oldVersion, ReadState().PlayerVersion);
        Assert.DoesNotContain(_http.Requests.Skip(requestsBefore), url => url.Contains("/channel/common/"));
    }

    [Fact]
    public async Task RefusedDeferral_UpdatesAsUsual()
    {
        await CreateUpdater().EnsureLatestAsync(null, default);
        PublishSecondVersion();

        var result = await CreateUpdater().EnsureLatestAsync((_, _, _) => Task.FromResult(false), null, default);

        Assert.Equal("version-bbbb444455556666", result.Installed.VersionGuid);
        Assert.Null(result.Deferred);
    }

    [Fact]
    public async Task BackgroundUpdate_PreparesNextToTheOldVersionThenSwitches()
    {
        await CreateUpdater().EnsureLatestAsync(null, default);
        string oldVersion = _roblox.VersionGuid;
        PublishSecondVersion();
        var updater = CreateUpdater();

        var latest = await updater.GetLatestVersionAsync(default);
        var prepared = await updater.PrepareAsync(latest.VersionGuid, isFreshInstall: false, null, default);

        // the old version is still the installed one, untouched, while the new one waits
        Assert.Equal(oldVersion, ReadState().PlayerVersion);
        Assert.True(File.Exists(Path.Combine(_paths.VersionDirectory(oldVersion), "RobloxPlayerBeta.exe")));
        Assert.Equal("player v2", File.ReadAllText(prepared.ExecutablePath));

        var installed = updater.Commit(prepared);

        Assert.Equal("version-bbbb444455556666", installed.VersionGuid);
        Assert.Equal("version-bbbb444455556666", ReadState().PlayerVersion);
        Assert.False(Directory.Exists(_paths.VersionDirectory(oldVersion)));
    }

    [Fact]
    public async Task Update_KeepsTheOldVersionWhileRobloxRunsFromIt()
    {
        await CreateUpdater().EnsureLatestAsync(null, default);
        string oldDirectory = _paths.VersionDirectory(_roblox.VersionGuid);
        PublishSecondVersion();

        var client = new HttpClient(_http);
        var updater = new RobloxUpdater(_paths, new JsonStore<State>(_paths.StateFile), new RobloxDeployment(client, TimeSpan.Zero),
            new PackageDownloader(client, _paths.Downloads, _temp.Combine("RobloxDownloads"), 2, TimeSpan.Zero),
            _ => long.MaxValue, _registry.Root, isInUse: directory => directory == oldDirectory);

        await updater.EnsureLatestAsync(null, default);

        Assert.True(Directory.Exists(oldDirectory));

        // next launch, with that Roblox closed, the old folder goes
        await CreateUpdater().EnsureLatestAsync(null, default);
        Assert.False(Directory.Exists(oldDirectory));
    }

    [Fact]
    public void SelectPackages_FiltersByMap()
    {
        var selected = RobloxUpdater.SelectPackages(
        [
            new Package("RobloxApp.zip", "a", 1, 1),
            new Package("RobloxPlayerInstaller.exe", "b", 1, 1),
            new Package("WebView2RuntimeInstaller.zip", "c", 1, 1),
            new Package("mystery.zip", "d", 1, 1),
        ]);

        Assert.Equal("RobloxApp.zip", Assert.Single(selected).Name);
    }

    /// <summary>Unlike <see cref="Progress{T}"/>, reports synchronously so assertions see every update.</summary>
    private sealed class SyncProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
