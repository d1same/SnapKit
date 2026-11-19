namespace SnapKit;

/// <summary>BGRA pixel buffer. One byte order everywhere so capture, stitch, and blur share a type.</summary>
public sealed class PixelBuffer
{
    public int Width { get; }
    public int Height { get; }
    public byte[] Pixels { get; }
    public int Stride => Width * 4;

    public PixelBuffer(int width, int height)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Width and height must be positive.");
        Width = width;
        Height = height;
        Pixels = new byte[width * height * 4];
    }

    public PixelBuffer(int width, int height, byte[] pixels)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Width and height must be positive.");
        if (pixels.Length < width * height * 4)
            throw new ArgumentException("Pixel buffer is smaller than width * height * 4.", nameof(pixels));
        Width = width;
        Height = height;
        Pixels = pixels;
    }

    public PixelBuffer Clone()
    {
        var copy = new byte[Width * Height * 4];
        Buffer.BlockCopy(Pixels, 0, copy, 0, copy.Length);
        return new PixelBuffer(Width, Height, copy);
    }

    public void Fill(byte b, byte g, byte r, byte a = 255)
    {
        for (int i = 0; i < Width * Height * 4; i += 4)
        {
            Pixels[i] = b;
            Pixels[i + 1] = g;
            Pixels[i + 2] = r;
            Pixels[i + 3] = a;
        }
    }

    public void FillRect(int x, int y, int w, int h, byte b, byte g, byte r, byte a = 255)
    {
        int x0 = Math.Max(0, x);
        int y0 = Math.Max(0, y);
        int x1 = Math.Min(Width, x + w);
        int y1 = Math.Min(Height, y + h);
        for (int yy = y0; yy < y1; yy++)
        {
            int row = yy * Stride;
            for (int xx = x0; xx < x1; xx++)
            {
                int i = row + xx * 4;
                Pixels[i] = b;
                Pixels[i + 1] = g;
                Pixels[i + 2] = r;
                Pixels[i + 3] = a;
            }
        }
    }

    public void SetPixel(int x, int y, byte b, byte g, byte r, byte a = 255)
    {
        int i = (y * Width + x) * 4;
        Pixels[i] = b;
        Pixels[i + 1] = g;
        Pixels[i + 2] = r;
        Pixels[i + 3] = a;
    }

    public (byte B, byte G, byte R, byte A) GetPixel(int x, int y)
    {
        int i = (y * Width + x) * 4;
        return (Pixels[i], Pixels[i + 1], Pixels[i + 2], Pixels[i + 3]);
    }

    public string HexAt(int x, int y)
    {
        x = Math.Clamp(x, 0, Width - 1);
        y = Math.Clamp(y, 0, Height - 1);
        var (b, g, r, _) = GetPixel(x, y);
        return $"#{r:X2}{g:X2}{b:X2}";
    }

    public void ForceOpaque()
    {
        int n = Width * Height * 4;
        for (int i = 3; i < n; i += 4)
            Pixels[i] = 255;
    }

    public PixelBuffer Crop(int x, int y, int w, int h)
    {
        if (w <= 0 || h <= 0)
            throw new ArgumentOutOfRangeException(nameof(w));
        var dest = new PixelBuffer(w, h);
        dest.ForceOpaque();
        for (int row = 0; row < h; row++)
        {
            int sy = y + row;
            if ((uint)sy >= (uint)Height)
                continue;
            int copyW = w;
            int sx = x;
            int dx = 0;
            if (sx < 0)
            {
                dx = -sx;
                copyW -= dx;
                sx = 0;
            }
            if (sx >= Width || copyW <= 0)
                continue;
            if (sx + copyW > Width)
                copyW = Width - sx;
            Buffer.BlockCopy(Pixels, sy * Stride + sx * 4, dest.Pixels, row * dest.Stride + dx * 4, copyW * 4);
        }
        return dest;
    }

    public void CopyPixelsFrom(PixelBuffer src, int srcX, int srcY, int dstX, int dstY, int w, int h)
    {
        for (int row = 0; row < h; row++)
        {
            int sy = srcY + row;
            int dy = dstY + row;
            if ((uint)sy >= (uint)src.Height || (uint)dy >= (uint)Height)
                continue;
            int sx = srcX;
            int dx = dstX;
            int copyW = w;
            if (sx < 0)
            {
                dx -= sx;
                copyW += sx;
                sx = 0;
            }
            if (dx < 0)
            {
                sx -= dx;
                copyW += dx;
                dx = 0;
            }
            if (sx >= src.Width || dx >= Width || copyW <= 0)
                continue;
            if (sx + copyW > src.Width)
                copyW = src.Width - sx;
            if (dx + copyW > Width)
                copyW = Width - dx;
            if (copyW <= 0)
                continue;
            Buffer.BlockCopy(src.Pixels, sy * src.Stride + sx * 4, Pixels, dy * Stride + dx * 4, copyW * 4);
        }
    }

    public void Pixelate(int x, int y, int w, int h, int block)
    {
        if (block < 2)
            block = 2;
        int x0 = Math.Clamp(x, 0, Width);
        int y0 = Math.Clamp(y, 0, Height);
        int x1 = Math.Clamp(x + w, 0, Width);
        int y1 = Math.Clamp(y + h, 0, Height);
        for (int by = y0; by < y1; by += block)
        {
            for (int bx = x0; bx < x1; bx += block)
            {
                int bw = Math.Min(block, x1 - bx);
                int bh = Math.Min(block, y1 - by);
                long sb = 0, sg = 0, sr = 0;
                int count = bw * bh;
                for (int yy = 0; yy < bh; yy++)
                {
                    int row = (by + yy) * Stride + bx * 4;
                    for (int xx = 0; xx < bw; xx++)
                    {
                        int i = row + xx * 4;
                        sb += Pixels[i];
                        sg += Pixels[i + 1];
                        sr += Pixels[i + 2];
                    }
                }
                byte ab = (byte)(sb / count);
                byte ag = (byte)(sg / count);
                byte ar = (byte)(sr / count);
                for (int yy = 0; yy < bh; yy++)
                {
                    int row = (by + yy) * Stride + bx * 4;
                    for (int xx = 0; xx < bw; xx++)
                    {
                        int i = row + xx * 4;
                        Pixels[i] = ab;
                        Pixels[i + 1] = ag;
                        Pixels[i + 2] = ar;
                        Pixels[i + 3] = 255;
                    }
                }
            }
        }
    }

    public PixelBuffer Resize(int newWidth, int newHeight)
    {
        if (newWidth <= 0 || newHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(newWidth));
        var dst = new PixelBuffer(newWidth, newHeight);
        for (int y = 0; y < newHeight; y++)
        {
            int sy = y * Height / newHeight;
            for (int x = 0; x < newWidth; x++)
            {
                int sx = x * Width / newWidth;
                int si = (sy * Width + sx) * 4;
                int di = (y * newWidth + x) * 4;
                dst.Pixels[di] = Pixels[si];
                dst.Pixels[di + 1] = Pixels[si + 1];
                dst.Pixels[di + 2] = Pixels[si + 2];
                dst.Pixels[di + 3] = 255;
            }
        }
        return dst;
    }
}
