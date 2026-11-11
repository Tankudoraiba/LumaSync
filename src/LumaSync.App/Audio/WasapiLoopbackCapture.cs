using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace LumaSync;

public sealed record AudioAnalysis(float Level, float[] Bands, float[] LeftBands, float[] RightBands, float LeftLevel, float RightLevel)
{
    public AudioAnalysis(float level, float[] bands) : this(level, bands, bands, bands, level, level) { }

    public AudioAnalysis(float level, float[] bands, float[] leftBands, float[] rightBands)
        : this(level, bands, leftBands, rightBands, level, level) { }

    public static AudioAnalysis Silence => new(0, new float[8], new float[8], new float[8]);
}

/// <summary>Captures the default Windows render mix through WASAPI loopback without microphone access or file writes.</summary>
public sealed class WasapiLoopbackCapture : IDisposable
{
    private const uint AudioClientLoopback = 0x00020000;
    private const uint AudioBufferSilent = 0x00000002;
    private const uint ClassContextAll = 23;
    private static readonly Guid AudioClientId = new("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2");
    private static readonly Guid AudioCaptureClientId = new("C8ADBD64-E71E-48a0-A4DE-185C395CD317");
    private static readonly Guid FloatSubFormat = new("00000003-0000-0010-8000-00AA00389B71");
    private static readonly Guid PcmSubFormat = new("00000001-0000-0010-8000-00AA00389B71");
    private readonly ManualResetEventSlim initialized = new(false);
    private readonly float[] leftSampleWindow = new float[AudioSpectrumAnalyzer.WindowSize];
    private readonly float[] rightSampleWindow = new float[AudioSpectrumAnalyzer.WindowSize];
    private readonly AudioSpectrumAnalyzer spectrumAnalyzer = new();
    private Thread? worker;
    private volatile bool stopping;
    private volatile bool running;
    private AudioAnalysis latest = AudioAnalysis.Silence;
    private Exception? startupFailure;
    private int samplesWritten;
    private double smoothedLevel;
    private double smoothedLeftLevel, smoothedRightLevel;
    private int disposed;

    public AudioAnalysis Latest => Volatile.Read(ref latest);
    public bool IsRunning => running;
    public Exception? Failure => Volatile.Read(ref startupFailure);

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        if (worker is not null) throw new InvalidOperationException("Loopback capture has already been started.");
        worker = new Thread(CaptureThread) { IsBackground = true, Name = "LumaSync WASAPI loopback" };
        worker.SetApartmentState(ApartmentState.MTA);
        worker.Start();
        var wait = Task.Run(() => initialized.Wait(TimeSpan.FromSeconds(5)), cancellationToken);
        if (!await wait) throw new TimeoutException("Windows audio loopback did not initialize within five seconds.");
        if (startupFailure is not null) throw new InvalidOperationException("Could not start Windows audio loopback.", startupFailure);
        if (!running) throw new IOException("Windows audio loopback stopped during initialization.");
    }

    private void CaptureThread()
    {
        var comInitialized = false;
        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? endpoint = null;
        IAudioClient? audioClient = null;
        IAudioCaptureClient? captureClient = null;
        object? audioClientObject = null, captureClientObject = null;
        IntPtr formatPointer = IntPtr.Zero;
        try
        {
            var comResult = CoInitializeEx(IntPtr.Zero, 0);
            if (comResult < 0) Marshal.ThrowExceptionForHR(comResult);
            comInitialized = true;

            enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
            Marshal.ThrowExceptionForHR(enumerator.GetDefaultAudioEndpoint(0, 1, out endpoint));
            var audioIid = AudioClientId;
            Marshal.ThrowExceptionForHR(endpoint.Activate(ref audioIid, ClassContextAll, IntPtr.Zero, out audioClientObject));
            audioClient = (IAudioClient)audioClientObject;
            Marshal.ThrowExceptionForHR(audioClient.GetMixFormat(out formatPointer));
            var format = Marshal.PtrToStructure<WaveFormatEx>(formatPointer);
            ValidateFormat(formatPointer, format);

            var sessionId = Guid.Empty;
            Marshal.ThrowExceptionForHR(audioClient.Initialize(0, AudioClientLoopback, 10_000_000, 0, formatPointer, ref sessionId));
            var captureIid = AudioCaptureClientId;
            Marshal.ThrowExceptionForHR(audioClient.GetService(ref captureIid, out captureClientObject));
            captureClient = (IAudioCaptureClient)captureClientObject;
            Marshal.ThrowExceptionForHR(audioClient.Start());
            running = true;
            initialized.Set();

            while (!stopping)
            {
                Marshal.ThrowExceptionForHR(captureClient.GetNextPacketSize(out var packetFrames));
                while (packetFrames > 0 && !stopping)
                {
                    Marshal.ThrowExceptionForHR(captureClient.GetBuffer(out var data, out var frames, out var flags, out _, out _));
                    try { ConsumePacket(data, frames, flags, formatPointer, format); }
                    finally { Marshal.ThrowExceptionForHR(captureClient.ReleaseBuffer(frames)); }
                    Marshal.ThrowExceptionForHR(captureClient.GetNextPacketSize(out packetFrames));
                }
                if (packetFrames == 0) Thread.Sleep(5);
            }
        }
        catch (Exception ex)
        {
            startupFailure = ex;
            if (!initialized.IsSet) initialized.Set();
        }
        finally
        {
            running = false;
            try { if (audioClient is not null) _ = audioClient.Stop(); } catch { }
            if (captureClientObject is not null && Marshal.IsComObject(captureClientObject)) Marshal.FinalReleaseComObject(captureClientObject);
            if (audioClientObject is not null && Marshal.IsComObject(audioClientObject)) Marshal.FinalReleaseComObject(audioClientObject);
            if (endpoint is not null && Marshal.IsComObject(endpoint)) Marshal.FinalReleaseComObject(endpoint);
            if (enumerator is not null && Marshal.IsComObject(enumerator)) Marshal.FinalReleaseComObject(enumerator);
            if (formatPointer != IntPtr.Zero) Marshal.FreeCoTaskMem(formatPointer);
            if (comInitialized) CoUninitialize();
        }
    }

    private static void ValidateFormat(IntPtr formatPointer, WaveFormatEx format)
    {
        var isFloat = format.FormatTag == 3;
        var isPcm = format.FormatTag == 1;
        if (format.FormatTag == 0xFFFE && format.ExtraSize >= 22)
        {
            var subtype = Marshal.PtrToStructure<Guid>(IntPtr.Add(formatPointer, 24));
            isFloat = subtype == FloatSubFormat;
            isPcm = subtype == PcmSubFormat;
        }
        if ((!isFloat && !isPcm) || format.Channels is < 1 or > 32 || format.SamplesPerSec is < 8000 or > 384000
            || (isFloat && format.BitsPerSample != 32) || (isPcm && format.BitsPerSample is not (16 or 24 or 32))
            || format.BlockAlign != format.Channels * (format.BitsPerSample / 8))
            throw new NotSupportedException($"Unsupported Windows loopback audio format ({format.FormatTag}, {format.Channels} channels, {format.BitsPerSample} bits).");
    }

    private void ConsumePacket(IntPtr data, uint frames, uint flags, IntPtr formatPointer, WaveFormatEx format)
    {
        if (frames == 0) return;
        var isFloat = format.FormatTag == 3;
        if (format.FormatTag == 0xFFFE)
            isFloat = Marshal.PtrToStructure<Guid>(IntPtr.Add(formatPointer, 24)) == FloatSubFormat;
        var bytesPerSample = format.BitsPerSample / 8;
        double leftSumSquares = 0, rightSumSquares = 0;
        for (var frame = 0; frame < frames; frame++)
        {
            double left = 0, right = 0;
            if ((flags & AudioBufferSilent) == 0)
            {
                var frameOffset = checked((int)frame * format.BlockAlign);
                var leftAddress = IntPtr.Add(data, frameOffset);
                left = ReadSample(leftAddress, format.BitsPerSample, isFloat);
                right = format.Channels == 1
                    ? left
                    : ReadSample(IntPtr.Add(leftAddress, bytesPerSample), format.BitsPerSample, isFloat);
            }
            left = Math.Clamp(left, -1, 1);
            right = Math.Clamp(right, -1, 1);
            leftSumSquares += left * left;
            rightSumSquares += right * right;
            leftSampleWindow[samplesWritten] = (float)left;
            rightSampleWindow[samplesWritten] = (float)right;
            samplesWritten++;
            if (samplesWritten == AudioSpectrumAnalyzer.WindowSize)
            {
                PublishAnalysis(format.SamplesPerSec);
                samplesWritten = 0;
            }
        }

        var level = (float)Math.Clamp(Math.Sqrt((leftSumSquares + rightSumSquares) / (2d * frames)) * 3.5, 0, 1);
        var leftLevel = Math.Clamp(Math.Sqrt(leftSumSquares / frames) * 3.5, 0, 1);
        var rightLevel = Math.Clamp(Math.Sqrt(rightSumSquares / frames) * 3.5, 0, 1);
        smoothedLevel = smoothedLevel * .65 + level * .35;
        smoothedLeftLevel = SmoothEnvelope(smoothedLeftLevel, leftLevel);
        smoothedRightLevel = SmoothEnvelope(smoothedRightLevel, rightLevel);
        var current = Latest;
        Volatile.Write(ref latest, current with
        {
            Level = (float)smoothedLevel,
            LeftLevel = (float)smoothedLeftLevel,
            RightLevel = (float)smoothedRightLevel
        });
    }

    private static double SmoothEnvelope(double previous, double current) =>
        previous + (current - previous) * (current >= previous ? .45 : .18);

    private static double ReadSample(IntPtr address, int bits, bool isFloat)
    {
        if (isFloat)
        {
            var value = BitConverter.Int32BitsToSingle(Marshal.ReadInt32(address));
            return float.IsFinite(value) ? value : 0;
        }
        return bits switch
        {
            16 => Marshal.ReadInt16(address) / 32768d,
            24 => ReadInt24(address) / 8388608d,
            32 => Marshal.ReadInt32(address) / 2147483648d,
            _ => 0
        };
    }

    private static int ReadInt24(IntPtr address)
    {
        var value = Marshal.ReadByte(address) | Marshal.ReadByte(address, 1) << 8 | Marshal.ReadByte(address, 2) << 16;
        return (value & 0x800000) != 0 ? value | unchecked((int)0xFF000000) : value;
    }

    private void PublishAnalysis(uint sampleRate)
    {
        var analysis = spectrumAnalyzer.AnalyzeStereo(leftSampleWindow, rightSampleWindow, sampleRate);
        Volatile.Write(ref latest, analysis);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        stopping = true;
        if (worker is { } thread && thread != Thread.CurrentThread && !thread.Join(TimeSpan.FromSeconds(2)))
            startupFailure ??= new TimeoutException("Windows audio loopback did not stop within two seconds.");
        initialized.Dispose();
    }

    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    private struct WaveFormatEx
    {
        public ushort FormatTag, Channels;
        public uint SamplesPerSec, AverageBytesPerSec;
        public ushort BlockAlign, BitsPerSample, ExtraSize;
    }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumeratorCom { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, uint stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr client);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr client);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid interfaceId, uint classContext, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
        [PreserveSig] int OpenPropertyStore(int access, out IntPtr properties);
        [PreserveSig] int GetId(out IntPtr id);
        [PreserveSig] int GetState(out uint state);
    }

    [ComImport, Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioClient
    {
        [PreserveSig] int Initialize(uint shareMode, uint streamFlags, long bufferDuration, long periodicity, IntPtr format, ref Guid sessionGuid);
        [PreserveSig] int GetBufferSize(out uint frames);
        [PreserveSig] int GetStreamLatency(out long latency);
        [PreserveSig] int GetCurrentPadding(out uint frames);
        [PreserveSig] int IsFormatSupported(uint shareMode, IntPtr format, out IntPtr closestFormat);
        [PreserveSig] int GetMixFormat(out IntPtr format);
        [PreserveSig] int GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);
        [PreserveSig] int Start();
        [PreserveSig] int Stop();
        [PreserveSig] int Reset();
        [PreserveSig] int SetEventHandle(IntPtr eventHandle);
        [PreserveSig] int GetService(ref Guid serviceId, [MarshalAs(UnmanagedType.IUnknown)] out object service);
    }

    [ComImport, Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioCaptureClient
    {
        [PreserveSig] int GetBuffer(out IntPtr data, out uint frames, out uint flags, out ulong devicePosition, out ulong qpcPosition);
        [PreserveSig] int ReleaseBuffer(uint frames);
        [PreserveSig] int GetNextPacketSize(out uint frames);
    }

    [DllImport("ole32.dll")] private static extern int CoInitializeEx(IntPtr reserved, uint coInit);
    [DllImport("ole32.dll")] private static extern void CoUninitialize();
}
