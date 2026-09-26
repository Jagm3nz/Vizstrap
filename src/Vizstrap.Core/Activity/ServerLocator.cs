using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Vizstrap.Core.Logging;

namespace Vizstrap.Core.Activity;

/// <summary>Where a game server is, from ipinfo.io (Bloxstrap's "Query server location").</summary>
public sealed class ServerLocator(HttpClient http)
{
    private readonly ConcurrentDictionary<string, Task<string?>> _locations = new();

    /// <summary>"City, Region, Country", or null if ipinfo.io didn't know. Each address is asked once.</summary>
    public Task<string?> LocateAsync(string address) => _locations.GetOrAdd(address, QueryAsync);

    private async Task<string?> QueryAsync(string address)
    {
        try
        {
            var info = await http.GetFromJsonAsync<IpInfoResponse>($"https://ipinfo.io/{address}/json");
            return info is null ? null : Format(info.City, info.Region, info.Country);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            Log.Warn(nameof(ServerLocator), $"Couldn't locate {address}: {ex.Message}");
            return null;
        }
    }

    /// <summary>Bloxstrap's format; the region is skipped when it's the city itself (e.g. "Singapore").</summary>
    internal static string? Format(string? city, string? region, string? country)
    {
        if (string.IsNullOrWhiteSpace(city))
            return null;

        return city == region
            ? string.Create(CultureInfo.InvariantCulture, $"{region}, {country}")
            : string.Create(CultureInfo.InvariantCulture, $"{city}, {region}, {country}");
    }

    private sealed record IpInfoResponse(
        [property: JsonPropertyName("city")] string? City,
        [property: JsonPropertyName("region")] string? Region,
        [property: JsonPropertyName("country")] string? Country);
}
