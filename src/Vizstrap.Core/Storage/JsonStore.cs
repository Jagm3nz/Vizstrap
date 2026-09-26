using System.Text.Json;
using System.Text.Json.Serialization;
using Vizstrap.Core.Logging;

namespace Vizstrap.Core.Storage;

/// <summary>
/// Loads and saves one JSON document. Saving writes a temp file first, so a crash never leaves a half-written file.
/// </summary>
public sealed class JsonStore<T> where T : class, new()
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public JsonStore(string filePath)
    {
        FilePath = filePath;
    }

    public string FilePath { get; }

    public T Value { get; private set; } = new();

    public void Load()
    {
        if (!File.Exists(FilePath))
        {
            Value = new T();
            return;
        }

        try
        {
            Value = JsonSerializer.Deserialize<T>(File.ReadAllText(FilePath), Options) ?? new T();
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            Log.Warn($"JsonStore<{typeof(T).Name}>", $"Could not read {FilePath}, using defaults: {ex.Message}");
            Value = new T();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);

        string tempPath = FilePath + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(Value, Options));
        File.Move(tempPath, FilePath, overwrite: true);
    }
}
