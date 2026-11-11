using SharpGen.Runtime;
using System.Diagnostics;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace LumaSync;

/// <summary>Reads the selected output through DXGI duplication and a reusable BGRA or HDR10 CPU staging texture.</summary>
internal sealed class DxgiCaptureSurface : IDisposable
{
    private const int WaitTimeout = unchecked((int)0x887A0027);
    private readonly IDXGIAdapter1 adapter;
    private readonly IDXGIOutput output;
    private readonly IDXGIOutput1 output1;
    private readonly ID3D11Device device;
    private readonly ID3D11DeviceContext context;
    private readonly IDXGIOutputDuplication duplication;
    private ID3D11Texture2D? staging;
    private DxgiVideoDownscaler? downscaler;
    private Format downscalerFormat;
    private int downscalerWidth, downscalerHeight;
    private bool downscaleUnavailable;
    private Format stagingFormat;
    private int width, height;
    private bool hasFrame, disposed;
    private bool bgra = true;
    private readonly bool hdrPqRec2020;
    public long NewFrameCount { get; private set; }
    public long ReusedFrameCount { get; private set; }
    public Format PixelFormat { get; private set; }
    public bool UsesHdr10 => hdrPqRec2020 && PixelFormat == Format.R10G10B10A2_UNorm;
    public bool LastFrameWasUpdated { get; private set; }
    public bool LastFrameWasDownscaled { get; private set; }
    public double LastMapWaitMs { get; private set; }
    public double LastPixelProcessingMs { get; private set; }

    private DxgiCaptureSurface(IDXGIAdapter1 adapter, IDXGIOutput output, IDXGIOutput1 output1,
        ID3D11Device device, ID3D11DeviceContext context, IDXGIOutputDuplication duplication, bool hdrPqRec2020)
    {
        this.adapter = adapter; this.output = output; this.output1 = output1;
        this.device = device; this.context = context; this.duplication = duplication; this.hdrPqRec2020 = hdrPqRec2020;
    }

    public static DxgiCaptureSurface Open(string displayDeviceName)
    {
        var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
        try
        {
            for (var ai = 0; ; ai++)
            {
                var adapterResult = factory.EnumAdapters1(ai, out var candidateAdapter);
                if (adapterResult.Failure) break;
                IDXGIAdapter1? adapter = candidateAdapter;
                try
                {
                    for (var oi = 0; ; oi++)
                    {
                        var outputResult = adapter.EnumOutputs(oi, out var candidateOutput);
                        if (outputResult.Failure) break;
                        var output = candidateOutput!;
                        if (!string.Equals(output.Description.DeviceName, displayDeviceName, StringComparison.OrdinalIgnoreCase))
                        { output.Dispose(); continue; }

                        IDXGIOutput1? output1 = null;
                        ID3D11Device? device = null;
                        ID3D11DeviceContext? context = null;
                        IDXGIOutputDuplication? duplication = null;
                        try
                        {
                            output1 = output.QueryInterface<IDXGIOutput1>();
                            var createResult = D3D11.D3D11CreateDevice(adapter, DriverType.Unknown,
                                DeviceCreationFlags.BgraSupport | DeviceCreationFlags.VideoSupport,
                                Array.Empty<FeatureLevel>(), out device, out context);
                            if (createResult.Failure)
                            {
                                device?.Dispose(); context?.Dispose(); device = null; context = null;
                                D3D11.D3D11CreateDevice(adapter, DriverType.Unknown, DeviceCreationFlags.BgraSupport,
                                    Array.Empty<FeatureLevel>(), out device, out context).CheckError();
                            }
                            var hdrPqRec2020 = false;
                            try
                            {
                                using var output6 = output.QueryInterface<IDXGIOutput6>();
                                hdrPqRec2020 = output6.Description1.ColorSpace == ColorSpaceType.RgbFullG2084NoneP2020;
                            }
                            catch { /* Older drivers keep the established BGRA duplication path. */ }
                            if (hdrPqRec2020)
                            {
                                try
                                {
                                    using var output5 = output.QueryInterface<IDXGIOutput5>();
                                    duplication = output5.DuplicateOutput1(device, 0,
                                        [Format.R10G10B10A2_UNorm, Format.B8G8R8A8_UNorm]);
                                }
                                catch
                                {
                                    hdrPqRec2020 = false;
                                    duplication = output1.DuplicateOutput(device);
                                }
                            }
                            else duplication = output1.DuplicateOutput(device);
                            var opened = new DxgiCaptureSurface(adapter, output, output1, device, context, duplication, hdrPqRec2020);
                            adapter = null; output1 = null; output = null!; device = null; context = null; duplication = null;
                            return opened;
                        }
                        finally
                        {
                            duplication?.Dispose(); context?.Dispose(); device?.Dispose(); output1?.Dispose(); output?.Dispose();
                        }
                    }
                }
                finally { adapter?.Dispose(); }
            }
            throw new InvalidOperationException($"DXGI could not find display {displayDeviceName}.");
        }
        finally { factory.Dispose(); }
    }

    public ColorRgb[] Sample(FrameProcessor processor, LayoutOptions layout, double brightness, double smoothing,
        double gamma, double saturation, double colorTemperature = 6500, bool detectLetterbox = false, ScreenCrop crop = default)
    {
        var colors = new ColorRgb[checked(layout.Top + layout.Right + layout.Bottom + layout.Left)];
        SampleInto(processor, layout, colors, brightness, smoothing, gamma, saturation, colorTemperature, detectLetterbox, crop);
        return colors;
    }

    /// <summary>Samples the current desktop image into caller-owned color storage.</summary>
    public void SampleInto(FrameProcessor processor, LayoutOptions layout, Span<ColorRgb> destination,
        double brightness, double smoothing, double gamma, double saturation,
        double colorTemperature = 6500, bool detectLetterbox = false, ScreenCrop crop = default,
        bool sampleReusedFrame = true)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        LastFrameWasUpdated = false;
        IDXGIResource? resource = null;
        var result = duplication.AcquireNextFrame(0, out _, out resource!);
        if (result.Failure)
        {
            if (result.Code != WaitTimeout) result.CheckError();
            ReusedFrameCount++;
            if (!hasFrame) throw new DxgiFrameUnavailableException("DXGI is waiting for the first desktop update.");
            if (!sampleReusedFrame) return;
        }
        else
        {
            try
            {
                using var texture = resource!.QueryInterface<ID3D11Texture2D>();
                var description = texture.Description;
                if (description.Format is not (Format.B8G8R8A8_UNorm or Format.R8G8B8A8_UNorm or Format.R10G10B10A2_UNorm))
                    throw new NotSupportedException($"DXGI capture format {description.Format} is not supported by the LED sampler.");
                var sourceWidth = description.Width;
                var sourceHeight = description.Height;
                var downscaled = false;
                if (description.Format != Format.R10G10B10A2_UNorm && !downscaleUnavailable)
                {
                    var depthFloor = Math.Max(1, (int)Math.Ceiling(12 / Math.Clamp(layout.Depth, .02, .20)));
                    var targetWidth = Math.Min(sourceWidth, Math.Max(sourceWidth / 2,
                        Math.Max(depthFloor, Math.Max(layout.Top, layout.Bottom) * 12)));
                    var targetHeight = Math.Min(sourceHeight, Math.Max(sourceHeight / 2,
                        Math.Max(depthFloor, Math.Max(layout.Left, layout.Right) * 12)));
                    if (targetWidth < sourceWidth || targetHeight < sourceHeight)
                    {
                        try
                        {
                            EnsureDownscaler(texture, targetWidth, targetHeight);
                            downscaler!.Scale(texture, context);
                            DisposeStaging();
                            width = downscaler.Width; height = downscaler.Height;
                            LastFrameWasDownscaled = true;
                            downscaled = true;
                        }
                        catch (Exception)
                        {
                            downscaler?.Dispose(); downscaler = null;
                            downscaleUnavailable = true;
                            LastFrameWasDownscaled = false;
                        }
                    }
                    else
                    {
                        downscaler?.Dispose(); downscaler = null;
                        LastFrameWasDownscaled = false;
                    }
                }
                else
                {
                    downscaler?.Dispose(); downscaler = null;
                    LastFrameWasDownscaled = false;
                }
                if (!downscaled)
                {
                    EnsureStaging(description);
                    context.CopyResource(staging!, texture);
                    width = (int)sourceWidth; height = (int)sourceHeight;
                }
                PixelFormat = description.Format;
                bgra = description.Format == Format.B8G8R8A8_UNorm;
                hasFrame = true; NewFrameCount++; LastFrameWasUpdated = true;
            }
            finally
            {
                resource?.Dispose();
                duplication.ReleaseFrame();
            }
        }

        var mapStarted = Stopwatch.GetTimestamp();
        var mappedTexture = LastFrameWasDownscaled ? downscaler!.StagingTexture : staging!;
        var mapped = context.Map(mappedTexture, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
        LastMapWaitMs = (Stopwatch.GetTimestamp() - mapStarted) * 1000d / Stopwatch.Frequency;
        try
        {
            var sampleStarted = Stopwatch.GetTimestamp();
            try
            {
                unsafe { processor.SampleInto((byte*)mapped.DataPointer, width, height, mapped.RowPitch, layout,
                    destination, brightness, smoothing, gamma, saturation, bgra, colorTemperature, detectLetterbox,
                    hdrPqRec2020 && PixelFormat == Format.R10G10B10A2_UNorm, crop); }
            }
            finally { LastPixelProcessingMs = (Stopwatch.GetTimestamp() - sampleStarted) * 1000d / Stopwatch.Frequency; }
        }
        finally { context.Unmap(mappedTexture, 0); }
    }

    private void EnsureDownscaler(ID3D11Texture2D source, int targetWidth, int targetHeight)
    {
        var description = source.Description;
        if (downscaler is not null && downscalerFormat == description.Format
            && downscalerWidth == targetWidth && downscalerHeight == targetHeight) return;
        downscaler?.Dispose();
        downscaler = new DxgiVideoDownscaler(device, context, source, targetWidth, targetHeight);
        downscalerFormat = description.Format;
        downscalerWidth = targetWidth;
        downscalerHeight = targetHeight;
    }

    private void EnsureStaging(Texture2DDescription source)
    {
        if (staging is not null && width == source.Width && height == source.Height && stagingFormat == source.Format) return;
        staging?.Dispose();
        var description = new Texture2DDescription
        {
            Width = source.Width,
            Height = source.Height,
            MipLevels = 1,
            ArraySize = 1,
            Format = source.Format,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Staging,
            BindFlags = BindFlags.None,
            CPUAccessFlags = CpuAccessFlags.Read,
            MiscFlags = ResourceOptionFlags.None
        };
        staging = device.CreateTexture2D(description);
        stagingFormat = source.Format;
    }

    private void DisposeStaging()
    {
        staging?.Dispose();
        staging = null;
        stagingFormat = default;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        downscaler?.Dispose();
        staging?.Dispose(); duplication.Dispose(); context.Dispose(); device.Dispose(); output1.Dispose(); output.Dispose(); adapter.Dispose();
    }
}

internal sealed class DxgiFrameUnavailableException(string message) : Exception(message);
