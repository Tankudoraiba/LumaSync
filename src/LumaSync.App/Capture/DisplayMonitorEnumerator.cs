using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;

namespace LumaSync;

public readonly record struct DisplayTarget(string DeviceName, Rectangle Bounds, bool IsPrimary);

/// <summary>Enumerates attached Windows displays without loading Windows Forms.</summary>
public static class DisplayMonitorEnumerator
{
    public static DisplayTarget[] GetDisplays()
    {
        var displays = new List<DisplayTarget>();
        Exception? callbackError = null;
        MonitorEnumProc callback = (monitor, _, _, _) =>
        {
            try
            {
                var info = new MonitorInfoEx { Size = Marshal.SizeOf<MonitorInfoEx>(), DeviceName = string.Empty };
                if (!GetMonitorInfo(monitor, ref info))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not read display monitor information.");
                displays.Add(new DisplayTarget(info.DeviceName,
                    Rectangle.FromLTRB(info.Monitor.Left, info.Monitor.Top, info.Monitor.Right, info.Monitor.Bottom),
                    (info.Flags & 1) != 0));
                return true;
            }
            catch (Exception ex)
            {
                callbackError = ex;
                return false;
            }
        };

        if (!EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero))
        {
            if (callbackError is not null) throw new InvalidOperationException("Display enumeration failed.", callbackError);
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not enumerate attached displays.");
        }
        return displays.ToArray();
    }

    private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr deviceContext, IntPtr clipRectangle, IntPtr data);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(IntPtr deviceContext, IntPtr clipRectangle, MonitorEnumProc callback, IntPtr data);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfoEx info);
}
