using System.Security.Cryptography;
using Vizstrap.Core.Logging;

namespace Vizstrap.Core.Mods;

public enum CursorStyle
{
    Default,
    /// <summary>The cartoony cursor used from 2006.</summary>
    From2006,
    /// <summary>The angular cursor used from 2013.</summary>
    From2013,
    /// <summary>A picture of the user's own.</summary>
    Custom,
}

public enum DeathSound
{
    /// <summary>Roblox's own: the classic "oof", default again since July 2025.</summary>
    Default,
    Muted,
    /// <summary>A sound file of the user's own.</summary>
    Custom,
}

public enum EmojiStyle
{
    /// <summary>Roblox's own Twemoji.</summary>
    Default,
    Catmoji,
    Windows11,
    Windows10,
    Windows8,

    /// <summary>OpenMoji: open emoji, flat with outlines.</summary>
    OpenMoji,

    /// <summary>Emojitwo: round and shiny, EmojiOne 2's style; no emoji newer than Unicode 12.</summary>
    EmojiTwo,
}

/// <summary>Raised when an emoji font could not be downloaded intact.</summary>
public sealed class EmojiDownloadException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Built-in mods, as in Bloxstrap. Each one is just files in the Modifications folder, so the folder stays
/// the single source of truth: a preset counts as on when its files are there with the expected content,
/// and turning it off only removes files that are still exactly the preset's.
/// </summary>
public sealed class ModPresets
{
    private const string LogSource = nameof(ModPresets);

    private const string EmojiTarget = @"content\fonts\TwemojiMozilla.ttf";

    /// <summary>The death sound; Roblox also plays it for the old "uuhhh" names.</summary>
    private const string DeathSoundTarget = @"content\sounds\oof.ogg";

    private const string SilentSound = @"Sounds\Empty.mp3";

    private static readonly string[] CursorTargets =
    [
        @"content\textures\Cursors\KeyboardMouse\ArrowCursor.png",
        @"content\textures\Cursors\KeyboardMouse\ArrowFarCursor.png",
    ];

    private const string BloxstrapEmojiUrl =
        "https://github.com/bloxstraplabs/rbxcustom-fontemojis/releases/download/my-phone-is-78-percent/";

    private static readonly (string Target, string Resource)[] Cursor2006 =
    [
        (@"content\textures\Cursors\KeyboardMouse\ArrowCursor.png", @"Cursor\From2006\ArrowCursor.png"),
        (@"content\textures\Cursors\KeyboardMouse\ArrowFarCursor.png", @"Cursor\From2006\ArrowFarCursor.png"),
    ];

    private static readonly (string Target, string Resource)[] Cursor2013 =
    [
        (@"content\textures\Cursors\KeyboardMouse\ArrowCursor.png", @"Cursor\From2013\ArrowCursor.png"),
        (@"content\textures\Cursors\KeyboardMouse\ArrowFarCursor.png", @"Cursor\From2013\ArrowFarCursor.png"),
    ];

    private static readonly (string Target, string Resource)[] OldAvatarBackgroundFiles =
    [
        (@"ExtraContent\places\Mobile.rbxl", "OldAvatarBackground.rbxl"),
    ];

    private static readonly (string Target, string Resource)[] OldCharacterSoundFiles =
    [
        (@"content\sounds\action_footsteps_plastic.mp3", @"Sounds\OldWalk.mp3"),
        (@"content\sounds\action_jump.mp3", @"Sounds\OldJump.mp3"),
        (@"content\sounds\action_get_up.mp3", @"Sounds\OldGetUp.mp3"),
        (@"content\sounds\action_falling.mp3", @"Sounds\Empty.mp3"),
        // current Roblox plays the falling sound from .ogg (Bloxstrap only covers the old .mp3); a silent
        // MP3 under that name stays silent whether the engine sniffs the format or rejects the file
        (@"content\sounds\action_falling.ogg", @"Sounds\Empty.mp3"),
        (@"content\sounds\action_jump_land.mp3", @"Sounds\Empty.mp3"),
        (@"content\sounds\action_swim.mp3", @"Sounds\Empty.mp3"),
        (@"content\sounds\impact_water.mp3", @"Sounds\Empty.mp3"),
    ];

    /// <summary>
    /// The emoji fonts, where they come from (fixed versions) and their MD5 so a download can be verified.
    /// All are COLR v0: Roblox draws only that (and SVG) in colour, COLRv1, CBDT and sbix come out black.
    /// </summary>
    internal static IReadOnlyDictionary<EmojiStyle, (string Url, string Md5)> EmojiFonts { get; } = new Dictionary<EmojiStyle, (string, string)>
    {
        // the ones Bloxstrap publishes
        [EmojiStyle.Catmoji] = (BloxstrapEmojiUrl + "Catmoji.ttf", "98138f398a8cde897074dd2b8d53eca0"),
        [EmojiStyle.Windows11] = (BloxstrapEmojiUrl + "Win1122H2SegoeUIEmoji.ttf", "d50758427673578ddf6c9edcdbf367f5"),
        [EmojiStyle.Windows10] = (BloxstrapEmojiUrl + "Win10April2018SegoeUIEmoji.ttf", "d8a7eecbebf9dfdf622db8ccda63aff5"),
        [EmojiStyle.Windows8] = (BloxstrapEmojiUrl + "Win8.1SegoeUIEmoji.ttf", "2b01c6caabbe95afc92aa63b9bf100f3"),

        // OpenMoji 17.0 (CC BY-SA 4.0), the COLR v0 build from its repository at the release tag
        [EmojiStyle.OpenMoji] = ("https://raw.githubusercontent.com/hfg-gmuend/openmoji/17.0.0/font/OpenMoji-color-glyf_colr_0/OpenMoji-color-glyf_colr_0.ttf",
            "ecdcbd2cfa35a43e2e102269e7006bb4"),

        // Emojitwo (CC BY 4.0) as COLR v0, built by the Emoji-COLRv0 project
        [EmojiStyle.EmojiTwo] = ("https://github.com/Emoji-COLRv0/Emoji-COLRv0/releases/download/vNov22/EmojiTwoCOLRv0.ttf",
            "efdc475cbb9a9bdcb8b7c2075bce2eeb"),
    };

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static readonly byte[][] FontSignatures =
    [
        [0x00, 0x01, 0x00, 0x00], // TrueType
        "OTTO"u8.ToArray(),       // OpenType (CFF)
        "ttcf"u8.ToArray(),       // TrueType collection
        "true"u8.ToArray(),       // old Apple TrueType
    ];

    private readonly string _mods;
    private readonly string _customFont;

    public ModPresets(VizstrapPaths paths)
    {
        _mods = paths.Modifications;
        _customFont = paths.CustomFont;
    }

    public static string EmojiUrl(EmojiStyle style) => EmojiFonts[style].Url;

    // ---- cursor

    /// <summary>Custom when the cursor files are there but aren't one of the presets.</summary>
    public CursorStyle Cursor =>
        AllMatch(Cursor2006) ? CursorStyle.From2006 :
        AllMatch(Cursor2013) ? CursorStyle.From2013 :
        CursorTargets.All(target => File.Exists(Path.Combine(_mods, target))) ? CursorStyle.Custom :
        CursorStyle.Default;

    /// <param name="customImage">A PNG for <see cref="CursorStyle.Custom"/>; null keeps the custom cursor already there.</param>
    public void SetCursor(CursorStyle style, string? customImage = null)
    {
        if (style == CursorStyle.Custom && customImage is null)
            return;

        // choosing anything else replaces a custom cursor too
        if (Cursor == CursorStyle.Custom)
        {
            foreach (string target in CursorTargets)
                Delete(Path.Combine(_mods, target));
        }

        RemoveMatching(Cursor2006);
        RemoveMatching(Cursor2013);

        if (style == CursorStyle.From2006)
            Write(Cursor2006);
        else if (style == CursorStyle.From2013)
            Write(Cursor2013);
        else if (style == CursorStyle.Custom)
            CopyIn(customImage!, IsPngFile, "PNG image", CursorTargets);
    }

    public static bool IsPngFile(string path) => HasSignature(path, PngSignature);

    // ---- death sound

    public DeathSound DeathSound
    {
        get
        {
            string file = Path.Combine(_mods, DeathSoundTarget);

            return !File.Exists(file) ? DeathSound.Default :
                Matches(file, SilentSound) ? DeathSound.Muted :
                DeathSound.Custom;
        }
    }

    /// <param name="customSound">An OGG, MP3 or WAV file for <see cref="DeathSound.Custom"/>; null keeps the one already there.</param>
    public void SetDeathSound(DeathSound sound, string? customSound = null)
    {
        switch (sound)
        {
            case DeathSound.Default:
                Delete(Path.Combine(_mods, DeathSoundTarget));
                break;

            case DeathSound.Muted:
                Write([(DeathSoundTarget, SilentSound)]);
                break;

            case DeathSound.Custom when customSound is not null:
                CopyIn(customSound, IsSoundFile, "OGG, MP3 or WAV sound", [DeathSoundTarget]);
                break;
        }
    }

    /// <summary>Roblox's audio engine goes by a file's content, not its name, so any of these works as "oof.ogg".</summary>
    public static bool IsSoundFile(string path)
    {
        if (!File.Exists(path))
            return false;

        var header = new byte[12];
        int length;

        using (var stream = File.OpenRead(path))
            length = stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false);

        var start = header.AsSpan(0, length);

        return start.StartsWith("OggS"u8) ||
            start.StartsWith("ID3"u8) ||
            (length >= 2 && start[0] == 0xFF && (start[1] & 0xE0) == 0xE0) || // an MPEG audio frame
            (length >= 12 && start.StartsWith("RIFF"u8) && start[8..12].SequenceEqual("WAVE"u8));
    }

    // ---- simple on/off presets

    public bool OldAvatarBackground => AllMatch(OldAvatarBackgroundFiles);

    public void SetOldAvatarBackground(bool enabled) => Toggle(OldAvatarBackgroundFiles, enabled);

    public bool OldCharacterSounds => AllMatch(OldCharacterSoundFiles);

    public void SetOldCharacterSounds(bool enabled) => Toggle(OldCharacterSoundFiles, enabled);

    // ---- emoji

    public EmojiStyle Emoji
    {
        get
        {
            string file = Path.Combine(_mods, EmojiTarget);

            if (!File.Exists(file))
                return EmojiStyle.Default;

            string md5 = Md5(file);
            return EmojiFonts.FirstOrDefault(pair => pair.Value.Md5 == md5).Key;
        }
    }

    /// <summary>Downloads the emoji font (verified by MD5) or, for Default, removes the one Vizstrap put there.</summary>
    public async Task SetEmojiAsync(EmojiStyle style, HttpClient http, CancellationToken cancellationToken = default)
    {
        string file = Path.Combine(_mods, EmojiTarget);

        if (style == EmojiStyle.Default)
        {
            if (Emoji != EmojiStyle.Default)
                Delete(file);

            return;
        }

        if (Emoji == style)
            return;

        byte[] font;

        try
        {
            font = await http.GetByteArrayAsync(EmojiUrl(style), cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            throw new EmojiDownloadException($"Could not download the {style} emoji font.", ex);
        }

        if (Convert.ToHexStringLower(MD5.HashData(font)) != EmojiFonts[style].Md5)
            throw new EmojiDownloadException($"The downloaded {style} emoji font is not the expected file.");

        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        ModApplier.ClearReadOnly(file);
        await File.WriteAllBytesAsync(file, font, cancellationToken);
        Log.Info(LogSource, $"Emoji font set to {style}");
    }

    // ---- Vizstrap's colours in Roblox

    /// <summary>The tEXt keyword in the pictures Vizstrap draws for Roblox; its value is the accent.</summary>
    public const string ThemeMarker = "Vizstrap theme";

    /// <summary>Roblox's loading spinners and logos that the theme redraws (see RobloxThemeRenderer).</summary>
    public static IReadOnlyList<string> RobloxThemeTargets { get; } =
    [
        @"content\textures\loading\loadingCircle.png",
        @"content\textures\ui\LoadingScreen\LoadingSpinner.png",
        @"content\textures\DarkThemeLoadingCircle.png",
        @"content\textures\LightThemeLoadingCircle.png",
        @"content\textures\loading\robloxlogo.png",
        @"content\textures\ui\TopBar\coloredlogo.png",
        @"content\textures\ui\TopBar\coloredlogo@2x.png",
        @"content\textures\ui\TopBar\coloredlogo@3x.png",
        @"ExtraContent\textures\ui\InGameMenu\roblox_logo.png",
    ];

    /// <summary>The accent the theme pictures were drawn in, or null unless all of them are Vizstrap's.</summary>
    public string? RobloxThemeAccent
    {
        get
        {
            var accents = RobloxThemeTargets
                .Select(target => PngText.ReadFile(Path.Combine(_mods, target), ThemeMarker))
                .Distinct()
                .ToList();

            return accents is [{ } accent] ? accent : null;
        }
    }

    /// <summary>
    /// Writes the drawn pictures (relative path → PNG carrying <see cref="ThemeMarker"/>), or for null
    /// removes the ones Vizstrap drew, leaving any file the user put there themselves.
    /// </summary>
    public void SetRobloxTheme(IReadOnlyDictionary<string, byte[]>? pictures)
    {
        foreach (string target in RobloxThemeTargets)
        {
            string path = Path.Combine(_mods, target);

            if (pictures is null)
            {
                if (PngText.ReadFile(path, ThemeMarker) is not null)
                    Delete(path);

                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            ModApplier.ClearReadOnly(path);
            File.WriteAllBytes(path, pictures[target]);
        }
    }

    // ---- custom font

    public bool HasCustomFont => File.Exists(_customFont);

    /// <summary>Copies the font in as CustomFont.ttf, or removes it for null; <see cref="ModApplier"/> does the rest.</summary>
    public void SetCustomFont(string? fontFile)
    {
        if (fontFile is null)
        {
            Delete(_customFont);
            return;
        }

        if (!IsFontFile(fontFile))
            throw new InvalidDataException($"{fontFile} is not a TrueType or OpenType font.");

        Directory.CreateDirectory(Path.GetDirectoryName(_customFont)!);
        ModApplier.ClearReadOnly(_customFont);
        File.Copy(fontFile, _customFont, overwrite: true);
    }

    public static bool IsFontFile(string path) => FontSignatures.Any(signature => HasSignature(path, signature));

    private static bool HasSignature(string path, byte[] signature)
    {
        if (!File.Exists(path))
            return false;

        var header = new byte[signature.Length];

        using (var stream = File.OpenRead(path))
        {
            if (stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) != header.Length)
                return false;
        }

        return header.AsSpan().SequenceEqual(signature);
    }

    /// <summary>Copies a user's file to every target, after checking it's the right kind of file.</summary>
    private void CopyIn(string source, Func<string, bool> isValid, string kind, string[] targets)
    {
        if (!isValid(source))
            throw new InvalidDataException($"{source} is not a {kind}.");

        foreach (string target in targets)
        {
            string path = Path.Combine(_mods, target);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            ModApplier.ClearReadOnly(path);
            File.Copy(source, path, overwrite: true);
        }
    }

    // ---- helpers

    private bool AllMatch((string Target, string Resource)[] files) =>
        files.All(file => Matches(Path.Combine(_mods, file.Target), file.Resource));

    private void Toggle((string Target, string Resource)[] files, bool enabled)
    {
        if (enabled)
            Write(files);
        else
            RemoveMatching(files);
    }

    private void Write((string Target, string Resource)[] files)
    {
        foreach (var (target, resource) in files)
        {
            string path = Path.Combine(_mods, target);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            ModApplier.ClearReadOnly(path);

            using var source = OpenResource(resource);
            using var destination = File.Create(path);
            source.CopyTo(destination);
        }
    }

    /// <summary>Removes preset files, leaving alone any file the user replaced with their own.</summary>
    private void RemoveMatching((string Target, string Resource)[] files)
    {
        foreach (var (target, resource) in files)
        {
            string path = Path.Combine(_mods, target);

            if (Matches(path, resource))
                Delete(path);
        }
    }

    private static bool Matches(string path, string resource)
    {
        if (!File.Exists(path))
            return false;

        using var stream = OpenResource(resource);
        return Md5(path) == Convert.ToHexStringLower(MD5.HashData(stream));
    }

    private static Stream OpenResource(string name) =>
        typeof(ModPresets).Assembly.GetManifestResourceStream(name)
        ?? throw new InvalidOperationException($"Missing embedded preset file {name}.");

    private static string Md5(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(MD5.HashData(stream));
    }

    private static void Delete(string path)
    {
        ModApplier.ClearReadOnly(path);

        if (File.Exists(path))
            File.Delete(path);
    }
}
