using Vizstrap.Core.Integrations;
using Vizstrap.Core.Storage;
using Vizstrap.Core.Tests.TestSupport;

namespace Vizstrap.Core.Tests;

public class IntegrationTests
{
    [Fact]
    public void Integrations_start_in_their_own_folder_with_arguments_on_one_line()
    {
        var info = CustomIntegrations.StartInfoFor(new CustomIntegration
        {
            Location = @"C:\Tools\Recorder\recorder.exe",
            LaunchArgs = "--start\r\n--quiet\n--fps 60",
        });

        Assert.Equal(@"C:\Tools\Recorder\recorder.exe", info.FileName);
        Assert.Equal("--start --quiet --fps 60", info.Arguments);
        Assert.Equal(@"C:\Tools\Recorder", info.WorkingDirectory);
        Assert.True(info.UseShellExecute);
    }

    [Fact]
    public void Integrations_without_a_program_or_that_fail_to_start_are_skipped()
    {
        var autoClose = CustomIntegrations.StartAll(
        [
            new CustomIntegration { Name = "empty", Location = "  " },
            new CustomIntegration { Name = "missing", Location = @"C:\does\not\exist\nothing.exe" },
        ]);

        Assert.Empty(autoClose);
    }

    [Fact]
    public void Integration_settings_default_like_Bloxstrap_and_survive_a_save()
    {
        using var directory = new TempDirectory();
        var store = new JsonStore<Settings>(directory.Combine("Settings.json"));
        store.Load();

        var settings = store.Value;
        Assert.True(settings.EnableActivityTracking);
        Assert.True(settings.UseDiscordRichPresence);
        Assert.False(settings.ShowServerLocation);
        Assert.False(settings.DisableDesktopApp);
        Assert.False(settings.AllowActivityJoining);
        Assert.False(settings.ShowAccountOnProfile);
        Assert.Empty(settings.CustomIntegrations);

        settings.ShowServerLocation = true;
        settings.CustomIntegrations.Add(new CustomIntegration { Name = "OBS", Location = @"C:\obs\obs64.exe", LaunchArgs = "--startrecording", AutoClose = false });
        store.Save();

        var reloaded = new JsonStore<Settings>(store.FilePath);
        reloaded.Load();

        Assert.True(reloaded.Value.ShowServerLocation);
        var integration = Assert.Single(reloaded.Value.CustomIntegrations);
        Assert.True(integration.SameAs(settings.CustomIntegrations[0]));
    }

    [Fact]
    public void Clones_are_independent_copies()
    {
        var original = new CustomIntegration { Name = "A", Location = "a.exe" };
        var clone = original.Clone();

        clone.Name = "B";

        Assert.Equal("A", original.Name);
        Assert.False(clone.SameAs(original));
    }
}
