using System.Text;

namespace Vizstrap.Core.Activity;

/// <summary>Follows a log file another process keeps writing, like <c>tail -f</c>.</summary>
public static class LogTailer
{
    /// <summary>
    /// Hands over the file's lines in batches, from the start and then as they're appended, until
    /// cancelled. A line is handed over only once its line break is written.
    /// </summary>
    public static async Task FollowAsync(
        string path, Action<IReadOnlyList<string>> onLines, TimeSpan pollInterval, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

        var pending = new StringBuilder();
        var buffer = new char[16 * 1024];

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                int read = await reader.ReadAsync(buffer, cancellationToken);

                if (read == 0)
                {
                    await Task.Delay(pollInterval, cancellationToken);
                    continue;
                }

                pending.Append(buffer, 0, read);

                var lines = TakeCompleteLines(pending);

                if (lines.Count > 0)
                    onLines(lines);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    internal static List<string> TakeCompleteLines(StringBuilder pending)
    {
        var lines = new List<string>();
        string text = pending.ToString();
        int start = 0;

        for (int newline = text.IndexOf('\n'); newline >= 0; newline = text.IndexOf('\n', start))
        {
            lines.Add(text[start..newline].TrimEnd('\r'));
            start = newline + 1;
        }

        pending.Remove(0, start);
        return lines;
    }
}
