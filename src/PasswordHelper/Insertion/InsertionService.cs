using System;
using System.Threading;
using System.Threading.Tasks;
using PasswordHelper.Core;
using PasswordHelper.Interop;

namespace PasswordHelper.Insertion;

internal enum InsertionOutcome
{
    /// <summary>The value was placed on the clipboard and a paste was sent to the original window.</summary>
    Inserted,

    /// <summary>The value is on the clipboard; the user must paste it themselves.</summary>
    CopiedOnly,
}

internal sealed record InsertionResult(InsertionOutcome Outcome, string? Reason)
{
    public static readonly InsertionResult Inserted = new(InsertionOutcome.Inserted, null);
    public static InsertionResult Copied(string reason) => new(InsertionOutcome.CopiedOnly, reason);
}

/// <summary>
/// Restores the captured foreground window and sends a paste shortcut to it. Every attempt is gated by validation:
/// the target must still be the same window from the same process, not hung, and not running at a higher integrity
/// level than this app. If anything is off, the method does nothing beyond leaving the value on the clipboard and
/// reports <see cref="InsertionOutcome.CopiedOnly"/> so the caller can tell the user to paste manually. It never
/// sends input to a window merely because it became active after the overlay opened.
/// </summary>
internal sealed class InsertionService
{
    private const int FocusAttempts = 3;
    private const int FocusSettleMs = 18;

    /// <summary>
    /// Attempts to paste into <paramref name="target"/>. The value must already be on the clipboard. The overlay
    /// must already be hidden so it is not the foreground window. Runs off the UI thread.
    /// </summary>
    public Task<InsertionResult> TryPasteAsync(ForegroundTarget? target)
    {
        return Task.Run(() => TryPaste(target));
    }

    private InsertionResult TryPaste(ForegroundTarget? target)
    {
        if (target is null)
        {
            return InsertionResult.Copied("There was no text field to paste into.");
        }

        if (!target.IsStillValid())
        {
            return InsertionResult.Copied("The original window is no longer available.");
        }

        if (ProcessUtil.IsHigherIntegrityThanUs(target.ProcessId))
        {
            // The target runs elevated while we do not: Windows blocks synthesized input into it by design.
            return InsertionResult.Copied("The target app runs as administrator, so it must be pasted manually.");
        }

        if (!RestoreForeground(target.Hwnd))
        {
            return InsertionResult.Copied("The original window could not be focused.");
        }

        // Re-validate immediately before sending input: the foreground must now be the intended target.
        IntPtr foreground = Win32.GetForegroundWindow();
        if (!SameTopLevel(foreground, target.Hwnd) || ProcessUtil.GetWindowProcessId(foreground) != target.ProcessId)
        {
            return InsertionResult.Copied("The original window did not come back to the foreground.");
        }

        SendPaste();
        Log.Info($"Pasted into '{target.ProcessName}'.");
        return InsertionResult.Inserted;
    }

    private static bool RestoreForeground(IntPtr hwnd)
    {
        if (Win32.IsIconic(hwnd))
        {
            Win32.ShowWindow(hwnd, Win32.SW_RESTORE);
        }

        for (int attempt = 0; attempt < FocusAttempts; attempt++)
        {
            IntPtr current = Win32.GetForegroundWindow();
            if (SameTopLevel(current, hwnd)) return true;

            uint currentThread = Win32.GetCurrentThreadId();
            uint foregroundThread = Win32.GetWindowThreadProcessId(current, out _);
            uint targetThread = Win32.GetWindowThreadProcessId(hwnd, out uint targetPid);

            // Permit this foreground change and briefly share input state with the involved threads so that
            // SetForegroundWindow is honoured even under Windows' foreground-lock.
            Win32.AllowSetForegroundWindow(unchecked((int)targetPid));
            bool attachedForeground = foregroundThread != 0 && foregroundThread != currentThread &&
                                      Win32.AttachThreadInput(currentThread, foregroundThread, true);
            bool attachedTarget = targetThread != 0 && targetThread != currentThread && targetThread != foregroundThread &&
                                  Win32.AttachThreadInput(currentThread, targetThread, true);
            try
            {
                Win32.BringWindowToTop(hwnd);
                Win32.SetForegroundWindow(hwnd);
            }
            finally
            {
                if (attachedTarget) Win32.AttachThreadInput(currentThread, targetThread, false);
                if (attachedForeground) Win32.AttachThreadInput(currentThread, foregroundThread, false);
            }

            if (SameTopLevel(Win32.GetForegroundWindow(), hwnd)) return true;
            Thread.Sleep(FocusSettleMs);
        }

        return SameTopLevel(Win32.GetForegroundWindow(), hwnd);
    }

    private static bool SameTopLevel(IntPtr a, IntPtr b)
    {
        if (a == IntPtr.Zero || b == IntPtr.Zero) return false;
        if (a == b) return true;
        return Win32.GetAncestor(a, Win32.GA_ROOT) == Win32.GetAncestor(b, Win32.GA_ROOT);
    }

    /// <summary>Sends Ctrl+V, first releasing any modifiers the user is physically holding to avoid stray combos.</summary>
    private static void SendPaste()
    {
        NeutralizeHeldModifiers();

        var inputs = new[]
        {
            KeyInput(Win32.VK_CONTROL, down: true),
            KeyInput('V', down: true),
            KeyInput('V', down: false),
            KeyInput(Win32.VK_CONTROL, down: false),
        };

        Win32.SendInput((uint)inputs.Length, inputs, Win32.INPUT.Size);
    }

    private static void NeutralizeHeldModifiers()
    {
        Span<int> modifiers = stackalloc int[]
        {
            Win32.VK_LSHIFT, Win32.VK_RSHIFT, Win32.VK_LMENU, Win32.VK_RMENU,
            Win32.VK_LWIN, Win32.VK_RWIN, Win32.VK_LCONTROL, Win32.VK_RCONTROL,
        };

        Span<Win32.INPUT> ups = stackalloc Win32.INPUT[modifiers.Length];
        int count = 0;
        foreach (int vk in modifiers)
        {
            if (Win32.IsKeyDown(vk))
            {
                ups[count++] = KeyInput(vk, down: false);
            }
        }

        if (count > 0)
        {
            Win32.SendInput((uint)count, ups[..count].ToArray(), Win32.INPUT.Size);
        }
    }

    private static Win32.INPUT KeyInput(int vk, bool down)
    {
        uint scan = Win32.MapVirtualKeyEx((uint)vk, Win32.MAPVK_VK_TO_VSC, Win32.GetKeyboardLayout(0));
        uint flags = down ? 0 : Win32.KEYEVENTF_KEYUP;
        if (IsExtendedKey(vk)) flags |= Win32.KEYEVENTF_EXTENDEDKEY;

        return new Win32.INPUT
        {
            type = Win32.INPUT_KEYBOARD,
            U = new Win32.InputUnion
            {
                ki = new Win32.KEYBDINPUT
                {
                    wVk = (ushort)vk,
                    wScan = (ushort)scan,
                    dwFlags = flags,
                    time = 0,
                    dwExtraInfo = Win32.InjectedInputSignature,
                },
            },
        };
    }

    private static bool IsExtendedKey(int vk) => vk is Win32.VK_RCONTROL or Win32.VK_RMENU
        or Win32.VK_LWIN or Win32.VK_RWIN or Win32.VK_INSERT or Win32.VK_DELETE or Win32.VK_HOME or Win32.VK_END
        or Win32.VK_PRIOR or Win32.VK_NEXT or Win32.VK_LEFT or Win32.VK_RIGHT or Win32.VK_UP or Win32.VK_DOWN;
}
