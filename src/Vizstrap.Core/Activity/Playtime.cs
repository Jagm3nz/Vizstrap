using System.Text;
using Vizstrap.Core.Logging;
using Vizstrap.Core.Storage;

namespace Vizstrap.Core.Activity;

/// <summary>Time on one game server, read from Roblox's log (UTC).</summary>
public sealed record PlaySession(long PlaceId, long UniverseId, string JobId, DateTime Joined, DateTime Left)
{
    public TimeSpan Length => Left > Joined ? Left - Joined : TimeSpan.Zero;

    /// <summary>The same stay read twice (after the game, and again from the log) has the same key.</summary>
    public string Key => $"{JobId}@{Joined:O}";

    /// <summary>What the stay is counted under: its game (universe), or its place when the log didn't say.</summary>
    public long GameKey => UniverseId > 0 ? UniverseId : -PlaceId;
}

/// <summary>Playtime.json: every stay on a server, and the games' names for when Roblox can't be asked.</summary>
public sealed class PlaytimeData
{
    public List<PlaySession> Sessions { get; set; } = [];

    /// <summary>Game names by <see cref="PlaySession.GameKey"/>.</summary>
    public Dictionary<long, string> Names { get; set; } = [];

    /// <summary>When the history was last cleared (UTC): logs still on disk don't bring older stays back.</summary>
    public DateTime? ClearedAt { get; set; }
}

/// <summary>One game's time in a period.</summary>
/// <param name="PlaceId">The place last played, for joining it again.</param>
public sealed record GamePlaytime(long GameKey, long UniverseId, long PlaceId, TimeSpan Time, int Sessions, DateTime LastPlayed);

public sealed record PlaytimeSummary(TimeSpan Total, int Sessions, IReadOnlyList<GamePlaytime> Games);

public enum PlaytimePeriod
{
    Today,
    ThisWeek,
    AllTime,
}

/// <summary>
/// Play time per game, kept in Playtime.json from Roblox's logs: after each run the run's log is read, and
/// logs still on disk are read again when the history is looked at (the same stay is kept once).
/// </summary>
public sealed class PlaytimeStore(string file)
{
    private const string LogSource = nameof(PlaytimeStore);

    private readonly JsonStore<PlaytimeData> _store = new(file);

    public PlaytimeData Data => _store.Value;

    public void Load() => _store.Load();

    public void Save() => _store.Save();

    /// <summary>Adds stays not seen yet; one seen before is kept with the later end (a log read while Roblox was still open). Returns how many changed.</summary>
    public int Add(IEnumerable<PlaySession> sessions)
    {
        var known = Data.Sessions.Select((session, index) => (session.Key, index)).ToDictionary(pair => pair.Key, pair => pair.index);
        int changed = 0;

        foreach (var session in sessions)
        {
            if (session.Length <= TimeSpan.Zero || session.Joined < Data.ClearedAt)
                continue;

            if (!known.TryGetValue(session.Key, out int index))
            {
                known[session.Key] = Data.Sessions.Count;
                Data.Sessions.Add(session);
                changed++;
            }
            else if (session.Left > Data.Sessions[index].Left)
            {
                Data.Sessions[index] = session;
                changed++;
            }
        }

        return changed;
    }

    /// <summary>Reads every player log in Roblox's logs folder; returns how many stays changed.</summary>
    public int ImportLogs(string logsDirectory)
    {
        if (!Directory.Exists(logsDirectory))
            return 0;

        int changed = 0;

        foreach (string log in Directory.EnumerateFiles(logsDirectory, "*_Player_*.log"))
        {
            try
            {
                changed += Add(PlaytimeLog.Read(log));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Warn(LogSource, $"{Path.GetFileName(log)} can't be read: {ex.Message}");
            }
        }

        return changed;
    }

    public void Clear(DateTime nowUtc)
    {
        Data.Sessions.Clear();
        Data.Names.Clear();
        Data.ClearedAt = nowUtc;
    }

    /// <summary>
    /// Loads, changes and saves the file while holding a lock shared by all Vizstrap processes (the one
    /// watching a game and the settings may write at the same time); returns the store as saved.
    /// </summary>
    public static PlaytimeStore Update(string file, Action<PlaytimeStore> change)
    {
        using var mutex = new Mutex(false, @"Local\Vizstrap.Playtime");
        bool owned = false;

        try
        {
            try
            {
                owned = mutex.WaitOne(TimeSpan.FromSeconds(10));
            }
            catch (AbandonedMutexException)
            {
                owned = true;
            }

            var store = new PlaytimeStore(file);
            store.Load();
            change(store);
            store.Save();
            return store;
        }
        finally
        {
            if (owned)
                mutex.ReleaseMutex();
        }
    }
}

/// <summary>The stays in one of Roblox's logs.</summary>
public static class PlaytimeLog
{
    /// <summary>
    /// Every server joined in the log, with when it was left; one still open when the log ends (Roblox still
    /// running, or closed without a word) ends at the log's last line.
    /// </summary>
    public static IReadOnlyList<PlaySession> Read(string logFile)
    {
        using var stream = new FileStream(logFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return Read(ReadLines(reader));
    }

    public static IReadOnlyList<PlaySession> Read(IEnumerable<string> lines)
    {
        var tracker = new ActivityTracker();
        DateTime? last = null;

        foreach (string line in lines)
        {
            tracker.ProcessLine(line);

            if (line.IndexOf(' ') is > 0 and var space && ActivityTracker.ReadTimestamp(line.AsSpan(0, space)) is { } time)
                last = time;
        }

        var sessions = tracker.History
            .Where(session => session.TimeLeft is not null)
            .Select(session => ToPlay(session, session.TimeLeft!.Value))
            .ToList();

        if (tracker.Current is { } open && last is { } end)
            sessions.Add(ToPlay(open, end));

        sessions.Sort((a, b) => a.Joined.CompareTo(b.Joined));
        return sessions;
    }

    private static PlaySession ToPlay(GameSession session, DateTime left) =>
        new(session.PlaceId, session.UniverseId, session.JobId, session.TimeJoined, left);

    private static IEnumerable<string> ReadLines(StreamReader reader)
    {
        while (reader.ReadLine() is { } line)
            yield return line;
    }
}

/// <summary>Sums of play time: per game in a period, and per day.</summary>
public static class Playtime
{
    /// <summary>The period in UTC: today and this week (from Monday) start at local midnight.</summary>
    public static (DateTime From, DateTime To) Range(PlaytimePeriod period, DateTime nowUtc, TimeZoneInfo zone)
    {
        var localToday = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, zone).Date;

        var localStart = period switch
        {
            PlaytimePeriod.Today => localToday,
            PlaytimePeriod.ThisWeek => localToday.AddDays(-(((int)localToday.DayOfWeek + 6) % 7)),
            _ => DateTime.MinValue,
        };

        return (localStart == DateTime.MinValue ? DateTime.MinValue : ToUtc(localStart, zone), nowUtc);
    }

    /// <summary>Each game's time between <paramref name="from"/> and <paramref name="to"/> (UTC); stays crossing the edges count in part. Most played first.</summary>
    public static PlaytimeSummary Summarise(IEnumerable<PlaySession> sessions, DateTime from, DateTime to)
    {
        var games = new Dictionary<long, (TimeSpan Time, int Sessions, PlaySession Last)>();

        foreach (var session in sessions)
        {
            var time = Overlap(session, from, to);

            if (time <= TimeSpan.Zero)
                continue;

            var game = games.GetValueOrDefault(session.GameKey);
            var last = game.Last is null || session.Left > game.Last.Left ? session : game.Last;
            games[session.GameKey] = (game.Time + time, game.Sessions + 1, last);
        }

        var list = games
            .Select(pair => new GamePlaytime(pair.Key, pair.Value.Last.UniverseId, pair.Value.Last.PlaceId, pair.Value.Time, pair.Value.Sessions, pair.Value.Last.Left))
            .OrderByDescending(game => game.Time)
            .ToList();

        return new PlaytimeSummary(TimeSpan.FromTicks(list.Sum(game => game.Time.Ticks)), list.Sum(game => game.Sessions), list);
    }

    /// <summary>Time played on each of the last <paramref name="days"/> local days, oldest first, today last.</summary>
    public static IReadOnlyList<(DateTime Day, TimeSpan Time)> Daily(IEnumerable<PlaySession> sessions, int days, DateTime nowUtc, TimeZoneInfo zone)
    {
        var today = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, zone).Date;
        var list = sessions.ToList();

        return [.. Enumerable.Range(0, days).Select(index =>
        {
            var day = today.AddDays(index - days + 1);
            var from = ToUtc(day, zone);
            var to = ToUtc(day.AddDays(1), zone);
            return (day, TimeSpan.FromTicks(list.Sum(session => Overlap(session, from, to).Ticks)));
        })];
    }

    private static TimeSpan Overlap(PlaySession session, DateTime from, DateTime to)
    {
        var start = session.Joined > from ? session.Joined : from;
        var end = session.Left < to ? session.Left : to;
        return end > start ? end - start : TimeSpan.Zero;
    }

    private static DateTime ToUtc(DateTime local, TimeZoneInfo zone) =>
        zone.IsInvalidTime(local)
            ? TimeZoneInfo.ConvertTimeToUtc(local.AddHours(1), zone)
            : TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), zone);
}
