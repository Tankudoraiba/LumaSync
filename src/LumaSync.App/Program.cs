using System.Drawing;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace LumaSync;

internal static class Program
{
    private const uint WindowMessageCreate = 0x0001, WindowMessageDestroy = 0x0002,
        WindowMessagePaint = 0x000F, WindowMessageEraseBackground = 0x0014,
        WindowMessageClose = 0x0010, WindowMessageGetMinMaxInfo = 0x0024,
        WindowMessageTimer = 0x0113, WindowMessageLeftButtonUp = 0x0202;
    private const uint WindowMessageTrayCallback = 0x8000 + 41, WindowMessageQueryEndSession = 0x0011,
        WindowMessageEndSession = 0x0016;
    private const uint PickerMessagePaint = 0x000F, PickerMessageEraseBackground = 0x0014,
        PickerMessageClose = 0x0010, PickerMessageDestroy = 0x0002,
        PickerMessageSize = 0x0005, PickerMessageLeftButtonDown = 0x0201, PickerMessageLeftButtonUp = 0x0202,
        PickerMessageMouseMove = 0x0200, PickerMessageKeyDown = 0x0100, PickerMessageChar = 0x0102;
    private const int UiLogicalWidth = NativeUiWindowPolicy.LogicalWidth, UiLogicalHeight = NativeUiWindowPolicy.LogicalHeight;
    private const int PickerLogicalWidth = 700, PickerLogicalHeight = 640;
    private const uint PickerWindowStyle = 0x00C80000, PickerExtendedWindowStyle = 0x00000080;
    private const int EmbeddedApplicationIconResourceId = 32512;
    private const uint LoadImageDefaultSize = 0x0040;
    private const uint NotifyIconAdd = 0, NotifyIconModify = 1, NotifyIconDelete = 2;
    private const uint NotifyFlagMessage = 1, NotifyFlagIcon = 2, NotifyFlagTip = 4;
    private const int TrayIconId = 1, TrayCommandOpen = 1001, TrayCommandToggle = 1002, TrayCommandExit = 1003;
    private const nuint DiagnosticsTimerId = 2;
    private const string InstanceMutexName = "Local\\LumaSync.SingleInstance.v1";
    private const string ActivateExistingMessageName = "LumaSync.SingleInstance.Activate.v1";
    private const uint MessageBoxYesNo = 0x00000004, MessageBoxIconQuestion = 0x00000020;
    private const uint MenuString = 0, MenuSeparator = 0x0800, MenuChecked = 0x0008;
    private const uint PopupRightButton = 0x0002, PopupReturnCommand = 0x0100;
    private const int TextTransparent = 1;
    private const int TextButtonCentered = 0x00008825; // single line, centered, no prefix parsing, ellipsize to fit
    private const int TextStatusSingleLine = 0x00008824; // single line, left aligned, no prefix parsing, ellipsize
    private const uint PaletteBackground = 0x00221710; // #101722
    private const uint PalettePanel = 0x00352218; // #182235
    private const uint PaletteButton = 0x00523626; // #263652
    private const uint PaletteButtonAlt = 0x00472732; // #322747
    private const uint PaletteActive = 0x00483F19; // #193F48
    private const uint PaletteOn = 0x00563042; // #423056
    private const uint PaletteAccentTeal = 0x00D2E637; // #37E6D2
    private const uint PaletteAccentPink = 0x00A34FFF; // #FF4FA3
    private const uint PaletteText = 0x00FCF5F1; // #F1F5FC
    private const uint PaletteSecondary = 0x00DDCBBE; // #BECBDD
    private const uint PaletteMuted = 0x00AB9282; // #8292AB
    private static readonly int[] NativeModes = [0, 1, 2, 3, 4, 5];
    private static readonly WindowProcedure Procedure = WindowProc;
    private static readonly WindowProcedure ColorPickerProcedure = ColorPickerWindowProc;
    private static DxgiCaptureSurface? capture;
    private static readonly FrameProcessor Processor = new();
    private static LayoutOptions Layout = new();
    private static LedSample[] ledLayout = [];
    private static ColorRgb[] ledColors = new ColorRgb[54];
    private static ColorRgb[] previousColors = new ColorRgb[54];
    private static byte[] screenSyncFrame = new byte[7 + 54 * 5];
    private static volatile AppSettings settings = new();
    private static AppSettings? tuningLabelSettings;
    private static string[] tuningLabelCache = [];
    private static volatile bool lightingEnabled;
    private static string displayName = string.Empty;
    private static DisplayTarget[] availableDisplays = [];
    private static DisplayTarget selectedDisplay;
    private static string status = "Starting DXGI capture…";
    private static int reportedControllerLedCount;
    private static byte sequence = 1;
    private static int calibrationPreviewColorPacked = -1;
    private static IntPtr window;
    private static IntPtr paintBufferDc, paintBufferBitmap, paintBufferPreviousBitmap;
    private static int paintBufferWidth, paintBufferHeight;
    private static IntPtr pickerBufferDc, pickerBufferBitmap, pickerBufferPreviousBitmap;
    private static int pickerBufferWidth, pickerBufferHeight;
    private static IntPtr colorPickerWindow, colorPickerOwner;
    private static ColorRgb pendingPickerColor;
    private static string pendingHexText = string.Empty;
    private static bool hexInputActive;
    private static int activeColorSlider = -1;
    private static CancellationTokenSource? captureCancellation;
    private static Task? captureTask;
    private static IntPtr trayIconHandle;
    private static IntPtr uiFont, brandTitleFont, brandSubtitleFont;
    private static bool trayAvailable;
    private static bool explicitExit;
    private static bool startWithWindows;
    private static bool showDiagnostics;
    private static long processedFrames, queuedFrames, outputFrames, droppedCaptureFrames;
    private static long changedFrames;
    private static long candidateFrames, pacingRejectedFrames;
    private static long diagnosticWindowTimestamp, diagnosticPreviousCaptureFrames, diagnosticPreviousChangedFrames;
    private static long diagnosticPreviousQueuedFrames, diagnosticPreviousOutputFrames;
    private static long diagnosticPreviousCandidateFrames, diagnosticPreviousPacingRejectedFrames;
    private static long recentCaptureMilliFps, recentChangedMilliFps, recentQueuedMilliFps, recentOutputMilliFps;
    private static long recentCandidateMilliFps, recentPacingRejectedMilliFps;
    private static long lastProcessingTicks, lastUsbWriteTicks, lastEndToEndTicks;
    private static int controllerConnected;
    private static Mutex? instanceMutex;
    private static readonly uint ActivateExistingMessage = RegisterWindowMessage(ActivateExistingMessageName);
    private static long effectStartedTimestamp = Stopwatch.GetTimestamp();
    private static int forceOutputRefresh = 1;
    private static long captureRetryAt;
    private static readonly uint TaskbarCreatedMessage = RegisterWindowMessage("TaskbarCreated");

    private static int Main()
    {
        instanceMutex = new Mutex(true, InstanceMutexName, out var firstInstance);
        if (!firstInstance)
        {
            try { firstInstance = instanceMutex.WaitOne(0); }
            catch (AbandonedMutexException) { firstInstance = true; }
        }
        if (!firstInstance)
        {
            _ = PostMessage(new IntPtr(0xffff), ActivateExistingMessage, IntPtr.Zero, IntPtr.Zero);
            instanceMutex.Dispose();
            instanceMutex = null;
            return 0;
        }

        settings = SettingsStore.Load(out var settingsWarning);
        lightingEnabled = settings.LightingEnabled;
        Layout = new LayoutOptions(settings.Top, settings.Right, settings.Bottom, settings.Left,
            settings.SamplingDepth / 100d, settings.Clockwise, settings.StartCorner);
        var ledCount = Layout.Create().Count;
        ledLayout = Layout.Create().ToArray();
        ledColors = new ColorRgb[ledCount];
        previousColors = new ColorRgb[ledCount];
        screenSyncFrame = new byte[7 + ledCount * 5];
        availableDisplays = DisplayMonitorEnumerator.GetDisplays();
        selectedDisplay = availableDisplays.FirstOrDefault(d =>
            string.Equals(d.DeviceName, settings.DisplayDeviceName, StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(selectedDisplay.DeviceName)) selectedDisplay = availableDisplays.FirstOrDefault(d => d.IsPrimary);
        if (string.IsNullOrWhiteSpace(selectedDisplay.DeviceName)) { ReleaseSingleInstanceMutex(); return 2; }
        displayName = selectedDisplay.DeviceName;
        if (settingsWarning is not null) status = "Saved settings ignored: " + settingsWarning;
        try { startWithWindows = WindowsStartupRegistration.IsEnabled(GetExecutablePath()); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        { status = "Could not read this user's Windows startup setting: " + ex.Message; }

        var instance = GetModuleHandle(null);
        var windowClass = new WindowClass
        {
            Size = (uint)Marshal.SizeOf<WindowClass>(),
            Procedure = Procedure,
            Instance = instance,
            Cursor = LoadCursor(IntPtr.Zero, new IntPtr(32512)),
            ClassName = "LumaSyncNativeMainWindow"
        };
        if (RegisterClassEx(ref windowClass) == 0) { ReleaseSingleInstanceMutex(); return 3; }
        var initialSize = GetOuterWindowSize(NativeUiWindowPolicy.InitialClientWidth, NativeUiWindowPolicy.InitialClientHeight);
        window = CreateWindowEx(0, windowClass.ClassName, "LumaSync · Screen Sync",
            NativeUiWindowPolicy.ResizableWindowStyle, 120, 70, initialSize.Width, initialSize.Height,
            IntPtr.Zero, IntPtr.Zero, instance, IntPtr.Zero);
        if (window == IntPtr.Zero) { ReleaseSingleInstanceMutex(); return 4; }
        trayIconHandle = LoadImage(instance, new IntPtr(EmbeddedApplicationIconResourceId),
            1, 0, 0, LoadImageDefaultSize);
        if (trayIconHandle != IntPtr.Zero)
        {
            _ = SendMessage(window, 0x0080, IntPtr.Zero, trayIconHandle);
            _ = SendMessage(window, 0x0080, new IntPtr(1), trayIconHandle);
        }
        trayAvailable = AddTrayIcon(window);
        SetCaption(window, "LumaSync · Screen Sync");
        _ = SetTimer(window, DiagnosticsTimerId, 500, IntPtr.Zero);
        var startHidden = settings.StartMinimizedToTray && trayAvailable;
        ShowWindow(window, startHidden ? 0 : 5);
        if (!startHidden) UpdateWindow(window);
        SetCaption(window, "LumaSync · Screen Sync");
        _ = RepairCaptionAsync(window);
        Message message;
        while (GetMessage(out message, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref message);
            DispatchMessage(ref message);
        }
        ReleaseSingleInstanceMutex();
        return (int)message.WParam;
    }

    private static IntPtr WindowProc(IntPtr hwnd, uint message, UIntPtr wParam, IntPtr lParam)
    {
        try
        {
            switch (message)
            {
                case WindowMessageCreate:
                    try
                    {
                        StartCaptureWorker(hwnd);
                    }
                    catch (Exception ex)
                    {
                        status = "DXGI unavailable: " + ex.Message;
                        Interlocked.Exchange(ref captureRetryAt, Stopwatch.GetTimestamp() + 3 * Stopwatch.Frequency);
                    }
                    SetCaption(hwnd, "LumaSync · Screen Sync");
                    return IntPtr.Zero;
                case WindowMessageLeftButtonUp:
                    var clientX = unchecked((short)((long)lParam & 0xFFFF));
                    var clientY = unchecked((short)(((long)lParam >> 16) & 0xFFFF));
                    if (!GetClientRect(hwnd, out var clientRect) || clientRect.Right <= 0 || clientRect.Bottom <= 0) break;
                    var click = new NativeUiCoordinateTransform(UiLogicalWidth, UiLogicalHeight,
                        clientRect.Right, clientRect.Bottom).ToLogical(clientX, clientY);
                    var clickX = click.X;
                    var clickY = click.Y;
                    if (clickY is >= 30 and <= 75 && clickX is >= 660 and <= 755)
                    {
                        ImportSettings(hwnd);
                        return IntPtr.Zero;
                    }
                    if (clickY is >= 30 and <= 75 && clickX is >= 760 and <= 870)
                    {
                        ExportSettings(hwnd);
                        return IntPtr.Zero;
                    }
                    if (clickY is >= 30 and <= 75 && clickX is >= 565 and <= 650)
                    {
                        showDiagnostics = !showDiagnostics;
                        InvalidateRect(hwnd, IntPtr.Zero, false);
                        return IntPtr.Zero;
                    }
                    if (clickY is >= 710 and <= 744 && clickX is >= 600 and <= 870)
                    {
                        ResetToDefaults(hwnd);
                        return IntPtr.Zero;
                    }
                    if (clickX is >= 32 and <= 250 && clickY is >= 95 and <= 145)
                    {
                        lightingEnabled = !lightingEnabled;
                        settings = settings with { LightingEnabled = lightingEnabled };
                        try { SettingsStore.Save(settings); }
                        catch (Exception ex) { status = "Settings could not be saved: " + ex.Message; }
                        InvalidateRect(hwnd, IntPtr.Zero, false);
                        return IntPtr.Zero;
                    }
                    if (TryGetTuningEdit(clickX, clickY, out var tuning, out var tuningDelta))
                    {
                        ApplyTuningChange(hwnd, tuning, tuningDelta);
                        return IntPtr.Zero;
                    }
                    if (clickY is >= 666 and <= 704 && clickX is >= 32 and <= 300)
                    {
                        SaveSettingsChange(hwnd, settings with { StartMinimizedToTray = !settings.StartMinimizedToTray },
                            settings.StartMinimizedToTray ? "Start hidden disabled" : "Start hidden enabled for next launch");
                        return IntPtr.Zero;
                    }
                    if (clickY is >= 666 and <= 704 && clickX is >= 315 and <= 585)
                    {
                        SaveSettingsChange(hwnd, settings with { MinimizeToTrayOnClose = !settings.MinimizeToTrayOnClose },
                            settings.MinimizeToTrayOnClose ? "Close-to-tray disabled" : "Close-to-tray enabled");
                        return IntPtr.Zero;
                    }
                    if (clickY is >= 666 and <= 704 && clickX is >= 600 and <= 870)
                    {
                        ToggleWindowsStartup(hwnd);
                        return IntPtr.Zero;
                    }
                    if (clickY is >= 202 and <= 242)
                    {
                        if (clickX is >= 32 and <= 250) CycleNativeMode(hwnd);
                        else if (clickX is >= 280 and <= 325) AdjustEffectSpeed(hwnd, -5);
                        else if (clickX is >= 410 and <= 455) AdjustEffectSpeed(hwnd, 5);
                        else if (clickX is >= 485 and < 755) OpenColorPicker(hwnd);
                        else if (clickX is >= 765 and <= 870) CycleBaseColor(hwnd);
                        return IntPtr.Zero;
                    }
                    if (TryGetEdgeEdit(clickX, clickY, out var edge, out var delta))
                    {
                        ApplyLayoutChange(hwnd, edge, delta);
                        return IntPtr.Zero;
                    }
                    if (clickX is >= 32 and <= 250 && clickY is >= 155 and <= 195)
                    {
                        SelectNextDisplay(hwnd);
                        return IntPtr.Zero;
                    }
                    if (clickX is >= 470 and <= 650 && clickY is >= 155 and <= 195)
                    {
                        var updated = LedLayoutEditor.ChangeOrientation(settings, !settings.Clockwise, settings.StartCorner);
                        ApplyMappingSettingsChange(hwnd, updated, "Strip direction saved · capture restarted");
                        return IntPtr.Zero;
                    }
                    if (clickX is >= 670 and <= 870 && clickY is >= 155 and <= 195)
                    {
                        var nextCorner = (LedCorner)(((int)settings.StartCorner + 1) % 4);
                        var updated = LedLayoutEditor.ChangeOrientation(settings, settings.Clockwise, nextCorner);
                        ApplyMappingSettingsChange(hwnd, updated, "Strip starting corner saved · capture restarted");
                        return IntPtr.Zero;
                    }
                    break;
                case WindowMessagePaint:
                    Paint(hwnd);
                    return IntPtr.Zero;
                case NativeUiWindowPolicy.SizeMessage:
                    // Resize can expose invalid areas without producing another control event.
                    // Repaint the whole client so the retained back buffer is recreated at the new size.
                    if (NativeUiWindowPolicy.IsResizeMessage(message)) InvalidateRect(hwnd, IntPtr.Zero, false);
                    return IntPtr.Zero;
                case NativeUiWindowPolicy.SizingMessage:
                    if (NativeUiWindowPolicy.IsSizingMessage(message)
                        && GetClientRect(hwnd, out var currentClient) && GetWindowRect(hwnd, out var currentWindow))
                    {
                        var bounds = Marshal.PtrToStructure<Rect>(lParam);
                        var adjusted = NativeUiResizePolicy.Constrain(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom,
                            unchecked((uint)wParam.ToUInt64()),
                            currentWindow.Right - currentWindow.Left - currentClient.Right,
                            currentWindow.Bottom - currentWindow.Top - currentClient.Bottom);
                        bounds.Left = adjusted.Left;
                        bounds.Top = adjusted.Top;
                        bounds.Right = adjusted.Right;
                        bounds.Bottom = adjusted.Bottom;
                        Marshal.StructureToPtr(bounds, lParam, false);
                        return new IntPtr(1);
                    }
                    break;
                case WindowMessageEraseBackground:
                    // Paint fills the full client surface from a retained back buffer.
                    return new IntPtr(1);
                case WindowMessageTimer:
                    if (wParam == new UIntPtr(DiagnosticsTimerId))
                    {
                        UpdateDiagnosticRates();
                        TryRestartStoppedCapture(hwnd);
                        if (showDiagnostics) InvalidateRect(hwnd, IntPtr.Zero, false);
                        return IntPtr.Zero;
                    }
                    break;
                case WindowMessageTrayCallback:
                    HandleTrayCallback(hwnd, unchecked((uint)lParam.ToInt64()));
                    return IntPtr.Zero;
                case WindowMessageGetMinMaxInfo:
                    var minMax = Marshal.PtrToStructure<MinMaxInfo>(lParam);
                    var minSize = GetOuterWindowSize(NativeUiWindowPolicy.MinimumClientWidth, NativeUiWindowPolicy.MinimumClientHeight);
                    minMax.MinTrackSize = new Point { X = minSize.Width, Y = minSize.Height };
                    Marshal.StructureToPtr(minMax, lParam, false);
                    return IntPtr.Zero;
                case WindowMessageClose:
                    if (WindowClosePolicy.ShouldMinimizeToTray(settings.MinimizeToTrayOnClose, trayAvailable,
                            trayRetryPending: false, explicitExitRequested: explicitExit, windowsSessionEnding: false))
                    {
                        ShowWindow(hwnd, 0);
                        status = "Running in the notification area · lighting remains active";
                        SetTrayIconText("LumaSync · running in the notification area");
                        return IntPtr.Zero;
                    }
                    DestroyWindow(hwnd);
                    return IntPtr.Zero;
                case WindowMessageEndSession:
                    if (wParam != UIntPtr.Zero)
                    {
                        explicitExit = true;
                        DestroyWindow(hwnd);
                    }
                    return IntPtr.Zero;
                case WindowMessageQueryEndSession:
                    return new IntPtr(1);
                case WindowMessageDestroy:
                    _ = KillTimer(hwnd, DiagnosticsTimerId);
                    RemoveTrayIcon(hwnd);
                    _ = StopCaptureWorker();
                    DisposeBrandFonts();
                    DisposeUiFont();
                    ReleasePaintBuffer();
                    PostQuitMessage(0);
                    return IntPtr.Zero;
            }
            if (message == TaskbarCreatedMessage)
            {
                trayAvailable = AddTrayIcon(hwnd);
                return IntPtr.Zero;
            }
            if (message == ActivateExistingMessage)
            {
                RestoreWindow(hwnd);
                return IntPtr.Zero;
            }
        }
        catch (Exception ex)
        {
            status = "Prototype error: " + ex.Message;
            SetCaption(hwnd, status);
            InvalidateRect(hwnd, IntPtr.Zero, true);
        }
        return DefWindowProc(hwnd, message, wParam, lParam);
    }

    private static bool AddTrayIcon(IntPtr hwnd)
    {
        if (trayIconHandle == IntPtr.Zero) return false;
        var data = new NotifyIconData
        {
            Size = (uint)Marshal.SizeOf<NotifyIconData>(), Window = hwnd, Id = TrayIconId,
            Flags = NotifyFlagMessage | NotifyFlagIcon | NotifyFlagTip,
            CallbackMessage = WindowMessageTrayCallback, Icon = trayIconHandle,
            Tip = "LumaSync · Screen Sync", Info = string.Empty, InfoTitle = string.Empty
        };
        return Shell_NotifyIcon(NotifyIconAdd, ref data);
    }

    private static void SetTrayIconText(string text)
    {
        if (!trayAvailable || window == IntPtr.Zero) return;
        var data = new NotifyIconData
        {
            Size = (uint)Marshal.SizeOf<NotifyIconData>(), Window = window, Id = TrayIconId,
            Flags = NotifyFlagTip, Tip = text, Info = string.Empty, InfoTitle = string.Empty
        };
        _ = Shell_NotifyIcon(NotifyIconModify, ref data);
    }

    private static void RemoveTrayIcon(IntPtr hwnd)
    {
        if (trayAvailable)
        {
            var data = new NotifyIconData
            {
                Size = (uint)Marshal.SizeOf<NotifyIconData>(), Window = hwnd, Id = TrayIconId,
                Tip = string.Empty, Info = string.Empty, InfoTitle = string.Empty
            };
            _ = Shell_NotifyIcon(NotifyIconDelete, ref data);
        }
        trayAvailable = false;
    }

    private static void HandleTrayCallback(IntPtr hwnd, uint mouseMessage)
    {
        if (mouseMessage is 0x0203) RestoreWindow(hwnd);
        else if (mouseMessage is 0x007B or 0x0205) ShowTrayMenu(hwnd);
    }

    private static void ShowTrayMenu(IntPtr hwnd)
    {
        var menu = CreatePopupMenu();
        if (menu == IntPtr.Zero) return;
        _ = AppendMenu(menu, MenuString, TrayCommandOpen, "Open LumaSync");
        _ = AppendMenu(menu, MenuString | (lightingEnabled ? MenuChecked : 0), TrayCommandToggle,
            lightingEnabled ? "Turn Screen Sync off" : "Turn Screen Sync on");
        _ = AppendMenu(menu, MenuSeparator, 0, null);
        _ = AppendMenu(menu, MenuString, TrayCommandExit, "Exit · turn LEDs off");
        _ = GetCursorPos(out var point);
        _ = SetForegroundWindow(hwnd);
        var command = TrackPopupMenu(menu, PopupRightButton | PopupReturnCommand, point.X, point.Y, 0, hwnd, IntPtr.Zero);
        _ = DestroyMenu(menu);
        _ = PostMessage(hwnd, 0, IntPtr.Zero, IntPtr.Zero);
        switch (command)
        {
            case TrayCommandOpen: RestoreWindow(hwnd); break;
            case TrayCommandToggle:
                lightingEnabled = !lightingEnabled;
                settings = settings with { LightingEnabled = lightingEnabled };
                try { SettingsStore.Save(settings); }
                catch (Exception ex) { status = "Settings could not be saved: " + ex.Message; }
                SetTrayIconText(lightingEnabled ? "LumaSync · Screen Sync on" : "LumaSync · Screen Sync off");
                InvalidateRect(hwnd, IntPtr.Zero, false);
                break;
            case TrayCommandExit:
                explicitExit = true;
                DestroyWindow(hwnd);
                break;
        }
    }

    private static void RestoreWindow(IntPtr hwnd)
    {
        ShowWindow(hwnd, 9);
        _ = SetForegroundWindow(hwnd);
        SetTrayIconText("LumaSync · Screen Sync");
        InvalidateRect(hwnd, IntPtr.Zero, false);
    }

    private static void CycleNativeMode(IntPtr hwnd)
    {
        var current = Array.IndexOf(NativeModes, settings.ModeIndex);
        var next = NativeModes[(current + 1) % NativeModes.Length];
        effectStartedTimestamp = Stopwatch.GetTimestamp();
        Interlocked.Exchange(ref forceOutputRefresh, 1);
        SaveSettingsChange(hwnd, settings with { ModeIndex = next }, $"{NativeModeName(next)} selected");
    }

    private static void AdjustEffectSpeed(IntPtr hwnd, int delta)
    {
        var speed = Math.Clamp(settings.EffectSpeed + delta, 5, 100);
        if (speed == settings.EffectSpeed) return;
        Interlocked.Exchange(ref forceOutputRefresh, 1);
        SaveSettingsChange(hwnd, settings with { EffectSpeed = speed }, $"Effect speed · {speed:0}%");
    }

    private static void CycleBaseColor(IntPtr hwnd)
    {
        var next = BaseColorPresets.NextHex(settings.StaticColorHex);
        Interlocked.Exchange(ref forceOutputRefresh, 1);
        SaveSettingsChange(hwnd, settings with { StaticColorHex = next }, $"Base color · {BaseColorPresets.NameFor(next)} #{next}");
    }

    private static void OpenColorPicker(IntPtr owner)
    {
        if (colorPickerWindow != IntPtr.Zero)
        {
            _ = SetForegroundWindow(colorPickerWindow);
            return;
        }

        var pickerClass = new WindowClass
        {
            Size = (uint)Marshal.SizeOf<WindowClass>(),
            Procedure = ColorPickerProcedure,
            Instance = GetModuleHandle(null),
            Cursor = LoadCursor(IntPtr.Zero, new IntPtr(32512)),
            Icon = trayIconHandle,
            SmallIcon = trayIconHandle,
            ClassName = "LumaSyncColorPickerWindow"
        };
        if (RegisterClassEx(ref pickerClass) == 0 && Marshal.GetLastWin32Error() != 1410)
        {
            status = $"Color picker could not open (Win32 {Marshal.GetLastWin32Error()}).";
            InvalidateRect(owner, IntPtr.Zero, false);
            return;
        }

        pendingPickerColor = ParseStaticColor(settings.StaticColorHex);
        pendingHexText = $"#{settings.StaticColorHex.ToUpperInvariant()}";
        hexInputActive = false;
        activeColorSlider = -1;
        colorPickerOwner = owner;
        var pickerSize = GetOuterWindowSize(PickerLogicalWidth, PickerLogicalHeight,
            PickerWindowStyle, PickerExtendedWindowStyle);
        var x = 160;
        var y = 100;
        if (GetWindowRect(owner, out var ownerRect))
        {
            x = ownerRect.Left + Math.Max(0, (ownerRect.Right - ownerRect.Left - pickerSize.Width) / 2);
            y = ownerRect.Top + Math.Max(0, (ownerRect.Bottom - ownerRect.Top - pickerSize.Height) / 2);
        }
        colorPickerWindow = CreateWindowEx(PickerExtendedWindowStyle, pickerClass.ClassName,
            "LumaSync · Custom Color", PickerWindowStyle, x, y, pickerSize.Width, pickerSize.Height,
            owner, IntPtr.Zero, pickerClass.Instance, IntPtr.Zero);
        if (colorPickerWindow == IntPtr.Zero)
        {
            colorPickerOwner = IntPtr.Zero;
            status = $"Color picker window could not be created (Win32 {Marshal.GetLastWin32Error()}).";
            InvalidateRect(owner, IntPtr.Zero, false);
            return;
        }

        _ = EnableWindow(owner, false);
        _ = ShowWindow(colorPickerWindow, 5);
        _ = UpdateWindow(colorPickerWindow);
        _ = SetForegroundWindow(colorPickerWindow);
    }

    private static IntPtr ColorPickerWindowProc(IntPtr hwnd, uint message, UIntPtr wParam, IntPtr lParam)
    {
        try
        {
            switch (message)
            {
                case PickerMessageEraseBackground:
                    return new IntPtr(1);
                case PickerMessageSize:
                    InvalidateRect(hwnd, IntPtr.Zero, false);
                    return IntPtr.Zero;
                case PickerMessagePaint:
                    PaintColorPicker(hwnd);
                    return IntPtr.Zero;
                case PickerMessageLeftButtonDown:
                {
                    var (x, y) = ColorPickerClientPoint(hwnd, lParam);
                    hexInputActive = x is >= 150 and <= 660 && y is >= 76 and < 118;
                    if (hexInputActive)
                    {
                        pendingHexText = "#";
                        activeColorSlider = -1;
                        _ = SetFocus(hwnd);
                        InvalidateRect(hwnd, IntPtr.Zero, false);
                        return IntPtr.Zero;
                    }
                    var slider = ColorPickerSliderAt(x, y);
                    if (slider >= 0)
                    {
                        activeColorSlider = slider;
                        _ = SetCapture(hwnd);
                        UpdatePickerSliderFromX(x);
                    }
                    return IntPtr.Zero;
                }
                case PickerMessageMouseMove:
                    if (activeColorSlider >= 0)
                    {
                        var (x, _) = ColorPickerClientPoint(hwnd, lParam);
                        UpdatePickerSliderFromX(x);
                    }
                    return IntPtr.Zero;
                case PickerMessageKeyDown:
                    if (wParam.ToUInt32() == 0x1B) { ClearCalibrationPreview(); DestroyWindow(hwnd); } // Escape cancels.
                    else if (wParam.ToUInt32() == 0x0D) ApplyPickerColor(hwnd); // Enter applies.
                    return IntPtr.Zero;
                case PickerMessageChar:
                    if (hexInputActive)
                    {
                        var character = (char)wParam.ToUInt32();
                        if (character == '\b')
                        {
                            if (pendingHexText.Length > 1) pendingHexText = pendingHexText[..^1];
                        }
                        else if (Uri.IsHexDigit(character) && pendingHexText.Length < 7)
                        {
                            pendingHexText += char.ToUpperInvariant(character);
                        }
                        else return IntPtr.Zero;

                        var digits = pendingHexText.TrimStart('#');
                        if (digits.Length == 6 && uint.TryParse(digits,
                                System.Globalization.NumberStyles.HexNumber,
                                System.Globalization.CultureInfo.InvariantCulture, out var packed))
                        {
                            pendingPickerColor = new ColorRgb((byte)(packed >> 16), (byte)(packed >> 8), (byte)packed);
                        }
                        InvalidateRect(hwnd, IntPtr.Zero, false);
                        if (colorPickerOwner != IntPtr.Zero) InvalidateRect(colorPickerOwner, IntPtr.Zero, false);
                    }
                    return IntPtr.Zero;
                case PickerMessageLeftButtonUp:
                {
                    if (activeColorSlider >= 0)
                    {
                        activeColorSlider = -1;
                        _ = ReleaseCapture();
                        return IntPtr.Zero;
                    }

                    var (x, y) = ColorPickerClientPoint(hwnd, lParam);
                    for (var i = 0; i < BaseColorPresets.All.Count; i++)
                    {
                        var left = 40 + i % 4 * 155;
                        var top = 132 + i / 4 * 48;
                        if (x < left || x >= left + 145 || y < top || y >= top + 42) continue;
                        var preset = BaseColorPresets.All[i];
                        pendingPickerColor = ParseStaticColor(preset.Hex);
                        pendingHexText = $"#{preset.Hex.ToUpperInvariant()}";
                        hexInputActive = false;
                        Interlocked.Exchange(ref calibrationPreviewColorPacked,
                            pendingPickerColor.R << 16 | pendingPickerColor.G << 8 | pendingPickerColor.B);
                        Interlocked.Exchange(ref forceOutputRefresh, 1);
                        InvalidateRect(hwnd, IntPtr.Zero, false);
                        if (colorPickerOwner != IntPtr.Zero) InvalidateRect(colorPickerOwner, IntPtr.Zero, false);
                        return IntPtr.Zero;
                    }

                    if (y is >= 566 and < 616 && x is >= 40 and < 185)
                    {
                        ApplyPickerColor(hwnd);
                        return IntPtr.Zero;
                    }
                    if (y is >= 440 and < 548 && x is >= 40 and < 660)
                    {
                        var band = (y - 440) / 38;
                        if ((y - 440) % 38 >= 32) return IntPtr.Zero;
                        var column = Math.Clamp((x - 40) / 124, 0, 4);
                        var localX = (x - 40) % 124;
                        if (localX < 26) AdjustHueCorrection(colorPickerOwner, band, column, -1);
                        else if (localX >= 98) AdjustHueCorrection(colorPickerOwner, band, column, 1);
                        else return IntPtr.Zero;
                        InvalidateRect(hwnd, IntPtr.Zero, false);
                        return IntPtr.Zero;
                    }
                    if (y is >= 566 and < 616 && x is >= 195 and < 340)
                    {
                        var updated = settings with
                        {
                            HueCorrectionRedGain = 100,
                            HueCorrectionGreenGain = 100,
                            HueCorrectionBlueGain = 100,
                            HueCorrection2RedGain = 100,
                            HueCorrection2GreenGain = 100,
                            HueCorrection2BlueGain = 100,
                            HueCorrection3RedGain = 100,
                            HueCorrection3GreenGain = 100,
                            HueCorrection3BlueGain = 100
                        };
                        Interlocked.Exchange(ref forceOutputRefresh, 1);
                        SaveSettingsChange(colorPickerOwner, updated, "Hue correction gains reset");
                        InvalidateRect(hwnd, IntPtr.Zero, false);
                        return IntPtr.Zero;
                    }
                    if (y is >= 566 and < 616 && x is >= 350 and < 495)
                    {
                        ClearCalibrationPreview();
                        pendingPickerColor = ParseStaticColor(settings.StaticColorHex);
                        pendingHexText = $"#{settings.StaticColorHex.ToUpperInvariant()}";
                        hexInputActive = false;
                        InvalidateRect(hwnd, IntPtr.Zero, false);
                        return IntPtr.Zero;
                    }
                    if (y is >= 566 and < 616 && x is >= 505 and < 660)
                    {
                        ClearCalibrationPreview();
                        DestroyWindow(hwnd);
                        return IntPtr.Zero;
                    }
                    return IntPtr.Zero;
                }
                case PickerMessageClose:
                    ClearCalibrationPreview();
                    DestroyWindow(hwnd);
                    return IntPtr.Zero;
                case PickerMessageDestroy:
                    if (colorPickerWindow == hwnd)
                    {
                        ReleasePickerPaintBuffer();
                        colorPickerWindow = IntPtr.Zero;
                        activeColorSlider = -1;
                        var owner = colorPickerOwner;
                        colorPickerOwner = IntPtr.Zero;
                        if (owner != IntPtr.Zero)
                        {
                            _ = EnableWindow(owner, true);
                            _ = SetForegroundWindow(owner);
                            InvalidateRect(owner, IntPtr.Zero, false);
                        }
                    }
                    return IntPtr.Zero;
            }
        }
        catch (Exception ex)
        {
            status = "Color picker error: " + ex.Message;
            if (hwnd != IntPtr.Zero) DestroyWindow(hwnd);
        }
        return DefWindowProc(hwnd, message, wParam, lParam);
    }

    private static int ColorPickerSliderAt(int x, int y)
    {
        var row = y switch { >= 240 and < 300 => 0, >= 304 and < 364 => 1, >= 368 and < 428 => 2, _ => -1 };
        return row >= 0 && x is >= 150 and <= 560 ? row : -1;
    }

    private static (int X, int Y) ColorPickerClientPoint(IntPtr hwnd, IntPtr lParam)
    {
        var clientX = unchecked((short)((long)lParam & 0xFFFF));
        var clientY = unchecked((short)(((long)lParam >> 16) & 0xFFFF));
        if (!GetClientRect(hwnd, out var client) || client.Right <= 0 || client.Bottom <= 0)
            return (clientX, clientY);
        return new NativeUiCoordinateTransform(PickerLogicalWidth, PickerLogicalHeight,
            client.Right, client.Bottom).ToLogical(clientX, clientY);
    }

    private static void UpdatePickerSliderFromX(int x)
    {
        if (activeColorSlider < 0) return;
        var value = (byte)Math.Clamp((int)Math.Round(Math.Clamp(x - 160, 0, 392) * 255d / 391d), 0, 255);
        pendingPickerColor = activeColorSlider switch
        {
            0 => pendingPickerColor with { R = value },
            1 => pendingPickerColor with { G = value },
            _ => pendingPickerColor with { B = value }
        };
        pendingHexText = $"#{pendingPickerColor.R:X2}{pendingPickerColor.G:X2}{pendingPickerColor.B:X2}";
        if (colorPickerWindow != IntPtr.Zero) InvalidateRect(colorPickerWindow, IntPtr.Zero, false);
    }

    private static void AdjustHueCorrection(IntPtr owner, int band, int parameter, int direction)
    {
        var current = settings;
        var updated = band switch
        {
            0 => parameter switch
            {
                0 => current with { HueCorrectionStart = Math.Clamp(current.HueCorrectionStart + direction * 5, 0, current.HueCorrectionEnd - 5) },
                1 => current with { HueCorrectionEnd = Math.Clamp(current.HueCorrectionEnd + direction * 5, current.HueCorrectionStart + 5, 360) },
                2 => current with { HueCorrectionRedGain = Math.Clamp(current.HueCorrectionRedGain + direction * 5, 0, 200) },
                3 => current with { HueCorrectionGreenGain = Math.Clamp(current.HueCorrectionGreenGain + direction * 5, 0, 200) },
                _ => current with { HueCorrectionBlueGain = Math.Clamp(current.HueCorrectionBlueGain + direction * 5, 0, 200) }
            },
            1 => parameter switch
            {
                0 => current with { HueCorrection2Start = Math.Clamp(current.HueCorrection2Start + direction * 5, 0, current.HueCorrection2End - 5) },
                1 => current with { HueCorrection2End = Math.Clamp(current.HueCorrection2End + direction * 5, current.HueCorrection2Start + 5, 360) },
                2 => current with { HueCorrection2RedGain = Math.Clamp(current.HueCorrection2RedGain + direction * 5, 0, 200) },
                3 => current with { HueCorrection2GreenGain = Math.Clamp(current.HueCorrection2GreenGain + direction * 5, 0, 200) },
                _ => current with { HueCorrection2BlueGain = Math.Clamp(current.HueCorrection2BlueGain + direction * 5, 0, 200) }
            },
            _ => parameter switch
            {
                0 => current with { HueCorrection3Start = Math.Clamp(current.HueCorrection3Start + direction * 5, 0, current.HueCorrection3End - 5) },
                1 => current with { HueCorrection3End = Math.Clamp(current.HueCorrection3End + direction * 5, current.HueCorrection3Start + 5, 360) },
                2 => current with { HueCorrection3RedGain = Math.Clamp(current.HueCorrection3RedGain + direction * 5, 0, 200) },
                3 => current with { HueCorrection3GreenGain = Math.Clamp(current.HueCorrection3GreenGain + direction * 5, 0, 200) },
                _ => current with { HueCorrection3BlueGain = Math.Clamp(current.HueCorrection3BlueGain + direction * 5, 0, 200) }
            }
        };
        if (updated == current) return;
        Interlocked.Exchange(ref forceOutputRefresh, 1);
        SaveSettingsChange(owner, updated, $"Hue band {band + 1} saved");
    }

    private static void ApplyOutputColorGains(Span<ColorRgb> colors, AppSettings correction)
    {
        var hasBand1 = correction.HueCorrectionRedGain != 100 || correction.HueCorrectionGreenGain != 100 || correction.HueCorrectionBlueGain != 100;
        var hasBand2 = correction.HueCorrection2RedGain != 100 || correction.HueCorrection2GreenGain != 100 || correction.HueCorrection2BlueGain != 100;
        var hasBand3 = correction.HueCorrection3RedGain != 100 || correction.HueCorrection3GreenGain != 100 || correction.HueCorrection3BlueGain != 100;
        if (!hasBand1 && !hasBand2 && !hasBand3) return;
        for (var i = 0; i < colors.Length; i++)
        {
            var color = colors[i];
            if (hasBand1 && IsHueInRange(color, correction.HueCorrectionStart, correction.HueCorrectionEnd))
                colors[i] = ApplyGains(color, correction.HueCorrectionRedGain, correction.HueCorrectionGreenGain, correction.HueCorrectionBlueGain);
            else if (hasBand2 && IsHueInRange(color, correction.HueCorrection2Start, correction.HueCorrection2End))
                colors[i] = ApplyGains(color, correction.HueCorrection2RedGain, correction.HueCorrection2GreenGain, correction.HueCorrection2BlueGain);
            else if (hasBand3 && IsHueInRange(color, correction.HueCorrection3Start, correction.HueCorrection3End))
                colors[i] = ApplyGains(color, correction.HueCorrection3RedGain, correction.HueCorrection3GreenGain, correction.HueCorrection3BlueGain);
        }
    }

    private static ColorRgb ApplyGains(ColorRgb color, int red, int green, int blue) =>
        new(ScaleChannel(color.R, red), ScaleChannel(color.G, green), ScaleChannel(color.B, blue));

    private static bool IsHueInRange(ColorRgb color, int hueStart, int hueEnd)
    {
        var maximum = Math.Max(color.R, Math.Max(color.G, color.B));
        var minimum = Math.Min(color.R, Math.Min(color.G, color.B));
        var delta = maximum - minimum;
        if (delta < 24) return false;
        double hue;
        if (maximum == color.R)
        {
            hue = 60d * (color.G - color.B) / delta;
            if (hue < 0) hue += 360;
        }
        else if (maximum == color.G) hue = 60d * (color.B - color.R) / delta + 120;
        else hue = 60d * (color.R - color.G) / delta + 240;
        return hue >= hueStart && hue <= hueEnd;
    }

    private static byte ScaleChannel(byte value, int gain) =>
        (byte)Math.Clamp((int)Math.Round(value * gain / 100d), 0, 255);

    private static void ApplyPickerColor(IntPtr hwnd)
    {
        var hexDigits = pendingHexText.Trim().TrimStart('#');
        if (hexDigits.Length != 6 || !uint.TryParse(hexDigits,
                System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out _))
        {
            status = "Enter exactly six hexadecimal digits before saving the color.";
            InvalidateRect(hwnd, IntPtr.Zero, false);
            return;
        }
        var hex = $"{pendingPickerColor.R:X2}{pendingPickerColor.G:X2}{pendingPickerColor.B:X2}";
        ClearCalibrationPreview();
        hexInputActive = false;
        Interlocked.Exchange(ref forceOutputRefresh, 1);
        if (colorPickerOwner != IntPtr.Zero)
            SaveSettingsChange(colorPickerOwner, settings with { StaticColorHex = hex }, $"Custom base color · #{hex}");
        DestroyWindow(hwnd);
    }

    private static void ClearCalibrationPreview()
    {
        if (Interlocked.Exchange(ref calibrationPreviewColorPacked, -1) < 0) return;
        Interlocked.Exchange(ref forceOutputRefresh, 1);
        if (colorPickerOwner != IntPtr.Zero) InvalidateRect(colorPickerOwner, IntPtr.Zero, false);
    }

    private static void PaintColorPicker(IntPtr hwnd)
    {
        var paintHdc = BeginPaint(hwnd, out var paint);
        var hdc = paintHdc;
        var originalFont = IntPtr.Zero;
        try
        {
            if (!GetClientRect(hwnd, out var client) || client.Right <= 0 || client.Bottom <= 0) return;
            var usePickerBuffer = EnsurePickerPaintBuffer(paintHdc, client.Right, client.Bottom);
            hdc = usePickerBuffer ? pickerBufferDc : paintHdc;
            _ = SetMapMode(hdc, 1);
            _ = SetViewportOrgEx(hdc, 0, 0, IntPtr.Zero);
            var physicalBackground = new Rect { Right = client.Right, Bottom = client.Bottom };
            var physicalBrush = CreateSolidBrush(PaletteBackground);
            FillRect(hdc, ref physicalBackground, physicalBrush);
            DeleteObject(physicalBrush);

            _ = SetMapMode(hdc, 7);
            _ = SetWindowExtEx(hdc, PickerLogicalWidth, PickerLogicalHeight, IntPtr.Zero);
            var scale = Math.Min(client.Right / (double)PickerLogicalWidth, client.Bottom / (double)PickerLogicalHeight);
            var viewportWidth = Math.Max(1, (int)Math.Round(PickerLogicalWidth * scale));
            var viewportHeight = Math.Max(1, (int)Math.Round(PickerLogicalHeight * scale));
            _ = SetViewportExtEx(hdc, viewportWidth, viewportHeight, IntPtr.Zero);
            _ = SetViewportOrgEx(hdc, (client.Right - viewportWidth) / 2, (client.Bottom - viewportHeight) / 2, IntPtr.Zero);
            var background = new Rect { Right = PickerLogicalWidth, Bottom = PickerLogicalHeight };
            var backgroundBrush = CreateSolidBrush(PaletteBackground);
            FillRect(hdc, ref background, backgroundBrush);
            DeleteObject(backgroundBrush);
            SetBkMode(hdc, TextTransparent);
            if (uiFont != IntPtr.Zero) originalFont = SelectObject(hdc, uiFont);

            var header = new Rect { Left = 24, Top = 16, Right = 676, Bottom = 60 };
            var headerBrush = CreateSolidBrush(PalettePanel);
            FillRect(hdc, ref header, headerBrush);
            DeleteObject(headerBrush);
            SetTextColor(hdc, PaletteAccentTeal);
            DrawText(hdc, "CUSTOM COLOR PICKER", -1, ref header, TextButtonCentered);

            var preview = new Rect { Left = 40, Top = 76, Right = 132, Bottom = 118 };
            var previewBrush = CreateSolidBrush(ColorRef(pendingPickerColor));
            FillRect(hdc, ref preview, previewBrush);
            DeleteObject(previewBrush);
            SetTextColor(hdc, PaletteText);
            var hexField = new Rect { Left = 150, Top = 76, Right = 660, Bottom = 118 };
            var hexBrush = CreateSolidBrush(hexInputActive ? PaletteButton : PalettePanel);
            FillRect(hdc, ref hexField, hexBrush);
            DeleteObject(hexBrush);
            SetTextColor(hdc, PaletteText);
            DrawText(hdc, hexInputActive ? $"HEX  {pendingHexText}|   ·   type 6 hex digits" : $"HEX  {pendingHexText}   ·   click to enter a hex color",
                -1, ref hexField, TextButtonCentered);

            for (var i = 0; i < BaseColorPresets.All.Count; i++)
            {
                var preset = BaseColorPresets.All[i];
                var left = 40 + i % 4 * 155;
                var top = 132 + i / 4 * 48;
                var swatchButton = new Rect { Left = left, Top = top, Right = left + 145, Bottom = top + 42 };
                var presetBrush = CreateSolidBrush(PaletteButton);
                FillRect(hdc, ref swatchButton, presetBrush);
                DeleteObject(presetBrush);
                var presetColor = ParseStaticColor(preset.Hex);
                var presetSwatch = new Rect { Left = left + 7, Top = top + 10, Right = left + 28, Bottom = top + 31 };
                var presetColorBrush = CreateSolidBrush(ColorRef(presetColor));
                FillRect(hdc, ref presetSwatch, presetColorBrush);
                DeleteObject(presetColorBrush);
                SetTextColor(hdc, PaletteText);
                var presetName = new Rect { Left = left + 32, Top = top, Right = left + 141, Bottom = top + 42 };
                DrawText(hdc, preset.Name, -1, ref presetName, TextButtonCentered);
            }

            SetTextColor(hdc, PaletteAccentTeal);
            var presetHeading = new Rect { Left = 40, Top = 116, Right = 660, Bottom = 132 };
            DrawText(hdc, "CALIBRATION COLORS  ·  CLICK TO PREVIEW ON LEDS", -1, ref presetHeading, TextButtonCentered);

            DrawColorChannelSlider(hdc, 0, "RED", pendingPickerColor.R, 240);
            DrawColorChannelSlider(hdc, 1, "GREEN", pendingPickerColor.G, 304);
            DrawColorChannelSlider(hdc, 2, "BLUE", pendingPickerColor.B, 368);
            SetTextColor(hdc, PaletteAccentTeal);
            var correction = settings;
            var gainHeading = new Rect { Left = 40, Top = 410, Right = 660, Bottom = 438 };
            DrawText(hdc, "SAVED HUE CORRECTIONS  ·  THREE INDEPENDENT BANDS  ·  PER-BAND RGB GAINS", -1, ref gainHeading, TextButtonCentered);
            var correctionValues = new[]
            {
                new[] { $"H1 LO {correction.HueCorrectionStart}°", $"H1 HI {correction.HueCorrectionEnd}°", $"R {correction.HueCorrectionRedGain}%", $"G {correction.HueCorrectionGreenGain}%", $"B {correction.HueCorrectionBlueGain}%" },
                new[] { $"H2 LO {correction.HueCorrection2Start}°", $"H2 HI {correction.HueCorrection2End}°", $"R {correction.HueCorrection2RedGain}%", $"G {correction.HueCorrection2GreenGain}%", $"B {correction.HueCorrection2BlueGain}%" },
                new[] { $"H3 LO {correction.HueCorrection3Start}°", $"H3 HI {correction.HueCorrection3End}°", $"R {correction.HueCorrection3RedGain}%", $"G {correction.HueCorrection3GreenGain}%", $"B {correction.HueCorrection3BlueGain}%" }
            };
            for (var band = 0; band < correctionValues.Length; band++)
            {
                var rowY = 440 + band * 38;
                for (var parameter = 0; parameter < correctionValues[band].Length; parameter++)
                {
                    var x = 40 + parameter * 124;
                    var minus = new Rect { Left = x, Top = rowY, Right = x + 24, Bottom = rowY + 32 };
                    var value = new Rect { Left = x + 25, Top = rowY, Right = x + 99, Bottom = rowY + 32 };
                    var plus = new Rect { Left = x + 100, Top = rowY, Right = x + 124, Bottom = rowY + 32 };
                    DrawColorPickerButton(hdc, minus, "−", PaletteButton);
                    DrawColorPickerButton(hdc, plus, "+", PaletteButton);
                    SetTextColor(hdc, PaletteSecondary);
                    DrawText(hdc, correctionValues[band][parameter], -1, ref value, TextButtonCentered);
                }
            }
            DrawColorPickerButton(hdc, new Rect { Left = 40, Top = 566, Right = 185, Bottom = 616 }, "SAVE COLOR", PaletteActive);
            DrawColorPickerButton(hdc, new Rect { Left = 195, Top = 566, Right = 340, Bottom = 616 }, "NEUTRAL RGB", PaletteButton);
            DrawColorPickerButton(hdc, new Rect { Left = 350, Top = 566, Right = 495, Bottom = 616 }, "STOP PREVIEW", PaletteButton);
            DrawColorPickerButton(hdc, new Rect { Left = 505, Top = 566, Right = 660, Bottom = 616 }, "CLOSE", PaletteButton);
            if (usePickerBuffer)
            {
                _ = SetMapMode(pickerBufferDc, 1);
                _ = BitBlt(paintHdc, 0, 0, client.Right, client.Bottom, pickerBufferDc, 0, 0, 0x00CC0020);
            }
        }
        finally
        {
            if (originalFont != IntPtr.Zero && originalFont != new IntPtr(-1)) _ = SelectObject(hdc, originalFont);
            EndPaint(hwnd, ref paint);
        }
    }

    private static void DrawColorChannelSlider(IntPtr hdc, int channel, string label, byte selectedValue, int y)
    {
        SetTextColor(hdc, PaletteSecondary);
        var name = new Rect { Left = 40, Top = y, Right = 138, Bottom = y + 40 };
        DrawText(hdc, label, -1, ref name, TextButtonCentered);
        var track = new Rect { Left = 160, Top = y + 10, Right = 552, Bottom = y + 30 };
        var trackBackground = CreateSolidBrush(PalettePanel);
        FillRect(hdc, ref track, trackBackground);
        DeleteObject(trackBackground);
        var dcBrush = GetStockObject(18);
        const int segments = 48;
        var width = track.Right - track.Left;
        for (var i = 0; i < segments; i++)
        {
            var amount = (byte)(i * 255 / (segments - 1));
            var color = channel switch
            {
                0 => new ColorRgb(amount, pendingPickerColor.G, pendingPickerColor.B),
                1 => new ColorRgb(pendingPickerColor.R, amount, pendingPickerColor.B),
                _ => new ColorRgb(pendingPickerColor.R, pendingPickerColor.G, amount)
            };
            _ = SetDCBrushColor(hdc, ColorRef(color));
            var left = track.Left + i * width / segments;
            var right = track.Left + (i + 1) * width / segments;
            var segment = new Rect { Left = left, Top = track.Top, Right = right, Bottom = track.Bottom };
            FillRect(hdc, ref segment, dcBrush);
        }
        var thumbX = track.Left + selectedValue * width / 255;
        var thumb = new Rect { Left = thumbX - 2, Top = track.Top - 4, Right = thumbX + 3, Bottom = track.Bottom + 4 };
        var thumbBrush = CreateSolidBrush(PaletteText);
        FillRect(hdc, ref thumb, thumbBrush);
        DeleteObject(thumbBrush);
        SetTextColor(hdc, PaletteText);
        var value = new Rect { Left = 570, Top = y, Right = 660, Bottom = y + 40 };
        DrawText(hdc, selectedValue.ToString("000"), -1, ref value, TextButtonCentered);
    }

    private static void DrawColorPickerButton(IntPtr hdc, Rect rect, string label, uint fillColor)
    {
        var brush = CreateSolidBrush(fillColor);
        FillRect(hdc, ref rect, brush);
        DeleteObject(brush);
        SetTextColor(hdc, PaletteText);
        DrawText(hdc, label, -1, ref rect, TextButtonCentered);
    }

    private static uint ColorRef(ColorRgb color) => (uint)(color.R | color.G << 8 | color.B << 16);

    private static bool EnsurePickerPaintBuffer(IntPtr referenceDc, int width, int height)
    {
        if (pickerBufferDc != IntPtr.Zero && pickerBufferWidth == width && pickerBufferHeight == height) return true;
        ReleasePickerPaintBuffer();
        var dc = CreateCompatibleDC(referenceDc);
        if (dc == IntPtr.Zero) return false;
        var bitmap = CreateCompatibleBitmap(referenceDc, width, height);
        if (bitmap == IntPtr.Zero)
        {
            _ = DeleteDC(dc);
            return false;
        }
        var previous = SelectObject(dc, bitmap);
        if (previous == IntPtr.Zero || previous == new IntPtr(-1))
        {
            _ = DeleteObject(bitmap);
            _ = DeleteDC(dc);
            return false;
        }
        pickerBufferDc = dc;
        pickerBufferBitmap = bitmap;
        pickerBufferPreviousBitmap = previous;
        pickerBufferWidth = width;
        pickerBufferHeight = height;
        return true;
    }

    private static void ReleasePickerPaintBuffer()
    {
        if (pickerBufferDc != IntPtr.Zero && pickerBufferPreviousBitmap != IntPtr.Zero)
            _ = SelectObject(pickerBufferDc, pickerBufferPreviousBitmap);
        if (pickerBufferBitmap != IntPtr.Zero) _ = DeleteObject(pickerBufferBitmap);
        if (pickerBufferDc != IntPtr.Zero) _ = DeleteDC(pickerBufferDc);
        pickerBufferDc = IntPtr.Zero;
        pickerBufferBitmap = IntPtr.Zero;
        pickerBufferPreviousBitmap = IntPtr.Zero;
        pickerBufferWidth = pickerBufferHeight = 0;
    }

    private static string NativeModeName(int mode) => mode switch
    {
        0 => "Screen Sync", 1 => "Rainbow", 2 => "Color cycle", 3 => "Breathing",
        4 => "Music Sync", 5 => "Static color", _ => "Unknown mode"
    };

    private static ColorRgb ParseStaticColor(string hex)
    {
        if (hex.Length != 6 || !uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out var value))
            return new ColorRgb(255, 255, 255);
        return new ColorRgb((byte)(value >> 16), (byte)(value >> 8), (byte)value);
    }

    private static void SaveSettingsChange(IntPtr hwnd, AppSettings updated, string successStatus)
    {
        settings = updated;
        try
        {
            SettingsStore.Save(settings);
            status = successStatus;
        }
        catch (Exception ex) { status = "Preference could not be saved: " + ex.Message; }
        InvalidateRect(hwnd, IntPtr.Zero, false);
    }

    private static void ToggleWindowsStartup(IntPtr hwnd)
    {
        var requested = !startWithWindows;
        try
        {
            WindowsStartupRegistration.SetEnabled(requested, GetExecutablePath());
            startWithWindows = requested;
            status = requested
                ? "LumaSync will start with Windows for this user."
                : "LumaSync was removed from this user's Windows startup.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException)
        {
            status = "Could not update this user's Windows startup setting: " + ex.Message;
        }
        InvalidateRect(hwnd, IntPtr.Zero, false);
    }

    private static void ResetToDefaults(IntPtr hwnd)
    {
        var answer = MessageBox(hwnd,
            "Restore the default layout and lighting settings? Lighting will stop and the LEDs will turn off. Windows autostart will also be disabled.",
            "Reset LumaSync", MessageBoxYesNo | MessageBoxIconQuestion);
        if (answer != 6) return;

        lightingEnabled = false;
        if (!StopCaptureWorker())
        {
            status = "Could not safely stop capture; default settings were not applied.";
            InvalidateRect(hwnd, IntPtr.Zero, false);
            return;
        }

        var defaults = SettingsResetPolicy.Reset(settings);
        settings = defaults;
        Layout = new LayoutOptions(defaults.Top, defaults.Right, defaults.Bottom, defaults.Left,
            defaults.SamplingDepth / 100d, defaults.Clockwise, defaults.StartCorner);
        ledLayout = Layout.Create().ToArray();
        ledColors = new ColorRgb[ledLayout.Length];
        previousColors = new ColorRgb[ledLayout.Length];
        screenSyncFrame = new byte[7 + ledLayout.Length * 5];
        Processor.Reset();
        tuningLabelSettings = null;
        effectStartedTimestamp = Stopwatch.GetTimestamp();
        Interlocked.Exchange(ref forceOutputRefresh, 1);

        string? startupError = null;
        try
        {
            WindowsStartupRegistration.SetEnabled(false, GetExecutablePath());
            startWithWindows = false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException)
        { startupError = ex.Message; }
        try { SettingsStore.Save(settings); }
        catch (Exception ex) { status = "Defaults applied but settings could not be saved: " + ex.Message; }
        if (startupError is not null) status = "Defaults applied; Windows autostart could not be disabled: " + startupError;
        else if (!status.StartsWith("Defaults applied but", StringComparison.Ordinal)) status = "Default settings restored · lighting off";

        try { availableDisplays = DisplayMonitorEnumerator.GetDisplays(); }
        catch { availableDisplays = []; }
        selectedDisplay = availableDisplays.FirstOrDefault(d => d.IsPrimary);
        if (string.IsNullOrWhiteSpace(selectedDisplay.DeviceName))
            selectedDisplay = availableDisplays.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(selectedDisplay.DeviceName)) displayName = selectedDisplay.DeviceName;
        else status = "Default settings restored; no display is currently available.";

        try { StartCaptureWorker(hwnd); }
        catch (Exception ex) { status = "Defaults restored; capture could not restart · " + ex.Message; }
        InvalidateRect(hwnd, IntPtr.Zero, false);
    }

    private static void ImportSettings(IntPtr hwnd)
    {
        string file;
        try
        {
            if (!TryShowSettingsFileDialog(hwnd, save: false, out file, out var dialogError))
            {
                if (dialogError != 0)
                {
                    status = $"Settings file picker failed · Windows error 0x{dialogError:X8}";
                    InvalidateRect(hwnd, IntPtr.Zero, false);
                }
                return;
            }
            if (!SettingsTransfer.TryImport(file, out var imported, out var error))
                throw new InvalidDataException(error ?? "Settings file is invalid.");

            availableDisplays = DisplayMonitorEnumerator.GetDisplays();
            var targetDisplay = availableDisplays.FirstOrDefault(d =>
                !string.IsNullOrWhiteSpace(imported.DisplayDeviceName)
                && string.Equals(d.DeviceName, imported.DisplayDeviceName, StringComparison.OrdinalIgnoreCase));
            var displayFallback = string.IsNullOrWhiteSpace(targetDisplay.DeviceName);
            if (displayFallback)
            {
                targetDisplay = availableDisplays.FirstOrDefault(d =>
                    string.Equals(d.DeviceName, displayName, StringComparison.OrdinalIgnoreCase));
                if (string.IsNullOrWhiteSpace(targetDisplay.DeviceName))
                    targetDisplay = availableDisplays.FirstOrDefault(d => d.IsPrimary);
                if (string.IsNullOrWhiteSpace(targetDisplay.DeviceName))
                    throw new InvalidOperationException("No display is currently available.");
                imported = imported with { DisplayDeviceName = targetDisplay.DeviceName };
            }

            effectStartedTimestamp = Stopwatch.GetTimestamp();
            Interlocked.Exchange(ref forceOutputRefresh, 1);
            ApplyMappingSettingsChange(hwnd, imported,
                displayFallback ? "Settings imported · saved display unavailable; using current display" : "Settings imported",
                targetDisplay, imported.LightingEnabled);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or System.Security.SecurityException or ArgumentException or InvalidOperationException)
        {
            status = "Settings import failed · " + ex.Message;
            InvalidateRect(hwnd, IntPtr.Zero, false);
        }
    }

    private static void ExportSettings(IntPtr hwnd)
    {
        try
        {
            if (!TryShowSettingsFileDialog(hwnd, save: true, out var file, out var dialogError))
            {
                if (dialogError != 0)
                {
                    status = $"Settings file picker failed · Windows error 0x{dialogError:X8}";
                    InvalidateRect(hwnd, IntPtr.Zero, false);
                }
                return;
            }
            SettingsTransfer.Export(file, settings);
            status = "Settings exported locally";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or System.Security.SecurityException or ArgumentException)
        {
            status = "Settings export failed · " + ex.Message;
        }
        InvalidateRect(hwnd, IntPtr.Zero, false);
    }

    private static bool TryShowSettingsFileDialog(IntPtr hwnd, bool save, out string file, out uint error)
    {
        const int maxPathCharacters = 32_768;
        var fileBuffer = Marshal.AllocHGlobal(maxPathCharacters * sizeof(char));
        var filterBuffer = Marshal.StringToHGlobalUni("LumaSync settings (*.json)\0*.json\0All files (*.*)\0*.*\0\0");
        var titleBuffer = Marshal.StringToHGlobalUni("LumaSync · Settings");
        var extensionBuffer = Marshal.StringToHGlobalUni("json");
        try
        {
            var initialName = ("LumaSync-settings.json\0").ToCharArray();
            Marshal.Copy(initialName, 0, fileBuffer, initialName.Length);
            var dialog = new OpenFileName
            {
                Size = Marshal.SizeOf<OpenFileName>(),
                Owner = hwnd,
                Filter = filterBuffer,
                FilterIndex = 1,
                File = fileBuffer,
                MaxFile = maxPathCharacters,
                Title = titleBuffer,
                Flags = save
                    ? 0x00000002 | 0x00000800 | 0x00000008 | 0x00080000
                    : 0x00001000 | 0x00000800 | 0x00000008 | 0x00080000 | 0x00000004,
                DefaultExtension = extensionBuffer
            };
            var accepted = save ? GetSaveFileName(ref dialog) : GetOpenFileName(ref dialog);
            error = accepted ? 0 : CommDlgExtendedError();
            file = accepted ? Marshal.PtrToStringUni(fileBuffer) ?? string.Empty : string.Empty;
            return accepted;
        }
        finally
        {
            Marshal.FreeHGlobal(fileBuffer);
            Marshal.FreeHGlobal(filterBuffer);
            Marshal.FreeHGlobal(titleBuffer);
            Marshal.FreeHGlobal(extensionBuffer);
        }
    }

    private static string GetExecutablePath() => Environment.ProcessPath
        ?? Path.Combine(AppContext.BaseDirectory, "LumaSync.exe");

    private static void StartCaptureWorker(IntPtr hwnd)
    {
        capture = DxgiCaptureSurface.Open(displayName);
        Interlocked.Exchange(ref captureRetryAt, 0);
        Interlocked.Exchange(ref processedFrames, 0);
        Interlocked.Exchange(ref queuedFrames, 0);
        Interlocked.Exchange(ref changedFrames, 0);
        Interlocked.Exchange(ref outputFrames, 0);
        Interlocked.Exchange(ref droppedCaptureFrames, 0);
        Interlocked.Exchange(ref lastProcessingTicks, 0);
        Interlocked.Exchange(ref lastUsbWriteTicks, 0);
        Interlocked.Exchange(ref lastEndToEndTicks, 0);
        Interlocked.Exchange(ref diagnosticWindowTimestamp, 0);
        Interlocked.Exchange(ref diagnosticPreviousCaptureFrames, 0);
        Interlocked.Exchange(ref diagnosticPreviousChangedFrames, 0);
        Interlocked.Exchange(ref diagnosticPreviousQueuedFrames, 0);
        Interlocked.Exchange(ref diagnosticPreviousOutputFrames, 0);
        Interlocked.Exchange(ref diagnosticPreviousCandidateFrames, 0);
        Interlocked.Exchange(ref diagnosticPreviousPacingRejectedFrames, 0);
        Interlocked.Exchange(ref recentCaptureMilliFps, 0);
        Interlocked.Exchange(ref recentChangedMilliFps, 0);
        Interlocked.Exchange(ref recentQueuedMilliFps, 0);
        Interlocked.Exchange(ref recentOutputMilliFps, 0);
        Interlocked.Exchange(ref recentCandidateMilliFps, 0);
        Interlocked.Exchange(ref recentPacingRejectedMilliFps, 0);
        captureCancellation = new CancellationTokenSource();
        var openedCapture = capture!;
        var cancellationToken = captureCancellation.Token;
        status = $"DXGI active on {displayName} · {selectedDisplay.Bounds.Width}×{selectedDisplay.Bounds.Height}";
        captureTask = Task.Run(() => CaptureLoop(hwnd, openedCapture, cancellationToken));
    }

    private static void TryRestartStoppedCapture(IntPtr hwnd)
    {
        var task = captureTask;
        if (task is { IsCompleted: false }) return;
        var retryAt = Interlocked.Read(ref captureRetryAt);
        if (retryAt == 0 || Stopwatch.GetTimestamp() < retryAt) return;
        try
        {
            captureTask = null;
            StartCaptureWorker(hwnd);
        }
        catch (Exception ex)
        {
            status = "Screen capture reconnecting · " + ex.Message;
            Interlocked.Exchange(ref captureRetryAt, Stopwatch.GetTimestamp() + 3 * Stopwatch.Frequency);
            InvalidateRect(hwnd, IntPtr.Zero, false);
        }
    }

    private static bool StopCaptureWorker()
    {
        captureCancellation?.Cancel();
        if (captureTask is not null)
        {
            try
            {
                if (!captureTask.Wait(TimeSpan.FromSeconds(3))) return false;
            }
            catch (AggregateException) { }
            captureTask = null;
        }
        captureCancellation?.Dispose();
        captureCancellation = null;
        capture?.Dispose();
        capture = null;
        return true;
    }

    private static void ApplyLayoutChange(IntPtr hwnd, LedEdge edge, int delta)
    {
        AppSettings updated;
        try { updated = LedLayoutEditor.ChangeEdgeCount(settings, edge, delta); }
        catch (Exception ex)
        {
            status = $"LED count unchanged · {ex.Message}";
            InvalidateRect(hwnd, IntPtr.Zero, false);
            return;
        }
        ApplyMappingSettingsChange(hwnd, updated, "LED layout saved · capture restarted");
    }

    private static void ApplyMappingSettingsChange(IntPtr hwnd, AppSettings updated, string successMessage,
        DisplayTarget? nextDisplay = null, bool? nextLightingEnabled = null)
    {
        if (!StopCaptureWorker())
        {
            status = "Could not safely stop capture; mapping settings were not changed.";
            InvalidateRect(hwnd, IntPtr.Zero, false);
            return;
        }

        settings = updated;
        if (nextLightingEnabled is { } enabled) lightingEnabled = enabled;
        if (nextDisplay is { } display)
        {
            selectedDisplay = display;
            displayName = display.DeviceName;
        }
        Layout = new LayoutOptions(settings.Top, settings.Right, settings.Bottom, settings.Left,
            settings.SamplingDepth / 100d, settings.Clockwise, settings.StartCorner);
        ledLayout = Layout.Create().ToArray();
        ledColors = new ColorRgb[ledLayout.Length];
        previousColors = new ColorRgb[ledLayout.Length];
        screenSyncFrame = new byte[7 + ledLayout.Length * 5];
        Processor.Reset();
        string? saveWarning = null;
        try { SettingsStore.Save(settings); }
        catch (Exception ex) { saveWarning = ex.Message; }
        try
        {
            StartCaptureWorker(hwnd);
            var controllerCount = reportedControllerLedCount > 0 ? reportedControllerLedCount : ledLayout.Length;
            status = saveWarning is not null
                ? $"Settings applied but could not be saved · {saveWarning}"
                : ledLayout.Length == controllerCount
                    ? successMessage
                    : $"Layout has {ledLayout.Length} LEDs; controller has {controllerCount}. Match totals to resume output.";
        }
        catch (Exception ex) { status = $"Settings applied; capture could not restart · {ex.Message}"; }
        InvalidateRect(hwnd, IntPtr.Zero, false);
    }

    private static void SelectNextDisplay(IntPtr hwnd)
    {
        try { availableDisplays = DisplayMonitorEnumerator.GetDisplays(); }
        catch (Exception ex)
        {
            status = $"Could not enumerate displays · {ex.Message}";
            InvalidateRect(hwnd, IntPtr.Zero, false);
            return;
        }
        if (availableDisplays.Length == 0)
        {
            status = "No display is currently available.";
            InvalidateRect(hwnd, IntPtr.Zero, false);
            return;
        }

        var currentIndex = Array.FindIndex(availableDisplays,
            d => string.Equals(d.DeviceName, displayName, StringComparison.OrdinalIgnoreCase));
        if (availableDisplays.Length == 1 && currentIndex == 0)
        {
            status = "Only one display is currently available.";
            InvalidateRect(hwnd, IntPtr.Zero, false);
            return;
        }
        var nextIndex = DisplaySelectionPolicy.NextIndex(availableDisplays.Length, currentIndex);
        var nextDisplay = availableDisplays[nextIndex];
        ApplyMappingSettingsChange(hwnd, settings with { DisplayDeviceName = nextDisplay.DeviceName },
            $"Selected {nextDisplay.DeviceName} · {nextDisplay.Bounds.Width}×{nextDisplay.Bounds.Height}", nextDisplay);
    }

    private static bool TryGetEdgeEdit(int x, int y, out LedEdge edge, out int delta)
    {
        edge = default;
        delta = 0;
        if (y is < 606 or > 634) return false;
        for (var i = 0; i < 4; i++)
        {
            var left = 80 + i * 190;
            if (x >= left && x < left + 34) { edge = (LedEdge)i; delta = -1; return true; }
            if (x >= left + 136 && x < left + 170) { edge = (LedEdge)i; delta = 1; return true; }
        }
        return false;
    }

    private static bool TryGetTuningEdit(int x, int y, out ScreenSyncTuning tuning, out int delta)
        => ScreenSyncSettingsEditor.TryMapControl(x, y, out tuning, out delta);

    private static void ApplyTuningChange(IntPtr hwnd, ScreenSyncTuning tuning, int delta)
    {
        var updated = ScreenSyncSettingsEditor.Adjust(settings, tuning, delta);
        if (updated == settings) return;
        if (tuning is ScreenSyncTuning.SamplingDepth or ScreenSyncTuning.CaptureRate)
        {
            ApplyMappingSettingsChange(hwnd, updated, tuning == ScreenSyncTuning.SamplingDepth
                ? $"Sampling depth · {updated.SamplingDepth:0}% · capture restarted"
                : $"Capture rate · {updated.CaptureRate} Hz · capture restarted");
            return;
        }
        var label = tuning switch
        {
            ScreenSyncTuning.Brightness => $"Brightness · {updated.Brightness:0}%",
            ScreenSyncTuning.Smoothing => $"Smoothing · {updated.Smoothing:0}%",
            ScreenSyncTuning.Gamma => $"Gamma · {updated.Gamma:0.0}",
            ScreenSyncTuning.Saturation => $"Saturation · {updated.Saturation:0}%",
            ScreenSyncTuning.ColorTemperature => $"Color temperature · {updated.ColorTemperature:0} K",
            ScreenSyncTuning.OutputRate => $"LED output limit · {OutputPacing.EffectiveRate(updated.CaptureRate, updated.OutputRate)} Hz",
            ScreenSyncTuning.LetterboxDetection => updated.DetectLetterbox ? "Letterbox detection enabled" : "Letterbox detection disabled",
            ScreenSyncTuning.CropLeft => $"Left screen exclusion · {updated.CropLeftPercent:0}%",
            ScreenSyncTuning.CropRight => $"Right screen exclusion · {updated.CropRightPercent:0}%",
            ScreenSyncTuning.CropTop => $"Top screen exclusion · {updated.CropTopPercent:0}%",
            ScreenSyncTuning.CropBottom => $"Bottom screen exclusion · {updated.CropBottomPercent:0}%",
            _ => "Screen Sync settings saved"
        };
        Interlocked.Exchange(ref forceOutputRefresh, 1);
        SaveSettingsChange(hwnd, updated, label);
    }

    private static void CaptureLoop(IntPtr hwnd, DxgiCaptureSurface source, CancellationToken cancellationToken)
    {
        using (source)
        {
            WindowsHidController? controller = null;
            WasapiLoopbackCapture? audioCapture = null;
            var reconnectFailures = 0;
            var reconnectAt = 0L;
            var audioRetryAt = 0L;
            var lastPreviewPaint = 0L;
            var audioIssue = "Windows audio loopback is initializing.";
            LatestFrameOutputPump? outputPump = null;
            try
            {
                var periodTicks = Math.Max(1L, (long)(Stopwatch.Frequency / settings.CaptureRate));
                var nextPulse = Stopwatch.GetTimestamp();
                var previousOutputTimestamp = 0L;
                var outputNeedsBlack = true;
                var firstFrameForEnabledState = true;
                AppSettings? previousSampleSettings = null;

                void QueueOutput(long queuedAt, long sampleStarted, bool isStatic, ColorRgb staticColor, byte outputSequence)
                {
                    if (outputPump is null) return;
                    var accepted = isStatic
                        ? outputPump.QueueStatic(staticColor, outputSequence, sampleStarted)
                        : outputPump.QueueFrame(screenSyncFrame, sampleStarted);
                    if (accepted)
                    {
                        Interlocked.Increment(ref queuedFrames);
                        previousOutputTimestamp = queuedAt;
                    }
                }

                using var pulse = new HighResolutionPulse();
                while (!cancellationToken.IsCancellationRequested)
                {
                    nextPulse += periodTicks;
                    var remainingTicks = nextPulse - Stopwatch.GetTimestamp();
                    if (remainingTicks > 0 && !pulse.Wait(TimeSpan.FromSeconds(remainingTicks / (double)Stopwatch.Frequency), cancellationToken)) break;
                    var now = Stopwatch.GetTimestamp();
                    var lateness = now - nextPulse;
                    if (lateness > periodTicks)
                    {
                        Interlocked.Add(ref droppedCaptureFrames, lateness / periodTicks);
                        nextPulse = now;
                    }
                    if (outputPump?.Failure is { } outputError)
                    {
                        outputPump.Dispose();
                        outputPump = null;
                        outputNeedsBlack = true;
                        firstFrameForEnabledState = true;
                        ScheduleControllerReconnect(ref controller, outputError, hwnd, ref reconnectFailures,
                            ref reconnectAt, ref previousOutputTimestamp);
                    }
                    // Keep all tunable values coherent for this frame while the UI saves live edits.
                    var frameSettings = settings;
                    var sampleReusedFrame = frameSettings.Smoothing > 0 || frameSettings != previousSampleSettings;
                    previousSampleSettings = frameSettings;
                    if (controller is null)
                    {
                        if (now < reconnectAt) continue;
                        try
                        {
                            var candidate = WindowsHidController.ConnectAndReadInfo();
                            reportedControllerLedCount = candidate.Info.LedCount;
                            if (candidate.Info.LedCount != ledLayout.Length)
                            {
                                var foundCount = candidate.Info.LedCount;
                                candidate.Dispose();
                                throw new InvalidOperationException($"Layout has {ledLayout.Length} LEDs but the controller reports {foundCount}.");
                            }
                            controller = candidate;
                            outputPump = new LatestFrameOutputPump(candidate, screenSyncFrame.Length, RecordOutput,
                                () => Interlocked.Increment(ref droppedCaptureFrames));
                            Interlocked.Exchange(ref controllerConnected, 1);
                            reconnectFailures = 0;
                            reconnectAt = 0;
                            outputNeedsBlack = true;
                            firstFrameForEnabledState = true;
                            previousOutputTimestamp = 0;
                            status = $"Connected · 1A86:FE07 · {controller.Info.LedCount} LEDs · firmware {controller.Info.Firmware}";
                            InvalidateRect(hwnd, IntPtr.Zero, false);
                        }
                        catch (Exception ex)
                        {
                            ScheduleControllerReconnect(ref controller, ex, hwnd, ref reconnectFailures,
                                ref reconnectAt, ref previousOutputTimestamp);
                            continue;
                        }
                    }
                    if (!lightingEnabled)
                    {
                        audioCapture?.Dispose();
                        audioCapture = null;
                        audioRetryAt = 0;
                        if (outputNeedsBlack)
                        {
                            Array.Clear(ledColors);
                            var blackSequence = NextSequence();
                            ScreenSyncFrameBuilder.BuildInto(ledColors, blackSequence, screenSyncFrame);
                            QueueOutput(now, now, isStatic: false, default, blackSequence);
                            outputNeedsBlack = false;
                            firstFrameForEnabledState = true;
                            InvalidateRect(hwnd, IntPtr.Zero, false);
                        }
                        continue;
                    }
                    var previewColorPacked = Volatile.Read(ref calibrationPreviewColorPacked);
                    var modeIndex = previewColorPacked >= 0 ? 5 : frameSettings.ModeIndex;
                    if (modeIndex != 4 && audioCapture is not null)
                    {
                        audioCapture.Dispose();
                        audioCapture = null;
                        audioRetryAt = 0;
                    }
                    if (modeIndex == 4)
                    {
                        if (audioCapture is null && now >= audioRetryAt)
                        {
                            var candidateAudio = new WasapiLoopbackCapture();
                            try
                            {
                                candidateAudio.StartAsync(cancellationToken).GetAwaiter().GetResult();
                                audioCapture = candidateAudio;
                                audioIssue = string.Empty;
                                Interlocked.Exchange(ref forceOutputRefresh, 1);
                                status = "Music Sync · local desktop-audio loopback active";
                                InvalidateRect(hwnd, IntPtr.Zero, false);
                            }
                            catch (Exception ex)
                            {
                                candidateAudio.Dispose();
                                audioRetryAt = now + 3 * Stopwatch.Frequency;
                                audioIssue = $"Music Sync unavailable · {ex.GetBaseException().Message}";
                            }
                        }
                        if (audioCapture is { IsRunning: false })
                        {
                            audioIssue = audioCapture.Failure?.GetBaseException().Message ?? "Audio loopback stopped.";
                            audioCapture.Dispose();
                            audioCapture = null;
                            audioRetryAt = now + 3 * Stopwatch.Frequency;
                        }
                        if (audioCapture is null)
                        {
                            if (outputNeedsBlack)
                            {
                                Array.Clear(ledColors);
                                var blackSequence = NextSequence();
                                ScreenSyncFrameBuilder.BuildInto(ledColors, blackSequence, screenSyncFrame);
                                QueueOutput(now, now, isStatic: false, default, blackSequence);
                                outputNeedsBlack = false;
                                firstFrameForEnabledState = true;
                                InvalidateRect(hwnd, IntPtr.Zero, false);
                            }
                            if (!string.Equals(status, audioIssue, StringComparison.Ordinal))
                            {
                                status = audioIssue;
                                InvalidateRect(hwnd, IntPtr.Zero, false);
                            }
                            continue;
                        }
                    }
                    outputNeedsBlack = true;
                    var processingStarted = Stopwatch.GetTimestamp();
                    try
                    {
                        if (modeIndex == 0)
                        {
                            source.SampleInto(Processor, Layout, ledColors,
                                brightness: frameSettings.Brightness / 100d,
                                smoothing: frameSettings.Smoothing / 100d,
                                gamma: frameSettings.Gamma,
                                saturation: frameSettings.Saturation / 100d,
                                colorTemperature: frameSettings.ColorTemperature,
                                detectLetterbox: frameSettings.DetectLetterbox,
                                crop: new ScreenCrop(frameSettings.CropLeftPercent / 100d, frameSettings.CropRightPercent / 100d,
                                    frameSettings.CropTopPercent / 100d, frameSettings.CropBottomPercent / 100d),
                                sampleReusedFrame: sampleReusedFrame);
                        }
                        else if (modeIndex is >= 1 and <= 3)
                        {
                            EffectEngine.RenderInto((LightingEffect)(modeIndex - 1),
                                TimeSpan.FromSeconds((now - Interlocked.Read(ref effectStartedTimestamp)) / (double)Stopwatch.Frequency),
                                ledColors, ParseStaticColor(frameSettings.StaticColorHex), frameSettings.Brightness / 100d,
                                frameSettings.EffectSpeed / 100d);
                        }
                        else if (modeIndex == 4)
                        {
                            AudioEffectEngine.RenderInto(audioCapture!.Latest, ledLayout, ledColors,
                                frameSettings.Brightness / 100d, frameSettings.EffectSpeed / 50d);
                        }
                        else if (modeIndex == 5)
                        {
                            var staticColor = previewColorPacked >= 0
                                ? new ColorRgb((byte)(previewColorPacked >> 16), (byte)(previewColorPacked >> 8), (byte)previewColorPacked)
                                : ParseStaticColor(frameSettings.StaticColorHex);
                            ledColors.AsSpan().Fill(staticColor.Scale(frameSettings.Brightness / 100d));
                        }
                    }
                    catch (DxgiFrameUnavailableException)
                    {
                        Interlocked.Increment(ref droppedCaptureFrames);
                        continue;
                    }
                    ApplyOutputColorGains(ledColors, frameSettings);
                    Interlocked.Exchange(ref lastProcessingTicks, Stopwatch.GetTimestamp() - processingStarted);
                    if (modeIndex != 0 || source.LastFrameWasUpdated) Interlocked.Increment(ref processedFrames);
                    var frameChanged = !ledColors.AsSpan().SequenceEqual(previousColors);
                    if (frameChanged) Interlocked.Increment(ref changedFrames);
                    if (frameChanged && now - lastPreviewPaint >= Stopwatch.Frequency / 30)
                    {
                        lastPreviewPaint = now;
                        InvalidateRect(hwnd, IntPtr.Zero, false);
                    }
                    var heartbeatDue = previousOutputTimestamp == 0 || now - previousOutputTimestamp >= Stopwatch.Frequency;
                    var forcedUpdate = Interlocked.Exchange(ref forceOutputRefresh, 0) != 0;
                    var outputRate = OutputPacing.EffectiveRate(frameSettings.CaptureRate, frameSettings.OutputRate);
                    var outputCheckTimestamp = Stopwatch.GetTimestamp();
                    var freshScreenFrame = modeIndex == 0 && source.LastFrameWasUpdated;
                    var outputCandidate = firstFrameForEnabledState || heartbeatDue || forcedUpdate || frameChanged || freshScreenFrame;
                    if (outputCandidate)
                    {
                        Interlocked.Increment(ref candidateFrames);
                        if (OutputPacing.IsDueForCapture(outputCheckTimestamp, previousOutputTimestamp, outputRate,
                                frameSettings.CaptureRate, Stopwatch.Frequency))
                        {
                            var isStaticOutput = modeIndex == 5;
                            var outputSequence = NextSequence();
                            if (!isStaticOutput)
                                ScreenSyncFrameBuilder.BuildInto(ledColors, outputSequence, screenSyncFrame);
                            QueueOutput(outputCheckTimestamp, processingStarted, isStaticOutput, ledColors[0], outputSequence);
                            ledColors.CopyTo(previousColors, 0);
                            firstFrameForEnabledState = false;
                        }
                        else Interlocked.Increment(ref pacingRejectedFrames);
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                status = "Screen capture stopped: " + ex.Message;
                Interlocked.Exchange(ref captureRetryAt, Stopwatch.GetTimestamp() + 3 * Stopwatch.Frequency);
                InvalidateRect(hwnd, IntPtr.Zero, false);
            }
            finally
            {
                audioCapture?.Dispose();
                outputPump?.Dispose();
                if (controller is not null)
                {
                    try
                    {
                        var shutdownFrame = ShutdownFrameSender.BuildBlackFrame(controller.Info.LedCount, NextSequence());
                        controller.WriteScreenSyncFrame(shutdownFrame);
                    }
                    catch (Exception) { }
                    controller.Dispose();
                }
                if (ReferenceEquals(capture, source)) capture = null;
            }
        }
    }

    private static void ScheduleControllerReconnect(ref WindowsHidController? controller, Exception error,
        IntPtr hwnd, ref int consecutiveFailures, ref long reconnectAt, ref long previousOutputTimestamp)
    {
        Interlocked.Exchange(ref controllerConnected, 0);
        controller?.Dispose();
        controller = null;
        consecutiveFailures++;
        var delay = ControllerReconnectPolicy.DelayForFailure(consecutiveFailures);
        reconnectAt = Stopwatch.GetTimestamp() + (long)(delay.TotalSeconds * Stopwatch.Frequency);
        previousOutputTimestamp = 0;
        status = $"Controller unavailable · retrying in {delay.TotalSeconds:0.##}s · {error.Message}";
        InvalidateRect(hwnd, IntPtr.Zero, false);
    }

    private static void RecordOutput(long writeStarted, long sampleStarted = 0)
    {
        var completed = Stopwatch.GetTimestamp();
        Interlocked.Increment(ref outputFrames);
        Interlocked.Exchange(ref lastUsbWriteTicks, completed - writeStarted);
        Interlocked.Exchange(ref lastEndToEndTicks, completed - (sampleStarted == 0 ? writeStarted : sampleStarted));
    }

    private static void ReleaseSingleInstanceMutex()
    {
        if (instanceMutex is null) return;
        try { instanceMutex.ReleaseMutex(); }
        catch (ApplicationException) { }
        instanceMutex.Dispose();
        instanceMutex = null;
    }

    private static string BuildDiagnosticsText()
    {
        var captureFps = Interlocked.Read(ref recentCaptureMilliFps) / 1000d;
        var changedFps = Interlocked.Read(ref recentChangedMilliFps) / 1000d;
        var queuedFps = Interlocked.Read(ref recentQueuedMilliFps) / 1000d;
        var outputFps = Interlocked.Read(ref recentOutputMilliFps) / 1000d;
        var candidateFps = Interlocked.Read(ref recentCandidateMilliFps) / 1000d;
        var pacingRejectedFps = Interlocked.Read(ref recentPacingRejectedMilliFps) / 1000d;
        var processingMs = Interlocked.Read(ref lastProcessingTicks) * 1000d / Stopwatch.Frequency;
        var usbMs = Interlocked.Read(ref lastUsbWriteTicks) * 1000d / Stopwatch.Frequency;
        var latencyMs = Interlocked.Read(ref lastEndToEndTicks) * 1000d / Stopwatch.Frequency;
        var device = Interlocked.CompareExchange(ref controllerConnected, 0, 0) == 0 ? "HID WAIT" : "HID OK";
        return $"CAP {captureFps:0.0} | CHG {changedFps:0.0} | C {candidateFps:0.0} | G {pacingRejectedFps:0.0} | Q {queuedFps:0.0} | OUT {outputFps:0.0} | P {processingMs:0.0} | USB {usbMs:0.0} | LAT {latencyMs:0.0} | D{Interlocked.Read(ref droppedCaptureFrames)} | {device}";
    }

    private static void UpdateDiagnosticRates()
    {
        var now = Stopwatch.GetTimestamp();
        var previousTimestamp = Interlocked.Exchange(ref diagnosticWindowTimestamp, now);
        var captureFrames = Interlocked.Read(ref processedFrames);
        var queued = Interlocked.Read(ref queuedFrames);
        var outputFramesCompleted = Interlocked.Read(ref outputFrames);
        var candidates = Interlocked.Read(ref candidateFrames);
        var pacingRejected = Interlocked.Read(ref pacingRejectedFrames);
        var previousCaptureFrames = Interlocked.Exchange(ref diagnosticPreviousCaptureFrames, captureFrames);
        var previousQueuedFrames = Interlocked.Exchange(ref diagnosticPreviousQueuedFrames, queued);
        var changed = Interlocked.Read(ref changedFrames);
        var previousChangedFrames = Interlocked.Exchange(ref diagnosticPreviousChangedFrames, changed);
        var previousOutputFrames = Interlocked.Exchange(ref diagnosticPreviousOutputFrames, outputFramesCompleted);
        var previousCandidates = Interlocked.Exchange(ref diagnosticPreviousCandidateFrames, candidates);
        var previousPacingRejected = Interlocked.Exchange(ref diagnosticPreviousPacingRejectedFrames, pacingRejected);
        var elapsed = now - previousTimestamp;
        if (previousTimestamp == 0 || elapsed <= 0) return;

        var captureRateMilliFps = Math.Max(0, (long)((captureFrames - previousCaptureFrames)
            * (double)Stopwatch.Frequency * 1000d / elapsed));
        var outputRateMilliFps = Math.Max(0, (long)((outputFramesCompleted - previousOutputFrames)
            * (double)Stopwatch.Frequency * 1000d / elapsed));
        var changedRateMilliFps = Math.Max(0, (long)((changed - previousChangedFrames)
            * (double)Stopwatch.Frequency * 1000d / elapsed));
        var queuedRateMilliFps = Math.Max(0, (long)((queued - previousQueuedFrames)
            * (double)Stopwatch.Frequency * 1000d / elapsed));
        var candidateRateMilliFps = Math.Max(0, (long)((candidates - previousCandidates)
            * (double)Stopwatch.Frequency * 1000d / elapsed));
        var pacingRejectedRateMilliFps = Math.Max(0, (long)((pacingRejected - previousPacingRejected)
            * (double)Stopwatch.Frequency * 1000d / elapsed));
        Interlocked.Exchange(ref recentCaptureMilliFps, captureRateMilliFps);
        Interlocked.Exchange(ref recentChangedMilliFps, changedRateMilliFps);
        Interlocked.Exchange(ref recentQueuedMilliFps, queuedRateMilliFps);
        Interlocked.Exchange(ref recentOutputMilliFps, outputRateMilliFps);
        Interlocked.Exchange(ref recentCandidateMilliFps, candidateRateMilliFps);
        Interlocked.Exchange(ref recentPacingRejectedMilliFps, pacingRejectedRateMilliFps);
    }

    private static byte NextSequence()
    {
        var current = sequence;
        sequence = sequence >= 254 ? (byte)1 : (byte)(sequence + 1);
        return current;
    }

    private static void Paint(IntPtr hwnd)
    {
        var paintHdc = BeginPaint(hwnd, out var paint);
        var drawHdc = paintHdc;
        var originalFont = IntPtr.Zero;
        try
        {
            if (!GetClientRect(hwnd, out var client) || client.Right <= 0 || client.Bottom <= 0) return;
            var useBackBuffer = EnsurePaintBuffer(paintHdc, client.Right, client.Bottom);
            drawHdc = useBackBuffer ? paintBufferDc : paintHdc;
            if (uiFont == IntPtr.Zero)
                uiFont = CreateFontW(-12, 0, 0, 0, 500, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe UI");
            if (uiFont != IntPtr.Zero) originalFont = SelectObject(drawHdc, uiFont);
            var hdc = drawHdc;
            _ = SetMapMode(hdc, 1); // Clear letterbox margins in physical client coordinates.
            _ = SetViewportOrgEx(hdc, 0, 0, IntPtr.Zero);
            var physicalBackground = new Rect { Right = client.Right, Bottom = client.Bottom };
            var physicalBrush = CreateSolidBrush(PaletteBackground);
            FillRect(hdc, ref physicalBackground, physicalBrush);
            DeleteObject(physicalBrush);

            _ = SetMapMode(hdc, 7); // MM_ISOTROPIC keeps text and controls proportional at every window size.
            _ = SetWindowExtEx(hdc, UiLogicalWidth, UiLogicalHeight, IntPtr.Zero);
            var scale = Math.Min(client.Right / (double)UiLogicalWidth, client.Bottom / (double)UiLogicalHeight);
            var viewportWidth = Math.Max(1, (int)Math.Round(UiLogicalWidth * scale));
            var viewportHeight = Math.Max(1, (int)Math.Round(UiLogicalHeight * scale));
            _ = SetViewportExtEx(hdc, viewportWidth, viewportHeight, IntPtr.Zero);
            _ = SetViewportOrgEx(hdc, (client.Right - viewportWidth) / 2, (client.Bottom - viewportHeight) / 2, IntPtr.Zero);
            var background = new Rect { Right = UiLogicalWidth, Bottom = UiLogicalHeight };
            var brush = CreateSolidBrush(PaletteBackground);
            FillRect(hdc, ref background, brush);
            DeleteObject(brush);
            SetBkMode(hdc, TextTransparent);
            SetTextColor(hdc, PaletteText);
            DrawBranding(hdc);
            DrawHeaderAction(hdc, new Rect { Left = 565, Top = 30, Right = 650, Bottom = 75 },
                showDiagnostics ? "HIDE FPS" : "SHOW FPS");
            DrawHeaderAction(hdc, new Rect { Left = 660, Top = 30, Right = 755, Bottom = 75 }, "IMPORT");
            DrawHeaderAction(hdc, new Rect { Left = 760, Top = 30, Right = 870, Bottom = 75 }, "EXPORT");
            var toggle = new Rect { Left = 32, Top = 95, Right = 250, Bottom = 145 };
            var toggleBrush = CreateSolidBrush(lightingEnabled ? PaletteOn : PaletteButton);
            FillRect(hdc, ref toggle, toggleBrush);
            DeleteObject(toggleBrush);
            SetTextColor(hdc, PaletteText);
            DrawText(hdc, lightingEnabled ? "LIGHTING  ON" : "LIGHTING  OFF", -1, ref toggle, TextButtonCentered);
            SetTextColor(hdc, PaletteSecondary);
            if (showDiagnostics)
            {
                var statusLine = new Rect { Left = 280, Top = 96, Right = 870, Bottom = 118 };
                DrawText(hdc, status, -1, ref statusLine, TextStatusSingleLine);
                SetTextColor(hdc, PaletteAccentTeal);
                var metricsLine = new Rect { Left = 280, Top = 120, Right = 870, Bottom = 145 };
                DrawText(hdc, BuildDiagnosticsText(), -1, ref metricsLine, TextStatusSingleLine);
                SetTextColor(hdc, PaletteSecondary);
            }
            else
            {
                var details = new Rect { Left = 280, Top = 95, Right = 870, Bottom = 145 };
                DrawText(hdc, status, -1, ref details, TextStatusSingleLine);
            }
            SetTextColor(hdc, PaletteAccentTeal);
            DrawDisplaySelector(hdc);
            DrawModeControls(hdc);
            DrawLedPreview(hdc);
            DrawTuningEditor(hdc);
            DrawEdgeEditor(hdc);
            DrawTrayPreferences(hdc);
            SetTextColor(hdc, PaletteMuted);
            var note = new Rect { Left = 32, Top = 710, Right = 585, Bottom = 744 };
            var closeNote = settings.MinimizeToTrayOnClose
                ? "Close hides the app; use tray Exit to turn the LEDs off."
                : "Lighting state is saved locally. Closing the app turns the LEDs off.";
            DrawText(hdc, closeNote, -1, ref note, TextStatusSingleLine);
            DrawHeaderAction(hdc, new Rect { Left = 600, Top = 710, Right = 870, Bottom = 744 }, "RESET TO DEFAULTS");
            if (useBackBuffer)
            {
                _ = SetMapMode(paintBufferDc, 1); // BitBlt dimensions are physical client pixels.
                _ = BitBlt(paintHdc, 0, 0, client.Right, client.Bottom, paintBufferDc, 0, 0, 0x00CC0020);
            }
        }
        finally
        {
            if (originalFont != IntPtr.Zero && originalFont != new IntPtr(-1)) _ = SelectObject(drawHdc, originalFont);
            EndPaint(hwnd, ref paint);
        }
    }

    private static bool EnsurePaintBuffer(IntPtr referenceDc, int width, int height)
    {
        if (paintBufferDc != IntPtr.Zero && paintBufferWidth == width && paintBufferHeight == height)
            return true;

        ReleasePaintBuffer();
        var dc = CreateCompatibleDC(referenceDc);
        if (dc == IntPtr.Zero) return false;
        var bitmap = CreateCompatibleBitmap(referenceDc, width, height);
        if (bitmap == IntPtr.Zero)
        {
            _ = DeleteDC(dc);
            return false;
        }
        var previous = SelectObject(dc, bitmap);
        if (previous == IntPtr.Zero || previous == new IntPtr(-1))
        {
            DeleteObject(bitmap);
            _ = DeleteDC(dc);
            return false;
        }
        paintBufferDc = dc;
        paintBufferBitmap = bitmap;
        paintBufferPreviousBitmap = previous;
        paintBufferWidth = width;
        paintBufferHeight = height;
        return true;
    }

    private static void ReleasePaintBuffer()
    {
        if (paintBufferDc != IntPtr.Zero && paintBufferPreviousBitmap != IntPtr.Zero)
            _ = SelectObject(paintBufferDc, paintBufferPreviousBitmap);
        if (paintBufferBitmap != IntPtr.Zero) _ = DeleteObject(paintBufferBitmap);
        if (paintBufferDc != IntPtr.Zero) _ = DeleteDC(paintBufferDc);
        paintBufferDc = IntPtr.Zero;
        paintBufferBitmap = IntPtr.Zero;
        paintBufferPreviousBitmap = IntPtr.Zero;
        paintBufferWidth = paintBufferHeight = 0;
    }

    private static (int Width, int Height) GetOuterWindowSize(int clientWidth, int clientHeight,
        uint style = NativeUiWindowPolicy.ResizableWindowStyle, uint exStyle = 0)
    {
        var bounds = new Rect { Right = clientWidth, Bottom = clientHeight };
        if (!AdjustWindowRectEx(ref bounds, style, false, exStyle))
            return (clientWidth + 16, clientHeight + 48);
        return (bounds.Right - bounds.Left, bounds.Bottom - bounds.Top);
    }

    private static void DrawBranding(IntPtr hdc)
    {
        if (trayIconHandle != IntPtr.Zero)
            _ = DrawIconEx(hdc, 28, 18, trayIconHandle, 52, 52, 0, IntPtr.Zero, 0x0003);

        if (brandTitleFont == IntPtr.Zero)
            brandTitleFont = CreateFontW(-26, 0, 0, 0, 700, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe UI Semibold");
        if (brandSubtitleFont == IntPtr.Zero)
            brandSubtitleFont = CreateFontW(-11, 0, 0, 0, 500, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe UI");

        var oldFont = SelectObject(hdc, brandTitleFont);
        SetTextColor(hdc, PaletteAccentTeal);
        var title = new Rect { Left = 92, Top = 16, Right = 510, Bottom = 50 };
        DrawText(hdc, "LUMASYNC", -1, ref title, 0x00000020 | 0x00000004);
        _ = SelectObject(hdc, brandSubtitleFont);
        SetTextColor(hdc, PaletteSecondary);
        var subtitle = new Rect { Left = 95, Top = 49, Right = 530, Bottom = 70 };
        DrawText(hdc, "MIKU AMBIENT  ·  SCREEN SYNC", -1, ref subtitle, 0x00000020 | 0x00000004);
        _ = SelectObject(hdc, oldFont);

        var accent = CreateSolidBrush(PaletteAccentPink);
        var accentMark = new Rect { Left = 94, Top = 75, Right = 132, Bottom = 78 };
        FillRect(hdc, ref accentMark, accent);
        DeleteObject(accent);
        var accentLineBrush = CreateSolidBrush(PaletteAccentTeal);
        var accentLine = new Rect { Left = 132, Top = 76, Right = 520, Bottom = 78 };
        FillRect(hdc, ref accentLine, accentLineBrush);
        DeleteObject(accentLineBrush);
    }

    private static void DisposeBrandFonts()
    {
        if (brandTitleFont != IntPtr.Zero) DeleteObject(brandTitleFont);
        if (brandSubtitleFont != IntPtr.Zero) DeleteObject(brandSubtitleFont);
        brandTitleFont = IntPtr.Zero;
        brandSubtitleFont = IntPtr.Zero;
    }

    private static void DisposeUiFont()
    {
        if (uiFont != IntPtr.Zero) DeleteObject(uiFont);
        uiFont = IntPtr.Zero;
    }

    private static void DrawLedPreview(IntPtr hdc)
    {
        // Keep the preview's top LEDs below the mode controls and its bottom LEDs
        // clear of the tuning panel. The former taller preview let both rows touch.
        var monitor = new Rect { Left = 190, Top = 270, Right = 730, Bottom = 390 };
        var screen = CreateSolidBrush(0x00180F0A);
        FillRect(hdc, ref monitor, screen);
        DeleteObject(screen);
        var ledBrush = GetStockObject(18); // DC_BRUSH avoids per-frame GDI handle churn.
        for (var i = 0; i < ledLayout.Length; i++)
        {
            var point = ledLayout[i];
            const int size = 13;
            int x, y;
            switch (point.Edge)
            {
                case LedEdge.Top: x = monitor.Left + (int)(point.U * (monitor.Right - monitor.Left)) - size / 2; y = monitor.Top - size - 8; break;
                case LedEdge.Right: x = monitor.Right + 8; y = monitor.Top + (int)(point.V * (monitor.Bottom - monitor.Top)) - size / 2; break;
                case LedEdge.Bottom: x = monitor.Left + (int)(point.U * (monitor.Right - monitor.Left)) - size / 2; y = monitor.Bottom + 8; break;
                default: x = monitor.Left - size - 8; y = monitor.Top + (int)(point.V * (monitor.Bottom - monitor.Top)) - size / 2; break;
            }
            var color = ledColors[i];
            var colorRef = (uint)(color.R | color.G << 8 | color.B << 16);
            _ = SetDCBrushColor(hdc, colorRef);
            var led = new Rect { Left = x, Top = y, Right = x + size, Bottom = y + size };
            FillRect(hdc, ref led, ledBrush);
        }
        SetTextColor(hdc, PaletteMuted);
        var displayLabel = new Rect { Left = monitor.Left, Top = monitor.Top + 49, Right = monitor.Right, Bottom = monitor.Top + 77 };
        var centerLabel = settings.ModeIndex == 0 ? "SCREEN COLOR PREVIEW" : NativeModeName(settings.ModeIndex).ToUpperInvariant() + " PREVIEW";
        DrawText(hdc, centerLabel, -1, ref displayLabel, TextButtonCentered);
    }

    private static void DrawModeControls(IntPtr hdc)
    {
        var mode = new Rect { Left = 32, Top = 202, Right = 250, Bottom = 242 };
        var modeBrush = CreateSolidBrush(settings.ModeIndex == 0 ? PaletteActive : PaletteButtonAlt);
        FillRect(hdc, ref mode, modeBrush);
        DeleteObject(modeBrush);
        SetTextColor(hdc, PaletteText);
        DrawText(hdc, $"MODE  ·  {NativeModeName(settings.ModeIndex).ToUpperInvariant()}", -1, ref mode, TextButtonCentered);

        var minus = new Rect { Left = 280, Top = 202, Right = 325, Bottom = 242 };
        var plus = new Rect { Left = 410, Top = 202, Right = 455, Bottom = 242 };
        var speedBrush = CreateSolidBrush(PaletteButton);
        FillRect(hdc, ref minus, speedBrush);
        FillRect(hdc, ref plus, speedBrush);
        DeleteObject(speedBrush);
        DrawText(hdc, "−", -1, ref minus, TextButtonCentered);
        DrawText(hdc, "+", -1, ref plus, TextButtonCentered);
        SetTextColor(hdc, PaletteSecondary);
        var speedLabel = new Rect { Left = 330, Top = 202, Right = 405, Bottom = 242 };
        var rateLabel = settings.ModeIndex == 4 ? $"GAIN {settings.EffectSpeed:0}" : $"SPEED {settings.EffectSpeed:0}";
        DrawText(hdc, rateLabel, -1, ref speedLabel, TextButtonCentered);

        var color = ParseStaticColor(settings.StaticColorHex);
        var colorSwatch = CreateSolidBrush((uint)(color.R | color.G << 8 | color.B << 16));
        var swatchRect = new Rect { Left = 485, Top = 211, Right = 510, Bottom = 233 };
        FillRect(hdc, ref swatchRect, colorSwatch);
        DeleteObject(colorSwatch);
        SetTextColor(hdc, PaletteSecondary);
        var colorLabel = new Rect { Left = 520, Top = 202, Right = 630, Bottom = 242 };
        DrawText(hdc, $"{BaseColorPresets.NameFor(settings.StaticColorHex)}  #{settings.StaticColorHex.ToUpperInvariant()}",
            -1, ref colorLabel, TextButtonCentered);
        DrawHeaderAction(hdc, new Rect { Left = 638, Top = 202, Right = 755, Bottom = 242 }, "PICK COLOR");
        DrawHeaderAction(hdc, new Rect { Left = 765, Top = 202, Right = 870, Bottom = 242 }, "PRESETS  ›");
    }

    private static void DrawDisplaySelector(IntPtr hdc)
    {
        var selector = new Rect { Left = 32, Top = 155, Right = 250, Bottom = 195 };
        var selectorBrush = CreateSolidBrush(PaletteButton);
        FillRect(hdc, ref selector, selectorBrush);
        DeleteObject(selectorBrush);
        SetTextColor(hdc, PaletteText);
        var savedDisplayIndex = Array.FindIndex(availableDisplays,
            d => string.Equals(d.DeviceName, displayName, StringComparison.OrdinalIgnoreCase));
        var selectorText = availableDisplays.Length > 1 ? "DISPLAY  ·  NEXT  ›"
            : availableDisplays.Length == 0 ? "DISPLAY  ·  NONE"
            : savedDisplayIndex < 0 ? "DISPLAY  ·  RESTORE" : "DISPLAY  ·  1 CONNECTED";
        DrawText(hdc, selectorText, -1, ref selector, TextButtonCentered);
        SetTextColor(hdc, PaletteSecondary);
        var label = new Rect { Left = 255, Top = 155, Right = 465, Bottom = 195 };
        DrawText(hdc, $"{selectedDisplay.DeviceName} · {selectedDisplay.Bounds.Width}×{selectedDisplay.Bounds.Height}", -1, ref label, TextButtonCentered);
        var direction = new Rect { Left = 470, Top = 155, Right = 650, Bottom = 195 };
        var mappingBrush = CreateSolidBrush(PaletteButton);
        FillRect(hdc, ref direction, mappingBrush);
        DeleteObject(mappingBrush);
        SetTextColor(hdc, PaletteText);
        DrawText(hdc, settings.Clockwise ? "DIRECTION  ·  CW" : "DIRECTION  ·  CCW", -1, ref direction, TextButtonCentered);
        var corner = new Rect { Left = 660, Top = 155, Right = 870, Bottom = 195 };
        var cornerBrush = CreateSolidBrush(PaletteButton);
        FillRect(hdc, ref corner, cornerBrush);
        DeleteObject(cornerBrush);
        var cornerName = settings.StartCorner.ToString().Replace("Top", "TOP-").Replace("Bottom", "BOTTOM-").Replace("Left", "LEFT").Replace("Right", "RIGHT");
        DrawText(hdc, $"START  ·  {cornerName}", -1, ref corner, TextButtonCentered);
    }

    private static void DrawEdgeEditor(IntPtr hdc)
    {
        SetTextColor(hdc, PaletteAccentTeal);
        var heading = new Rect { Left = 80, Top = 581, Right = 820, Bottom = 603 };
        DrawText(hdc, "LED LAYOUT  ·  USE − / + TO ADJUST EDGE COUNTS", -1, ref heading, 0x00000000);
        for (var i = 0; i < 4; i++)
        {
            var edge = (LedEdge)i;
            var left = 80 + i * 190;
            var minus = new Rect { Left = left, Top = 606, Right = left + 34, Bottom = 634 };
            var plus = new Rect { Left = left + 136, Top = 606, Right = left + 170, Bottom = 634 };
            var controlBrush = CreateSolidBrush(PaletteButton);
            FillRect(hdc, ref minus, controlBrush);
            FillRect(hdc, ref plus, controlBrush);
            DeleteObject(controlBrush);
            SetTextColor(hdc, PaletteText);
            DrawText(hdc, "−", -1, ref minus, TextButtonCentered);
            DrawText(hdc, "+", -1, ref plus, TextButtonCentered);
            var count = edge switch
            {
                LedEdge.Top => settings.Top,
                LedEdge.Right => settings.Right,
                LedEdge.Bottom => settings.Bottom,
                _ => settings.Left
            };
            SetTextColor(hdc, PaletteSecondary);
            var label = new Rect { Left = left + 38, Top = 606, Right = left + 132, Bottom = 634 };
            DrawText(hdc, $"{edge.ToString().ToUpperInvariant()}  {count}", -1, ref label, TextButtonCentered);
        }
        SetTextColor(hdc, PaletteMuted);
        var total = new Rect { Left = 80, Top = 638, Right = 820, Bottom = 658 };
        var controllerCount = reportedControllerLedCount > 0 ? reportedControllerLedCount.ToString() : "not detected";
        DrawText(hdc, $"LAYOUT TOTAL {ledLayout.Length}  /  CONTROLLER {controllerCount}  ·  {settings.CaptureRate} HZ", -1, ref total, TextButtonCentered);
    }

    private static void DrawTuningEditor(IntPtr hdc)
    {
        if (!ReferenceEquals(tuningLabelSettings, settings))
        {
            tuningLabelSettings = settings;
            tuningLabelCache =
            [
                $"BRIGHT  {settings.Brightness:0}%",
                $"SMOOTH  {settings.Smoothing:0}%",
                $"DEPTH  {settings.SamplingDepth:0}%",
                $"GAMMA  {settings.Gamma:0.0}",
                $"SAT  {settings.Saturation:0}%",
                $"TEMP  {settings.ColorTemperature:0}K",
                $"CAP  {settings.CaptureRate}HZ",
                $"OUT  {OutputPacing.EffectiveRate(settings.CaptureRate, settings.OutputRate)}HZ",
                $"LETTERBOX {(settings.DetectLetterbox ? "ON" : "OFF")}",
                $"CROP L {settings.CropLeftPercent:0}%",
                $"CROP R {settings.CropRightPercent:0}%",
                $"CROP T {settings.CropTopPercent:0}%",
                $"CROP B {settings.CropBottomPercent:0}%"
            ];
        }
        var labels = tuningLabelCache;
        SetTextColor(hdc, PaletteAccentTeal);
        var heading = new Rect { Left = 32, Top = 444, Right = 870, Bottom = 462 };
        DrawText(hdc, "SCREEN SYNC TUNING  ·  CLICK − / + TO ADJUST", -1, ref heading, 0x00000000);
        for (var i = 0; i < labels.Length; i++)
        {
            var row = i < 5 ? 0 : i < 9 ? 1 : 2;
            var column = row == 0 ? i : row == 1 ? i - 5 : i - 9;
            var x = 32 + column * 168;
            var y = row switch { 0 => 466, 1 => 504, _ => 542 };
            var control = new Rect { Left = x, Top = y, Right = x + 162, Bottom = y + 34 };
            var background = CreateSolidBrush(PalettePanel);
            FillRect(hdc, ref control, background);
            DeleteObject(background);
            var minus = new Rect { Left = x + 2, Top = y + 3, Right = x + 28, Bottom = y + 31 };
            var plus = new Rect { Left = x + 134, Top = y + 3, Right = x + 160, Bottom = y + 31 };
            var button = CreateSolidBrush(PaletteButton);
            FillRect(hdc, ref minus, button);
            FillRect(hdc, ref plus, button);
            DeleteObject(button);
            SetTextColor(hdc, PaletteText);
            DrawText(hdc, "−", -1, ref minus, TextButtonCentered);
            DrawText(hdc, "+", -1, ref plus, TextButtonCentered);
            SetTextColor(hdc, PaletteSecondary);
            var value = new Rect { Left = x + 29, Top = y + 3, Right = x + 133, Bottom = y + 31 };
            DrawText(hdc, labels[i], -1, ref value, TextButtonCentered);
        }
    }

    private static void DrawTrayPreferences(IntPtr hdc)
    {
        DrawPreference(hdc, new Rect { Left = 32, Top = 666, Right = 300, Bottom = 704 },
            settings.StartMinimizedToTray ? "START HIDDEN  ·  ON" : "START HIDDEN  ·  OFF",
            settings.StartMinimizedToTray);
        DrawPreference(hdc, new Rect { Left = 315, Top = 666, Right = 585, Bottom = 704 },
            settings.MinimizeToTrayOnClose ? "CLOSE TO TRAY  ·  ON" : "CLOSE TO TRAY  ·  OFF",
            settings.MinimizeToTrayOnClose);
        DrawPreference(hdc, new Rect { Left = 600, Top = 666, Right = 870, Bottom = 704 },
            startWithWindows ? "START WITH WINDOWS  ·  ON" : "START WITH WINDOWS  ·  OFF",
            startWithWindows);
    }

    private static void DrawPreference(IntPtr hdc, Rect rect, string label, bool enabled)
    {
        var brush = CreateSolidBrush(enabled ? PaletteActive : PaletteButton);
        FillRect(hdc, ref rect, brush);
        DeleteObject(brush);
        SetTextColor(hdc, enabled ? PaletteText : PaletteSecondary);
        DrawText(hdc, label, -1, ref rect, TextButtonCentered);
    }

    private static void DrawHeaderAction(IntPtr hdc, Rect rect, string label)
    {
        var brush = CreateSolidBrush(PaletteButton);
        FillRect(hdc, ref rect, brush);
        DeleteObject(brush);
        SetTextColor(hdc, PaletteText);
        DrawText(hdc, label, -1, ref rect, TextButtonCentered);
    }

    private static void SetCaption(IntPtr hwnd, string text)
    {
        _ = SetWindowText(hwnd, text);
    }

    private static async Task RepairCaptionAsync(IntPtr hwnd)
    {
        for (var attempt = 0; attempt < 8; attempt++)
        {
            await Task.Delay(250).ConfigureAwait(false);
            if (GetWindowTextLength(hwnd) > 1) return;
            _ = SetWindowText(hwnd, "LumaSync · Screen Sync");
        }
    }

    private delegate IntPtr WindowProcedure(IntPtr hwnd, uint message, UIntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClass
    {
        public uint Size, Style;
        public WindowProcedure Procedure;
        public int ClassExtra, WindowExtra;
        public IntPtr Instance, Icon, Cursor, Background;
        [MarshalAs(UnmanagedType.LPWStr)] public string? MenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string ClassName;
        public IntPtr SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OpenFileName
    {
        public int Size;
        public IntPtr Owner, Instance, Filter, CustomFilter;
        public int MaxCustomFilter, FilterIndex;
        public IntPtr File;
        public int MaxFile;
        public IntPtr FileTitle;
        public int MaxFileTitle;
        public IntPtr InitialDirectory, Title;
        public int Flags;
        public short FileOffset, FileExtension;
        public IntPtr DefaultExtension, CustomData, Hook, TemplateName, Reserved;
        public int ReservedValue, FlagsEx;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint Size;
        public IntPtr Window;
        public uint Id, Flags, CallbackMessage;
        public IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint BalloonTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public uint InfoFlags;
        public Guid ItemGuid;
        public IntPtr BalloonIcon;
    }
    [StructLayout(LayoutKind.Sequential)] private struct MinMaxInfo
    {
        public Point Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct PaintStruct { public IntPtr Dc; public int Erase; public Rect Paint; public int Restore, IncUpdate; [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] Reserved; }
    [StructLayout(LayoutKind.Sequential)] private struct Message { public IntPtr Window; public uint Id; public UIntPtr WParam; public IntPtr LParam; public uint Time; public Point Point; }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? moduleName);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern ushort RegisterClassEx(ref WindowClass windowClass);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CreateWindowEx(uint exStyle, [MarshalAs(UnmanagedType.LPWStr)] string className, [MarshalAs(UnmanagedType.LPWStr)] string windowName, uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool ShowWindow(IntPtr hwnd, int command);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UpdateWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int GetMessage(out Message message, IntPtr hwnd, uint minimum, uint maximum);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref Message message);
    [DllImport("user32.dll")] private static extern IntPtr DispatchMessage(ref Message message);
    [DllImport("user32.dll", EntryPoint = "DefWindowProcW", CharSet = CharSet.Unicode, ExactSpelling = true)] private static extern IntPtr DefWindowProc(IntPtr hwnd, uint message, UIntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern void PostQuitMessage(int exitCode);
    [DllImport("user32.dll", EntryPoint = "SendMessageW", CharSet = CharSet.Unicode, ExactSpelling = true)] private static extern IntPtr SendMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", EntryPoint = "SetWindowTextW", CharSet = CharSet.Unicode, ExactSpelling = true)] private static extern bool SetWindowText(IntPtr hwnd, [MarshalAs(UnmanagedType.LPWStr)] string text);
    [DllImport("user32.dll", EntryPoint = "GetWindowTextLengthW", ExactSpelling = true)] private static extern int GetWindowTextLength(IntPtr hwnd);
    [DllImport("user32.dll", SetLastError = true)] private static extern UIntPtr SetTimer(IntPtr hwnd, UIntPtr timerId, uint milliseconds, IntPtr callback);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool KillTimer(IntPtr hwnd, UIntPtr timerId);
    [DllImport("user32.dll", EntryPoint = "LoadImageW", ExactSpelling = true)] private static extern IntPtr LoadImage(IntPtr instance, IntPtr name, uint type, int width, int height, uint flags);
    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode, ExactSpelling = true)] private static extern bool Shell_NotifyIcon(uint message, ref NotifyIconData data);
    [DllImport("user32.dll", EntryPoint = "RegisterWindowMessageW", CharSet = CharSet.Unicode, ExactSpelling = true)] private static extern uint RegisterWindowMessage(string message);
    [DllImport("user32.dll")] private static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll", EntryPoint = "AppendMenuW", CharSet = CharSet.Unicode, ExactSpelling = true)] private static extern bool AppendMenu(IntPtr menu, uint flags, int item, string? text);
    [DllImport("user32.dll", EntryPoint = "TrackPopupMenu", ExactSpelling = true)] private static extern uint TrackPopupMenu(IntPtr menu, uint flags, int x, int y, int reserved, IntPtr hwnd, IntPtr rect);
    [DllImport("user32.dll")] private static extern bool DestroyMenu(IntPtr menu);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool AdjustWindowRectEx(ref Rect rect, uint style, bool hasMenu, uint exStyle);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool EnableWindow(IntPtr hwnd, bool enable);
    [DllImport("user32.dll")] private static extern IntPtr SetCapture(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll", EntryPoint = "PostMessageW", ExactSpelling = true)] private static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", EntryPoint = "MessageBoxW", CharSet = CharSet.Unicode, ExactSpelling = true)] private static extern int MessageBox(IntPtr hwnd, string text, string caption, uint type);
    [DllImport("comdlg32.dll", EntryPoint = "GetOpenFileNameW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool GetOpenFileName(ref OpenFileName dialog);
    [DllImport("comdlg32.dll", EntryPoint = "GetSaveFileNameW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool GetSaveFileName(ref OpenFileName dialog);
    [DllImport("comdlg32.dll", EntryPoint = "CommDlgExtendedError", ExactSpelling = true)] private static extern uint CommDlgExtendedError();
    [DllImport("user32.dll")] private static extern bool InvalidateRect(IntPtr hwnd, IntPtr rect, bool erase);
    [DllImport("user32.dll")] private static extern IntPtr SetFocus(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool InvalidateRect(IntPtr hwnd, ref Rect rect, bool erase);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] private static extern IntPtr BeginPaint(IntPtr hwnd, out PaintStruct paint);
    [DllImport("user32.dll")] private static extern bool EndPaint(IntPtr hwnd, ref PaintStruct paint);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int width, int height);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr destination, int x, int y, int width, int height,
        IntPtr source, int sourceX, int sourceY, uint rasterOperation);
    [DllImport("user32.dll")] private static extern int FillRect(IntPtr hdc, ref Rect rect, IntPtr brush);
    [DllImport("gdi32.dll")] private static extern int SetBkMode(IntPtr hdc, int mode);
    [DllImport("gdi32.dll")] private static extern uint SetTextColor(IntPtr hdc, uint color);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int DrawText(IntPtr hdc, string text, int count, ref Rect rect, uint format);
    [DllImport("user32.dll")] private static extern bool DrawIconEx(IntPtr hdc, int x, int y, IntPtr icon, int width, int height, uint step, IntPtr brush, uint flags);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateSolidBrush(uint color);
    [DllImport("gdi32.dll", EntryPoint = "CreateFontW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern IntPtr CreateFontW(int height, int width, int escapement, int orientation, int weight,
        uint italic, uint underline, uint strikeOut, uint charSet, uint outputPrecision, uint clipPrecision,
        uint quality, uint pitchAndFamily, string faceName);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern int SetMapMode(IntPtr hdc, int mode);
    [DllImport("gdi32.dll")] private static extern bool SetWindowExtEx(IntPtr hdc, int x, int y, IntPtr size);
    [DllImport("gdi32.dll")] private static extern bool SetViewportExtEx(IntPtr hdc, int x, int y, IntPtr size);
    [DllImport("gdi32.dll")] private static extern bool SetViewportOrgEx(IntPtr hdc, int x, int y, IntPtr point);
    [DllImport("gdi32.dll")] private static extern IntPtr GetStockObject(int objectType);
    [DllImport("gdi32.dll")] private static extern uint SetDCBrushColor(IntPtr hdc, uint color);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("user32.dll")] private static extern IntPtr LoadCursor(IntPtr instance, IntPtr cursorName);
}
