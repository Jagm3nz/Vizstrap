using System.IO.Compression;
using Vizstrap.Core.Packages;
using Vizstrap.Core.Tests.TestSupport;
using Vizstrap.Effects;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace Vizstrap.Core.Tests;

public class ModPackageManifestTests
{
    [Fact]
    public void A_manifest_reads_with_its_plugin()
    {
        var manifest = PackageManifest.Parse("""
            { "id": "author.neon-cursor", "name": "Neon cursor", "version": "1.2",
              // comments and trailing commas are fine
              "plugin": { "run": "python", "args": ["plugin\\plugin.py"] }, }
            """);

        Assert.Equal("author.neon-cursor", manifest.Id);
        Assert.Equal("python", manifest.Plugin!.Run);
        Assert.Equal([@"plugin\plugin.py"], manifest.Plugin.Args);
    }

    [Theory]
    [InlineData("""{ "id": "Bad Id", "name": "x" }""", "\"id\"")]
    [InlineData("""{ "id": "../escape", "name": "x" }""", "\"id\"")]
    [InlineData("""{ "id": "ok.id" }""", "\"name\"")]
    [InlineData("""{ "id": "ok.id", "name": "x", "plugin": { "run": " " } }""", "\"run\"")]
    [InlineData("""{ "id": """, "JSON")]
    public void A_bad_manifest_says_what_is_wrong(string json, string mentioned)
    {
        var error = Assert.Throws<InvalidDataException>(() => PackageManifest.Parse(json));
        Assert.Contains(mentioned, error.Message);
    }
}

public sealed class PackageStoreTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly PackageStore _store;

    public PackageStoreTests() => _store = new PackageStore(_temp.Combine("Packages"));

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void A_template_packs_installs_and_shows_what_it_holds()
    {
        string source = _temp.Combine("My mod");
        PackageStore.CreateTemplate(source);
        string packed = _temp.Combine("my-mod" + PackageStore.Extension);

        PackageStore.Pack(source, packed);
        var installed = _store.Install(packed);

        Assert.Equal("your-name.my-mod", installed.Id);
        Assert.Equal(["effects", "pages", "plugin"], installed.Contents());
        var effect = Assert.Single(installed.Effects);
        Assert.Equal("your-name.my-mod/scanlines", effect.Id);
        Assert.Equal("Scanlines", effect.Name);
        Assert.Equal("Strength", Assert.Single(effect.Parameters).Name);
        Assert.Single(_store.List());
    }

    [Fact]
    public void A_zip_of_a_folder_installs_from_inside_it_and_an_update_replaces_it()
    {
        string zip = _temp.Combine("folder.zip");
        Zip(zip, ("wrapped/vizmod.json", """{ "id": "a.b", "name": "One", "version": "1" }"""), ("wrapped/files/content/x.txt", "x"));
        _store.Install(zip);

        string update = _temp.Combine("update.vzmod");
        Zip(update, ("vizmod.json", """{ "id": "a.b", "name": "One", "version": "2" }"""));
        var installed = _store.Install(update);

        Assert.Equal("2", installed.Manifest.Version);
        Assert.Null(installed.FilesDirectory);
        Assert.Single(_store.List());
    }

    [Fact]
    public void Paths_out_of_the_package_and_missing_manifests_are_refused()
    {
        string escaping = _temp.Combine("escaping.vzmod");
        Zip(escaping, ("vizmod.json", """{ "id": "a.b", "name": "x" }"""), ("../../evil.txt", "no"));
        string empty = _temp.Combine("empty.vzmod");
        Zip(empty, ("readme.txt", "no manifest"));

        Assert.Throws<InvalidDataException>(() => _store.Install(escaping));
        Assert.Throws<InvalidDataException>(() => _store.Install(empty));
        Assert.False(File.Exists(_temp.Combine("evil.txt")));
        Assert.Empty(_store.List());
        Assert.Empty(Directory.GetDirectories(_store.Folder));
    }

    [Fact]
    public void A_folder_installs_and_removes()
    {
        string source = _temp.Combine("source");
        Directory.CreateDirectory(Path.Combine(source, "loading-themes", "Dark"));
        File.WriteAllText(Path.Combine(source, "vizmod.json"), """{ "id": "x.themes", "name": "Themes" }""");
        File.WriteAllText(Path.Combine(source, "loading-themes", "Dark", "Theme.xml"), "<BloxstrapCustomBootstrapper Version=\"1\" />");

        var installed = _store.Install(source);
        Assert.Equal(["loading-themes"], installed.Contents());
        Assert.Single(installed.LoadingThemes);

        _store.Remove("x.themes");
        Assert.Empty(_store.List());
        Assert.Throws<ArgumentException>(() => _store.Remove(@"..\Settings"));
    }

    private static void Zip(string path, params (string Name, string Content)[] entries)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);

        foreach (var (name, content) in entries)
        {
            using var writer = new StreamWriter(archive.CreateEntry(name).Open());
            writer.Write(content);
        }
    }
}

/// <summary>Packages' HLSL effects on Windows' software renderer.</summary>
public sealed class CustomEffectTests : IDisposable
{
    private const int Width = 64, Height = 36;

    private readonly TempDirectory _temp = new();
    private readonly ID3D11Device _device;
    private readonly EffectRenderer _renderer;
    private readonly ID3D11Texture2D _output;
    private readonly ID3D11RenderTargetView _outputView;

    public CustomEffectTests()
    {
        D3D11.D3D11CreateDevice(null, DriverType.Warp, DeviceCreationFlags.BgraSupport, [FeatureLevel.Level_11_0], out ID3D11Device? device).CheckError();
        _device = device!;
        _renderer = new EffectRenderer(_device);
        _renderer.Resize(Width, Height);
        _output = _device.CreateTexture2D(new Texture2DDescription(Format.B8G8R8A8_UNorm, Width, Height, 1, 1, BindFlags.RenderTarget));
        _outputView = _device.CreateRenderTargetView(_output);
    }

    public void Dispose()
    {
        _outputView.Dispose();
        _output.Dispose();
        _renderer.Dispose();
        _device.Dispose();
        _temp.Dispose();
    }

    [Fact]
    public void The_templates_effect_compiles()
    {
        string source = _temp.Combine("template");
        PackageStore.CreateTemplate(source);
        var effect = new CustomEffect("t/scanlines", "Scanlines", Path.Combine(source, "effects", "scanlines.hlsl"), []);

        Assert.Empty(CustomEffectSources.Check([effect]));
    }

    [Fact]
    public void An_effect_runs_after_Vizstraps_own_with_its_sliders()
    {
        var errors = _renderer.LoadCustomEffects([("t/invert", "float3 Effect(float2 uv, float3 colour) { return lerp(colour, 1 - colour, Param0); }")]);
        Assert.Empty(errors);

        var pixels = new byte[Width * Height * 4];
        for (int i = 0; i < pixels.Length; i += 4)
            (pixels[i], pixels[i + 1], pixels[i + 2], pixels[i + 3]) = (30, 30, 30, 255);
        _device.ImmediateContext.UpdateSubresource(pixels, _renderer.Scene, 0, Width * 4);

        _renderer.Render(_outputView, EffectParameters.None, 0, [new CustomPass("t/invert", [1f])]);
        int inverted = Blue();
        _renderer.Render(_outputView, EffectParameters.None, 0, [new CustomPass("t/invert", [0f])]);
        int kept = Blue();

        Assert.InRange(inverted, 250, 255);   // inverted in linear light: dark 30 becomes almost white
        Assert.InRange(kept, 29, 31);
    }

    [Fact]
    public void A_broken_effect_reports_the_compiler_message_on_its_own_line()
    {
        var errors = _renderer.LoadCustomEffects([("t/broken", "float3 Effect(float2 uv, float3 colour)\n{\n    return nope;\n}")]);

        string error = Assert.Single(errors).Value;
        Assert.Contains("broken.hlsl(3", error);
        Assert.False(_renderer.HasCustomEffect("t/broken"));
    }

    private unsafe int Blue()
    {
        using var staging = _device.CreateTexture2D(new Texture2DDescription(Format.B8G8R8A8_UNorm, Width, Height, 1, 1, BindFlags.None, ResourceUsage.Staging, CpuAccessFlags.Read));
        _device.ImmediateContext.CopyResource(staging, _output);
        var mapped = _device.ImmediateContext.Map(staging, 0, MapMode.Read);
        int blue = ((byte*)mapped.DataPointer)[(Height / 2) * mapped.RowPitch + Width / 2 * 4];
        _device.ImmediateContext.Unmap(staging, 0);
        return blue;
    }
}
