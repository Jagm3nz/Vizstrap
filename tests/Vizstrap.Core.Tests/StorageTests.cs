using Vizstrap.Core.Install;
using Vizstrap.Core.Storage;
using Vizstrap.Core.Tests.TestSupport;

namespace Vizstrap.Core.Tests;

public class JsonStoreTests
{
    [Fact]
    public void SaveThenLoad_RoundTrips()
    {
        using var temp = new TempDirectory();
        string file = temp.Combine("sub", "State.json");

        var store = new JsonStore<State>(file);
        store.Value.PlayerVersion = "version-1";
        store.Value.PackageHashes["a.zip"] = "abc";
        store.Save();

        var reloaded = new JsonStore<State>(file);
        reloaded.Load();

        Assert.Equal("version-1", reloaded.Value.PlayerVersion);
        Assert.Equal("abc", reloaded.Value.PackageHashes["a.zip"]);
        Assert.False(File.Exists(file + ".tmp"));
    }

    [Fact]
    public void Load_MissingFile_GivesDefaults()
    {
        using var temp = new TempDirectory();

        var store = new JsonStore<Settings>(temp.Combine("Settings.json"));
        store.Load();

        Assert.Null(store.Value.Language);
    }

    [Fact]
    public void Load_CorruptFile_GivesDefaults()
    {
        using var temp = new TempDirectory();
        string file = temp.Combine("Settings.json");
        File.WriteAllText(file, "{ not json");

        var store = new JsonStore<Settings>(file);
        store.Load();

        Assert.Null(store.Value.Language);
    }
}

public class SpeedMeterTests
{
    [Fact]
    public void AveragesOverWindow()
    {
        var now = TimeSpan.Zero;
        var meter = new SpeedMeter(TimeSpan.FromSeconds(3), () => now);

        now = TimeSpan.FromSeconds(1);
        meter.Add(1000);
        now = TimeSpan.FromSeconds(2);
        meter.Add(1000);

        Assert.Equal(1000, meter.BytesPerSecond, precision: 3);
    }

    [Fact]
    public void ForgetsOldSamples()
    {
        var now = TimeSpan.Zero;
        var meter = new SpeedMeter(TimeSpan.FromSeconds(2), () => now);

        now = TimeSpan.FromSeconds(1);
        meter.Add(100_000);

        for (int second = 2; second <= 10; second++)
        {
            now = TimeSpan.FromSeconds(second);
            meter.Add(10);
        }

        Assert.InRange(meter.BytesPerSecond, 9, 11);
    }

    [Fact]
    public void NeverNegative()
    {
        var now = TimeSpan.Zero;
        var meter = new SpeedMeter(TimeSpan.FromSeconds(3), () => now);

        now = TimeSpan.FromSeconds(1);
        meter.Add(500);
        meter.Add(-800);

        Assert.Equal(0, meter.BytesPerSecond);
    }
}
