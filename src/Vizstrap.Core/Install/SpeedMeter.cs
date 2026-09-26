using System.Diagnostics;

namespace Vizstrap.Core.Install;

/// <summary>Download speed averaged over a sliding time window.</summary>
public sealed class SpeedMeter
{
    private readonly TimeSpan _window;
    private readonly Func<TimeSpan> _clock;
    private readonly Queue<(TimeSpan Time, long Total)> _samples = new();
    private long _total;

    public SpeedMeter(TimeSpan? window = null, Func<TimeSpan>? clock = null)
    {
        _window = window ?? TimeSpan.FromSeconds(3);

        if (clock is null)
        {
            var stopwatch = Stopwatch.StartNew();
            clock = () => stopwatch.Elapsed;
        }

        _clock = clock;
        _samples.Enqueue((_clock(), 0));
    }

    public void Add(long bytes)
    {
        _total += bytes;

        var now = _clock();
        _samples.Enqueue((now, _total));

        // keep one sample older than the window as the baseline
        while (_samples.Count > 2 && now - _samples.ElementAt(1).Time > _window)
            _samples.Dequeue();
    }

    public double BytesPerSecond
    {
        get
        {
            var (oldestTime, oldestTotal) = _samples.Peek();
            double seconds = (_clock() - oldestTime).TotalSeconds;

            return seconds <= 0 ? 0 : Math.Max(0, (_total - oldestTotal) / seconds);
        }
    }
}
