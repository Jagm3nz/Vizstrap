using System.Text;
using System.Text.Json;
using Vizstrap.Core.Activity;
using Vizstrap.Core.Discord;
using Vizstrap.Core.Tests.TestSupport;

namespace Vizstrap.Core.Tests;

public class DiscordActivityTests
{
    private static JsonElement Json(DiscordActivity activity)
    {
        using var buffer = new MemoryStream();

        using (var writer = new Utf8JsonWriter(buffer))
            activity.WriteJson(writer);

        return JsonDocument.Parse(buffer.ToArray()).RootElement.Clone();
    }

    [Fact]
    public void Writes_Discords_activity_object()
    {
        var json = Json(new DiscordActivity
        {
            Details = "Obby",
            State = "by Builderman",
            Start = DateTimeOffset.FromUnixTimeSeconds(1_790_000_000),
            LargeImage = "https://tr.rbxcdn.com/obby.png",
            LargeText = "Obby",
            SmallImage = "roblox",
            SmallText = "Roblox",
            Buttons = [new("See game page", "https://www.roblox.com/games/1")],
        });

        Assert.Equal(0, json.GetProperty("type").GetInt32());
        Assert.Equal("Obby", json.GetProperty("details").GetString());
        Assert.Equal("by Builderman", json.GetProperty("state").GetString());
        Assert.Equal(1_790_000_000_000, json.GetProperty("timestamps").GetProperty("start").GetInt64()); // milliseconds
        Assert.False(json.GetProperty("timestamps").TryGetProperty("end", out _));
        Assert.Equal("https://tr.rbxcdn.com/obby.png", json.GetProperty("assets").GetProperty("large_image").GetString());
        Assert.Equal("Roblox", json.GetProperty("assets").GetProperty("small_text").GetString());
        Assert.Equal("https://www.roblox.com/games/1", json.GetProperty("buttons")[0].GetProperty("url").GetString());
    }

    [Fact]
    public void Keeps_texts_within_Discords_limits()
    {
        var json = Json(new DiscordActivity
        {
            Details = "X",
            State = new string('s', 200),
            Buttons =
            [
                new("A label that is much longer than thirty-two characters", "https://a"),
                new("Two", "https://b"),
                new("Three", "https://c"),
            ],
        });

        Assert.Equal("X⠀", json.GetProperty("details").GetString()); // Discord needs two characters
        Assert.Equal(128, json.GetProperty("state").GetString()!.Length);
        Assert.EndsWith("…", json.GetProperty("state").GetString());
        Assert.Equal(2, json.GetProperty("buttons").GetArrayLength());
        Assert.Equal(32, json.GetProperty("buttons")[0].GetProperty("label").GetString()!.Length);
    }

    [Fact]
    public void Leaves_out_what_is_not_set()
    {
        var json = Json(new DiscordActivity { Details = "Obby" });

        Assert.False(json.TryGetProperty("state", out _));
        Assert.False(json.TryGetProperty("timestamps", out _));
        Assert.False(json.TryGetProperty("assets", out _));
        Assert.False(json.TryGetProperty("buttons", out _));
    }
}

public class DiscordIpcClientTests
{
    private static readonly TimeSpan FastRetry = TimeSpan.FromMilliseconds(100);

    [Fact]
    public async Task Shakes_hands_then_sets_the_activity_for_its_process()
    {
        string prefix = FakeDiscord.NewPrefix();
        await using var discord = new FakeDiscord(prefix);
        await using var client = new DiscordIpcClient("123456", prefix, processId: 4242, retryInterval: FastRetry);

        client.SetActivity(new DiscordActivity { Details = "Obby" });
        client.Start();

        var (op, handshake) = await discord.NextFrameAsync();
        Assert.Equal(0, op);
        Assert.Equal(1, handshake.GetProperty("v").GetInt32());
        Assert.Equal("123456", handshake.GetProperty("client_id").GetString());

        var (commandOp, command) = await discord.NextFrameAsync();
        Assert.Equal(1, commandOp);
        Assert.Equal("SET_ACTIVITY", command.GetProperty("cmd").GetString());
        Assert.Equal(4242, command.GetProperty("args").GetProperty("pid").GetInt32());
        Assert.Equal("Obby", command.GetProperty("args").GetProperty("activity").GetProperty("details").GetString());
        Assert.False(string.IsNullOrEmpty(command.GetProperty("nonce").GetString()));

        Assert.True(await client.WaitUntilSentAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal("tester", client.ConnectedUser);
    }

    [Fact]
    public async Task Sends_the_latest_activity_once_Discord_starts()
    {
        string prefix = FakeDiscord.NewPrefix();
        await using var client = new DiscordIpcClient("1", prefix, retryInterval: FastRetry);
        client.Start();
        client.SetActivity(new DiscordActivity { Details = "old" });
        client.SetActivity(new DiscordActivity { Details = "new" });

        await Task.Delay(300); // Discord isn't running yet
        Assert.False(client.IsConnected);

        await using var discord = new FakeDiscord(prefix, index: 3);
        await discord.NextFrameAsync(); // handshake
        var (_, command) = await discord.NextFrameAsync();

        Assert.Equal("new", command.GetProperty("args").GetProperty("activity").GetProperty("details").GetString());
    }

    [Fact]
    public async Task Clearing_and_disposing_remove_the_activity()
    {
        string prefix = FakeDiscord.NewPrefix();
        await using var discord = new FakeDiscord(prefix);
        var client = new DiscordIpcClient("1", prefix, retryInterval: FastRetry);
        client.SetActivity(new DiscordActivity { Details = "Obby" });
        client.Start();
        await discord.NextFrameAsync();
        await discord.NextFrameAsync();

        client.SetActivity(null);
        var (_, cleared) = await discord.NextFrameAsync();
        Assert.False(cleared.GetProperty("args").TryGetProperty("activity", out _));

        client.SetActivity(new DiscordActivity { Details = "again" });
        await discord.NextFrameAsync();

        await client.DisposeAsync();
        var (_, disposed) = await discord.NextFrameAsync();
        Assert.False(disposed.GetProperty("args").TryGetProperty("activity", out _));
    }

    [Fact]
    public async Task Answers_pings()
    {
        string prefix = FakeDiscord.NewPrefix();
        await using var discord = new FakeDiscord(prefix);
        await using var client = new DiscordIpcClient("1", prefix, retryInterval: FastRetry);
        client.Start();
        await discord.NextFrameAsync();
        await discord.NextFrameAsync();

        await discord.SendAsync(3, """{"ping":1}""");
        var (op, pong) = await discord.NextFrameAsync();

        Assert.Equal(4, op);
        Assert.Equal(1, pong.GetProperty("ping").GetInt32());
    }
}

public class GamePresenceTests
{
    internal static readonly PresenceTexts Texts = new(
        "by {0}", "In a private server", "In a reserved server", "Join server", "See game page", "Playing on {0} (@{1})");

    private static GameSession Session(ServerType type = ServerType.Public, string launchData = "") => new()
    {
        PlaceId = 1818,
        JobId = "abc",
        UniverseId = 13058,
        ServerType = type,
        LaunchData = launchData,
        TimeJoined = new DateTime(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc),
    };

    private static readonly UniverseInfo Universe = new(13058, 1818, "Classic: Crossroads", "Roblox", true, "https://tr.rbxcdn.com/cross.png");

    [Fact]
    public void Shows_the_game_its_creator_and_the_time_played()
    {
        var activity = GamePresence.Build(Session(), Universe, null, new PresenceOptions(false, false), Texts);

        Assert.Equal("Classic: Crossroads", activity.Details);
        Assert.Equal("by Roblox ☑️", activity.State);
        Assert.Equal(DateTimeOffset.Parse("2026-09-24T12:00:00Z"), activity.Start);
        Assert.Equal("https://tr.rbxcdn.com/cross.png", activity.LargeImage);
        Assert.Equal(GamePresence.RobloxImageKey, activity.SmallImage);
        Assert.Equal(["See game page"], activity.Buttons.Select(button => button.Label));
    }

    [Theory]
    [InlineData(ServerType.Private, "In a private server")]
    [InlineData(ServerType.Reserved, "In a reserved server")]
    public void Private_and_reserved_servers_say_so_instead_of_the_creator(ServerType type, string state) =>
        Assert.Equal(state, GamePresence.Build(Session(type), Universe, null, new PresenceOptions(false, false), Texts).State);

    [Theory]
    [InlineData(ServerType.Public, "", true)]
    [InlineData(ServerType.Private, "", false)]
    [InlineData(ServerType.Reserved, "", false)]
    [InlineData(ServerType.Reserved, "lobby=7", true)]
    public void Joining_is_offered_only_for_servers_others_can_get_into(ServerType type, string launchData, bool joinable)
    {
        var buttons = GamePresence.Buttons(Session(type, launchData), new PresenceOptions(true, false), Texts);

        Assert.Equal(joinable, buttons.Any(button => button.Label == "Join server"));
        Assert.Equal("See game page", buttons[^1].Label);

        if (joinable)
            Assert.StartsWith("roblox://experiences/start?placeId=1818&gameInstanceId=abc", buttons[0].Url);
    }

    [Fact]
    public void Joining_needs_the_setting()
    {
        var buttons = GamePresence.Buttons(Session(), new PresenceOptions(false, false), Texts);

        Assert.DoesNotContain(buttons, button => button.Label == "Join server");
    }

    [Fact]
    public void The_account_replaces_the_Roblox_logo_when_enabled()
    {
        var user = new UserInfo(42, "builder_42", "Builder", "https://tr.rbxcdn.com/42.png");

        var activity = GamePresence.Build(Session(), Universe, user, new PresenceOptions(false, true), Texts);

        Assert.Equal("https://tr.rbxcdn.com/42.png", activity.SmallImage);
        Assert.Equal("Playing on Builder (@builder_42)", activity.SmallText);
    }

    [Theory]
    [InlineData(ServerType.Public, "Classic: Crossroads · by Roblox ☑️")]
    [InlineData(ServerType.Private, "Classic: Crossroads · In a private server")]
    public void The_watermark_takes_the_line_under_the_game_and_the_rest_moves_to_the_icon(ServerType type, string iconText)
    {
        var activity = GamePresence.Build(Session(type), Universe, null, new PresenceOptions(false, false, Watermark: true), Texts);

        Assert.Equal("Classic: Crossroads", activity.Details);
        Assert.Equal("Powered by Vizstrap", activity.State);
        Assert.Equal(iconText, activity.LargeText);
    }

    [Fact]
    public void An_own_Discord_application_shows_the_Vizstrap_logo_instead_of_Roblox_s()
    {
        var options = new PresenceOptions(false, false, BrandImage: "vizstrap_violet");

        var game = GamePresence.Build(Session(), Universe, null, options, Texts);
        var menu = GamePresence.BuildMenu(DateTime.UtcNow, options, Texts);

        Assert.Equal("vizstrap_violet", game.SmallImage);
        Assert.Equal("Powered by Vizstrap", game.SmallText);
        Assert.Equal("vizstrap_violet", menu.LargeImage);

        // without one (Bloxstrap's application) the Roblox logo stays
        Assert.Equal(GamePresence.RobloxImageKey, GamePresence.Build(Session(), Universe, null, new PresenceOptions(false, false), Texts).SmallImage);
    }

    [Fact]
    public void Teleports_within_a_game_keep_counting_from_the_first_join()
    {
        var first = Session();
        var teleported = new GameSession
        {
            PlaceId = 2,
            JobId = "def",
            UniverseId = 13058,
            IsTeleport = true,
            RootSession = first,
            TimeJoined = first.TimeJoined.AddMinutes(10),
        };

        Assert.Equal(DateTimeOffset.Parse("2026-09-24T12:00:00Z"),
            GamePresence.Build(teleported, Universe, null, new PresenceOptions(false, false), Texts).Start);
    }
}

public class RichPresenceControllerTests
{
    private static FakeHttpHandler Roblox()
    {
        var http = new FakeHttpHandler();
        http.MapText("https://games.roblox.com/v1/games?universeIds=13058",
            """{"data":[{"id":13058,"rootPlaceId":1818,"name":"Classic: Crossroads","creator":{"name":"Roblox","hasVerifiedBadge":false}}]}""");
        http.MapText("https://thumbnails.roblox.com/v1/games/icons?universeIds=13058&returnPolicy=PlaceHolder&size=512x512&format=Png&isCircular=false",
            """{"data":[{"targetId":13058,"state":"Completed","imageUrl":"https://tr.rbxcdn.com/cross.png"}]}""");
        http.MapText("https://thumbnails.roblox.com/v1/assets?assetIds=555&returnPolicy=PlaceHolder&size=512x512&format=Png&isCircular=false",
            """{"data":[{"targetId":555,"state":"Completed","imageUrl":"https://tr.rbxcdn.com/555.png"}]}""");
        http.MapText("https://apis.roblox.com/universes/v1/places/1818/universe", """{"universeId":13058}""");
        return http;
    }

    private static (RichPresenceController Controller, List<DiscordActivity?> Published) Create(
        FakeHttpHandler http, PresenceOptions? options = null)
    {
        var published = new List<DiscordActivity?>();
        var controller = new RichPresenceController(new RobloxWebApi(new HttpClient(http)),
            activity => { lock (published) published.Add(activity); },
            options ?? new PresenceOptions(true, false), GamePresenceTests.Texts);
        return (controller, published);
    }

    private static GameSession Session(long universeId = 13058) => new()
    {
        PlaceId = 1818,
        JobId = "abc",
        UniverseId = universeId,
        TimeJoined = DateTime.UtcNow,
    };

    private static RpcMessage Message(string json)
    {
        using var document = JsonDocument.Parse(json);
        return new RpcMessage(document.RootElement.GetProperty("command").GetString()!, document.RootElement.GetProperty("data").Clone());
    }

    [Fact]
    public async Task Shows_the_game_on_join_and_nothing_after_leaving()
    {
        var (controller, published) = Create(Roblox());

        await controller.OnGameJoinedAsync(Session());
        var shown = Assert.Single(published)!;
        Assert.Equal("Classic: Crossroads", shown.Details);
        Assert.Equal("by Roblox", shown.State);
        Assert.Equal(["Join server", "See game page"], shown.Buttons.Select(button => button.Label));

        await controller.OnGameLeftAsync();
        Assert.Null(published[^1]);
    }

    [Fact]
    public async Task With_the_menu_presence_Roblox_shows_between_games_instead_of_nothing()
    {
        var started = new DateTime(2026, 9, 24, 20, 0, 0, DateTimeKind.Utc);
        var published = new List<DiscordActivity?>();
        var controller = new RichPresenceController(new RobloxWebApi(new HttpClient(Roblox())),
            activity => { lock (published) published.Add(activity); },
            new PresenceOptions(false, false, Watermark: true, ShowInMenu: true), GamePresenceTests.Texts, started);

        await controller.OnRobloxStartedAsync();
        Assert.Equal("In the Roblox app", published[^1]!.Details);
        Assert.Equal("Powered by Vizstrap", published[^1]!.State);
        Assert.Equal(new DateTimeOffset(started), published[^1]!.Start);

        await controller.OnGameJoinedAsync(Session());
        Assert.Equal("Classic: Crossroads", published[^1]!.Details);

        await controller.OnGameLeftAsync();
        Assert.Equal("In the Roblox app", published[^1]!.Details);

        // BloxstrapRPC only changes a game's presence
        await controller.OnRpcMessageAsync(Message("{\"command\":\"SetRichPresence\",\"data\":{\"details\":\"Round 3\"}}"));
        Assert.Equal("In the Roblox app", published[^1]!.Details);
    }

    [Fact]
    public async Task Without_the_menu_presence_nothing_shows_between_games()
    {
        var (controller, published) = Create(Roblox());

        await controller.OnRobloxStartedAsync();

        Assert.Null(Assert.Single(published));
    }

    [Fact]
    public async Task Finds_the_game_from_the_place_when_the_log_had_no_universe()
    {
        var (controller, published) = Create(Roblox());

        await controller.OnGameJoinedAsync(Session(universeId: 0));

        Assert.Equal("Classic: Crossroads", Assert.Single(published)!.Details);
    }

    [Fact]
    public async Task Shows_nothing_when_Roblox_cant_be_reached()
    {
        var (controller, published) = Create(new FakeHttpHandler());

        await controller.OnGameJoinedAsync(Session());

        Assert.Empty(published);
    }

    [Fact]
    public async Task Games_change_texts_and_images_through_BloxstrapRPC()
    {
        var (controller, published) = Create(Roblox());
        await controller.OnGameJoinedAsync(Session());

        await controller.OnRpcMessageAsync(Message(
            """{"command":"SetRichPresence","data":{"details":"Round 3","state":"Red team","timeEnd":1790000600,"largeImage":{"assetId":555,"hoverText":"Map: Desert"},"smallImage":{"clear":true}}}"""));

        var changed = published[^1]!;
        Assert.Equal("Round 3", changed.Details);
        Assert.Equal("Red team", changed.State);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1790000600), changed.End);
        Assert.Equal("https://tr.rbxcdn.com/555.png", changed.LargeImage);
        Assert.Equal("Map: Desert", changed.LargeText);
        Assert.Null(changed.SmallImage);

        await controller.OnRpcMessageAsync(Message(
            """{"command":"SetRichPresence","data":{"details":"<reset>","timeStart":0,"largeImage":{"reset":true}}}"""));

        var reset = published[^1]!;
        Assert.Equal("Classic: Crossroads", reset.Details);
        Assert.Equal("Red team", reset.State);
        Assert.Null(reset.Start);
        Assert.Equal("https://tr.rbxcdn.com/cross.png", reset.LargeImage);
    }

    [Fact]
    public async Task Texts_longer_than_Discord_allows_are_ignored()
    {
        var (controller, published) = Create(Roblox());
        await controller.OnGameJoinedAsync(Session());

        await controller.OnRpcMessageAsync(Message($$$"""{"command":"SetRichPresence","data":{"details":"{{{new string('x', 129)}}}"}}"""));

        Assert.Equal("Classic: Crossroads", published[^1]!.Details);
    }

    [Fact]
    public async Task The_account_picture_is_not_replaced_by_the_game()
    {
        var http = Roblox();
        http.MapText("https://users.roblox.com/v1/users/42", """{"id":42,"name":"builder_42","displayName":"Builder"}""");
        http.MapText("https://thumbnails.roblox.com/v1/users/avatar-headshot?userIds=42&size=180x180&format=Png&isCircular=false",
            """{"data":[{"targetId":42,"state":"Completed","imageUrl":"https://tr.rbxcdn.com/42.png"}]}""");
        var (controller, published) = Create(http, new PresenceOptions(false, true));
        var session = Session();
        session.UserId = 42;

        await controller.OnGameJoinedAsync(session);
        await controller.OnRpcMessageAsync(Message("""{"command":"SetRichPresence","data":{"smallImage":{"assetId":555}}}"""));

        Assert.Equal("https://tr.rbxcdn.com/42.png", published[^1]!.SmallImage);
    }

    [Fact]
    public async Task Launch_data_makes_a_reserved_server_joinable()
    {
        var (controller, published) = Create(Roblox());
        var session = Session();
        session.ServerType = ServerType.Reserved;
        await controller.OnGameJoinedAsync(session);
        Assert.DoesNotContain(published[^1]!.Buttons, button => button.Label == "Join server");

        session.LaunchData = "lobby=7"; // the tracker stores it before passing the message on
        await controller.OnRpcMessageAsync(Message("""{"command":"SetLaunchData","data":"lobby=7"}"""));

        Assert.Contains(published[^1]!.Buttons, button => button.Url.EndsWith("&launchData=lobby%3D7"));
    }

    [Fact]
    public async Task Hiding_clears_Discord_and_showing_brings_the_game_back()
    {
        var (controller, published) = Create(Roblox());
        await controller.OnGameJoinedAsync(Session());

        await controller.SetVisibleAsync(false);
        Assert.Null(published[^1]);

        await controller.SetVisibleAsync(true);
        Assert.Equal("Classic: Crossroads", published[^1]!.Details);
    }

    [Fact]
    public async Task Messages_before_the_game_is_shown_apply_after_it()
    {
        var (controller, published) = Create(Roblox());

        var joining = controller.OnGameJoinedAsync(Session());
        var message = controller.OnRpcMessageAsync(Message("""{"command":"SetRichPresence","data":{"state":"Lobby"}}"""));
        await Task.WhenAll(joining, message);

        Assert.Equal("Lobby", published[^1]!.State);
    }
}
