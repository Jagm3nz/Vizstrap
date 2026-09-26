namespace Vizstrap.Core.Effects;

/// <summary>
/// One frame from the player's own game, kept on this computer for the effects preview in the settings:
/// the picture at half size (B8G8R8A8) and the depth AI's map for it (closeness 0 far … 1 near).
/// Saved every so often while the effects run; each save replaces the last.
/// </summary>
public sealed record PreviewFrame(int Width, int Height, byte[] Pixels, int DepthWidth, int DepthHeight, float[] Depth)
{
    public const string FileName = "preview.bin";

    private const int Magic = 0x5650_5A56; // "VZPV"
    private const int Version = 1;

    public static string PathIn(string folder) => Path.Combine(folder, FileName);

    public static bool Exists(string folder) => File.Exists(PathIn(folder));

    /// <summary>Writes the frame (to a temporary file first, so a reader never sees half of it).</summary>
    public void Save(string folder)
    {
        Directory.CreateDirectory(folder);
        string path = PathIn(folder), partial = path + ".part";

        using (var writer = new BinaryWriter(File.Create(partial)))
        {
            writer.Write(Magic);
            writer.Write(Version);
            writer.Write(Width);
            writer.Write(Height);
            writer.Write(DepthWidth);
            writer.Write(DepthHeight);
            writer.Write(Pixels);

            foreach (float value in Depth)
                writer.Write(value);
        }

        File.Move(partial, path, overwrite: true);
    }

    /// <summary>The saved frame, or null when there's none or it's unreadable.</summary>
    public static PreviewFrame? Load(string folder)
    {
        try
        {
            using var reader = new BinaryReader(File.OpenRead(PathIn(folder)));

            if (reader.ReadInt32() != Magic || reader.ReadInt32() != Version)
                return null;

            int width = reader.ReadInt32(), height = reader.ReadInt32();
            int depthWidth = reader.ReadInt32(), depthHeight = reader.ReadInt32();

            if (width is <= 0 or > 8192 || height is <= 0 or > 8192 || depthWidth is <= 0 or > 2048 || depthHeight is <= 0 or > 2048)
                return null;

            byte[] pixels = reader.ReadBytes(width * height * 4);
            var depth = new float[depthWidth * depthHeight];

            for (int i = 0; i < depth.Length; i++)
                depth[i] = reader.ReadSingle();

            return pixels.Length == width * height * 4 ? new PreviewFrame(width, height, pixels, depthWidth, depthHeight, depth) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or EndOfStreamException)
        {
            return null;
        }
    }
}
