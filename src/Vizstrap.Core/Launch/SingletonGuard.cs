using Vizstrap.Core.Logging;

namespace Vizstrap.Core.Launch;

/// <summary>
/// Lets several Roblox players run at once. Roblox keeps to one player with two named objects: the mutex
/// "ROBLOX_singletonMutex" and the event "ROBLOX_singletonEvent", through which a new player tells the
/// running one to quit. Holding a mutex under the event's name before Roblox starts makes Roblox's event
/// fail to be created, so players stop closing each other (MultiRoblox's approach; Bloxstrap held only the
/// mutex, which stopped working when Roblox moved to the event). Nothing inside Roblox is touched.
/// The guard must stay open, on a thread that lives as long, for as long as the players it let start.
/// </summary>
public sealed class SingletonGuard : IDisposable
{
    public const string MutexName = "ROBLOX_singletonMutex";
    public const string EventName = "ROBLOX_singletonEvent";

    private const string LogSource = nameof(SingletonGuard);

    private readonly Mutex _mutex;
    private readonly bool _ownsMutex;
    private readonly Mutex _eventBlocker;
    private readonly bool _ownsEventBlocker;

    private SingletonGuard(Mutex mutex, bool ownsMutex, Mutex eventBlocker, bool ownsEventBlocker)
    {
        _mutex = mutex;
        _ownsMutex = ownsMutex;
        _eventBlocker = eventBlocker;
        _ownsEventBlocker = ownsEventBlocker;
    }

    /// <summary>
    /// True when a Roblox player already made its event, so the next player would close it. Only closing
    /// those players frees the name. A guard's own mutex under that name doesn't count.
    /// </summary>
    public static bool IsHeldByRoblox(string eventName = EventName)
    {
        try
        {
            using (EventWaitHandle.OpenExisting(eventName))
                return true;
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            // no such name, or a mutex (a guard) under it
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }

    /// <summary>The guard, or null while Roblox holds its event (see <see cref="IsHeldByRoblox"/>).</summary>
    /// <remarks>Other guards (other Vizstrap launches) may hold the same names; each keeps them alive.</remarks>
    public static SingletonGuard? TryTake(string mutexName = MutexName, string eventName = EventName)
    {
        if (IsHeldByRoblox(eventName))
            return null;

        Mutex? mutex = null;

        try
        {
            mutex = new Mutex(initiallyOwned: true, mutexName, out bool ownsMutex);
            var eventBlocker = new Mutex(initiallyOwned: true, eventName, out bool ownsEventBlocker);
            var guard = new SingletonGuard(mutex, ownsMutex, eventBlocker, ownsEventBlocker);

            // Roblox may have made its event between the check and the mutex
            if (IsHeldByRoblox(eventName))
            {
                guard.Dispose();
                return null;
            }

            Log.Info(LogSource, "Holding Roblox's singleton: several players can run");
            return guard;
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            // the event name is Roblox's event after all
            mutex?.Dispose();
            return null;
        }
    }

    public void Dispose()
    {
        Release(_mutex, _ownsMutex);
        Release(_eventBlocker, _ownsEventBlocker);
    }

    private static void Release(Mutex mutex, bool owned)
    {
        try
        {
            if (owned)
                mutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // disposed from another thread than the one that took it: closing the handle is enough
        }

        mutex.Dispose();
    }
}
