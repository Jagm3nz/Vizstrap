using System.Buffers.Binary;
using System.Text;

namespace Vizstrap.Core.Mods;

/// <summary>
/// Reads and writes PNG tEXt chunks, so pictures Vizstrap generates carry a label saying so (and
/// which accent they were made for) without a separate record of them.
/// </summary>
public static class PngText
{
    private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static readonly uint[] CrcTable = BuildCrcTable();

    /// <summary>The PNG with a tEXt chunk "<paramref name="keyword"/>" = <paramref name="text"/> before its end.</summary>
    public static byte[] Add(byte[] png, string keyword, string text)
    {
        if (!png.AsSpan().StartsWith(Signature))
            throw new InvalidDataException("Not a PNG file.");

        int end = FindChunk(png, "IEND") ?? throw new InvalidDataException("PNG without an end.");

        byte[] data = [.. Encoding.Latin1.GetBytes(keyword), 0, .. Encoding.Latin1.GetBytes(text)];
        byte[] chunk = BuildChunk("tEXt", data);

        return [.. png.AsSpan(0, end), .. chunk, .. png.AsSpan(end)];
    }

    /// <summary>The text stored under <paramref name="keyword"/>, or null (also for files that aren't PNG).</summary>
    public static string? Read(ReadOnlySpan<byte> png, string keyword)
    {
        if (!png.StartsWith(Signature))
            return null;

        int position = Signature.Length;

        while (position + 12 <= png.Length)
        {
            int length = BinaryPrimitives.ReadInt32BigEndian(png[position..]);

            if (length < 0 || position + 12 + length > png.Length)
                return null;

            var type = png.Slice(position + 4, 4);
            var data = png.Slice(position + 8, length);

            if (type.SequenceEqual("tEXt"u8))
            {
                int separator = data.IndexOf((byte)0);

                if (separator > 0 && Encoding.Latin1.GetString(data[..separator]) == keyword)
                    return Encoding.Latin1.GetString(data[(separator + 1)..]);
            }

            if (type.SequenceEqual("IEND"u8))
                return null;

            position += 12 + length;
        }

        return null;
    }

    public static string? ReadFile(string path, string keyword) =>
        File.Exists(path) ? Read(File.ReadAllBytes(path), keyword) : null;

    private static int? FindChunk(byte[] png, string wanted)
    {
        int position = Signature.Length;

        while (position + 12 <= png.Length)
        {
            int length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(position));

            if (Encoding.ASCII.GetString(png, position + 4, 4) == wanted)
                return position;

            position += 12 + length;
        }

        return null;
    }

    private static byte[] BuildChunk(string type, byte[] data)
    {
        var chunk = new byte[12 + data.Length];
        BinaryPrimitives.WriteInt32BigEndian(chunk, data.Length);
        Encoding.ASCII.GetBytes(type, chunk.AsSpan(4));
        data.CopyTo(chunk, 8);
        BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(8 + data.Length), Crc32(chunk.AsSpan(4, 4 + data.Length)));
        return chunk;
    }

    private static uint Crc32(ReadOnlySpan<byte> bytes)
    {
        uint crc = 0xFFFFFFFF;

        foreach (byte b in bytes)
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);

        return crc ^ 0xFFFFFFFF;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];

        for (uint n = 0; n < 256; n++)
        {
            uint c = n;

            for (int k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;

            table[n] = c;
        }

        return table;
    }
}
