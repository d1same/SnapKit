using System.IO;
using System.Runtime.InteropServices;

namespace SnapKit;

/// <summary>
/// Zero-dependency H.264 MP4 (+ optional AAC stereo audio) writer built directly on
/// Windows Media Foundation (mfplat.dll + mfreadwrite.dll) using native top-down NV12 frames.
/// Must be created, written to, and finalized on MTA threads (never WPF STA).
/// </summary>
internal sealed class Mp4SinkWriter : IDisposable
{
    private readonly object _gate = new();
    private readonly int _width;
    private readonly int _height;
    private readonly int _fps;
    private readonly byte[] _nv12Scratch;
    private IMFSinkWriter? _writer;
    private uint _videoStream;
    private uint _audioStream;
    private bool _hasAudio;
    private bool _started;
    private bool _finalized;
    private bool _mfActive;
    private long _lastVideoTs = -1;

    public Mp4SinkWriter(string outputPath, int width, int height, int fps, bool enableAudio)
    {
        _width = Math.Max(16, width & ~1);
        _height = Math.Max(16, height & ~1);
        _fps = Math.Clamp(fps, 5, 60);
        _nv12Scratch = new byte[Nv12Convert.BufferSize(_width, _height)];

        Check(MFStartup(MfVersion, MfStartupNoSocket), "MFStartup");
        _mfActive = true;

        try
        {
            // Try hardware transforms first, then fall back cleanly to software H.264, then video-only if AAC fails.
            if (!TryInitialize(outputPath, enableAudio, hardwareTransforms: true, out var firstErr))
            {
                if (!TryInitialize(outputPath, enableAudio, hardwareTransforms: false, out var secondErr))
                {
                    if (enableAudio && TryInitialize(outputPath, enableAudio: false, hardwareTransforms: false, out _))
                    {
                        AudioFallbackNote = "AAC audio encoder unavailable; recorded video without audio.";
                    }
                    else
                    {
                        throw secondErr ?? firstErr ?? new InvalidOperationException("Could not initialize Windows Media Foundation H.264 encoder.");
                    }
                }
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public int Width => _width;
    public int Height => _height;
    public bool HasAudio => _hasAudio;
    public string? AudioFallbackNote { get; private set; }
    public int VideoFramesWritten { get; private set; }

    private bool TryInitialize(string outputPath, bool enableAudio, bool hardwareTransforms, out Exception? error)
    {
        error = null;
        IMFAttributes? attrs = null;
        IMFSinkWriter? writer = null;
        try
        {
            if (File.Exists(outputPath))
                File.Delete(outputPath);

            Check(MFCreateAttributes(out attrs, 2), "MFCreateAttributes");
            Check(attrs.SetGUID(MfTranscodeContainerType, MfTranscodeContainerTypeMpeg4), "SetContainerMpeg4");
            if (hardwareTransforms)
                attrs.SetUINT32(MfReadWriteEnableHardwareTransforms, 1);

            Check(MFCreateSinkWriterFromURL(outputPath, IntPtr.Zero, attrs, out writer), "MFCreateSinkWriterFromURL");
            ConfigureVideoStream(writer);
            if (enableAudio)
                ConfigureAudioStream(writer);

            Check(writer.BeginWriting(), "BeginWriting");
            _writer = writer;
            _hasAudio = enableAudio;
            _started = true;
            writer = null;
            return true;
        }
        catch (Exception ex)
        {
            error = ex;
            ReleaseCom(writer);
            return false;
        }
        finally
        {
            ReleaseCom(attrs);
        }
    }

    public void WriteVideoFrame(PixelBuffer frame, long timestamp100Ns, long duration100Ns)
    {
        lock (_gate)
        {
            if (!_started || _finalized || _writer == null)
                return;

            if (timestamp100Ns <= _lastVideoTs)
                timestamp100Ns = _lastVideoTs + 10_000; // +1ms strictly monotonic
            _lastVideoTs = timestamp100Ns;

            Nv12Convert.FromBgraInto(frame, _width, _height, _nv12Scratch);

            int totalBytes = _nv12Scratch.Length;
            IMFMediaBuffer? buffer = null;
            IMFSample? sample = null;
            try
            {
                Check(MFCreateMemoryBuffer((uint)totalBytes, out buffer), "MFCreateMemoryBuffer(video)");
                Check(buffer.Lock(out IntPtr dest, out _, out _), "Lock(video)");
                try
                {
                    Marshal.Copy(_nv12Scratch, 0, dest, totalBytes);
                }
                finally
                {
                    buffer.Unlock();
                }

                Check(buffer.SetCurrentLength((uint)totalBytes), "SetCurrentLength(video)");
                Check(MFCreateSample(out sample), "MFCreateSample(video)");
                Check(sample.AddBuffer(buffer), "AddBuffer(video)");
                Check(sample.SetSampleTime(timestamp100Ns), "SetSampleTime(video)");
                Check(sample.SetSampleDuration(Math.Max(10_000, duration100Ns)), "SetSampleDuration(video)");
                Check(_writer.WriteSample(_videoStream, sample), "WriteSample(video)");
                VideoFramesWritten++;
            }
            finally
            {
                ReleaseCom(sample);
                ReleaseCom(buffer);
            }
        }
    }

    public void WriteAudioPcm(byte[] pcmBytes, long timestamp100Ns, long duration100Ns)
    {
        if (!_hasAudio || pcmBytes.Length == 0)
            return;

        lock (_gate)
        {
            if (!_started || _finalized || _writer == null)
                return;

            IMFMediaBuffer? buffer = null;
            IMFSample? sample = null;
            try
            {
                Check(MFCreateMemoryBuffer((uint)pcmBytes.Length, out buffer), "MFCreateMemoryBuffer(audio)");
                Check(buffer.Lock(out IntPtr dest, out _, out _), "Lock(audio)");
                try
                {
                    Marshal.Copy(pcmBytes, 0, dest, pcmBytes.Length);
                }
                finally
                {
                    buffer.Unlock();
                }

                Check(buffer.SetCurrentLength((uint)pcmBytes.Length), "SetCurrentLength(audio)");
                Check(MFCreateSample(out sample), "MFCreateSample(audio)");
                Check(sample.AddBuffer(buffer), "AddBuffer(audio)");
                Check(sample.SetSampleTime(timestamp100Ns), "SetSampleTime(audio)");
                Check(sample.SetSampleDuration(Math.Max(10_000, duration100Ns)), "SetSampleDuration(audio)");
                Check(_writer.WriteSample(_audioStream, sample), "WriteSample(audio)");
            }
            finally
            {
                ReleaseCom(sample);
                ReleaseCom(buffer);
            }
        }
    }

    public void Finish()
    {
        lock (_gate)
        {
            if (_finalized || !_started || _writer == null)
                return;
            _finalized = true;
            if (VideoFramesWritten > 0)
                Check(_writer.Finalize(), "Finalize");
        }
    }

    public void Dispose()
    {
        try
        {
            Finish();
        }
        catch
        {
            // Dispose must not throw if Finish already failed or caller is unwinding.
        }
        lock (_gate)
        {
            ReleaseCom(_writer);
            _writer = null;
            if (_mfActive)
            {
                _mfActive = false;
                try { MFShutdown(); } catch { /* ignore */ }
            }
        }
    }

    private void ConfigureVideoStream(IMFSinkWriter writer)
    {
        IMFMediaType? outType = null;
        IMFMediaType? inType = null;
        try
        {
            uint bitrate = (uint)Math.Clamp((long)_width * _height * 6, 1_500_000, 12_000_000);

            Check(MFCreateMediaType(out outType), "MFCreateMediaType(video out)");
            Check(outType.SetGUID(MfMtMajorType, MfMediaTypeVideo), "SetMajorType(video out)");
            Check(outType.SetGUID(MfMtSubtype, MfVideoFormatH264), "SetSubtype(H264)");
            Check(outType.SetUINT32(MfMtAvgBitrate, bitrate), "SetAvgBitrate");
            Check(outType.SetUINT32(MfMtInterlaceMode, 2), "SetInterlaceMode"); // MFVideoInterlace_Progressive
            Check(outType.SetUINT64(MfMtFrameSize, Pack(_width, _height)), "SetFrameSize(out)");
            Check(outType.SetUINT64(MfMtFrameRate, Pack(_fps, 1)), "SetFrameRate(out)");
            Check(outType.SetUINT64(MfMtPixelAspectRatio, Pack(1, 1)), "SetPixelAspectRatio(out)");
            outType.SetUINT32(MfMtMpeg2Profile, 77); // Main profile
            Check(writer.AddStream(outType, out _videoStream), "AddStream(video)");

            Check(MFCreateMediaType(out inType), "MFCreateMediaType(video in)");
            Check(inType.SetGUID(MfMtMajorType, MfMediaTypeVideo), "SetMajorType(video in)");
            Check(inType.SetGUID(MfMtSubtype, MfVideoFormatNv12), "SetSubtype(NV12)");
            Check(inType.SetUINT32(MfMtInterlaceMode, 2), "SetInterlaceMode(in)");
            Check(inType.SetUINT64(MfMtFrameSize, Pack(_width, _height)), "SetFrameSize(in)");
            Check(inType.SetUINT64(MfMtFrameRate, Pack(_fps, 1)), "SetFrameRate(in)");
            Check(inType.SetUINT64(MfMtPixelAspectRatio, Pack(1, 1)), "SetPixelAspectRatio(in)");
            Check(writer.SetInputMediaType(_videoStream, inType, null), "SetInputMediaType(video)");
        }
        finally
        {
            ReleaseCom(inType);
            ReleaseCom(outType);
        }
    }

    private void ConfigureAudioStream(IMFSinkWriter writer)
    {
        IMFMediaType? outType = null;
        IMFMediaType? inType = null;
        try
        {
            Check(MFCreateMediaType(out outType), "MFCreateMediaType(audio out)");
            Check(outType.SetGUID(MfMtMajorType, MfMediaTypeAudio), "SetMajorType(audio out)");
            Check(outType.SetGUID(MfMtSubtype, MfAudioFormatAac), "SetSubtype(AAC)");
            Check(outType.SetUINT32(MfMtAudioBitsPerSample, AudioMixer.BitsPerSample), "SetAudioBits(out)");
            Check(outType.SetUINT32(MfMtAudioSamplesPerSecond, AudioMixer.SampleRate), "SetAudioRate(out)");
            Check(outType.SetUINT32(MfMtAudioNumChannels, AudioMixer.Channels), "SetAudioChannels(out)");
            Check(outType.SetUINT32(MfMtAudioAvgBytesPerSecond, 16000), "SetAudioBitrate(out)"); // 128 kbps
            outType.SetUINT32(MfMtAacPayloadType, 0);
            outType.SetUINT32(MfMtAacAudioProfileLevelIndication, 0x29);
            Check(writer.AddStream(outType, out _audioStream), "AddStream(audio)");

            Check(MFCreateMediaType(out inType), "MFCreateMediaType(audio in)");
            Check(inType.SetGUID(MfMtMajorType, MfMediaTypeAudio), "SetMajorType(audio in)");
            Check(inType.SetGUID(MfMtSubtype, MfAudioFormatPcm), "SetSubtype(PCM)");
            Check(inType.SetUINT32(MfMtAudioBitsPerSample, AudioMixer.BitsPerSample), "SetAudioBits(in)");
            Check(inType.SetUINT32(MfMtAudioSamplesPerSecond, AudioMixer.SampleRate), "SetAudioRate(in)");
            Check(inType.SetUINT32(MfMtAudioNumChannels, AudioMixer.Channels), "SetAudioChannels(in)");
            Check(inType.SetUINT32(MfMtAudioBlockAlignment, AudioMixer.BlockAlign), "SetAudioBlockAlign(in)");
            Check(inType.SetUINT32(MfMtAudioAvgBytesPerSecond, AudioMixer.BytesPerSecond), "SetAudioAvgBytes(in)");
            Check(writer.SetInputMediaType(_audioStream, inType, null), "SetInputMediaType(audio)");
        }
        finally
        {
            ReleaseCom(inType);
            ReleaseCom(outType);
        }
    }

    private static ulong Pack(int high, int low) => ((ulong)(uint)high << 32) | (uint)low;

    private static void Check(int hr, string operation)
    {
        if (hr < 0)
            throw new COMException($"Media Foundation {operation} failed (0x{hr:X8}).", hr);
    }

    private static void ReleaseCom(object? obj)
    {
        if (obj != null && Marshal.IsComObject(obj))
            Marshal.ReleaseComObject(obj);
    }

    private const uint MfVersion = 0x00020070;
    private const uint MfStartupNoSocket = 1;

    private static readonly Guid MfMediaTypeVideo = new("73646976-0000-0010-8000-00AA00389B71");
    private static readonly Guid MfMediaTypeAudio = new("73647561-0000-0010-8000-00AA00389B71");
    private static readonly Guid MfVideoFormatH264 = new("34363248-0000-0010-8000-00AA00389B71");
    private static readonly Guid MfVideoFormatNv12 = new("3231564E-0000-0010-8000-00AA00389B71");
    private static readonly Guid MfAudioFormatAac = new("00001610-0000-0010-8000-00AA00389B71");
    private static readonly Guid MfAudioFormatPcm = new("00000001-0000-0010-8000-00AA00389B71");

    private static readonly Guid MfTranscodeContainerType = new("150ff23f-4abc-478b-ac4f-e1916fba1cca");
    private static readonly Guid MfTranscodeContainerTypeMpeg4 = new("dc6cd05d-b9d0-40ef-bd35-fa622c1ab28a");

    private static readonly Guid MfMtMajorType = new("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
    private static readonly Guid MfMtSubtype = new("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
    private static readonly Guid MfMtAvgBitrate = new("20332624-fb0d-4d9e-bd0d-cbf6786c102e");
    private static readonly Guid MfMtInterlaceMode = new("e2724bb8-e676-4806-b4b2-a8d6efb44ccd");
    private static readonly Guid MfMtFrameSize = new("1652c33d-d6b2-4012-b834-72030849a37d");
    private static readonly Guid MfMtFrameRate = new("c459a2e8-3d2c-4e44-b132-fee5156c7bb0");
    private static readonly Guid MfMtPixelAspectRatio = new("c6376a1e-8d0a-4027-be45-6d9a0ad39bb6");
    private static readonly Guid MfMtMpeg2Profile = new("ad76a80b-2d5c-4e0b-b375-64e520137036");

    private static readonly Guid MfMtAudioNumChannels = new("37e48bf5-645e-4c5b-89de-ada9e29b696a");
    private static readonly Guid MfMtAudioSamplesPerSecond = new("5faeeae7-0290-4c31-9e8a-c534f68d9dba");
    private static readonly Guid MfMtAudioAvgBytesPerSecond = new("1aab75c8-cfef-451c-ab95-ac034b8e1731");
    private static readonly Guid MfMtAudioBlockAlignment = new("322de230-9eeb-43bd-ab7a-ff412251541d");
    private static readonly Guid MfMtAudioBitsPerSample = new("f2deb57f-40fa-4764-aa33-ed4f2d1ff669");
    private static readonly Guid MfMtAacPayloadType = new("bfbabe79-7434-4d1c-94f0-72a3b9e17188");
    private static readonly Guid MfMtAacAudioProfileLevelIndication = new("7632f0e6-9538-4d61-acda-ea29c8c14456");

    private static readonly Guid MfReadWriteEnableHardwareTransforms = new("a634a91c-822b-41b9-a494-4de4643612b0");

    [DllImport("mfplat.dll", ExactSpelling = true)]
    private static extern int MFStartup(uint version, uint flags);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    private static extern int MFShutdown();

    [DllImport("mfplat.dll", ExactSpelling = true)]
    private static extern int MFCreateAttributes(out IMFAttributes attributes, uint initialSize);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    private static extern int MFCreateMediaType(out IMFMediaType mediaType);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    private static extern int MFCreateMemoryBuffer(uint maxLength, out IMFMediaBuffer buffer);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    private static extern int MFCreateSample(out IMFSample sample);

    [DllImport("mfreadwrite.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int MFCreateSinkWriterFromURL(
        [MarshalAs(UnmanagedType.LPWStr)] string outputUrl,
        IntPtr byteStream,
        IMFAttributes? attributes,
        out IMFSinkWriter sinkWriter);

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("2cd2d921-c447-44a7-a13c-4adabfc247e3")]
    private interface IMFAttributes
    {
        [PreserveSig] int GetItem(in Guid guidKey, IntPtr value);
        [PreserveSig] int GetItemType(in Guid guidKey, out int type);
        [PreserveSig] int CompareItem(in Guid guidKey, IntPtr value, [MarshalAs(UnmanagedType.Bool)] out bool result);
        [PreserveSig] int Compare(IMFAttributes theirs, int matchType, [MarshalAs(UnmanagedType.Bool)] out bool result);
        [PreserveSig] int GetUINT32(in Guid guidKey, out uint value);
        [PreserveSig] int GetUINT64(in Guid guidKey, out ulong value);
        [PreserveSig] int GetDouble(in Guid guidKey, out double value);
        [PreserveSig] int GetGUID(in Guid guidKey, out Guid value);
        [PreserveSig] int GetStringLength(in Guid guidKey, out uint length);
        [PreserveSig] int GetString(in Guid guidKey, IntPtr value, uint bufSize, out uint length);
        [PreserveSig] int GetAllocatedString(in Guid guidKey, out IntPtr value, out uint length);
        [PreserveSig] int GetBlobSize(in Guid guidKey, out uint blobSize);
        [PreserveSig] int GetBlob(in Guid guidKey, IntPtr buf, uint bufSize, out uint blobSize);
        [PreserveSig] int GetAllocatedBlob(in Guid guidKey, out IntPtr buf, out uint size);
        [PreserveSig] int GetUnknown(in Guid guidKey, in Guid riid, out IntPtr value);
        [PreserveSig] int SetItem(in Guid guidKey, IntPtr value);
        [PreserveSig] int DeleteItem(in Guid guidKey);
        [PreserveSig] int DeleteAllItems();
        [PreserveSig] int SetUINT32(in Guid guidKey, uint value);
        [PreserveSig] int SetUINT64(in Guid guidKey, ulong value);
        [PreserveSig] int SetDouble(in Guid guidKey, double value);
        [PreserveSig] int SetGUID(in Guid guidKey, in Guid value);
        [PreserveSig] int SetString(in Guid guidKey, [MarshalAs(UnmanagedType.LPWStr)] string value);
        [PreserveSig] int SetBlob(in Guid guidKey, IntPtr buf, uint bufSize);
        [PreserveSig] int SetUnknown(in Guid guidKey, [MarshalAs(UnmanagedType.IUnknown)] object value);
        [PreserveSig] int LockStore();
        [PreserveSig] int UnlockStore();
        [PreserveSig] int GetCount(out uint items);
        [PreserveSig] int GetItemByIndex(uint index, out Guid guidKey, IntPtr value);
        [PreserveSig] int CopyAllItems(IMFAttributes dest);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("44ae0fa8-ea31-4109-8d2e-4cae4997c555")]
    private interface IMFMediaType
    {
        // IMFAttributes (30 slots)
        [PreserveSig] int GetItem(in Guid guidKey, IntPtr value);
        [PreserveSig] int GetItemType(in Guid guidKey, out int type);
        [PreserveSig] int CompareItem(in Guid guidKey, IntPtr value, [MarshalAs(UnmanagedType.Bool)] out bool result);
        [PreserveSig] int Compare(IMFAttributes theirs, int matchType, [MarshalAs(UnmanagedType.Bool)] out bool result);
        [PreserveSig] int GetUINT32(in Guid guidKey, out uint value);
        [PreserveSig] int GetUINT64(in Guid guidKey, out ulong value);
        [PreserveSig] int GetDouble(in Guid guidKey, out double value);
        [PreserveSig] int GetGUID(in Guid guidKey, out Guid value);
        [PreserveSig] int GetStringLength(in Guid guidKey, out uint length);
        [PreserveSig] int GetString(in Guid guidKey, IntPtr value, uint bufSize, out uint length);
        [PreserveSig] int GetAllocatedString(in Guid guidKey, out IntPtr value, out uint length);
        [PreserveSig] int GetBlobSize(in Guid guidKey, out uint blobSize);
        [PreserveSig] int GetBlob(in Guid guidKey, IntPtr buf, uint bufSize, out uint blobSize);
        [PreserveSig] int GetAllocatedBlob(in Guid guidKey, out IntPtr buf, out uint size);
        [PreserveSig] int GetUnknown(in Guid guidKey, in Guid riid, out IntPtr value);
        [PreserveSig] int SetItem(in Guid guidKey, IntPtr value);
        [PreserveSig] int DeleteItem(in Guid guidKey);
        [PreserveSig] int DeleteAllItems();
        [PreserveSig] int SetUINT32(in Guid guidKey, uint value);
        [PreserveSig] int SetUINT64(in Guid guidKey, ulong value);
        [PreserveSig] int SetDouble(in Guid guidKey, double value);
        [PreserveSig] int SetGUID(in Guid guidKey, in Guid value);
        [PreserveSig] int SetString(in Guid guidKey, [MarshalAs(UnmanagedType.LPWStr)] string value);
        [PreserveSig] int SetBlob(in Guid guidKey, IntPtr buf, uint bufSize);
        [PreserveSig] int SetUnknown(in Guid guidKey, [MarshalAs(UnmanagedType.IUnknown)] object value);
        [PreserveSig] int LockStore();
        [PreserveSig] int UnlockStore();
        [PreserveSig] int GetCount(out uint items);
        [PreserveSig] int GetItemByIndex(uint index, out Guid guidKey, IntPtr value);
        [PreserveSig] int CopyAllItems(IMFAttributes dest);
        // IMFMediaType
        [PreserveSig] int GetMajorType(out Guid majorType);
        [PreserveSig] int IsCompressedFormat([MarshalAs(UnmanagedType.Bool)] out bool compressed);
        [PreserveSig] int IsEqual(IMFMediaType other, out uint flags);
        [PreserveSig] int GetRepresentation(Guid guidRepresentation, out IntPtr representation);
        [PreserveSig] int FreeRepresentation(Guid guidRepresentation, IntPtr representation);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("045FA593-8799-42b8-BC8D-8968C6453507")]
    private interface IMFMediaBuffer
    {
        [PreserveSig] int Lock(out IntPtr buffer, out uint maxLength, out uint currentLength);
        [PreserveSig] int Unlock();
        [PreserveSig] int GetCurrentLength(out uint currentLength);
        [PreserveSig] int SetCurrentLength(uint currentLength);
        [PreserveSig] int GetMaxLength(out uint maxLength);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("c40a00f2-b93a-4d80-ae8c-5a1c634f58e4")]
    private interface IMFSample
    {
        // IMFAttributes (30 slots)
        [PreserveSig] int GetItem(in Guid guidKey, IntPtr value);
        [PreserveSig] int GetItemType(in Guid guidKey, out int type);
        [PreserveSig] int CompareItem(in Guid guidKey, IntPtr value, [MarshalAs(UnmanagedType.Bool)] out bool result);
        [PreserveSig] int Compare(IMFAttributes theirs, int matchType, [MarshalAs(UnmanagedType.Bool)] out bool result);
        [PreserveSig] int GetUINT32(in Guid guidKey, out uint value);
        [PreserveSig] int GetUINT64(in Guid guidKey, out ulong value);
        [PreserveSig] int GetDouble(in Guid guidKey, out double value);
        [PreserveSig] int GetGUID(in Guid guidKey, out Guid value);
        [PreserveSig] int GetStringLength(in Guid guidKey, out uint length);
        [PreserveSig] int GetString(in Guid guidKey, IntPtr value, uint bufSize, out uint length);
        [PreserveSig] int GetAllocatedString(in Guid guidKey, out IntPtr value, out uint length);
        [PreserveSig] int GetBlobSize(in Guid guidKey, out uint blobSize);
        [PreserveSig] int GetBlob(in Guid guidKey, IntPtr buf, uint bufSize, out uint blobSize);
        [PreserveSig] int GetAllocatedBlob(in Guid guidKey, out IntPtr buf, out uint size);
        [PreserveSig] int GetUnknown(in Guid guidKey, in Guid riid, out IntPtr value);
        [PreserveSig] int SetItem(in Guid guidKey, IntPtr value);
        [PreserveSig] int DeleteItem(in Guid guidKey);
        [PreserveSig] int DeleteAllItems();
        [PreserveSig] int SetUINT32(in Guid guidKey, uint value);
        [PreserveSig] int SetUINT64(in Guid guidKey, ulong value);
        [PreserveSig] int SetDouble(in Guid guidKey, double value);
        [PreserveSig] int SetGUID(in Guid guidKey, in Guid value);
        [PreserveSig] int SetString(in Guid guidKey, [MarshalAs(UnmanagedType.LPWStr)] string value);
        [PreserveSig] int SetBlob(in Guid guidKey, IntPtr buf, uint bufSize);
        [PreserveSig] int SetUnknown(in Guid guidKey, [MarshalAs(UnmanagedType.IUnknown)] object value);
        [PreserveSig] int LockStore();
        [PreserveSig] int UnlockStore();
        [PreserveSig] int GetCount(out uint items);
        [PreserveSig] int GetItemByIndex(uint index, out Guid guidKey, IntPtr value);
        [PreserveSig] int CopyAllItems(IMFAttributes dest);
        // IMFSample
        [PreserveSig] int GetSampleFlags(out uint flags);
        [PreserveSig] int SetSampleFlags(uint flags);
        [PreserveSig] int GetSampleTime(out long sampleTime);
        [PreserveSig] int SetSampleTime(long sampleTime);
        [PreserveSig] int GetSampleDuration(out long sampleDuration);
        [PreserveSig] int SetSampleDuration(long sampleDuration);
        [PreserveSig] int GetBufferCount(out uint count);
        [PreserveSig] int GetBufferByIndex(uint index, out IMFMediaBuffer buffer);
        [PreserveSig] int ConvertToContiguousBuffer(out IMFMediaBuffer buffer);
        [PreserveSig] int AddBuffer(IMFMediaBuffer buffer);
        [PreserveSig] int RemoveBufferByIndex(uint index);
        [PreserveSig] int RemoveAllBuffers();
        [PreserveSig] int GetTotalLength(out uint totalLength);
        [PreserveSig] int CopyToBuffer(IMFMediaBuffer buffer);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("3137f1cd-fe5e-4805-a5d8-fb477448cb3d")]
    private interface IMFSinkWriter
    {
        [PreserveSig] int AddStream(IMFMediaType targetMediaType, out uint streamIndex);
        [PreserveSig] int SetInputMediaType(uint streamIndex, IMFMediaType inputMediaType, IMFAttributes? encodingParameters);
        [PreserveSig] int BeginWriting();
        [PreserveSig] int WriteSample(uint streamIndex, IMFSample sample);
        [PreserveSig] int SendStreamTick(uint streamIndex, long timestamp);
        [PreserveSig] int PlaceMarker(int markerType, IntPtr markerValue, IntPtr contextValue);
        [PreserveSig] int NotifyEndOfSegment(uint streamIndex);
        [PreserveSig] int Flush(uint streamIndex);
        [PreserveSig] int Finalize();
        [PreserveSig] int GetServiceForStream(uint streamIndex, in Guid guidService, in Guid riid, out IntPtr obj);
        [PreserveSig] int GetStatistics(uint streamIndex, IntPtr stats);
    }
}
