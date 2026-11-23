using System.Buffers.Binary;

namespace SnapKit;

/// <summary>
/// Pure-C# 16-bit stereo PCM mixer for combining optional system loopback + microphone streams
/// cleanly into a single synchronized AAC-ready PCM buffer.
/// </summary>
public static class AudioMixer
{
    public const int SampleRate = 48000;
    public const int Channels = 2;
    public const int BitsPerSample = 16;
    public const int BlockAlign = Channels * (BitsPerSample / 8); // 4 bytes per stereo frame
    public const int BytesPerSecond = SampleRate * BlockAlign;

    public static byte[] MixPcm16(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b, int targetBytes)
    {
        int aligned = (Math.Max(0, targetBytes) / BlockAlign) * BlockAlign;
        var output = new byte[aligned];
        int sampleCount = aligned / 2;

        for (int s = 0; s < sampleCount; s++)
        {
            int byteOffset = s * 2;
            int va = byteOffset + 1 < a.Length ? BinaryPrimitives.ReadInt16LittleEndian(a.Slice(byteOffset, 2)) : 0;
            int vb = byteOffset + 1 < b.Length ? BinaryPrimitives.ReadInt16LittleEndian(b.Slice(byteOffset, 2)) : 0;
            int sum = Math.Clamp(va + vb, short.MinValue, short.MaxValue);
            BinaryPrimitives.WriteInt16LittleEndian(output.AsSpan(byteOffset, 2), (short)sum);
        }

        return output;
    }

    public static int BytesForMilliseconds(int milliseconds)
    {
        int frames = (int)Math.Round(SampleRate * (Math.Max(1, milliseconds) / 1000.0));
        return Math.Max(BlockAlign, frames * BlockAlign);
    }
}
