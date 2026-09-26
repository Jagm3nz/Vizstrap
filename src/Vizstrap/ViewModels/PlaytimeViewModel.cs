using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vizstrap.Core;
using Vizstrap.Core.Activity;
using Vizstrap.Core.Logging;
using Vizstrap.Localization;
using Vizstrap.Views;

namespace Vizstrap.ViewModels;

/// <summary>One day's bar in the last-7-days chart.</summary>
/// <param name="BarHeight">In pixels, the busiest day filling the chart.</param>
public sealed record PlaytimeDay(string Label, string Tooltip, double BarHeight, bool IsToday);

/// <summary>One game's row: its time in the period and its share of it.</summary>
public sealed record PlaytimeGame(string Name, string? IconUrl, string Time, double Share, string Details, long PlaceId);

/// <summary>
/// The "Playtime" page: how long each game was played today, this week and in all, from Roblox's logs
/// (Activity.PlaytimeStore). Read when the page is first shown; nothing here is saved with the settings.
/// </summary>
public sealed partial class PlaytimeViewModel : ObservableObject
{
    private const string LogSource = nameof(PlaytimeViewModel);

    /// <summary>The chart's height for the busiest day.</summary>
    public const double ChartHeight = 120;

    private PlaytimeData _data = new();
    private Dictionary<long, string?> _icons = [];
    private bool _loaded;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsToday), nameof(IsThisWeek), nameof(IsAllTime))]
    private PlaytimePeriod _period = PlaytimePeriod.ThisWeek;

    [ObservableProperty]
    private string _totalText = "";

    [ObservableProperty]
    private string _sessionsText = "";

    [ObservableProperty]
    private string _topGameText = "";

    [ObservableProperty]
    private string _chartMaxText = "";

    [ObservableProperty]
    private bool _isEmpty;

    [ObservableProperty]
    private bool _isLoading;

    public ObservableCollection<PlaytimeDay> Days { get; } = [];

    public ObservableCollection<PlaytimeGame> Games { get; } = [];

    /// <summary>Without activity tracking nothing new is counted (the history stays).</summary>
    public bool IsTrackingOff => !App.Settings.Value.EnableActivityTracking;

    public bool IsToday
    {
        get => Period == PlaytimePeriod.Today;
        set { if (value) Period = PlaytimePeriod.Today; }
    }

    public bool IsThisWeek
    {
        get => Period == PlaytimePeriod.ThisWeek;
        set { if (value) Period = PlaytimePeriod.ThisWeek; }
    }

    public bool IsAllTime
    {
        get => Period == PlaytimePeriod.AllTime;
        set { if (value) Period = PlaytimePeriod.AllTime; }
    }

    /// <summary>Reads the history (and Roblox's logs still on disk) the first time the page shows.</summary>
    public async Task EnsureLoadedAsync()
    {
        if (_loaded)
            return;

        _loaded = true;
        IsLoading = true;

        try
        {
            bool tracking = App.Settings.Value.EnableActivityTracking;
            var store = await Task.Run(() => PlaytimeStore.Update(App.Paths.Playtime, store =>
            {
                if (tracking)
                    store.ImportLogs(RobloxPaths.Logs);
            }));
            _data = store.Data;
            Refresh();

            await FetchGamesAsync();
            Refresh();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn(LogSource, $"Play time couldn't be read: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnPeriodChanged(PlaytimePeriod value) => Refresh();

    [RelayCommand]
    private void Play(PlaytimeGame? game)
    {
        if (game is null)
            return;

        // Vizstrap handles roblox:// links, so this goes through the usual launch
        Process.Start(new ProcessStartInfo($"roblox://experiences/start?placeId={game.PlaceId}") { UseShellExecute = true })?.Dispose();
    }

    [RelayCommand]
    private void Clear()
    {
        if (!MessageWindow.Confirm(Strings.Playtime_ClearQuestion, "", Strings.Playtime_Clear, Strings.Common_Cancel, danger: true))
            return;

        try
        {
            _data = PlaytimeStore.Update(App.Paths.Playtime, store => store.Clear(DateTime.UtcNow)).Data;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error(LogSource, ex);
            MessageWindow.ShowError(Strings.Error_Unexpected, ex);
        }

        Refresh();
    }

    /// <summary>Names and icons from Roblox for the games in the history; names are kept for next time.</summary>
    private async Task FetchGamesAsync()
    {
        var universes = _data.Sessions.Select(session => session.UniverseId).Where(id => id > 0).Distinct().ToList();

        if (universes.Count == 0)
            return;

        try
        {
            var api = new RobloxWebApi(App.Http);
            var found = new Dictionary<long, UniverseInfo>();

            // the games API takes a limited number of ids at once
            foreach (var chunk in universes.Chunk(50))
            {
                foreach (var (id, info) in await api.GetUniversesAsync(chunk))
                    found[id] = info;
            }

            _icons = found.ToDictionary(pair => pair.Key, pair => pair.Value.IconUrl);
            var names = found.Where(pair => pair.Value.Name.Length > 0).ToDictionary(pair => pair.Key, pair => pair.Value.Name);

            if (names.Any(pair => _data.Names.GetValueOrDefault(pair.Key) != pair.Value))
            {
                _data = (await Task.Run(() => PlaytimeStore.Update(App.Paths.Playtime, store =>
                {
                    foreach (var (id, name) in names)
                        store.Data.Names[id] = name;
                }))).Data;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            // offline: the names kept from before will do
            Log.Warn(LogSource, $"Game names couldn't be fetched: {ex.Message}");
        }
    }

    private void Refresh()
    {
        var now = DateTime.UtcNow;
        var zone = TimeZoneInfo.Local;
        var (from, to) = Playtime.Range(Period, now, zone);
        var summary = Playtime.Summarise(_data.Sessions, from, to);

        TotalText = Duration(summary.Total);
        SessionsText = summary.Sessions.ToString(CultureInfo.CurrentCulture);
        TopGameText = summary.Games.Count > 0 ? NameOf(summary.Games[0]) : "—";
        IsEmpty = summary.Games.Count == 0;

        Games.Clear();
        var today = TimeZoneInfo.ConvertTimeFromUtc(now, zone).Date;

        foreach (var game in summary.Games)
        {
            double share = summary.Total > TimeSpan.Zero ? game.Time / summary.Total : 0;
            string details = string.Format(Strings.Playtime_Details, game.Sessions, LastPlayed(game.LastPlayed, today, zone));
            Games.Add(new PlaytimeGame(NameOf(game), _icons.GetValueOrDefault(game.UniverseId), Duration(game.Time), share, details, game.PlaceId));
        }

        Days.Clear();
        var daily = Playtime.Daily(_data.Sessions, 7, now, zone);
        var busiest = daily.Max(day => day.Time);
        ChartMaxText = busiest > TimeSpan.Zero ? Duration(busiest) : "";

        foreach (var (day, time) in daily)
        {
            // a day with any play shows at least a sliver
            double height = busiest > TimeSpan.Zero && time > TimeSpan.Zero ? Math.Max(3, ChartHeight * (time / busiest)) : 0;
            Days.Add(new PlaytimeDay(day.ToString("ddd", CultureInfo.CurrentUICulture), $"{day.ToString("dddd, d MMMM", CultureInfo.CurrentUICulture)}: {Duration(time)}",
                height, day == today));
        }
    }

    private string NameOf(GamePlaytime game) =>
        _data.Names.GetValueOrDefault(game.GameKey) is { Length: > 0 } name ? name : string.Format(Strings.Playtime_UnknownGame, game.PlaceId);

    private static string LastPlayed(DateTime utc, DateTime today, TimeZoneInfo zone)
    {
        var day = TimeZoneInfo.ConvertTimeFromUtc(utc, zone).Date;
        int days = (int)(today - day).TotalDays;

        return days switch
        {
            <= 0 => Strings.Playtime_LastToday,
            1 => Strings.Playtime_LastYesterday,
            < 7 => string.Format(Strings.Playtime_LastDaysAgo, days),
            _ => day.ToString("d", CultureInfo.CurrentCulture),
        };
    }

    internal static string Duration(TimeSpan time) => time.TotalMinutes switch
    {
        < 1 when time > TimeSpan.Zero => Strings.Playtime_LessThanMinute,
        < 60 => string.Format(Strings.Playtime_Minutes, (int)time.TotalMinutes),
        _ => string.Format(Strings.Playtime_HoursMinutes, (int)time.TotalHours, time.Minutes),
    };
}
