using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using PasswordHelper.Core;
using PasswordHelper.Interop;
using PasswordHelper.UI.Common;
using PasswordHelper.Vault;

namespace PasswordHelper.UI.Tray;

/// <summary>
/// The system-tray presence: a notify icon plus a themed right-click menu. Left-click opens the overlay; right-click
/// opens the menu. Survives Explorer restarts by re-adding the icon when it receives the TaskbarCreated broadcast.
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private const uint IconId = 1;

    private readonly MessageWindow _window;
    private Win32.NOTIFYICONDATA _data;
    private IntPtr _hIcon;
    private ContextMenu? _menu;
    private MenuItem? _lockItem;
    private VaultState _vaultState = VaultState.Loading;
    private bool _added;
    private bool _disposed;

    public TrayIcon(MessageWindow window)
    {
        _window = window;
        _window.TrayCallback = OnTrayCallback;
        _window.ExplorerRestarted += ReAdd;

        LoadIcon();
        BuildMenu();
        AddIcon();
    }

    public event Action? ShowRequested;
    public event Action? SettingsRequested;
    public event Action? ExitRequested;
    public event Action? LockRequested;
    public event Action? UnlockRequested;

    private void LoadIcon()
    {
        try
        {
            Win32.ExtractIconEx(AppPaths.ExecutablePath, 0, out IntPtr large, out IntPtr small, 1);
            _hIcon = small != IntPtr.Zero ? small : large;
            if (small != IntPtr.Zero && large != IntPtr.Zero && small != large)
            {
                Win32.DestroyIcon(large);
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Could not load the tray icon from the executable", ex);
        }
    }

    private void AddIcon()
    {
        _data = new Win32.NOTIFYICONDATA
        {
            cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Win32.NOTIFYICONDATA>(),
            hWnd = _window.Handle,
            uID = IconId,
            uFlags = Win32.NIF_MESSAGE | Win32.NIF_ICON | Win32.NIF_TIP | Win32.NIF_SHOWTIP,
            uCallbackMessage = _window.TrayCallbackMessage,
            hIcon = _hIcon,
            szTip = "Password helper",
            szInfo = string.Empty,
            szInfoTitle = string.Empty,
            uVersion = Win32.NOTIFYICON_VERSION_4,
        };

        if (!Win32.Shell_NotifyIcon(Win32.NIM_ADD, ref _data))
        {
            Log.Error("Could not add the system tray icon.");
            return;
        }

        _added = true;
        Win32.Shell_NotifyIcon(Win32.NIM_SETVERSION, ref _data);
    }

    private void ReAdd()
    {
        if (_disposed) return;
        Log.Info("Explorer restarted; re-adding the tray icon.");
        _added = false;
        AddIcon();
    }

    /// <summary>Shows a system balloon notification (used for issues the user must know about, e.g. a hotkey conflict).</summary>
    public void ShowBalloon(string title, string message, bool warning = false)
    {
        if (!_added) return;
        Win32.NOTIFYICONDATA data = _data;
        data.uFlags = Win32.NIF_INFO;
        data.szInfoTitle = title;
        data.szInfo = message;
        data.dwInfoFlags = warning ? Win32.NIIF_WARNING : Win32.NIIF_INFO;
        Win32.Shell_NotifyIcon(Win32.NIM_MODIFY, ref data);
    }

    public void UpdateVaultState(VaultState state)
    {
        _vaultState = state;
        if (_lockItem is null) return;

        switch (state)
        {
            case VaultState.Locked:
                _lockItem.Header = "Unlock Vault";
                _lockItem.IsEnabled = true;
                _lockItem.Visibility = Visibility.Visible;
                _lockItem.Icon = MenuGlyph(Icons.Unlock);
                break;
            case VaultState.Ready:
                _lockItem.Header = "Lock Vault";
                _lockItem.IsEnabled = true;
                _lockItem.Visibility = Visibility.Visible;
                _lockItem.Icon = MenuGlyph(Icons.Lock);
                break;
            default:
                _lockItem.Visibility = Visibility.Collapsed;
                break;
        }
    }

    private bool OnTrayCallback(uint msg, IntPtr wParam, IntPtr lParam)
    {
        uint evt = (uint)Win32.LoWord(lParam);
        switch (evt)
        {
            case Win32.NIN_SELECT:
            case Win32.NIN_KEYSELECT:
            case Win32.WM_LBUTTONUP:
                ShowRequested?.Invoke();
                return true;
            case Win32.WM_CONTEXTMENU:
            case Win32.WM_RBUTTONUP:
                ShowMenu();
                return true;
            default:
                return false;
        }
    }

    private void ShowMenu()
    {
        if (_menu is null) return;

        UpdateVaultState(_vaultState);

        // Foreground ourselves first so the menu dismisses correctly when the user clicks elsewhere.
        Win32.SetForegroundWindow(_window.Handle);

        Win32.POINT cursor = ScreenUtil.GetCursorPosition();
        MonitorGeometry geo = ScreenUtil.GetMonitorFromCursor();
        _menu.Placement = System.Windows.Controls.Primitives.PlacementMode.AbsolutePoint;
        _menu.HorizontalOffset = cursor.X / geo.Scale;
        _menu.VerticalOffset = cursor.Y / geo.Scale;
        _menu.IsOpen = true;
    }

    private void BuildMenu()
    {
        _menu = new ContextMenu();

        var header = new MenuItem
        {
            Header = "Password helper",
            Style = (Style)Application.Current.FindResource("MenuHeaderItem"),
        };
        _menu.Items.Add(header);
        _menu.Items.Add(new Separator());

        _menu.Items.Add(MakeItem("Show Password helper", Icons.Key, () => ShowRequested?.Invoke()));
        _menu.Items.Add(MakeItem("Settings", Icons.Settings, () => SettingsRequested?.Invoke()));

        _lockItem = MakeItem("Lock Vault", Icons.Lock, OnLockItem);
        _menu.Items.Add(_lockItem);

        _menu.Items.Add(new Separator());
        _menu.Items.Add(MakeItem("Exit", Icons.Close, () => ExitRequested?.Invoke()));
    }

    private void OnLockItem()
    {
        if (_vaultState == VaultState.Locked) UnlockRequested?.Invoke();
        else LockRequested?.Invoke();
    }

    private MenuItem MakeItem(string header, string iconPath, Action onClick)
    {
        var item = new MenuItem { Header = header, Icon = MenuGlyph(iconPath) };
        item.Click += (_, _) => onClick();
        return item;
    }

    private static FrameworkElement MenuGlyph(string iconPath)
    {
        var brush = new SolidColorBrush(Color.FromRgb(0xA2, 0xA7, 0xB0));
        brush.Freeze();
        return new Path
        {
            Data = Icons.Get(iconPath),
            Stroke = brush,
            StrokeThickness = 1.6,
            Stretch = Stretch.Uniform,
            Width = 15,
            Height = 15,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
        };
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_menu is not null) _menu.IsOpen = false;

        if (_added)
        {
            Win32.Shell_NotifyIcon(Win32.NIM_DELETE, ref _data);
            _added = false;
        }

        if (_hIcon != IntPtr.Zero)
        {
            Win32.DestroyIcon(_hIcon);
            _hIcon = IntPtr.Zero;
        }

        _window.TrayCallback = null;
    }
}
