using System.Diagnostics;
using Vizstrap.Core.Launch;
using Vizstrap.Core.Platform;

namespace Vizstrap.Core.Tests;

/// <summary>Uses its own object names, never Roblox's, so a Roblox running on the machine isn't affected.</summary>
public class SingletonGuardTests
{
    private readonly string _mutexName = $"VizstrapTest_singletonMutex_{Guid.NewGuid():N}";
    private readonly string _eventName = $"VizstrapTest_singletonEvent_{Guid.NewGuid():N}";

    [Fact]
    public void The_guard_keeps_Roblox_from_making_its_event()
    {
        using var guard = SingletonGuard.TryTake(_mutexName, _eventName);

        Assert.NotNull(guard);
        Assert.False(SingletonGuard.IsHeldByRoblox(_eventName));

        // what Roblox does at startup: its event can't be made while a mutex has the name
        Assert.Throws<WaitHandleCannotBeOpenedException>(() => new EventWaitHandle(false, EventResetMode.AutoReset, _eventName));
    }

    [Fact]
    public void No_guard_while_Roblox_holds_its_event()
    {
        using var robloxEvent = new EventWaitHandle(false, EventResetMode.AutoReset, _eventName);

        Assert.True(SingletonGuard.IsHeldByRoblox(_eventName));
        Assert.Null(SingletonGuard.TryTake(_mutexName, _eventName));
    }

    [Fact]
    public void Several_launches_share_the_names_until_the_last_lets_go()
    {
        var first = SingletonGuard.TryTake(_mutexName, _eventName);
        var second = SingletonGuard.TryTake(_mutexName, _eventName);

        Assert.NotNull(first);
        Assert.NotNull(second);

        first!.Dispose();
        Assert.Throws<WaitHandleCannotBeOpenedException>(() => new EventWaitHandle(false, EventResetMode.AutoReset, _eventName));

        second!.Dispose();
        using var robloxEvent = new EventWaitHandle(false, EventResetMode.AutoReset, _eventName);
        Assert.True(SingletonGuard.IsHeldByRoblox(_eventName));
    }

    [Fact]
    public void Nothing_named_means_not_held() => Assert.False(SingletonGuard.IsHeldByRoblox(_eventName));
}

public class RobloxInstancesTests
{
    [Theory]
    [InlineData(@"""C:\Roblox\Versions\version-1\RobloxPlayerBeta.exe"" --launch-to-tray", true)]
    [InlineData(@"""C:\Roblox\Versions\version-1\RobloxPlayerBeta.exe"" --LAUNCH-TO-TRAY", true)]
    [InlineData(@"""C:\Roblox\Versions\version-1\RobloxPlayerBeta.exe"" roblox-player:1+launchmode:play", false)]
    [InlineData(@"""C:\Roblox\Versions\version-1\RobloxPlayerBeta.exe""", false)]
    [InlineData(null, false)]
    public void Tray_players_are_told_apart(string? commandLine, bool expected) =>
        Assert.Equal(expected, RobloxInstances.IsTrayCommandLine(commandLine));

    [Fact]
    public void A_process_command_line_can_be_read()
    {
        using var self = Process.GetCurrentProcess();

        string? commandLine = ProcessPaths.TryGetCommandLine(self.Id);

        Assert.NotNull(commandLine);
        Assert.Contains(Path.GetFileNameWithoutExtension(Environment.ProcessPath!), commandLine, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Running_is_checked_without_handles()
    {
        using var self = Process.GetCurrentProcess();

        Assert.True(RobloxInstances.IsRunning(self.Id));
        Assert.False(RobloxInstances.IsRunning(int.MaxValue - 1));
    }
}
