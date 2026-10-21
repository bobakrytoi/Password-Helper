using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using PasswordHelper.ClipboardServices;
using PasswordHelper.Core;
using PasswordHelper.Input;
using PasswordHelper.Insertion;
using PasswordHelper.Interop;
using PasswordHelper.UI.Common;
using PasswordHelper.UI.Dialogs;
using PasswordHelper.UI.Overlay;
using PasswordHelper.UI.Settings;
using PasswordHelper.UI.Toast;
using PasswordHelper.UI.Tray;
using PasswordHelper.Vault;

namespace PasswordHelper;

/// <summary>
/// Owns every long-lived component and wires them together. There is no main window: the application lives in the
/// tray and shows the radial overlay on demand. Created and used only on the WPF UI thread.
/// </summary>
internal sealed class AppHost : IDisposable
{
    private readonly App _app;
    private readonly CommandLineArgs _args;

    private SettingsService _settings = null!;
    private MessageWindow _messageWindow = null!;
    private VaultService _vault = null!;
    private SoundService _sound = null!;
    private ToastService _toast = null!;
    private ClipboardManager _clipboard = null!;
    private ClipboardHistoryService _history = null!;
    private InsertionService _insertion = null!;
    private OverlayController _overlay = null!;
    private TrayIcon _tray = null!;
    private HotkeyManager _hotkeys = null!;

    private SettingsWindow? _settingsWindow;
    private DispatcherTimer? _autoLockTimer;
    private DateTime _lastVaultUse = DateTime.UtcNow;
    private bool _sessionNotificationsRegistered;
    private bool _disposed;

    public AppHost(App app, CommandLineArgs args)
    {
        _app = app;
        _args = args;
    }

    public void Start()
    {
        bool firstRun = !File.Exists(AppPaths.SettingsFile);

        _settings = SettingsService.Load(AppPaths.SettingsFile);
        _messageWindow = new MessageWindow();
        _sound = new SoundService(_settings);
        _sound.InstallGlobalHandler();

        _toast = new ToastService(_settings.Current);
        _vault = new VaultService(AppPaths.VaultFile);
        _clipboard = new ClipboardManager(_messageWindow.Handle, _settings);
        _history = new ClipboardHistoryService(_messageWindow, _settings);
        _insertion = new InsertionService();
        _overlay = new OverlayController(_settings, _vault, _clipboard, _history, _insertion, _toast);

        _tray = new TrayIcon(_messageWindow);
        _tray.ShowRequested += () => _overlay.Toggle();
        _tray.SettingsRequested += ShowSettings;
        _tray.ExitRequested += () => _app.Shutdown();
        _tray.LockRequested += LockVault;
        _tray.UnlockRequested += UnlockVault;

        _hotkeys = new HotkeyManager(_messageWindow);
        _hotkeys.Activated += () => _overlay.Toggle();

        _messageWindow.SystemResumed += OnSystemResumed;
        _messageWindow.SessionLocked += OnSessionLocked;

        _vault.Changed += OnVaultChanged;
        _vault.Used += (_, _) => _lastVaultUse = DateTime.UtcNow;
        _settings.Changed += OnSettingsChanged;

        if (Win32.WTSRegisterSessionNotification(_messageWindow.Handle, Win32.NOTIFY_FOR_THIS_SESSION))
        {
            _sessionNotificationsRegistered = true;
        }
        else
        {
            Log.Warn("Could not register for session lock/unlock notifications.");
        }

        _history.Start();
        RegisterConfiguredHotkey();
        SetupAutoLockTimer();

        _ = LoadVaultAsync();

        if (firstRun)
        {
            HotkeyGesture current = _hotkeys.Current ?? HotkeyGesture.Default;
            _tray.ShowBalloon("Password helper is running",
                $"Press {current.ToDisplayString()} anytime to open the password menu. Right-click the tray icon for settings.");
        }

        Log.Info("Startup complete; running in the system tray.");
    }

    private async System.Threading.Tasks.Task LoadVaultAsync()
    {
        await _vault.LoadAsync();
        if (_vault.State == VaultState.Unavailable && _vault.Problem is not null)
        {
            OfferVaultRecovery();
        }
    }

    private void RegisterConfiguredHotkey()
    {
        HotkeyGesture gesture = HotkeyGesture.TryParse(_settings.Current.Hotkey, out HotkeyGesture parsed) ? parsed : HotkeyGesture.Default;
        HotkeyRegistrationResult result = _hotkeys.Register(gesture);

        if (!result.Success && gesture != HotkeyGesture.Default)
        {
            Log.Warn($"Configured hotkey '{gesture}' could not be registered: {result.Message}. Trying the default.");
            result = _hotkeys.Register(HotkeyGesture.Default);
            if (result.Success)
            {
                _settings.Update(s => s.Hotkey = HotkeyGesture.Default.ToString());
            }
        }

        if (!result.Success)
        {
            Log.Error($"No activation hotkey is registered: {result.Message}");
            _tray.ShowBalloon("Shortcut unavailable",
                $"{result.Message} Open Settings to choose another shortcut.", warning: true);
        }
    }

    // ------------------------------------------------------------------ settings window

    private void ShowSettings()
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }

        var host = new SettingsHost
        {
            TryApplyHotkey = TryChangeHotkey,
            GetCurrentHotkey = () => _hotkeys.Current ?? HotkeyGesture.Default,
            GetVaultState = () => _vault.State,
            LockVault = LockVault,
            UnlockVault = UnlockVault,
            ClearClipboardHistory = ClearClipboardHistory,
            DeleteVaultData = DeleteVaultData,
        };

        _settingsWindow = new SettingsWindow(_settings, host);
        _settingsWindow.CenterOnOwnerOrCursor();
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private string? TryChangeHotkey(HotkeyGesture gesture)
    {
        HotkeyRegistrationResult result = _hotkeys.Register(gesture);
        if (!result.Success)
        {
            return result.Message;
        }

        _settings.Update(s => s.Hotkey = gesture.ToString());
        return null;
    }

    // ------------------------------------------------------------------ vault

    private void LockVault()
    {
        _vault.Lock();
        _toast.Show("Vault locked");
    }

    private async void UnlockVault()
    {
        await _vault.UnlockAsync();
        if (_vault.State == VaultState.Ready)
        {
            _lastVaultUse = DateTime.UtcNow;
            _toast.Show("Vault unlocked");
        }
        else if (_vault.State == VaultState.Unavailable)
        {
            OfferVaultRecovery();
        }
    }

    private void OfferVaultRecovery()
    {
        Window? owner = _settingsWindow;
        VaultRecoveryChoice choice = VaultRecoveryDialog.Show(owner, _vault.Problem ?? "The vault could not be opened.", _vault.BackupAvailable);
        _ = ApplyRecoveryAsync(choice);
    }

    private async System.Threading.Tasks.Task ApplyRecoveryAsync(VaultRecoveryChoice choice)
    {
        string? error = choice switch
        {
            VaultRecoveryChoice.RestoreBackup => await _vault.RestoreBackupAsync(),
            VaultRecoveryChoice.StartNew => await _vault.StartNewVaultAsync(),
            _ => null,
        };

        if (error is not null)
        {
            _toast.Show(error, ToastKind.Warning);
        }
        else if (choice == VaultRecoveryChoice.RestoreBackup && _vault.State == VaultState.Ready)
        {
            _toast.Show("Vault restored from backup");
        }
    }

    private void ClearClipboardHistory()
    {
        _history.Clear();
        _clipboard.ClearIfStillOurs();
        _toast.Show("Clipboard history cleared");
    }

    private async void DeleteVaultData()
    {
        bool confirmed = ConfirmDialog.Show(_settingsWindow, "Delete all saved passwords?",
            "This permanently deletes the vault file and its backup. Saved passwords cannot be recovered afterwards.",
            "Delete everything", "Cancel", danger: true);
        if (!confirmed) return;

        string? error = await _vault.DeleteAllAsync();
        _toast.Show(error ?? "All saved passwords deleted", error is null ? ToastKind.Success : ToastKind.Warning);
    }

    // ------------------------------------------------------------------ events

    private void OnVaultChanged(object? sender, EventArgs e) => _tray.UpdateVaultState(_vault.State);

    private void OnSettingsChanged(object? sender, AppSettings settings)
    {
        _history.Trim();
        SetupAutoLockTimer();
    }

    private void OnSystemResumed() => _hotkeys.Reapply();

    private void OnSessionLocked()
    {
        if (_settings.Current.LockVaultOnWindowsLock)
        {
            _vault.Lock();
        }
    }

    private void SetupAutoLockTimer()
    {
        int minutes = _settings.Current.AutoLockMinutes;
        if (minutes <= 0)
        {
            _autoLockTimer?.Stop();
            return;
        }

        _autoLockTimer ??= CreateAutoLockTimer();
        _autoLockTimer.Start();
    }

    private DispatcherTimer CreateAutoLockTimer()
    {
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(30) };
        timer.Tick += (_, _) =>
        {
            int minutes = _settings.Current.AutoLockMinutes;
            if (minutes > 0 && _vault.State == VaultState.Ready && !_overlay.IsOpen &&
                DateTime.UtcNow - _lastVaultUse >= TimeSpan.FromMinutes(minutes))
            {
                _vault.Lock();
                Log.Info("Vault auto-locked after inactivity.");
            }
        };
        return timer;
    }

    public void HandleInstanceCommand(InstanceCommand command)
    {
        switch (command)
        {
            case InstanceCommand.Show:
                _overlay.Toggle();
                break;
            case InstanceCommand.Settings:
                ShowSettings();
                break;
            case InstanceCommand.Exit:
                _app.Shutdown();
                break;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try { _settings?.Flush(); } catch (Exception ex) { Log.Warn("Flushing settings failed", ex); }

        _autoLockTimer?.Stop();
        _overlay?.CloseIfOpen();

        if (_sessionNotificationsRegistered && _messageWindow is not null)
        {
            Win32.WTSUnRegisterSessionNotification(_messageWindow.Handle);
        }

        _hotkeys?.Dispose();
        _history?.Dispose();
        _clipboard?.Dispose();
        _tray?.Dispose();
        _toast?.Dispose();
        _sound?.Dispose();
        _vault?.Dispose();
        _messageWindow?.Dispose();
    }
}
