using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using Vizstrap.Core.Packages;
using Vizstrap.ViewModels;
using Vizstrap.Views.Widgets;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = Wpf.Ui.Controls.TextBox;

namespace Vizstrap.Views.SettingsPages;

/// <summary>
/// A tab a package adds (pages\name.xml), built from its elements with the same cards as Vizstrap's own pages. The
/// settings window has a fixed set of these (<see cref="Slots"/>, one type each, as its navigation creates pages by
/// type); slot n shows the n-th package tab.
/// </summary>
public abstract class PackagePage : Page
{
    private readonly int _slot;
    private bool _built;

    protected PackagePage(int slot)
    {
        _slot = slot;
        ScrollViewer.SetCanContentScroll(this, false);
        Loaded += OnLoaded;
    }

    /// <summary>The page types for the package tabs, in order.</summary>
    public static IReadOnlyList<Type> Slots { get; } =
    [
        typeof(PackagePage1), typeof(PackagePage2), typeof(PackagePage3), typeof(PackagePage4), typeof(PackagePage5), typeof(PackagePage6),
        typeof(PackagePage7), typeof(PackagePage8), typeof(PackagePage9), typeof(PackagePage10), typeof(PackagePage11), typeof(PackagePage12),
    ];

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel settings || _slot >= settings.PackagePages.Pages.Count)
            return;

        var page = settings.PackagePages.Pages[_slot];

        if (!_built)
        {
            _built = true;
            Content = Build(page, settings);
        }

        if (page.UsesActivity)
            await settings.Playtime.EnsureLoadedAsync();
    }

    private static FrameworkElement Build(PackagePageViewModel model, SettingsViewModel settings)
    {
        var header = new StackPanel { Margin = new Thickness(0, 0, 24, 16) };
        header.Children.Add(Styled(new TextBlock { Text = model.Page.Title }, "PageTitle"));
        header.Children.Add(Styled(new TextBlock { Text = model.Page.Description ?? string.Format(Localization.Strings.Packages_PageFrom, model.Page.PackageName) }, "PageDescription"));

        var body = new StackPanel { Margin = new Thickness(0, 0, 24, 16) };

        foreach (var element in model.Page.Elements)
        {
            var control = Element(element, model, settings);

            if (element.VisibleWhen is { } condition && model.Input(condition.TrimStart('!')) is ToggleInput toggle)
            {
                control.SetBinding(VisibilityProperty, new Binding(nameof(ToggleInput.IsOn))
                {
                    Source = toggle,
                    Converter = (IValueConverter)Application.Current.Resources[condition.StartsWith('!') ? "InverseBoolToVisible" : "BoolToVisible"],
                });
            }

            body.Children.Add(control);
        }

        var scroller = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = body };
        Grid.SetRow(scroller, 1);

        var grid = new Grid { Margin = new Thickness(24, 20, 0, 0) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(header);
        grid.Children.Add(scroller);
        return grid;
    }

    private static FrameworkElement Element(PageElement element, PackagePageViewModel model, SettingsViewModel settings) => element switch
    {
        SectionElement section => Styled(new TextBlock { Text = section.Title, Margin = new Thickness(0, 16, 0, 10) }, "SectionTitle"),
        TextElement text => text.Muted
            ? Styled(new TextBlock { Text = text.Text, Margin = new Thickness(0, 0, 0, 8) }, "CardDescription")
            : new TextBlock { Text = text.Text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) },
        LinkElement link => Link(link),
        WidgetElement widget => Widget(widget.Name, settings),
        InputElement input => Input(model.Input(input.Id)!),
        _ => throw new NotSupportedException(element.GetType().Name),
    };

    private static FrameworkElement Input(PackageInput input)
    {
        FrameworkElement control = input switch
        {
            ToggleInput toggle => Bound(new ToggleSwitch(), ToggleSwitch.IsCheckedProperty, toggle, nameof(ToggleInput.IsOn)),
            SliderInput slider => SliderControl(slider),
            ChoiceInput choice => ChoiceControl(choice),
            TextInput text => Bound(new TextBox { Width = 240, MaxLength = text.MaxLength }, System.Windows.Controls.TextBox.TextProperty, text, nameof(PackageInput.Value),
                UpdateSourceTrigger.PropertyChanged),
            _ => throw new NotSupportedException(input.GetType().Name),
        };

        return Card(input.Title, input.Description, control);
    }

    private static FrameworkElement SliderControl(SliderInput input)
    {
        var slider = new Slider
        {
            Width = 200, Minimum = input.Slider.Min, Maximum = input.Slider.Max,
            SmallChange = input.Slider.Step, LargeChange = input.Slider.Step * 10, VerticalAlignment = VerticalAlignment.Center,
        };
        Bound(slider, RangeBase.ValueProperty, input, nameof(SliderInput.Number));

        var value = new TextBlock { Width = 48, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        value.SetResourceReference(TextBlock.ForegroundProperty, "NeonTextSecondaryBrush");
        Bound(value, TextBlock.TextProperty, input, nameof(SliderInput.NumberText), mode: BindingMode.OneWay);

        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(slider);
        panel.Children.Add(value);
        return panel;
    }

    private static FrameworkElement ChoiceControl(ChoiceInput input)
    {
        var box = new ComboBox
        {
            Width = 220, ItemsSource = input.Options,
            DisplayMemberPath = nameof(ChoiceOption.Title), SelectedValuePath = nameof(ChoiceOption.Value),
        };
        return Bound(box, Selector.SelectedValueProperty, input, nameof(PackageInput.Value));
    }

    private static FrameworkElement Link(LinkElement link)
    {
        var content = new StackPanel();
        content.Children.Add(Styled(new TextBlock { Text = link.Title }, "CardTitle"));

        if (link.Description is { } description)
            content.Children.Add(Styled(new TextBlock { Text = description }, "CardDescription"));

        var card = new CardAction { Icon = new SymbolIcon(SymbolRegular.Open24), Content = content };
        card.SetResourceReference(StyleProperty, "SettingsAction");
        card.Click += (_, _) => Process.Start(new ProcessStartInfo(link.Url) { UseShellExecute = true })?.Dispose();
        return card;
    }

    private static FrameworkElement Widget(string name, SettingsViewModel settings)
    {
        FrameworkElement widget = name switch
        {
            "PlaytimePeriod" => new PlaytimePeriodWidget(),
            "PlaytimeSummary" => new PlaytimeSummaryWidget(),
            "PlaytimeChart" => new PlaytimeChartWidget(),
            "PlaytimeGames" => new PlaytimeGamesWidget(),
            "PlaytimeClear" => new PlaytimeClearWidget(),
            _ => throw new NotSupportedException(name),
        };

        widget.DataContext = settings.Playtime;
        return widget;
    }

    private static FrameworkElement Card(string title, string? description, FrameworkElement control)
    {
        var header = new StackPanel();
        header.Children.Add(Styled(new TextBlock { Text = title }, "CardTitle"));

        if (description is not null)
            header.Children.Add(Styled(new TextBlock { Text = description }, "CardDescription"));

        var card = new CardControl { Header = header, Content = control };
        card.SetResourceReference(StyleProperty, "SettingsCard");
        return card;
    }

    private static T Styled<T>(T element, string style) where T : FrameworkElement
    {
        element.SetResourceReference(StyleProperty, style);
        return element;
    }

    private static T Bound<T>(T element, DependencyProperty property, object source, string path,
        UpdateSourceTrigger trigger = UpdateSourceTrigger.Default, BindingMode mode = BindingMode.TwoWay) where T : FrameworkElement
    {
        element.SetBinding(property, new Binding(path) { Source = source, Mode = mode, UpdateSourceTrigger = trigger });
        return element;
    }
}

public sealed class PackagePage1() : PackagePage(0);
public sealed class PackagePage2() : PackagePage(1);
public sealed class PackagePage3() : PackagePage(2);
public sealed class PackagePage4() : PackagePage(3);
public sealed class PackagePage5() : PackagePage(4);
public sealed class PackagePage6() : PackagePage(5);
public sealed class PackagePage7() : PackagePage(6);
public sealed class PackagePage8() : PackagePage(7);
public sealed class PackagePage9() : PackagePage(8);
public sealed class PackagePage10() : PackagePage(9);
public sealed class PackagePage11() : PackagePage(10);
public sealed class PackagePage12() : PackagePage(11);
