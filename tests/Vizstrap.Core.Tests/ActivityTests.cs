using System.Net;
using System.Text;
using Vizstrap.Core.Activity;
using Vizstrap.Core.Tests.TestSupport;

namespace Vizstrap.Core.Tests;

public class ActivityTrackerTests
{
    private const string Job1 = "d9758c4f-19b0-4512-9118-4306aec5fdd1";
    private const string Job2 = "6599cafc-8bdc-47d1-85d5-f44e98bc69af";

    /// <summary>A log line as Roblox 0.740 writes it, stamped <paramref name="second"/> seconds after 12:00:00 UTC.</summary>
    private static string Line(int second, string message) =>
        $"2026-09-24T12:{second / 60:00}:{second % 60:00}.816Z,{second}.816669,1aac,6 {message}";

    private static IEnumerable<string> Join(int second, string job, long place, long universe, string rcc = "10.34.7.132",
        string udmux = "128.116.31.33", string referral = "") =>
    [
        Line(second, $"[FLog::Output] ! Joining game '{job}' place {place} at {rcc}"),
        Line(second, $"[FLog::GameJoinLoadTime] Report game_join_loadtime: placeid:{place}, join_time:0.446, universeid:{universe}, referral_page:{referral}, sid:9f1bca76, clienttime:1790265518.5, userid:42, "),
        Line(second, $"[FLog::Network] UDMUX Address = {udmux}, Port = 58358 | RCC Server Address = {rcc}, Port = 58358"),
        Line(second + 1, $"[FLog::Network] serverId: {udmux}|58358"),
        Line(second + 1, $"[FLog::Network] Replicator created for player {udmux}|58358"),
        Line(second + 1, "[FLog::Network] Replicator created: 000000004D876F30"),
    ];

    private static string Leave(int second) => Line(second, "[FLog::SingleSurfaceApp] leaveUGCGameInternal");

    private static string Disconnect(int second) => Line(second, "[FLog::Network] Time to disconnect replication data: 0.784300");

    private static string Teleport(int second, int joinType = 10) =>
        Line(second, $"[FLog::UgcExperienceController] UgcExperienceController: doTeleport: joinScriptUrl https://assetgame.roblox.com/Game/Join.ashx?ticketVersion=2&ticket={{\"UserId\"%3a42%2c\"JoinTypeId\"%3a{joinType}%2c\"x\"%3a1}}");

    private static string Rpc(int second, string json) => Line(second, $"[FLog::CreatorOutput] [BloxstrapRPC] {json}");

    private static ActivityTracker Feed(IEnumerable<string> lines, ActivityTracker? tracker = null)
    {
        tracker ??= new ActivityTracker();

        foreach (string line in lines)
            tracker.ProcessLine(line);

        return tracker;
    }

    [Fact]
    public void Joining_a_game_reads_the_server_the_game_and_the_player()
    {
        var joined = new List<GameSession>();
        var tracker = new ActivityTracker();
        tracker.GameJoined += (_, session) => joined.Add(session);

        Feed(Join(37, Job1, 84637477108843, 10515143755), tracker);

        var session = Assert.Single(joined);
        Assert.Same(session, tracker.Current);
        Assert.Equal(84637477108843, session.PlaceId);
        Assert.Equal(Job1, session.JobId);
        Assert.Equal(10515143755, session.UniverseId);
        Assert.Equal(42, session.UserId);
        Assert.Equal("128.116.31.33", session.MachineAddress); // the public UDMUX proxy, not the 10.x server
        Assert.True(session.HasPublicAddress);
        Assert.Equal(ServerType.Public, session.ServerType);
        Assert.False(session.IsTeleport);
        Assert.Equal(new DateTime(2026, 9, 24, 12, 0, 38, 816, DateTimeKind.Utc), session.TimeJoined);
        Assert.Equal(DateTimeKind.Utc, session.TimeJoined.Kind);
    }

    [Fact]
    public void Leaving_a_game_moves_it_to_the_history()
    {
        var left = new List<GameSession>();
        int appClosed = 0;
        var tracker = new ActivityTracker();
        tracker.GameLeft += (_, session) => left.Add(session);
        tracker.AppClosed += (_, _) => appClosed++;

        Feed([.. Join(0, Job1, 1, 11), Leave(90), Disconnect(90)], tracker);

        Assert.Null(tracker.Current);
        Assert.Equal(1, appClosed);
        var session = Assert.Single(left);
        Assert.Same(session, Assert.Single(tracker.History));
        Assert.Equal(new DateTime(2026, 9, 24, 12, 1, 30, 816, DateTimeKind.Utc), session.TimeLeft);
    }

    /// <summary>Roblox 0.740 connects to the new server before it disconnects from the old one.</summary>
    [Fact]
    public void A_teleport_switches_games_even_though_the_old_server_disconnects_afterwards()
    {
        var events = new List<string>();
        var tracker = new ActivityTracker();
        tracker.GameJoined += (_, session) => events.Add($"joined {session.PlaceId}");
        tracker.GameLeft += (_, session) => events.Add($"left {session.PlaceId}");

        Feed([.. Join(0, Job1, 1, 11), Teleport(20), .. Join(21, Job2, 2, 11), Disconnect(23)], tracker);

        Assert.Equal(["joined 1", "left 1", "joined 2"], events);

        var current = tracker.Current!;
        Assert.Equal(2, current.PlaceId);
        Assert.True(current.IsTeleport);
        Assert.Same(tracker.History[0], current.RootSession); // same game: the visit continues
        Assert.Equal(tracker.History[0].TimeJoined, current.PlayingSince);

        Feed([Leave(100), Disconnect(100)], tracker);

        Assert.Equal("left 2", events[^1]);
        Assert.Null(tracker.Current);
        Assert.Equal([2L, 1L], tracker.History.Select(session => session.PlaceId));
    }

    [Fact]
    public void A_teleport_to_another_game_starts_a_new_visit()
    {
        var tracker = Feed([.. Join(0, Job1, 1, 11), Teleport(20), .. Join(21, Job2, 2, 22), Disconnect(23)]);

        Assert.True(tracker.Current!.IsTeleport);
        Assert.Null(tracker.Current.RootSession);
        Assert.Equal(tracker.Current.TimeJoined, tracker.Current.PlayingSince);
    }

    [Fact]
    public void A_teleport_with_Bloxstraps_order_of_entries_works_too()
    {
        var tracker = Feed([.. Join(0, Job1, 1, 11), Teleport(20), Disconnect(20), .. Join(21, Job2, 2, 11)]);

        Assert.Equal(2, tracker.Current!.PlaceId);
        Assert.Same(tracker.History[0], tracker.Current.RootSession);
    }

    [Fact]
    public void A_cancelled_join_is_forgotten()
    {
        var joined = new List<GameSession>();
        var tracker = new ActivityTracker();
        tracker.GameJoined += (_, session) => joined.Add(session);

        Feed([Line(0, $"[FLog::Output] ! Joining game '{Job1}' place 1 at 10.34.7.132"), Leave(3)], tracker);
        Feed(Join(10, Job2, 2, 22), tracker);

        Assert.Equal(2, Assert.Single(joined).PlaceId);
        Assert.Empty(tracker.History);
    }

    [Theory]
    [InlineData("RequestPrivateGame", ServerType.Private)]
    [InlineData("GameDetailPageJSHybridEvent", ServerType.Private)]
    [InlineData("HomePage", ServerType.Public)]
    public void The_referral_page_tells_private_servers_apart(string referral, ServerType expected)
    {
        var tracker = Feed(Join(0, Job1, 1, 11, referral: referral));

        Assert.Equal(expected, tracker.Current!.ServerType);
    }

    [Theory]
    [InlineData(4, ServerType.Reserved)]
    [InlineData(6, ServerType.Reserved)]
    [InlineData(10, ServerType.Public)]
    public void Teleports_to_reserved_servers_are_recognised(int joinType, ServerType expected)
    {
        var tracker = Feed([.. Join(0, Job1, 1, 11), Teleport(20, joinType), .. Join(21, Job2, 2, 11)]);

        Assert.Equal(expected, tracker.Current!.ServerType);
    }

    [Fact]
    public void Lines_that_continue_a_previous_entry_are_ignored()
    {
        var tracker = Feed([
            .. Join(0, Job1, 1, 11),
            "  \"DFIntTextureQualityOverride\": \"3\",",
            "}\"",
            "",
            "[FLog::Output] All use of Roblox services must comply with Roblox's Terms of Use",
        ]);

        Assert.Equal(1, tracker.Current!.PlaceId);
    }

    [Fact]
    public void BloxstrapRPC_messages_reach_listeners_while_in_a_game()
    {
        var messages = new List<RpcMessage>();
        var tracker = new ActivityTracker();
        tracker.RpcMessageReceived += (_, message) => messages.Add(message);

        Feed([Rpc(0, """{"command":"SetRichPresence","data":{"details":"early"}}""")], tracker); // not in a game yet
        Feed(Join(1, Job1, 1, 11), tracker);
        Feed([Rpc(5, """{"command":"SetRichPresence","data":{"details":"Round 1"}}""")], tracker);

        var message = Assert.Single(messages);
        Assert.Equal("SetRichPresence", message.Command);
        Assert.Equal("Round 1", message.Data.GetProperty("details").GetString());
    }

    [Fact]
    public void BloxstrapRPC_messages_are_limited_to_one_a_second_by_log_time()
    {
        var messages = new List<RpcMessage>();
        var tracker = Feed(Join(0, Job1, 1, 11));
        tracker.RpcMessageReceived += (_, message) => messages.Add(message);

        Feed([
            Rpc(5, """{"command":"SetRichPresence","data":{"state":"a"}}"""),
            Rpc(5, """{"command":"SetRichPresence","data":{"state":"b"}}"""),
            Rpc(6, """{"command":"SetRichPresence","data":{"state":"c"}}"""),
        ], tracker);

        Assert.Equal(["a", "c"], messages.Select(message => message.Data.GetProperty("state").GetString()));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"data":{}}""")]
    [InlineData("""{"command":""}""")]
    [InlineData("[1,2]")]
    public void Unreadable_BloxstrapRPC_messages_are_ignored(string json)
    {
        int count = 0;
        var tracker = Feed(Join(0, Job1, 1, 11));
        tracker.RpcMessageReceived += (_, _) => count++;

        Feed([Rpc(5, json)], tracker);

        Assert.Equal(0, count);
    }

    [Fact]
    public void SetLaunchData_is_kept_for_invite_links()
    {
        var tracker = Feed(Join(0, Job1, 1, 11));

        Feed([Rpc(5, """{"command":"SetLaunchData","data":"team=red&x=1"}""")], tracker);
        Assert.Equal("team=red&x=1", tracker.Current!.LaunchData);

        Feed([Rpc(7, $$"""{"command":"SetLaunchData","data":"{{new string('x', 201)}}"}""")], tracker);
        Assert.Equal("team=red&x=1", tracker.Current.LaunchData); // too long, refused like in Bloxstrap
    }
}

public class GameHistoryTests
{
    private static readonly DateTime Noon = new(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc);

    private static GameSession Session(long place, string job, int joinedMinute, int leftMinute,
        GameSession? root = null, ServerType type = ServerType.Public) => new()
    {
        PlaceId = place,
        JobId = job,
        UniverseId = 11,
        ServerType = type,
        RootSession = root,
        TimeJoined = Noon.AddMinutes(joinedMinute),
        TimeLeft = Noon.AddMinutes(leftMinute),
    };

    [Fact]
    public void Teleports_within_a_game_fold_into_one_visit()
    {
        var lobby = Session(1, "lobby", 0, 10);
        var round = Session(2, "round", 10, 25, root: lobby);
        var lobbyAgain = Session(1, "lobby2", 25, 40, root: lobby);
        var other = Session(9, "other", 45, 50);

        var visits = GameHistory.Visits([other, lobbyAgain, round, lobby]);

        Assert.Equal(2, visits.Count);
        Assert.Same(other, visits[0].First);

        var visit = visits[1];
        Assert.Same(lobby, visit.First);
        Assert.Equal(Noon, visit.Joined);
        Assert.Equal(Noon.AddMinutes(40), visit.Left);
        Assert.Same(lobbyAgain, visit.RejoinTarget); // the newest public server, with its own place
        Assert.Equal("roblox://experiences/start?placeId=1&gameInstanceId=lobby2", visit.RejoinDeeplink);
    }

    [Fact]
    public void Visits_without_a_public_server_rejoin_where_they_started()
    {
        var start = Session(1, "a", 0, 5, type: ServerType.Private);
        var reserved = Session(2, "b", 5, 9, root: start, type: ServerType.Reserved);

        var visit = Assert.Single(GameHistory.Visits([reserved, start]));

        Assert.Same(start, visit.RejoinTarget);
    }
}

public class GameSessionTests
{
    [Fact]
    public void Invite_links_join_the_exact_server_and_carry_launch_data()
    {
        var session = new GameSession { PlaceId = 1818, JobId = "abc", LaunchData = "team=red&x=1" };

        Assert.Equal("roblox://experiences/start?placeId=1818&gameInstanceId=abc&launchData=team%3Dred%26x%3D1", session.InviteDeeplink());
        Assert.Equal("roblox://experiences/start?placeId=1818&gameInstanceId=abc", session.InviteDeeplink(includeLaunchData: false));
        Assert.Equal("https://www.roblox.com/games/1818", session.GamePageUrl);
    }

    [Theory]
    [InlineData("128.116.31.33", true)]
    [InlineData("10.34.7.132", false)]
    [InlineData("", false)]
    public void Only_public_addresses_can_be_located(string address, bool expected) =>
        Assert.Equal(expected, new GameSession { PlaceId = 1, JobId = "a", MachineAddress = address }.HasPublicAddress);
}

public class RobloxLogLocatorTests
{
    private const string VersionDirectory = @"C:\Users\Test\AppData\Local\Vizstrap\Versions\version-2366ba214ec740ca";

    private static string OurHead(string versionDirectory = VersionDirectory) =>
        "2026-09-24T15:58:02.163Z,0.163890,5a94,6,Warning [FLog::RobloxStarterNetworkStarterModule] userAgent: Roblox/WinInetRobloxApp/0.740.0.7400927 (GlobalDist; RobloxDirectDownload)\n" +
        $"2026-09-24T15:58:02.166Z,0.166890,5a94,6,Info [FLog::UpdateController] WindowsUpdateController: updaterFullPath: {versionDirectory}\\RobloxPlayerInstaller.exe\n";

    private const string TrayHead =
        "2026-09-24T15:58:00.194Z,0.194430,5eb0,6,Warning [FLog::RobloxStarterNetworkStarterModule] userAgent: Roblox/WinInetRobloxApp/0.740.0.7400927 (GlobalDist; RobloxDirectDownload) AppState/TrayMode\n" +
        "2026-09-24T15:58:00.196Z,0.196437,5eb0,6,Info [FLog::UpdateController] WindowsUpdateController: updaterFullPath: C:\\Users\\Test\\AppData\\Local\\Roblox\\Versions\\version-2366ba214ec740ca\\RobloxPlayerInstaller.exe\n";

    [Fact]
    public async Task The_tray_process_log_is_skipped_even_when_it_appears_first()
    {
        using var logs = new TempDirectory();
        var started = DateTime.UtcNow;
        File.WriteAllText(logs.Combine("0.740_20260924T155800Z_Player_46C01_last.log"), TrayHead);
        File.WriteAllText(logs.Combine("0.740_20260924T155802Z_Player_33780_last.log"), OurHead());

        string? log = await RobloxLogLocator.FindAsync(logs.Path, VersionDirectory, started, TimeSpan.FromSeconds(5), default);

        Assert.Equal(logs.Combine("0.740_20260924T155802Z_Player_33780_last.log"), log);
    }

    [Fact]
    public async Task Waits_for_the_log_to_be_written()
    {
        using var logs = new TempDirectory();
        var started = DateTime.UtcNow;
        string path = logs.Combine("late_Player_last.log");

        var finding = RobloxLogLocator.FindAsync(logs.Path, VersionDirectory, started, TimeSpan.FromSeconds(10), default);
        await Task.Delay(400);
        File.WriteAllText(path, "[FLog::Output] All use of Roblox services must comply with Roblox's Terms of Use\n");
        await Task.Delay(400);
        File.AppendAllText(path, OurHead());

        Assert.Equal(path, await finding);
    }

    [Fact]
    public async Task Logs_from_before_the_launch_are_ignored()
    {
        using var logs = new TempDirectory();
        string old = logs.Combine("old_Player_last.log");
        File.WriteAllText(old, OurHead());
        File.SetCreationTimeUtc(old, DateTime.UtcNow.AddHours(-1));

        string? log = await RobloxLogLocator.FindAsync(logs.Path, VersionDirectory, DateTime.UtcNow, TimeSpan.FromMilliseconds(600), default);

        Assert.Null(log);
    }

    [Fact]
    public async Task Falls_back_to_a_log_that_is_not_the_tray_process_when_none_mentions_the_folder()
    {
        using var logs = new TempDirectory();
        var started = DateTime.UtcNow;
        File.WriteAllText(logs.Combine("a_Player_last.log"), TrayHead);
        File.WriteAllText(logs.Combine("b_Player_last.log"), OurHead(@"D:\Elsewhere\version-1"));

        string? log = await RobloxLogLocator.FindAsync(logs.Path, VersionDirectory, started, TimeSpan.FromMilliseconds(600), default);

        Assert.Equal(logs.Combine("b_Player_last.log"), log);
    }

    [Fact]
    public void A_version_folder_is_not_mistaken_for_one_whose_name_it_starts()
    {
        using var logs = new TempDirectory();
        string path = logs.Combine("x.log");
        File.WriteAllText(path, OurHead(VersionDirectory + "ff"));

        Assert.Equal(RobloxLogLocator.Owner.Unknown, RobloxLogLocator.Inspect(path, VersionDirectory));
        Assert.Equal(RobloxLogLocator.Owner.Ours, RobloxLogLocator.Inspect(path, VersionDirectory + "ff"));
    }
}

public class LogTailerTests
{
    [Fact]
    public void Only_complete_lines_are_taken()
    {
        var pending = new StringBuilder("first\r\nsecond\nthi");

        Assert.Equal(["first", "second"], LogTailer.TakeCompleteLines(pending));
        Assert.Equal("thi", pending.ToString());

        pending.Append("rd\n");
        Assert.Equal(["third"], LogTailer.TakeCompleteLines(pending));
        Assert.Equal("", pending.ToString());
    }

    [Fact]
    public async Task Follows_lines_appended_by_a_writer_that_keeps_the_file_open()
    {
        using var directory = new TempDirectory();
        string path = directory.Combine("roblox.log");

        await using var writer = new StreamWriter(new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite)) { AutoFlush = true };
        await writer.WriteAsync("one\ntw");

        var lines = new List<string>();
        using var cancellation = new CancellationTokenSource();
        var following = LogTailer.FollowAsync(path, batch =>
        {
            lock (lines)
                lines.AddRange(batch);
        }, TimeSpan.FromMilliseconds(50), cancellation.Token);

        await writer.WriteAsync("o\nthree\n");
        await WaitUntilAsync(() => { lock (lines) return lines.Count == 3; });

        cancellation.Cancel();
        await following; // ends quietly

        Assert.Equal(["one", "two", "three"], lines);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (int i = 0; i < 200 && !condition(); i++)
            await Task.Delay(25);
    }
}

public class RobloxWebApiTests
{
    private static RobloxWebApi Api(FakeHttpHandler http) =>
        new(new HttpClient(http)) { ThumbnailRetryDelay = (_, _) => Task.CompletedTask };

    [Fact]
    public async Task Game_details_come_with_their_icon_and_are_cached()
    {
        var http = new FakeHttpHandler();
        http.MapText("https://games.roblox.com/v1/games?universeIds=11",
            """{"data":[{"id":11,"rootPlaceId":1,"name":"Obby","creator":{"id":5,"name":"Builderman","type":"User","hasVerifiedBadge":true}}]}""");
        http.MapText("https://thumbnails.roblox.com/v1/games/icons?universeIds=11&returnPolicy=PlaceHolder&size=512x512&format=Png&isCircular=false",
            """{"data":[{"targetId":11,"state":"Completed","imageUrl":"https://tr.rbxcdn.com/obby.png"}]}""");
        var api = Api(http);

        var universe = await api.GetUniverseAsync(11);
        await api.GetUniverseAsync(11);

        Assert.Equal(new UniverseInfo(11, 1, "Obby", "Builderman", true, "https://tr.rbxcdn.com/obby.png"), universe);
        Assert.Equal(2, http.Requests.Count);
    }

    [Fact]
    public async Task Thumbnails_still_rendering_are_asked_for_again()
    {
        var http = new FakeHttpHandler();
        int calls = 0;
        http.Map("https://thumbnails.roblox.com/v1/assets?assetIds=77&returnPolicy=PlaceHolder&size=512x512&format=Png&isCircular=false", () =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(++calls < 3
                    ? """{"data":[{"targetId":77,"state":"Pending","imageUrl":null}]}"""
                    : """{"data":[{"targetId":77,"state":"Completed","imageUrl":"https://tr.rbxcdn.com/77.png"}]}"""),
            });

        Assert.Equal("https://tr.rbxcdn.com/77.png", await Api(http).GetAssetThumbnailAsync(77));
        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task Users_come_with_their_headshot()
    {
        var http = new FakeHttpHandler();
        http.MapText("https://users.roblox.com/v1/users/42", """{"id":42,"name":"builder_42","displayName":"Builder"}""");
        http.MapText("https://thumbnails.roblox.com/v1/users/avatar-headshot?userIds=42&size=180x180&format=Png&isCircular=false",
            """{"data":[{"targetId":42,"state":"Completed","imageUrl":"https://tr.rbxcdn.com/42.png"}]}""");

        Assert.Equal(new UserInfo(42, "builder_42", "Builder", "https://tr.rbxcdn.com/42.png"), await Api(http).GetUserAsync(42));
    }

    [Fact]
    public async Task A_place_leads_to_its_game()
    {
        var http = new FakeHttpHandler();
        http.MapText("https://apis.roblox.com/universes/v1/places/1818/universe", """{"universeId":13058}""");

        Assert.Equal(13058, await Api(http).GetUniverseIdAsync(1818));
    }

    [Fact]
    public async Task Failures_surface_as_HTTP_errors()
    {
        var http = new FakeHttpHandler();

        await Assert.ThrowsAsync<HttpRequestException>(() => Api(http).GetUniverseAsync(11));
    }
}

public class ServerLocatorTests
{
    [Theory]
    [InlineData("Frankfurt am Main", "Hesse", "DE", "Frankfurt am Main, Hesse, DE")]
    [InlineData("Singapore", "Singapore", "SG", "Singapore, SG")]
    [InlineData("", "Hesse", "DE", null)]
    [InlineData(null, null, null, null)]
    public void Locations_are_written_like_in_Bloxstrap(string? city, string? region, string? country, string? expected) =>
        Assert.Equal(expected, ServerLocator.Format(city, region, country));

    [Fact]
    public async Task Each_address_is_looked_up_once_and_failures_give_null()
    {
        var http = new FakeHttpHandler();
        http.MapText("https://ipinfo.io/128.116.31.33/json", """{"ip":"128.116.31.33","city":"Frankfurt am Main","region":"Hesse","country":"DE"}""");
        var locator = new ServerLocator(new HttpClient(http));

        Assert.Equal("Frankfurt am Main, Hesse, DE", await locator.LocateAsync("128.116.31.33"));
        Assert.Equal("Frankfurt am Main, Hesse, DE", await locator.LocateAsync("128.116.31.33"));
        Assert.Null(await locator.LocateAsync("128.116.5.33"));
        Assert.Equal(1, http.CountRequests("https://ipinfo.io/128.116.31.33/json"));
    }
}
