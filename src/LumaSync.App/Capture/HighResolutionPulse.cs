using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace LumaSync;

/// <summary>One-shot high-resolution Windows timer pulse that also wakes promptly on cancellation.</summary>
internal sealed class HighResolutionPulse : IDisposable
{
    private const uint CreateWaitableTimerHighResolution = 0x00000002;
    private const uint TimerAllAccess = 0x001F0003;
    private const uint Infinite = 0xFFFFFFFF;
    private readonly SafeWaitHandle timer;
    private bool timerResolutionRaised;
    public string Mode { get; }

    public HighResolutionPulse()
    {
        timer = CreateWaitableTimerEx(IntPtr.Zero, null, CreateWaitableTimerHighResolution, TimerAllAccess);
        if (!timer.IsInvalid)
        {
            Mode = "high-resolution";
            return;
        }
        timer.Dispose();
        timer = CreateWaitableTimerEx(IntPtr.Zero, null, 0, TimerAllAccess);
        if (!timer.IsInvalid)
        {
            var result = timeBeginPeriod(1);
            timerResolutionRaised = result == 0;
            Mode = timerResolutionRaised ? "1 ms timer fallback" : $"standard timer fallback (timeBeginPeriod {result})";
        }
        else
        {
            timer.Dispose();
            var error = Marshal.GetLastWin32Error();
            throw new Win32Exception(error, "Could not create a Windows waitable timer.");
        }
    }

    public bool Wait(TimeSpan delay, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var dueTime = -Math.Max(1L, (long)Math.Ceiling(delay.TotalMilliseconds * 10_000));
        if (!SetWaitableTimer(timer, ref dueTime, 0, IntPtr.Zero, IntPtr.Zero, false))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not schedule a Windows timer pulse.");

        var handles = new[]
        {
            timer.DangerousGetHandle(),
            cancellationToken.WaitHandle.SafeWaitHandle.DangerousGetHandle()
        };
        var result = WaitForMultipleObjects((uint)handles.Length, handles, false, Infinite);
        if (result == 0) return !cancellationToken.IsCancellationRequested;
        if (result == 1) return false;
        throw new Win32Exception(Marshal.GetLastWin32Error(), "Waiting for a Windows timer pulse failed.");
    }

    public void Dispose()
    {
        timer.Dispose();
        if (timerResolutionRaised)
        {
            _ = timeEndPeriod(1);
            timerResolutionRaised = false;
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "CreateWaitableTimerExW", SetLastError = true)]
    private static extern SafeWaitHandle CreateWaitableTimerEx(IntPtr attributes, string? name, uint flags, uint desiredAccess);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWaitableTimer(SafeWaitHandle timer, ref long dueTime, int period, IntPtr completionRoutine, IntPtr argument, [MarshalAs(UnmanagedType.Bool)] bool resume);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForMultipleObjects(uint count, [In] IntPtr[] handles, [MarshalAs(UnmanagedType.Bool)] bool waitAll, uint milliseconds);
    [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
    private static extern uint timeBeginPeriod(uint period);
    [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
    private static extern uint timeEndPeriod(uint period);
}
