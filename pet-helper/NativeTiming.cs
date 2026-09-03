using System.Runtime.InteropServices;

namespace PetHelper;

/// <summary>
/// Native timing helpers for physics-flight diagnostics: a display-refresh probe and a
/// 1 ms Windows timer resolution while a flight is running. Pure Win32, no content, no state.
/// </summary>
internal static class NativeTiming
{
    private const int DeviceCapabilityVerticalRefresh = 116;

    /// <summary>Returns the nominal refresh rate in Hz of the display the window is on, or 0.</summary>
    public static double ProbeDisplayRefreshHz(nint hwnd)
    {
        try
        {
            var dc = GetDC(hwnd);
            if (dc == IntPtr.Zero) return 0d;
            try
            {
                var hz = GetDeviceCaps(dc, DeviceCapabilityVerticalRefresh);
                return hz > 1 ? hz : 0d;
            }
            finally
            {
                ReleaseDC(hwnd, dc);
            }
        }
        catch
        {
            return 0d;
        }
    }

    public static void EnableHighResolutionTimer() => timeBeginPeriod(1);

    public static void DisableHighResolutionTimer() => timeEndPeriod(1);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern int GetDeviceCaps(IntPtr hdc, int nIndex);

    [DllImport("winmm.dll")]
    private static extern uint timeBeginPeriod(uint uMilliseconds);

    [DllImport("winmm.dll")]
    private static extern uint timeEndPeriod(uint uMilliseconds);
}
