using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vizstrap.Core.FastFlags;
using Vizstrap.Core.Logging;
using Vizstrap.Localization;

namespace Vizstrap.ViewModels;

public sealed record MsaaOption(MsaaMode Value, string Name);

public sealed record TextureOption(TextureQuality Value, string Name);

public sealed record ApiOption(GraphicsApi Value, string Name);

public sealed record QualityOption(int? Level, string Name);

public sealed record GeometryOption(GeometryDetail Value, string Name);

/// <summary>One row of the Fast Flag editor.</summary>
public sealed partial class FlagRow : ObservableObject
{
    private readonly Action<FlagRow, string> _onValueChanged;

    [ObservableProperty]
    private string _value;

    public FlagRow(string name, string value, Action<FlagRow, string> onValueChanged)
    {
        Name = name;
        _value = value;
        _onValueChanged = onValueChanged;
        IsAllowed = FastFlagAllowlist.IsAllowed(name);
    }

    public string Name { get; }

    /// <summary>Roblox only applies allowlisted flags; the rest are silently ignored.</summary>
    public bool IsAllowed { get; }

    public string Status => IsAllowed ? Strings.Flags_Allowed : Strings.Flags_Ignored;

    partial void OnValueChanged(string value) => _onValueChanged(this, value);
}

/// <summary>
/// The "Engine settings" page and its Fast Flag editor, like Bloxstrap's. Presets and the editor work on
/// one pending flag dictionary that is written to Modifications\ClientSettings\ClientAppSettings.json on Save.
/// </summary>
public sealed partial class EngineSettingsViewModel : ObservableObject
{
    private const string LogSource = nameof(EngineSettingsViewModel);

    private readonly FastFlagFile _file = new(App.Paths);
    private Dictionary<string, string> _saved;
    private Dictionary<string, string> _flags;
    private Dictionary<string, string>? _beforeReset;
    private bool _savedUseManager;

    [ObservableProperty]
    private bool _useFastFlagManager;

    [ObservableProperty]
    private string _search = "";

    public EngineSettingsViewModel()
    {
        MsaaModes =
        [
            new(MsaaMode.Automatic, Strings.Flags_Automatic),
            new(MsaaMode.X1, "1x"),
            new(MsaaMode.X2, "2x"),
            new(MsaaMode.X4, "4x"),
        ];
        TextureQualities =
        [
            new(TextureQuality.Automatic, Strings.Flags_Automatic),
            new(TextureQuality.Level0, string.Format(Strings.Flags_Level, 0)),
            new(TextureQuality.Level1, string.Format(Strings.Flags_Level, 1)),
            new(TextureQuality.Level2, string.Format(Strings.Flags_Level, 2)),
            new(TextureQuality.Level3, string.Format(Strings.Flags_Level, 3)),
        ];
        GraphicsApis =
        [
            new(GraphicsApi.Automatic, Strings.Flags_Automatic),
            new(GraphicsApi.Direct3D11, "Direct3D 11"),
            new(GraphicsApi.Vulkan, "Vulkan"),
            new(GraphicsApi.OpenGL, "OpenGL"),
        ];
        GeometryDetails =
        [
            new(GeometryDetail.Automatic, Strings.Flags_Automatic),
            new(GeometryDetail.Low, Strings.Flags_GeometryLow),
            new(GeometryDetail.High, Strings.Flags_GeometryHigh),
        ];
        QualityLevels =
        [
            new(null, Strings.Flags_Automatic),
            .. Enumerable.Range(1, FastFlagPresets.MaxQualityLevel).Select(level => new QualityOption(level, level.ToString())),
        ];

        _saved = _file.Load();
        _flags = new Dictionary<string, string>(_saved);
        _useFastFlagManager = _savedUseManager = App.Settings.Value.UseFastFlagManager;
        RebuildRows();
    }

    public IReadOnlyList<MsaaOption> MsaaModes { get; }

    public IReadOnlyList<TextureOption> TextureQualities { get; }

    public IReadOnlyList<ApiOption> GraphicsApis { get; }

    public IReadOnlyList<QualityOption> QualityLevels { get; }

    public IReadOnlyList<GeometryOption> GeometryDetails { get; }

    public ObservableCollection<FlagRow> Rows { get; } = new();

    public int FlagCount => _flags.Count;

    public int IgnoredCount => _flags.Keys.Count(name => !FastFlagAllowlist.IsAllowed(name));

    public bool HasIgnoredFlags => IgnoredCount > 0;

    public string IgnoredSummary => string.Format(Strings.Flags_IgnoredSummary, IgnoredCount);

    public bool IsDirty =>
        UseFastFlagManager != _savedUseManager ||
        _flags.Count != _saved.Count ||
        _flags.Any(pair => !_saved.TryGetValue(pair.Key, out string? value) || value != pair.Value);

    // ---- presets (read from and written to the pending flags)

    public MsaaOption SelectedMsaa
    {
        get => MsaaModes.First(option => option.Value == FastFlagPresets.GetMsaa(_flags));
        set => ChangeSelection(value, () => FastFlagPresets.SetMsaa(_flags, value.Value));
    }

    public TextureOption SelectedTextureQuality
    {
        get => TextureQualities.First(option => option.Value == FastFlagPresets.GetTextureQuality(_flags));
        set => ChangeSelection(value, () => FastFlagPresets.SetTextureQuality(_flags, value.Value));
    }

    public ApiOption SelectedGraphicsApi
    {
        get => GraphicsApis.First(option => option.Value == FastFlagPresets.GetGraphicsApi(_flags));
        set => ChangeSelection(value, () => FastFlagPresets.SetGraphicsApi(_flags, value.Value));
    }

    public QualityOption SelectedQualityLevel
    {
        get => QualityLevels.First(option => option.Level == FastFlagPresets.GetQualityLevel(_flags));
        set => ChangeSelection(value, () => FastFlagPresets.SetQualityLevel(_flags, value.Level));
    }

    public bool DisableDpiScale
    {
        get => FastFlagPresets.GetDisableDpiScale(_flags);
        set => Change(() => FastFlagPresets.SetDisableDpiScale(_flags, value));
    }

    public bool ExclusiveFullscreen
    {
        get => FastFlagPresets.GetExclusiveFullscreen(_flags);
        set => Change(() => FastFlagPresets.SetExclusiveFullscreen(_flags, value));
    }

    public bool GraySky
    {
        get => FastFlagPresets.GetGraySky(_flags);
        set => Change(() => FastFlagPresets.SetGraySky(_flags, value));
    }

    public bool NoGrass
    {
        get => FastFlagPresets.GetNoGrass(_flags);
        set => Change(() => FastFlagPresets.SetNoGrass(_flags, value));
    }

    public GeometryOption SelectedGeometryDetail
    {
        get => GeometryDetails.First(option => option.Value == FastFlagPresets.GetGeometryDetail(_flags));
        set => ChangeSelection(value, () => FastFlagPresets.SetGeometryDetail(_flags, value.Value));
    }

    public bool PauseVoxelizer
    {
        get => FastFlagPresets.GetPauseVoxelizer(_flags);
        set => Change(() => FastFlagPresets.SetPauseVoxelizer(_flags, value));
    }

    public bool StillGrass
    {
        get => FastFlagPresets.GetStillGrass(_flags);
        set => Change(() => FastFlagPresets.SetStillGrass(_flags, value));
    }

    /// <summary>One click for the most FPS; like every preset, it waits for Save.</summary>
    [RelayCommand]
    private void ApplyPerformanceProfile() => Change(() => FastFlagPresets.ApplyPerformanceProfile(_flags));

    [RelayCommand]
    private void ApplyQualityProfile() => Change(() => FastFlagPresets.ApplyQualityProfile(_flags));

    /// <summary>Clears every flag on Save; switching it back off restores them (like Bloxstrap).</summary>
    public bool ResetAll
    {
        get => _beforeReset is not null;
        set
        {
            if (value == ResetAll)
                return;

            if (value)
            {
                _beforeReset = new Dictionary<string, string>(_flags);
                _flags.Clear();
            }
            else
            {
                _flags = _beforeReset!;
                _beforeReset = null;
            }

            Changed();
        }
    }

    // ---- editor

    partial void OnSearchChanged(string value) => RebuildRows();

    /// <summary>Adds or replaces one flag; returns the reason it was refused, or null.</summary>
    public string? AddFlag(string name, string value)
    {
        name = name.Trim();
        string? problem = FastFlagFile.ValueProblem(name, value.Trim());

        if (problem is not null)
            return ProblemMessage(problem);

        _flags[name] = FastFlagFile.Normalize(name, value);
        LeaveReset();
        Changed();
        return null;
    }

    /// <summary>Merges a JSON object of flags; returns the reason it was refused, or null.</summary>
    public string? ImportJson(string json)
    {
        Dictionary<string, string> imported;

        try
        {
            imported = FastFlagFile.Parse(json);
        }
        catch (Exception ex) when (ex is JsonException or FormatException)
        {
            return Strings.Flags_InvalidJson;
        }

        var badName = imported.Keys.FirstOrDefault(name => FastFlagFile.TypeOf(name) is null);

        if (badName is not null)
            return $"{Strings.Flags_InvalidName} ({badName})";

        foreach (var (name, value) in imported)
            _flags[name] = FastFlagFile.Normalize(name, value);

        LeaveReset();
        Changed();
        Log.Info(LogSource, $"Imported {imported.Count} flags");
        return null;
    }

    public void Delete(IEnumerable<FlagRow> rows)
    {
        foreach (var row in rows.ToList())
            _flags.Remove(row.Name);

        Changed();
    }

    public string ExportJson() => JsonSerializer.Serialize(
        _flags.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToDictionary(pair => pair.Key, pair => pair.Value),
        new JsonSerializerOptions { WriteIndented = true });

    [RelayCommand]
    private void OpenAllowlistAnnouncement() =>
        Process.Start(new ProcessStartInfo(FastFlagAllowlist.AnnouncementUrl) { UseShellExecute = true })?.Dispose();

    public void Save()
    {
        _file.Save(_flags);
        App.Settings.Value.UseFastFlagManager = UseFastFlagManager;

        _saved = new Dictionary<string, string>(_flags);
        _savedUseManager = UseFastFlagManager;
        _beforeReset = null;
        Changed();

        Log.Info(LogSource, $"Saved {_flags.Count} flags ({IgnoredCount} not allowlisted), manager {(UseFastFlagManager ? "on" : "off")}");
    }

    private void OnRowValueChanged(FlagRow row, string value)
    {
        string? problem = FastFlagFile.ValueProblem(row.Name, value);

        if (problem is not null)
        {
            Views.MessageWindow.ShowError(ProblemMessage(problem), null);
            RebuildRows();
            return;
        }

        _flags[row.Name] = FastFlagFile.Normalize(row.Name, value);
        Changed(rebuildRows: false);
    }

    private static string ProblemMessage(string problem) => problem switch
    {
        "name" => Strings.Flags_InvalidName,
        "bool" => Strings.Flags_InvalidBool,
        _ => Strings.Flags_InvalidInt,
    };

    private void LeaveReset() => _beforeReset = null;

    private void Change(Action apply)
    {
        apply();
        LeaveReset();
        Changed();
    }

    private void ChangeSelection(object? option, Action apply)
    {
        // a ComboBox briefly pushes null while its items are rebound
        if (option is not null)
            Change(apply);
    }

    private void Changed(bool rebuildRows = true)
    {
        if (rebuildRows)
            RebuildRows();

        // every preset reads the same dictionary, so refresh them all
        OnPropertyChanged(string.Empty);
    }

    private void RebuildRows()
    {
        Rows.Clear();

        var matching = _flags
            .Where(pair => Search.Length == 0 || pair.Key.Contains(Search, StringComparison.OrdinalIgnoreCase))
            .OrderBy(pair => pair.Key, StringComparer.Ordinal);

        foreach (var (name, value) in matching)
            Rows.Add(new FlagRow(name, value, OnRowValueChanged));
    }
}
