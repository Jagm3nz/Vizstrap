using Vizstrap.Core.Activity;
using Vizstrap.Core.Tests.TestSupport;

namespace Vizstrap.Core.Tests;

public class PlaytimeLogTests
{
    private const string Job1 = "d9758c4f-19b0-4512-9118-4306aec5fdd1";
    private const string Job2 = "6599cafc-8bdc-47d1-85d5-f44e98bc69af";

    /// <summary>A log line as Roblox 0.740 writes it, <paramref name="second"/> seconds after 12:00:00 UTC.</summary>
    internal static string Line(int second, string message) =>
        $"2026-09-24T{12 + second / 3600:00}:{second / 60 % 60:00}:{second % 60:00}.816Z,{second}.816669,1aac,6 {message}";

    internal static IEnumerable<string> Join(int second, string job, long place, long universe) =>
    [
        Line(second, $"[FLog::Output] ! Joining game '{job}' place {place} at 10.34.7.132"),
        Line(second, $"[FLog::GameJoinLoadTime] Report game_join_loadtime: placeid:{place}, join_time:0.446, universeid:{universe}, referral_page:, sid:9f1bca76, clienttime:1790265518.5, userid:42, "),
        Line(second + 1, "[FLog::Network] Replicator created: 000000004D876F30"),
    ];

    internal static string Leave(int second) => Line(second, "[FLog::SingleSurfaceApp] leaveUGCGameInternal");

    internal static string Disconnect(int second) => Line(second, "[FLog::Network] Time to disconnect replication data: 0.784300");

    private static readonly DateTime Noon = new(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Each_server_is_a_stay_from_joining_to_leaving()
    {
        var sessions = PlaytimeLog.Read([.. Join(0, Job1, 1, 11), Leave(600), Disconnect(600), .. Join(700, Job2, 2, 22), Leave(1300), Disconnect(1300)]);

        Assert.Equal(2, sessions.Count);
        Assert.Equal((1L, 11L, Job1), (sessions[0].PlaceId, sessions[0].UniverseId, sessions[0].JobId));
        Assert.InRange(sessions[0].Length.TotalSeconds, 598, 600);
        Assert.Equal(22, sessions[1].UniverseId);
        Assert.InRange(sessions[1].Length.TotalSeconds, 598, 600);
    }

    [Fact]
    public void A_stay_still_open_when_the_log_ends_ends_at_its_last_line()
    {
        var sessions = PlaytimeLog.Read([.. Join(0, Job1, 1, 11), Line(1800, "[FLog::Output] still playing")]);

        var session = Assert.Single(sessions);
        Assert.Equal(Noon.AddSeconds(1800).AddMilliseconds(816), session.Left);
    }

    [Fact]
    public void A_log_without_games_has_no_stays()
    {
        Assert.Empty(PlaytimeLog.Read([Line(0, "[FLog::Output] AppState/TrayMode"), Line(5, "[FLog::Output] bye")]));
    }
}

public sealed class PlaytimeStoreTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private static PlaySession Stay(string job, int startMinute, int minutes, long universe = 11) =>
        new(1, universe, job, new DateTime(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc).AddMinutes(startMinute),
            new DateTime(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc).AddMinutes(startMinute + minutes));

    [Fact]
    public void The_same_stay_is_kept_once_with_its_latest_end()
    {
        var store = new PlaytimeStore(_temp.Combine("Playtime.json"));

        Assert.Equal(1, store.Add([Stay("a", 0, 5)]));
        Assert.Equal(0, store.Add([Stay("a", 0, 5)]));
        Assert.Equal(1, store.Add([Stay("a", 0, 30)]));   // read again after Roblox closed
        Assert.Equal(0, store.Add([Stay("a", 0, 10)]));

        Assert.Equal(TimeSpan.FromMinutes(30), Assert.Single(store.Data.Sessions).Length);
    }

    [Fact]
    public void Logs_on_disk_are_read_and_the_history_survives_a_restart()
    {
        string logs = _temp.Combine("logs");
        Directory.CreateDirectory(logs);
        File.WriteAllLines(Path.Combine(logs, "0.740.0_20260924T120000Z_Player_ABCDE_last.log"),
            [.. PlaytimeLogTests.Join(0, "d9758c4f-19b0-4512-9118-4306aec5fdd1", 1, 11), PlaytimeLogTests.Leave(900), PlaytimeLogTests.Disconnect(900)]);
        File.WriteAllText(Path.Combine(logs, "0.740.0_20260924T120000Z_Studio_ABCDE_last.log"), "not a player log");
        string file = _temp.Combine("Playtime.json");

        var first = PlaytimeStore.Update(file, store => store.ImportLogs(logs));
        var again = PlaytimeStore.Update(file, store => Assert.Equal(0, store.ImportLogs(logs)));

        Assert.Single(first.Data.Sessions);
        Assert.Single(again.Data.Sessions);
    }

    [Fact]
    public void Clearing_keeps_older_stays_from_coming_back_from_the_logs()
    {
        var store = new PlaytimeStore(_temp.Combine("Playtime.json"));
        store.Add([Stay("a", 0, 5)]);
        store.Data.Names[11] = "Game";

        store.Clear(new DateTime(2026, 9, 24, 13, 0, 0, DateTimeKind.Utc));

        Assert.Empty(store.Data.Names);
        Assert.Equal(0, store.Add([Stay("a", 0, 5)]));
        Assert.Equal(1, store.Add([Stay("b", 90, 5)]));
    }
}

public class PlaytimeSummaryTests
{
    private static readonly TimeZoneInfo Warsaw = TimeZoneInfo.FindSystemTimeZoneById("Central European Standard Time");

    private static PlaySession Stay(long universe, DateTime joinedUtc, int minutes, string job = "") =>
        new(universe * 10, universe, job.Length > 0 ? job : Guid.NewGuid().ToString(), joinedUtc, joinedUtc.AddMinutes(minutes));

    private static DateTime Utc(int day, int hour, int minute = 0) => new(2026, 9, day, hour, minute, 0, DateTimeKind.Utc);

    [Fact]
    public void Games_are_summed_most_played_first()
    {
        var summary = Playtime.Summarise([Stay(1, Utc(24, 10), 30), Stay(2, Utc(24, 11), 50), Stay(1, Utc(24, 12), 40)], DateTime.MinValue, Utc(25, 0));

        Assert.Equal(TimeSpan.FromMinutes(120), summary.Total);
        Assert.Equal(3, summary.Sessions);
        Assert.Equal([1L, 2L], summary.Games.Select(game => game.UniverseId));
        Assert.Equal(TimeSpan.FromMinutes(70), summary.Games[0].Time);
        Assert.Equal(2, summary.Games[0].Sessions);
        Assert.Equal(Utc(24, 12, 40), summary.Games[0].LastPlayed);
    }

    [Fact]
    public void A_stay_across_midnight_counts_on_both_days()
    {
        // 23:30 to 00:30 in Warsaw (UTC+2 in September)
        var stay = Stay(1, Utc(24, 21, 30), 60);

        var daily = Playtime.Daily([stay], 7, Utc(25, 10), Warsaw);

        Assert.Equal(new DateTime(2026, 9, 25), daily[^1].Day);
        Assert.Equal(TimeSpan.FromMinutes(30), daily[^1].Time);
        Assert.Equal(TimeSpan.FromMinutes(30), daily[^2].Time);

        var (from, to) = Playtime.Range(PlaytimePeriod.Today, Utc(25, 10), Warsaw);
        Assert.Equal(TimeSpan.FromMinutes(30), Playtime.Summarise([stay], from, to).Total);
    }

    [Fact]
    public void The_week_starts_on_Monday_at_local_midnight()
    {
        // Friday 25 September 2026 → Monday 21 September, 00:00 in Warsaw = 22:00 UTC on Sunday
        var (from, to) = Playtime.Range(PlaytimePeriod.ThisWeek, Utc(25, 10), Warsaw);

        Assert.Equal(Utc(20, 22), from);
        Assert.Equal(Utc(25, 10), to);
        Assert.Equal(DateTime.MinValue, Playtime.Range(PlaytimePeriod.AllTime, Utc(25, 10), Warsaw).From);
    }

    [Fact]
    public void Stays_without_a_universe_are_counted_by_their_place()
    {
        var stay = new PlaySession(1818, 0, "job", Utc(24, 10), Utc(24, 11));

        var game = Assert.Single(Playtime.Summarise([stay], DateTime.MinValue, Utc(25, 0)).Games);

        Assert.Equal(-1818, game.GameKey);
        Assert.Equal(1818, game.PlaceId);
    }
}
