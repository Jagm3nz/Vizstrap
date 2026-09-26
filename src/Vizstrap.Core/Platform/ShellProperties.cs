using System.Globalization;
using System.Runtime.InteropServices;

namespace Vizstrap.Core.Platform;

public static class ShellProperties
{
    private const uint ShopFilePath = 0x2;

    /// <summary>
    /// Windows looks the tab up by its translated caption, so "Compatibility" alone only works on English
    /// Windows (Bloxstrap's version always lands on "General" elsewhere). Unknown languages fall back to it.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> CompatibilityTabNames = new Dictionary<string, string>
    {
        ["en"] = "Compatibility",
        ["pl"] = "Zgodność",
        ["de"] = "Kompatibilität",
        ["fr"] = "Compatibilité",
        ["es"] = "Compatibilidad",
        ["pt"] = "Compatibilidade",
        ["it"] = "Compatibilità",
        ["ru"] = "Совместимость",
        ["uk"] = "Сумісність",
        ["cs"] = "Kompatibilita",
        ["nl"] = "Compatibiliteit",
        ["tr"] = "Uyumluluk",
        ["zh"] = "兼容性",
        ["ja"] = "互換性",
        ["ko"] = "호환성",
    };

    public static string CompatibilityTabName(CultureInfo windowsLanguage) =>
        CompatibilityTabNames.GetValueOrDefault(windowsLanguage.TwoLetterISOLanguageName, "Compatibility");

    /// <summary>Opens Explorer's Properties dialog for a file, on the given tab when it exists.</summary>
    public static bool Show(string filePath, string? tab = null) => SHObjectProperties(IntPtr.Zero, ShopFilePath, filePath, tab);

    /// <summary>The Properties dialog on its Compatibility tab (DPI scaling, fullscreen optimisations…).</summary>
    public static bool ShowCompatibility(string filePath, CultureInfo windowsLanguage) =>
        Show(filePath, CompatibilityTabName(windowsLanguage));

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool SHObjectProperties(IntPtr hwnd, uint shopObjectType, string objectName, string? propertyPage);
}
