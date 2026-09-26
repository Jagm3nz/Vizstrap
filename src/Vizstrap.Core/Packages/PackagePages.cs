using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Vizstrap.Core.Packages;

/// <summary>One element of a package's tab; <paramref name="VisibleWhen"/> names a toggle ("id" or "!id") that shows it.</summary>
public abstract record PageElement(string? VisibleWhen);

public sealed record SectionElement(string Title, string? VisibleWhen) : PageElement(VisibleWhen);

public sealed record TextElement(string Text, bool Muted, string? VisibleWhen) : PageElement(VisibleWhen);

public sealed record LinkElement(string Title, string Url, string? Description, string? VisibleWhen) : PageElement(VisibleWhen);

/// <summary>An element the player sets; its value is kept per package under <see cref="Id"/>.</summary>
public abstract record InputElement(string Id, string Title, string? Description, string? VisibleWhen) : PageElement(VisibleWhen)
{
    /// <summary>The value before the player changes it, as it's stored ("true", "0.5", a choice's value, text).</summary>
    public abstract string DefaultValue { get; }
}

public sealed record ToggleElement(string Id, string Title, string? Description, bool Default, string? VisibleWhen)
    : InputElement(Id, Title, Description, VisibleWhen)
{
    public override string DefaultValue => Default ? "true" : "false";
}

public sealed record SliderElement(string Id, string Title, string? Description, double Min, double Max, double Step, double Default, string? VisibleWhen)
    : InputElement(Id, Title, Description, VisibleWhen)
{
    public override string DefaultValue => Default.ToString(CultureInfo.InvariantCulture);
}

public sealed record ChoiceOption(string Value, string Title);

public sealed record ChoiceElement(string Id, string Title, string? Description, IReadOnlyList<ChoiceOption> Options, string Default, string? VisibleWhen)
    : InputElement(Id, Title, Description, VisibleWhen)
{
    public override string DefaultValue => Default;
}

public sealed record TextBoxElement(string Id, string Title, string? Description, string Default, int MaxLength, string? VisibleWhen)
    : InputElement(Id, Title, Description, VisibleWhen)
{
    public override string DefaultValue => Default;
}

/// <summary>A ready-made part of Vizstrap shown on the tab, like the activity view's parts.</summary>
public sealed record WidgetElement(string Name, string? VisibleWhen) : PageElement(VisibleWhen);

/// <summary>A tab a package adds to the settings: pages\name.xml.</summary>
public sealed record PackagePage(string PackageId, string PackageName, string FileName, string Title, string? Icon, string? Description,
    IReadOnlyList<PageElement> Elements)
{
    public IEnumerable<InputElement> Inputs => Elements.OfType<InputElement>();
}

/// <summary>
/// Reads a package's tabs. The format (in the package guide and docs\making-a-package.md):
/// <c>&lt;VizstrapPage Title="…" Icon="Timer24"&gt;</c> with Section, Text, Link, Toggle, Slider, Choice, TextBox and the
/// widgets; any text attribute can have translations like <c>Title.pl="…"</c>.
/// </summary>
public static partial class PackagePages
{
    public const string RootName = "VizstrapPage";

    /// <summary>At most this many tabs come from all switched-on packages together.</summary>
    public const int MaxPages = 12;

    public const int MaxElements = 200;

    /// <summary>The widgets a tab can show.</summary>
    public static IReadOnlyList<string> Widgets { get; } =
        ["PlaytimePeriod", "PlaytimeSummary", "PlaytimeChart", "PlaytimeGames", "PlaytimeClear"];

    /// <param name="language">The player's language ("pl", "pt-BR"…) for the translated attributes.</param>
    /// <exception cref="InvalidDataException">The XML isn't a valid tab; the message says where and why.</exception>
    public static PackagePage Parse(string packageId, string packageName, string fileName, string xml, string language)
    {
        XElement root;

        try
        {
            root = XElement.Parse(xml, LoadOptions.SetLineInfo);
        }
        catch (XmlException ex)
        {
            throw new InvalidDataException($"Not valid XML: {ex.Message}");
        }

        if (root.Name.LocalName != RootName)
            throw new InvalidDataException($"The root element must be <{RootName}>, not <{root.Name.LocalName}>.");

        var reader = new Reader(language);
        string title = reader.Text(root, "Title") ?? Path.GetFileNameWithoutExtension(fileName);
        var elements = new List<PageElement>();
        var ids = new HashSet<string>(StringComparer.Ordinal);

        foreach (var element in root.Elements())
        {
            if (elements.Count == MaxElements)
                throw new InvalidDataException($"A tab can have at most {MaxElements} elements.");

            var read = reader.Element(element);

            if (read is InputElement input && !ids.Add(input.Id))
                throw Error(element, $"the id \"{input.Id}\" is used twice");

            elements.Add(read);
        }

        // VisibleWhen has to name a toggle of the same tab
        var toggles = elements.OfType<ToggleElement>().Select(toggle => toggle.Id).ToHashSet(StringComparer.Ordinal);

        foreach (var element in elements.Where(element => element.VisibleWhen is not null))
        {
            if (!toggles.Contains(element.VisibleWhen!.TrimStart('!')))
                throw new InvalidDataException($"VisibleWhen=\"{element.VisibleWhen}\" doesn't name a Toggle on this tab.");
        }

        return new PackagePage(packageId, packageName, fileName, title, reader.Plain(root, "Icon"), reader.Text(root, "Description"), elements);
    }

    /// <summary>
    /// A package's settings as its program gets them: every input of its tabs with the player's value or its default,
    /// typed (toggles true/false, sliders numbers, the rest text).
    /// </summary>
    public static Dictionary<string, object> Values(IEnumerable<PackagePage> pages, IReadOnlyDictionary<string, string>? saved)
    {
        var values = new Dictionary<string, object>(StringComparer.Ordinal);

        foreach (var input in pages.SelectMany(page => page.Inputs))
        {
            string value = saved is not null && saved.TryGetValue(input.Id, out var stored) ? stored : input.DefaultValue;

            values[input.Id] = input switch
            {
                ToggleElement => value == "true",
                SliderElement slider => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number)
                    ? Math.Clamp(number, Math.Min(slider.Min, slider.Max), Math.Max(slider.Min, slider.Max))
                    : slider.Default,
                _ => value,
            };
        }

        return values;
    }

    private static InvalidDataException Error(XElement element, string problem) =>
        new(((IXmlLineInfo)element).HasLineInfo()
            ? $"Line {((IXmlLineInfo)element).LineNumber}, <{element.Name.LocalName}>: {problem}."
            : $"<{element.Name.LocalName}>: {problem}.");

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_.-]{0,63}$")]
    private static partial Regex IdPattern();

    private sealed class Reader(string language)
    {
        public PageElement Element(XElement element)
        {
            string? visibleWhen = Plain(element, "VisibleWhen");

            return element.Name.LocalName switch
            {
                "Section" => new SectionElement(Required(element, "Title"), visibleWhen),
                "Text" => new TextElement(Text(element, "Text") ?? Content(element), Plain(element, "Muted") == "true", visibleWhen),
                "Link" => Link(element, visibleWhen),
                "Toggle" => new ToggleElement(Id(element), Required(element, "Title"), Text(element, "Description"),
                    Plain(element, "Default") == "true", visibleWhen),
                "Slider" => Slider(element, visibleWhen),
                "Choice" => Choice(element, visibleWhen),
                "TextBox" => new TextBoxElement(Id(element), Required(element, "Title"), Text(element, "Description"),
                    Text(element, "Default") ?? "", (int)Number(element, "MaxLength", 200), visibleWhen),
                var name when Widgets.Contains(name) => new WidgetElement(name, visibleWhen),
                var name => throw Error(element, $"unknown element; use Section, Text, Link, Toggle, Slider, Choice, TextBox or {string.Join(", ", Widgets)}"),
            };
        }

        /// <summary>A text attribute in the player's language ("Title.pl", "Title.pt-BR", "Title.pt"), else as written.</summary>
        public string? Text(XElement element, string name)
        {
            string? translated = element.Attribute($"{name}.{language}")?.Value
                ?? (language.Contains('-') ? element.Attribute($"{name}.{language.Split('-')[0]}")?.Value : null);

            return (translated ?? element.Attribute(name)?.Value)?.Trim() is { Length: > 0 } text ? text : null;
        }

        public string? Plain(XElement element, string name) => element.Attribute(name)?.Value.Trim() is { Length: > 0 } text ? text : null;

        private string Required(XElement element, string name) => Text(element, name) ?? throw Error(element, $"{name} is missing");

        private string Content(XElement element) =>
            element.Value.Trim() is { Length: > 0 } text ? text : throw Error(element, "the text is missing");

        private string Id(XElement element) =>
            Plain(element, "Id") is { } id && IdPattern().IsMatch(id)
                ? id
                : throw Error(element, "Id must start with a letter and use letters, digits, '_', '.', '-' (up to 64)");

        private double Number(XElement element, string name, double fallback)
        {
            if (Plain(element, name) is not { } text)
                return fallback;

            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) && double.IsFinite(number)
                ? number
                : throw Error(element, $"{name} must be a number like 0.5");
        }

        private LinkElement Link(XElement element, string? visibleWhen)
        {
            string url = Plain(element, "Url") ?? throw Error(element, "Url is missing");

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
                throw Error(element, "Url must be an https:// address");

            return new LinkElement(Required(element, "Title"), uri.AbsoluteUri, Text(element, "Description"), visibleWhen);
        }

        private SliderElement Slider(XElement element, string? visibleWhen)
        {
            double min = Number(element, "Min", 0), max = Number(element, "Max", 1);

            if (max <= min)
                throw Error(element, "Max must be more than Min");

            double step = Number(element, "Step", (max - min) / 100);
            double value = Math.Clamp(Number(element, "Default", min), min, max);

            if (step <= 0)
                throw Error(element, "Step must be more than 0");

            return new SliderElement(Id(element), Required(element, "Title"), Text(element, "Description"), min, max, step, value, visibleWhen);
        }

        private ChoiceElement Choice(XElement element, string? visibleWhen)
        {
            var options = element.Elements()
                .Select(option => option.Name.LocalName == "Option"
                    ? new ChoiceOption(Plain(option, "Value") ?? throw Error(option, "Value is missing"), Text(option, "Title") ?? Plain(option, "Value")!)
                    : throw Error(option, "only <Option> goes inside a Choice"))
                .ToList();

            if (options.Count == 0)
                throw Error(element, "add at least one <Option Value=\"…\" Title=\"…\" />");

            string value = Plain(element, "Default") ?? options[0].Value;

            if (options.All(option => option.Value != value))
                throw Error(element, $"Default \"{value}\" isn't one of its options");

            return new ChoiceElement(Id(element), Required(element, "Title"), Text(element, "Description"), options, value, visibleWhen);
        }
    }
}
