using Vizstrap.Core.Packages;

namespace Vizstrap.Views.Loading;

/// <summary>
/// Loading themes that come with switched-on mod packages, kept in the settings as
/// "package:packageId/themeFolder" (a colon can't be in a theme folder's name, so no clash).
/// </summary>
internal static class PackageThemes
{
    private const string Prefix = "package:";

    public static bool IsPackageTheme(string? id) => id?.StartsWith(Prefix, StringComparison.Ordinal) == true;

    public static string IdOf(InstalledPackage package, string themeDirectory) =>
        $"{Prefix}{package.Id}/{Path.GetFileName(themeDirectory)}";

    /// <summary>The theme's folder, or null when its package is gone or switched off.</summary>
    public static string? DirectoryOf(string? id)
    {
        if (!IsPackageTheme(id) || id![Prefix.Length..].Split('/', 2) is not [var packageId, var folder])
            return null;

        var package = App.EnabledPackages().FirstOrDefault(candidate => candidate.Id == packageId);
        return package?.LoadingThemes.FirstOrDefault(theme => Path.GetFileName(theme) == folder);
    }
}
