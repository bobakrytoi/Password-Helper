using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using PasswordHelper.Interop;

namespace PasswordHelper.Core;

/// <summary>Read-only inspection of other processes: image name and integrity level.</summary>
internal static class ProcessUtil
{
    public static readonly uint CurrentProcessId = (uint)Environment.ProcessId;

    /// <summary>Full image path of a process, or empty on failure (access denied, process gone).</summary>
    public static string GetImagePath(uint processId)
    {
        if (processId == 0) return string.Empty;
        IntPtr handle = Win32.OpenProcess(Win32.PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (handle == IntPtr.Zero) return string.Empty;
        try
        {
            var sb = new StringBuilder(1024);
            int size = sb.Capacity;
            return Win32.QueryFullProcessImageName(handle, 0, sb, ref size) ? sb.ToString() : string.Empty;
        }
        finally
        {
            Win32.CloseHandle(handle);
        }
    }

    public static string GetImageFileName(uint processId)
    {
        string path = GetImagePath(processId);
        return path.Length == 0 ? string.Empty : Path.GetFileName(path);
    }

    public static uint GetWindowProcessId(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return 0;
        Win32.GetWindowThreadProcessId(hwnd, out uint pid);
        return pid;
    }

    /// <summary>
    /// True when the process runs at a higher integrity level than this one (e.g. an elevated/admin window while we
    /// run as a standard user). Synthesized input cannot reach such windows, so insertion must fall back to copy-only.
    /// </summary>
    public static bool IsHigherIntegrityThanUs(uint processId)
    {
        int? ours = GetIntegrityLevel(CurrentProcessId);
        int? theirs = GetIntegrityLevel(processId);
        return ours is { } o && theirs is { } t && t > o;
    }

    private static int? GetIntegrityLevel(uint processId)
    {
        IntPtr process = processId == CurrentProcessId
            ? GetCurrentProcessPseudoHandle()
            : Win32.OpenProcess(Win32.PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (process == IntPtr.Zero) return null;

        bool closeProcess = process != GetCurrentProcessPseudoHandle();
        IntPtr token = IntPtr.Zero;
        try
        {
            if (!Win32.OpenProcessToken(process, Win32.TOKEN_QUERY, out token)) return null;

            Win32.GetTokenInformation(token, Win32.TokenIntegrityLevel, IntPtr.Zero, 0, out int needed);
            if (needed <= 0) return null;

            IntPtr buffer = Marshal.AllocHGlobal(needed);
            try
            {
                if (!Win32.GetTokenInformation(token, Win32.TokenIntegrityLevel, buffer, needed, out _)) return null;

                // TOKEN_MANDATORY_LABEL { SID_AND_ATTRIBUTES Label { PSID Sid; DWORD Attributes; } }
                IntPtr sid = Marshal.ReadIntPtr(buffer); // Label.Sid is the first field
                IntPtr countPtr = Win32.GetSidSubAuthorityCount(sid);
                if (countPtr == IntPtr.Zero) return null;
                int count = Marshal.ReadByte(countPtr);
                if (count == 0) return null;

                IntPtr levelPtr = Win32.GetSidSubAuthority(sid, (uint)(count - 1));
                return levelPtr == IntPtr.Zero ? null : Marshal.ReadInt32(levelPtr);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        finally
        {
            if (token != IntPtr.Zero) Win32.CloseHandle(token);
            if (closeProcess) Win32.CloseHandle(process);
        }
    }

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    private static IntPtr GetCurrentProcessPseudoHandle() => GetCurrentProcess();
}
