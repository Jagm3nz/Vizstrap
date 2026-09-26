using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Xml.Linq;
using Vizstrap.Localization;

namespace Vizstrap.Views.Loading.XmlTheme;

public sealed partial class XmlThemeLoadingWindow
{
    private delegate object Handler(XmlThemeLoadingWindow window, XElement xml);

    private static readonly Dictionary<string, Handler> Handlers = new(StringComparer.Ordinal)
    {
        ["Button"] = (window, xml) => window.CreateButton(xml),
        ["ProgressBar"] = (window, xml) => window.CreateProgressBar(xml),
        ["ProgressRing"] = (window, xml) => window.CreateProgressRing(xml),
        ["TextBlock"] = (window, xml) => window.CreateTextBlock(xml),
        ["MarkdownTextBlock"] = (window, xml) => window.CreateMarkdownTextBlock(xml),
        ["Image"] = (window, xml) => window.CreateImage(xml),
        ["Grid"] = (window, xml) => window.CreateGrid(xml),
        ["StackPanel"] = (window, xml) => window.CreateStackPanel(xml),
        ["Border"] = (window, xml) => window.CreateBorder(xml),

        ["SolidColorBrush"] = (window, xml) => window.CreateSolidColorBrush(xml),
        ["ImageBrush"] = (window, xml) => window.CreateImageBrush(xml),
        ["LinearGradientBrush"] = (window, xml) => window.CreateLinearGradientBrush(xml),
        ["GradientStop"] = (_, xml) => CreateGradientStop(xml),

        ["ScaleTransform"] = (_, xml) => new ScaleTransform(Parse(xml, "ScaleX", 1.0), Parse(xml, "ScaleY", 1.0), Parse(xml, "CenterX", 0.0), Parse(xml, "CenterY", 0.0)),
        ["SkewTransform"] = (_, xml) => new SkewTransform(Parse(xml, "AngleX", 0.0), Parse(xml, "AngleY", 0.0), Parse(xml, "CenterX", 0.0), Parse(xml, "CenterY", 0.0)),
        ["RotateTransform"] = (_, xml) => new RotateTransform(Parse(xml, "Angle", 0.0), Parse(xml, "CenterX", 0.0), Parse(xml, "CenterY", 0.0)),
        ["TranslateTransform"] = (_, xml) => new TranslateTransform(Parse(xml, "X", 0.0), Parse(xml, "Y", 0.0)),

        ["BlurEffect"] = (_, xml) => new BlurEffect
        {
            KernelType = ParseEnum(xml, "KernelType", KernelType.Gaussian),
            Radius = Parse(xml, "Radius", 5.0),
            RenderingBias = ParseEnum(xml, "RenderingBias", RenderingBias.Performance),
        },
        ["DropShadowEffect"] = (_, xml) => new DropShadowEffect
        {
            BlurRadius = Parse(xml, "BlurRadius", 5.0),
            Direction = Parse(xml, "Direction", 315.0),
            Opacity = Parse(xml, "Opacity", 1.0),
            ShadowDepth = Parse(xml, "ShadowDepth", 5.0),
            RenderingBias = ParseEnum(xml, "RenderingBias", RenderingBias.Performance),
            Color = Value<Color>(xml, "Color", new ColorConverter()) ?? Colors.Black,
        },

        ["Ellipse"] = (window, xml) => window.CreateShape(new Ellipse(), xml),
        ["Line"] = (window, xml) => window.CreateLine(xml),
        ["Rectangle"] = (window, xml) => window.CreateRectangle(xml),

        ["RowDefinition"] = (_, xml) => new RowDefinition
        {
            Height = Value<GridLength>(xml, "Height", new GridLengthConverter()) ?? new GridLength(1, GridUnitType.Star),
            MinHeight = Parse(xml, "MinHeight", 0.0),
            MaxHeight = Parse(xml, "MaxHeight", double.PositiveInfinity),
        },
        ["ColumnDefinition"] = (_, xml) => new ColumnDefinition
        {
            Width = Value<GridLength>(xml, "Width", new GridLengthConverter()) ?? new GridLength(1, GridUnitType.Star),
            MinWidth = Parse(xml, "MinWidth", 0.0),
            MaxWidth = Parse(xml, "MaxWidth", double.PositiveInfinity),
        },
    };

    /// <summary>Bloxstrap's text keys that themes use, in Vizstrap's words.</summary>
    private static readonly Dictionary<string, Func<string>> Texts = new(StringComparer.Ordinal)
    {
        ["Common.Cancel"] = () => Strings.Common_Cancel,
        ["Common.Close"] = () => Strings.Common_Close,
        ["Version"] = () => App.Version,
    };

    private T Create<T>(XElement xml) where T : class
    {
        string name = xml.Name.LocalName;

        if (!Handlers.TryGetValue(name, out var handler))
            throw new XmlThemeException($"Unknown element {name}.");

        return handler(this, xml) as T
            ?? throw new XmlThemeException($"{name} can't be used inside {xml.Parent?.Name.LocalName}.");
    }

    // ---- elements

    private Button CreateButton(XElement xml)
    {
        var button = new Button();
        ApplyControl(button, xml);
        button.Content = ReadContent(xml);

        if (NameOf(xml) == "CancelButton")
        {
            Bind(button, IsEnabledProperty, nameof(ViewModels.LoadingViewModel.CanCancel));
            Bind(button, ButtonBase.CommandProperty, nameof(ViewModels.LoadingViewModel.CancelCommand));
        }

        return button;
    }

    private ProgressBar CreateProgressBar(XElement xml)
    {
        var bar = new ProgressBar();
        ApplyControl(bar, xml);

        // Bloxstrap's CornerRadius and IndicatorCornerRadius have no counterpart here and are ignored
        bar.Value = Parse(xml, "Value", 0.0);
        bar.Maximum = Parse(xml, "Maximum", 100.0);
        bar.IsIndeterminate = Parse(xml, "IsIndeterminate", false);

        if (NameOf(xml) == "PrimaryProgressBar")
        {
            // the launch reports progress as a fraction
            bar.Maximum = 1;
            Bind(bar, ProgressBar.IsIndeterminateProperty, nameof(ViewModels.LoadingViewModel.IsIndeterminate));
            Bind(bar, RangeBase.ValueProperty, nameof(ViewModels.LoadingViewModel.Progress));
        }

        return bar;
    }

    private Wpf.Ui.Controls.ProgressRing CreateProgressRing(XElement xml)
    {
        var ring = new Wpf.Ui.Controls.ProgressRing();
        ApplyControl(ring, xml);

        double maximum = Parse(xml, "Maximum", 100.0);
        ring.Progress = maximum > 0 ? Parse(xml, "Value", 0.0) / maximum * 100 : 0;
        ring.IsIndeterminate = Parse(xml, "IsIndeterminate", false);

        if (NameOf(xml) == "PrimaryProgressRing")
        {
            Bind(ring, Wpf.Ui.Controls.ProgressRing.IsIndeterminateProperty, nameof(ViewModels.LoadingViewModel.IsIndeterminate));
            Bind(ring, Wpf.Ui.Controls.ProgressRing.ProgressProperty, nameof(ViewModels.LoadingViewModel.Progress), new PercentConverter());
        }

        return ring;
    }

    private TextBlock CreateTextBlock(XElement xml)
    {
        var text = new TextBlock();
        ApplyTextBlock(text, xml);
        text.Text = Translate(xml.Attribute("Text")?.Value);

        if (NameOf(xml) == "StatusText")
            Bind(text, TextBlock.TextProperty, nameof(ViewModels.LoadingViewModel.Status));

        return text;
    }

    /// <summary>**bold**, *italic* and [links](https://…); enough for what themes show.</summary>
    private TextBlock CreateMarkdownTextBlock(XElement xml)
    {
        var text = new TextBlock();
        ApplyTextBlock(text, xml);

        string markdown = Translate(xml.Attribute("Text")?.Value) ?? "";
        int position = 0;

        foreach (Match match in MarkdownPattern().Matches(markdown))
        {
            if (match.Index > position)
                text.Inlines.Add(new Run(markdown[position..match.Index]));

            if (match.Groups["bold"].Success)
                text.Inlines.Add(new Bold(new Run(match.Groups["bold"].Value)));
            else if (match.Groups["italic"].Success)
                text.Inlines.Add(new Italic(new Run(match.Groups["italic"].Value)));
            else
                text.Inlines.Add(Link(match.Groups["label"].Value, match.Groups["url"].Value));

            position = match.Index + match.Length;
        }

        if (position < markdown.Length)
            text.Inlines.Add(new Run(markdown[position..]));

        return text;
    }

    private static Inline Link(string label, string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            return new Run(label);

        var link = new Hyperlink(new Run(label)) { NavigateUri = uri };
        link.RequestNavigate += (_, e) => Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true })?.Dispose();
        return link;
    }

    private Image CreateImage(XElement xml)
    {
        var image = new Image();
        ApplyFrameworkElement(image, xml);

        image.Stretch = ParseEnum(xml, "Stretch", Stretch.Uniform);
        image.StretchDirection = ParseEnum(xml, "StretchDirection", StretchDirection.Both);
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);

        var source = ReadImageSource(xml, "Source");

        if (source is null)
        {
            Bind(image, Image.SourceProperty, nameof(ViewModels.LoadingViewModel.IconImage));
        }
        else if (Parse(xml, "IsAnimated", false))
        {
            XamlAnimatedGif.AnimationBehavior.SetSourceUri(image, source);
            XamlAnimatedGif.AnimationBehavior.SetRepeatBehavior(image, Parse(xml, "RepeatBehavior", RepeatBehavior.Forever));
        }
        else
        {
            image.Source = LoadBitmap(source, xml);
        }

        return image;
    }

    private Grid CreateGrid(XElement xml)
    {
        var grid = new Grid();
        ApplyFrameworkElement(grid, xml);

        foreach (var child in xml.Elements())
        {
            switch (child.Name.LocalName)
            {
                case "Grid.RowDefinitions":
                    foreach (var row in child.Elements())
                        grid.RowDefinitions.Add(Create<RowDefinition>(row));
                    break;

                case "Grid.ColumnDefinitions":
                    foreach (var column in child.Elements())
                        grid.ColumnDefinitions.Add(Create<ColumnDefinition>(column));
                    break;

                case var property when property.StartsWith("Grid.", StringComparison.Ordinal):
                    break;

                default:
                    grid.Children.Add(Create<FrameworkElement>(child));
                    break;
            }
        }

        return grid;
    }

    private StackPanel CreateStackPanel(XElement xml)
    {
        var panel = new StackPanel();
        ApplyFrameworkElement(panel, xml);
        panel.Orientation = ParseEnum(xml, "Orientation", Orientation.Vertical);

        foreach (var child in ElementChildren(xml))
            panel.Children.Add(Create<FrameworkElement>(child));

        return panel;
    }

    private Border CreateBorder(XElement xml)
    {
        var border = new Border();
        ApplyFrameworkElement(border, xml);

        ApplyBrush(border, Border.BackgroundProperty, xml, "Background");
        ApplyBrush(border, Border.BorderBrushProperty, xml, "BorderBrush");
        border.BorderThickness = Value<Thickness>(xml, "BorderThickness", new ThicknessConverter()) ?? border.BorderThickness;
        border.Padding = Value<Thickness>(xml, "Padding", new ThicknessConverter()) ?? border.Padding;
        border.CornerRadius = Value<CornerRadius>(xml, "CornerRadius", new CornerRadiusConverter()) ?? border.CornerRadius;

        var children = ElementChildren(xml).ToList();

        if (children.Count > 1)
            throw new XmlThemeException("A Border can only hold one element.");

        if (children.Count == 1)
            border.Child = Create<UIElement>(children[0]);

        return border;
    }

    // ---- shapes

    private Shape CreateShape(Shape shape, XElement xml)
    {
        ApplyFrameworkElement(shape, xml);

        ApplyBrush(shape, Shape.FillProperty, xml, "Fill");
        ApplyBrush(shape, Shape.StrokeProperty, xml, "Stroke");

        shape.Stretch = ParseEnum(xml, "Stretch", Stretch.Fill);
        shape.StrokeDashCap = ParseEnum(xml, "StrokeDashCap", PenLineCap.Flat);
        shape.StrokeDashOffset = Parse(xml, "StrokeDashOffset", 0.0);
        shape.StrokeEndLineCap = ParseEnum(xml, "StrokeEndLineCap", PenLineCap.Flat);
        shape.StrokeLineJoin = ParseEnum(xml, "StrokeLineJoin", PenLineJoin.Miter);
        shape.StrokeMiterLimit = Parse(xml, "StrokeMiterLimit", 10.0);
        shape.StrokeStartLineCap = ParseEnum(xml, "StrokeStartLineCap", PenLineCap.Flat);
        shape.StrokeThickness = Parse(xml, "StrokeThickness", 1.0);

        return shape;
    }

    private Line CreateLine(XElement xml)
    {
        var line = (Line)CreateShape(new Line(), xml);
        line.X1 = Parse(xml, "X1", 0.0);
        line.X2 = Parse(xml, "X2", 0.0);
        line.Y1 = Parse(xml, "Y1", 0.0);
        line.Y2 = Parse(xml, "Y2", 0.0);
        return line;
    }

    private Rectangle CreateRectangle(XElement xml)
    {
        var rectangle = (Rectangle)CreateShape(new Rectangle(), xml);
        rectangle.RadiusX = Parse(xml, "RadiusX", 0.0);
        rectangle.RadiusY = Parse(xml, "RadiusY", 0.0);
        return rectangle;
    }

    // ---- brushes

    private Brush CreateSolidColorBrush(XElement xml) => new SolidColorBrush(Value<Color>(xml, "Color", new ColorConverter()) ?? Colors.Transparent)
    {
        Opacity = Parse(xml, "Opacity", 1.0),
    };

    private Brush CreateImageBrush(XElement xml)
    {
        var brush = new ImageBrush
        {
            Opacity = Parse(xml, "Opacity", 1.0),
            AlignmentX = ParseEnum(xml, "AlignmentX", AlignmentX.Center),
            AlignmentY = ParseEnum(xml, "AlignmentY", AlignmentY.Center),
            Stretch = ParseEnum(xml, "Stretch", Stretch.Fill),
            TileMode = ParseEnum(xml, "TileMode", TileMode.None),
            ViewboxUnits = ParseEnum(xml, "ViewboxUnits", BrushMappingMode.RelativeToBoundingBox),
            ViewportUnits = ParseEnum(xml, "ViewportUnits", BrushMappingMode.RelativeToBoundingBox),
        };

        if (Value<Rect>(xml, "Viewbox", new RectConverter()) is { } viewbox)
            brush.Viewbox = viewbox;

        if (Value<Rect>(xml, "Viewport", new RectConverter()) is { } viewport)
            brush.Viewport = viewport;

        var source = ReadImageSource(xml, "ImageSource");

        if (source is null)
            Bind(brush, ImageBrush.ImageSourceProperty, nameof(ViewModels.LoadingViewModel.IconImage));
        else
            brush.ImageSource = LoadBitmap(source, xml);

        return brush;
    }

    private Brush CreateLinearGradientBrush(XElement xml)
    {
        var brush = new LinearGradientBrush
        {
            Opacity = Parse(xml, "Opacity", 1.0),
            ColorInterpolationMode = ParseEnum(xml, "ColorInterpolationMode", ColorInterpolationMode.SRgbLinearInterpolation),
            MappingMode = ParseEnum(xml, "MappingMode", BrushMappingMode.RelativeToBoundingBox),
            SpreadMethod = ParseEnum(xml, "SpreadMethod", GradientSpreadMethod.Pad),
        };

        if (Value<Point>(xml, "StartPoint", new PointConverter()) is { } start)
            brush.StartPoint = start;

        if (Value<Point>(xml, "EndPoint", new PointConverter()) is { } end)
            brush.EndPoint = end;

        foreach (var child in xml.Elements())
            brush.GradientStops.Add(Create<GradientStop>(child));

        return brush;
    }

    private static GradientStop CreateGradientStop(XElement xml) =>
        new(Value<Color>(xml, "Color", new ColorConverter()) ?? Colors.Transparent, Parse(xml, "Offset", 0.0));

    /// <summary>A brush from an attribute ("#FF0000", "Red", or "{ResourceName}") or a property element.</summary>
    private void ApplyBrush(DependencyObject target, DependencyProperty property, XElement xml, string name)
    {
        string? value = xml.Attribute(name)?.Value;

        if (value is not null)
        {
            if (value.StartsWith('{') && value.EndsWith('}') && target is FrameworkElement element)
            {
                element.SetResourceReference(property, value[1..^1]);
                return;
            }

            target.SetValue(property, Reference<Brush>(xml, name, new BrushConverter()));
            return;
        }

        if (xml.Element($"{xml.Name.LocalName}.{name}") is { } propertyElement)
        {
            var brushElement = propertyElement.Elements().FirstOrDefault()
                ?? throw new XmlThemeException($"{xml.Name.LocalName}.{name} needs a brush inside.");

            target.SetValue(property, Create<Brush>(brushElement));
        }
    }

    // ---- shared properties

    /// <param name="frameworkBasics">False for the window and title bar, which keep their own size and place.</param>
    private void ApplyControl(Control control, XElement xml, bool frameworkBasics = true)
    {
        if (frameworkBasics)
            ApplyFrameworkElement(control, xml);
        else
            ApplyLooks(control, xml);

        control.Padding = Value<Thickness>(xml, "Padding", new ThicknessConverter()) ?? control.Padding;
        control.BorderThickness = Value<Thickness>(xml, "BorderThickness", new ThicknessConverter()) ?? control.BorderThickness;

        ApplyBrush(control, ForegroundProperty, xml, "Foreground");
        ApplyBrush(control, BackgroundProperty, xml, "Background");
        ApplyBrush(control, BorderBrushProperty, xml, "BorderBrush");

        if (ParseNullable<double>(xml, "FontSize") is { } fontSize)
            control.FontSize = fontSize;

        control.FontWeight = Value<FontWeight>(xml, "FontWeight", new FontWeightConverter()) ?? FontWeights.Normal;
        control.FontStyle = Value<FontStyle>(xml, "FontStyle", new FontStyleConverter()) ?? FontStyles.Normal;

        if (ReadFontFamily(xml) is { } fontFamily)
            control.FontFamily = fontFamily;
    }

    private void ApplyTextBlock(TextBlock text, XElement xml)
    {
        ApplyFrameworkElement(text, xml);

        ApplyBrush(text, TextBlock.ForegroundProperty, xml, "Foreground");
        ApplyBrush(text, TextBlock.BackgroundProperty, xml, "Background");

        if (ParseNullable<double>(xml, "FontSize") is { } fontSize)
            text.FontSize = fontSize;

        text.FontWeight = Value<FontWeight>(xml, "FontWeight", new FontWeightConverter()) ?? FontWeights.Normal;
        text.FontStyle = Value<FontStyle>(xml, "FontStyle", new FontStyleConverter()) ?? FontStyles.Normal;
        text.LineHeight = Parse(xml, "LineHeight", double.NaN);
        text.LineStackingStrategy = ParseEnum(xml, "LineStackingStrategy", LineStackingStrategy.MaxHeight);
        text.TextAlignment = ParseEnum(xml, "TextAlignment", TextAlignment.Center);
        text.TextTrimming = ParseEnum(xml, "TextTrimming", TextTrimming.None);
        text.TextWrapping = ParseEnum(xml, "TextWrapping", TextWrapping.NoWrap);
        text.TextDecorations = Reference<TextDecorationCollection>(xml, "TextDecorations", new TextDecorationCollectionConverter());
        text.IsHyphenationEnabled = Parse(xml, "IsHyphenationEnabled", false);
        text.BaselineOffset = Parse(xml, "BaselineOffset", double.NaN);
        text.Padding = Value<Thickness>(xml, "Padding", new ThicknessConverter()) ?? text.Padding;

        if (ReadFontFamily(xml) is { } fontFamily)
            text.FontFamily = fontFamily;
    }

    private void ApplyFrameworkElement(FrameworkElement element, XElement xml)
    {
        if (NameOf(xml) is { } name)
        {
            if (!_usedNames.Add(name))
                throw new XmlThemeException($"Two elements are named {name}.");

            element.Name = name;
        }

        element.Visibility = ParseEnum(xml, "Visibility", Visibility.Visible);
        element.IsEnabled = Parse(xml, "IsEnabled", true);
        element.Height = Parse(xml, "Height", double.NaN);
        element.Width = Parse(xml, "Width", double.NaN);

        // Bloxstrap's defaults, not WPF's Stretch
        element.HorizontalAlignment = ParseEnum(xml, "HorizontalAlignment", HorizontalAlignment.Left);
        element.VerticalAlignment = ParseEnum(xml, "VerticalAlignment", VerticalAlignment.Top);

        int zIndex = Parse(xml, "Panel.ZIndex", 0);

        if (zIndex is < 0 or > 1000)
            throw new XmlThemeException($"{xml.Name.LocalName}.Panel.ZIndex must be between 0 and 1000.");

        Panel.SetZIndex(element, zIndex);
        Grid.SetRow(element, Parse(xml, "Grid.Row", 0));
        Grid.SetRowSpan(element, Parse(xml, "Grid.RowSpan", 1));
        Grid.SetColumn(element, Parse(xml, "Grid.Column", 0));
        Grid.SetColumnSpan(element, Parse(xml, "Grid.ColumnSpan", 1));

        ApplyLooks(element, xml);
    }

    /// <summary>Margin, opacity, transforms and effects: what every element and the window itself take.</summary>
    private void ApplyLooks(FrameworkElement element, XElement xml)
    {
        element.Margin = Value<Thickness>(xml, "Margin", new ThicknessConverter()) ?? element.Margin;
        element.Opacity = Parse(xml, "Opacity", 1.0);
        ApplyBrush(element, OpacityMaskProperty, xml, "OpacityMask");

        if (Value<Point>(xml, "RenderTransformOrigin", new PointConverter()) is { } origin)
            element.RenderTransformOrigin = origin;

        ApplyTransform(element, xml, "RenderTransform", RenderTransformProperty);
        ApplyTransform(element, xml, "LayoutTransform", LayoutTransformProperty);

        if (xml.Element($"{xml.Name.LocalName}.Effect") is { } effectElement)
        {
            var effects = effectElement.Elements().ToList();

            if (effects.Count > 1)
                throw new XmlThemeException($"{xml.Name.LocalName}.Effect can hold one effect.");

            if (effects.Count == 1)
                element.Effect = Create<Effect>(effects[0]);
        }
    }

    private void ApplyTransform(FrameworkElement element, XElement xml, string name, DependencyProperty property)
    {
        if (xml.Element($"{xml.Name.LocalName}.{name}") is not { } transformElement)
            return;

        var group = new TransformGroup();

        foreach (var child in transformElement.Elements())
            group.Children.Add(Create<Transform>(child));

        element.SetValue(property, group);
    }

    private object? ReadContent(XElement xml)
    {
        var attribute = xml.Attribute("Content");
        var element = xml.Element($"{xml.Name.LocalName}.Content");

        if (attribute is not null && element is not null)
            throw new XmlThemeException($"{xml.Name.LocalName} has Content twice.");

        if (attribute is not null)
            return Translate(attribute.Value);

        if (element is null)
            return null;

        var children = element.Elements().ToList();

        if (children.Count != 1)
            throw new XmlThemeException($"{xml.Name.LocalName}.Content needs exactly one element.");

        return Create<UIElement>(children[0]);
    }

    private FontFamily? ReadFontFamily(XElement xml) =>
        xml.Attribute("FontFamily")?.Value is { } family ? new FontFamily(ThemePath(family)) : null;

    /// <summary>
    /// A picture in the theme's folder; null means "{Icon}", the launch's icon. Only local files are
    /// allowed, as in Bloxstrap: a theme can't make Vizstrap fetch anything from the internet.
    /// </summary>
    private Uri? ReadImageSource(XElement xml, string name)
    {
        string value = xml.Attribute(name)?.Value
            ?? throw new XmlThemeException($"{xml.Name.LocalName} needs {name}.");

        if (value == "{Icon}")
            return null;

        string path = ThemePath(value);

        if (!System.IO.Path.IsPathRooted(path))
            path = System.IO.Path.Combine(_themeDirectory, path);

        if (!Uri.TryCreate(path, UriKind.Absolute, out var uri) || !uri.IsFile)
            throw new XmlThemeException($"{xml.Name.LocalName}.{name}: only pictures in the theme's folder can be used ({value}).");

        return uri;
    }

    private static BitmapImage LoadBitmap(Uri source, XElement xml)
    {
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = source;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or ArgumentException or UnauthorizedAccessException)
        {
            throw new XmlThemeException($"{xml.Name.LocalName}: the picture {source.LocalPath} can't be loaded ({ex.Message}).", ex);
        }
    }

    private string ThemePath(string value) => value.Replace("theme://", _themeDirectory + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static string? Translate(string? text)
    {
        if (text is null || text.Length < 2 || text[0] != '{' || text[^1] != '}')
            return text;

        string key = text[1..^1];
        return Texts.TryGetValue(key, out var translate) ? translate() : key;
    }

    private static string? NameOf(XElement xml) => xml.Attribute("Name")?.Value;

    private static IEnumerable<XElement> ElementChildren(XElement xml) =>
        xml.Elements().Where(child => !child.Name.LocalName.StartsWith(xml.Name.LocalName + ".", StringComparison.Ordinal));

    // ---- attribute parsing

    private static T Parse<T>(XElement xml, string name, T fallback) where T : struct =>
        ParseNullable<T>(xml, name) ?? fallback;

    private static T ParseEnum<T>(XElement xml, string name, T fallback) where T : struct, Enum => Parse(xml, name, fallback);

    private static T? ParseNullable<T>(XElement xml, string name) where T : struct
    {
        string? value = xml.Attribute(name)?.Value;

        if (value is null)
            return null;

        // "Auto" is how XAML says "no fixed size"
        if (typeof(T) == typeof(double) && value.Equals("Auto", StringComparison.OrdinalIgnoreCase))
            return (T)(object)double.NaN;

        return Value<T>(xml, name, TypeDescriptor.GetConverter(typeof(T)));
    }

    private static T? Value<T>(XElement xml, string name, TypeConverter converter) where T : struct =>
        (T?)ConvertAttribute(xml, name, typeof(T), converter);

    private static T? Reference<T>(XElement xml, string name, TypeConverter converter) where T : class =>
        (T?)ConvertAttribute(xml, name, typeof(T), converter);

    private static object? ConvertAttribute(XElement xml, string name, Type type, TypeConverter converter)
    {
        string? value = xml.Attribute(name)?.Value;

        if (value is null)
            return null;

        try
        {
            return converter.ConvertFromInvariantString(value);
        }
        catch (Exception ex) when (ex is FormatException or NotSupportedException or ArgumentException or InvalidOperationException)
        {
            throw new XmlThemeException($"{xml.Name.LocalName}.{name}: \"{value}\" isn't a valid {type.Name}.", ex);
        }
    }

    private sealed class PercentConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is double fraction ? fraction * 100 : 0.0;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }

    [GeneratedRegex(@"\*\*(?<bold>.+?)\*\*|\*(?<italic>.+?)\*|\[(?<label>[^\]]+)\]\((?<url>[^)]+)\)")]
    private static partial Regex MarkdownPattern();
}
