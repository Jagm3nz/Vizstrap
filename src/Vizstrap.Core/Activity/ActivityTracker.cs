using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Vizstrap.Core.Logging;

namespace Vizstrap.Core.Activity;

/// <summary>A BloxstrapRPC message printed by a game: <c>{"command": "...", "data": ...}</c>.</summary>
public sealed record RpcMessage(string Command, JsonElement Data);

/// <summary>
/// Follows what the player is doing from Roblox's log lines, like Bloxstrap's ActivityWatcher.
/// Unlike it, open server connections are counted: on a teleport Roblox connects to the new server
/// before it logs the old one's disconnect, and that disconnect must not end the new game.
/// Not thread-safe: feed lines from one thread and read the state on that thread.
/// </summary>
public sealed partial class ActivityTracker
{
    private const string LogSource = nameof(ActivityTracker);

    // these log entries are what Bloxstrap relies on too; their FLog levels could change in any Roblox update
    private const string GameJoiningEntry = "[FLog::Output] ! Joining game";
    private const string GameJoiningUniverseEntry = "[FLog::GameJoinLoadTime] Report game_join_loadtime:";
    private const string GameJoiningUdmuxEntry = "[FLog::Network] UDMUX Address = ";
    private const string GameJoinedEntry = "[FLog::Network] Replicator created: ";
    private const string GameDisconnectedEntry = "[FLog::Network] Time to disconnect replication data:";
    private const string GameTeleportingEntry = "[FLog::UgcExperienceController] UgcExperienceController: doTeleport: joinScriptUrl";
    private const string GameLeavingEntry = "[FLog::SingleSurfaceApp] leaveUGCGameInternal";
    private const string GameMessageEntry = "[FLog::CreatorOutput] [BloxstrapRPC] ";

    /// <summary>Roblox's ServerSessionJoinType values for reserved servers (NewGamePrivateGame, SpecificPrivateGame).</summary>
    private static readonly int[] ReservedJoinTypes = [4, 6];

    private static readonly TimeSpan RpcRateLimit = TimeSpan.FromSeconds(1);

    private readonly Func<DateTime> _utcNow;
    private readonly List<GameSession> _history = [];

    private GameSession? _joining;
    private int _connections;
    private bool _teleportMarker;
    private bool _reservedTeleportMarker;
    private DateTime _lastRpcMessage = DateTime.MinValue;

    /// <param name="utcNow">Clock for lines without a readable timestamp.</param>
    public ActivityTracker(Func<DateTime>? utcNow = null)
    {
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    /// <summary>The game the player is on, or null (in the app, loading or gone).</summary>
    public GameSession? Current { get; private set; }

    public bool InGame => Current is not null;

    /// <summary>Games left in this Roblox session, newest first.</summary>
    public IReadOnlyList<GameSession> History => _history;

    public event EventHandler<GameSession>? GameJoined;

    public event EventHandler<GameSession>? GameLeft;

    /// <summary>The player went back to Roblox's desktop app (left a game or cancelled joining one).</summary>
    public event EventHandler? AppClosed;

    /// <summary>A BloxstrapRPC message from the current game (at most one a second, like Bloxstrap).</summary>
    public event EventHandler<RpcMessage>? RpcMessageReceived;

    public void ProcessLine(string line)
    {
        // "2026-09-24T15:58:37.816Z,35.816669,1aac,6 [FLog::Output] ..." — lines without the prefix continue a previous entry
        int messageStart = line.IndexOf(' ');

        if (messageStart <= 0)
            return;

        string message = line[(messageStart + 1)..];

        if (!message.StartsWith('['))
            return;

        DateTime time = ReadTimestamp(line.AsSpan(0, messageStart)) ?? _utcNow();

        if (message.StartsWith(GameJoiningEntry, StringComparison.Ordinal))
            OnJoining(message);
        else if (message.StartsWith(GameJoiningUniverseEntry, StringComparison.Ordinal))
            OnJoinReport(message);
        else if (message.StartsWith(GameJoiningUdmuxEntry, StringComparison.Ordinal))
            OnUdmux(message);
        else if (message.StartsWith(GameJoinedEntry, StringComparison.Ordinal))
            OnConnected(time);
        else if (message.StartsWith(GameDisconnectedEntry, StringComparison.Ordinal))
            OnDisconnected(time);
        else if (message.StartsWith(GameTeleportingEntry, StringComparison.Ordinal))
            OnTeleporting(message);
        else if (message.StartsWith(GameLeavingEntry, StringComparison.Ordinal))
            OnLeaving();
        else if (message.StartsWith(GameMessageEntry, StringComparison.Ordinal))
            OnRpcMessage(message[GameMessageEntry.Length..], time);
    }

    private void OnJoining(string message)
    {
        var match = JoiningPattern().Match(message);

        if (!match.Success)
        {
            Log.Warn(LogSource, $"Unexpected join entry: {message}");
            return;
        }

        _joining = new GameSession
        {
            JobId = match.Groups[1].Value,
            PlaceId = long.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture),
            MachineAddress = match.Groups[3].Value,
            IsTeleport = _teleportMarker,
            ServerType = _reservedTeleportMarker ? ServerType.Reserved : ServerType.Public,
        };

        _teleportMarker = false;
        _reservedTeleportMarker = false;

        Log.Info(LogSource, $"Joining {_joining}{(_joining.IsTeleport ? " (teleport)" : "")}");
    }

    private void OnJoinReport(string message)
    {
        if (_joining is null)
            return;

        var match = JoinReportPattern().Match(message);

        if (!match.Success)
        {
            Log.Warn(LogSource, $"Unexpected join report: {message}");
            return;
        }

        _joining.UniverseId = long.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        _joining.UserId = long.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);

        string referral = ReferralPattern().Match(message) is { Success: true } referralMatch ? referralMatch.Groups[1].Value : "";

        if (referral.Contains("RequestPrivateGame", StringComparison.OrdinalIgnoreCase) ||
            referral.Contains("GameDetailPageJSHybridEvent", StringComparison.OrdinalIgnoreCase))
            _joining.ServerType = ServerType.Private;

        // a teleport within the same game continues the same visit
        if (_joining.IsTeleport && Current?.UniverseId == _joining.UniverseId)
            _joining.RootSession = Current.RootSession ?? Current;
        else if (_joining.IsTeleport && _history.FirstOrDefault() is { } previous && previous.UniverseId == _joining.UniverseId)
            _joining.RootSession = previous.RootSession ?? previous;
    }

    private void OnUdmux(string message)
    {
        if (_joining is null)
            return;

        var match = UdmuxPattern().Match(message);

        if (!match.Success || match.Groups[2].Value != _joining.MachineAddress)
        {
            Log.Warn(LogSource, $"Unexpected UDMUX entry: {message}");
            return;
        }

        _joining.MachineAddress = match.Groups[1].Value;
    }

    private void OnConnected(DateTime time)
    {
        _connections++;

        if (_joining is null)
            return;

        // on a teleport the new server connects before the old one disconnects
        if (Current is not null)
            EndCurrent(time);

        Current = _joining;
        Current.TimeJoined = time;
        _joining = null;

        Log.Info(LogSource, $"Joined {Current} (universe {Current.UniverseId}, {Current.ServerType}, {Current.MachineAddress})");
        GameJoined?.Invoke(this, Current);
    }

    private void OnDisconnected(DateTime time)
    {
        _connections = Math.Max(0, _connections - 1);

        if (_connections == 0 && Current is not null)
            EndCurrent(time);
    }

    private void EndCurrent(DateTime time)
    {
        var left = Current!;
        left.TimeLeft = time;
        _history.Insert(0, left);
        Current = null;

        Log.Info(LogSource, $"Left {left}");
        GameLeft?.Invoke(this, left);
    }

    private void OnTeleporting(string message)
    {
        _teleportMarker = true;

        if (JoinTypePattern().Match(message) is { Success: true } match &&
            int.TryParse(match.Groups[1].Value, CultureInfo.InvariantCulture, out int joinType) &&
            ReservedJoinTypes.Contains(joinType))
            _reservedTeleportMarker = true;

        Log.Info(LogSource, $"Teleporting{(_reservedTeleportMarker ? " to a reserved server" : "")}");
    }

    private void OnLeaving()
    {
        // leaving while still joining means the join was cancelled or failed
        if (_joining is not null && Current is null)
        {
            Log.Info(LogSource, $"Join of {_joining} did not finish");
            _joining = null;
        }

        AppClosed?.Invoke(this, EventArgs.Empty);
    }

    private void OnRpcMessage(string json, DateTime time)
    {
        if (Current is null)
            return;

        if (time - _lastRpcMessage < RpcRateLimit)
        {
            Log.Info(LogSource, "Dropped a BloxstrapRPC message (rate limit)");
            return;
        }

        RpcMessage? message;

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            message = root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("command", out var command) && command.ValueKind == JsonValueKind.String &&
                !string.IsNullOrEmpty(command.GetString())
                    ? new RpcMessage(command.GetString()!, root.TryGetProperty("data", out var data) ? data.Clone() : default)
                    : null;
        }
        catch (JsonException)
        {
            message = null;
        }

        if (message is null)
        {
            Log.Warn(LogSource, $"Unreadable BloxstrapRPC message: {json}");
            return;
        }

        if (message.Command == "SetLaunchData")
        {
            if (message.Data.ValueKind != JsonValueKind.String || message.Data.GetString()!.Length > 200)
            {
                Log.Warn(LogSource, "SetLaunchData needs a string of at most 200 characters");
                return;
            }

            Current.LaunchData = message.Data.GetString()!;
        }

        _lastRpcMessage = time;
        RpcMessageReceived?.Invoke(this, message);
    }

    internal static DateTime? ReadTimestamp(ReadOnlySpan<char> prefix)
    {
        int comma = prefix.IndexOf(',');

        if (comma > 0)
            prefix = prefix[..comma];

        return DateTime.TryParse(prefix, CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var time) ? time : null;
    }

    [GeneratedRegex(@"! Joining game '([0-9a-f\-]{36})' place ([0-9]+) at ([0-9\.]+)")]
    private static partial Regex JoiningPattern();

    [GeneratedRegex(@"universeid:([0-9]+).*userid:([0-9]+)")]
    private static partial Regex JoinReportPattern();

    [GeneratedRegex(@"referral_page:([^,]+)")]
    private static partial Regex ReferralPattern();

    [GeneratedRegex(@"UDMUX Address = ([0-9\.]+), Port = [0-9]+ \| RCC Server Address = ([0-9\.]+), Port = [0-9]+")]
    private static partial Regex UdmuxPattern();

    [GeneratedRegex(@"JoinTypeId""%3a(\d+)%2c")]
    private static partial Regex JoinTypePattern();
}
