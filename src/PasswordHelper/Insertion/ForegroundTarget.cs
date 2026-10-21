using System;
using PasswordHelper.Core;
using PasswordHelper.Interop;

namespace PasswordHelper.Insertion;

/// <summary>
/// A snapshot of the window that was in the foreground just before the overlay opened. The process id is captured
/// too, so that if the window handle is later reused by a different process we can detect it and refuse to paste.
/// </summary>
internal sealed class ForegroundTarget
{
    public required IntPtr Hwnd { get; init; }
    public required uint ProcessId { get; init; }
    public required string ProcessName { get; init; }
    public required string ClassName { get; init; }
    public DateTime CapturedUtc { get; init; } = DateTime.UtcNow;

    /// <summary>True when the handle still refers to the same, usable window from the same process.</summary>
    public bool IsStillValid()
    {
        if (Hwnd == IntPtr.Zero || !Win32.IsWindow(Hwnd)) return false;
        if (ProcessUtil.GetWindowProcessId(Hwnd) != ProcessId) return false; // handle reused by another process
        if (Win32.IsHungAppWindow(Hwnd)) return false;
        return true;
    }
}

internal static class ForegroundCapture
{
    /// <summary>
    /// Captures the current foreground window, or null when there is none or it belongs to this application
    /// (in which case there is nothing meaningful to paste back into).
    /// </summary>
    public static ForegroundTarget? Capture()
    {
        IntPtr hwnd = Win32.GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return null;

        uint pid = ProcessUtil.GetWindowProcessId(hwnd);
        if (pid == 0 || pid == ProcessUtil.CurrentProcessId) return null;

        return new ForegroundTarget
        {
            Hwnd = hwnd,
            ProcessId = pid,
            ProcessName = ProcessUtil.GetImageFileName(pid),
            ClassName = Win32.GetClassName(hwnd),
        };
    }
}
