using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Vizstrap.Core.Logging;
using Vizstrap.Core.Packages;

namespace Vizstrap.ViewModels;

/// <summary>A value the player sets on a package's tab, kept as text ("true", "0.5", a choice's value…).</summary>
public abstract partial class PackageInput(InputElement element, string value) : ObservableObject
{
    [ObservableProperty]
    private string _value = value;

    public InputElement Element { get; } = element;

    public string Id => Element.Id;

    public string Title => Element.Title;

    public string? Description => Element.Description;

    /// <summary>For the typed views of the value (on/off, a number) to follow it.</summary>
    protected virtual void ValueChanged()
    {
    }

    partial void OnValueChanged(string value) => ValueChanged();
}

public sealed class ToggleInput(ToggleElement element, string value) : PackageInput(element, value)
{
    public bool IsOn
    {
        get => Value == "true";
        set => Value = value ? "true" : "false";
    }

    protected override void ValueChanged() => OnPropertyChanged(nameof(IsOn));
}

public sealed class SliderInput(SliderElement element, string value) : PackageInput(element, value)
{
    public SliderElement Slider { get; } = element;

    public double Number
    {
        get => double.TryParse(Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number)
            ? Math.Clamp(number, Slider.Min, Slider.Max)
            : Slider.Default;
        set
        {
            // on the slider's steps, counted from its minimum
            double snapped = Math.Round((value - Slider.Min) / Slider.Step) * Slider.Step + Slider.Min;
            Value = Math.Clamp(snapped, Slider.Min, Slider.Max).ToString("0.######", CultureInfo.InvariantCulture);
        }
    }

    public string NumberText => Number.ToString("0.##", CultureInfo.CurrentCulture);

    protected override void ValueChanged()
    {
        OnPropertyChanged(nameof(Number));
        OnPropertyChanged(nameof(NumberText));
    }
}

public sealed class ChoiceInput(ChoiceElement element, string value) : PackageInput(element, value)
{
    public IReadOnlyList<ChoiceOption> Options { get; } = element.Options;
}

public sealed class TextInput(TextBoxElement element, string value) : PackageInput(element, value)
{
    public int MaxLength { get; } = element.MaxLength;
}

/// <summary>One tab from a package, with its inputs (or why it can't be shown).</summary>
public sealed class PackagePageViewModel
{
    public PackagePageViewModel(PackagePage page, IReadOnlyDictionary<string, string>? saved)
    {
        Page = page;
        Inputs = [.. page.Inputs.Select<InputElement, PackageInput>(input =>
        {
            string value = saved is not null && saved.TryGetValue(input.Id, out var stored) ? stored : input.DefaultValue;

            return input switch
            {
                ToggleElement toggle => new ToggleInput(toggle, value),
                SliderElement slider => new SliderInput(slider, value),
                ChoiceElement choice => new ChoiceInput(choice, choice.Options.Any(option => option.Value == value) ? value : choice.Default),
                TextBoxElement text => new TextInput(text, value.Length <= text.MaxLength ? value : value[..text.MaxLength]),
                _ => throw new NotSupportedException(input.GetType().Name),
            };
        })];
    }

    public PackagePage Page { get; }

    public IReadOnlyList<PackageInput> Inputs { get; }

    public PackageInput? Input(string id) => Inputs.FirstOrDefault(input => input.Id == id);

    public bool UsesActivity => Page.Elements.OfType<WidgetElement>().Any(widget => widget.Name.StartsWith("Playtime", StringComparison.Ordinal));
}

/// <summary>
/// The tabs switched-on packages add to the settings (up to <see cref="PackagePages.MaxPages"/>); what the player sets
/// on them is saved with the other settings, per package.
/// </summary>
public sealed partial class PackagePagesViewModel : ObservableObject
{
    private const string LogSource = nameof(PackagePagesViewModel);

    private Dictionary<string, Dictionary<string, string>> _saved = [];

    public PackagePagesViewModel()
    {
        var settings = App.Settings.Value;
        string language = (Localization.Strings.Culture ?? CultureInfo.CurrentUICulture).Name;
        var pages = new List<PackagePageViewModel>();

        foreach (var package in App.EnabledPackages())
        {
            foreach (var page in package.Pages(language, (file, error) => Log.Warn(LogSource, $"{package.Id}'s tab {file} can't be shown: {error}")))
            {
                if (pages.Count == PackagePages.MaxPages)
                {
                    Log.Warn(LogSource, $"More than {PackagePages.MaxPages} tabs from packages: {package.Id}'s {page.FileName} is left out");
                    continue;
                }

                var model = new PackagePageViewModel(page, settings.PackageValues.GetValueOrDefault(package.Id));

                foreach (var input in model.Inputs)
                    input.PropertyChanged += (_, _) => OnPropertyChanged(nameof(IsDirty));

                pages.Add(model);
            }
        }

        Pages = pages;
        TakeSnapshot();
    }

    public IReadOnlyList<PackagePageViewModel> Pages { get; }

    public bool IsDirty => Current().Any(package =>
        !_saved.TryGetValue(package.Key, out var saved) || package.Value.Any(value => saved.GetValueOrDefault(value.Key) != value.Value));

    public void Save()
    {
        var values = App.Settings.Value.PackageValues;

        foreach (var (package, inputs) in Current())
        {
            if (!values.TryGetValue(package, out var stored))
                values[package] = stored = [];

            foreach (var (id, value) in inputs)
                stored[id] = value;
        }

        TakeSnapshot();
    }

    private Dictionary<string, Dictionary<string, string>> Current()
    {
        var current = new Dictionary<string, Dictionary<string, string>>();

        foreach (var page in Pages)
        {
            if (!current.TryGetValue(page.Page.PackageId, out var inputs))
                current[page.Page.PackageId] = inputs = [];

            foreach (var input in page.Inputs)
                inputs[input.Id] = input.Value;
        }

        return current;
    }

    private void TakeSnapshot()
    {
        _saved = Current();
        OnPropertyChanged(nameof(IsDirty));
    }
}
