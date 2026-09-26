using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Xml;
using System.Xml.Linq;
using Vizstrap.Core.Appearance;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace Vizstrap.Views.Loading.XmlTheme;

/// <summary>A theme file Vizstrap can't show; the message says what and where.</summary>
public sealed class XmlThemeException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// A loading window built from a theme in Bloxstrap's XML format ("BloxstrapCustomBootstrapper",
/// version 1), so Bloxstrap's custom themes work in Vizstrap. The element set, attributes and defaults
/// follow Bloxstrap's CustomDialog (MIT, see THIRD-PARTY-NOTICES.md); named elements are wired to the
/// launch: StatusText, PrimaryProgressBar, PrimaryProgressRing and CancelButton.
/// </summary>
public sealed partial class XmlThemeLoadingWindow : FluentWindow
{
    private const int SupportedVersion = 1;
    private const int MaxElements = 100;

    private readonly Grid _root = new();
    private readonly Grid _elementGrid = new();
    private readonly TitleBar _titleBar;
    private readonly HashSet<string> _usedNames = new(StringComparer.Ordinal);
    private readonly string _themeDirectory;
    private readonly bool _appIsDark;

    /// <param name="themeDirectory">The theme's folder; "theme://" paths and relative paths start there.</param>
    public XmlThemeLoadingWindow(string themeDirectory, bool appIsDark)
    {
        _themeDirectory = themeDirectory;
        _appIsDark = appIsDark;

        // Bloxstrap's CustomDialog.xaml, in code
        Width = 800;
        Height = 450;
        MinWidth = 150;
        MinHeight = 150;
        MaxWidth = 1000;
        MaxHeight = 1000;
        ResizeMode = ResizeMode.NoResize;
        ExtendsContentIntoTitleBar = true;
        WindowBackdropType = WindowBackdropType.None;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SetResourceReference(BackgroundProperty, "ApplicationBackgroundBrush");

        _titleBar = new TitleBar
        {
            Title = "",
            Padding = new Thickness(8),
            CanMaximize = false,
            ShowClose = false,
            ShowMaximize = false,
            ShowMinimize = false,
        };
        Panel.SetZIndex(_titleBar, 1001);

        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _root.Children.Add(_titleBar);
        Grid.SetRow(_elementGrid, 1);
        _root.Children.Add(_elementGrid);
        Content = _root;
    }

    /// <summary>Reads Theme.xml from the folder and builds the window, or throws <see cref="XmlThemeException"/>.</summary>
    public static XmlThemeLoadingWindow Load(string themeDirectory, bool appIsDark)
    {
        string file = Path.Combine(themeDirectory, XmlThemes.ThemeFile);
        XElement xml;

        try
        {
            xml = XElement.Load(file);
        }
        catch (Exception ex) when (ex is XmlException or IOException or UnauthorizedAccessException)
        {
            throw new XmlThemeException($"{XmlThemes.ThemeFile} can't be read: {ex.Message}", ex);
        }

        var window = new XmlThemeLoadingWindow(themeDirectory, appIsDark);
        window.Build(xml);
        return window;
    }

    private void Build(XElement xml)
    {
        if (xml.Name != "BloxstrapCustomBootstrapper")
            throw new XmlThemeException("The root element must be BloxstrapCustomBootstrapper.");

        string? version = xml.Attribute("Version")?.Value;

        if (version is null)
            throw new XmlThemeException("BloxstrapCustomBootstrapper needs a Version.");

        if (!uint.TryParse(version, NumberStyles.None, CultureInfo.InvariantCulture, out uint number) || number != SupportedVersion)
            throw new XmlThemeException($"Theme version {version} isn't supported (only {SupportedVersion}).");

        int count = xml.Descendants().Count();

        if (count > MaxElements)
            throw new XmlThemeException($"The theme has {count} elements; at most {MaxElements} are allowed.");

        BuildRoot(xml);

        foreach (var child in xml.Elements())
        {
            // property elements ("BloxstrapCustomBootstrapper.Background") were handled with the root
            if (child.Name.LocalName.StartsWith("BloxstrapCustomBootstrapper.", StringComparison.Ordinal))
                continue;

            if (child.Name == "TitleBar")
            {
                BuildTitleBar(child);
                continue;
            }

            if (child.Name == "BloxstrapCustomBootstrapper")
                throw new XmlThemeException("BloxstrapCustomBootstrapper can only be the root.");

            _elementGrid.Children.Add(Create<UIElement>(child));
        }
    }

    private void BuildRoot(XElement xml)
    {
        if (xml.Attribute("Width") is not null)
            Width = Parse(xml, "Width", 800.0);

        if (xml.Attribute("Height") is not null)
            Height = Parse(xml, "Height", 450.0);

        ApplyControl(this, xml, frameworkBasics: false);

        // effects and transforms go on the content, as in Bloxstrap
        _elementGrid.RenderTransform = RenderTransform;
        RenderTransform = null!;
        _elementGrid.LayoutTransform = LayoutTransform;
        LayoutTransform = null!;
        _elementGrid.Effect = Effect;
        Effect = null;

        string theme = xml.Attribute("Theme")?.Value ?? "Default";
        bool dark = theme switch
        {
            "Dark" => true,
            "Light" => false,
            "Default" => _appIsDark,
            _ => throw new XmlThemeException($"Unknown Theme \"{theme}\" (Default, Dark or Light)."),
        };

        // WPF-UI's colours for the theme's light or dark, for this window only (what ThemesDictionary loads)
        Resources.MergedDictionaries.Clear();
        Resources.MergedDictionaries.Add((ResourceDictionary)Application.LoadComponent(
            new Uri($"/Wpf.Ui;component/Resources/Theme/{(dark ? "Dark" : "Light")}.xaml", UriKind.Relative)));
        _titleBar.ApplicationTheme = dark ? ApplicationTheme.Dark : ApplicationTheme.Light;

        // WPF-UI paints the window from this resource, and only with a plain colour (grey otherwise), so
        // the theme's background also goes on the content, which covers the whole window
        bool hasBackground = xml.Attribute("Background") is not null || xml.Element("BloxstrapCustomBootstrapper.Background") is not null;

        if (hasBackground)
        {
            _root.Background = Background;

            if (Background is System.Windows.Media.SolidColorBrush)
                Resources["ApplicationBackgroundBrush"] = Background;
        }

        WindowCornerPreference = ParseEnum(xml, "WindowCornerPreference", WindowCornerPreference.Round);

        // the root's margin frames the elements, not the window
        _elementGrid.Margin = Margin;
        Margin = new Thickness(0);
        Padding = new Thickness(0);

        Title = xml.Attribute("Title")?.Value ?? "Vizstrap";

        if (Parse(xml, "IgnoreTitleBarInset", false))
        {
            Grid.SetRow(_elementGrid, 0);
            Grid.SetRowSpan(_elementGrid, 2);
        }
    }

    private void BuildTitleBar(XElement xml)
    {
        ApplyControl(_titleBar, xml, frameworkBasics: false);

        // Bloxstrap keeps the title bar where it is and above everything
        _titleBar.RenderTransform = null!;
        _titleBar.LayoutTransform = null!;
        _titleBar.Effect = null;
        _titleBar.HorizontalAlignment = HorizontalAlignment.Stretch;
        _titleBar.Margin = new Thickness(0);
        Panel.SetZIndex(_titleBar, 1001);

        _titleBar.Visibility = ParseEnum(xml, "Visibility", Visibility.Visible);
        _titleBar.ShowMinimize = Parse(xml, "ShowMinimize", true);
        _titleBar.ShowClose = Parse(xml, "ShowClose", true);
        _titleBar.Title = xml.Attribute("Title")?.Value ?? "Vizstrap";
    }

    /// <summary>Binds a property of an element to the loading window's view model.</summary>
    private static void Bind(DependencyObject target, DependencyProperty property, string path, IValueConverter? converter = null) =>
        BindingOperations.SetBinding(target, property, new Binding(path) { Mode = BindingMode.OneWay, Converter = converter });
}
