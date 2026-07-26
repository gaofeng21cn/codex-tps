using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace CodexTPS.WindowsApp;

internal sealed class TaskbarReadoutForm : Form
{
    private const int WsExNoActivate = 0x08000000;
    private const int WsExToolWindow = 0x00000080;
    private const uint AbmGetState = 0x00000004;
    private const uint AbsAutoHide = 0x00000001;
    private const uint MonitorDefaultToNearest = 0x00000002;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;
    private static readonly IntPtr HwndTopmost = new(-1);

    private readonly System.Windows.Forms.Timer placementTimer = new()
    {
        Interval = 1_000,
    };
    private TaskbarEdge edge = TaskbarEdge.Bottom;
    private string rateText = "-- t/s";

    public TaskbarReadoutForm(ContextMenuStrip contextMenu)
    {
        AccessibleName = "Codex TPS 任务栏读数";
        BackColor = Color.FromArgb(36, 36, 38);
        ContextMenuStrip = contextMenu;
        Cursor = Cursors.Hand;
        DoubleBuffered = true;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;

        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint,
            value: true);
        placementTimer.Tick += (_, _) => RefreshPlacement();
    }

    public event EventHandler? OpenRequested;

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |= WsExNoActivate | WsExToolWindow;
            return parameters;
        }
    }

    public void Start()
    {
        RefreshPlacement();
        placementTimer.Start();
    }

    public void SetRate(double? tokensPerSecond)
    {
        var next = tokensPerSecond.HasValue && double.IsFinite(tokensPerSecond.Value)
            ? $"{Compact(Math.Max(0, tokensPerSecond.Value))} t/s"
            : "-- t/s";
        if (next == rateText)
        {
            return;
        }

        rateText = next;
        AccessibleDescription = $"当前吞吐率 {next}";
        Invalidate();
    }

    protected override void OnMouseClick(MouseEventArgs eventArgs)
    {
        base.OnMouseClick(eventArgs);
        if (eventArgs.Button == MouseButtons.Left)
        {
            OpenRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        base.OnPaint(eventArgs);
        eventArgs.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        eventArgs.Graphics.TextRenderingHint =
            System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var radius = Math.Max(4, 5 * DeviceDpi / 96);
        using var background = RoundedRectangle(
            new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1)),
            radius);
        using var backgroundBrush = new SolidBrush(Color.FromArgb(36, 36, 38));
        eventArgs.Graphics.FillPath(backgroundBrush, background);

        var vertical = edge is TaskbarEdge.Left or TaskbarEdge.Right;
        var displayText = vertical
            ? rateText.Replace(" ", Environment.NewLine, StringComparison.Ordinal)
            : rateText;
        using var textBrush = new SolidBrush(Color.White);
        using var font = new Font(
            "Segoe UI Variable Text",
            vertical ? 8f : 9f,
            FontStyle.Bold);
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            FormatFlags = StringFormatFlags.NoWrap,
        };
        eventArgs.Graphics.DrawString(
            displayText,
            font,
            textBrush,
            ClientRectangle,
            format);
    }

    protected override void OnSizeChanged(EventArgs eventArgs)
    {
        base.OnSizeChanged(eventArgs);
        if (Width <= 0 || Height <= 0)
        {
            return;
        }

        var radius = Math.Max(4, 5 * DeviceDpi / 96);
        using var path = RoundedRectangle(
            new Rectangle(0, 0, Width, Height),
            radius);
        var previous = Region;
        Region = new Region(path);
        previous?.Dispose();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            placementTimer.Stop();
            placementTimer.Dispose();
            ContextMenuStrip = null;
        }
        base.Dispose(disposing);
    }

    private void RefreshPlacement()
    {
        if (!TaskbarNative.TryGetGeometry(out var geometry))
        {
            Hide();
            return;
        }

        var placement = TaskbarPlacement.Calculate(geometry);
        edge = placement.Edge;
        if (!placement.IsVisible)
        {
            Hide();
            return;
        }

        if (!Visible)
        {
            Show();
        }

        SetWindowPos(
            Handle,
            HwndTopmost,
            placement.Bounds.X,
            placement.Bounds.Y,
            placement.Bounds.Width,
            placement.Bounds.Height,
            SwpNoActivate | SwpShowWindow);
        Invalidate();
    }

    private static GraphicsPath RoundedRectangle(Rectangle rectangle, float radius)
    {
        var diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(rectangle.Left, rectangle.Top, diameter, diameter, 180, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Top, diameter, diameter, 270, 90);
        path.AddArc(
            rectangle.Right - diameter,
            rectangle.Bottom - diameter,
            diameter,
            diameter,
            0,
            90);
        path.AddArc(rectangle.Left, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static string Compact(double value) => value switch
    {
        >= 1_000_000 => $"{value / 1_000_000:0.0}M",
        >= 1_000 => $"{value / 1_000:0.0}K",
        _ => $"{value:0.0}",
    };

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(
        IntPtr window,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    private static class TaskbarNative
    {
        public static bool TryGetGeometry(out TaskbarGeometry geometry)
        {
            geometry = default;
            var taskbar = FindWindow("Shell_TrayWnd", null);
            if (taskbar == IntPtr.Zero ||
                !IsWindowVisible(taskbar) ||
                !GetWindowRect(taskbar, out var taskbarRectangle))
            {
                return false;
            }

            var monitor = MonitorFromWindow(taskbar, MonitorDefaultToNearest);
            var monitorInfo = new MonitorInfo
            {
                Size = Marshal.SizeOf<MonitorInfo>(),
            };
            if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref monitorInfo))
            {
                return false;
            }

            Rectangle? notificationBounds = null;
            var notification = FindWindowEx(taskbar, IntPtr.Zero, "TrayNotifyWnd", null);
            if (notification != IntPtr.Zero &&
                IsWindowVisible(notification) &&
                GetWindowRect(notification, out var notificationRectangle))
            {
                notificationBounds = notificationRectangle.ToRectangle();
            }

            var appBarData = new AppBarData
            {
                Size = (uint)Marshal.SizeOf<AppBarData>(),
                Window = taskbar,
            };
            var appBarState = SHAppBarMessage(AbmGetState, ref appBarData);
            geometry = new TaskbarGeometry(
                taskbarRectangle.ToRectangle(),
                monitorInfo.Monitor.ToRectangle(),
                notificationBounds,
                GetWindowDpi(taskbar),
                AutoHide: (appBarState & AbsAutoHide) != 0);
            return true;
        }

        private static int GetWindowDpi(IntPtr window)
        {
            try
            {
                var dpi = GetDpiForWindow(window);
                return dpi > 0 ? (int)dpi : 96;
            }
            catch (EntryPointNotFoundException)
            {
                return 96;
            }
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindow(string className, string? windowName);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindowEx(
            IntPtr parent,
            IntPtr childAfter,
            string className,
            string? windowName);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr window, out NativeRectangle rectangle);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowVisible(IntPtr window);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo monitorInfo);

        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr window);

        [DllImport("shell32.dll")]
        private static extern uint SHAppBarMessage(uint message, ref AppBarData data);

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRectangle
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;

            public readonly Rectangle ToRectangle() => Rectangle.FromLTRB(
                Left,
                Top,
                Right,
                Bottom);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MonitorInfo
        {
            public int Size;
            public NativeRectangle Monitor;
            public NativeRectangle WorkArea;
            public uint Flags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct AppBarData
        {
            public uint Size;
            public IntPtr Window;
            public uint CallbackMessage;
            public uint Edge;
            public NativeRectangle Rectangle;
            public IntPtr Parameter;
        }
    }
}
