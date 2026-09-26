using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Vizstrap.Core.Effects;
using Vizstrap.Core.Logging;

namespace Vizstrap.Effects;

/// <summary>
/// Guesses depth from the picture with the depth AI (<see cref="DepthModel"/>) on the graphics card
/// through DirectML, on a thread of its own: frames are handed in when it's free, and the newest map
/// is picked up when ready. The model runs at one fixed size, which DirectML needs to be fast (about
/// 8 ms on an RTX 3060 against 95 ms with a size left open).
/// </summary>
public sealed class DepthEstimator : IDisposable
{
    /// <summary>The model's input height; the width follows the picture, both multiples of 14.</summary>
    public const int ModelHeight = 224;

    private const string LogSource = nameof(DepthEstimator);

    private static readonly Lock NativeLock = new();
    private static bool _directMLLoaded;

    private static readonly float[] Mean = [0.485f, 0.456f, 0.406f];
    private static readonly float[] Deviation = [0.229f, 0.224f, 0.225f];

    private readonly InferenceSession _session;
    private readonly string _inputName;
    private readonly DepthNormalizer _normalizer = new();
    private readonly Thread _worker;
    private readonly AutoResetEvent _frameReady = new(false);
    private readonly Lock _lock = new();
    private readonly float[] _input;
    private volatile bool _stopping;
    private bool _busy;
    private float[]? _latest;

    public DepthEstimator(string modelPath, int width, int height)
    {
        Width = width;
        Height = height;
        LoadDirectML();

        using var options = new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            EnableMemoryPattern = false,
            ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
            LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR,
        };
        options.AddFreeDimensionOverrideByName("batch_size", 1);
        options.AddFreeDimensionOverrideByName("height", height);
        options.AddFreeDimensionOverrideByName("width", width);
        options.AppendExecutionProvider_DML(0);

        _session = new InferenceSession(modelPath, options);
        _inputName = _session.InputMetadata.Keys.First();
        _input = new float[3 * width * height];

        _worker = new Thread(Work) { IsBackground = true, Name = "Vizstrap depth", Priority = ThreadPriority.BelowNormal };
        _worker.Start();
        Log.Info(LogSource, $"Depth AI ready at {width}x{height}");
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>How long the last map took, for the log.</summary>
    public double LastMilliseconds { get; private set; }

    /// <summary>The model size for a picture: 224 high, as wide as the picture's shape (multiple of 14, 224 … 518).</summary>
    public static (int Width, int Height) SizeFor(int pictureWidth, int pictureHeight)
    {
        double aspect = pictureHeight > 0 ? (double)pictureWidth / pictureHeight : 16.0 / 9;
        int width = (int)Math.Round(ModelHeight * aspect / 14) * 14;
        return (Math.Clamp(width, 224, 518), ModelHeight);
    }

    /// <summary>True while a frame is being worked on (a new one would be dropped).</summary>
    public bool IsBusy
    {
        get
        {
            lock (_lock)
                return _busy;
        }
    }

    /// <summary>Hands in a frame (B8G8R8A8 rows of <see cref="Width"/> pixels) unless one is being worked on.</summary>
    public unsafe bool TrySubmit(IntPtr pixels, int rowPitch)
    {
        lock (_lock)
        {
            if (_busy)
                return false;
            _busy = true;
        }

        int plane = Width * Height;

        for (int y = 0; y < Height; y++)
        {
            byte* row = (byte*)pixels + y * rowPitch;

            for (int x = 0; x < Width; x++)
            {
                int index = y * Width + x;
                _input[index] = (row[x * 4 + 2] / 255f - Mean[0]) / Deviation[0];
                _input[plane + index] = (row[x * 4 + 1] / 255f - Mean[1]) / Deviation[1];
                _input[2 * plane + index] = (row[x * 4] / 255f - Mean[2]) / Deviation[2];
            }
        }

        _frameReady.Set();
        return true;
    }

    /// <summary>The newest map (closeness 0 far … 1 near, <see cref="Width"/> × <see cref="Height"/>), once.</summary>
    public bool TryTake(out float[] closeness)
    {
        lock (_lock)
        {
            closeness = _latest!;
            _latest = null;
            return closeness is not null;
        }
    }

    public void Dispose()
    {
        _stopping = true;
        _frameReady.Set();
        _worker.Join(TimeSpan.FromSeconds(2));
        _session.Dispose();
        _frameReady.Dispose();
    }

    /// <summary>
    /// Windows 10 has an old DirectML.dll in System32 that ONNX Runtime would pick up and fail with.
    /// Loading the one shipped with Vizstrap by its full path first makes it the one used.
    /// </summary>
    private static void LoadDirectML()
    {
        lock (NativeLock)
        {
            if (_directMLLoaded)
                return;

            var runtime = NativeLibrary.Load("onnxruntime", typeof(InferenceSession).Assembly, null);
            var name = new StringBuilder(1024);
            GetModuleFileNameW(runtime, name, name.Capacity);

            foreach (string? folder in (string?[])[Path.GetDirectoryName(name.ToString()), AppContext.BaseDirectory])
            {
                string candidate = Path.Combine(folder ?? "", "DirectML.dll");

                if (File.Exists(candidate))
                {
                    NativeLibrary.Load(candidate);
                    _directMLLoaded = true;
                    Log.Info(LogSource, $"DirectML from {candidate}");
                    return;
                }
            }

            Log.Warn(LogSource, "Vizstrap's DirectML.dll wasn't found; Windows' own will be tried");
            _directMLLoaded = true;
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern uint GetModuleFileNameW(IntPtr module, StringBuilder name, int size);

    private void Work()
    {
        var input = new DenseTensor<float>(_input, [1, 3, Height, Width]);

        while (true)
        {
            _frameReady.WaitOne();

            if (_stopping)
                return;

            try
            {
                var clock = Stopwatch.StartNew();
                using var results = _session.Run([NamedOnnxValue.CreateFromTensor(_inputName, input)]);
                float[] depth = results.First().AsTensor<float>().ToArray();
                _normalizer.Normalize(depth);
                LastMilliseconds = clock.Elapsed.TotalMilliseconds;

                lock (_lock)
                    _latest = depth;
            }
            catch (OnnxRuntimeException ex)
            {
                Log.Warn(LogSource, $"Depth AI failed on a frame: {ex.Message}");
            }
            finally
            {
                lock (_lock)
                    _busy = false;
            }
        }
    }
}
