using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Interop;

namespace PetHelper;

/// <summary>
/// Moves a WPF window through one atomic Win32 SetWindowPos call. Setting WPF's Left/Top
/// individually issues two SetWindowPos calls and can briefly render the intermediate
/// (X moved, Y not yet) position, which shows up as flicker on transparent windows during
/// high-frequency drags. Moving the OS window directly is also what makes DragMove smooth:
/// the window surface is not redrawn while it is being repositioned.
/// WPF's Left/Top/Width/Height dependency properties are intentionally not updated here;
/// callers re-apply the final rect through the normal property setters at drag end.
/// </summary>
internal static class WindowMover
{
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint WM_NCLBUTTONDOWN = 0x00A1;

    /// <summary>Repositions the window without changing its size, Z order, or activation.</summary>
    public static void Move(Window window, double left, double top)
    {
        var dpi = VisualTreeHelper.GetDpi(window);
        var x = (int)Math.Round(left * dpi.DpiScaleX);
        var y = (int)Math.Round(top * dpi.DpiScaleY);
        SetWindowPos(
            new WindowInteropHelper(window).Handle,
            IntPtr.Zero,
            x,
            y,
            0,
            0,
            SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
    }

    /// <summary>Repositions and resizes the window in one call, without changing Z order or activation.</summary>
    public static void MoveAndResize(Window window, Rect rect)
    {
        var dpi = VisualTreeHelper.GetDpi(window);
        var x = (int)Math.Round(rect.X * dpi.DpiScaleX);
        var y = (int)Math.Round(rect.Y * dpi.DpiScaleY);
        var width = (int)Math.Round(rect.Width * dpi.DpiScaleX);
        var height = (int)Math.Round(rect.Height * dpi.DpiScaleY);
        SetWindowPos(
            new WindowInteropHelper(window).Handle,
            IntPtr.Zero,
            x,
            y,
            width,
            height,
            SWP_NOZORDER | SWP_NOACTIVATE);
    }

    /// <summary>
    /// Lets Windows own an interactive resize. This keeps left and top edges stable because
    /// the OS updates window position and size as one operation instead of racing WPF layout.
    /// </summary>
    public static void BeginNativeResize(Window window, ResizeEdge edge)
    {
        var hitTestCode = WindowResizeMath.NativeHitTestCode(edge);
        var handle = new WindowInteropHelper(window).Handle;
        if (hitTestCode == 0 || handle == IntPtr.Zero) return;

        ReleaseCapture();
        SendMessage(handle, WM_NCLBUTTONDOWN, (IntPtr)hitTestCode, IntPtr.Zero);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
}
