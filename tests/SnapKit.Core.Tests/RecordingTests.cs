using System.Buffers.Binary;
using SnapKit;

namespace SnapKit.Tests;

public class RecordingTests
{
    [Fact]
    public void Gif_encoder_writes_valid_gif89a_looping_animation_with_delta_coalescing()
    {
        var f1 = new PixelBuffer(24, 16);
        f1.Fill(40, 80, 200);
        f1.FillRect(4, 4, 8, 8, 255, 255, 255);

        var f2Same = f1.Clone();

        var f3Changed = f1.Clone();
        f3Changed.FillRect(12, 6, 6, 6, 30, 220, 50);

        using var ms = new MemoryStream();
        using (var gif = new GifEncoder(ms, 24, 16, leaveOpen: true))
        {
            gif.AddFrame(f1, 100);
            gif.AddFrame(f2Same, 100);   // coalesced into f1's delay
            gif.AddFrame(f3Changed, 80); // delta frame
            gif.Finish();
            Assert.Equal(2, gif.FrameCount);
        }

        byte[] bytes = ms.ToArray();
        Assert.True(bytes.Length > 40);
        // Header: "GIF89a"
        Assert.Equal("GIF89a", System.Text.Encoding.ASCII.GetString(bytes, 0, 6));
        // Logical screen width & height (little-endian)
        Assert.Equal(24, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(6, 2)));
        Assert.Equal(16, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(8, 2)));
        // Contains NETSCAPE2.0 loop block
        Assert.Contains("NETSCAPE2.0", System.Text.Encoding.ASCII.GetString(bytes));
        // Ends with GIF trailer 0x3B
        Assert.Equal(0x3B, bytes[^1]);
    }

    [Fact]
    public void Recording_overlay_draws_cursor_and_click_ring_pixels()
    {
        var frame = new PixelBuffer(48, 48);
        frame.Fill(20, 20, 20);

        RecordingOverlay.DrawClickPulses(frame, new[] { new ClickPulse(24, 24, 0.08, Secondary: false) });
        RecordingOverlay.DrawCursor(frame, 10, 10, scale: 1.0);

        // Cursor outline & fill pixels at (10,10) and (11,12)
        Assert.Equal(20, frame.GetPixel(10, 10).R);
        Assert.Equal(250, frame.GetPixel(11, 12).R);

        // Click ring altered pixels around (24,24)
        bool sawHighlight = false;
        for (int x = 24; x < 44; x++)
        {
            var px = frame.GetPixel(x, 24);
            if (px.R > 60 && px.G > 50)
            {
                sawHighlight = true;
                break;
            }
        }
        Assert.True(sawHighlight);
    }

    [Fact]
    public void Audio_mixer_sums_and_clamps_stereo_pcm16_cleanly()
    {
        var a = new byte[8];
        var b = new byte[8];
        BinaryPrimitives.WriteInt16LittleEndian(a.AsSpan(0, 2), 20000);
        BinaryPrimitives.WriteInt16LittleEndian(a.AsSpan(2, 2), -20000);
        BinaryPrimitives.WriteInt16LittleEndian(b.AsSpan(0, 2), 20000);
        BinaryPrimitives.WriteInt16LittleEndian(b.AsSpan(2, 2), -20000);

        var mixed = AudioMixer.MixPcm16(a, b, 12);
        Assert.Equal(12, mixed.Length);
        Assert.Equal(short.MaxValue, BinaryPrimitives.ReadInt16LittleEndian(mixed.AsSpan(0, 2)));
        Assert.Equal(short.MinValue, BinaryPrimitives.ReadInt16LittleEndian(mixed.AsSpan(2, 2)));
        // Unfilled tail is clean silence
        Assert.Equal(0, BinaryPrimitives.ReadInt16LittleEndian(mixed.AsSpan(8, 2)));
    }

    [Fact]
    public void Recording_shortcuts_and_toggles_roundtrip()
    {
        var settings = AppSettings.CreateDefault();
        Assert.Equal("Ctrl+Alt+V", settings.RecordVideo.Display);
        Assert.Equal("Ctrl+Alt+G", settings.RecordGif.Display);
        Assert.True(settings.RecordShowCursor);
        Assert.True(settings.RecordSystemAudio);
        Assert.False(settings.RecordMicAudio);

        settings.RecordMicAudio = true;
        settings.RecordShowCursor = false;
        var path = Path.Combine(Path.GetTempPath(), "snapkit-rec-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            SettingsStore.Save(settings, path);
            var loaded = SettingsStore.Load(path);
            Assert.True(loaded.RecordMicAudio);
            Assert.False(loaded.RecordShowCursor);
            Assert.Equal("Ctrl+Alt+V", loaded.RecordVideo.Display);
            Assert.Equal("Ctrl+Alt+G", loaded.RecordGif.Display);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Nv12_converter_produces_valid_studio_range_y_and_uv_planes()
    {
        var frame = new PixelBuffer(8, 6);
        frame.FillRect(0, 0, 8, 2, 0, 0, 0);         // black -> Y=16, U=128, V=128
        frame.FillRect(0, 2, 8, 2, 255, 255, 255);   // white -> Y=235, U=128, V=128
        frame.FillRect(0, 4, 8, 2, 0, 0, 255);       // pure red -> high V, low U

        byte[] nv12 = Nv12Convert.FromBgra(frame, 8, 6);
        Assert.Equal(8 * 6 + (8 * 6) / 2, nv12.Length);

        // Top row (black)
        Assert.Equal(16, nv12[0]);
        // Middle row (white)
        Assert.Equal(235, nv12[2 * 8]);
        // UV plane starts at 48: black row UV = (128, 128)
        Assert.Equal(128, nv12[48]);
        Assert.Equal(128, nv12[49]);
        // Red row UV (row 2 of UV plane = 48 + 16 = 64): V > 200, U < 110
        Assert.True(nv12[64] < 110);
        Assert.True(nv12[65] > 200);
    }
}
