namespace Vizstrap.Core.Activity;

/// <summary>One visit to a game, teleports within it included.</summary>
/// <param name="First">The session the visit started with (its game and server type).</param>
/// <param name="Left">When the last server of the visit was left (UTC).</param>
/// <param name="RejoinTarget">The newest public server of the visit, or the first one if there's none.</param>
public sealed record GameVisit(GameSession First, DateTime Left, GameSession RejoinTarget)
{
    public DateTime Joined => First.TimeJoined;

    /// <summary>A link back to <see cref="RejoinTarget"/>, without launch data (like Bloxstrap).</summary>
    public string RejoinDeeplink => RejoinTarget.InviteDeeplink(includeLaunchData: false);
}

public static class GameHistory
{
    /// <summary>
    /// Folds teleports within the same game into the visit they belong to, like Bloxstrap's game
    /// history. <paramref name="history"/> and the result are newest first.
    /// </summary>
    public static IReadOnlyList<GameVisit> Visits(IReadOnlyList<GameSession> history)
    {
        var visits = new List<GameVisit>();

        foreach (var root in history.Where(session => session.RootSession is null))
        {
            var chain = history.Where(session => session == root || session.RootSession == root).ToList();

            var left = chain.Max(session => session.TimeLeft ?? session.TimeJoined);
            var rejoin = chain.FirstOrDefault(session => session.ServerType == ServerType.Public) ?? root;

            visits.Add(new GameVisit(root, left, rejoin));
        }

        return visits;
    }
}
