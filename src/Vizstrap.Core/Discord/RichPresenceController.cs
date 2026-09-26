using System.Text.Json;
using Vizstrap.Core.Activity;
using Vizstrap.Core.Logging;

namespace Vizstrap.Core.Discord;

/// <summary>
/// Turns game activity into Discord Rich Presence, like Bloxstrap's DiscordRichPresence: the game on
/// join, the Roblox app (or nothing, like Bloxstrap) between games, and changes a game asks for
/// through BloxstrapRPC. Events are handled one at a time in arrival order, so the tracker can report
/// them from any thread.
/// </summary>
/// <param name="robloxStarted">When Roblox was opened (UTC), for the time shown in the menu.</param>
public sealed class RichPresenceController(
    RobloxWebApi api, Action<DiscordActivity?> publish, PresenceOptions options, PresenceTexts texts, DateTime robloxStarted = default)
{
    private const string LogSource = nameof(RichPresenceController);
    private const string ResetValue = "<reset>";

    private readonly object _gate = new();
    private Task _tail = Task.CompletedTask;
    private CancellationTokenSource _sessionCancellation = new();

    private GameSession? _session;
    private bool _showingGame;
    private DiscordActivity? _current;
    private DiscordActivity? _original;
    private bool _visible = true;

    /// <summary>What Discord is showing (or would, when hidden).</summary>
    public DiscordActivity? Current => _current;

    public Task OnGameJoinedAsync(GameSession session)
    {
        var cancellation = RenewSessionCancellation();
        return Enqueue(() => ShowGameAsync(session, cancellation.Token));
    }

    public Task OnGameLeftAsync()
    {
        RenewSessionCancellation();

        return Enqueue(() =>
        {
            _session = null;
            _showingGame = false;
            _current = _original = MenuPresence();
            Publish();
            return Task.CompletedTask;
        });
    }

    /// <summary>Roblox has just opened: shows the menu presence (when enabled) until a game is joined.</summary>
    public Task OnRobloxStartedAsync() => Enqueue(() =>
    {
        if (_session is null)
        {
            _current = _original = MenuPresence();
            Publish();
        }

        return Task.CompletedTask;
    });

    private DiscordActivity? MenuPresence() =>
        options.ShowInMenu ? GamePresence.BuildMenu(robloxStarted == default ? DateTime.UtcNow : robloxStarted, options, texts) : null;

    public Task OnRpcMessageAsync(RpcMessage message) => Enqueue(() => ApplyMessageAsync(message));

    /// <summary>The tray menu's switch: hides the presence without forgetting it.</summary>
    public Task SetVisibleAsync(bool visible) => Enqueue(() =>
    {
        _visible = visible;
        Publish();
        return Task.CompletedTask;
    });

    private async Task ShowGameAsync(GameSession session, CancellationToken cancellationToken)
    {
        // the menu presence stays up while the game's details load, and if they can't be
        _session = session;
        _showingGame = false;

        try
        {
            long universeId = session.UniverseId > 0
                ? session.UniverseId
                : await api.GetUniverseIdAsync(session.PlaceId, cancellationToken) ?? 0;

            var universe = await api.GetUniverseAsync(universeId, cancellationToken);

            if (universe is null)
            {
                Log.Warn(LogSource, $"Roblox has no details for universe {universeId}; not showing {session}");
                return;
            }

            var user = options.ShowAccount && session.UserId > 0
                ? await api.GetUserAsync(session.UserId, cancellationToken)
                : null;

            _current = _original = GamePresence.Build(session, universe, user, options, texts);
            _showingGame = true;
            Log.Info(LogSource, $"Showing {universe.Name} ({session})");
            Publish();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            Log.Warn(LogSource, $"Couldn't load the game's details, so it won't show on Discord: {ex.Message}");
        }
    }

    private async Task ApplyMessageAsync(RpcMessage message)
    {
        if (!_showingGame || _current is null || _original is null || _session is null)
            return;

        if (message.Command == "SetLaunchData")
        {
            _current = _current with { Buttons = GamePresence.Buttons(_session, options, texts) };
            Publish();
            return;
        }

        if (message.Command != "SetRichPresence" || message.Data.ValueKind != JsonValueKind.Object)
            return;

        var data = message.Data;
        var updated = _current;

        if (ReadText(data, "details") is { } details)
            updated = updated with { Details = details == ResetValue ? _original.Details : details };

        if (ReadText(data, "state") is { } state)
            updated = updated with { State = state == ResetValue ? _original.State : state };

        if (ReadTimestamp(data, "timeStart") is { } start)
            updated = updated with { Start = start == 0 ? null : DateTimeOffset.FromUnixTimeSeconds(start) };

        if (ReadTimestamp(data, "timeEnd") is { } end)
            updated = updated with { End = end == 0 ? null : DateTimeOffset.FromUnixTimeSeconds(end) };

        // the account picture wins over the game's small image, as in Bloxstrap
        if (!options.ShowAccount && data.TryGetProperty("smallImage", out var small) && small.ValueKind == JsonValueKind.Object)
        {
            var (image, text) = await ReadImageAsync(small, updated.SmallImage, updated.SmallText, _original.SmallImage, _original.SmallText);
            updated = updated with { SmallImage = image, SmallText = text };
        }

        if (data.TryGetProperty("largeImage", out var large) && large.ValueKind == JsonValueKind.Object)
        {
            var (image, text) = await ReadImageAsync(large, updated.LargeImage, updated.LargeText, _original.LargeImage, _original.LargeText);
            updated = updated with { LargeImage = image, LargeText = text };
        }

        _current = updated;
        Publish();
    }

    /// <summary><c>{ "assetId": 123, "hoverText": "…", "clear": true, "reset": true }</c></summary>
    private async Task<(string? Image, string? Text)> ReadImageAsync(
        JsonElement image, string? currentImage, string? currentText, string? originalImage, string? originalText)
    {
        if (image.TryGetProperty("clear", out var clear) && clear.ValueKind == JsonValueKind.True)
            return (null, currentText);

        if (image.TryGetProperty("reset", out var reset) && reset.ValueKind == JsonValueKind.True)
            return (originalImage, originalText);

        string? url = currentImage;

        if (image.TryGetProperty("assetId", out var assetId) && assetId.TryGetInt64(out long id) && id > 0)
        {
            try
            {
                url = await api.GetAssetThumbnailAsync(id) ?? currentImage;
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
            {
                Log.Warn(LogSource, $"Couldn't load the image of asset {id}: {ex.Message}");
            }
        }

        string? text = image.TryGetProperty("hoverText", out var hoverText) && hoverText.ValueKind == JsonValueKind.String
            ? hoverText.GetString()
            : currentText;

        return (url, text);
    }

    /// <summary>A text the game set; ignored when longer than Discord allows, as in Bloxstrap.</summary>
    private static string? ReadText(JsonElement data, string name)
    {
        if (!data.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
            return null;

        string text = value.GetString()!;

        if (text.Length > DiscordActivity.MaxTextLength)
        {
            Log.Warn(LogSource, $"Ignoring {name}: longer than {DiscordActivity.MaxTextLength} characters");
            return null;
        }

        return text;
    }

    private static long? ReadTimestamp(JsonElement data, string name) =>
        data.TryGetProperty(name, out var value) && value.TryGetInt64(out long seconds) && seconds >= 0 ? seconds : null;

    private void Publish() => publish(_visible ? _current : null);

    private CancellationTokenSource RenewSessionCancellation()
    {
        lock (_gate)
        {
            _sessionCancellation.Cancel();
            _sessionCancellation = new CancellationTokenSource();
            return _sessionCancellation;
        }
    }

    private Task Enqueue(Func<Task> work)
    {
        lock (_gate)
        {
            _tail = _tail.ContinueWith(async _ =>
            {
                try
                {
                    await work();
                }
                catch (Exception ex)
                {
                    Log.Error(LogSource, ex);
                }
            }, TaskScheduler.Default).Unwrap();

            return _tail;
        }
    }
}
