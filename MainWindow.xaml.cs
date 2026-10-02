using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Globalization;
using System.ComponentModel;
using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinForms = System.Windows.Forms;

namespace CursorLine;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private const int GwlExStyle = -20;
    private const long WsExTransparent = 0x00000020L;
    private const long WsExToolWindow = 0x00000080L;
    private const long WsExNoActivate = 0x08000000L;
    private const int WmNcHitTest = 0x0084;
    private const int HtTransparent = -1;
    private const uint EventSystemForeground = 0x0003;
    private const uint WineventSkipOwnProcess = 0x0002;
    private const string PenPositionMapName = "Local\\CursorLinePenPosition";
    private const long PenPositionMapSize = 32;

    private readonly WinForms.ContextMenuStrip _trayMenu = new();
    private readonly WinForms.ToolStripMenuItem _toggleOverlayItem = new("Hide overlay");
    private readonly WinForms.ToolStripMenuItem _customizeLineItem = new("Customize");
    private readonly System.Drawing.Icon _trayIconImage = LoadTrayIcon();
    private readonly WinForms.NotifyIcon _trayIcon;
    private readonly WinEventProc _foregroundEventCallback;
    private readonly SolidColorBrush _lineBrush = new(Colors.White);
    private OverlaySettings _settings = OverlaySettings.Load();
    private MonitorDisplay[] _displays = [];
    private double _dpiScaleX = 1;
    private double _dpiScaleY = 1;
    private string? _loadedBigCursorImagePath;
    private int _bigCursorImagePixelWidth;
    private int _bigCursorImagePixelHeight;
    private bool _isExiting;
    private bool _isLoaded;
    private bool _renderingSubscribed;
    private bool _manuallyHidden;
    private IntPtr _foregroundEventHook;
    private string _foregroundProcessName = string.Empty;
    private MemoryMappedFile? _penPositionMap;
    private MemoryMappedViewAccessor? _penPositionView;
    private DateTime _nextPenMapLookupUtc;
    private CustomizationWindow? _customizationWindow;

    public MainWindow()
    {
        InitializeComponent();

        OverlayLine.Stroke = _lineBrush;
        CursorCircle.Stroke = _lineBrush;
        CursorCircle.Fill = System.Windows.Media.Brushes.Transparent;
        _foregroundEventCallback = OnForegroundEvent;
        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;

        var exitItem = new WinForms.ToolStripMenuItem("Exit");
        _customizeLineItem.Click += (_, _) => Dispatcher.Invoke(OpenCustomization);
        _toggleOverlayItem.Click += (_, _) => Dispatcher.Invoke(ToggleOverlay);
        exitItem.Click += (_, _) => Dispatcher.Invoke(ExitApplication);
        _trayMenu.Items.Add(_customizeLineItem);
        _trayMenu.Items.Add(_toggleOverlayItem);
        _trayMenu.Items.Add(new WinForms.ToolStripSeparator());
        _trayMenu.Items.Add(exitItem);
        _trayMenu.Opening += (_, _) =>
            _toggleOverlayItem.Text = _manuallyHidden || !IsVisible ? "Show overlay" : "Hide overlay";

        _trayIcon = new WinForms.NotifyIcon
        {
            Icon = _trayIconImage,
            Text = "CursorLine",
            ContextMenuStrip = _trayMenu,
            Visible = true
        };
        _trayIcon.DoubleClick += (_, _) => Dispatcher.Invoke(ToggleOverlay);

        IsVisibleChanged += OnOverlayVisibilityChanged;
        Closing += OnClosing;
        Closed += (_, _) =>
        {
            StopFrameUpdates();
            if (_foregroundEventHook != IntPtr.Zero)
            {
                UnhookWinEvent(_foregroundEventHook);
            }
            _customizationWindow?.Close();
            _penPositionView?.Dispose();
            _penPositionMap?.Dispose();
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _trayIconImage.Dispose();
            _trayMenu.Dispose();
        };
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_isExiting)
        {
            return;
        }

        e.Cancel = true;
        _manuallyHidden = true;
        UpdateOverlayVisibility();
    }

    private void ToggleOverlay()
    {
        _manuallyHidden = IsVisible;
        UpdateOverlayVisibility();
    }

    private void ExitApplication()
    {
        _isExiting = true;
        Close();
        System.Windows.Application.Current.Shutdown();
    }

    private void OpenCustomization()
    {
        if (_customizationWindow is null)
        {
            _customizationWindow = new CustomizationWindow(_settings, _displays);
            _customizationWindow.SettingsChanged += settings =>
            {
                _settings = settings;
                ApplyAppearance();
                UpdateOverlayVisibility();
            };
            _customizationWindow.Closed += (_, _) => _customizationWindow = null;
        }

        if (!_customizationWindow.IsVisible)
        {
            _customizationWindow.Show();
        }

        _customizationWindow.Activate();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        var source = HwndSource.FromHwnd(handle);
        source?.AddHook(WindowMessageHook);

        var extendedStyle = GetWindowLongPtr(handle, GwlExStyle).ToInt64();
        SetWindowLongPtr(handle, GwlExStyle, new IntPtr(extendedStyle | WsExTransparent | WsExToolWindow | WsExNoActivate));

        _foregroundEventHook = SetWinEventHook(
            EventSystemForeground,
            EventSystemForeground,
            IntPtr.Zero,
            _foregroundEventCallback,
            0,
            0,
            WineventSkipOwnProcess);
        _foregroundProcessName = GetForegroundProcessName(GetForegroundWindow());
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        _dpiScaleX = dpi.DpiScaleX;
        _dpiScaleY = dpi.DpiScaleY;

        _displays = EnumerateDisplays();
        if (_displays.Length == 0)
        {
            _displays =
            [
                new MonitorDisplay(
                    "PRIMARY", "Primary display", 0, 0,
                    GetSystemMetrics(0), GetSystemMetrics(1), true,
                    _dpiScaleX, _dpiScaleY, IntPtr.Zero)
            ];
        }

        _settings.DisplayDeviceName = ResolveSelectedDisplay().DeviceName;
        ApplyAppearance();
        _isLoaded = true;
        _foregroundProcessName = GetForegroundProcessName(GetForegroundWindow());
        UpdateOverlayVisibility();
        if (IsVisible)
        {
            StartFrameUpdates();
        }
    }

    private void OnOverlayVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!_isLoaded)
        {
            return;
        }

        if (IsVisible)
        {
            StartFrameUpdates();
        }
        else
        {
            StopFrameUpdates();
        }
    }

    private void StartFrameUpdates()
    {
        if (_renderingSubscribed)
        {
            return;
        }

        CompositionTarget.Rendering += UpdateLine;
        _renderingSubscribed = true;
        UpdateLine(this, EventArgs.Empty);
    }

    private void StopFrameUpdates()
    {
        if (!_renderingSubscribed)
        {
            return;
        }

        CompositionTarget.Rendering -= UpdateLine;
        _renderingSubscribed = false;
    }

    private void OnForegroundEvent(
        IntPtr hook,
        uint eventType,
        IntPtr hwnd,
        int objectId,
        int childId,
        uint eventThread,
        uint eventTime)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        GetWindowThreadProcessId(hwnd, out var processId);
        if (processId == Environment.ProcessId)
        {
            return;
        }

        _foregroundProcessName = GetForegroundProcessName(hwnd);
        Dispatcher.BeginInvoke(UpdateOverlayVisibility);
    }

    private static string GetForegroundProcessName(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return string.Empty;
        }

        GetWindowThreadProcessId(hwnd, out var processId);
        if (processId == 0 || processId == Environment.ProcessId)
        {
            return string.Empty;
        }

        try
        {
            using var process = Process.GetProcessById((int)processId);
            return process.ProcessName;
        }
        catch (ArgumentException)
        {
            return string.Empty;
        }
        catch (InvalidOperationException)
        {
            return string.Empty;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return string.Empty;
        }
    }

    private void UpdateOverlayVisibility()
    {
        if (!_isLoaded || _isExiting)
        {
            return;
        }

        var isWhitelisted = !_settings.WhitelistEnabled ||
                            _settings.IsApplicationWhitelisted(_foregroundProcessName);
        var shouldShow = !_manuallyHidden && isWhitelisted;
        if (shouldShow && !IsVisible)
        {
            Show();
        }
        else if (!shouldShow && IsVisible)
        {
            Hide();
        }
    }

    private void ApplyAppearance()
    {
        var display = ResolveSelectedDisplay();
        _dpiScaleX = display.DpiScaleX;
        _dpiScaleY = display.DpiScaleY;
        Left = display.Left / _dpiScaleX;
        Top = display.Top / _dpiScaleY;
        Width = display.Width / _dpiScaleX;
        Height = display.Height / _dpiScaleY;

        _lineBrush.Color = _settings.GetLineColor();
        var strokeWidth = _settings.LineWidthPixels / _dpiScaleX;
        OverlayLine.StrokeThickness = strokeWidth;
        CursorCircle.StrokeThickness = strokeWidth;

        var radiusX = _settings.CursorCircleRadiusPixels / _dpiScaleX;
        var radiusY = _settings.CursorCircleRadiusPixels / _dpiScaleY;
        CursorCircle.Width = radiusX * 2;
        CursorCircle.Height = radiusY * 2;
        CursorCircle.Visibility = _settings.DrawCursorCircle ? Visibility.Visible : Visibility.Collapsed;
        UpdateBigCursorImage();
        UpdateLine(null, EventArgs.Empty);
    }

    private void UpdateBigCursorImage()
    {
        if (!string.Equals(_loadedBigCursorImagePath, _settings.BigCursorImagePath, StringComparison.OrdinalIgnoreCase))
        {
            _loadedBigCursorImagePath = _settings.BigCursorImagePath;
            _bigCursorImagePixelWidth = 0;
            _bigCursorImagePixelHeight = 0;
            BigCursorImage.Source = null;

            if (_settings.BigCursorImagePath is { Length: > 0 } imagePath)
            {
                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.UriSource = new Uri(imagePath, UriKind.Absolute);
                    bitmap.EndInit();
                    bitmap.Freeze();
                    BigCursorImage.Source = bitmap;
                    _bigCursorImagePixelWidth = bitmap.PixelWidth;
                    _bigCursorImagePixelHeight = bitmap.PixelHeight;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                                  NotSupportedException or UriFormatException or FormatException)
                {
                    System.Windows.MessageBox.Show(
                        $"Could not load the configured big cursor image:\n{exception.Message}",
                        "CursorLine",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }
        }

        if (_bigCursorImagePixelWidth > 0 && _bigCursorImagePixelHeight > 0)
        {
            var maxDimension = Math.Max(_bigCursorImagePixelWidth, _bigCursorImagePixelHeight);
            var scale = _settings.BigCursorScalePixels / maxDimension;
            BigCursorImage.Width = _bigCursorImagePixelWidth * scale / _dpiScaleX;
            BigCursorImage.Height = _bigCursorImagePixelHeight * scale / _dpiScaleY;
            BigCursorRotationTransform.Angle = _settings.BigCursorRotationDegrees;
            BigCursorImage.Visibility = _settings.EnableBigCursor ? Visibility.Visible : Visibility.Collapsed;
        }
        else
        {
            BigCursorImage.Visibility = Visibility.Collapsed;
        }
    }

    private void UpdateLine(object? sender, EventArgs e)
    {
        if (!TryGetPenPosition(out var cursor) && !GetCursorPos(out cursor))
        {
            return;
        }

        var display = ResolveSelectedDisplay();
        var isOnSelectedDisplay = cursor.X >= display.Left &&
                                  cursor.X < display.Left + display.Width &&
                                  cursor.Y >= display.Top &&
                                  cursor.Y < display.Top + display.Height;
        if (!isOnSelectedDisplay)
        {
            OverlayLine.Visibility = Visibility.Collapsed;
            CursorCircle.Visibility = Visibility.Collapsed;
            BigCursorImage.Visibility = Visibility.Collapsed;
            return;
        }

        OverlayLine.Visibility = _settings.DrawLine ? Visibility.Visible : Visibility.Collapsed;
        CursorCircle.Visibility = _settings.DrawCursorCircle ? Visibility.Visible : Visibility.Collapsed;
        BigCursorImage.Visibility = _settings.EnableBigCursor && BigCursorImage.Source is not null
            ? Visibility.Visible
            : Visibility.Collapsed;
        var endX = (cursor.X - display.Left) / _dpiScaleX;
        var endY = (cursor.Y - display.Top) / _dpiScaleY;
        var (startX, startY) = _settings.StartCorner switch
        {
            "TopLeft" => (0d, 0d),
            "TopRight" => (Width, 0d),
            "BottomLeft" => (0d, Height),
            _ => (Width, Height)
        };

        OverlayLine.X1 = startX;
        OverlayLine.Y1 = startY;
        OverlayLine.X2 = endX;
        OverlayLine.Y2 = endY;

        Canvas.SetLeft(CursorCircle, endX - CursorCircle.Width / 2);
        Canvas.SetTop(CursorCircle, endY - CursorCircle.Height / 2);
        Canvas.SetLeft(BigCursorImage, endX);
        Canvas.SetTop(BigCursorImage, endY);
    }

    private bool TryGetPenPosition(out NativePoint point)
    {
        point = default;
        if (_penPositionView is null)
        {
            var now = DateTime.UtcNow;
            if (now < _nextPenMapLookupUtc)
            {
                return false;
            }

            _nextPenMapLookupUtc = now.AddSeconds(1);
            try
            {
                _penPositionMap = MemoryMappedFile.OpenExisting(
                    PenPositionMapName,
                    MemoryMappedFileRights.Read);
                _penPositionView = _penPositionMap.CreateViewAccessor(
                    0,
                    PenPositionMapSize,
                    MemoryMappedFileAccess.Read);
            }
            catch (FileNotFoundException)
            {
                return false;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var firstSequence = _penPositionView.ReadInt64(0);
            if ((firstSequence & 1) != 0)
            {
                continue;
            }

            Thread.MemoryBarrier();
            var inRange = _penPositionView.ReadInt32(8);
            var x = _penPositionView.ReadInt32(12);
            var y = _penPositionView.ReadInt32(16);
            var updatedAt = _penPositionView.ReadInt64(24);
            Thread.MemoryBarrier();
            var secondSequence = _penPositionView.ReadInt64(0);

            if (firstSequence != secondSequence || (secondSequence & 1) != 0)
            {
                continue;
            }

            if (inRange == 0 || Environment.TickCount64 - updatedAt > 500)
            {
                return false;
            }

            point = new NativePoint { X = x, Y = y };
            return true;
        }

        return false;
    }

    private MonitorDisplay ResolveSelectedDisplay()
    {
        var selected = _displays.FirstOrDefault(display =>
            string.Equals(display.DeviceName, _settings.DisplayDeviceName, StringComparison.OrdinalIgnoreCase));
        if (selected is not null)
        {
            return selected;
        }

        var primary = _displays.FirstOrDefault(display => display.IsPrimary) ?? _displays.First();
        _settings.DisplayDeviceName = primary.DeviceName;
        return primary;
    }

    private static MonitorDisplay[] EnumerateDisplays()
    {
        var displays = new List<MonitorDisplay>();
        MonitorEnumProc callback = (IntPtr monitor, IntPtr hdc, ref NativeRect monitorRect, IntPtr data) =>
        {
            var info = new NativeMonitorInfo
            {
                Size = (uint)Marshal.SizeOf<NativeMonitorInfo>(),
                DeviceName = string.Empty
            };
            if (!GetMonitorInfo(monitor, ref info))
            {
                return true;
            }

            uint dpiX = 96;
            uint dpiY = 96;
            if (GetDpiForMonitor(monitor, 0, out var reportedDpiX, out var reportedDpiY) == 0)
            {
                dpiX = reportedDpiX;
                dpiY = reportedDpiY;
            }

            var bounds = info.MonitorBounds;
            displays.Add(new MonitorDisplay(
                info.DeviceName,
                $"{info.DeviceName} ({bounds.Right - bounds.Left}x{bounds.Bottom - bounds.Top})" +
                    ((info.Flags & MonitorInfoPrimary) != 0 ? " - Primary" : string.Empty),
                bounds.Left,
                bounds.Top,
                bounds.Right - bounds.Left,
                bounds.Bottom - bounds.Top,
                (info.Flags & MonitorInfoPrimary) != 0,
                dpiX / 96d,
                dpiY / 96d,
                monitor));
            return true;
        };

        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);
        return displays
            .OrderByDescending(display => display.IsPrimary)
            .ThenBy(display => display.DeviceName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IntPtr WindowMessageHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WmNcHitTest)
        {
            handled = true;
            return new IntPtr(HtTransparent);
        }

        return IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    private delegate void WinEventProc(
        IntPtr hook,
        uint eventType,
        IntPtr hwnd,
        int objectId,
        int childId,
        uint eventThread,
        uint eventTime);

    private const uint MonitorInfoPrimary = 1;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeMonitorInfo
    {
        public uint Size;
        public NativeRect MonitorBounds;
        public NativeRect WorkBounds;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
    }

    private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdc, ref NativeRect monitorRect, IntPtr data);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWinEventHook(
        uint eventMin,
        uint eventMax,
        IntPtr eventHookModule,
        WinEventProc callback,
        uint processId,
        uint threadId,
        uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWinEvent(IntPtr hook);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clipRect, MonitorEnumProc callback, IntPtr data);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref NativeMonitorInfo info);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);

    private static System.Drawing.Icon LoadTrayIcon()
    {
        using var iconStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("CursorLine.cl.png")
            ?? throw new FileNotFoundException("Embedded tray icon is missing.");
        using var bitmap = new System.Drawing.Bitmap(iconStream);
        var iconHandle = bitmap.GetHicon();
        try
        {
            using var icon = System.Drawing.Icon.FromHandle(iconHandle);
            return (System.Drawing.Icon)icon.Clone();
        }
        finally
        {
            DestroyIcon(iconHandle);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr iconHandle);

}

internal sealed record MonitorDisplay(
    string DeviceName,
    string DisplayName,
    int Left,
    int Top,
    int Width,
    int Height,
    bool IsPrimary,
    double DpiScaleX,
    double DpiScaleY,
    IntPtr Handle);

internal sealed class OverlaySettings
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public double LineWidthPixels { get; set; } = 4;
    public bool DrawLine { get; set; } = true;
    public string LineColorHex { get; set; } = "#FFFFFF";
    public string StartCorner { get; set; } = "BottomRight";
    public bool DrawCursorCircle { get; set; }
    public double CursorCircleRadiusPixels { get; set; } = 24;
    public bool EnableBigCursor { get; set; }
    public string? BigCursorImagePath { get; set; }
    public double BigCursorScalePixels { get; set; } = 128;
    public double BigCursorRotationDegrees { get; set; }
    public string? DisplayDeviceName { get; set; }
    public bool WhitelistEnabled { get; set; }
    public List<string> WhitelistedApplications { get; set; } = [];

    public OverlaySettings Clone() => new()
    {
        LineWidthPixels = LineWidthPixels,
        DrawLine = DrawLine,
        LineColorHex = LineColorHex,
        StartCorner = StartCorner,
        DrawCursorCircle = DrawCursorCircle,
        CursorCircleRadiusPixels = CursorCircleRadiusPixels,
        EnableBigCursor = EnableBigCursor,
        BigCursorImagePath = BigCursorImagePath,
        BigCursorScalePixels = BigCursorScalePixels,
        BigCursorRotationDegrees = BigCursorRotationDegrees,
        DisplayDeviceName = DisplayDeviceName,
        WhitelistEnabled = WhitelistEnabled,
        WhitelistedApplications = [.. WhitelistedApplications]
    };

    public bool IsApplicationWhitelisted(string processName) =>
        WhitelistedApplications.Contains(processName, StringComparer.OrdinalIgnoreCase);

    private static string UserDataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CursorLine");

    private static string UserSettingsPath => Path.Combine(UserDataDirectory, "settings.json");

    public static string CopyBigCursorImage(string sourcePath)
    {
        Directory.CreateDirectory(UserDataDirectory);
        var destinationPath = Path.Combine(UserDataDirectory, $"big-cursor-{Guid.NewGuid():N}.png");
        File.Copy(sourcePath, destinationPath);
        return destinationPath;
    }

    public System.Windows.Media.Color GetLineColor()
    {
        if (LineColorHex.Length == 7 && LineColorHex[0] == '#' &&
            uint.TryParse(LineColorHex.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
        {
            return System.Windows.Media.Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        }

        return Colors.White;
    }

    public void SetLineColor(System.Windows.Media.Color color) =>
        LineColorHex = $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(UserSettingsPath)!);
        File.WriteAllText(UserSettingsPath, JsonSerializer.Serialize(this, JsonOptions));
    }

    public static OverlaySettings Load()
    {
        try
        {
            var userSettings = UserSettingsPath;
            string settingsJson;
            if (File.Exists(userSettings))
            {
                settingsJson = File.ReadAllText(userSettings);
            }
            else
            {
                using var settingsStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("CursorLine.settings.json")
                    ?? throw new FileNotFoundException("Embedded default settings are missing.");
                using var reader = new StreamReader(settingsStream);
                settingsJson = reader.ReadToEnd();
            }

            var settings = JsonSerializer.Deserialize<OverlaySettings>(settingsJson, JsonOptions);
            if (settings is not null && double.IsFinite(settings.LineWidthPixels) && settings.LineWidthPixels is >= 1 and <= 100)
            {
                settings.WhitelistedApplications ??= [];
                if (settings.StartCorner is not ("TopLeft" or "TopRight" or "BottomLeft" or "BottomRight"))
                {
                    settings.StartCorner = "BottomRight";
                }

                if (!double.IsFinite(settings.CursorCircleRadiusPixels) || settings.CursorCircleRadiusPixels is < 1 or > 2000)
                {
                    settings.CursorCircleRadiusPixels = 24;
                }

                if (!double.IsFinite(settings.BigCursorScalePixels) || settings.BigCursorScalePixels is < 16 or > 512)
                {
                    settings.BigCursorScalePixels = 128;
                }

                if (!double.IsFinite(settings.BigCursorRotationDegrees) ||
                    settings.BigCursorRotationDegrees is < 0 or > 360)
                {
                    settings.BigCursorRotationDegrees = 0;
                }

                return settings;
            }
        }
        catch (IOException)
        {
        }
        catch (JsonException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return new OverlaySettings();
    }
}