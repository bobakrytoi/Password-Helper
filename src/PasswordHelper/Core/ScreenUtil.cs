using System;
using PasswordHelper.Interop;

namespace PasswordHelper.Core;

/// <summary>Geometry of a display, in device pixels, together with its effective DPI.</summary>
internal readonly record struct MonitorGeometry(Win32.RECT Bounds, Win32.RECT WorkArea, uint Dpi)
{
    public double Scale => Dpi / 96.0;
}

/// <summary>Monitor lookup helpers used to place the overlay on the right screen in device pixels.</summary>
internal static class ScreenUtil
{
    public static Win32.POINT GetCursorPosition()
    {
        Win32.GetCursorPos(out Win32.POINT p);
        return p;
    }

    public static MonitorGeometry GetMonitorFromCursor() => Describe(Win32.MonitorFromPoint(GetCursorPosition(), Win32.MONITOR_DEFAULTTONEAREST));

    public static MonitorGeometry GetMonitorFromPoint(int x, int y) =>
        Describe(Win32.MonitorFromPoint(new Win32.POINT(x, y), Win32.MONITOR_DEFAULTTONEAREST));

    public static MonitorGeometry GetMonitorFromWindow(IntPtr hwnd) =>
        Describe(Win32.MonitorFromWindow(hwnd, Win32.MONITOR_DEFAULTTONEAREST));

    private static MonitorGeometry Describe(IntPtr monitor)
    {
        var info = new Win32.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Win32.MONITORINFO>() };
        uint dpi = 96;
        if (monitor != IntPtr.Zero)
        {
            Win32.GetMonitorInfo(monitor, ref info);
            if (Win32.GetDpiForMonitor(monitor, Win32.MDT_EFFECTIVE_DPI, out uint dpiX, out _) == 0 && dpiX > 0)
            {
                dpi = dpiX;
            }
        }

        if (info.rcMonitor.Width == 0)
        {
            // Fallback to a sane default if the monitor could not be described.
            info.rcMonitor = new Win32.RECT(0, 0, 1920, 1080);
            info.rcWork = info.rcMonitor;
        }

        return new MonitorGeometry(info.rcMonitor, info.rcWork, dpi);
    }
}
