using System.Globalization;

namespace Vizstrap.Core;

public static class ByteSize
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB"];

    /// <summary>Formats a byte count with one decimal, e.g. "12.4 MB" (decimal separator follows the culture).</summary>
    public static string Format(double bytes, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;

        int unit = 0;

        while (Math.Abs(bytes) >= 1024 && unit < Units.Length - 1)
        {
            bytes /= 1024;
            unit++;
        }

        string number = unit == 0 ? bytes.ToString("0", culture) : bytes.ToString("0.0", culture);
        return $"{number} {Units[unit]}";
    }
}
