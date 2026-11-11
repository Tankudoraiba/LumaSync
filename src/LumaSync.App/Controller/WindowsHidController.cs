using System.Runtime.InteropServices;
using System.Security;
using System.IO;
using Microsoft.Win32.SafeHandles;

namespace LumaSync;

/// <summary>Transport for the reference-matched QuikLight FE07 vendor HID interface.</summary>
/// <remarks>Construction performs one readDeviceInfo request. Frame writes occur only when the caller explicitly submits frames.</remarks>
public sealed class WindowsHidController : ILightingController
{
    private const uint GenericRead = 0x80000000, GenericWrite = 0x40000000, FileFlagOverlapped = 0x40000000;
    private const uint DigcfPresent = 2, DigcfDeviceInterface = 0x10;
    private static readonly Guid HidGuid = GetHidGuid();
    private readonly SafeHidHandle handle;
    private readonly object writeLock = new();
    private readonly byte[] screenSyncReport = new byte[65];
    public ControllerDeviceInfo Info { get; }

    private WindowsHidController(SafeHidHandle handle, ControllerDeviceInfo info) { this.handle = handle; Info = info; }

    public static WindowsHidController ConnectAndReadInfo()
    {
        var paths = UsbControllerDiscovery.GetPresentControllerInterfacePaths()
            .Where(p => p.Contains("&mi_00", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (paths.Length != 1) throw new IOException($"Expected one FE07 interface 0; found {paths.Length}.");

        var device = SafeHidHandle.Open(paths[0], GenericRead | GenericWrite, FileFlagOverlapped);
        try
        {
            if (device.IsInvalid) throw new IOException($"Could not open controller (Win32 {Marshal.GetLastWin32Error()}).");
            if (!device.TryGetAttributes(out var attributes) || attributes.VendorId != 0x1A86 || attributes.ProductId != 0xFE07)
                throw new IOException("Opened interface attributes do not match 1A86:FE07.");
            if (!device.TryGetCapabilities(out var caps) || caps.UsagePage < 0xFF00)
                throw new IOException("The matched interface is not the expected vendor-defined HID interface.");
            if (caps.OutputReportByteLength != 65 || caps.InputReportByteLength < 25 || !device.HasOnlyZeroOutputReportId(caps))
                throw new IOException("HID report sizes or report ID differ from the verified controller.");

            const byte sequence = 0x42;
            var packet = DeviceInfoProtocol.BuildRequest(sequence);
            var request = new byte[caps.OutputReportByteLength];
            packet.CopyTo(request, 1);
            if (!device.TryWriteOnce(request, 1000, out _))
                throw new IOException($"The device-info request failed (Win32 {Marshal.GetLastWin32Error()}).");

            for (var i = 0; i < 3; i++)
            {
                var input = new byte[caps.InputReportByteLength];
                if (!device.TryReadOnce(input, 350, out var received)) break;
                if (DeviceInfoProtocol.TryParseReply(input.AsSpan(0, (int)received), sequence, out var info) && info is not null)
                {
                    if (info.LedCount is < 1 or > 240) throw new IOException($"Controller reported unsupported LED count {info.LedCount}.");
                    if (info.Firmware <= new Version(1, 0, 2)) throw new IOException($"Firmware {info.Firmware} needs the legacy frame path, which is not implemented yet.");
                    return new WindowsHidController(device, info);
                }
            }
            throw new TimeoutException("No matching device-info reply arrived.");
        }
        catch { device.Dispose(); throw; }
    }

    public void WriteScreenSyncFrame(ReadOnlySpan<byte> frame)
    {
        lock (writeLock)
        {
            for (var offset = 0; offset < frame.Length; offset += 64)
            {
                Array.Clear(screenSyncReport);
                frame.Slice(offset, Math.Min(64, frame.Length - offset)).CopyTo(screenSyncReport.AsSpan(1));
                if (!handle.TryWriteOnce(screenSyncReport, 250, out var transferred) || transferred != screenSyncReport.Length)
                    throw new IOException($"HID frame write failed or was short (Win32 {Marshal.GetLastWin32Error()}, {transferred}/{screenSyncReport.Length} bytes).");
            }
        }
    }

    public void WriteStaticColor(ColorRgb color, byte sequence)
    {
        var payload = CandidateHidColorReport.Build(color, sequence);
        var report = new byte[65]; // verified report-ID-zero plus the 64-byte command
        payload.CopyTo(report, 1);
        if (!handle.TryWriteOnce(report, 250, out var transferred) || transferred != report.Length)
            throw new IOException($"Static-color HID write failed or was short (Win32 {Marshal.GetLastWin32Error()}, {transferred}/65 bytes).");
    }

    public void Dispose() => handle.Dispose();

    private static Guid GetHidGuid() { HidD_GetHidGuid(out var guid); return guid; }

    [StructLayout(LayoutKind.Sequential)] private struct DeviceInterfaceData { public int Size; public Guid InterfaceClassGuid; public int Flags; public UIntPtr Reserved; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeOverlapped { public UIntPtr Internal, InternalHigh; public uint Offset, OffsetHigh; public IntPtr EventHandle; }
    [StructLayout(LayoutKind.Sequential)] private struct HidAttributes { public int Size; public ushort VendorId, ProductId, VersionNumber; }
    [StructLayout(LayoutKind.Sequential, Pack = 2)] private struct HidCaps
    {
        public ushort Usage, UsagePage, InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
        public ushort NumberLinkCollectionNodes, NumberInputButtonCaps, NumberInputValueCaps, NumberInputDataIndices;
        public ushort NumberOutputButtonCaps, NumberOutputValueCaps, NumberOutputDataIndices;
        public ushort NumberFeatureButtonCaps, NumberFeatureValueCaps, NumberFeatureDataIndices;
    }

    private sealed class SafeHidHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        private SafeHidHandle() : base(true) { }
        public static SafeHidHandle Open(string path, uint access, uint flags) => CreateFile(path, access, 3, IntPtr.Zero, 3, flags, IntPtr.Zero);
        public bool TryGetAttributes(out HidAttributes attributes)
        {
            attributes = new HidAttributes { Size = Marshal.SizeOf<HidAttributes>() };
            return HidD_GetAttributes(this, ref attributes);
        }
        public bool TryGetCapabilities(out HidCaps caps)
        {
            caps = default;
            if (!HidD_GetPreparsedData(this, out var data)) return false;
            try { return HidP_GetCaps(data, out caps) == 0x00110000; }
            finally { _ = HidD_FreePreparsedData(data); }
        }
        public bool HasOnlyZeroOutputReportId(HidCaps caps)
        {
            if (caps.NumberOutputValueCaps == 0 || !HidD_GetPreparsedData(this, out var data)) return false;
            try
            {
                var count = caps.NumberOutputValueCaps;
                var buffer = Marshal.AllocHGlobal(count * 72);
                try
                {
                    if (HidP_GetValueCaps(1, buffer, ref count, data) != 0x00110000 || count == 0) return false;
                    return Enumerable.Range(0, count).All(i => Marshal.ReadByte(buffer, i * 72 + 2) == 0);
                }
                finally { Marshal.FreeHGlobal(buffer); }
            }
            finally { _ = HidD_FreePreparsedData(data); }
        }
        public bool TryWriteOnce(byte[] buffer, int timeout, out uint transferred) => Transfer(buffer, false, timeout, out transferred);
        public bool TryReadOnce(byte[] buffer, int timeout, out uint transferred) => Transfer(buffer, true, timeout, out transferred);
        private bool Transfer(byte[] buffer, bool read, int timeout, out uint transferred)
        {
            transferred = 0;
            var pending = new PendingHidTransfer(this, buffer);
            try
            {
                pending.AddHandleReference();
                var complete = read
                    ? ReadFile(this, pending.BufferAddress, (uint)buffer.Length, IntPtr.Zero, pending.OverlappedAddress)
                    : WriteFile(this, pending.BufferAddress, (uint)buffer.Length, IntPtr.Zero, pending.OverlappedAddress);
                if (complete) return GetOverlappedResult(this, pending.OverlappedAddress, out transferred, false);
                if (Marshal.GetLastWin32Error() != 997) return false;
                if (WaitForSingleObject(pending.EventHandle, (uint)timeout) != 0)
                {
                    _ = CancelIoEx(this, pending.OverlappedAddress);
                    if (WaitForSingleObject(pending.EventHandle, 1000) == 0)
                        _ = GetOverlappedResult(this, pending.OverlappedAddress, out _, false);
                    else
                    {
                        pending.ScheduleCompletionCleanup();
                        pending = null!; // The cleanup worker now owns the buffer, event, OVERLAPPED, and handle reference.
                    }
                    return false;
                }
                return GetOverlappedResult(this, pending.OverlappedAddress, out transferred, false);
            }
            finally { pending?.Dispose(); }
        }

        private sealed class PendingHidTransfer : IDisposable
        {
            private readonly SafeHidHandle owner;
            private GCHandle pinnedBuffer;
            private bool handleReferenceAdded;
            private int disposed;
            public IntPtr EventHandle { get; }
            public IntPtr OverlappedAddress { get; }
            public IntPtr BufferAddress => pinnedBuffer.AddrOfPinnedObject();

            public PendingHidTransfer(SafeHidHandle owner, byte[] buffer)
            {
                this.owner = owner;
                EventHandle = CreateEvent(IntPtr.Zero, true, false, null);
                if (EventHandle == IntPtr.Zero) throw new IOException($"Could not create HID transfer event (Win32 {Marshal.GetLastWin32Error()}).");
                try
                {
                    pinnedBuffer = GCHandle.Alloc(buffer, GCHandleType.Pinned);
                    OverlappedAddress = Marshal.AllocHGlobal(Marshal.SizeOf<NativeOverlapped>());
                    Marshal.StructureToPtr(new NativeOverlapped { EventHandle = EventHandle }, OverlappedAddress, false);
                }
                catch
                {
                    if (pinnedBuffer.IsAllocated) pinnedBuffer.Free();
                    _ = CloseHandle(EventHandle);
                    throw;
                }
            }

            public void AddHandleReference()
            {
                var success = false;
                owner.DangerousAddRef(ref success);
                handleReferenceAdded = success;
            }

            public void ScheduleCompletionCleanup()
            {
                if (!ThreadPool.QueueUserWorkItem(static state => ((PendingHidTransfer)state!).WaitForCompletionAndDispose(), this))
                    WaitForCompletionAndDispose();
            }

            private void WaitForCompletionAndDispose()
            {
                _ = WaitForSingleObject(EventHandle, uint.MaxValue);
                _ = GetOverlappedResult(owner, OverlappedAddress, out _, false);
                Dispose();
            }

            public void Dispose()
            {
                if (Interlocked.Exchange(ref disposed, 1) != 0) return;
                _ = CloseHandle(EventHandle);
                Marshal.FreeHGlobal(OverlappedAddress);
                if (pinnedBuffer.IsAllocated) pinnedBuffer.Free();
                if (handleReferenceAdded) owner.DangerousRelease();
            }
        }
        protected override bool ReleaseHandle() => CloseHandle(handle);
    }

    [DllImport("hid.dll")] private static extern void HidD_GetHidGuid(out Guid hidGuid);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, IntPtr enumerator, IntPtr hwndParent, uint flags);
    [DllImport("setupapi.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr info, ref Guid guid, uint index, ref DeviceInterfaceData data);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref DeviceInterfaceData data, IntPtr detail, uint size, out uint required, IntPtr info);
    [DllImport("setupapi.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeHidHandle CreateFile(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr CreateEvent(IntPtr security, bool manualReset, bool initialState, string? name);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool WriteFile(SafeHidHandle handle, IntPtr buffer, uint count, IntPtr written, IntPtr overlapped);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool ReadFile(SafeHidHandle handle, IntPtr buffer, uint count, IntPtr read, IntPtr overlapped);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetOverlappedResult(SafeHidHandle handle, IntPtr overlapped, out uint transferred, bool wait);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CancelIoEx(SafeHidHandle handle, IntPtr overlapped);
    [DllImport("hid.dll", SetLastError = true)] private static extern bool HidD_GetAttributes(SafeHidHandle device, ref HidAttributes attributes);
    [DllImport("hid.dll", SetLastError = true)] private static extern bool HidD_GetPreparsedData(SafeHidHandle device, out IntPtr data);
    [DllImport("hid.dll", SetLastError = true)] private static extern bool HidD_FreePreparsedData(IntPtr data);
    [DllImport("hid.dll", EntryPoint = "HidP_GetCaps")] private static extern uint HidP_GetCaps(IntPtr data, out HidCaps caps);
    [DllImport("hid.dll", EntryPoint = "HidP_GetValueCaps")] private static extern uint HidP_GetValueCaps(ushort reportType, IntPtr caps, ref ushort count, IntPtr data);
}
