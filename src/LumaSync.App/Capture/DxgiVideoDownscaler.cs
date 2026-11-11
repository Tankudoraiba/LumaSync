using Vortice.Direct3D11;
using Vortice.DXGI;

namespace LumaSync;

/// <summary>Scales a duplicated BGRA desktop frame on the GPU before mapping it to CPU memory.</summary>
internal sealed class DxgiVideoDownscaler : IDisposable
{
    private readonly ID3D11VideoDevice videoDevice;
    private readonly ID3D11VideoContext videoContext;
    private readonly ID3D11VideoProcessorEnumerator processorEnumerator;
    private readonly ID3D11VideoProcessor processor;
    private readonly ID3D11Texture2D scaledTexture;
    private readonly ID3D11Texture2D stagingTexture;
    private readonly ID3D11VideoProcessorOutputView outputView;
    private int outputFrame;
    private bool disposed;

    public ID3D11Texture2D StagingTexture => stagingTexture;
    public int Width { get; }
    public int Height { get; }

    public DxgiVideoDownscaler(ID3D11Device device, ID3D11DeviceContext context,
        ID3D11Texture2D source, int width, int height)
    {
        var sourceDescription = source.Description;
        Width = Math.Clamp(width, 1, (int)sourceDescription.Width);
        Height = Math.Clamp(height, 1, (int)sourceDescription.Height);
        if (Width == sourceDescription.Width && Height == sourceDescription.Height)
            throw new ArgumentException("The requested video processor output must be smaller than the source.");

        ID3D11VideoDevice? createdVideoDevice = null;
        ID3D11VideoContext? createdVideoContext = null;
        ID3D11VideoProcessorEnumerator? createdEnumerator = null;
        ID3D11VideoProcessor? createdProcessor = null;
        ID3D11Texture2D? createdTexture = null;
        ID3D11Texture2D? createdStaging = null;
        ID3D11VideoProcessorOutputView? createdOutputView = null;
        try
        {
            createdVideoDevice = device.QueryInterface<ID3D11VideoDevice>();
            createdVideoContext = context.QueryInterface<ID3D11VideoContext>();
            var content = new VideoProcessorContentDescription
            {
                InputFrameFormat = VideoFrameFormat.Progressive,
                InputFrameRate = new Rational(75, 1),
                InputWidth = (int)sourceDescription.Width,
                InputHeight = (int)sourceDescription.Height,
                OutputFrameRate = new Rational(75, 1),
                OutputWidth = Width,
                OutputHeight = Height,
                Usage = VideoUsage.PlaybackNormal
            };
            createdEnumerator = createdVideoDevice.CreateVideoProcessorEnumerator(content);
            createdProcessor = createdVideoDevice.CreateVideoProcessor(createdEnumerator, 0);
            createdVideoContext.VideoProcessorSetStreamFrameFormat(createdProcessor, 0, VideoFrameFormat.Progressive);
            createdVideoContext.VideoProcessorSetStreamAutoProcessingMode(createdProcessor, 0, false);

            var textureDescription = new Texture2DDescription
            {
                Width = Width,
                Height = Height,
                MipLevels = 1,
                ArraySize = 1,
                Format = sourceDescription.Format,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.RenderTarget,
                CPUAccessFlags = CpuAccessFlags.None,
                MiscFlags = ResourceOptionFlags.None
            };
            createdTexture = device.CreateTexture2D(textureDescription);
            createdStaging = device.CreateTexture2D(textureDescription with
            {
                Usage = ResourceUsage.Staging,
                BindFlags = BindFlags.None,
                CPUAccessFlags = CpuAccessFlags.Read
            });
            createdOutputView = createdVideoDevice.CreateVideoProcessorOutputView(createdTexture, createdEnumerator,
                new VideoProcessorOutputViewDescription
                {
                    ViewDimension = VideoProcessorOutputViewDimension.Texture2D,
                    Texture2D = new Texture2DVideoProcessorOutputView { MipSlice = 0 }
                });

            videoDevice = createdVideoDevice; createdVideoDevice = null;
            videoContext = createdVideoContext; createdVideoContext = null;
            processorEnumerator = createdEnumerator; createdEnumerator = null;
            processor = createdProcessor; createdProcessor = null;
            scaledTexture = createdTexture; createdTexture = null;
            stagingTexture = createdStaging; createdStaging = null;
            outputView = createdOutputView; createdOutputView = null;
        }
        finally
        {
            createdOutputView?.Dispose(); createdStaging?.Dispose(); createdTexture?.Dispose();
            createdProcessor?.Dispose(); createdEnumerator?.Dispose(); createdVideoContext?.Dispose(); createdVideoDevice?.Dispose();
        }
    }

    public void Scale(ID3D11Texture2D source, ID3D11DeviceContext context)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var inputDescription = new VideoProcessorInputViewDescription
        {
            ViewDimension = VideoProcessorInputViewDimension.Texture2D,
            Texture2D = new Texture2DVideoProcessorInputView { MipSlice = 0, ArraySlice = 0 }
        };
        using var inputView = videoDevice.CreateVideoProcessorInputView(source, processorEnumerator, inputDescription);
        var stream = new VideoProcessorStream { Enable = true, InputSurface = inputView };
        videoContext.VideoProcessorBlt(processor, outputView, unchecked(++outputFrame), 1, [stream]);
        context.CopyResource(stagingTexture, scaledTexture);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        outputView.Dispose(); stagingTexture.Dispose(); scaledTexture.Dispose(); processor.Dispose();
        processorEnumerator.Dispose(); videoContext.Dispose(); videoDevice.Dispose();
    }
}
