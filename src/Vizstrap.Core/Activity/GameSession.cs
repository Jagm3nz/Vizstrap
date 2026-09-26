namespace Vizstrap.Core.Activity;

public enum ServerType
{
    Public,
    Private,
    Reserved,
}

/// <summary>One stay on one game server, as read from Roblox's log (Bloxstrap's ActivityData).</summary>
public sealed class GameSession
{
    public required long PlaceId { get; init; }

    /// <summary>The server instance ("job") id.</summary>
    public required string JobId { get; set; }

    public long UniverseId { get; set; }

    public long UserId { get; set; }

    /// <summary>The server's address; the public UDMUX proxy once Roblox reports it.</summary>
    public string MachineAddress { get; set; } = "";

    public ServerType ServerType { get; set; }

    /// <summary>Joined by a teleport from another server rather than from the app or website.</summary>
    public bool IsTeleport { get; init; }

    /// <summary>UTC, from the log line.</summary>
    public DateTime TimeJoined { get; set; }

    /// <summary>UTC, from the log line; null while still on the server.</summary>
    public DateTime? TimeLeft { get; set; }

    /// <summary>Set by the game through BloxstrapRPC's SetLaunchData; passed on in invite links.</summary>
    public string LaunchData { get; set; } = "";

    /// <summary>For a teleport within the same game, the session the player first joined (for history and play time).</summary>
    public GameSession? RootSession { get; set; }

    /// <summary>Private (10.x) addresses can't be located; Roblox reports the public proxy a moment later.</summary>
    public bool HasPublicAddress => MachineAddress.Length > 0 && !MachineAddress.StartsWith("10.", StringComparison.Ordinal);

    /// <summary>When the player started playing this game, counting teleports within it.</summary>
    public DateTime PlayingSince => RootSession?.TimeJoined ?? TimeJoined;

    /// <summary>A link that joins this exact server (Bloxstrap's invite deeplink).</summary>
    public string InviteDeeplink(bool includeLaunchData = true)
    {
        string link = $"roblox://experiences/start?placeId={PlaceId}&gameInstanceId={JobId}";

        if (includeLaunchData && LaunchData.Length > 0)
            link += "&launchData=" + Uri.EscapeDataString(LaunchData);

        return link;
    }

    public string GamePageUrl => $"https://www.roblox.com/games/{PlaceId}";

    public override string ToString() => $"{PlaceId}/{JobId}";
}
