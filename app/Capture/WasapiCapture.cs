using System.Runtime.InteropServices;

namespace SnapKit;

/// <summary>
/// Zero-dependency Windows WASAPI capture stream supporting both Microphone (eCapture)
/// and System Audio Loopback (eRender + AUDCLNT_STREAMFLAGS_LOOPBACK), letting Windows
/// auto-convert to 48 kHz stereo 16-bit PCM.
/// </summary>
internal sealed class WasapiCapture : IDisposable
{
    private readonly object _lock = new();
    private readonly List<byte> _queue = new(AudioMixer.BytesPerSecond);
    private IAudioClient? _client;
    private IAudioCaptureClient? _capture;
    private bool _running;
    private float _level;

    public float PeakLevel => Volatile.Read(ref _level);

    private WasapiCapture(IAudioClient client, IAudioCaptureClient capture)
    {
        _client = client;
        _capture = capture;
    }

    public static WasapiCapture? TryCreate(bool loopback, out string? error)
    {
        error = null;
        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? device = null;
        IAudioClient? client = null;
        IAudioCaptureClient? capture = null;
        IntPtr formatPtr = IntPtr.Zero;

        try
        {
            var clsid = new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E");
            var type = Type.GetTypeFromCLSID(clsid);
            if (type == null)
            {
                error = "WASAPI audio enumerator unavailable.";
                return null;
            }
            enumerator = (IMMDeviceEnumerator?)Activator.CreateInstance(type);
            if (enumerator == null)
            {
                error = "Could not initialize audio enumerator.";
                return null;
            }

            // eRender = 0 (for loopback), eCapture = 1 (for mic); eMultimedia = 1
            int dataFlow = loopback ? 0 : 1;
            int hr = enumerator.GetDefaultAudioEndpoint(dataFlow, 1, out device);
            if (hr < 0 || device == null)
                hr = enumerator.GetDefaultAudioEndpoint(dataFlow, 0, out device);
            if (hr < 0 || device == null)
            {
                error = loopback ? "No system audio output device found." : "No microphone found.";
                return null;
            }

            Guid iidClient = new("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2");
            hr = device.Activate(ref iidClient, 0x17, IntPtr.Zero, out object clientObj);
            if (hr < 0)
            {
                error = "Could not activate audio client.";
                return null;
            }
            client = (IAudioClient)clientObj;

            var format = new WaveFormatEx
            {
                FormatTag = 1, // WAVE_FORMAT_PCM
                Channels = (ushort)AudioMixer.Channels,
                SamplesPerSec = (uint)AudioMixer.SampleRate,
                AvgBytesPerSec = (uint)AudioMixer.BytesPerSecond,
                BlockAlign = (ushort)AudioMixer.BlockAlign,
                BitsPerSample = (ushort)AudioMixer.BitsPerSample,
                ExtraSize = 0
            };
            formatPtr = Marshal.AllocHGlobal(Marshal.SizeOf<WaveFormatEx>());
            Marshal.StructureToPtr(format, formatPtr, false);

            // AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM | AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY
            uint flags = 0x80000000u | 0x08000000u;
            if (loopback)
                flags |= 0x00020000u; // AUDCLNT_STREAMFLAGS_LOOPBACK

            // Shared mode (0), 200ms buffer (2,000,000 * 100ns)
            hr = client.Initialize(0, flags, 2_000_000, 0, formatPtr, IntPtr.Zero);
            if (hr < 0)
            {
                error = loopback ? "System audio loopback unavailable." : "Microphone capture unavailable.";
                ReleaseCom(client);
                return null;
            }

            Guid iidCapture = new("C8ADBD64-E71E-48a0-A4DE-185C395CD317");
            hr = client.GetService(ref iidCapture, out object captureObj);
            if (hr < 0)
            {
                error = "Could not acquire WASAPI capture service.";
                ReleaseCom(client);
                return null;
            }
            capture = (IAudioCaptureClient)captureObj;

            var instance = new WasapiCapture(client, capture);
            client = null;
            capture = null;
            return instance;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            ReleaseCom(capture);
            ReleaseCom(client);
            return null;
        }
        finally
        {
            if (formatPtr != IntPtr.Zero)
                Marshal.FreeHGlobal(formatPtr);
            ReleaseCom(device);
            ReleaseCom(enumerator);
        }
    }

    public void Start()
    {
        if (_client == null || _running)
            return;
        if (_client.Start() >= 0)
            _running = true;
    }

    public void Pump()
    {
        if (!_running || _capture == null)
            return;
        try
        {
            while (_capture.GetNextPacketSize(out uint packetFrames) >= 0 && packetFrames > 0)
            {
                if (_capture.GetBuffer(out IntPtr data, out uint frames, out uint flags, out _, out _) < 0)
                    break;
                try
                {
                    int byteCount = (int)frames * AudioMixer.BlockAlign;
                    if (byteCount > 0)
                    {
                        var chunk = new byte[byteCount];
                        bool silent = (flags & 0x2u) != 0; // AUDCLNT_BUFFERFLAGS_SILENT
                        if (!silent && data != IntPtr.Zero)
                        {
                            Marshal.Copy(data, chunk, 0, byteCount);
                            int peak = 0;
                            for (int i = 0; i + 1 < byteCount; i += 8)
                            {
                                short sample = (short)(chunk[i] | (chunk[i + 1] << 8));
                                int mag = sample == short.MinValue ? 32767 : Math.Abs(sample);
                                if (mag > peak) peak = mag;
                            }
                            float norm = Math.Clamp(peak / 24000f, 0f, 1f);
                            float prev = Volatile.Read(ref _level);
                            Volatile.Write(ref _level, Math.Max(norm, prev * 0.65f));
                        }
                        else
                        {
                            Volatile.Write(ref _level, Volatile.Read(ref _level) * 0.5f);
                        }
                        lock (_lock)
                        {
                            // Prevent unbounded lag if writer falls behind
                            if (_queue.Count > AudioMixer.BytesPerSecond * 2)
                                _queue.RemoveRange(0, _queue.Count - AudioMixer.BytesPerSecond);
                            _queue.AddRange(chunk);
                        }
                    }
                }
                finally
                {
                    _capture.ReleaseBuffer(frames);
                }
            }
        }
        catch
        {
            // Device unplugged or endpoint changed; return silence cleanly.
        }
    }

    public void DiscardPending()
    {
        Pump();
        lock (_lock)
            _queue.Clear();
    }

    public byte[] ReadChunk(int maxBytes)
    {
        Pump();
        lock (_lock)
        {
            int take = Math.Min(_queue.Count, (maxBytes / AudioMixer.BlockAlign) * AudioMixer.BlockAlign);
            if (take <= 0)
                return Array.Empty<byte>();
            var slice = _queue.GetRange(0, take).ToArray();
            _queue.RemoveRange(0, take);
            return slice;
        }
    }

    public void Dispose()
    {
        if (_running && _client != null)
        {
            try { _client.Stop(); } catch { /* ignore */ }
            _running = false;
        }
        ReleaseCom(_capture);
        ReleaseCom(_client);
        _capture = null;
        _client = null;
    }

    private static void ReleaseCom(object? obj)
    {
        if (obj != null && Marshal.IsComObject(obj))
            Marshal.ReleaseComObject(obj);
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct WaveFormatEx
    {
        public ushort FormatTag;
        public ushort Channels;
        public uint SamplesPerSec;
        public uint AvgBytesPerSec;
        public ushort BlockAlign;
        public ushort BitsPerSample;
        public ushort ExtraSize;
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, uint stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr client);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr client);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, uint clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2")]
    private interface IAudioClient
    {
        [PreserveSig] int Initialize(int shareMode, uint streamFlags, long bufferDuration, long periodicity, IntPtr format, IntPtr audioSessionGuid);
        [PreserveSig] int GetBufferSize(out uint numBufferFrames);
        [PreserveSig] int GetStreamLatency(out long latency);
        [PreserveSig] int GetCurrentPadding(out uint numPaddingFrames);
        [PreserveSig] int IsFormatSupported(int shareMode, IntPtr format, out IntPtr closestMatch);
        [PreserveSig] int GetMixFormat(out IntPtr deviceFormat);
        [PreserveSig] int GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);
        [PreserveSig] int Start();
        [PreserveSig] int Stop();
        [PreserveSig] int Reset();
        [PreserveSig] int SetEventHandle(IntPtr eventHandle);
        [PreserveSig] int GetService(ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317")]
    private interface IAudioCaptureClient
    {
        [PreserveSig] int GetBuffer(out IntPtr data, out uint numFramesToRead, out uint flags, out ulong devicePosition, out ulong qpcPosition);
        [PreserveSig] int ReleaseBuffer(uint numFramesRead);
        [PreserveSig] int GetNextPacketSize(out uint numFramesInNextPacket);
    }
}
