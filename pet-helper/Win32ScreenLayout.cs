using System.Runtime.InteropServices;
using System.Windows;

namespace PetHelper;

/// <summary>
/// Win32-backed monitor layout. Work areas are returned in DIPs: each monitor's physical
/// rcWork is scaled by that monitor's DPI, matching WPF's device-independent coordinate
/// system. Falls back to the primary <see cref="SystemParameters.WorkArea"/> on any API
/// failure so the pet never dies because of a layout probe.
/// </summary>
public sealed class Win32ScreenLayout : IScreenLayout
{
    private const uint MonitorDefaultToPrimary = 0x1;
    private const uint MonitorDefaultToNearest = 0x2;
    private const int MonitorInfoFPrimary = 0x1;

    private readonly Rect primaryWorkArea;
    private readonly IReadOnlyList<Rect> workAreas;
    private readonly MonitorWorkAreaCache monitorWorkAreaCache = new();

    public Win32ScreenLayout()
    {
        try
        {
            var monitors = new List<(IntPtr Handle, RECT Work, bool Primary)>();
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (monitor, _, _, _) =>
            {
                var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
                if (GetMonitorInfo(monitor, ref info))
                {
                    monitors.Add((monitor, info.rcWork, (info.dwFlags & MonitorInfoFPrimary) != 0));
                }
                return true;
            }, IntPtr.Zero);

            if (monitors.Count == 0) throw new InvalidOperationException("no monitors enumerated");

            var converted = monitors
                .Select(entry => new { Rect = ToDip(entry.Work, MonitorDpi(entry.Handle)), Primary = entry.Primary })
                .ToList();
            workAreas = converted.Select(entry => entry.Rect).ToList();
            primaryWorkArea = converted.FirstOrDefault(entry => entry.Primary)?.Rect ?? workAreas[0];
        }
        catch
        {
            primaryWorkArea = SystemParameters.WorkArea;
            workAreas = [SystemParameters.WorkArea];
        }
    }

    public Rect PrimaryWorkArea => primaryWorkArea;

    public IReadOnlyList<Rect> WorkAreas => workAreas;

    public Rect WorkAreaFor(Rect windowRect)
    {
        var center = new Point(windowRect.X + windowRect.Width / 2, windowRect.Y + windowRect.Height / 2);
        var point = new POINT((int)Math.Round(center.X), (int)Math.Round(center.Y));
        var monitor = MonitorFromPoint(point, MonitorDefaultToNearest);
        if (monitorWorkAreaCache.TryGet(monitor, out var cached)) return cached;
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        var workArea = GetMonitorInfo(monitor, ref info)
            ? ToDip(info.rcWork, MonitorDpi(monitor))
            : primaryWorkArea;
        monitorWorkAreaCache.Add(monitor, workArea);
        return workArea;
    }

    private static uint MonitorDpi(nint monitor)
    {
        if (GetDpiForMonitor(monitor, 0, out var dpiX, out _) == 0)
        {
            return Math.Max(dpiX, 1u);
        }
        return 96u;
    }

    private static Rect ToDip(RECT physical, uint dpi)
    {
        var scale = 96d / dpi;
        return new Rect(physical.Left * scale, physical.Top * scale,
            physical.Right * scale - physical.Left * scale,
            physical.Bottom * scale - physical.Top * scale);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;

        public POINT(int x, int y)
        {
            X = x;
            Y = y;
        }

        public static readonly POINT Zero = new(0, 0);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public int dwFlags;
    }

    private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdc, IntPtr lprcClip, IntPtr data);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc callback, IntPtr data);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT pt, uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);
}
