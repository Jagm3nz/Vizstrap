using Microsoft.Win32;
using Vizstrap.Core.Platform;
using Vizstrap.Core.Tests.TestSupport;

namespace Vizstrap.Core.Tests;

/// <summary>A throwaway HKCU\Software\VizstrapTests_{guid} key standing in for HKCU.</summary>
internal sealed class TestRegistry : IDisposable
{
    private readonly string _path = $@"Software\VizstrapTests_{Guid.NewGuid():N}";

    public TestRegistry()
    {
        Root = Registry.CurrentUser.CreateSubKey(_path);
        Classes = Root.CreateSubKey(@"Software\Classes");
    }

    public RegistryKey Root { get; }

    public RegistryKey Classes { get; }

    public void Dispose()
    {
        Classes.Dispose();
        Root.Dispose();
        Registry.CurrentUser.DeleteSubKeyTree(_path, throwOnMissingSubKey: false);
    }
}

public class ProtocolRegistrarTests
{
    [Fact]
    public void RegisterVizstrap_WritesBothProtocols()
    {
        using var registry = new TestRegistry();
        var registrar = new ProtocolRegistrar(registry.Classes);

        registrar.RegisterVizstrap(@"C:\Apps\Vizstrap.exe");

        foreach (string protocol in ProtocolRegistrar.PlayerProtocols)
        {
            Assert.Equal("\"C:\\Apps\\Vizstrap.exe\" -player \"%1\"", registrar.GetCommand(protocol));

            using var key = registry.Classes.OpenSubKey(protocol)!;
            Assert.Equal("", key.GetValue("URL Protocol"));
        }
    }

    [Fact]
    public void Unregister_RemovesProtocols()
    {
        using var registry = new TestRegistry();
        var registrar = new ProtocolRegistrar(registry.Classes);
        registrar.RegisterVizstrap(@"C:\Apps\Vizstrap.exe");

        registrar.Unregister();

        Assert.Null(registrar.GetCommand("roblox-player"));
    }

    [Theory]
    [InlineData("\"C:\\A B\\Bloxstrap.exe\" -player \"%1\"", "C:\\A B\\Bloxstrap.exe")]
    [InlineData("C:\\Roblox\\RobloxPlayerBeta.exe %1", "C:\\Roblox\\RobloxPlayerBeta.exe")]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void GetExecutable(string? command, string? expected) => Assert.Equal(expected, ProtocolRegistrar.GetExecutable(command));
}

public sealed class SelfInstallerTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly TestRegistry _registry = new();
    private readonly VizstrapPaths _paths;
    private readonly ProtocolRegistrar _protocols;
    private readonly SelfInstaller _installer;
    private readonly string _source;

    public SelfInstallerTests()
    {
        _paths = new VizstrapPaths(_temp.Combine("Vizstrap"));
        _protocols = new ProtocolRegistrar(_registry.Classes);
        _installer = new SelfInstaller(_paths, _protocols, _registry.Root, _temp.Combine("StartMenu"), _temp.Combine("Desktop"));

        _source = _temp.Combine("Downloads", "Vizstrap.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(_source)!);
        File.WriteAllText(_source, "fake exe");
    }

    public void Dispose()
    {
        _registry.Dispose();
        _temp.Dispose();
    }

    [Fact]
    public void Install_CopiesExe_CreatesShortcuts_AndTakesOverLinks()
    {
        _installer.Install(_source, "0.1.0", createDesktopShortcut: true, robloxSizeKb: 1000);

        Assert.True(_installer.IsInstalled);
        Assert.Equal("fake exe", File.ReadAllText(_paths.InstalledExecutable));
        Assert.Equal(_paths.InstalledExecutable, Shortcut.GetTarget(_installer.StartMenuShortcut), ignoreCase: true);
        Assert.True(File.Exists(_installer.DesktopShortcut));
        Assert.Equal(ProtocolRegistrar.VizstrapCommand(_paths.InstalledExecutable), _protocols.GetCommand("roblox-player"));

        using var uninstallKey = _registry.Root.OpenSubKey(SelfInstaller.UninstallKeyPath)!;
        Assert.Equal($"\"{_paths.InstalledExecutable}\" -uninstall", uninstallKey.GetValue("UninstallString"));
        Assert.Equal("0.1.0", uninstallKey.GetValue("DisplayVersion"));
    }

    [Fact]
    public void Uninstall_HandsLinksBackToPreviousLauncher()
    {
        string bloxstrap = _temp.Combine("Bloxstrap", "Bloxstrap.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(bloxstrap)!);
        File.WriteAllText(bloxstrap, "other launcher");
        string bloxstrapCommand = $"\"{bloxstrap}\" -player \"%1\"";
        _protocols.Register(bloxstrapCommand, bloxstrap);

        _installer.Install(_source, "0.1.0", createDesktopShortcut: false, robloxSizeKb: 0);
        _installer.Install(_source, "0.1.1", createDesktopShortcut: false, robloxSizeKb: 0); // reinstall must not forget Bloxstrap
        _installer.Uninstall();

        Assert.Equal(bloxstrapCommand, _protocols.GetCommand("roblox-player"));
        Assert.Equal(bloxstrapCommand, _protocols.GetCommand("roblox"));
    }

    [Fact]
    public void Uninstall_FallsBackToOfficialPlayer()
    {
        string officialDirectory = _temp.Combine("Roblox", "Versions", "version-1");
        Directory.CreateDirectory(officialDirectory);
        string official = Path.Combine(officialDirectory, "RobloxPlayerBeta.exe");
        File.WriteAllText(official, "official");
        using (var key = _registry.Root.CreateSubKey(SelfInstaller.OfficialPlayerUninstallKeyPath))
            key.SetValue("InstallLocation", officialDirectory);

        _installer.Install(_source, "0.1.0", createDesktopShortcut: false, robloxSizeKb: 0);
        _installer.Uninstall();

        Assert.Equal(ProtocolRegistrar.OfficialCommand(official), _protocols.GetCommand("roblox-player"));
    }

    [Fact]
    public void Uninstall_WithNoOtherLauncher_RemovesLinks_AndEverythingElse()
    {
        _installer.Install(_source, "0.1.0", createDesktopShortcut: true, robloxSizeKb: 0);
        Directory.CreateDirectory(Path.Combine(_paths.Versions, "version-1"));
        File.WriteAllText(_paths.StateFile, "{}");

        _installer.Uninstall();

        Assert.Null(_protocols.GetCommand("roblox-player"));
        Assert.False(File.Exists(_installer.StartMenuShortcut));
        Assert.False(File.Exists(_installer.DesktopShortcut));
        Assert.Null(_registry.Root.OpenSubKey(SelfInstaller.UninstallKeyPath));
        Assert.False(Directory.Exists(_paths.Base));
        Assert.False(_installer.IsInstalled);
    }

    [Fact]
    public void Uninstall_LeavesLinksAlone_WhenAnotherLauncherTookThem()
    {
        _installer.Install(_source, "0.1.0", createDesktopShortcut: false, robloxSizeKb: 0);
        _protocols.Register("\"C:\\Other\\Launcher.exe\" %1", @"C:\Other\Launcher.exe");

        _installer.Uninstall();

        Assert.Equal("\"C:\\Other\\Launcher.exe\" %1", _protocols.GetCommand("roblox-player"));
    }

    [Fact]
    public void Shortcuts_CanBeToggled()
    {
        _installer.Install(_source, "0.1.0", createDesktopShortcut: false, robloxSizeKb: 0);
        Assert.True(_installer.HasStartMenuShortcut);
        Assert.False(_installer.HasDesktopShortcut);

        _installer.SetDesktopShortcut(true);
        _installer.SetStartMenuShortcut(false);

        Assert.True(_installer.HasDesktopShortcut);
        Assert.False(_installer.HasStartMenuShortcut);
        Assert.False(File.Exists(_installer.StartMenuShortcut));
    }

    [Fact]
    public void DisablingShortcut_LeavesForeignShortcutAlone()
    {
        Directory.CreateDirectory(_temp.Combine("Desktop"));
        Shortcut.Create(_installer.DesktopShortcut, _source, "not ours");

        _installer.SetDesktopShortcut(false);

        Assert.True(File.Exists(_installer.DesktopShortcut));
        Assert.False(_installer.HasDesktopShortcut);
    }

    [Fact]
    public void Uninstall_KeepsForeignDesktopShortcut()
    {
        Directory.CreateDirectory(_temp.Combine("Desktop"));
        Shortcut.Create(_installer.DesktopShortcut, _source, "not ours");
        _installer.Install(_source, "0.1.0", createDesktopShortcut: false, robloxSizeKb: 0);

        _installer.Uninstall();

        Assert.True(File.Exists(_installer.DesktopShortcut));
    }
}

public class ProcessPathsTests
{
    [Fact]
    public void TryGetImagePath_ReturnsOwnExecutable() =>
        Assert.Equal(Environment.ProcessPath, ProcessPaths.TryGetImagePath(Environment.ProcessId), ignoreCase: true);

    [Fact]
    public void FindRunningUnder_MatchesByFolder()
    {
        string name = Path.GetFileNameWithoutExtension(Environment.ProcessPath)!;
        string folder = Path.GetDirectoryName(Environment.ProcessPath)!;

        Assert.Contains(Environment.ProcessId, ProcessPaths.FindRunningUnder(name, folder));
        Assert.DoesNotContain(Environment.ProcessId, ProcessPaths.FindRunningUnder(name, Path.GetTempPath()));
    }
}

public class InterProcessLockTests
{
    [Fact]
    public async Task SecondAcquire_WaitsForRelease()
    {
        string name = $"VizstrapTest-{Guid.NewGuid():N}";
        bool waited = false;

        var first = await InterProcessLock.AcquireAsync(name, null, default);
        var second = InterProcessLock.AcquireAsync(name, () => waited = true, default);

        await Task.Delay(300);
        Assert.False(second.IsCompleted);

        first.Dispose();
        using var acquired = await second.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(waited);
    }

    [Fact]
    public async Task WaitingAcquire_CanBeCancelled()
    {
        string name = $"VizstrapTest-{Guid.NewGuid():N}";
        using var first = await InterProcessLock.AcquireAsync(name, null, default);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => InterProcessLock.AcquireAsync(name, null, cancellation.Token).WaitAsync(TimeSpan.FromSeconds(5)));
    }
}
