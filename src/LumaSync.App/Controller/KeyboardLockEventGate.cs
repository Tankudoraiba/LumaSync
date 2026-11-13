using System.Diagnostics;
using System.Runtime.InteropServices;

namespace LumaSync;

/// <summary>Gives the controller's composite-keyboard lock LED report a short quiet window.</summary>
internal static class KeyboardLockEventGate
{
    private const int WhKeyboardLl = 13;
    private const uint WmKeyDown = 0x0100, WmSysKeyDown = 0x0104;
    private const uint VkCapsLock = 0x14, VkNumLock = 0x90, VkScrollLock = 0x91;
    private const int QuietMilliseconds = 8;
    private static readonly long QuietTicks = Stopwatch.Frequency * QuietMilliseconds / 1000;
    private static readonly LowLevelKeyboardProc Callback = HookCallback;
    private static IntPtr hook;
    private static long lastLockKeyEvent;
    private static long lockKeyEventCount;
    private static long interruptedFrameCount;

    public static bool IsInstalled => hook != IntPtr.Zero;
    public static long LastLockKeyEvent => Interlocked.Read(ref lastLockKeyEvent);
    public static long LockKeyEventCount => Interlocked.Read(ref lockKeyEventCount);
    public static long InterruptedFrameCount => Interlocked.Read(ref interruptedFrameCount);

    public static bool Install(IntPtr module)
    {
        if (hook != IntPtr.Zero) return true;
        hook = SetWindowsHookEx(WhKeyboardLl, Callback, module, 0);
        return hook != IntPtr.Zero;
    }

    public static void Uninstall()
    {
        if (hook == IntPtr.Zero) return;
        _ = UnhookWindowsHookEx(hook);
        hook = IntPtr.Zero;
    }

    public static void WaitForQuietPeriod()
    {
        while (true)
        {
            var last = LastLockKeyEvent;
            if (last == 0 || Stopwatch.GetTimestamp() - last >= QuietTicks) return;
            Thread.Sleep(1);
        }
    }

    public static void RecordInterruptedFrame() => Interlocked.Increment(ref interruptedFrameCount);

    private static IntPtr HookCallback(int code, UIntPtr message, IntPtr data)
    {
        if (code >= 0 && message.ToUInt32() is WmKeyDown or WmSysKeyDown)
        {
            var key = Marshal.PtrToStructure<KeyboardData>(data).VirtualKey;
            if (key is VkCapsLock or VkNumLock or VkScrollLock)
            {
                Interlocked.Exchange(ref lastLockKeyEvent, Stopwatch.GetTimestamp());
                Interlocked.Increment(ref lockKeyEventCount);
            }
        }
        return CallNextHookEx(hook, code, message, data);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardData
    {
        public uint VirtualKey;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    private delegate IntPtr LowLevelKeyboardProc(int code, UIntPtr message, IntPtr data);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int hookId, LowLevelKeyboardProc callback, IntPtr module, uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, UIntPtr message, IntPtr data);
}
