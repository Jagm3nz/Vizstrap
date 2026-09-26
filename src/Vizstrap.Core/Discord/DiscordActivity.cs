using System.Text.Json;

namespace Vizstrap.Core.Discord;

public sealed record DiscordButton(string Label, string Url);

/// <summary>
/// What Discord shows on the profile ("Playing Roblox" and the lines under it). Images are asset keys
/// of the Discord application or https URLs. <see cref="WriteJson"/> applies Discord's limits.
/// </summary>
public sealed record DiscordActivity
{
    public const int MaxTextLength = 128;
    public const int MaxButtons = 2;
    public const int MaxButtonLabelLength = 32;
    public const int MaxUrlLength = 512;

    /// <summary>Braille blank: invisible, but counts towards Discord's two-character minimum (Bloxstrap's trick).</summary>
    private const char Filler = '⠀';

    public string? Details { get; init; }

    public string? State { get; init; }

    public DateTimeOffset? Start { get; init; }

    public DateTimeOffset? End { get; init; }

    public string? LargeImage { get; init; }

    public string? LargeText { get; init; }

    public string? SmallImage { get; init; }

    public string? SmallText { get; init; }

    public IReadOnlyList<DiscordButton> Buttons { get; init; } = [];

    /// <summary>The "activity" object of a SET_ACTIVITY command.</summary>
    public void WriteJson(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteNumber("type", 0); // "Playing"

        WriteText(writer, "details", Details);
        WriteText(writer, "state", State);

        if (Start is not null || End is not null)
        {
            writer.WriteStartObject("timestamps");

            if (Start is { } start)
                writer.WriteNumber("start", start.ToUnixTimeMilliseconds());

            if (End is { } end)
                writer.WriteNumber("end", end.ToUnixTimeMilliseconds());

            writer.WriteEndObject();
        }

        if (!string.IsNullOrEmpty(LargeImage) || !string.IsNullOrEmpty(SmallImage))
        {
            writer.WriteStartObject("assets");

            if (!string.IsNullOrEmpty(LargeImage))
            {
                writer.WriteString("large_image", LargeImage);
                WriteText(writer, "large_text", LargeText);
            }

            if (!string.IsNullOrEmpty(SmallImage))
            {
                writer.WriteString("small_image", SmallImage);
                WriteText(writer, "small_text", SmallText);
            }

            writer.WriteEndObject();
        }

        var buttons = Buttons
            .Where(button => button.Label.Length > 0 && button.Url.Length is > 0 and <= MaxUrlLength)
            .Take(MaxButtons)
            .ToList();

        if (buttons.Count > 0)
        {
            writer.WriteStartArray("buttons");

            foreach (var button in buttons)
            {
                writer.WriteStartObject();
                writer.WriteString("label", Truncate(button.Label, MaxButtonLabelLength));
                writer.WriteString("url", button.Url);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        writer.WriteEndObject();
    }

    private static void WriteText(Utf8JsonWriter writer, string name, string? text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        writer.WriteString(name, text.Length < 2 ? text.PadRight(2, Filler) : Truncate(text, MaxTextLength));
    }

    private static string Truncate(string text, int length) =>
        text.Length <= length ? text : text[..(length - 1)] + "…";
}
