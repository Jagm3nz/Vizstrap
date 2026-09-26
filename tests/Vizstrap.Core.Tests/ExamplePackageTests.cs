using System.Xml.Linq;
using Vizstrap.Core.Packages;
using Vizstrap.Core.Tests.TestSupport;
using Vizstrap.Effects;

namespace Vizstrap.Core.Tests;

/// <summary>The example package in examples\synthwave-pack installs whole and its effects compile.</summary>
public sealed class ExamplePackageTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private static string ExampleFolder()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            string candidate = Path.Combine(directory.FullName, "examples", "synthwave-pack");

            if (Directory.Exists(candidate))
                return candidate;
        }

        throw new DirectoryNotFoundException("examples\\synthwave-pack isn't above the test's folder.");
    }

    [Fact]
    public void The_example_packs_installs_and_holds_everything()
    {
        string packed = _temp.Combine("Synthwave Pack.vzmod");
        PackageStore.Pack(ExampleFolder(), packed);
        var installed = new PackageStore(_temp.Combine("Packages")).Install(packed);

        Assert.Equal("vizstrap.synthwave", installed.Id);
        Assert.Equal(["files", "effects", "loading-themes", "plugin"], installed.Contents());
        Assert.Equal(["Retro TV", "Synthwave", "Toon outlines"], installed.Effects.Select(effect => effect.Name));
        Assert.All(installed.Effects, effect => Assert.InRange(effect.Parameters.Count, 2, 3));
        Assert.True(File.Exists(Path.Combine(installed.FilesDirectory!, @"content\textures\Cursors\KeyboardMouse\ArrowCursor.png")));
        Assert.True(File.Exists(Path.Combine(installed.Directory, installed.Manifest.Plugin!.Args![^1])));
    }

    [Fact]
    public void The_examples_effects_compile()
    {
        var installed = new PackageStore(_temp.Combine("Packages")).Install(ExampleFolder());

        Assert.Empty(CustomEffectSources.Check(installed.Effects));
    }

    [Fact]
    public void The_examples_loading_theme_is_a_bloxstrap_theme()
    {
        var installed = new PackageStore(_temp.Combine("Packages")).Install(ExampleFolder());
        var theme = XDocument.Load(Path.Combine(Assert.Single(installed.LoadingThemes), "Theme.xml"));

        Assert.Equal("BloxstrapCustomBootstrapper", theme.Root!.Name.LocalName);
        Assert.Contains(theme.Descendants(), element => (string?)element.Attribute("Name") == "StatusText");
        Assert.Contains(theme.Descendants(), element => (string?)element.Attribute("Name") == "PrimaryProgressBar");
    }
}
