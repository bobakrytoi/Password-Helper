using System;
using System.Runtime.InteropServices;
using PasswordHelper.Core;
using PasswordHelper.Interop;

namespace PasswordHelper.Input;

internal enum HotkeyRegistrationStatus
{
    Registered,
    Conflict,
    Invalid,
    Failed,
}

internal sealed record HotkeyRegistrationResult(HotkeyRegistrationStatus Status, HotkeyGesture Gesture, string? Message)
{
    public bool Success => Status == HotkeyRegistrationStatus.Registered;
}

/// <summary>
/// Registers the activation shortcut with Windows' <c>RegisterHotKey</c>. This is a pull mechanism: Windows posts
/// <c>WM_HOTKEY</c> to the message window only when the exact registered combination is pressed, so the app never
/// observes any other keystroke. <c>MOD_NOREPEAT</c> ensures a single press fires exactly once even if held down.
/// </summary>
internal sealed class HotkeyManager : IDisposable
{
    private const int HotkeyId = 0xB001;

    private readonly IntPtr _hwnd;
    private HotkeyGesture? _registered;
    private bool _disposed;

    public HotkeyManager(MessageWindow window)
    {
        _hwnd = window.Handle;
        window.HotkeyPressed += OnHotkeyPressed;
    }

    /// <summary>Raised on the UI thread when the registered hotkey is pressed.</summary>
    public event Action? Activated;

    public HotkeyGesture? Current => _registered;

    private void OnHotkeyPressed(int id)
    {
        if (id == HotkeyId) Activated?.Invoke();
    }

    /// <summary>
    /// Validates and registers <paramref name="gesture"/>. On failure the previously working hotkey is left
    /// registered (the caller is told what went wrong and nothing is silently overwritten).
    /// </summary>
    public HotkeyRegistrationResult Register(HotkeyGesture gesture)
    {
        string? problem = gesture.Validate();
        if (problem is not null)
        {
            return new HotkeyRegistrationResult(HotkeyRegistrationStatus.Invalid, gesture, problem);
        }

        if (_registered == gesture)
        {
            return new HotkeyRegistrationResult(HotkeyRegistrationStatus.Registered, gesture, null);
        }

        // Register the new hotkey under a temporary id first, so a failure never leaves us with no hotkey at all.
        const int probeId = HotkeyId + 1;
        uint modifiers = gesture.ToNativeModifiers() | Win32.MOD_NOREPEAT;
        if (!Win32.RegisterHotKey(_hwnd, probeId, modifiers, (uint)gesture.Key))
        {
            int error = Marshal.GetLastPInvokeError();
            if (error == Win32.ERROR_HOTKEY_ALREADY_REGISTERED)
            {
                return new HotkeyRegistrationResult(HotkeyRegistrationStatus.Conflict, gesture,
                    $"{gesture.ToDisplayString()} is already in use by another application. Choose a different shortcut.");
            }

            return new HotkeyRegistrationResult(HotkeyRegistrationStatus.Failed, gesture,
                $"Windows could not register {gesture.ToDisplayString()} (error 0x{error:X8}).");
        }

        Win32.UnregisterHotKey(_hwnd, probeId);

        Unregister();
        if (!Win32.RegisterHotKey(_hwnd, HotkeyId, modifiers, (uint)gesture.Key))
        {
            int error = Marshal.GetLastPInvokeError();
            Log.Error($"Hotkey registration failed after the probe succeeded (0x{error:X8})");
            return new HotkeyRegistrationResult(HotkeyRegistrationStatus.Failed, gesture,
                "Windows could not register the shortcut.");
        }

        _registered = gesture;
        Log.Info($"Hotkey registered: {gesture}");
        return new HotkeyRegistrationResult(HotkeyRegistrationStatus.Registered, gesture, null);
    }

    /// <summary>Re-applies the current registration, e.g. defensively after the system resumes from sleep.</summary>
    public void Reapply()
    {
        if (_registered is { } gesture)
        {
            Win32.UnregisterHotKey(_hwnd, HotkeyId);
            if (!Win32.RegisterHotKey(_hwnd, HotkeyId, gesture.ToNativeModifiers() | Win32.MOD_NOREPEAT, (uint)gesture.Key))
            {
                Log.Warn($"Could not re-register the hotkey after resume (0x{Marshal.GetLastPInvokeError():X8})");
            }
        }
    }

    private void Unregister()
    {
        if (_registered is not null)
        {
            Win32.UnregisterHotKey(_hwnd, HotkeyId);
            _registered = null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Unregister();
    }
}
