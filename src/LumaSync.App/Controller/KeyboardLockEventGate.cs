using System.Diagnostics;
using System.Runtime.InteropServices;

namespace LumaSync;

/// <summary>Prevents keyboard indicator and Windows-key shortcut reports from colliding with LED frames.</summary>
internal static class KeyboardLockEventGate
{
    private const int WhKeyboardLl = 13;
    private const uint WmKeyDown = 0x0100, WmKeyUp = 0x0101, WmSysKeyDown = 0x0104, WmSysKeyUp = 0x0105;
    private const uint VkCapsLock = 0x14, VkLWin = 0x5B, VkRWin = 0x5C, VkNumLock = 0x90, VkScrollLock = 0x91;
    private const int QuietMilliseconds = 8;
    private const int MaximumWindowsShortcutPauseMilliseconds = 3000;
    private static readonly long QuietTicks = Stopwatch.Frequency * QuietMilliseconds / 1000;
    private static readonly long MaximumWindowsShortcutPauseTicks = Stopwatch.Frequency * MaximumWindowsShortcutPauseMilliseconds / 1000;
    private static readonly LowLevelKeyboardProc Callback = HookCallback;
    private static IntPtr hook;
    private static long lastKeyboardOutputEvent;
    private static long keyboardOutputEventCount;
    private static long windowsShortcutStarted;
    private static long interruptedFrameCount;
    private static int windowsKeyDown, windowsShortcutActive, windowsShortcutTimedOut;

    public static bool IsInstalled => hook != IntPtr.Zero;
    public static long LastKeyboardOutputEvent => Interlocked.Read(ref lastKeyboardOutputEvent);
    public static long KeyboardOutputEventCount => Interlocked.Read(ref keyboardOutputEventCount);
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
            if (Volatile.Read(ref windowsShortcutActive) != 0)
            {
                if (Stopwatch.GetTimestamp() - Interlocked.Read(ref windowsShortcutStarted) < MaximumWindowsShortcutPauseTicks)
                {
                    Thread.Sleep(1);
                    continue;
                }
                Interlocked.Exchange(ref windowsShortcutActive, 0);
                Interlocked.Exchange(ref windowsShortcutTimedOut, 1);
                RecordKeyboardOutputEvent();
            }
            var last = LastKeyboardOutputEvent;
            if (last == 0 || Stopwatch.GetTimestamp() - last >= QuietTicks) return;
            Thread.Sleep(1);
        }
    }

    public static void RecordInterruptedFrame() => Interlocked.Increment(ref interruptedFrameCount);

    private static IntPtr HookCallback(int code, UIntPtr message, IntPtr data)
    {
        if (code >= 0)
        {
            var messageId = message.ToUInt32();
            var keyDown = messageId is WmKeyDown or WmSysKeyDown;
            var keyUp = messageId is WmKeyUp or WmSysKeyUp;
            if (keyDown || keyUp)
            {
                var key = Marshal.PtrToStructure<KeyboardData>(data).VirtualKey;
                if (keyDown)
                {
                    if (key is VkLWin or VkRWin)
                    {
                        Interlocked.Exchange(ref windowsKeyDown, 1);
                        if (Volatile.Read(ref windowsShortcutTimedOut) == 0)
                        {
                            if (Interlocked.Exchange(ref windowsShortcutActive, 1) == 0)
                                Interlocked.Exchange(ref windowsShortcutStarted, Stopwatch.GetTimestamp());
                            RecordKeyboardOutputEvent();
                        }
                    }
                    else if (Volatile.Read(ref windowsKeyDown) != 0 && Volatile.Read(ref windowsShortcutTimedOut) == 0)
                    {
                        RecordKeyboardOutputEvent();
                    }
                    if (key is VkCapsLock or VkNumLock or VkScrollLock)
                        RecordKeyboardOutputEvent();
                }
                else
                {
                    if (key is VkLWin or VkRWin)
                    {
                        if (Volatile.Read(ref windowsShortcutActive) != 0)
                            RecordKeyboardOutputEvent();
                        Interlocked.Exchange(ref windowsKeyDown, 0);
                        Interlocked.Exchange(ref windowsShortcutActive, 0);
                        Interlocked.Exchange(ref windowsShortcutTimedOut, 0);
                    }
                    else if (Volatile.Read(ref windowsKeyDown) != 0 && Volatile.Read(ref windowsShortcutActive) != 0)
                        RecordKeyboardOutputEvent();
                }
            }
        }
        return CallNextHookEx(hook, code, message, data);
    }

    private static void RecordKeyboardOutputEvent()
    {
        Interlocked.Exchange(ref lastKeyboardOutputEvent, Stopwatch.GetTimestamp());
        Interlocked.Increment(ref keyboardOutputEventCount);
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
