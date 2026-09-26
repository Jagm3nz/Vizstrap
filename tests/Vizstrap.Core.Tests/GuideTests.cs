using System.Text.RegularExpressions;
using Vizstrap.Core.Packages;
using Vizstrap.Core.Tests.TestSupport;
using Vizstrap.Effects;

namespace Vizstrap.Core.Tests;

/// <summary>The examples in the guides (docs\) work as written.</summary>
public sealed partial class GuideTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private static string Guide(string name)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            string candidate = Path.Combine(directory.FullName, "docs", name);

            if (File.Exists(candidate))
                return File.ReadAllText(candidate);
        }

        throw new FileNotFoundException(name);
    }

    [GeneratedRegex("```(\\w+)\\r?\\n(.*?)```", RegexOptions.Singleline)]
    private static partial Regex CodeBlock();

    private static IEnumerable<string> Blocks(string guide, string language) =>
        CodeBlock().Matches(guide).Where(match => match.Groups[1].Value == language).Select(match => match.Groups[2].Value);

    [Fact]
    public void Every_shader_in_the_shader_guide_compiles()
    {
        var shaders = Blocks(Guide("making-shaders.md"), "hlsl").ToList();
        Assert.True(shaders.Count >= 8);

        var effects = shaders.Select((source, index) =>
        {
            string file = _temp.Combine($"example{index}.hlsl");
            File.WriteAllText(file, source);
            return new CustomEffect($"guide/example{index}", $"Example {index}", file, []);
        }).ToList();

        Assert.Empty(CustomEffectSources.Check(effects));
    }

    [Fact]
    public void Every_tab_in_the_package_guide_reads()
    {
        var tabs = Blocks(Guide("making-a-package.md"), "xml").Where(xml => xml.TrimStart().StartsWith("<VizstrapPage", StringComparison.Ordinal)).ToList();
        Assert.True(tabs.Count >= 2);

        foreach (string xml in tabs)
            PackagePages.Parse("guide.example", "Guide", "tab.xml", xml, "en");
    }
}
