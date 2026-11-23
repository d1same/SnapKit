using System.Runtime.InteropServices;

namespace SnapKit;

internal enum DecodedSampleKind
{
    EndOfStream,
    Video,
    Audio
}

internal readonly record struct DecodedSample(
    DecodedSampleKind Kind,
    long Timestamp100Ns,
    long Duration100Ns,
    PixelBuffer? VideoFrame,
    byte[]? AudioPcm);

/// <summary>
/// Zero-dependency Windows Media Foundation MP4 reader (mfplat.dll + mfreadwrite.dll)
/// that decodes H.264 video frames to top-down BGRA32 PixelBuffers and optional AAC audio
/// to 48 kHz 16-bit stereo PCM. Must be created and used on MTA threads (never WPF STA).
/// </summary>
internal sealed class Mp4SourceReader : IDisposable
{
    private IMFSourceReader? _reader;
    private bool _mfActive;
    private uint _videoStream = uint.MaxValue;
    private uint _audioStream = uint.MaxValue;
    private int _videoStride;

    public Mp4SourceReader(string inputPath)
    {
        Check(MFStartup(MfVersion, MfStartupNoSocket), "MFStartup");
        _mfActive = true;

        IMFAttributes? attrs = null;
        try
        {
            Check(MFCreateAttributes(out attrs, 1), "MFCreateAttributes");
            attrs.SetUINT32(MfSourceReaderEnableVideoProcessing, 1);

            Check(MFCreateSourceReaderFromURL(inputPath, attrs, out _reader), "MFCreateSourceReaderFromURL");
            DiscoverStreams();
            if (_videoStream == uint.MaxValue)
                throw new InvalidOperationException("No video stream found in MP4 file.");

            ConfigureVideoOutput();
            if (_audioStream != uint.MaxValue)
                HasAudio = TryConfigureAudioOutput();
        }
        catch
        {
            Dispose();
            throw;
        }
        finally
        {
            ReleaseCom(attrs);
        }
    }

    public int Width { get; private set; }
    public int Height { get; private set; }
    public int Fps { get; private set; } = 30;
    public bool HasAudio { get; private set; }

    public DecodedSample ReadNext()
    {
        if (_reader == null)
            return new DecodedSample(DecodedSampleKind.EndOfStream, 0, 0, null, null);

        while (true)
        {
            Check(_reader.ReadSample(
                MfSourceReaderAnyStream,
                0,
                out uint streamIndex,
                out uint flags,
                out long timestamp,
                out IMFSample? sample), "ReadSample");

            try
            {
                if ((flags & MfSourceReaderFlagCurrentMediaTypeChanged) != 0 && streamIndex == _videoStream)
                    RefreshVideoGeometry();

                if ((flags & MfSourceReaderFlagEndOfStream) != 0)
                    return new DecodedSample(DecodedSampleKind.EndOfStream, timestamp, 0, null, null);

                if (sample == null)
                    continue;

                long duration = 0;
                sample.GetSampleDuration(out duration);

                if (streamIndex == _videoStream)
                {
                    var frame = ExtractVideoFrame(sample);
                    if (frame != null)
                        return new DecodedSample(DecodedSampleKind.Video, timestamp, duration, frame, null);
                }
                else if (HasAudio && streamIndex == _audioStream)
                {
                    var pcm = ExtractAudioBytes(sample);
                    if (pcm is { Length: > 0 })
                        return new DecodedSample(DecodedSampleKind.Audio, timestamp, duration, null, pcm);
                }
            }
            finally
            {
                ReleaseCom(sample);
            }
        }
    }

    public void Dispose()
    {
        ReleaseCom(_reader);
        _reader = null;
        if (_mfActive)
        {
            _mfActive = false;
            try { MFShutdown(); } catch { /* ignore */ }
        }
    }

    private void DiscoverStreams()
    {
        for (uint i = 0; i < 8; i++)
        {
            int hr = _reader!.GetNativeMediaType(i, 0, out var nativeType);
            if (hr < 0 || nativeType == null)
                break;
            try
            {
                if (nativeType.GetMajorType(out var major) >= 0)
                {
                    if (major == MfMediaTypeVideo && _videoStream == uint.MaxValue)
                        _videoStream = i;
                    else if (major == MfMediaTypeAudio && _audioStream == uint.MaxValue)
                        _audioStream = i;
                }
            }
            finally
            {
                ReleaseCom(nativeType);
            }
        }
    }

    private void ConfigureVideoOutput()
    {
        IMFMediaType? rgbType = null;
        try
        {
            Check(_reader!.SetStreamSelection(MfSourceReaderAllStreams, false), "DeselectAllStreams");
            Check(_reader.SetStreamSelection(_videoStream, true), "SelectVideoStream");

            Check(MFCreateMediaType(out rgbType), "MFCreateMediaType(rgb32)");
            Check(rgbType.SetGUID(MfMtMajorType, MfMediaTypeVideo), "SetMajorType(video)");
            Check(rgbType.SetGUID(MfMtSubtype, MfVideoFormatRgb32), "SetSubtype(RGB32)");
            Check(_reader.SetCurrentMediaType(_videoStream, IntPtr.Zero, rgbType), "SetCurrentMediaType(video)");

            RefreshVideoGeometry();
        }
        finally
        {
            ReleaseCom(rgbType);
        }
    }

    private void RefreshVideoGeometry()
    {
        IMFMediaType? current = null;
        try
        {
            Check(_reader!.GetCurrentMediaType(_videoStream, out current), "GetCurrentMediaType(video)");
            if (current.GetUINT64(MfMtFrameSize, out ulong packedSize) >= 0)
            {
                Width = (int)(packedSize >> 32);
                Height = (int)(packedSize & 0xFFFFFFFF);
            }
            if (current.GetUINT64(MfMtFrameRate, out ulong packedFps) >= 0)
            {
                uint num = (uint)(packedFps >> 32);
                uint den = (uint)(packedFps & 0xFFFFFFFF);
                if (num > 0 && den > 0)
                    Fps = Math.Clamp((int)Math.Round(num / (double)den), 10, 60);
            }
            if (current.GetUINT32(MfMtDefaultStride, out uint rawStride) >= 0)
                _videoStride = unchecked((int)rawStride);
            else
                _videoStride = Width * 4;
        }
        finally
        {
            ReleaseCom(current);
        }
    }

    private bool TryConfigureAudioOutput()
    {
        IMFMediaType? pcmType = null;
        try
        {
            if (_reader!.SetStreamSelection(_audioStream, true) < 0)
                return false;
            Check(MFCreateMediaType(out pcmType), "MFCreateMediaType(pcm)");
            Check(pcmType.SetGUID(MfMtMajorType, MfMediaTypeAudio), "SetMajorType(audio)");
            Check(pcmType.SetGUID(MfMtSubtype, MfAudioFormatPcm), "SetSubtype(PCM)");
            Check(pcmType.SetUINT32(MfMtAudioBitsPerSample, AudioMixer.BitsPerSample), "SetAudioBits");
            Check(pcmType.SetUINT32(MfMtAudioSamplesPerSecond, AudioMixer.SampleRate), "SetAudioRate");
            Check(pcmType.SetUINT32(MfMtAudioNumChannels, AudioMixer.Channels), "SetAudioChannels");
            Check(pcmType.SetUINT32(MfMtAudioBlockAlignment, AudioMixer.BlockAlign), "SetAudioBlockAlign");
            Check(pcmType.SetUINT32(MfMtAudioAvgBytesPerSecond, AudioMixer.BytesPerSecond), "SetAudioAvgBytes");
            if (_reader.SetCurrentMediaType(_audioStream, IntPtr.Zero, pcmType) < 0)
            {
                _reader.SetStreamSelection(_audioStream, false);
                return false;
            }
            return true;
        }
        catch
        {
            try { _reader?.SetStreamSelection(_audioStream, false); } catch { /* ignore */ }
            return false;
        }
        finally
        {
            ReleaseCom(pcmType);
        }
    }

    private PixelBuffer? ExtractVideoFrame(IMFSample sample)
    {
        if (Width <= 0 || Height <= 0)
            return null;
        IMFMediaBuffer? buffer = null;
        try
        {
            if (sample.ConvertToContiguousBuffer(out buffer) < 0 || buffer == null)
                return null;
            if (buffer.Lock(out IntPtr srcPtr, out _, out uint curLen) < 0)
                return null;
            try
            {
                int absStride = _videoStride == 0 ? Width * 4 : Math.Abs(_videoStride);
                if (curLen < (uint)(absStride * Height))
                    absStride = Width * 4;

                var frame = new PixelBuffer(Width, Height);
                int rowCopy = Math.Min(Width * 4, absStride);
                bool bottomUp = _videoStride < 0;
                for (int y = 0; y < Height; y++)
                {
                    int srcRow = bottomUp ? (Height - 1 - y) : y;
                    IntPtr rowPtr = IntPtr.Add(srcPtr, srcRow * absStride);
                    int dstOffset = y * frame.Stride;
                    Marshal.Copy(rowPtr, frame.Pixels, dstOffset, rowCopy);
                    // Ensure opaque alpha for RGB32/XRGB32 decoded frames
                    for (int x = 3; x < rowCopy; x += 4)
                        frame.Pixels[dstOffset + x] = 255;
                }
                return frame;
            }
            finally
            {
                buffer.Unlock();
            }
        }
        finally
        {
            ReleaseCom(buffer);
        }
    }

    private static byte[]? ExtractAudioBytes(IMFSample sample)
    {
        IMFMediaBuffer? buffer = null;
        try
        {
            if (sample.ConvertToContiguousBuffer(out buffer) < 0 || buffer == null)
                return null;
            if (buffer.Lock(out IntPtr srcPtr, out _, out uint curLen) < 0)
                return null;
            try
            {
                if (curLen == 0)
                    return null;
                var bytes = new byte[curLen];
                Marshal.Copy(srcPtr, bytes, 0, (int)curLen);
                return bytes;
            }
            finally
            {
                buffer.Unlock();
            }
        }
        finally
        {
            ReleaseCom(buffer);
        }
    }

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
    private const uint MfSourceReaderAnyStream = 0xFFFFFFFE;
    private const uint MfSourceReaderAllStreams = 0xFFFFFFFE;
    private const uint MfSourceReaderFlagEndOfStream = 0x00000002;
    private const uint MfSourceReaderFlagCurrentMediaTypeChanged = 0x00000020;

    private static readonly Guid MfMediaTypeVideo = new("73646976-0000-0010-8000-00AA00389B71");
    private static readonly Guid MfMediaTypeAudio = new("73647561-0000-0010-8000-00AA00389B71");
    private static readonly Guid MfVideoFormatRgb32 = new("00000016-0000-0010-8000-00AA00389B71");
    private static readonly Guid MfAudioFormatPcm = new("00000001-0000-0010-8000-00AA00389B71");
    private static readonly Guid MfSourceReaderEnableVideoProcessing = new("fb394f3d-ccf1-42ee-bbb3-f9b845d5681d");

    private static readonly Guid MfMtMajorType = new("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
    private static readonly Guid MfMtSubtype = new("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
    private static readonly Guid MfMtFrameSize = new("1652c33d-d6b2-4012-b834-72030849a37d");
    private static readonly Guid MfMtFrameRate = new("c459a2e8-3d2c-4e44-b132-fee5156c7bb0");
    private static readonly Guid MfMtDefaultStride = new("644b4e48-1e02-4516-b0eb-c01ca9d49ac6");
    private static readonly Guid MfMtAudioNumChannels = new("37e48bf5-645e-4c5b-89de-ada9e29b696a");
    private static readonly Guid MfMtAudioSamplesPerSecond = new("5faeeae7-0290-4c31-9e8a-c534f68d9dba");
    private static readonly Guid MfMtAudioAvgBytesPerSecond = new("1aab75c8-cfef-451c-ab95-ac034b8e1731");
    private static readonly Guid MfMtAudioBlockAlignment = new("322de230-9eeb-43bd-ab7a-ff412251541d");
    private static readonly Guid MfMtAudioBitsPerSample = new("f2deb57f-40fa-4764-aa33-ed4f2d1ff669");

    [DllImport("mfplat.dll", ExactSpelling = true)]
    private static extern int MFStartup(uint version, uint flags);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    private static extern int MFShutdown();

    [DllImport("mfplat.dll", ExactSpelling = true)]
    private static extern int MFCreateAttributes(out IMFAttributes attributes, uint initialSize);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    private static extern int MFCreateMediaType(out IMFMediaType mediaType);

    [DllImport("mfreadwrite.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int MFCreateSourceReaderFromURL(
        [MarshalAs(UnmanagedType.LPWStr)] string url,
        IMFAttributes? attributes,
        out IMFSourceReader sourceReader);

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

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("70ae66f2-c809-4e4f-8915-bdcb406b7993")]
    private interface IMFSourceReader
    {
        [PreserveSig] int GetStreamSelection(uint streamIndex, [MarshalAs(UnmanagedType.Bool)] out bool selected);
        [PreserveSig] int SetStreamSelection(uint streamIndex, [MarshalAs(UnmanagedType.Bool)] bool selected);
        [PreserveSig] int GetNativeMediaType(uint streamIndex, uint mediaTypeIndex, out IMFMediaType mediaType);
        [PreserveSig] int GetCurrentMediaType(uint streamIndex, out IMFMediaType mediaType);
        [PreserveSig] int SetCurrentMediaType(uint streamIndex, IntPtr reserved, IMFMediaType mediaType);
        [PreserveSig] int SetCurrentPosition(in Guid timeFormat, IntPtr position);
        [PreserveSig] int ReadSample(uint streamIndex, uint controlFlags, out uint actualStreamIndex, out uint streamFlags, out long timestamp, out IMFSample? sample);
        [PreserveSig] int Flush(uint streamIndex);
        [PreserveSig] int GetServiceForStream(uint streamIndex, in Guid service, in Guid riid, out IntPtr obj);
        [PreserveSig] int GetPresentationAttribute(uint streamIndex, in Guid attribute, IntPtr value);
    }
}
