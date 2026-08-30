using System.Buffers.Binary;
using System.IO.Compression;

namespace Moovie.App.Web;

/// <summary>
/// Writes 8-bit RGBA pixels as a PNG, in managed code and nothing else.
///
/// Avalonia can do this — <see cref="Avalonia.Media.Imaging.WriteableBitmap"/> saves as PNG — but
/// that goes through Skia, and this runs on a background thread so as not to hold up drawing.
/// Two threads in Skia at once is asking for trouble, and the bitmaps would be released on the
/// finaliser thread, in Skia again, at a moment nothing controls. Nothing here is unmanaged, so
/// none of that arises.
/// </summary>
internal static class PngWriter
{
    private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];
    private static readonly uint[] CrcTable = BuildCrcTable();

    public static byte[] Encode(byte[] pixels, int width, int height)
    {
        var png = new MemoryStream();
        png.Write(Signature);

        Span<byte> header = stackalloc byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header[..4], (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(header.Slice(4, 4), (uint)height);
        header[8] = 8;                      // bits per channel
        header[9] = 6;                      // truecolour with alpha
        header[10] = header[11] = header[12] = 0;   // deflate, standard filtering, no interlace
        WriteChunk(png, "IHDR"u8, header);

        WriteChunk(png, "IDAT"u8, Compress(pixels, width, height));
        WriteChunk(png, "IEND"u8, []);

        return png.ToArray();
    }

    private static byte[] Compress(byte[] pixels, int width, int height)
    {
        var rowBytes = width * 4;
        var raw = new MemoryStream();

        // Deflate is left to find the repetition across a row; what it cannot see by itself is
        // that a row is often identical to the one above it, which in a window of flat panels is
        // most of them. Subtracting the row above turns those into a run of zeroes.
        using (var zlib = new ZLibStream(raw, CompressionLevel.Fastest, leaveOpen: true))
        {
            var line = new byte[rowBytes];

            for (var y = 0; y < height; y++)
            {
                var offset = y * rowBytes;

                if (y == 0)
                {
                    zlib.WriteByte(0);
                    zlib.Write(pixels, 0, rowBytes);
                    continue;
                }

                zlib.WriteByte(2);
                for (var i = 0; i < rowBytes; i++)
                    line[i] = (byte)(pixels[offset + i] - pixels[offset - rowBytes + i]);
                zlib.Write(line, 0, rowBytes);
            }
        }

        return raw.ToArray();
    }

    private static void WriteChunk(Stream png, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, (uint)data.Length);
        png.Write(length);
        png.Write(type);
        png.Write(data);

        Span<byte> crc = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32(type, data));
        png.Write(crc);
    }

    private static uint Crc32(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in type)
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        foreach (var b in data)
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFFu;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (var n = 0u; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[n] = c;
        }

        return table;
    }
}
