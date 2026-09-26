using System.Text.RegularExpressions;

namespace Vizstrap.Core.Appearance;

public enum ThemeLayout
{
    /// <summary>Icon, texts and bar in the middle.</summary>
    Centered,
    /// <summary>Everything in a band along the bottom, leaving the picture free.</summary>
    Bottom,
    /// <summary>Big icon on the left, texts and bar on the right.</summary>
    Side,
    /// <summary>Just a thin bar and a small status line at the bottom.</summary>
    Minimal,
}

public enum ThemeSize
{
    Small,
    Medium,
    Large,
}

/// <summary>The loading window put together in Vizstrap's own editor (the "Custom" style).</summary>
public sealed partial class CustomLoadingTheme
{
    public const string DefaultBackground = "#15122A";
    public const string DefaultText = "#FFFFFF";

    /// <summary>"#RRGGBB" or "#AARRGGBB".</summary>
    public string BackgroundColor { get; set; } = DefaultBackground;

    /// <summary>A picture copied into Vizstrap's folder; drawn over the background colour.</summary>
    public string? BackgroundImage { get; set; }

    /// <summary>Crop the picture to fill the window (true) or show all of it (false).</summary>
    public bool FillImage { get; set; } = true;

    /// <summary>How much the picture is darkened (0–80 %), so text stays readable on it.</summary>
    public int Dim { get; set; } = 35;

    public string TextColor { get; set; } = DefaultText;

    /// <summary>The progress bar's colour; null uses the accent.</summary>
    public string? BarColor { get; set; }

    public ThemeLayout Layout { get; set; } = ThemeLayout.Centered;

    public ThemeSize Size { get; set; } = ThemeSize.Medium;

    public bool ShowIcon { get; set; } = true;

    public const int MaxDim = 80;

    public static (int Width, int Height) Dimensions(ThemeSize size) => size switch
    {
        ThemeSize.Small => (380, 230),
        ThemeSize.Large => (720, 405),
        _ => (520, 300),
    };

    public static bool IsColor(string? text) => text is not null && HexColor().IsMatch(text);

    public CustomLoadingTheme Clone() => (CustomLoadingTheme)MemberwiseClone();

    public bool SameAs(CustomLoadingTheme other) =>
        BackgroundColor == other.BackgroundColor && BackgroundImage == other.BackgroundImage && FillImage == other.FillImage &&
        Dim == other.Dim && TextColor == other.TextColor && BarColor == other.BarColor && Layout == other.Layout &&
        Size == other.Size && ShowIcon == other.ShowIcon;

    [GeneratedRegex("^#([0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})$")]
    private static partial Regex HexColor();
}
