namespace SnapKit;

/// <summary>
/// Fast pure-C# BGRA32 (top-down) to NV12 (4:2:0 Y + interleaved UV) converter
/// for universal top-down H.264 Media Foundation hardware & software encoding.
/// </summary>
public static class Nv12Convert
{
    public static int BufferSize(int width, int height)
    {
        int w = Math.Max(2, width & ~1);
        int h = Math.Max(2, height & ~1);
        return w * h + (w * h) / 2;
    }

    public static byte[] FromBgra(PixelBuffer source, int targetWidth, int targetHeight)
    {
        int w = Math.Max(2, targetWidth & ~1);
        int h = Math.Max(2, targetHeight & ~1);
        var nv12 = new byte[BufferSize(w, h)];
        FromBgraInto(source, w, h, nv12);
        return nv12;
    }

    public static void FromBgraInto(PixelBuffer source, int targetWidth, int targetHeight, byte[] dest)
    {
        int w = Math.Max(2, targetWidth & ~1);
        int h = Math.Max(2, targetHeight & ~1);
        int uvStart = w * h;
        byte[] src = source.Pixels;
        int srcW = source.Width;
        int srcH = source.Height;
        int srcStride = source.Stride;

        // BT.601 studio-range (16..235 Y, 16..240 UV) — universally expected by H.264 encoders.
        for (int y = 0; y < h; y++)
        {
            int sy = Math.Min(y, srcH - 1);
            int srcRow = sy * srcStride;
            int yRow = y * w;
            for (int x = 0; x < w; x++)
            {
                int sx = Math.Min(x, srcW - 1);
                int i = srcRow + sx * 4;
                int b = src[i];
                int g = src[i + 1];
                int r = src[i + 2];
                int luma = ((66 * r + 129 * g + 25 * b + 128) >> 8) + 16;
                dest[yRow + x] = (byte)Math.Clamp(luma, 16, 235);
            }
        }

        // 2x2 subsampled interleaved U, V plane
        for (int y = 0; y < h; y += 2)
        {
            int sy0 = Math.Min(y, srcH - 1);
            int sy1 = Math.Min(y + 1, srcH - 1);
            int row0 = sy0 * srcStride;
            int row1 = sy1 * srcStride;
            int uvRow = uvStart + (y >> 1) * w;

            for (int x = 0; x < w; x += 2)
            {
                int sx0 = Math.Min(x, srcW - 1);
                int sx1 = Math.Min(x + 1, srcW - 1);

                int i00 = row0 + sx0 * 4;
                int i01 = row0 + sx1 * 4;
                int i10 = row1 + sx0 * 4;
                int i11 = row1 + sx1 * 4;

                int b = (src[i00] + src[i01] + src[i10] + src[i11] + 2) >> 2;
                int g = (src[i00 + 1] + src[i01 + 1] + src[i10 + 1] + src[i11 + 1] + 2) >> 2;
                int r = (src[i00 + 2] + src[i01 + 2] + src[i10 + 2] + src[i11 + 2] + 2) >> 2;

                int u = ((-38 * r - 74 * g + 112 * b + 128) >> 8) + 128;
                int v = ((112 * r - 94 * g - 18 * b + 128) >> 8) + 128;

                dest[uvRow + x] = (byte)Math.Clamp(u, 16, 240);
                dest[uvRow + x + 1] = (byte)Math.Clamp(v, 16, 240);
            }
        }
    }
}
