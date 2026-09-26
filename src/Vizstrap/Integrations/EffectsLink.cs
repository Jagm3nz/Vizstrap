using Vizstrap.Core.Effects;
using Vizstrap.Core.Storage;
using Vizstrap.Effects;
using Vizstrap.Localization;

namespace Vizstrap.Integrations;

/// <summary>
/// Ties the running picture effects to the settings file: a look picked in game with F7 is kept, and
/// changes saved in the settings while playing reach the effects within a second. The file is read
/// and written on its own (not through App.Settings), since the settings window is another process.
/// </summary>
internal sealed class EffectsLink
{
    private readonly string _settingsFile = App.Paths.SettingsFile;
    private readonly Lock _lock = new();
    private DateTime _lastSeen;

    private EffectsLink() => _lastSeen = Stamp();

    public static EffectsOptions Options()
    {
        var link = new EffectsLink();
        return new EffectsOptions(
            LookName: NameOf,
            SaveSettings: link.Save,
            ReadChangedSettings: link.ReadChanged,
            PreviewFolder: App.Paths.Effects);
    }

    public static string NameOf(EffectPreset preset) => preset switch
    {
        EffectPreset.Realistic => Strings.Effects_Realistic,
        EffectPreset.Cinematic => Strings.Effects_Cinematic,
        EffectPreset.Vivid => Strings.Effects_Vivid,
        EffectPreset.Light => Strings.Effects_Light,
        _ => Strings.Effects_Custom,
    };

    /// <summary>A look asked for by a plugin: kept in the settings, where the running effects pick it up.</summary>
    public static void SaveLook(EffectPreset preset)
    {
        var store = new JsonStore<Settings>(App.Paths.SettingsFile);
        store.Load();
        var look = EffectPresets.Look(preset)!;
        look.Enabled = store.Value.Effects.Enabled;
        look.Custom = store.Value.Effects.Custom;
        store.Value.Effects = look;
        store.Save();
    }

    private void Save(EffectSettings look)
    {
        lock (_lock)
        {
            var store = new JsonStore<Settings>(_settingsFile);
            store.Load();
            store.Value.Effects = look;
            store.Save();

            // our own write isn't a change to pick up
            _lastSeen = Stamp();
        }
    }

    private EffectSettings? ReadChanged()
    {
        lock (_lock)
        {
            var stamp = Stamp();

            if (stamp == _lastSeen)
                return null;

            _lastSeen = stamp;
            var store = new JsonStore<Settings>(_settingsFile);
            store.Load();
            return store.Value.Effects;
        }
    }

    private DateTime Stamp() => File.Exists(_settingsFile) ? File.GetLastWriteTimeUtc(_settingsFile) : DateTime.MinValue;
}
