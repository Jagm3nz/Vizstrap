using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Vizstrap.Core.Activity;

public sealed record UniverseInfo(long Id, long RootPlaceId, string Name, string CreatorName, bool CreatorVerified, string? IconUrl);

public sealed record UserInfo(long Id, string Name, string DisplayName, string? HeadshotUrl);

/// <summary>
/// The public Roblox web APIs Bloxstrap uses for Rich Presence and game history. Answers are cached
/// for the lifetime of the object; failures throw <see cref="HttpRequestException"/>.
/// </summary>
public sealed class RobloxWebApi(HttpClient http)
{
    private const int ThumbnailAttempts = 5;

    private readonly ConcurrentDictionary<long, UniverseInfo> _universes = new();
    private readonly ConcurrentDictionary<long, UserInfo> _users = new();
    private readonly ConcurrentDictionary<long, string?> _assetThumbnails = new();
    private readonly ConcurrentDictionary<long, long> _placeUniverses = new();

    /// <summary>Waits between thumbnail attempts while Roblox is still rendering one; replaceable in tests.</summary>
    internal Func<int, CancellationToken, Task> ThumbnailRetryDelay { get; set; } =
        (attempt, token) => Task.Delay(500 * attempt, token);

    public async Task<UniverseInfo?> GetUniverseAsync(long universeId, CancellationToken cancellationToken = default) =>
        (await GetUniversesAsync([universeId], cancellationToken)).GetValueOrDefault(universeId);

    /// <summary>Details and 512×512 icons of several games in two requests.</summary>
    public async Task<IReadOnlyDictionary<long, UniverseInfo>> GetUniversesAsync(
        IEnumerable<long> universeIds, CancellationToken cancellationToken = default)
    {
        var ids = universeIds.Where(id => id > 0).Distinct().ToList();
        var missing = ids.Where(id => !_universes.ContainsKey(id)).ToList();

        if (missing.Count > 0)
        {
            string query = string.Join(',', missing);

            var games = await http.GetFromJsonAsync<DataArray<GameResponse>>(
                $"https://games.roblox.com/v1/games?universeIds={query}", cancellationToken);

            var icons = await GetThumbnailsAsync(
                $"https://thumbnails.roblox.com/v1/games/icons?universeIds={query}&returnPolicy=PlaceHolder&size=512x512&format=Png&isCircular=false",
                cancellationToken);

            foreach (var game in games?.Data ?? [])
            {
                _universes[game.Id] = new UniverseInfo(game.Id, game.RootPlaceId, game.Name ?? "",
                    game.Creator?.Name ?? "", game.Creator?.HasVerifiedBadge == true, icons.GetValueOrDefault(game.Id));
            }
        }

        return ids.Where(_universes.ContainsKey).ToDictionary(id => id, id => _universes[id]);
    }

    /// <summary>Name, display name and a 180×180 headshot.</summary>
    public async Task<UserInfo?> GetUserAsync(long userId, CancellationToken cancellationToken = default)
    {
        if (_users.TryGetValue(userId, out var cached))
            return cached;

        var user = await http.GetFromJsonAsync<UserResponse>($"https://users.roblox.com/v1/users/{userId}", cancellationToken);

        if (user is null)
            return null;

        var headshots = await GetThumbnailsAsync(
            $"https://thumbnails.roblox.com/v1/users/avatar-headshot?userIds={userId}&size=180x180&format=Png&isCircular=false",
            cancellationToken);

        return _users[userId] = new UserInfo(user.Id, user.Name ?? "", user.DisplayName ?? user.Name ?? "", headshots.GetValueOrDefault(userId));
    }

    /// <summary>A 512×512 image of any asset (for BloxstrapRPC images).</summary>
    public async Task<string?> GetAssetThumbnailAsync(long assetId, CancellationToken cancellationToken = default)
    {
        if (_assetThumbnails.TryGetValue(assetId, out var cached))
            return cached;

        var thumbnails = await GetThumbnailsAsync(
            $"https://thumbnails.roblox.com/v1/assets?assetIds={assetId}&returnPolicy=PlaceHolder&size=512x512&format=Png&isCircular=false",
            cancellationToken);

        return _assetThumbnails[assetId] = thumbnails.GetValueOrDefault(assetId);
    }

    /// <summary>The game a place belongs to, for joins whose log entry lacks it.</summary>
    public async Task<long?> GetUniverseIdAsync(long placeId, CancellationToken cancellationToken = default)
    {
        if (_placeUniverses.TryGetValue(placeId, out long cached))
            return cached;

        var response = await http.GetFromJsonAsync<UniverseIdResponse>(
            $"https://apis.roblox.com/universes/v1/places/{placeId}/universe", cancellationToken);

        if (response?.UniverseId is not > 0)
            return null;

        return _placeUniverses[placeId] = response.UniverseId.Value;
    }

    /// <summary>Image URLs by target id; retries while Roblox reports them as still being rendered.</summary>
    private async Task<Dictionary<long, string?>> GetThumbnailsAsync(string url, CancellationToken cancellationToken)
    {
        List<ThumbnailResponse> thumbnails = [];

        for (int attempt = 1; attempt <= ThumbnailAttempts; attempt++)
        {
            var response = await http.GetFromJsonAsync<DataArray<ThumbnailResponse>>(url, cancellationToken);
            thumbnails = response?.Data ?? [];

            if (thumbnails.All(thumbnail => thumbnail.State != "Pending") || attempt == ThumbnailAttempts)
                break;

            await ThumbnailRetryDelay(attempt, cancellationToken);
        }

        return thumbnails
            .GroupBy(thumbnail => thumbnail.TargetId)
            .ToDictionary(group => group.Key, group =>
                group.First() is { State: "Completed", ImageUrl: { Length: > 0 } imageUrl } ? imageUrl : null);
    }

    private sealed record DataArray<T>([property: JsonPropertyName("data")] List<T>? Data);

    private sealed record GameResponse(
        [property: JsonPropertyName("id")] long Id,
        [property: JsonPropertyName("rootPlaceId")] long RootPlaceId,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("creator")] CreatorResponse? Creator);

    private sealed record CreatorResponse(
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("hasVerifiedBadge")] bool HasVerifiedBadge);

    private sealed record UserResponse(
        [property: JsonPropertyName("id")] long Id,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("displayName")] string? DisplayName);

    private sealed record ThumbnailResponse(
        [property: JsonPropertyName("targetId")] long TargetId,
        [property: JsonPropertyName("state")] string? State,
        [property: JsonPropertyName("imageUrl")] string? ImageUrl);

    private sealed record UniverseIdResponse([property: JsonPropertyName("universeId")] long? UniverseId);
}
