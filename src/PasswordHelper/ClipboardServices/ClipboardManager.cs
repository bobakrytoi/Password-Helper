using System;
using System.Security.Cryptography;
using System.Windows.Threading;
using PasswordHelper.Core;

namespace PasswordHelper.ClipboardServices;

/// <summary>
/// Writes values to the clipboard on the app's behalf and remembers which writes were ours. A random "owner token"
/// is written alongside each value, so auto-clear only ever empties the clipboard while it still holds the exact
/// value we put there — a value copied afterwards by another application is never erased.
/// </summary>
internal sealed class ClipboardManager : IDisposable
{
    private readonly IntPtr _owner;
    private readonly SettingsService _settings;
    private readonly DispatcherTimer _clearTimer;
    private readonly byte[] _lastToken = new byte[16];
    private bool _haveToken;

    public ClipboardManager(IntPtr owner, SettingsService settings)
    {
        _owner = owner;
        _settings = settings;
        _clearTimer = new DispatcherTimer(DispatcherPriority.Background);
        _clearTimer.Tick += (_, _) =>
        {
            _clearTimer.Stop();
            ClearIfStillOurs();
        };
    }

    /// <summary>Copies a password. Keeps it out of Windows history/cloud sync and schedules the auto-clear.</summary>
    public bool SetSensitiveText(ReadOnlySpan<char> password)
    {
        RandomNumberGenerator.Fill(_lastToken);
        _haveToken = true;
        uint sequence = ClipboardText.SetText(_owner, password, sensitive: true, _lastToken);
        if (sequence == 0)
        {
            _haveToken = false;
            return false;
        }

        ScheduleAutoClear();
        return true;
    }

    /// <summary>Copies ordinary text (a username or a re-copied history entry).</summary>
    public bool SetText(ReadOnlySpan<char> text)
    {
        _clearTimer.Stop();
        RandomNumberGenerator.Fill(_lastToken);
        _haveToken = true;
        uint sequence = ClipboardText.SetText(_owner, text, sensitive: false, _lastToken);
        if (sequence == 0)
        {
            _haveToken = false;
            return false;
        }

        return true;
    }

    public void CancelPendingClear() => _clearTimer.Stop();

    /// <summary>Clears the clipboard immediately, but only if it still holds the value we last placed.</summary>
    public bool ClearIfStillOurs()
    {
        if (!_haveToken) return false;
        if (!CurrentContentIsOurs()) return false;

        bool cleared = ClipboardText.Clear(_owner);
        if (cleared)
        {
            _haveToken = false;
            Log.Info("Cleared the clipboard value previously copied by Password helper.");
        }

        return cleared;
    }

    private bool CurrentContentIsOurs()
    {
        Span<byte> token = stackalloc byte[16];
        if (!ClipboardText.TryGetOwnerToken(_owner, token, out int written) || written != _lastToken.Length)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(token, _lastToken);
    }

    private void ScheduleAutoClear()
    {
        _clearTimer.Stop();
        int seconds = _settings.Current.ClearClipboardAfterSeconds;
        if (seconds <= 0) return;

        _clearTimer.Interval = TimeSpan.FromSeconds(seconds);
        _clearTimer.Start();
    }

    public void Dispose()
    {
        _clearTimer.Stop();
        CryptographicOperations.ZeroMemory(_lastToken);
    }
}
