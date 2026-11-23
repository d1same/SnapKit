namespace SnapKit;

/// <summary>
/// Pure-C# GIF89a animated encoder with 255-color median-cut quantization,
/// dirty-rectangle + transparent-unchanged-pixel delta compression,
/// duplicate-frame delay coalescing, and NETSCAPE2.0 infinite looping.
/// </summary>
public sealed class GifEncoder : IDisposable
{
    private const int TransparentIndex = 255;

    private readonly Stream _stream;
    private readonly bool _leaveOpen;
    private readonly int _width;
    private readonly int _height;
    private bool _headerWritten;
    private bool _finished;
    private byte[]? _previousBgra;
    private byte[]? _pendingBgra;
    private int _pendingDelayCs;

    public int FrameCount { get; private set; }

    public GifEncoder(Stream stream, int width, int height, bool leaveOpen = false)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "GIF dimensions must be positive.");
        if (width > ushort.MaxValue || height > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(width), "GIF dimensions exceed 65535.");
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _width = width;
        _height = height;
        _leaveOpen = leaveOpen;
    }

    public void AddFrame(PixelBuffer frame, int delayMilliseconds)
    {
        if (_finished)
            throw new InvalidOperationException("GIF has already been finalized.");
        if (frame.Width != _width || frame.Height != _height)
            throw new ArgumentException("Frame dimensions must match GIF dimensions.", nameof(frame));

        int delayCs = Math.Clamp((int)Math.Round(delayMilliseconds / 10.0), 2, 6000);
        if (_pendingBgra != null && BuffersEqual(_pendingBgra, frame.Pixels))
        {
            _pendingDelayCs = Math.Min(65000, _pendingDelayCs + delayCs);
            return;
        }

        if (_pendingBgra != null)
            FlushPending();

        _pendingBgra = new byte[frame.Pixels.Length];
        Buffer.BlockCopy(frame.Pixels, 0, _pendingBgra, 0, frame.Pixels.Length);
        _pendingDelayCs = delayCs;
    }

    public void Finish()
    {
        if (_finished)
            return;
        if (_pendingBgra != null)
            FlushPending();
        if (!_headerWritten)
            WriteHeader();
        _stream.WriteByte(0x3B); // GIF Trailer
        _stream.Flush();
        _finished = true;
    }

    public void Dispose()
    {
        try
        {
            if (!_finished)
                Finish();
        }
        finally
        {
            if (!_leaveOpen)
                _stream.Dispose();
        }
    }

    private void FlushPending()
    {
        if (_pendingBgra == null)
            return;
        var current = _pendingBgra;
        int delayCs = _pendingDelayCs;
        _pendingBgra = null;

        if (!_headerWritten)
            WriteHeader();

        int x0 = 0, y0 = 0, x1 = _width, y1 = _height;
        bool useDelta = _previousBgra != null;
        if (useDelta)
        {
            if (!FindDirtyBounds(_previousBgra!, current, _width, _height, out x0, out y0, out x1, out y1))
            {
                // Identical to previous written frame; emit a 1x1 transparent hold frame.
                x0 = 0; y0 = 0; x1 = 1; y1 = 1;
            }
        }

        int dw = x1 - x0;
        int dh = y1 - y0;

        // Quantize changed pixels inside the dirty rect into up to 255 colors (index 255 = transparent).
        var palette = BuildPalette(current, _previousBgra, _width, x0, y0, dw, dh, useDelta, out var lut);
        var indices = new byte[dw * dh];
        int dst = 0;
        for (int y = y0; y < y1; y++)
        {
            int row = y * _width * 4;
            for (int x = x0; x < x1; x++)
            {
                int i = row + x * 4;
                if (useDelta
                    && current[i] == _previousBgra![i]
                    && current[i + 1] == _previousBgra[i + 1]
                    && current[i + 2] == _previousBgra[i + 2])
                {
                    indices[dst++] = TransparentIndex;
                }
                else
                {
                    int key = ((current[i + 2] >> 3) << 10) | ((current[i + 1] >> 3) << 5) | (current[i] >> 3);
                    indices[dst++] = lut[key];
                }
            }
        }

        WriteGraphicControlExtension(delayCs, useDelta);
        WriteImageDescriptor(x0, y0, dw, dh);
        _stream.Write(palette, 0, palette.Length);
        WriteLzwData(indices);

        _previousBgra ??= new byte[current.Length];
        Buffer.BlockCopy(current, 0, _previousBgra, 0, current.Length);
        FrameCount++;
    }

    private void WriteHeader()
    {
        _headerWritten = true;
        // "GIF89a"
        _stream.Write(new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61 });
        WriteUInt16(_width);
        WriteUInt16(_height);
        _stream.WriteByte(0x70); // No Global Color Table, 8-bit color resolution
        _stream.WriteByte(0x00); // Background color index
        _stream.WriteByte(0x00); // Pixel aspect ratio

        // NETSCAPE2.0 Application Extension for infinite looping
        _stream.Write(new byte[]
        {
            0x21, 0xFF, 0x0B,
            (byte)'N', (byte)'E', (byte)'T', (byte)'S', (byte)'C', (byte)'A', (byte)'P', (byte)'E',
            (byte)'2', (byte)'.', (byte)'0',
            0x03, 0x01, 0x00, 0x00, 0x00
        });
    }

    private void WriteGraphicControlExtension(int delayCs, bool hasTransparency)
    {
        _stream.WriteByte(0x21); // Extension Introducer
        _stream.WriteByte(0xF9); // Graphic Control Label
        _stream.WriteByte(0x04); // Block Size
        // Disposal method = 1 (Do not dispose) << 2 = 0x04; bit 0 = transparent flag
        _stream.WriteByte((byte)(0x04 | (hasTransparency ? 0x01 : 0x00)));
        WriteUInt16(Math.Clamp(delayCs, 2, 65535));
        _stream.WriteByte(TransparentIndex);
        _stream.WriteByte(0x00); // Block Terminator
    }

    private void WriteImageDescriptor(int x, int y, int w, int h)
    {
        _stream.WriteByte(0x2C); // Image Separator
        WriteUInt16(x);
        WriteUInt16(y);
        WriteUInt16(w);
        WriteUInt16(h);
        // Local Color Table flag (0x80) | Size 7 -> 2^(7+1) = 256 entries
        _stream.WriteByte(0x87);
    }

    private void WriteUInt16(int value)
    {
        _stream.WriteByte((byte)(value & 0xFF));
        _stream.WriteByte((byte)((value >> 8) & 0xFF));
    }

    private static bool BuffersEqual(byte[] a, byte[] b)
    {
        if (a.Length != b.Length)
            return false;
        return a.AsSpan().SequenceEqual(b.AsSpan());
    }

    private static bool FindDirtyBounds(byte[] prev, byte[] curr, int w, int h, out int x0, out int y0, out int x1, out int y1)
    {
        int minX = w, minY = h, maxX = -1, maxY = -1;
        for (int y = 0; y < h; y++)
        {
            int row = y * w * 4;
            for (int x = 0; x < w; x++)
            {
                int i = row + x * 4;
                if (curr[i] != prev[i] || curr[i + 1] != prev[i + 1] || curr[i + 2] != prev[i + 2])
                {
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }
        }
        if (maxX < minX)
        {
            x0 = y0 = x1 = y1 = 0;
            return false;
        }
        x0 = minX;
        y0 = minY;
        x1 = maxX + 1;
        y1 = maxY + 1;
        return true;
    }

    private static byte[] BuildPalette(
        byte[] curr,
        byte[]? prev,
        int fullWidth,
        int x0,
        int y0,
        int dw,
        int dh,
        bool useDelta,
        out byte[] lut)
    {
        // 5-bit per channel RGB histogram (32,768 buckets)
        var counts = new int[32768];
        var active = new List<int>(1024);
        int step = dw * dh > 400_000 ? 2 : 1;

        for (int y = y0; y < y0 + dh; y += step)
        {
            int row = y * fullWidth * 4;
            for (int x = x0; x < x0 + dw; x += step)
            {
                int i = row + x * 4;
                if (useDelta && prev != null
                    && curr[i] == prev[i]
                    && curr[i + 1] == prev[i + 1]
                    && curr[i + 2] == prev[i + 2])
                    continue;

                int key = ((curr[i + 2] >> 3) << 10) | ((curr[i + 1] >> 3) << 5) | (curr[i] >> 3);
                if (counts[key] == 0)
                    active.Add(key);
                counts[key]++;
            }
        }

        // Median-cut down to at most 255 buckets (index 255 reserved for transparency)
        var boxes = new List<ColorBox>(255);
        if (active.Count == 0)
        {
            active.Add(0);
            counts[0] = 1;
        }
        boxes.Add(ColorBox.Create(active, counts));

        while (boxes.Count < 255)
        {
            int bestIndex = -1;
            int bestScore = 0;
            for (int i = 0; i < boxes.Count; i++)
            {
                var box = boxes[i];
                if (box.Keys.Count <= 1)
                    continue;
                int span = box.MaxSpan;
                int score = span * box.Population;
                if (score > bestScore)
                {
                    bestScore = score;
                    bestIndex = i;
                }
            }
            if (bestIndex < 0)
                break;

            var target = boxes[bestIndex];
            var (left, right) = target.Split(counts);
            boxes[bestIndex] = left;
            boxes.Add(right);
        }

        var palette = new byte[256 * 3];
        var palR = new int[boxes.Count];
        var palG = new int[boxes.Count];
        var palB = new int[boxes.Count];

        for (int i = 0; i < boxes.Count; i++)
        {
            var (r, g, b) = boxes[i].Average(counts);
            palR[i] = r;
            palG[i] = g;
            palB[i] = b;
            palette[i * 3] = (byte)r;
            palette[i * 3 + 1] = (byte)g;
            palette[i * 3 + 2] = (byte)b;
        }

        // Direct bucket lookup for all sampled keys, plus nearest-neighbor fallback for unsampled keys
        lut = new byte[32768];
        if (step > 1)
            Array.Fill(lut, (byte)0xFF);
        for (int i = 0; i < boxes.Count; i++)
        {
            foreach (int key in boxes[i].Keys)
                lut[key] = (byte)i;
        }

        if (step > 1)
        {
            for (int y = y0; y < y0 + dh; y++)
            {
                int row = y * fullWidth * 4;
                for (int x = x0; x < x0 + dw; x++)
                {
                    int i = row + x * 4;
                    int key = ((curr[i + 2] >> 3) << 10) | ((curr[i + 1] >> 3) << 5) | (curr[i] >> 3);
                    if (lut[key] != 0xFF)
                        continue;
                    int r = ((key >> 10) & 31) * 255 / 31;
                    int g = ((key >> 5) & 31) * 255 / 31;
                    int b = (key & 31) * 255 / 31;
                    lut[key] = NearestColor(r, g, b, palR, palG, palB);
                }
            }
        }

        return palette;
    }

    private static byte NearestColor(int r, int g, int b, int[] palR, int[] palG, int[] palB)
    {
        int best = 0;
        int bestDist = int.MaxValue;
        for (int i = 0; i < palR.Length; i++)
        {
            int dr = r - palR[i];
            int dg = g - palG[i];
            int db = b - palB[i];
            int dist = dr * dr * 2 + dg * dg * 4 + db * db;
            if (dist < bestDist)
            {
                bestDist = dist;
                best = i;
                if (dist == 0)
                    break;
            }
        }
        return (byte)best;
    }

    private void WriteLzwData(byte[] indices)
    {
        const int minCodeSize = 8;
        const int clearCode = 1 << minCodeSize;      // 256
        const int endOfInfo = clearCode + 1;         // 257
        const int maxTableSize = 4096;

        _stream.WriteByte(minCodeSize);

        var writer = new SubBlockBitWriter(_stream);
        var hashKeys = new int[8192];
        var hashVals = new short[8192];
        Array.Fill(hashKeys, -1);

        int codeSize = minCodeSize + 1;
        int nextCode = endOfInfo + 1;

        writer.Write(clearCode, codeSize);

        if (indices.Length == 0)
        {
            writer.Write(endOfInfo, codeSize);
            writer.Flush();
            return;
        }

        int prefix = indices[0];
        for (int i = 1; i < indices.Length; i++)
        {
            int k = indices[i];
            int combined = (prefix << 8) | k;
            int slot = HashSlot(hashKeys, combined);
            if (hashKeys[slot] == combined)
            {
                prefix = hashVals[slot];
            }
            else
            {
                writer.Write(prefix, codeSize);
                if (nextCode < maxTableSize)
                {
                    hashKeys[slot] = combined;
                    hashVals[slot] = (short)nextCode++;
                    if (nextCode > (1 << codeSize) && codeSize < 12)
                        codeSize++;
                }
                else
                {
                    writer.Write(clearCode, codeSize);
                    Array.Fill(hashKeys, -1);
                    codeSize = minCodeSize + 1;
                    nextCode = endOfInfo + 1;
                }
                prefix = k;
            }
        }

        writer.Write(prefix, codeSize);
        writer.Write(endOfInfo, codeSize);
        writer.Flush();
    }

    private static int HashSlot(int[] keys, int combined)
    {
        int slot = ((combined * 2654435761u) >> 19) is var h ? (int)(h & 8191) : 0;
        while (keys[slot] != -1 && keys[slot] != combined)
            slot = (slot + 1) & 8191;
        return slot;
    }

    private sealed class SubBlockBitWriter
    {
        private readonly Stream _stream;
        private readonly byte[] _block = new byte[255];
        private int _blockLen;
        private uint _bitBuffer;
        private int _bitCount;

        public SubBlockBitWriter(Stream stream) => _stream = stream;

        public void Write(int code, int bits)
        {
            _bitBuffer |= ((uint)code) << _bitCount;
            _bitCount += bits;
            while (_bitCount >= 8)
            {
                EmitByte((byte)(_bitBuffer & 0xFF));
                _bitBuffer >>= 8;
                _bitCount -= 8;
            }
        }

        public void Flush()
        {
            if (_bitCount > 0)
            {
                EmitByte((byte)(_bitBuffer & 0xFF));
                _bitBuffer = 0;
                _bitCount = 0;
            }
            if (_blockLen > 0)
            {
                _stream.WriteByte((byte)_blockLen);
                _stream.Write(_block, 0, _blockLen);
                _blockLen = 0;
            }
            _stream.WriteByte(0x00); // Sub-block terminator
        }

        private void EmitByte(byte value)
        {
            _block[_blockLen++] = value;
            if (_blockLen == 255)
            {
                _stream.WriteByte(255);
                _stream.Write(_block, 0, 255);
                _blockLen = 0;
            }
        }
    }

    private readonly struct ColorBox
    {
        public List<int> Keys { get; }
        public int Population { get; }
        public int RMin { get; }
        public int RMax { get; }
        public int GMin { get; }
        public int GMax { get; }
        public int BMin { get; }
        public int BMax { get; }

        public int MaxSpan => Math.Max(RMax - RMin, Math.Max(GMax - GMin, BMax - BMin));

        private ColorBox(List<int> keys, int pop, int rMin, int rMax, int gMin, int gMax, int bMin, int bMax)
        {
            Keys = keys;
            Population = pop;
            RMin = rMin; RMax = rMax;
            GMin = gMin; GMax = gMax;
            BMin = bMin; BMax = bMax;
        }

        public static ColorBox Create(List<int> keys, int[] counts)
        {
            int rMin = 31, rMax = 0, gMin = 31, gMax = 0, bMin = 31, bMax = 0, pop = 0;
            foreach (int k in keys)
            {
                int r = (k >> 10) & 31;
                int g = (k >> 5) & 31;
                int b = k & 31;
                if (r < rMin) rMin = r;
                if (r > rMax) rMax = r;
                if (g < gMin) gMin = g;
                if (g > gMax) gMax = g;
                if (b < bMin) bMin = b;
                if (b > bMax) bMax = b;
                pop += counts[k];
            }
            return new ColorBox(keys, pop, rMin, rMax, gMin, gMax, bMin, bMax);
        }

        public (ColorBox Left, ColorBox Right) Split(int[] counts)
        {
            int rSpan = RMax - RMin;
            int gSpan = GMax - GMin;
            int bSpan = BMax - BMin;
            if (gSpan >= rSpan && gSpan >= bSpan)
                Keys.Sort((a, b) => ((a >> 5) & 31).CompareTo((b >> 5) & 31));
            else if (rSpan >= bSpan)
                Keys.Sort((a, b) => ((a >> 10) & 31).CompareTo((b >> 10) & 31));
            else
                Keys.Sort((a, b) => (a & 31).CompareTo(b & 31));

            int half = Population / 2;
            int acc = 0;
            int split = 1;
            for (int i = 0; i < Keys.Count - 1; i++)
            {
                acc += counts[Keys[i]];
                split = i + 1;
                if (acc >= half)
                    break;
            }

            var leftKeys = Keys.GetRange(0, split);
            var rightKeys = Keys.GetRange(split, Keys.Count - split);
            return (Create(leftKeys, counts), Create(rightKeys, counts));
        }

        public (int R, int G, int B) Average(int[] counts)
        {
            long sr = 0, sg = 0, sb = 0, total = 0;
            foreach (int k in Keys)
            {
                int w = counts[k];
                sr += (((k >> 10) & 31) * 255 / 31) * w;
                sg += (((k >> 5) & 31) * 255 / 31) * w;
                sb += ((k & 31) * 255 / 31) * w;
                total += w;
            }
            if (total <= 0)
                return (0, 0, 0);
            return ((int)(sr / total), (int)(sg / total), (int)(sb / total));
        }
    }
}
