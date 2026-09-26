namespace Vizstrap.Core.Platform;

/// <summary>
/// A named mutex usable from async code. Mutexes must be released by the thread that acquired them,
/// so a dedicated thread owns it for the lifetime of the lock.
/// </summary>
public sealed class InterProcessLock : IDisposable
{
    private readonly ManualResetEventSlim _release = new();
    private int _disposed;

    private InterProcessLock()
    {
    }

    /// <param name="onWaiting">Called (on the lock thread) if another process holds the lock and we have to wait.</param>
    public static Task<InterProcessLock> AcquireAsync(string name, Action? onWaiting, CancellationToken cancellationToken)
    {
        var instance = new InterProcessLock();
        var acquired = new TaskCompletionSource<InterProcessLock>(TaskCreationOptions.RunContinuationsAsynchronously);

        var thread = new Thread(() =>
        {
            bool owned = false;

            try
            {
                using var mutex = new Mutex(false, name);

                owned = TryWait(mutex, TimeSpan.Zero);

                if (!owned)
                    onWaiting?.Invoke();

                while (!owned)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        acquired.TrySetCanceled(cancellationToken);
                        return;
                    }

                    owned = TryWait(mutex, TimeSpan.FromMilliseconds(250));
                }

                acquired.TrySetResult(instance);
                instance._release.Wait();

                mutex.ReleaseMutex();
                owned = false;
            }
            catch (Exception ex)
            {
                acquired.TrySetException(ex);
            }
        })
        {
            IsBackground = true,
            Name = $"InterProcessLock {name}",
        };

        thread.Start();
        return acquired.Task;
    }

    private static bool TryWait(Mutex mutex, TimeSpan timeout)
    {
        try
        {
            return mutex.WaitOne(timeout);
        }
        catch (AbandonedMutexException)
        {
            // the previous owner crashed; the mutex is ours now
            return true;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
            _release.Set();
    }
}
