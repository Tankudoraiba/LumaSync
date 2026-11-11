using System.Diagnostics;

namespace LumaSync;

/// <summary>Serializes controller output on a dedicated thread while retaining only the newest waiting frame.</summary>
internal sealed class LatestFrameOutputPump : IDisposable
{
    private readonly object gate = new();
    private readonly AutoResetEvent frameReady = new(false);
    private readonly ILightingController controller;
    private readonly Action<long, long> completed;
    private readonly Action dropped;
    private readonly Thread worker;
    private byte[] activeFrame;
    private byte[] pendingFrame;
    private bool pendingReady;
    private bool stopping;
    private bool disposed;
    private bool pendingIsStatic;
    private ColorRgb pendingStaticColor;
    private byte pendingSequence;
    private long pendingSampleStarted;
    private Exception? failure;

    public LatestFrameOutputPump(ILightingController controller, int frameLength,
        Action<long, long> completed, Action dropped)
    {
        this.controller = controller ?? throw new ArgumentNullException(nameof(controller));
        if (frameLength < 1) throw new ArgumentOutOfRangeException(nameof(frameLength));
        this.completed = completed ?? throw new ArgumentNullException(nameof(completed));
        this.dropped = dropped ?? throw new ArgumentNullException(nameof(dropped));
        activeFrame = new byte[frameLength];
        pendingFrame = new byte[frameLength];
        worker = new Thread(WriteLoop) { IsBackground = true, Name = "LumaSync HID output" };
        worker.Start();
    }

    public Exception? Failure
    {
        get { lock (gate) return failure; }
    }

    public bool QueueFrame(ReadOnlySpan<byte> frame, long sampleStarted) =>
        Queue(frame, isStatic: false, default, 0, sampleStarted);

    public bool QueueStatic(ColorRgb color, byte sequence, long sampleStarted) =>
        Queue(ReadOnlySpan<byte>.Empty, isStatic: true, color, sequence, sampleStarted);

    private bool Queue(ReadOnlySpan<byte> frame, bool isStatic, ColorRgb color, byte sequence, long sampleStarted)
    {
        lock (gate)
        {
            if (stopping || failure is not null) return false;
            if (pendingReady) dropped();
            if (!isStatic)
            {
                if (frame.Length != pendingFrame.Length)
                    throw new ArgumentException("Queued Screen Sync frame has the wrong length.", nameof(frame));
                frame.CopyTo(pendingFrame);
            }
            pendingIsStatic = isStatic;
            pendingStaticColor = color;
            pendingSequence = sequence;
            pendingSampleStarted = sampleStarted;
            pendingReady = true;
            frameReady.Set();
            return true;
        }
    }

    private void WriteLoop()
    {
        while (true)
        {
            frameReady.WaitOne();
            bool isStatic;
            ColorRgb staticColor;
            byte sequence;
            long sampleStarted;
            lock (gate)
            {
                if (!pendingReady)
                {
                    if (stopping) return;
                    frameReady.Reset();
                    continue;
                }
                (activeFrame, pendingFrame) = (pendingFrame, activeFrame);
                isStatic = pendingIsStatic;
                staticColor = pendingStaticColor;
                sequence = pendingSequence;
                sampleStarted = pendingSampleStarted;
                pendingReady = false;
                if (!stopping) frameReady.Reset();
            }

            var started = Stopwatch.GetTimestamp();
            try
            {
                if (isStatic) controller.WriteStaticColor(staticColor, sequence);
                else controller.WriteScreenSyncFrame(activeFrame);
                completed(started, sampleStarted);
            }
            catch (Exception ex)
            {
                lock (gate)
                {
                    failure = ex;
                    stopping = true;
                    if (pendingReady)
                    {
                        pendingReady = false;
                        dropped();
                    }
                    frameReady.Set();
                }
                return;
            }
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            stopping = true;
            if (pendingReady)
            {
                pendingReady = false;
                dropped();
            }
            frameReady.Set();
        }

        if (Thread.CurrentThread != worker) _ = worker.Join(TimeSpan.FromSeconds(2));
        if (!worker.IsAlive) frameReady.Dispose();
    }
}
