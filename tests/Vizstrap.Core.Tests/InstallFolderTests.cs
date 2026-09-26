using Vizstrap.Core.Platform;
using Vizstrap.Core.Tests.TestSupport;

namespace Vizstrap.Core.Tests;

public sealed class InstallFolderTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Theory]
    [InlineData(@"D:\Games", @"D:\Games\Vizstrap")]
    [InlineData(@"D:\Games\", @"D:\Games\Vizstrap")]
    [InlineData(@"D:\Games\Vizstrap", @"D:\Games\Vizstrap")]
    [InlineData(@"D:\Games\vizstrap\", @"D:\Games\vizstrap")]
    [InlineData(@"D:\", @"D:\Vizstrap")]
    public void A_picked_folder_always_gets_a_Vizstrap_folder_of_its_own(string picked, string expected)
    {
        Assert.Equal(expected, InstallFolder.FromPicked(picked));
    }

    [Fact]
    public void A_missing_or_empty_folder_is_fine()
    {
        string empty = _temp.Combine("Empty", "Vizstrap");
        Directory.CreateDirectory(empty);

        Assert.Equal(InstallFolderProblem.None, InstallFolder.Check(_temp.Combine("Games", "Vizstrap")));
        Assert.Equal(InstallFolderProblem.None, InstallFolder.Check(empty));
        Assert.False(Directory.Exists(_temp.Combine("Games")));
        Assert.Empty(Directory.GetDirectories(_temp.Path, ".vizstrap-*"));
    }

    [Fact]
    public void A_folder_with_someone_elses_files_is_refused_but_Vizstraps_own_is_fine()
    {
        string foreign = _temp.Combine("Photos", "Vizstrap");
        Directory.CreateDirectory(foreign);
        File.WriteAllText(Path.Combine(foreign, "holiday.jpg"), "");
        string previous = _temp.Combine("Old", "Vizstrap");
        Directory.CreateDirectory(previous);
        File.WriteAllText(Path.Combine(previous, "Settings.json"), "{}");

        Assert.Equal(InstallFolderProblem.NotEmpty, InstallFolder.Check(foreign));
        Assert.Equal(InstallFolderProblem.None, InstallFolder.Check(previous));
    }

    [Fact]
    public void The_installed_folder_comes_from_the_apps_and_features_entry_while_Vizstrap_is_there()
    {
        using var registry = new TestRegistry();
        var paths = new VizstrapPaths(_temp.Combine("Games", "Vizstrap"));
        var installer = new SelfInstaller(paths, new ProtocolRegistrar(registry.Classes), registry.Root, _temp.Combine("StartMenu"), _temp.Combine("Desktop"));
        string source = _temp.Combine("Downloads", "Vizstrap.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        File.WriteAllText(source, "fake exe");

        Assert.Null(InstallFolder.Installed(registry.Root));

        installer.Install(source, "1.3.0", createDesktopShortcut: false, robloxSizeKb: 0, createStartMenuShortcut: false);

        Assert.Equal(paths.Base, InstallFolder.Installed(registry.Root));
        Assert.False(File.Exists(installer.StartMenuShortcut));
        Assert.False(File.Exists(installer.DesktopShortcut));

        File.Delete(paths.InstalledExecutable);
        Assert.Null(InstallFolder.Installed(registry.Root));
    }
}
