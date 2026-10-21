using System;
using System.Runtime.InteropServices;
using System.Threading;
using PasswordHelper.Core;
using PasswordHelper.Interop;

namespace PasswordHelper.ClipboardServices;

/// <summary>
/// Thin, defensive wrapper over the Win32 clipboard. Every method opens the clipboard with a short bounded retry
/// loop (another process may hold it briefly) and always closes it. Returns success flags rather than throwing,
/// because clipboard access failing transiently must never crash the app.
/// </summary>
internal static class ClipboardText
{
    private const int MaxOpenAttempts = 8;
    private const int OpenRetryDelayMs = 25;
    private const int MaxReadChars = 1024 * 1024; // 1M chars; longer clipboard text is ignored for history

    // Registered formats (lazily, once).
    public static readonly uint OwnerTokenFormat = Win32.RegisterClipboardFormat("PasswordHelper.OwnerToken");
    public static readonly uint ExcludeMonitorFormat = Win32.RegisterClipboardFormat("ExcludeClipboardContentFromMonitorProcessing");
    public static readonly uint CloudUploadFormat = Win32.RegisterClipboardFormat("CanUploadToCloudClipboard");
    public static readonly uint HistoryFormat = Win32.RegisterClipboardFormat("CanIncludeInClipboardHistory");

    public static uint SequenceNumber => Win32.GetClipboardSequenceNumber();

    public static bool IsTextAvailable() => Win32.IsClipboardFormatAvailable(Win32.CF_UNICODETEXT);

    private static bool Open(IntPtr owner)
    {
        for (int attempt = 0; attempt < MaxOpenAttempts; attempt++)
        {
            if (Win32.OpenClipboard(owner)) return true;
            Thread.Sleep(OpenRetryDelayMs);
        }

        Log.Warn("Could not open the clipboard; it is held by another application.");
        return false;
    }

    /// <summary>
    /// Replaces the clipboard with <paramref name="text"/>. When <paramref name="sensitive"/> is set, also writes the
    /// hints that keep the value out of Windows clipboard history (Win+V) and Cloud Clipboard sync. Writes the owner
    /// token so we can later recognise our own content. Returns the clipboard sequence number after the write, or 0.
    /// </summary>
    public static uint SetText(IntPtr owner, ReadOnlySpan<char> text, bool sensitive, ReadOnlySpan<byte> ownerToken)
    {
        if (!Open(owner)) return 0;
        try
        {
            Win32.EmptyClipboard();

            if (!SetUnicodeText(text))
            {
                Log.Warn("Writing text to the clipboard failed.");
                return 0;
            }

            if (!ownerToken.IsEmpty)
            {
                SetBytes(OwnerTokenFormat, ownerToken);
            }

            if (sensitive)
            {
                // Any data under this format tells clipboard managers to skip the content.
                SetBytes(ExcludeMonitorFormat, stackalloc byte[] { 0 });
                SetDword(HistoryFormat, 0);
                SetDword(CloudUploadFormat, 0);
            }

            return Win32.GetClipboardSequenceNumber();
        }
        finally
        {
            Win32.CloseClipboard();
        }
    }

    /// <summary>Empties the clipboard. Caller is responsible for deciding that this is safe to do.</summary>
    public static bool Clear(IntPtr owner)
    {
        if (!Open(owner)) return false;
        try
        {
            return Win32.EmptyClipboard();
        }
        finally
        {
            Win32.CloseClipboard();
        }
    }

    public static bool TryGetText(IntPtr owner, out string text)
    {
        text = string.Empty;
        if (!Win32.IsClipboardFormatAvailable(Win32.CF_UNICODETEXT)) return false;
        if (!Open(owner)) return false;
        try
        {
            IntPtr handle = Win32.GetClipboardData(Win32.CF_UNICODETEXT);
            if (handle == IntPtr.Zero) return false;

            IntPtr ptr = Win32.GlobalLock(handle);
            if (ptr == IntPtr.Zero) return false;
            try
            {
                nuint bytes = (nuint)Win32.GlobalSize(handle);
                int maxChars = (int)Math.Min((ulong)MaxReadChars, (ulong)bytes / sizeof(char));
                text = Marshal.PtrToStringUni(ptr, maxChars) ?? string.Empty;
                int nul = text.IndexOf('\0');
                if (nul >= 0) text = text[..nul];
                return true;
            }
            finally
            {
                Win32.GlobalUnlock(handle);
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Reading clipboard text failed", ex);
            return false;
        }
        finally
        {
            Win32.CloseClipboard();
        }
    }

    /// <summary>Reads our owner-token format, if present. Used to recognise clipboard content this app placed.</summary>
    public static bool TryGetOwnerToken(IntPtr owner, Span<byte> buffer, out int written)
    {
        written = 0;
        if (!Win32.IsClipboardFormatAvailable(OwnerTokenFormat)) return false;
        if (!Open(owner)) return false;
        try
        {
            IntPtr handle = Win32.GetClipboardData(OwnerTokenFormat);
            if (handle == IntPtr.Zero) return false;
            IntPtr ptr = Win32.GlobalLock(handle);
            if (ptr == IntPtr.Zero) return false;
            try
            {
                int size = Math.Min(buffer.Length, (int)Win32.GlobalSize(handle));
                for (int i = 0; i < size; i++) buffer[i] = Marshal.ReadByte(ptr, i);
                written = size;
                return size > 0;
            }
            finally
            {
                Win32.GlobalUnlock(handle);
            }
        }
        finally
        {
            Win32.CloseClipboard();
        }
    }

    public static bool IsMonitoringExcluded() => Win32.IsClipboardFormatAvailable(ExcludeMonitorFormat);

    /// <summary>Process id of the window that currently owns the clipboard, or 0 if unknown.</summary>
    public static uint GetOwnerProcessId()
    {
        IntPtr ownerWindow = Win32.GetClipboardOwner();
        if (ownerWindow == IntPtr.Zero) return 0;
        Win32.GetWindowThreadProcessId(ownerWindow, out uint pid);
        return pid;
    }

    private static unsafe bool SetUnicodeText(ReadOnlySpan<char> text)
    {
        int bytes = (text.Length + 1) * sizeof(char);
        IntPtr hGlobal = Win32.GlobalAlloc(Win32.GMEM_MOVEABLE, (UIntPtr)bytes);
        if (hGlobal == IntPtr.Zero) return false;

        IntPtr target = Win32.GlobalLock(hGlobal);
        if (target == IntPtr.Zero)
        {
            Win32.GlobalFree(hGlobal);
            return false;
        }

        try
        {
            var dest = new Span<char>((void*)target, text.Length + 1);
            text.CopyTo(dest);
            dest[text.Length] = '\0';
        }
        finally
        {
            Win32.GlobalUnlock(hGlobal);
        }

        if (Win32.SetClipboardData(Win32.CF_UNICODETEXT, hGlobal) == IntPtr.Zero)
        {
            Win32.GlobalFree(hGlobal); // ownership not transferred on failure
            return false;
        }

        return true;
    }

    private static unsafe bool SetBytes(uint format, ReadOnlySpan<byte> data)
    {
        int size = Math.Max(1, data.Length);
        IntPtr hGlobal = Win32.GlobalAlloc(Win32.GMEM_MOVEABLE, (UIntPtr)size);
        if (hGlobal == IntPtr.Zero) return false;

        IntPtr target = Win32.GlobalLock(hGlobal);
        if (target == IntPtr.Zero)
        {
            Win32.GlobalFree(hGlobal);
            return false;
        }

        try
        {
            var dest = new Span<byte>((void*)target, size);
            dest.Clear();
            data.CopyTo(dest);
        }
        finally
        {
            Win32.GlobalUnlock(hGlobal);
        }

        if (Win32.SetClipboardData(format, hGlobal) == IntPtr.Zero)
        {
            Win32.GlobalFree(hGlobal);
            return false;
        }

        return true;
    }

    private static bool SetDword(uint format, uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BitConverter.TryWriteBytes(bytes, value);
        return SetBytes(format, bytes);
    }
}
