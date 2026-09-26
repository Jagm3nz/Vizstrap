using Vizstrap.Core.Activity;
using Vizstrap.Core.Appearance;

namespace Vizstrap.Core.Discord;

/// <summary>The presence's texts, in Vizstrap's UI language.</summary>
/// <param name="ByCreator">"by {0}" (the creator).</param>
/// <param name="PlayingAs">"Playing on {0} (@{1})" (display name, user name).</param>
/// <param name="InMenu">"In the Roblox app", for when no game is open.</param>
public sealed record PresenceTexts(
    string ByCreator,
    string PrivateServer,
    string ReservedServer,
    string JoinServer,
    string SeeGamePage,
    string PlayingAs,
    string InMenu = "In the Roblox app");

/// <param name="AllowJoining">Show a "Join server" button (Bloxstrap's "Allow activity joining").</param>
/// <param name="ShowAccount">Show the Roblox account as the small image (Bloxstrap's "Show Roblox account").</param>
/// <param name="Watermark">"Powered by Vizstrap" as the line under the game.</param>
/// <param name="ShowInMenu">Keep a presence while Roblox is open outside a game (Bloxstrap shows none then).</param>
/// <param name="BrandImage">
/// Image key of the Vizstrap logo in the accent colour; only an own Discord application has it, so
/// null means the Roblox logo is used as before.
/// </param>
public sealed record PresenceOptions(
    bool AllowJoining, bool ShowAccount, bool Watermark = false, bool ShowInMenu = false, string? BrandImage = null);

/// <summary>Bloxstrap's Rich Presence for a game: its name, creator, play time, icon and buttons.</summary>
public static class GamePresence
{
    /// <summary>Image key of the Roblox logo in the Discord application.</summary>
    public const string RobloxImageKey = "roblox";

    /// <summary>A brand mark, the same in every language.</summary>
    public const string Watermark = "Powered by Vizstrap";

    /// <summary>Image key of the Vizstrap logo in an accent colour, as uploaded to an own Discord application.</summary>
    public static string LogoKey(AccentColor accent) => "vizstrap_" + accent.ToString().ToLowerInvariant();

    public static DiscordActivity Build(GameSession session, UniverseInfo universe, UserInfo? user,
        PresenceOptions options, PresenceTexts texts)
    {
        string state = session.ServerType switch
        {
            ServerType.Private => texts.PrivateServer,
            ServerType.Reserved => texts.ReservedServer,
            _ => string.Format(texts.ByCreator, universe.CreatorName) + (universe.CreatorVerified ? " ☑️" : ""),
        };

        bool showAccount = options.ShowAccount && user is not null;

        // Discord shows only one line under the game, so with the watermark there the creator (or
        // server type) moves into the text shown over the game's icon
        return new DiscordActivity
        {
            Details = universe.Name,
            State = options.Watermark ? Watermark : state,
            Start = new DateTimeOffset(DateTime.SpecifyKind(session.PlayingSince, DateTimeKind.Utc)),
            LargeImage = universe.IconUrl ?? RobloxImageKey,
            LargeText = options.Watermark ? $"{universe.Name} · {state}" : universe.Name,
            SmallImage = showAccount ? user!.HeadshotUrl ?? RobloxImageKey : options.BrandImage ?? RobloxImageKey,
            SmallText = showAccount ? string.Format(texts.PlayingAs, user!.DisplayName, user.Name) :
                options.BrandImage is null ? "Roblox" : Watermark,
            Buttons = Buttons(session, options, texts),
        };
    }

    /// <summary>Roblox is open but no game is: its app (home page, avatar, friends…), counted from <paramref name="since"/>.</summary>
    public static DiscordActivity BuildMenu(DateTime since, PresenceOptions options, PresenceTexts texts) => new()
    {
        Details = texts.InMenu,
        State = options.Watermark ? Watermark : null,
        Start = new DateTimeOffset(DateTime.SpecifyKind(since, DateTimeKind.Utc)),
        LargeImage = options.BrandImage ?? RobloxImageKey,
        LargeText = options.BrandImage is null ? "Roblox" : Watermark,
    };

    /// <summary>
    /// "Join server" for public servers (and reserved ones the game made joinable with launch data),
    /// then "See game page".
    /// </summary>
    public static IReadOnlyList<DiscordButton> Buttons(GameSession session, PresenceOptions options, PresenceTexts texts)
    {
        var buttons = new List<DiscordButton>();

        bool joinable = session.ServerType == ServerType.Public ||
            (session.ServerType == ServerType.Reserved && session.LaunchData.Length > 0);

        if (options.AllowJoining && joinable)
            buttons.Add(new DiscordButton(texts.JoinServer, session.InviteDeeplink()));

        buttons.Add(new DiscordButton(texts.SeeGamePage, session.GamePageUrl));
        return buttons;
    }
}
