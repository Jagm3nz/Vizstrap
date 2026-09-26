using Vizstrap.Core.FastFlags;
using Vizstrap.Core.Mods;
using Vizstrap.Core.Storage;
using Vizstrap.Core.Tests.TestSupport;

namespace Vizstrap.Core.Tests;

public class FastFlagFileTests
{
    [Fact]
    public void Parse_TurnsEveryValueIntoBloxstrapStyleStrings()
    {
        var flags = FastFlagFile.Parse("""
            {
              "FFlagDebugSkyGray": true,
              "DFIntTextureQualityOverride": 2,
              "FStringSomething": "text",
              "FFlagAlreadyString": "False",
            }
            """);

        Assert.Equal("True", flags["FFlagDebugSkyGray"]);
        Assert.Equal("2", flags["DFIntTextureQualityOverride"]);
        Assert.Equal("text", flags["FStringSomething"]);
        Assert.Equal("False", flags["FFlagAlreadyString"]);
    }

    [Theory]
    [InlineData("[1, 2]")]
    [InlineData("\"just a string\"")]
    public void Parse_RejectsAnythingButAnObject(string json) =>
        Assert.Throws<FormatException>(() => FastFlagFile.Parse(json));

    [Fact]
    public void SaveThenLoad_RoundTrips_AndNoFlagsMeansNoFile()
    {
        using var temp = new TempDirectory();
        var file = new FastFlagFile(new VizstrapPaths(temp.Path));

        file.Save(new Dictionary<string, string> { ["FIntDebugForceMSAASamples"] = "4" });
        Assert.Equal("4", file.Load()["FIntDebugForceMSAASamples"]);
        Assert.EndsWith(@"Modifications\ClientSettings\ClientAppSettings.json", file.FilePath);

        file.Save(new Dictionary<string, string>());
        Assert.False(File.Exists(file.FilePath));
        Assert.Empty(file.Load());
    }

    [Fact]
    public void Load_BrokenFile_GivesNoFlags()
    {
        using var temp = new TempDirectory();
        var file = new FastFlagFile(new VizstrapPaths(temp.Path));
        Directory.CreateDirectory(Path.GetDirectoryName(file.FilePath)!);
        File.WriteAllText(file.FilePath, "{ not json");

        Assert.Empty(file.Load());
    }

    [Theory]
    [InlineData("FFlagDebugSkyGray", FlagType.Bool)]
    [InlineData("DFFlagDisableDPIScale", FlagType.Bool)]
    [InlineData("SFFlagSomething", FlagType.Bool)]
    [InlineData("FIntDebugForceMSAASamples", FlagType.Int)]
    [InlineData("DFIntTextureQualityOverride", FlagType.Int)]
    [InlineData("FStringX", FlagType.String)]
    [InlineData("DFLogNetwork", FlagType.Log)]
    [InlineData("SkyGray", null)]
    [InlineData("FFlag", null)]
    [InlineData("FFlag Space", null)]
    public void TypeOf_ReadsThePrefix(string name, FlagType? expected) => Assert.Equal(expected, FastFlagFile.TypeOf(name));

    [Theory]
    [InlineData("FFlagDebugSkyGray", "true", null)]
    [InlineData("FFlagDebugSkyGray", "yes", "bool")]
    [InlineData("FIntFRMMaxGrassDistance", "0", null)]
    [InlineData("FIntFRMMaxGrassDistance", "far", "int")]
    [InlineData("FStringAnything", "far", null)]
    [InlineData("NotAFlag", "1", "name")]
    public void ValueProblem_ChecksValueAgainstType(string name, string value, string? expected) =>
        Assert.Equal(expected, FastFlagFile.ValueProblem(name, value));

    [Fact]
    public void Normalize_WritesBoolsLikeRoblox() =>
        Assert.Equal("True", FastFlagFile.Normalize("FFlagDebugSkyGray", "true"));
}

public class FastFlagAllowlistTests
{
    [Fact]
    public void HasTheEighteenFlagsFromRobloxsAnnouncement()
    {
        Assert.Equal(18, FastFlagAllowlist.All.Count);
        Assert.Equal(4, FastFlagAllowlist.All.Count(flag => flag.Category == FlagCategory.Geometry));
        Assert.Single(FastFlagAllowlist.All, flag => flag.Category == FlagCategory.UserInterface);
    }

    [Theory]
    [InlineData("FIntDebugForceMSAASamples", true)]
    [InlineData("fintdebugforcemsaasamples", true)]
    [InlineData("DFIntTaskSchedulerTargetFps", false)]
    public void IsAllowed(string name, bool expected) => Assert.Equal(expected, FastFlagAllowlist.IsAllowed(name));

    [Fact]
    public void EveryPresetUsesOnlyAllowlistedFlags()
    {
        var flags = new Dictionary<string, string>();
        FastFlagPresets.SetMsaa(flags, MsaaMode.X4);
        FastFlagPresets.SetDisableDpiScale(flags, true);
        FastFlagPresets.SetTextureQuality(flags, TextureQuality.Level2);
        FastFlagPresets.SetExclusiveFullscreen(flags, true);
        FastFlagPresets.SetGraphicsApi(flags, GraphicsApi.Vulkan);
        FastFlagPresets.SetQualityLevel(flags, 10);
        FastFlagPresets.SetGraySky(flags, true);
        FastFlagPresets.SetNoGrass(flags, true);
        FastFlagPresets.SetGeometryDetail(flags, GeometryDetail.Low);
        FastFlagPresets.SetPauseVoxelizer(flags, true);
        FastFlagPresets.SetStillGrass(flags, true);
        FastFlagPresets.ApplyPerformanceProfile(flags);
        FastFlagPresets.ApplyQualityProfile(flags);

        Assert.All(flags.Keys, name => Assert.True(FastFlagAllowlist.IsAllowed(name), name));
    }
}

public class FastFlagPresetTests
{
    private readonly Dictionary<string, string> _flags = new();

    [Theory]
    [InlineData(MsaaMode.X1, "1")]
    [InlineData(MsaaMode.X2, "2")]
    [InlineData(MsaaMode.X4, "4")]
    public void Msaa(MsaaMode mode, string value)
    {
        FastFlagPresets.SetMsaa(_flags, mode);
        Assert.Equal(value, _flags["FIntDebugForceMSAASamples"]);
        Assert.Equal(mode, FastFlagPresets.GetMsaa(_flags));

        FastFlagPresets.SetMsaa(_flags, MsaaMode.Automatic);
        Assert.Empty(_flags);
    }

    [Fact]
    public void TextureQuality_NeedsOverrideAndLevel()
    {
        FastFlagPresets.SetTextureQuality(_flags, TextureQuality.Level0);

        Assert.Equal("True", _flags["DFFlagTextureQualityOverrideEnabled"]);
        Assert.Equal("0", _flags["DFIntTextureQualityOverride"]);
        Assert.Equal(TextureQuality.Level0, FastFlagPresets.GetTextureQuality(_flags));

        _flags["DFFlagTextureQualityOverrideEnabled"] = "False";
        Assert.Equal(TextureQuality.Automatic, FastFlagPresets.GetTextureQuality(_flags));

        FastFlagPresets.SetTextureQuality(_flags, TextureQuality.Automatic);
        Assert.Empty(_flags);
    }

    [Fact]
    public void GraphicsApi_IsExclusive()
    {
        FastFlagPresets.SetGraphicsApi(_flags, GraphicsApi.Direct3D11);
        FastFlagPresets.SetGraphicsApi(_flags, GraphicsApi.OpenGL);

        Assert.Equal(GraphicsApi.OpenGL, FastFlagPresets.GetGraphicsApi(_flags));
        Assert.Equal(["FFlagDebugGraphicsPreferOpenGL"], _flags.Keys);
    }

    [Fact]
    public void ExclusiveFullscreen_TurnsRobloxsOwnAltEnterOff()
    {
        FastFlagPresets.SetExclusiveFullscreen(_flags, true);

        Assert.Equal("False", _flags["FFlagHandleAltEnterFullscreenManually"]);
        Assert.True(FastFlagPresets.GetExclusiveFullscreen(_flags));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(21)]
    public void QualityLevel_InRange(int level)
    {
        FastFlagPresets.SetQualityLevel(_flags, level);
        Assert.Equal(level, FastFlagPresets.GetQualityLevel(_flags));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(22)]
    [InlineData(null)]
    public void QualityLevel_OutOfRange_MeansAutomatic(int? level)
    {
        FastFlagPresets.SetQualityLevel(_flags, level);
        Assert.Null(FastFlagPresets.GetQualityLevel(_flags));
        Assert.Empty(_flags);
    }

    [Fact]
    public void NoGrass_ZeroesBothDistances()
    {
        FastFlagPresets.SetNoGrass(_flags, true);
        Assert.Equal(("0", "0"), (_flags["FIntFRMMinGrassDistance"], _flags["FIntFRMMaxGrassDistance"]));
        Assert.True(FastFlagPresets.GetNoGrass(_flags));

        _flags["FIntFRMMaxGrassDistance"] = "100";
        Assert.False(FastFlagPresets.GetNoGrass(_flags));
    }

    [Fact]
    public void BoolPresets_AcceptLowercaseFromHandEditedFiles()
    {
        _flags["FFlagDebugSkyGray"] = "true";
        _flags["DFFlagDisableDPIScale"] = "TRUE";

        Assert.True(FastFlagPresets.GetGraySky(_flags));
        Assert.True(FastFlagPresets.GetDisableDpiScale(_flags));
    }
}

public sealed class FastFlagModTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void TurningTheManagerOff_TakesTheFlagsBackOutOfRoblox()
    {
        var paths = new VizstrapPaths(_temp.Combine("Vizstrap"));
        var state = new JsonStore<State>(paths.StateFile);
        string version = paths.VersionDirectory("version-1");
        Directory.CreateDirectory(version);
        new FastFlagFile(paths).Save(new Dictionary<string, string> { ["FFlagDebugSkyGray"] = "True" });
        string applied = Path.Combine(version, FastFlagFile.RelativePath);

        new ModApplier(paths, state).Apply(version, includeFastFlags: true);
        Assert.True(File.Exists(applied));

        new ModApplier(paths, state).Apply(version, includeFastFlags: false);
        Assert.False(File.Exists(applied));
        Assert.True(File.Exists(new FastFlagFile(paths).FilePath), "the flags themselves are kept for later");
    }
}

public class NewerFastFlagPresetTests
{
    private readonly Dictionary<string, string> _flags = new();

    [Theory]
    [InlineData(GeometryDetail.Low, "0")]
    [InlineData(GeometryDetail.High, "500")]
    public void Geometry_detail_sets_all_four_switching_distances(GeometryDetail detail, string first)
    {
        FastFlagPresets.SetGeometryDetail(_flags, detail);

        Assert.Equal(4, _flags.Count);
        Assert.Equal(first, _flags["DFIntCSGLevelOfDetailSwitchingDistance"]);
        Assert.Equal(detail, FastFlagPresets.GetGeometryDetail(_flags));

        FastFlagPresets.SetGeometryDetail(_flags, GeometryDetail.Automatic);
        Assert.Empty(_flags);
    }

    [Fact]
    public void Distances_set_by_hand_read_as_automatic()
    {
        _flags["DFIntCSGLevelOfDetailSwitchingDistance"] = "0";

        Assert.Equal(GeometryDetail.Automatic, FastFlagPresets.GetGeometryDetail(_flags));
    }

    [Fact]
    public void Lighting_and_grass_switches_add_and_remove_their_flag()
    {
        FastFlagPresets.SetPauseVoxelizer(_flags, true);
        FastFlagPresets.SetStillGrass(_flags, true);

        Assert.Equal("True", _flags["DFFlagDebugPauseVoxelizer"]);
        Assert.Equal("0", _flags["FIntGrassMovementReducedMotionFactor"]);
        Assert.True(FastFlagPresets.GetPauseVoxelizer(_flags));
        Assert.True(FastFlagPresets.GetStillGrass(_flags));

        FastFlagPresets.SetPauseVoxelizer(_flags, false);
        FastFlagPresets.SetStillGrass(_flags, false);
        Assert.Empty(_flags);
    }

    [Fact]
    public void The_performance_profile_turns_everything_expensive_down()
    {
        FastFlagPresets.ApplyPerformanceProfile(_flags);

        Assert.Equal(MsaaMode.X1, FastFlagPresets.GetMsaa(_flags));
        Assert.Equal(TextureQuality.Level0, FastFlagPresets.GetTextureQuality(_flags));
        Assert.Equal(1, FastFlagPresets.GetQualityLevel(_flags));
        Assert.True(FastFlagPresets.GetNoGrass(_flags));
        Assert.Equal(GeometryDetail.Low, FastFlagPresets.GetGeometryDetail(_flags));
        Assert.True(FastFlagPresets.GetPauseVoxelizer(_flags));
    }

    [Fact]
    public void The_quality_profile_undoes_the_performance_one_and_keeps_other_choices()
    {
        FastFlagPresets.SetGraphicsApi(_flags, GraphicsApi.Vulkan);
        FastFlagPresets.ApplyPerformanceProfile(_flags);

        FastFlagPresets.ApplyQualityProfile(_flags);

        Assert.Equal(MsaaMode.X4, FastFlagPresets.GetMsaa(_flags));
        Assert.Equal(TextureQuality.Level3, FastFlagPresets.GetTextureQuality(_flags));
        Assert.Equal(FastFlagPresets.MaxQualityLevel, FastFlagPresets.GetQualityLevel(_flags));
        Assert.False(FastFlagPresets.GetNoGrass(_flags));
        Assert.Equal(GeometryDetail.High, FastFlagPresets.GetGeometryDetail(_flags));
        Assert.False(FastFlagPresets.GetPauseVoxelizer(_flags));
        Assert.Equal(GraphicsApi.Vulkan, FastFlagPresets.GetGraphicsApi(_flags));
    }
}
