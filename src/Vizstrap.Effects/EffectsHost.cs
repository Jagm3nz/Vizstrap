using System.Diagnostics;
using Vizstrap.Core.Effects;
using Vizstrap.Core.Launch;
using Vizstrap.Core.Logging;
using Vizstrap.Core.Packages;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace Vizstrap.Effects;

/// <param name="ExcludeFromCapture">Keep the overlay out of screen captures (it would capture itself otherwise).</param>
/// <param name="RequireForeground">Only draw while the game's window is in front (off only in tests).</param>
/// <param name="LookName">The name shown when F7 picks a look (in the player's language).</param>
/// <param name="SaveSettings">Keeps a look picked with F7 for next time.</param>
/// <param name="ReadChangedSettings">Asked every second: the effects' settings when they changed since the last call, else null.</param>
/// <param name="PreviewFolder">Where a frame for the settings' preview is kept every so often; null keeps none.</param>
/// <param name="CustomEffects">Picture effects from the switched-on mod packages; the settings say which are on.</param>
public sealed record EffectsOptions(
    bool ExcludeFromCapture = true,
    bool RequireForeground = true,
    Func<EffectPreset, string>? LookName = null,
    Action<EffectSettings>? SaveSettings = null,
    Func<EffectSettings?>? ReadChangedSettings = null,
    string? PreviewFolder = null,
    IReadOnlyList<CustomEffect>? CustomEffects = null);

/// <summary>
/// Runs the picture effects over one Roblox player until it closes or the host is disposed, on a thread
/// of its own. While Roblox is in front, the screen area of its window is captured, lit and shown in
/// the overlay on top; F8 switches the effects off and on, F7 picks the next look. When Roblox isn't
/// in front, the overlay hides and nothing is captured.
/// </summary>
public sealed class EffectsHost : IDisposable
{
    private const string LogSource = nameof(EffectsHost);

    private static readonly TimeSpan SettingsCheck = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan PreviewEvery = TimeSpan.FromSeconds(20);

    private readonly int _processId;
    private readonly EffectSettings _initialSettings;
    private readonly string? _modelPath;
    private readonly EffectsOptions _options;
    private readonly Thread _thread;
    private volatile bool _stopping;

    private EffectsHost(int processId, EffectSettings settings, string? modelPath, EffectsOptions options)
    {
        _processId = processId;
        _initialSettings = settings.Clone();
        _modelPath = modelPath;
        _options = options;
        _thread = new Thread(Run) { IsBackground = true, Name = "Vizstrap effects" };
        _thread.Start();
    }

    /// <summary>Frames drawn so far (for the log and tests).</summary>
    public long FramesDrawn { get; private set; }

    /// <summary>Depth maps used so far.</summary>
    public long DepthMaps { get; private set; }

    /// <summary>Preview frames kept so far.</summary>
    public long PreviewsSaved { get; private set; }

    /// <summary>True once the loop ended on its own (Roblox closed, or an error).</summary>
    public bool HasEnded { get; private set; }

    /// <param name="modelPath">The depth AI; null runs the effects that don't need depth.</param>
    public static EffectsHost Start(int robloxProcessId, EffectSettings settings, string? modelPath, EffectsOptions? options = null)
    {
        Log.Info(LogSource, $"Effects for Roblox (PID {robloxProcessId}), look {settings.Preset}, depth {(modelPath is null ? "off" : "on")}");
        return new EffectsHost(robloxProcessId, settings, modelPath, options ?? new EffectsOptions());
    }

    public void Dispose()
    {
        _stopping = true;
        _thread.Join(TimeSpan.FromSeconds(3));
    }

    private void Run()
    {
        // the window rectangles must be in physical pixels, whatever the display scaling
        Native.SetThreadDpiAwarenessContext(Native.DpiAwarenessPerMonitorV2);

        try
        {
            using var session = new Session(this);
            session.Loop();
        }
        catch (Exception ex)
        {
            // the game goes on without effects
            Log.Error(LogSource, ex);
        }
        finally
        {
            HasEnded = true;
            Log.Info(LogSource, $"Effects stopped after {FramesDrawn} frames, {DepthMaps} depth maps, {PreviewsSaved} previews");
        }
    }

    /// <summary>Everything the loop owns, created and disposed on the effects thread.</summary>
    private sealed class Session(EffectsHost host) : IDisposable
    {
        private readonly OverlayWindow _window = new(host._options.ExcludeFromCapture);
        private readonly Stopwatch _clock = Stopwatch.StartNew();

        private EffectSettings _settings = host._initialSettings;
        private EffectParameters _parameters = EffectParameters.From(host._initialSettings);
        private IReadOnlyList<CustomPass> _customPasses = CustomPasses(host._initialSettings);
        private ID3D11Device? _device;
        private EffectRenderer? _renderer;
        private DesktopCapture? _capture;
        private IDXGISwapChain1? _swapChain;
        private ID3D11RenderTargetView? _backBuffer;
        private OverlayLabel? _label;
        private DepthEstimator? _depth;
        private bool _depthFailed;
        private ID3D11Texture2D? _staging;
        private bool _stagingPending;
        private ID3D11Texture2D? _previewStaging;
        private bool _previewPending;
        private TimeSpan _nextPreview = TimeSpan.FromSeconds(5);
        private TimeSpan _nextSettingsCheck = SettingsCheck;
        private float[]? _latestDepth;
        private float _focus = 8;
        private bool _hasPicture;
        private bool _announced;
        private bool _enabled = true;
        private IntPtr _roblox;

        public void Loop()
        {
            while (!host._stopping)
            {
                var keys = _window.Pump();

                if (keys.HasFlag(OverlayKeys.Toggle))
                {
                    _enabled = !_enabled;
                    Log.Info(LogSource, $"Effects {(_enabled ? "on" : "off")} (F8)");
                }

                if (keys.HasFlag(OverlayKeys.NextLook))
                    NextLook();

                CheckSettings();

                if (_roblox == IntPtr.Zero || !Native.IsWindow(_roblox))
                    _roblox = Native.FindGameWindow(host._processId);

                if (_roblox == IntPtr.Zero)
                {
                    if (!RobloxInstances.IsRunning(host._processId))
                        return;

                    _window.Hide();
                    Thread.Sleep(250);
                    continue;
                }

                bool inFront = Native.GetForegroundWindow() == _roblox || !host._options.RequireForeground;
                _window.SetKeys(inFront);

                if (!_enabled || !_settings.Enabled || !inFront || Native.IsIconic(_roblox) || !Native.TryGetClientArea(_roblox, out var area))
                {
                    _window.Hide();
                    Thread.Sleep(50);
                    continue;
                }

                Frame(area);
            }
        }

        private void Frame(Native.Rect area)
        {
            int width = area.Right - area.Left, height = area.Bottom - area.Top;
            EnsureDevice(area);
            EnsureSize(width, height);

            // the look's name for a moment when the effects first show, so it's clear they're on
            if (!_announced)
            {
                _announced = true;
                _label!.Show($"Vizstrap · {host._options.LookName?.Invoke(_settings.Preset) ?? _settings.Preset.ToString()}");
            }

            var captured = _capture!.TryCapture(area, _renderer!.Scene, 16);

            if (captured == CaptureResult.Lost)
            {
                Thread.Sleep(50);
                return;
            }

            bool newPicture = captured == CaptureResult.NewFrame;
            _hasPicture |= newPicture;

            if (!_hasPicture)
                return;

            // a still picture (a paused menu) still gets drawn again when its depth comes in or a note shows
            bool newDepth = ReceiveDepth();

            if (!newPicture && !newDepth && !_label!.IsShowing)
            {
                FeedDepth(queueNew: false);
                KeepPreview(queueNew: false);
                return;
            }

            // Roblox's top bar (about 58 px at 100 % scaling) keeps its look
            _renderer.UiBand = 60f * Native.GetDpiForWindow(_roblox) / 96 / height;
            _renderer.FocusDepth = _focus;
            _renderer.Render(_backBuffer!, _parameters with { UseDepth = _parameters.UseDepth && _depth is not null }, (float)_clock.Elapsed.TotalSeconds, _customPasses);
            _label!.Draw(_swapChain!, width);
            _window.Show(area);
            _swapChain!.Present(0, PresentFlags.None);
            host.FramesDrawn++;

            FeedDepth(queueNew: newPicture);
            KeepPreview(queueNew: newPicture);
        }

        // ---- looks and settings

        /// <summary>F7: the next preset's look, kept for next time, with its name on screen.</summary>
        private void NextLook()
        {
            var next = EffectPresets.Next(_settings.Preset);

            var look = EffectPresets.Look(next)!;
            look.Enabled = _settings.Enabled;
            look.Custom = [.. _settings.Custom];
            Apply(look);

            string name = host._options.LookName?.Invoke(next) ?? next.ToString();
            _label?.Show($"Vizstrap · {name}");
            Log.Info(LogSource, $"Look {next} (F7)");

            // a fresh preview frame for the new look, soon
            _nextPreview = _clock.Elapsed + TimeSpan.FromSeconds(2);

            if (host._options.SaveSettings is { } save)
            {
                var saved = look.Clone();
                _ = Task.Run(() =>
                {
                    try
                    {
                        save(saved);
                    }
                    catch (Exception ex)
                    {
                        Log.Warn(LogSource, $"The look picked with F7 couldn't be kept: {ex.Message}");
                    }
                });
            }
        }

        /// <summary>Picks up changes made in the settings while playing.</summary>
        private void CheckSettings()
        {
            if (host._options.ReadChangedSettings is not { } read || _clock.Elapsed < _nextSettingsCheck)
                return;

            _nextSettingsCheck = _clock.Elapsed + SettingsCheck;

            try
            {
                if (read() is { } changed && !changed.SameAs(_settings))
                {
                    Log.Info(LogSource, $"Settings changed while playing: {(changed.Enabled ? "on" : "off")}, look {changed.Preset}");
                    Apply(changed);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // the file is being written; next second
            }
        }

        private void Apply(EffectSettings settings)
        {
            bool depthChanged = settings.UseDepth != _settings.UseDepth;
            _settings = settings.Clone();
            _parameters = EffectParameters.From(_settings);
            _customPasses = CustomPasses(_settings);

            if (depthChanged && _renderer is { Width: > 0 })
                EnsureDepth(_renderer.Width, _renderer.Height);
        }

        // ---- depth

        /// <summary>The newest depth map, if one came in, and the focus eased towards its middle.</summary>
        private bool ReceiveDepth()
        {
            if (_depth is null || !_depth.TryTake(out var closeness))
                return false;

            _renderer!.UpdateDepth(closeness, _depth.Width, _depth.Height);
            _latestDepth = closeness;
            float focus = DepthMath.ViewDepth(DepthMath.FocusCloseness(closeness, _depth.Width, _depth.Height));
            _focus += (focus - _focus) * 0.15f;
            host.DepthMaps++;
            return true;
        }

        /// <summary>
        /// Hands the depth AI the picture queued a moment ago (read back without waiting for the card), then
        /// queues this picture for next time (always the first time, then only when the picture changed).
        /// </summary>
        private void FeedDepth(bool queueNew)
        {
            if (_depth is null || _depth.IsBusy)
                return;

            var context = _device!.ImmediateContext;

            if (_stagingPending)
            {
                var result = context.Map(_staging!, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.DoNotWait, out var mapped);

                if (result == Vortice.DXGI.ResultCode.WasStillDrawing)
                    return;

                result.CheckError();

                try
                {
                    _depth.TrySubmit(mapped.DataPointer, (int)mapped.RowPitch);
                }
                finally
                {
                    context.Unmap(_staging!, 0);
                    _stagingPending = false;
                }
            }

            if (!queueNew && host.DepthMaps > 0)
                return;

            _renderer!.DrawModelInput(_depth.Width, _depth.Height);
            context.CopyResource(_staging!, _renderer.ModelInput!);
            _stagingPending = true;
        }

        // ---- preview frame

        /// <summary>
        /// Every so often, a half-size frame and its depth are kept for the settings' preview: queued on the
        /// card first and read back a frame later, so the game never waits.
        /// </summary>
        private void KeepPreview(bool queueNew)
        {
            if (host._options.PreviewFolder is not { } folder || _latestDepth is null || _depth is null)
                return;

            var context = _device!.ImmediateContext;

            if (_previewPending)
            {
                var result = context.Map(_previewStaging!, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.DoNotWait, out var mapped);

                if (result == Vortice.DXGI.ResultCode.WasStillDrawing)
                    return;

                result.CheckError();
                int width = (int)_previewStaging!.Description.Width, height = (int)_previewStaging.Description.Height;
                var pixels = new byte[width * height * 4];

                try
                {
                    unsafe
                    {
                        for (int y = 0; y < height; y++)
                            new ReadOnlySpan<byte>((byte*)mapped.DataPointer + y * mapped.RowPitch, width * 4).CopyTo(pixels.AsSpan(y * width * 4));
                    }
                }
                finally
                {
                    context.Unmap(_previewStaging!, 0);
                    _previewPending = false;
                }

                var frame = new PreviewFrame(width, height, pixels, _depth.Width, _depth.Height, (float[])_latestDepth.Clone());
                _ = Task.Run(() =>
                {
                    try
                    {
                        frame.Save(folder);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        Log.Warn(LogSource, $"The preview frame couldn't be kept: {ex.Message}");
                    }
                });
                host.PreviewsSaved++;
                return;
            }

            if (!queueNew || _clock.Elapsed < _nextPreview)
                return;

            _nextPreview = _clock.Elapsed + PreviewEvery;
            _renderer!.DrawPreviewPicture();
            var picture = _renderer.PreviewPicture!;

            if (_previewStaging is null || _previewStaging.Description.Width != picture.Description.Width || _previewStaging.Description.Height != picture.Description.Height)
            {
                _previewStaging?.Dispose();
                _previewStaging = _device.CreateTexture2D(new Texture2DDescription(Format.B8G8R8A8_UNorm, picture.Description.Width, picture.Description.Height, 1, 1,
                    BindFlags.None, ResourceUsage.Staging, CpuAccessFlags.Read));
            }

            context.CopyResource(_previewStaging, picture);
            _previewPending = true;
        }

        // ---- set-up

        private void EnsureDevice(Native.Rect area)
        {
            if (_device is not null)
                return;

            using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
            using var adapter = AdapterShowing(factory, area);

            D3D11.D3D11CreateDevice(adapter, adapter is null ? DriverType.Hardware : DriverType.Unknown, DeviceCreationFlags.BgraSupport,
                [FeatureLevel.Level_11_0], out _device).CheckError();

            _renderer = new EffectRenderer(_device!);
            _capture = new DesktopCapture(_device!);
            _label = new OverlayLabel(_device!);

            foreach (var (id, error) in CustomEffectSources.Load(_renderer, host._options.CustomEffects))
                Log.Warn(LogSource, $"The package effect {id} can't be used: {error}");

            using var dxgiDevice = _device!.QueryInterface<IDXGIDevice>();
            using var deviceAdapter = dxgiDevice.GetAdapter();
            using var deviceFactory = deviceAdapter.GetParent<IDXGIFactory2>();
            _swapChain = deviceFactory.CreateSwapChainForHwnd(_device, _window.Handle, new SwapChainDescription1(16, 16, Format.B8G8R8A8_UNorm)
            {
                BufferCount = 2,
                SwapEffect = SwapEffect.FlipDiscard,
                BufferUsage = Usage.RenderTargetOutput,
                AlphaMode = AlphaMode.Ignore,
            });

            // Alt+Enter would take the overlay fullscreen
            deviceFactory.MakeWindowAssociation(_window.Handle, WindowAssociationFlags.IgnoreAll);
            Log.Info(LogSource, $"Drawing on {deviceAdapter.Description.Description}");
        }

        private void EnsureSize(int width, int height)
        {
            if (_renderer!.Width == width && _renderer.Height == height)
                return;

            _renderer.Resize(width, height);
            _backBuffer?.Dispose();
            _swapChain!.ResizeBuffers(2, (uint)width, (uint)height, Format.B8G8R8A8_UNorm, SwapChainFlags.None).CheckError();
            using (var buffer = _swapChain.GetBuffer<ID3D11Texture2D>(0))
                _backBuffer = _device!.CreateRenderTargetView(buffer);

            EnsureDepth(width, height);
            Log.Info(LogSource, $"Roblox's window: {width}x{height}");
        }

        /// <summary>
        /// The depth AI at the size for this window's shape, or none when the settings leave it off; without
        /// it the effects that need depth rest.
        /// </summary>
        private void EnsureDepth(int width, int height)
        {
            if (host._modelPath is null || _depthFailed)
                return;

            if (!_settings.UseDepth)
            {
                _depth?.Dispose();
                _depth = null;
                _latestDepth = null;
                _renderer!.ClearDepth();
                return;
            }

            var (modelWidth, modelHeight) = DepthEstimator.SizeFor(width, height);

            if (_depth is not null && _depth.Width == modelWidth && _depth.Height == modelHeight)
                return;

            _depth?.Dispose();
            _depth = null;
            _staging?.Dispose();
            _stagingPending = false;
            _latestDepth = null;
            _renderer!.ClearDepth();

            try
            {
                _depth = new DepthEstimator(host._modelPath, modelWidth, modelHeight);
                _staging = _device!.CreateTexture2D(new Texture2DDescription(Format.B8G8R8A8_UNorm, (uint)modelWidth, (uint)modelHeight, 1, 1,
                    BindFlags.None, ResourceUsage.Staging, CpuAccessFlags.Read));
            }
            catch (Exception ex)
            {
                _depthFailed = true;
                Log.Warn(LogSource, $"The depth AI can't run here, effects without depth: {ex.Message}");
            }
        }

        private static IReadOnlyList<CustomPass> CustomPasses(EffectSettings settings) =>
            [.. settings.Custom.Where(state => state.Enabled).Select(state => new CustomPass(state.Id, state.Values))];

        private static IDXGIAdapter1? AdapterShowing(IDXGIFactory1 factory, Native.Rect area)
        {
            int x = (area.Left + area.Right) / 2, y = (area.Top + area.Bottom) / 2;

            for (uint index = 0; factory.EnumAdapters1(index, out var adapter).Success; index++)
            {
                for (uint outputIndex = 0; adapter.EnumOutputs(outputIndex, out var output).Success; outputIndex++)
                {
                    using (output)
                    {
                        var bounds = output.Description.DesktopCoordinates;

                        if (x >= bounds.Left && x < bounds.Right && y >= bounds.Top && y < bounds.Bottom)
                            return adapter;
                    }
                }

                adapter.Dispose();
            }

            return null;
        }

        public void Dispose()
        {
            _window.Hide();
            _depth?.Dispose();
            _staging?.Dispose();
            _previewStaging?.Dispose();
            _label?.Dispose();
            _backBuffer?.Dispose();
            _swapChain?.Dispose();
            _capture?.Dispose();
            _renderer?.Dispose();
            _device?.Dispose();
            _window.Dispose();
        }
    }
}
