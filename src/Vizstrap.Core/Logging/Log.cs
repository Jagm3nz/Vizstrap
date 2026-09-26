using System.Diagnostics;

namespace Vizstrap.Core.Logging;

/// <summary>
/// Minimal thread-safe file logger. Does nothing until <see cref="Initialize"/> is called.
/// </summary>
public static class Log
{
    private const int KeptLogFiles = 10;

    private static readonly object Sync = new();
    private static StreamWriter? _writer;

    public static string? FilePath { get; private set; }

    public static void Initialize(string logsDirectory)
    {
        lock (Sync)
        {
            if (_writer is not null)
                return;

            Directory.CreateDirectory(logsDirectory);
            DeleteOldLogs(logsDirectory);

            string fileName = $"Vizstrap_{DateTime.UtcNow:yyyyMMdd'T'HHmmss'Z'}_{Environment.ProcessId}.log";
            FilePath = Path.Combine(logsDirectory, fileName);

            var stream = new FileStream(FilePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
            _writer = new StreamWriter(stream) { AutoFlush = true };
        }
    }

    public static void Shutdown()
    {
        lock (Sync)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }

    public static void Info(string source, string message) => Write("INFO", source, message);

    public static void Warn(string source, string message) => Write("WARN", source, message);

    public static void Error(string source, string message) => Write("ERROR", source, message);

    public static void Error(string source, Exception exception) => Write("ERROR", source, exception.ToString());

    private static void Write(string level, string source, string message)
    {
        string line = $"{DateTime.UtcNow:yyyy-MM-dd'T'HH:mm:ss.fff'Z'} [{level}] [{source}] {message}";

        Debug.WriteLine(line);

        lock (Sync)
        {
            try
            {
                _writer?.WriteLine(line);
            }
            catch (IOException)
            {
                // logging must never take the app down
            }
        }
    }

    private static void DeleteOldLogs(string logsDirectory)
    {
        var oldLogs = new DirectoryInfo(logsDirectory)
            .GetFiles("Vizstrap_*.log")
            .OrderByDescending(file => file.CreationTimeUtc)
            .Skip(KeptLogFiles - 1);

        foreach (var file in oldLogs)
        {
            try
            {
                file.Delete();
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
