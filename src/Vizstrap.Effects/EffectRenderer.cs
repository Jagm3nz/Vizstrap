using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Vizstrap.Core.Effects;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace Vizstrap.Effects;

/// <summary>The effect strengths as the shaders take them, from <see cref="EffectSettings"/>.</summary>
public readonly record struct EffectParameters(
    float AmbientOcclusion, float IndirectLight, float Haze, float DepthOfField, float Bloom, float Contrast,
    float Saturation, float Warmth, float Vignette, float Sharpen, bool UseDepth,
    float Reflections = 0, float SunRays = 0)
{
    public static EffectParameters From(EffectSettings settings) => new(
        settings.AmbientOcclusion / 100f, settings.IndirectLight / 100f, settings.Haze / 100f, settings.DepthOfField / 100f,
        settings.Bloom / 100f, settings.Contrast / 100f, settings.Saturation / 100f, settings.Warmth / 100f,
        settings.Vignette / 100f, settings.Sharpen / 100f, settings.UseDepth,
        settings.Reflections / 100f, settings.SunRays / 100f);

    /// <summary>Everything off: the picture comes out as it went in.</summary>
    public static EffectParameters None { get; } = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, false);
}

/// <summary>A package's effect to draw after Vizstrap's own, with its sliders' values (Param0 …).</summary>
public sealed record CustomPass(string Id, float[] Values);

/// <summary>
/// Draws the effects on one D3D11 device: the captured picture goes in (<see cref="Scene"/>), the lit
/// picture comes out into a render target. Depth, when there is any, is uploaded with
/// <see cref="UpdateDepth"/>. The noisy effects are averaged over frames, so call <see cref="Render"/>
/// every frame and <see cref="ClearHistory"/> when the picture jumps (a new scene, a preview).
/// Everything runs on the thread that owns the device.
/// </summary>
public sealed class EffectRenderer : IDisposable
{
    /// <summary>Roblox's default camera: 70° vertical field of view.</summary>
    private const float TanHalfFov = 0.7002075f;

    private const int ShaderInputs = 9;

    private readonly ID3D11Device _device;
    private readonly ID3D11DeviceContext _context;
    private readonly ID3D11VertexShader _vertexShader;
    private readonly Dictionary<string, ID3D11PixelShader> _pixelShaders = [];
    private readonly ID3D11Buffer _constants;
    private readonly ID3D11SamplerState _linear;
    private readonly ID3D11SamplerState _nearest;
    private readonly List<IDisposable> _sized = [];
    private readonly Dictionary<string, ID3D11PixelShader> _custom = [];
    private readonly ID3D11Buffer _customConstants;
    private Target? _customA, _customB;

    private Target? _scene;
    private Target[] _depth = [];            // view depth + confidence, this frame and the last
    private Target _lightingRaw = null!, _lightingBlurred = null!, _lightingTemp = null!;
    private Target[] _lightingHistory = [];
    private Target _reflectionRaw = null!, _reflectionTemp = null!, _reflectionBlurred = null!;
    private Target[] _reflectionHistory = [];
    private Target _sunLight = null!, _sunCentre = null!, _sunRays = null!;
    private Target[] _picture = [];          // 1/2, 1/4, 1/8, 1/16
    private Target[] _glowDown = [];         // 1/2 … 1/32
    private Target[] _glowUp = [];           // 1/2 … 1/16
    private Target? _modelInput;
    private Target? _previewPicture;
    private ID3D11Texture2D? _depthLow;
    private ID3D11ShaderResourceView? _depthLowView;
    private ID3D11Texture2D? _depthPicture;
    private ID3D11ShaderResourceView? _depthPictureView;
    private int _history;
    private bool _historyValid;
    private uint _frame;

    public EffectRenderer(ID3D11Device device)
    {
        _device = device;
        _context = device.ImmediateContext;

        string source = ReadShaderSource();
        _vertexShader = device.CreateVertexShader(Compile(source, "VS", "vs_5_0").Span);

        foreach (string pass in (string[])["PS_Depth", "PS_Lighting", "PS_Reflections", "PS_Accumulate", "PS_BlurLighting",
                     "PS_SunLight", "PS_SunCentre", "PS_SunRays", "PS_DownFirst", "PS_Down", "PS_UpAdd", "PS_BrightPass",
                     "PS_ToPicture", "PS_Composite"])
            _pixelShaders[pass] = device.CreatePixelShader(Compile(source, pass, "ps_5_0").Span);

        _constants = device.CreateBuffer(new BufferDescription((uint)Unsafe.SizeOf<Constants>(), BindFlags.ConstantBuffer, ResourceUsage.Dynamic, CpuAccessFlags.Write));
        _customConstants = device.CreateBuffer(new BufferDescription(CustomEffectPrelude.ParameterCount * sizeof(float), BindFlags.ConstantBuffer, ResourceUsage.Dynamic, CpuAccessFlags.Write));
        _linear = device.CreateSamplerState(new SamplerDescription(Filter.MinMagMipLinear, TextureAddressMode.Clamp));
        _nearest = device.CreateSamplerState(new SamplerDescription(Filter.MinMagMipPoint, TextureAddressMode.Clamp));
    }

    public int Width { get; private set; }

    public int Height { get; private set; }

    /// <summary>The captured picture goes here (B8G8R8A8, the size given to <see cref="Resize"/>).</summary>
    public ID3D11Texture2D Scene => _scene?.Texture ?? throw new InvalidOperationException("Resize first.");

    /// <summary>The picture for the depth AI, once <see cref="DrawModelInput"/> ran (B8G8R8A8).</summary>
    public ID3D11Texture2D? ModelInput => _modelInput?.Texture;

    /// <summary>The half-size picture for the settings' preview, once <see cref="DrawPreviewPicture"/> ran (B8G8R8A8).</summary>
    public ID3D11Texture2D? PreviewPicture => _previewPicture?.Texture;

    public bool HasDepth => _depthLowView is not null;

    /// <summary>View depth (1 … 60) in focus; set from the depth map's middle.</summary>
    public float FocusDepth { get; set; } = 8;

    /// <summary>Roblox's top bar as a share of the height; depth effects stay out of it.</summary>
    public float UiBand { get; set; }

    public void Resize(int width, int height)
    {
        if (width == Width && height == Height)
            return;

        foreach (var resource in _sized)
            resource.Dispose();
        _sized.Clear();

        Width = width;
        Height = height;

        int halfWidth = Half(width, 1), halfHeight = Half(height, 1);
        int quarterWidth = Half(width, 2), quarterHeight = Half(height, 2);

        _scene = Create(width, height, Format.B8G8R8A8_UNorm, renderTarget: false);
        _depth = [Create(halfWidth, halfHeight, Format.R16G16_Float), Create(halfWidth, halfHeight, Format.R16G16_Float)];
        _lightingRaw = Create(halfWidth, halfHeight, Format.R16G16B16A16_Float);
        _lightingTemp = Create(halfWidth, halfHeight, Format.R16G16B16A16_Float);
        _lightingBlurred = Create(halfWidth, halfHeight, Format.R16G16B16A16_Float);
        _lightingHistory = [Create(halfWidth, halfHeight, Format.R16G16B16A16_Float), Create(halfWidth, halfHeight, Format.R16G16B16A16_Float)];
        _reflectionRaw = Create(halfWidth, halfHeight, Format.R16G16B16A16_Float);
        _reflectionTemp = Create(halfWidth, halfHeight, Format.R16G16B16A16_Float);
        _reflectionBlurred = Create(halfWidth, halfHeight, Format.R16G16B16A16_Float);
        _reflectionHistory = [Create(halfWidth, halfHeight, Format.R16G16B16A16_Float), Create(halfWidth, halfHeight, Format.R16G16B16A16_Float)];
        _sunLight = Create(quarterWidth, quarterHeight, Format.R11G11B10_Float);
        _sunCentre = Create(quarterWidth, quarterHeight, Format.R16G16B16A16_Float, mipmapped: true);
        _sunRays = Create(quarterWidth, quarterHeight, Format.R11G11B10_Float);
        _picture = [.. Enumerable.Range(1, 4).Select(level => Create(Half(width, level), Half(height, level), Format.R11G11B10_Float))];
        _glowDown = [.. Enumerable.Range(1, 5).Select(level => Create(Half(width, level), Half(height, level), Format.R11G11B10_Float))];
        _glowUp = [.. Enumerable.Range(1, 4).Select(level => Create(Half(width, level), Half(height, level), Format.R11G11B10_Float))];

        _previewPicture?.Dispose();
        _previewPicture = null;
        _customA?.Dispose();
        _customB?.Dispose();
        _customA = _customB = null;
        ClearHistory();
    }

    /// <summary>
    /// Compiles packages' effects (HLSL with an Effect() function, see Packages.TemplateTexts) for
    /// <see cref="Render"/>; returns the compiler's message for each one that failed.
    /// </summary>
    public IReadOnlyDictionary<string, string> LoadCustomEffects(IEnumerable<(string Id, string Source)> effects)
    {
        foreach (var shader in _custom.Values)
            shader.Dispose();
        _custom.Clear();

        var errors = new Dictionary<string, string>();
        string main = ReadShaderSource();

        foreach (var (id, source) in effects)
        {
            string name = id.Contains('/') ? id[(id.LastIndexOf('/') + 1)..] + ".hlsl" : id;

            // the author's line numbers in the compiler's messages
            string code = main + CustomEffectPrelude.Header + "\n#line 1 \"" + name + "\"\n" + source + CustomEffectPrelude.Entry;
            var result = Compiler.Compile(code, "PS_CustomEffect", name, "ps_5_0", out var blob, out var errorBlob);

            try
            {
                if (result.Failure || blob is null)
                {
                    errors[id] = errorBlob is null ? result.ToString() : BlobText(errorBlob);
                    continue;
                }

                _custom[id] = _device.CreatePixelShader(blob.AsSpan());
            }
            finally
            {
                blob?.Dispose();
                errorBlob?.Dispose();
            }
        }

        return errors;
    }

    public bool HasCustomEffect(string id) => _custom.ContainsKey(id);

    /// <summary>Forgets the frames averaged so far (the next frame starts afresh).</summary>
    public void ClearHistory() => _historyValid = false;

    /// <summary>
    /// Uploads the depth AI's latest map: closeness 0 (far) … 1 (near), row by row. The picture last drawn
    /// with <see cref="DrawModelInput"/> is taken as the one the AI saw, so moved parts can be told apart.
    /// </summary>
    public void UpdateDepth(ReadOnlySpan<float> closeness, int width, int height)
    {
        if (_depthLow is null || _depthLow.Description.Width != width || _depthLow.Description.Height != height)
        {
            ClearDepth();
            _depthLow = _device.CreateTexture2D(new Texture2DDescription(Format.R32_Float, (uint)width, (uint)height, 1, 1,
                BindFlags.ShaderResource, ResourceUsage.Dynamic, CpuAccessFlags.Write));
            _depthLowView = _device.CreateShaderResourceView(_depthLow);
        }

        var mapped = _context.Map(_depthLow, 0, MapMode.WriteDiscard);

        try
        {
            for (int y = 0; y < height; y++)
            {
                var row = closeness.Slice(y * width, width);
                unsafe
                {
                    row.CopyTo(new Span<float>((byte*)mapped.DataPointer + y * mapped.RowPitch, width));
                }
            }
        }
        finally
        {
            _context.Unmap(_depthLow, 0);
        }

        if (_modelInput is not null && _modelInput.Width == width && _modelInput.Height == height)
        {
            if (_depthPicture is null)
            {
                _depthPicture = _device.CreateTexture2D(new Texture2DDescription(Format.B8G8R8A8_UNorm, (uint)width, (uint)height, 1, 1));
                _depthPictureView = _device.CreateShaderResourceView(_depthPicture);
            }

            _context.CopyResource(_depthPicture, _modelInput.Texture);
        }
    }

    /// <summary>Forgets the depth (effects that need it switch off until the next map).</summary>
    public void ClearDepth()
    {
        _depthLowView?.Dispose();
        _depthLow?.Dispose();
        _depthPictureView?.Dispose();
        _depthPicture?.Dispose();
        _depthLowView = null;
        _depthLow = null;
        _depthPictureView = null;
        _depthPicture = null;
    }

    /// <summary>Draws the lit picture from <see cref="Scene"/> into <paramref name="output"/>.</summary>
    /// <param name="custom">Packages' effects drawn after Vizstrap's own, in order (ones that didn't compile are left out).</param>
    public void Render(ID3D11RenderTargetView output, EffectParameters parameters, float time, IReadOnlyList<CustomPass>? custom = null)
    {
        if (_scene is null)
            throw new InvalidOperationException("Resize first.");

        bool useDepth = parameters.UseDepth && _depthLowView is not null;
        var constants = new Constants
        {
            OutputSize = new Vector2(Width, Height),
            OutputTexel = new Vector2(1f / Width, 1f / Height),
            AoStrength = parameters.AmbientOcclusion,
            GiStrength = parameters.IndirectLight,
            Haze = parameters.Haze,
            DepthOfField = parameters.DepthOfField,
            Bloom = parameters.Bloom,
            Contrast = parameters.Contrast,
            Saturation = parameters.Saturation,
            Warmth = parameters.Warmth,
            Vignette = parameters.Vignette,
            Sharpen = parameters.Sharpen,
            FocusDepth = FocusDepth,
            Time = time,
            UseDepth = useDepth ? 1 : 0,
            TanHalfFov = TanHalfFov,
            Aspect = (float)Width / Height,
            UiBand = UiBand,
            Reflections = parameters.Reflections,
            SunRays = parameters.SunRays,
            FrameIndex = _frame++ % 1024,
            HistoryValid = _historyValid ? 1 : 0,
            SunMip = MathF.Floor(MathF.Log2(Math.Max(_sunCentre.Width, _sunCentre.Height))),
            HasDepthPicture = _depthPictureView is not null ? 1 : 0,
        };

        _context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        _context.VSSetShader(_vertexShader);
        _context.PSSetSamplers(0, [_linear, _nearest]);

        // the picture, blurred smaller and smaller
        Pass("PS_DownFirst", _picture[0], constants, _scene);
        for (int level = 1; level < _picture.Length; level++)
            Pass("PS_Down", _picture[level], constants, _picture[level - 1]);

        int previous = _history, current = 1 - _history;
        Target? depth = null, lighting = null, reflections = null, sunRays = null;

        if (useDepth)
        {
            depth = _depth[current];
            Pass("PS_Depth", depth, constants, _scene.View, _depthLowView, _depth[previous].View, _picture[1].View, _depthPictureView);

            // contact shadows and bounced light, averaged over frames, then smoothed along surfaces
            Pass("PS_Lighting", _lightingRaw, constants, depth.View, _picture[1].View);
            Pass("PS_Accumulate", _lightingHistory[current], constants, _lightingRaw, _lightingHistory[previous]);
            Blur(_lightingHistory[current], _lightingTemp, _lightingBlurred, depth, constants);
            lighting = _lightingBlurred;

            if (parameters.Reflections > 0)
            {
                Pass("PS_Reflections", _reflectionRaw, constants, depth.View, _picture[0].View);
                // averaged over frames and blurred only a little: a sheen, not a smear
                Pass("PS_Accumulate", _reflectionHistory[current], constants, _reflectionRaw, _reflectionHistory[previous]);
                Pass("PS_BlurLighting", _reflectionTemp, constants with { BlurDirection = new Vector2(0.5f, 0) }, _reflectionHistory[current], depth);
                Pass("PS_BlurLighting", _reflectionBlurred, constants with { BlurDirection = new Vector2(0, 0.5f) }, _reflectionTemp, depth);
                reflections = _reflectionBlurred;
            }

            if (parameters.SunRays > 0)
            {
                Pass("PS_SunLight", _sunLight, constants, _picture[1].View, depth.View);
                Pass("PS_SunCentre", _sunCentre, constants, _picture[1].View, depth.View);
                _context.GenerateMips(_sunCentre.View);
                Pass("PS_SunRays", _sunRays, constants, _sunLight.View, _sunCentre.View);
                sunRays = _sunRays;
            }

            _history = current;
            _historyValid = true;
        }

        // the glow: bright parts, down to 1/32 and back up, adding each level on the way
        Pass("PS_BrightPass", _glowDown[0], constants, _picture[0]);
        for (int level = 1; level < _glowDown.Length; level++)
            Pass("PS_Down", _glowDown[level], constants, _glowDown[level - 1]);

        Pass("PS_UpAdd", _glowUp[^1], constants, _glowDown[^1], _glowDown[^2]);
        for (int level = _glowUp.Length - 2; level >= 0; level--)
            Pass("PS_UpAdd", _glowUp[level], constants, _glowUp[level + 1], _glowDown[level]);

        var passes = (custom ?? []).Where(pass => _custom.ContainsKey(pass.Id)).ToList();

        if (passes.Count == 0)
        {
            Pass("PS_Composite", output, Width, Height, constants, _scene.View, depth?.View, lighting?.View,
                _picture[0].View, _picture[1].View, _picture[2].View, _glowUp[0].View, reflections?.View, sunRays?.View);
            return;
        }

        // Vizstrap's picture into a spare target, then each package effect in turn, the last one into the output
        _customA ??= new Target(_device, Width, Height, Format.B8G8R8A8_UNorm, renderTarget: true);
        _customB ??= new Target(_device, Width, Height, Format.B8G8R8A8_UNorm, renderTarget: true);
        Pass("PS_Composite", _customA, constants, _scene.View, depth?.View, lighting?.View,
            _picture[0].View, _picture[1].View, _picture[2].View, _glowUp[0].View, reflections?.View, sunRays?.View);

        Target source = _customA, spare = _customB;

        for (int index = 0; index < passes.Count; index++)
        {
            bool last = index == passes.Count - 1;
            WriteCustomValues(passes[index].Values);
            _context.PSSetShaderResources(0, new ID3D11ShaderResourceView?[ShaderInputs]!);
            _context.OMSetRenderTargets(last ? output : spare.RenderTarget!);
            _context.RSSetViewport(new Viewport(Width, Height));
            WriteConstants(constants);
            _context.PSSetConstantBuffer(0, _constants);
            _context.PSSetConstantBuffer(1, _customConstants);
            _context.PSSetShaderResources(0, new ID3D11ShaderResourceView?[] { source.View, depth?.View }!);
            _context.PSSetShader(_custom[passes[index].Id]);
            _context.Draw(3, 0);
            (source, spare) = (spare, source);
        }
    }

    /// <summary>Draws the picture the depth AI will look at, at its size.</summary>
    public void DrawModelInput(int width, int height) => DrawPicture(ref _modelInput, width, height, _picture[1]);

    /// <summary>Draws the half-size picture for the settings' preview.</summary>
    public void DrawPreviewPicture() => DrawPicture(ref _previewPicture, _picture[0].Width, _picture[0].Height, _picture[0]);

    public void Dispose()
    {
        foreach (var resource in _sized)
            resource.Dispose();
        _modelInput?.Dispose();
        _previewPicture?.Dispose();
        ClearDepth();
        _customA?.Dispose();
        _customB?.Dispose();
        foreach (var shader in _custom.Values)
            shader.Dispose();
        _customConstants.Dispose();
        _constants.Dispose();
        _linear.Dispose();
        _nearest.Dispose();
        _vertexShader.Dispose();
        foreach (var shader in _pixelShaders.Values)
            shader.Dispose();
    }

    // ---- passes

    private void DrawPicture(ref Target? target, int width, int height, Target source)
    {
        if (target is null || target.Width != width || target.Height != height)
        {
            target?.Dispose();
            target = new Target(_device, width, height, Format.B8G8R8A8_UNorm, renderTarget: true);
        }

        _context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        _context.VSSetShader(_vertexShader);
        _context.PSSetSamplers(0, [_linear, _nearest]);
        Pass("PS_ToPicture", target, new Constants(), source);
    }

    private void Blur(Target source, Target temp, Target result, Target depth, Constants constants)
    {
        Pass("PS_BlurLighting", temp, constants with { BlurDirection = Vector2.UnitX }, source, depth);
        Pass("PS_BlurLighting", result, constants with { BlurDirection = Vector2.UnitY }, temp, depth);
    }

    private void Pass(string shader, Target target, Constants constants, params Target?[] sources) =>
        Pass(shader, target.RenderTarget!, target.Width, target.Height,
            constants with { SourceTexel = sources[0] is { } first ? new Vector2(1f / first.Width, 1f / first.Height) : Vector2.Zero },
            [.. sources.Select(source => source?.View)]);

    private void Pass(string shader, Target target, Constants constants, params ID3D11ShaderResourceView?[] sources) =>
        Pass(shader, target.RenderTarget!, target.Width, target.Height, constants, sources);

    private unsafe void Pass(string shader, ID3D11RenderTargetView target, int width, int height, Constants constants, params ID3D11ShaderResourceView?[] sources)
    {
        // nothing may be read and written at once: clear the inputs before the new target goes on
        _context.PSSetShaderResources(0, new ID3D11ShaderResourceView?[ShaderInputs]!);
        _context.OMSetRenderTargets(target);
        _context.RSSetViewport(new Viewport(width, height));

        WriteConstants(constants);
        _context.PSSetConstantBuffer(0, _constants);
        _context.PSSetShaderResources(0, sources!);
        _context.PSSetShader(_pixelShaders[shader]);
        _context.Draw(3, 0);
    }

    private unsafe void WriteConstants(Constants constants)
    {
        var mapped = _context.Map(_constants, 0, MapMode.WriteDiscard);
        Unsafe.Write((void*)mapped.DataPointer, constants);
        _context.Unmap(_constants, 0);
    }

    private unsafe void WriteCustomValues(float[] values)
    {
        var mapped = _context.Map(_customConstants, 0, MapMode.WriteDiscard);
        var target = new Span<float>((void*)mapped.DataPointer, CustomEffectPrelude.ParameterCount);
        target.Clear();
        values.AsSpan(0, Math.Min(values.Length, target.Length)).CopyTo(target);
        _context.Unmap(_customConstants, 0);
    }

    private static string BlobText(Vortice.Direct3D.Blob blob) => blob.AsString().TrimEnd('\0', '\n', '\r', ' ');

    // ---- set-up

    private static int Half(int size, int level) => Math.Max(1, (size + (1 << level) - 1) >> level);

    private Target Create(int width, int height, Format format, bool renderTarget = true, bool mipmapped = false)
    {
        var target = new Target(_device, width, height, format, renderTarget, mipmapped);
        _sized.Add(target);
        return target;
    }

    private static string ReadShaderSource()
    {
        using var stream = typeof(EffectRenderer).Assembly.GetManifestResourceStream("Effects.hlsl")
            ?? throw new InvalidOperationException("The effect shaders are missing from the build.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static ReadOnlyMemory<byte> Compile(string source, string entryPoint, string profile) =>
        Compiler.Compile(source, entryPoint, "Effects.hlsl", profile, ShaderFlags.OptimizationLevel3);

    /// <summary>The constant buffer, laid out as in the shaders' cbuffer.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private record struct Constants
    {
        public Vector2 OutputSize;
        public Vector2 OutputTexel;
        public Vector2 SourceTexel;
        public Vector2 BlurDirection;
        public float AoStrength;
        public float GiStrength;
        public float Haze;
        public float DepthOfField;
        public float Bloom;
        public float Contrast;
        public float Saturation;
        public float Warmth;
        public float Vignette;
        public float Sharpen;
        public float FocusDepth;
        public float Time;
        public float UseDepth;
        public float TanHalfFov;
        public float Aspect;
        public float UiBand;
        public float Reflections;
        public float SunRays;
        public float FrameIndex;
        public float HistoryValid;
        public float SunMip;
        public float HasDepthPicture;
        public float Padding0;
        public float Padding1;
    }

    /// <summary>A texture with its views.</summary>
    private sealed class Target : IDisposable
    {
        public Target(ID3D11Device device, int width, int height, Format format, bool renderTarget, bool mipmapped = false)
        {
            Width = width;
            Height = height;
            Texture = device.CreateTexture2D(new Texture2DDescription(format, (uint)width, (uint)height, 1, mipmapped ? 0u : 1u,
                renderTarget ? BindFlags.ShaderResource | BindFlags.RenderTarget : BindFlags.ShaderResource,
                miscFlags: mipmapped ? ResourceOptionFlags.GenerateMips : ResourceOptionFlags.None));
            View = device.CreateShaderResourceView(Texture);
            RenderTarget = renderTarget ? device.CreateRenderTargetView(Texture) : null;
        }

        public int Width { get; }

        public int Height { get; }

        public ID3D11Texture2D Texture { get; }

        public ID3D11ShaderResourceView View { get; }

        public ID3D11RenderTargetView? RenderTarget { get; }

        public void Dispose()
        {
            RenderTarget?.Dispose();
            View.Dispose();
            Texture.Dispose();
        }
    }
}
