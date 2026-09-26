using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vizstrap.Core.Effects;
using Vizstrap.Core.Logging;
using Vizstrap.Core.Packages;
using Vizstrap.Effects;
using Vizstrap.Localization;
using Vizstrap.Views;

namespace Vizstrap.ViewModels;

public sealed record EffectPresetOption(EffectPreset Value, string Name);

/// <summary>One slider of a package's effect, as its effects/name.json describes it.</summary>
public sealed partial class CustomParameterItem(EffectParameter parameter, float value) : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValueText))]
    private double _value = Math.Clamp(value, Math.Min(parameter.Min, parameter.Max), Math.Max(parameter.Min, parameter.Max));

    public string Name => parameter.Name;

    public double Min => parameter.Min;

    public double Max => parameter.Max;

    public double Step => Math.Abs(Max - Min) / 100;

    public string ValueText => Value.ToString("0.##");
}

/// <summary>A switched-on package's picture effect: on or off, its sliders, and why it can't run if it can't.</summary>
public sealed partial class CustomEffectItem : ObservableObject
{
    [ObservableProperty]
    private bool _enabled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ErrorText))]
    private string? _error;

    public CustomEffectItem(CustomEffect effect, string packageName, CustomEffectState? state)
    {
        Id = effect.Id;
        Title = $"{effect.Name} ({packageName})";
        _enabled = state?.Enabled ?? false;
        Parameters = [.. effect.Parameters.Select((parameter, index) =>
            new CustomParameterItem(parameter, state is not null && index < state.Values.Length ? state.Values[index] : parameter.Default))];
    }

    public string Id { get; }

    public string Title { get; }

    public IReadOnlyList<CustomParameterItem> Parameters { get; }

    public string? ErrorText => Error is null ? null : string.Format(Strings.Effects_PackageError, Error);

    public CustomEffectState ToState() => new(Id, Enabled, [.. Parameters.Select(parameter => (float)parameter.Value)]);
}

/// <summary>
/// The picture effects on the Shaders page: on/off, a look (preset) and the strength of each effect.
/// Picking a look moves the sliders; moving a slider picks the look it matches, or "Custom".
/// Saved with the other settings.
/// </summary>
public sealed partial class EffectsViewModel : ObservableObject
{
    private const string LogSource = nameof(EffectsViewModel);

    private EffectSettings _saved = null!;
    private bool _applyingLook;

    // settings of effects whose packages are off or gone: kept for when they're back
    private readonly List<CustomEffectState> _otherCustom = [];

    [ObservableProperty]
    private bool _enabled;

    [ObservableProperty]
    private EffectPresetOption _selectedPreset;

    [ObservableProperty] private bool _useDepth;
    [ObservableProperty] private int _ambientOcclusion;
    [ObservableProperty] private int _indirectLight;
    [ObservableProperty] private int _haze;
    [ObservableProperty] private int _depthOfField;
    [ObservableProperty] private int _reflections;
    [ObservableProperty] private int _sunRays;
    [ObservableProperty] private int _bloom;
    [ObservableProperty] private int _contrast;
    [ObservableProperty] private int _saturation;
    [ObservableProperty] private int _warmth;
    [ObservableProperty] private int _vignette;
    [ObservableProperty] private int _sharpen;

    public EffectsViewModel()
    {
        Presets =
        [
            new(EffectPreset.Realistic, Strings.Effects_Realistic),
            new(EffectPreset.Cinematic, Strings.Effects_Cinematic),
            new(EffectPreset.Vivid, Strings.Effects_Vivid),
            new(EffectPreset.Light, Strings.Effects_Light),
            new(EffectPreset.Custom, Strings.Effects_Custom),
        ];

        var settings = App.Settings.Value.Effects;
        _selectedPreset = Presets[0];
        Load(settings);
        LoadCustomEffects(settings);
        TakeSnapshot();
    }

    /// <summary>Picture effects from switched-on mod packages.</summary>
    public ObservableCollection<CustomEffectItem> CustomEffects { get; } = [];

    public bool HasCustomEffects => CustomEffects.Count > 0;

    public IReadOnlyList<EffectPresetOption> Presets { get; }

    public bool IsModelReady => DepthModel.IsReady(App.Paths.Effects);

    public string ModelStatus => IsModelReady ? Strings.Effects_ModelReady : Strings.Effects_ModelMissing;

    /// <summary>A frame from the player's game is kept for the preview (see Effects.PreviewFrame).</summary>
    public bool HasPreview => PreviewFrame.Exists(App.Paths.Effects);

    public string PreviewText => HasPreview ? Strings.Effects_PreviewDescription : Strings.Effects_PreviewMissing;

    public bool IsDirty => !Current().SameAs(_saved);

    public EffectSettings Current() => new()
    {
        Enabled = Enabled,
        Preset = SelectedPreset.Value,
        UseDepth = UseDepth,
        AmbientOcclusion = AmbientOcclusion,
        IndirectLight = IndirectLight,
        Haze = Haze,
        DepthOfField = DepthOfField,
        Reflections = Reflections,
        SunRays = SunRays,
        Bloom = Bloom,
        Contrast = Contrast,
        Saturation = Saturation,
        Warmth = Warmth,
        Vignette = Vignette,
        Sharpen = Sharpen,
        Custom = [.. CustomEffects.Select(item => item.ToState()), .. _otherCustom],
    };

    public void Save()
    {
        App.Settings.Value.Effects = Current();
        TakeSnapshot();
        OnPropertyChanged(nameof(ModelStatus));
        Log.Info(LogSource, $"Effects {(Enabled ? "on" : "off")}, look {SelectedPreset.Value}, depth AI {UseDepth}");
    }

    [RelayCommand]
    private void OpenPreview()
    {
        var owner = Application.Current.Windows.OfType<SettingsWindow>().FirstOrDefault();

        if (!EffectsPreviewWindow.TryShow(this, owner))
        {
            OnPropertyChanged(nameof(HasPreview));
            OnPropertyChanged(nameof(PreviewText));
        }
    }

    partial void OnSelectedPresetChanged(EffectPresetOption value)
    {
        if (_applyingLook || EffectPresets.Look(value.Value) is not { } look)
            return;

        _applyingLook = true;
        Load(look);
        _applyingLook = false;
    }

    // any slider: the look becomes the preset it matches, or "Custom"
    partial void OnUseDepthChanged(bool value) => MatchPreset();
    partial void OnAmbientOcclusionChanged(int value) => MatchPreset();
    partial void OnIndirectLightChanged(int value) => MatchPreset();
    partial void OnHazeChanged(int value) => MatchPreset();
    partial void OnDepthOfFieldChanged(int value) => MatchPreset();
    partial void OnReflectionsChanged(int value) => MatchPreset();
    partial void OnSunRaysChanged(int value) => MatchPreset();
    partial void OnBloomChanged(int value) => MatchPreset();
    partial void OnContrastChanged(int value) => MatchPreset();
    partial void OnSaturationChanged(int value) => MatchPreset();
    partial void OnWarmthChanged(int value) => MatchPreset();
    partial void OnVignetteChanged(int value) => MatchPreset();
    partial void OnSharpenChanged(int value) => MatchPreset();

    private void MatchPreset()
    {
        if (_applyingLook)
            return;

        var match = EffectPresets.Match(Current());

        if (match == SelectedPreset.Value)
            return;

        _applyingLook = true;
        SelectedPreset = Presets.First(option => option.Value == match);
        _applyingLook = false;
    }

    private void Load(EffectSettings settings)
    {
        bool applying = _applyingLook;
        _applyingLook = true;

        if (!applying)
            Enabled = settings.Enabled;

        UseDepth = settings.UseDepth;
        AmbientOcclusion = settings.AmbientOcclusion;
        IndirectLight = settings.IndirectLight;
        Haze = settings.Haze;
        DepthOfField = settings.DepthOfField;
        Reflections = settings.Reflections;
        SunRays = settings.SunRays;
        Bloom = settings.Bloom;
        Contrast = settings.Contrast;
        Saturation = settings.Saturation;
        Warmth = settings.Warmth;
        Vignette = settings.Vignette;
        Sharpen = settings.Sharpen;
        SelectedPreset = Presets.First(option => option.Value == EffectPresets.Match(settings));

        _applyingLook = applying;
    }

    private void LoadCustomEffects(EffectSettings settings)
    {
        var effects = new List<CustomEffect>();

        foreach (var package in App.EnabledPackages())
        {
            foreach (var effect in package.Effects)
            {
                var item = new CustomEffectItem(effect, package.Manifest.Name, settings.Custom.FirstOrDefault(state => state.Id == effect.Id));
                item.PropertyChanged += (_, _) => OnPropertyChanged(nameof(CustomEffects));

                foreach (var parameter in item.Parameters)
                    parameter.PropertyChanged += (_, _) => OnPropertyChanged(nameof(CustomEffects));

                CustomEffects.Add(item);
                effects.Add(effect);
            }
        }

        _otherCustom.AddRange(settings.Custom.Where(state => CustomEffects.All(item => item.Id != state.Id)));

        if (effects.Count == 0)
            return;

        // compiled in the background, so an author sees at once what's wrong with an effect
        var dispatcher = Application.Current.Dispatcher;
        _ = Task.Run(() =>
        {
            try
            {
                var errors = CustomEffectSources.Check(effects);
                dispatcher.BeginInvoke(() =>
                {
                    foreach (var item in CustomEffects)
                        item.Error = errors.TryGetValue(item.Id, out var error) ? error : null;
                });
            }
            catch (Exception ex)
            {
                Log.Warn(LogSource, $"Package effects couldn't be checked: {ex.Message}");
            }
        });
    }

    private void TakeSnapshot() => _saved = Current();
}
