using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using PasswordHelper.Core;
using PasswordHelper.Interop;

namespace PasswordHelper.UI.Common;

/// <summary>
/// Produces the frosted-glass backdrop by capturing the desktop behind the overlay once and blurring it. This is a
/// genuine blur of the real pixels behind the window (not a flat grey fill), and it works identically on Windows 10
/// and 11 without relying on an OS backdrop that would blur the whole screen rectangle. Must run on the UI thread.
/// </summary>
internal static class ScreenBlur
{
    /// <summary>Captures one monitor's pixels into a frozen bitmap, or null if the capture fails.</summary>
    public static BitmapSource? CaptureMonitor(MonitorGeometry geo)
    {
        int width = geo.Bounds.Width;
        int height = geo.Bounds.Height;
        if (width <= 0 || height <= 0) return null;

        IntPtr screenDc = Win32.GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero) return null;

        IntPtr memDc = IntPtr.Zero;
        IntPtr dib = IntPtr.Zero;
        IntPtr previous = IntPtr.Zero;
        try
        {
            memDc = Win32.CreateCompatibleDC(screenDc);
            if (memDc == IntPtr.Zero) return null;

            var header = new Win32.BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf<Win32.BITMAPINFOHEADER>(),
                biWidth = width,
                biHeight = -height, // negative = top-down, matching WPF's row order
                biPlanes = 1,
                biBitCount = 32,
                biCompression = 0, // BI_RGB
            };

            dib = Win32.CreateDIBSection(memDc, ref header, Win32.DIB_RGB_COLORS, out IntPtr bits, IntPtr.Zero, 0);
            if (dib == IntPtr.Zero || bits == IntPtr.Zero) return null;

            previous = Win32.SelectObject(memDc, dib);
            if (!Win32.BitBlt(memDc, 0, 0, width, height, screenDc, geo.Bounds.Left, geo.Bounds.Top, Win32.SRCCOPY))
            {
                return null;
            }

            int stride = width * 4;
            byte[] buffer = new byte[stride * height];
            Marshal.Copy(bits, buffer, 0, buffer.Length);

            BitmapSource bmp = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr32, null, buffer, stride);
            bmp.Freeze();
            return bmp;
        }
        catch (Exception ex)
        {
            Log.Warn("Desktop capture for the glass backdrop failed", ex);
            return null;
        }
        finally
        {
            if (previous != IntPtr.Zero) Win32.SelectObject(memDc, previous);
            if (dib != IntPtr.Zero) Win32.DeleteObject(dib);
            if (memDc != IntPtr.Zero) Win32.DeleteDC(memDc);
            Win32.ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    /// <summary>
    /// Returns a blurred, downscaled copy of <paramref name="source"/>. Downscaling first makes the Gaussian cheap
    /// and, when the result is stretched back to full size, yields a smooth, strong blur.
    /// </summary>
    public static BitmapSource Blur(BitmapSource source, double intensity)
    {
        double scale = 0.35;
        int w = Math.Max(1, (int)(source.PixelWidth * scale));
        int h = Math.Max(1, (int)(source.PixelHeight * scale));
        double radius = 6 + intensity * 16; // intensity 0..1 -> radius 6..22 in downscaled space

        var visual = new DrawingVisual();
        using (DrawingContext dc = visual.RenderOpen())
        {
            dc.DrawImage(source, new Rect(0, 0, w, h));
        }

        visual.Effect = new BlurEffect
        {
            Radius = radius,
            KernelType = KernelType.Gaussian,
            RenderingBias = RenderingBias.Performance,
        };

        var target = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        target.Freeze();
        return target;
    }

    public static BitmapSource? CaptureAndBlur(MonitorGeometry geo, double intensity)
    {
        BitmapSource? captured = CaptureMonitor(geo);
        return captured is null ? null : Blur(captured, intensity);
    }
}
