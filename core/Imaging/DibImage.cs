namespace SnapKit;

/// <summary>24-bit bottom-up DIB. Outlook, browsers, and ticket editors paste this format.</summary>
public static class DibImage
{
    public static byte[] FromTopDownBgra(byte[] bgra, int width, int height, int stride)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (bgra.Length < stride * height)
            throw new ArgumentException("Pixel buffer is shorter than stride * height.", nameof(bgra));

        int rowBytes = ((width * 3 + 3) / 4) * 4;
        var dib = new byte[40 + rowBytes * height];
        WriteInt(dib, 0, 40);
        WriteInt(dib, 4, width);
        WriteInt(dib, 8, height);
        dib[12] = 1;
        dib[14] = 24;
        WriteInt(dib, 20, rowBytes * height);

        for (int y = 0; y < height; y++)
        {
            int src = (height - 1 - y) * stride;
            int dst = 40 + y * rowBytes;
            for (int x = 0; x < width; x++)
            {
                int i = src + x * 4;
                dib[dst++] = bgra[i];
                dib[dst++] = bgra[i + 1];
                dib[dst++] = bgra[i + 2];
            }
        }

        return dib;
    }

    private static void WriteInt(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)value;
        buffer[offset + 1] = (byte)(value >> 8);
        buffer[offset + 2] = (byte)(value >> 16);
        buffer[offset + 3] = (byte)(value >> 24);
    }
}
