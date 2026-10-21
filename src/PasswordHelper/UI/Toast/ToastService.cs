using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using PasswordHelper.Core;
using PasswordHelper.Interop;
using PasswordHelper.UI.Common;

namespace PasswordHelper.UI.Toast;

internal enum ToastKind
{
    Success,
    Info,
    Warning,
}

/// <summary>
/// Shows brief, non-intrusive confirmations ("Password copied", "Copied — press Ctrl+V to paste"). The toast window
/// never activates or accepts input, so it cannot steal focus from the application the user is pasting into.
/// Only one toast is visible at a time.
/// </summary>
internal sealed class ToastService : IDisposable
{
    private readonly AppSettings _settings;
    private ToastWindow? _current;

    public ToastService(AppSettings settings)
    {
        _settings = settings;
    }

    public void Show(string message, ToastKind kind = ToastKind.Success, int durationMs = 2100)
    {
        DismissCurrent();
        try
        {
            var toast = new ToastWindow(message, kind, _settings.ReducedMotion || !_settings.AnimationsEnabled);
            _current = toast;
            toast.Closed += (_, _) => { if (ReferenceEquals(_current, toast)) _current = null; };
            toast.ShowFor(durationMs);
        }
        catch (Exception ex)
        {
            Log.Warn("Could not display a toast", ex);
        }
    }

    private void DismissCurrent()
    {
        try { _current?.CloseNow(); } catch { /* ignore */ }
        _current = null;
    }

    public void Dispose() => DismissCurrent();
}

/// <summary>The toast popup. Code-only; borderless, click-through, non-activating.</summary>
internal sealed class ToastWindow : Window
{
    private readonly bool _reducedMotion;
    private readonly DispatcherTimer _timer = new();
    private bool _closing;

    public ToastWindow(string message, ToastKind kind, bool reducedMotion)
    {
        _reducedMotion = reducedMotion;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        IsHitTestVisible = false;
        Focusable = false;
        FontFamily = (FontFamily)Application.Current.FindResource("AppFontFamily");

        Content = BuildContent(message, kind);
        SourceInitialized += OnSourceInitialized;
    }

    private UIElement BuildContent(string message, ToastKind kind)
    {
        var pill = new Border
        {
            CornerRadius = new CornerRadius(12),
            Background = new SolidColorBrush(Color.FromArgb(0xF2, 0x1C, 0x1F, 0x25)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(15, 11, 17, 11),
            Margin = new Thickness(16),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 24,
                ShadowDepth = 4,
                Direction = 270,
                Opacity = 0.5,
                Color = Colors.Black,
            },
        };

        var stack = new StackPanel { Orientation = Orientation.Horizontal };

        (string path, Color color) = kind switch
        {
            ToastKind.Success => (Icons.Check, Color.FromRgb(0x8F, 0xD3, 0x9A)),
            ToastKind.Warning => (Icons.Shield, Color.FromRgb(0xE3, 0xC3, 0x6B)),
            _ => (Icons.Clipboard, Color.FromRgb(0xC7, 0xD2, 0xE3)),
        };

        var iconBrush = new SolidColorBrush(color);
        iconBrush.Freeze();
        stack.Children.Add(new Path
        {
            Data = Icons.Get(path),
            Stroke = iconBrush,
            StrokeThickness = 2,
            Stretch = Stretch.Uniform,
            Width = 17,
            Height = 17,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0),
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
        });

        stack.Children.Add(new TextBlock
        {
            Text = message,
            Foreground = new SolidColorBrush(Color.FromRgb(0xF5, 0xF6, 0xF8)),
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
        });

        pill.Child = stack;
        return pill;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        // Never activate, never take input, never appear in Alt+Tab.
        Win32.AddExtendedStyle(hwnd, Win32.WS_EX_NOACTIVATE | Win32.WS_EX_TRANSPARENT | Win32.WS_EX_TOOLWINDOW);
    }

    public void ShowFor(int durationMs)
    {
        Show();
        Dispatcher.BeginInvoke(new Action(PositionAndReveal), DispatcherPriority.Loaded);

        _timer.Interval = TimeSpan.FromMilliseconds(durationMs);
        _timer.Tick += (_, _) => { _timer.Stop(); FadeOutAndClose(); };
        _timer.Start();
    }

    private void PositionAndReveal()
    {
        MonitorGeometry geo = ScreenUtil.GetMonitorFromCursor();
        double scale = geo.Scale;
        int w = (int)Math.Ceiling(ActualWidth * scale);
        int h = (int)Math.Ceiling(ActualHeight * scale);
        int x = (geo.WorkArea.Left + geo.WorkArea.Right) / 2 - w / 2;
        int y = geo.WorkArea.Bottom - h - (int)(70 * scale);

        var hwnd = new WindowInteropHelper(this).Handle;
        Win32.SetWindowPos(hwnd, Win32.HWND_TOPMOST, x, y, w, h,
            Win32.SWP_NOACTIVATE | Win32.SWP_SHOWWINDOW);

        if (_reducedMotion)
        {
            Opacity = 1;
            return;
        }

        Opacity = 0;
        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        var slide = new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        var transform = new TranslateTransform();
        if (Content is UIElement el)
        {
            el.RenderTransform = transform;
            transform.BeginAnimation(TranslateTransform.YProperty, slide);
        }

        BeginAnimation(OpacityProperty, fade);
    }

    private void FadeOutAndClose()
    {
        if (_closing) return;
        _closing = true;

        if (_reducedMotion)
        {
            Close();
            return;
        }

        var fade = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(180)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
        fade.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, fade);
    }

    public void CloseNow()
    {
        _timer.Stop();
        _closing = true;
        Close();
    }
}
