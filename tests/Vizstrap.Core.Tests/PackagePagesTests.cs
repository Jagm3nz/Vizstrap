using Vizstrap.Core.Packages;
using Vizstrap.Core.Tests.TestSupport;

namespace Vizstrap.Core.Tests;

public class PackagePagesTests
{
    private static PackagePage Parse(string xml, string language = "en") => PackagePages.Parse("author.mod", "My mod", "tab.xml", xml, language);

    [Fact]
    public void A_tab_reads_every_element()
    {
        var page = Parse("""
            <VizstrapPage Title="My mod" Icon="Sparkle24" Description="About it.">
              <Section Title="Options" />
              <Toggle Id="greet" Title="Greet" Description="Say hello." Default="true" />
              <TextBox Id="greeting" Title="Greeting" Default="Hi" MaxLength="20" VisibleWhen="greet" />
              <Slider Id="volume" Title="Volume" Min="0" Max="100" Step="5" Default="50" />
              <Choice Id="style" Title="Style" Default="retro">
                <Option Value="neon" Title="Neon" />
                <Option Value="retro" Title="Retro" />
              </Choice>
              <Text Muted="true">A note.</Text>
              <Link Title="Site" Url="https://example.com/page" />
              <PlaytimeChart VisibleWhen="!greet" />
            </VizstrapPage>
            """);

        Assert.Equal(("My mod", "Sparkle24", "About it."), (page.Title, page.Icon, page.Description));
        Assert.Collection(page.Elements,
            element => Assert.Equal("Options", Assert.IsType<SectionElement>(element).Title),
            element => Assert.True(Assert.IsType<ToggleElement>(element).Default),
            element => Assert.Equal(("Hi", 20, "greet"), (Assert.IsType<TextBoxElement>(element).Default, ((TextBoxElement)element).MaxLength, element.VisibleWhen)),
            element => Assert.Equal((0.0, 100.0, 5.0, 50.0), (((SliderElement)element).Min, ((SliderElement)element).Max, ((SliderElement)element).Step, ((SliderElement)element).Default)),
            element => Assert.Equal(["neon", "retro"], Assert.IsType<ChoiceElement>(element).Options.Select(option => option.Value)),
            element => Assert.True(Assert.IsType<TextElement>(element).Muted),
            element => Assert.Equal("https://example.com/page", Assert.IsType<LinkElement>(element).Url),
            element => Assert.Equal(("PlaytimeChart", "!greet"), (Assert.IsType<WidgetElement>(element).Name, element.VisibleWhen)));
    }

    [Theory]
    [InlineData("pl", "Mój mod")]
    [InlineData("pl-PL", "Mój mod")]
    [InlineData("pt-BR", "Meu mod")]
    [InlineData("de", "My mod")]
    public void Text_comes_in_the_players_language_when_the_tab_has_it(string language, string title)
    {
        var page = Parse("""<VizstrapPage Title="My mod" Title.pl="Mój mod" Title.pt="Meu mod" />""", language);

        Assert.Equal(title, page.Title);
    }

    [Theory]
    [InlineData("<Frame />", "unknown element")]
    [InlineData("<Toggle Title=\"A\" />", "Id")]
    [InlineData("<Toggle Id=\"a\" Title=\"A\" /><Slider Id=\"a\" Title=\"B\" />", "used twice")]
    [InlineData("<Slider Id=\"a\" Title=\"A\" Min=\"5\" Max=\"1\" />", "Max must be more than Min")]
    [InlineData("<Choice Id=\"a\" Title=\"A\" Default=\"x\"><Option Value=\"y\" /></Choice>", "isn't one of its options")]
    [InlineData("<Link Title=\"A\" Url=\"http://example.com\" />", "https")]
    [InlineData("<Text VisibleWhen=\"nothing\">Hi</Text>", "doesn't name a Toggle")]
    public void A_broken_tab_says_what_is_wrong(string elements, string mentioned)
    {
        var error = Assert.Throws<InvalidDataException>(() => Parse($"<VizstrapPage Title=\"T\">{elements}</VizstrapPage>"));

        Assert.Contains(mentioned, error.Message);
    }

    [Fact]
    public void An_unknown_element_names_its_line()
    {
        var error = Assert.Throws<InvalidDataException>(() => Parse("<VizstrapPage Title=\"T\">\n  <Section Title=\"S\" />\n  <Button />\n</VizstrapPage>"));

        Assert.StartsWith("Line 3, <Button>", error.Message);
    }

    [Fact]
    public void A_program_gets_typed_values_with_defaults_for_what_wasnt_set()
    {
        var page = Parse("""
            <VizstrapPage Title="T">
              <Toggle Id="greet" Title="Greet" Default="true" />
              <Slider Id="volume" Title="Volume" Min="0" Max="10" Default="5" />
              <TextBox Id="greeting" Title="Greeting" Default="Hi" />
            </VizstrapPage>
            """);

        var values = PackagePages.Values([page], new Dictionary<string, string> { ["greet"] = "false", ["volume"] = "99" });

        Assert.Equal(false, values["greet"]);
        Assert.Equal(10.0, values["volume"]);
        Assert.Equal("Hi", values["greeting"]);
    }
}

public sealed class PackageTabsTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private static string Example(string name)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            string candidate = Path.Combine(directory.FullName, "examples", name);

            if (Directory.Exists(candidate))
                return candidate;
        }

        throw new DirectoryNotFoundException(name);
    }

    [Fact]
    public void The_templates_tab_reads_and_shows_in_its_contents()
    {
        string folder = _temp.Combine("template");
        PackageStore.CreateTemplate(folder);
        var installed = new PackageStore(_temp.Combine("Packages")).Install(folder);

        Assert.Contains(InstalledPackage.PagesFolder, installed.Contents());
        var page = Assert.Single(installed.Pages("en"));
        Assert.Equal(["greet", "greeting", "volume", "style"], page.Inputs.Select(input => input.Id));
    }

    [Fact]
    public void The_activity_package_is_a_tab_of_activity_widgets_in_English_and_Polish()
    {
        var installed = new PackageStore(_temp.Combine("Packages")).Install(Example("activity-pack"));

        Assert.Equal("vizstrap.activity", installed.Id);
        Assert.Equal([InstalledPackage.PagesFolder], installed.Contents());
        Assert.Equal("Activity", Assert.Single(installed.Pages("en")).Title);

        var page = Assert.Single(installed.Pages("pl"));
        Assert.Equal("Aktywność", page.Title);
        Assert.Equal(PackagePages.Widgets.ToHashSet(), page.Elements.OfType<WidgetElement>().Select(widget => widget.Name).ToHashSet());
    }

    [Fact]
    public void A_broken_tab_is_reported_and_left_out()
    {
        string folder = _temp.Combine("broken");
        Directory.CreateDirectory(Path.Combine(folder, "pages"));
        File.WriteAllText(Path.Combine(folder, "vizmod.json"), """{ "id": "author.broken", "name": "Broken" }""");
        File.WriteAllText(Path.Combine(folder, "pages", "a.xml"), "<VizstrapPage Title=\"Fine\" />");
        File.WriteAllText(Path.Combine(folder, "pages", "b.xml"), "<VizstrapPage><Nope /></VizstrapPage>");
        var installed = new PackageStore(_temp.Combine("Packages")).Install(folder);
        var broken = new List<string>();

        var pages = installed.Pages("en", (file, _) => broken.Add(file));

        Assert.Equal("Fine", Assert.Single(pages).Title);
        Assert.Equal(["b.xml"], broken);
    }
}
