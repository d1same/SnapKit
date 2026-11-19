using System.IO;
using System.Runtime.InteropServices;

namespace SnapKit;

internal static class Shutter
{
    private static readonly byte[] Wav = Build();
    private static GCHandle _pin;

    public static void Play()
    {
        _ = Task.Run(PlayCore);
    }

    private static void PlayCore()
    {
        if (!_pin.IsAllocated)
            _pin = GCHandle.Alloc(Wav, GCHandleType.Pinned);
        try
        {
            NativeMethods.PlaySound(Wav, IntPtr.Zero, NativeMethods.SndMemory | NativeMethods.SndNoDefault);
        }
        catch
        {
            // Sound is optional. A missing wave device should not break the capture.
        }
    }

    private static byte[] Build()
    {
        const int rate = 22050;
        const double seconds = 0.085;
        int count = (int)(rate * seconds);
        var pcm = new byte[count * 2];
        var random = new Random(2);
        for (int i = 0; i < count; i++)
        {
            double t = i / (double)rate;
            double envelope = Math.Exp(-t * 42);
            double noise = (random.NextDouble() * 2 - 1) * 0.55;
            double click = Math.Sin(2 * Math.PI * 1680 * t) * 0.45;
            short sample = (short)(Math.Clamp((noise + click) * envelope, -1, 1) * 14000);
            pcm[i * 2] = (byte)(sample & 0xFF);
            pcm[i * 2 + 1] = (byte)((sample >> 8) & 0xFF);
        }

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8);
        writer.Write(36 + pcm.Length);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(rate);
        writer.Write(rate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(pcm.Length);
        writer.Write(pcm);
        return stream.ToArray();
    }
}
