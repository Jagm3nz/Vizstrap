namespace Vizstrap.Core.Roblox;

/// <summary>
/// Where each Roblox Player package is extracted, relative to the version folder.
/// The official bootstrapper hardcodes this; the table comes from Bloxstrap (MIT, © pizzaboxer).
/// A new Roblox package needs a new entry here, otherwise it is skipped with a warning.
/// </summary>
public static class PackageMap
{
    public static IReadOnlyDictionary<string, string> Player { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["RobloxApp.zip"] = @"",
        ["Libraries.zip"] = @"",
        ["redist.zip"] = @"",
        ["shaders.zip"] = @"shaders\",
        ["ssl.zip"] = @"ssl\",
        ["WebView2.zip"] = @"",

        ["content-avatar.zip"] = @"content\avatar\",
        ["content-configs.zip"] = @"content\configs\",
        ["content-fonts.zip"] = @"content\fonts\",
        ["content-sky.zip"] = @"content\sky\",
        ["content-sounds.zip"] = @"content\sounds\",
        ["content-textures2.zip"] = @"content\textures\",
        ["content-models.zip"] = @"content\models\",

        ["content-textures3.zip"] = @"PlatformContent\pc\textures\",
        ["content-terrain.zip"] = @"PlatformContent\pc\terrain\",
        ["content-platform-fonts.zip"] = @"PlatformContent\pc\fonts\",
        ["content-platform-dictionaries.zip"] = @"PlatformContent\pc\shared_compression_dictionaries\",

        ["extracontent-luapackages.zip"] = @"ExtraContent\LuaPackages\",
        ["extracontent-translations.zip"] = @"ExtraContent\translations\",
        ["extracontent-models.zip"] = @"ExtraContent\models\",
        ["extracontent-textures.zip"] = @"ExtraContent\textures\",
        ["extracontent-places.zip"] = @"ExtraContent\places\",
    };

    /// <summary>Packages in the manifest that Vizstrap never downloads.</summary>
    public static IReadOnlySet<string> Ignored { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        // only needed when WebView2 is missing; out of scope for now
        "WebView2RuntimeInstaller.zip",
    };
}
