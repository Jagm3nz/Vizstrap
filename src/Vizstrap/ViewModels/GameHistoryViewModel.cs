using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vizstrap.Core.Activity;
using Vizstrap.Localization;
using Vizstrap.Views;

namespace Vizstrap.ViewModels;

public sealed record GameHistoryEntry(string Name, string? IconUrl, string Description, GameVisit Visit);

/// <summary>Bloxstrap's game history: the games left in this Roblox session, with a way back in.</summary>
public sealed partial class GameHistoryViewModel(
    Func<IReadOnlyList<GameSession>> history, RobloxWebApi api, Action<GameVisit> rejoin) : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private IReadOnlyList<GameHistoryEntry> _entries = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private string? _error;

    public bool IsEmpty => !IsLoading && Error is null && Entries.Count == 0;

    public async Task LoadAsync()
    {
        IsLoading = true;
        Error = null;

        var visits = GameHistory.Visits(history());

        try
        {
            var universeIds = new Dictionary<GameVisit, long>();

            foreach (var visit in visits)
            {
                universeIds[visit] = visit.First.UniverseId > 0
                    ? visit.First.UniverseId
                    : await api.GetUniverseIdAsync(visit.First.PlaceId) ?? 0;
            }

            var universes = await api.GetUniversesAsync(universeIds.Values);

            Entries = visits.Select(visit =>
            {
                var universe = universes.GetValueOrDefault(universeIds[visit]);
                string name = universe?.Name is { Length: > 0 } universeName
                    ? universeName
                    : visit.First.PlaceId.ToString(CultureInfo.CurrentCulture);

                return new GameHistoryEntry(name, universe?.IconUrl, Describe(visit, universe), visit);
            }).ToList();
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            Error = string.Format(Strings.History_LoadFailed, ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void Rejoin(GameHistoryEntry entry) => rejoin(entry.Visit);

    /// <summary>"Creator • 14:05 – 14:40", plus the server type when it isn't public.</summary>
    private static string Describe(GameVisit visit, UniverseInfo? universe)
    {
        var parts = new List<string>();

        if (!string.IsNullOrEmpty(universe?.CreatorName))
            parts.Add(universe.CreatorName);

        parts.Add($"{visit.Joined.ToLocalTime():t} – {visit.Left.ToLocalTime():t}");

        if (visit.First.ServerType != ServerType.Public)
            parts.Add(ServerInfoWindow.ServerTypeName(visit.First.ServerType));

        return string.Join(" • ", parts);
    }
}
