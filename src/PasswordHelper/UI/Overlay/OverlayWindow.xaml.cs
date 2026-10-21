using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using PasswordHelper.Core;
using PasswordHelper.Interop;
using PasswordHelper.UI.Common;

namespace PasswordHelper.UI.Overlay;

/// <summary>
/// The radial overlay window. Covers one monitor with a dimmed, genuinely blurred backdrop and arranges the action
/// buttons in rings around a central hub. Supports mouse and full keyboard interaction, a staggered open/close
/// animation (honouring reduced-motion), and switchable list panels for clipboard history and saved passwords.
/// </summary>
public partial class OverlayWindow : Window
{
    private readonly AppSettings _settings;
    private readonly bool _animate;
    private readonly double _speed;

    private MonitorGeometry _geo;
    private double _scale = 1;
    private Point _center;
    private RadialMenuModel? _model;
    private RadialMetrics _metrics;
    private RadialLayoutResult? _layout;

    private readonly List<Button> _radialButtons = new();
    private Border? _hub;
    private bool _closing;
    private bool _built;

    // View state
    private bool _panelVisible;
    private TextBox? _panelSearch;
    private readonly List<RowVisual> _panelRows = new();
    private int _panelSelection = -1;

    private static readonly ControlTemplate ChromelessButton = BuildChromelessButtonTemplate();

    internal OverlayWindow(AppSettings settings)
    {
        _settings = settings;
        // Controlled by the app's own settings (both default on). The "Reduced motion" toggle is the accessibility
        // opt-out; we do not hard-gate on the Windows animation/transparency switches, because the overlay's blur is
        // a manual screen capture that works regardless of the OS transparency setting.
        _animate = settings.AnimationsEnabled && !settings.ReducedMotion;
        _speed = Math.Clamp(settings.AnimationSpeed, 0.5, 2.0);
        InitializeComponent();

        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        Deactivated += OnDeactivated;
        Scrim.MouseLeftButtonDown += OnScrimClicked;
        PreviewKeyDown += OnPreviewKeyDown;
    }

    /// <summary>Raised when an item is chosen; the controller performs the work and decides whether to close.</summary>
    internal event Action? OpenAnimationCompleted;

    /// <summary>When true, losing activation does not dismiss the overlay (set while a modal child dialog is shown).</summary>
    internal bool SuppressAutoClose { get; set; }

    // ------------------------------------------------------------------ setup / show

    internal void Prepare(RadialMenuModel model, OverlayPlacement placement)
    {
        _model = model;

        Win32.POINT cursor = ScreenUtil.GetCursorPosition();
        _geo = ScreenUtil.GetMonitorFromCursor();
        _scale = _geo.Scale;

        double buttonRadius = _settings.ButtonSize switch
        {
            RadialButtonSize.Small => 30,
            RadialButtonSize.Large => 44,
            _ => 37,
        };
        _metrics = RadialMetrics.ForButtonRadius(buttonRadius);
        _layout = RadialLayout.Arrange(model.Items.Count, _metrics);

        // Allowance so labels under the outermost buttons stay on-screen.
        double bounding = _layout.BoundingRadius + 34;

        double centerDeviceX = placement == OverlayPlacement.ScreenCenter
            ? (_geo.WorkArea.Left + _geo.WorkArea.Right) / 2.0
            : cursor.X;
        double centerDeviceY = placement == OverlayPlacement.ScreenCenter
            ? (_geo.WorkArea.Top + _geo.WorkArea.Bottom) / 2.0
            : cursor.Y;

        // Keep the whole radial on the monitor's work area.
        double marginDev = 12 * _scale;
        double boundingDev = bounding * _scale;
        centerDeviceX = Clamp(centerDeviceX, _geo.WorkArea.Left + boundingDev + marginDev, _geo.WorkArea.Right - boundingDev - marginDev, (_geo.WorkArea.Left + _geo.WorkArea.Right) / 2.0);
        centerDeviceY = Clamp(centerDeviceY, _geo.WorkArea.Top + boundingDev + marginDev, _geo.WorkArea.Bottom - boundingDev - marginDev, (_geo.WorkArea.Top + _geo.WorkArea.Bottom) / 2.0);

        _center = new Point((centerDeviceX - _geo.Bounds.Left) / _scale, (centerDeviceY - _geo.Bounds.Top) / _scale);

        PrepareBackdrop();
    }

    private void PrepareBackdrop()
    {
        bool glass = _settings.GlassEffect;
        BitmapSource? blurred = glass ? ScreenBlur.CaptureAndBlur(_geo, _settings.BlurIntensity) : null;

        if (blurred is not null)
        {
            BackdropImage.Source = blurred;
            Scrim.Fill = new SolidColorBrush(Color.FromArgb(0x5C, 0, 0, 0)); // gentle dim over the blur
            _scrimTargetOpacity = 1.0;
        }
        else
        {
            // Fallback: a solid dark backdrop (no capture / transparency disabled).
            BackdropImage.Source = null;
            Scrim.Fill = new SolidColorBrush(Color.FromArgb(0xEB, 0x0C, 0x0D, 0x10));
            _scrimTargetOpacity = 1.0;
        }
    }

    private double _scrimTargetOpacity = 1.0;

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        // Do not appear in Alt+Tab; place exactly over the target monitor in device pixels.
        Win32.AddExtendedStyle(hwnd, Win32.WS_EX_TOOLWINDOW);
        Win32.SetWindowPos(hwnd, Win32.HWND_TOPMOST,
            _geo.Bounds.Left, _geo.Bounds.Top, _geo.Bounds.Width, _geo.Bounds.Height,
            Win32.SWP_SHOWWINDOW | Win32.SWP_NOACTIVATE);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Activate();
        if (!_built)
        {
            _built = true;
            BuildRadial();
            AnimateOpen();
        }
    }

    // ------------------------------------------------------------------ radial build

    private void BuildRadial()
    {
        RadialCanvas.Children.Clear();
        _radialButtons.Clear();

        _hub = BuildHub();
        RadialCanvas.Children.Add(_hub);
        Canvas.SetLeft(_hub, _center.X - _metrics.CenterRadius);
        Canvas.SetTop(_hub, _center.Y - _metrics.CenterRadius);

        if (_model is null || _layout is null) return;

        for (int i = 0; i < _model.Items.Count; i++)
        {
            RadialItem item = _model.Items[i];
            RadialPlacement placement = _layout.Placements[i];
            Button button = BuildRadialButton(item);
            _radialButtons.Add(button);
            RadialCanvas.Children.Add(button);

            double cx = _center.X + placement.X;
            double cy = _center.Y + placement.Y;
            PositionButton(button, cx, cy);
        }
    }

    private void PositionButton(Button button, double circleCenterX, double circleCenterY)
    {
        button.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double r = _metrics.ButtonRadius;
        double cellWidth = button.DesiredSize.Width;
        Canvas.SetLeft(button, circleCenterX - cellWidth / 2);
        Canvas.SetTop(button, circleCenterY - r);
        // Pivot intro scaling about the circle centre.
        button.RenderTransformOrigin = new Point(0.5, r / Math.Max(button.DesiredSize.Height, 1));
    }

    private Border BuildHub()
    {
        double radius = _metrics.CenterRadius;
        var hub = new Border
        {
            Width = radius * 2,
            Height = radius * 2,
            CornerRadius = new CornerRadius(radius),
            Background = GlassBrush(0x14, 0x17, 0x1B, 0.72),
            BorderBrush = Brush(0xFF, 0xFF, 0xFF, 0.16),
            BorderThickness = new Thickness(1),
            Opacity = _animate ? 0 : 1,
        };
        hub.Effect = SoftShadow();

        // The close "X" sits in the true centre of the hub, with a subtle "Esc" hint just beneath it.
        var layout = new Grid();

        Button close = BuildCloseButton(radius);
        close.HorizontalAlignment = HorizontalAlignment.Center;
        close.VerticalAlignment = VerticalAlignment.Center;
        layout.Children.Add(close);

        var hint = new TextBlock
        {
            Text = "Esc",
            FontSize = Math.Max(9, radius * 0.17),
            Foreground = Brush(0xA2, 0xA7, 0xB0, 0.7),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, radius * 0.16),
        };
        layout.Children.Add(hint);

        hub.Child = layout;
        hub.RenderTransform = new ScaleTransform(_animate ? 0.86 : 1, _animate ? 0.86 : 1);
        hub.RenderTransformOrigin = new Point(0.5, 0.5);
        return hub;
    }

    private Button BuildCloseButton(double hubRadius)
    {
        double size = hubRadius * 0.92;
        var x = new System.Windows.Shapes.Path
        {
            Data = Icons.Get(Icons.Close),
            Stroke = Brush(0xF5, 0xF6, 0xF8, 0.95),
            StrokeThickness = 2.2,
            Stretch = Stretch.Uniform,
            Width = size * 0.5,
            Height = size * 0.5,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
        };

        var ring = new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(size / 2),
            Background = Brushes.Transparent,
            Child = x,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var button = new Button
        {
            Template = ChromelessButton,
            Background = Brushes.Transparent,
            Content = ring,
            Cursor = Cursors.Hand,
            Focusable = true,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Close (Esc)",
        };
        button.Click += (_, _) => BeginClose();
        button.MouseEnter += (_, _) => { ring.Background = Brush(0xFF, 0xFF, 0xFF, 0.08); x.Stroke = Brush(0xFF, 0xFF, 0xFF, 1); };
        button.MouseLeave += (_, _) => { ring.Background = Brushes.Transparent; x.Stroke = Brush(0xF5, 0xF6, 0xF8, 0.95); };
        button.GotKeyboardFocus += (_, _) => ring.Background = Brush(0xFF, 0xFF, 0xFF, 0.10);
        button.LostKeyboardFocus += (_, _) => ring.Background = Brushes.Transparent;
        return button;
    }

    private Button BuildRadialButton(RadialItem item)
    {
        double r = _metrics.ButtonRadius;
        bool primary = item.Primary;

        FrameworkElement glyph = item.Kind == RadialItemKind.SavedEntry && item.Monogram is not null
            ? new TextBlock
            {
                Text = item.Monogram,
                FontSize = r * 0.62,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brush(0xF5, 0xF6, 0xF8, 0.96),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            }
            : new System.Windows.Shapes.Path
            {
                Data = Icons.Get(item.IconPath),
                Stroke = primary ? Brush(0xDC, 0xE4, 0xF0, 1) : Brush(0xE5, 0xE8, 0xEE, 0.92),
                StrokeThickness = 1.9,
                Stretch = Stretch.Uniform,
                Width = r * 0.82,
                Height = r * 0.82,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
            };

        var circle = new Border
        {
            Width = r * 2,
            Height = r * 2,
            CornerRadius = new CornerRadius(r),
            Background = primary ? GlassBrush(0x2A, 0x30, 0x3C, 0.72) : GlassBrush(0x20, 0x23, 0x2A, 0.6),
            BorderBrush = primary ? Brush(0xC7, 0xD2, 0xE3, 0.5) : Brush(0xFF, 0xFF, 0xFF, 0.13),
            BorderThickness = new Thickness(primary ? 1.4 : 1),
            Child = glyph,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new ScaleTransform(1, 1),
        };
        circle.Effect = SoftShadow();

        var label = new TextBlock
        {
            Text = item.Label,
            FontSize = 11.5,
            Foreground = Brush(0xDE, 0xE1, 0xE7, 0.92),
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = r * 2.9,
            Margin = new Thickness(0, 6, 0, 0),
        };

        var cell = new StackPanel { Orientation = Orientation.Vertical, HorizontalAlignment = HorizontalAlignment.Center };
        cell.Children.Add(circle);
        cell.Children.Add(label);

        var button = new Button
        {
            Template = ChromelessButton,
            Background = Brushes.Transparent,
            Content = cell,
            Cursor = Cursors.Hand,
            Focusable = true,
            Tag = item,
            Opacity = _animate ? 0 : 1,
            ToolTip = item.Tooltip,
        };
        button.RenderTransform = new TransformGroup
        {
            Children = { new ScaleTransform(1, 1), new TranslateTransform(0, 0) },
        };
        button.Click += (_, _) => item.Activate();
        button.MouseEnter += (_, _) => HoverButton(circle, label, true, primary);
        button.MouseLeave += (_, _) => HoverButton(circle, label, false, primary);
        button.GotKeyboardFocus += (_, _) => HoverButton(circle, label, true, primary);
        button.LostKeyboardFocus += (_, _) => HoverButton(circle, label, false, primary);
        return button;
    }

    private void HoverButton(Border circle, TextBlock label, bool on, bool primary)
    {
        var scale = (ScaleTransform)circle.RenderTransform;
        double target = on ? 1.075 : 1.0;
        if (_animate)
        {
            var anim = new DoubleAnimation(target, TimeSpan.FromMilliseconds(120)) { EasingFunction = EaseOut() };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
        }
        else
        {
            scale.ScaleX = scale.ScaleY = target;
        }

        circle.BorderBrush = on
            ? Brush(0xC7, 0xD2, 0xE3, primary ? 0.8 : 0.55)
            : (primary ? Brush(0xC7, 0xD2, 0xE3, 0.5) : Brush(0xFF, 0xFF, 0xFF, 0.13));
        if (on) circle.Background = primary ? GlassBrush(0x30, 0x37, 0x45, 0.8) : GlassBrush(0x29, 0x2D, 0x36, 0.72);
        else circle.Background = primary ? GlassBrush(0x2A, 0x30, 0x3C, 0.72) : GlassBrush(0x20, 0x23, 0x2A, 0.6);
        label.Foreground = on ? Brush(0xF5, 0xF6, 0xF8, 1) : Brush(0xDE, 0xE1, 0xE7, 0.92);
    }

    // ------------------------------------------------------------------ open / close animation

    private void AnimateOpen()
    {
        if (!_animate)
        {
            Scrim.Opacity = _scrimTargetOpacity;
            FocusFirst();
            OpenAnimationCompleted?.Invoke();
            return;
        }

        Scrim.BeginAnimation(OpacityProperty, new DoubleAnimation(0, _scrimTargetOpacity, Scaled(150)) { EasingFunction = EaseOut() });

        if (_hub is not null)
        {
            _hub.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, Scaled(170)) { EasingFunction = EaseOut() });
            AnimateScale(_hub, 0.86, 1, Scaled(190), 0);
        }

        double stepMs = 26 / _speed;
        double hubMs = 120 / _speed;
        for (int i = 0; i < _radialButtons.Count; i++)
        {
            Button b = _radialButtons[i];
            double delay = hubMs + i * stepMs;
            var begin = TimeSpan.FromMilliseconds(delay);

            b.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, Scaled(200)) { BeginTime = begin, EasingFunction = EaseOut() });

            var group = (TransformGroup)b.RenderTransform;
            var scale = (ScaleTransform)group.Children[0];
            var translate = (TranslateTransform)group.Children[1];

            // Start slightly pulled toward the centre, then settle outward into place.
            RadialPlacement p = _layout!.Placements[i];
            double mag = 0.16;
            translate.X = -p.X * mag;
            translate.Y = -p.Y * mag;

            var easeBack = new BackEase { Amplitude = 0.18, EasingMode = EasingMode.EaseOut };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.72, 1, Scaled(230)) { BeginTime = begin, EasingFunction = easeBack });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.72, 1, Scaled(230)) { BeginTime = begin, EasingFunction = easeBack });
            translate.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, Scaled(230)) { BeginTime = begin, EasingFunction = EaseOut() });
            translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, Scaled(230)) { BeginTime = begin, EasingFunction = EaseOut() });
        }

        // Fire "completed" after the last button settles so the controller knows interaction is ready.
        var timer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(hubMs + _radialButtons.Count * stepMs + 240),
        };
        timer.Tick += (_, _) => { timer.Stop(); OpenAnimationCompleted?.Invoke(); };
        timer.Start();

        FocusFirst();
    }

    /// <summary>Animates the overlay out and then closes it. Safe to call repeatedly; dismissal is never blocked.</summary>
    internal void BeginClose()
    {
        if (_closing) return;
        _closing = true;

        if (!_animate)
        {
            Close();
            return;
        }

        var fade = new DoubleAnimation(0, Scaled(130)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
        fade.Completed += (_, _) => Close();

        Scrim.BeginAnimation(OpacityProperty, new DoubleAnimation(Scrim.Opacity, 0, Scaled(130)));
        if (_hub is not null)
        {
            AnimateScale(_hub, 1, 0.9, Scaled(120), 0);
        }

        // Buttons collapse inward quickly and in reverse, then the whole window fades out.
        for (int i = 0; i < _radialButtons.Count; i++)
        {
            Button b = _radialButtons[i];
            double delay = (_radialButtons.Count - 1 - i) * (10 / _speed);
            var begin = TimeSpan.FromMilliseconds(delay);
            b.BeginAnimation(OpacityProperty, new DoubleAnimation(b.Opacity, 0, Scaled(110)) { BeginTime = begin });
        }

        if (PanelHost.Visibility == Visibility.Visible)
        {
            PanelHost.BeginAnimation(OpacityProperty, new DoubleAnimation(PanelHost.Opacity, 0, Scaled(120)));
        }

        // Fade the entire window as a safety net and to drive Close().
        BeginAnimation(OpacityProperty, fade);
    }

    private void AnimateScale(UIElement element, double from, double to, Duration duration, double beginMs)
    {
        if (element.RenderTransform is not ScaleTransform scale)
        {
            scale = new ScaleTransform(from, from);
            element.RenderTransform = scale;
            element.RenderTransformOrigin = new Point(0.5, 0.5);
        }

        var anim = new DoubleAnimation(from, to, duration) { BeginTime = TimeSpan.FromMilliseconds(beginMs), EasingFunction = EaseOut() };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
    }

    // ------------------------------------------------------------------ keyboard & mouse

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (_panelVisible) { ReturnToRadial(); e.Handled = true; }
            else { BeginClose(); e.Handled = true; }
            return;
        }

        if (_panelVisible)
        {
            HandlePanelKey(e);
            return;
        }

        switch (e.Key)
        {
            case Key.Left:
            case Key.Up:
                MoveRadialFocus(-1);
                e.Handled = true;
                break;
            case Key.Right:
            case Key.Down:
                MoveRadialFocus(1);
                e.Handled = true;
                break;
            case Key.Enter:
            case Key.Space:
                Button? focused = _radialButtons.FirstOrDefault(b => b.IsKeyboardFocused);
                if (focused is not null)
                {
                    focused.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                    e.Handled = true;
                }

                break;
        }
    }

    private void MoveRadialFocus(int direction)
    {
        if (_radialButtons.Count == 0) return;
        int current = _radialButtons.FindIndex(b => b.IsKeyboardFocused);
        int next = current < 0 ? (direction > 0 ? 0 : _radialButtons.Count - 1)
                               : (current + direction + _radialButtons.Count) % _radialButtons.Count;
        _radialButtons[next].Focus();
    }

    private void FocusFirst()
    {
        if (_radialButtons.Count > 0)
        {
            _radialButtons[0].Focus();
        }
        else
        {
            Focus();
        }
    }

    private void OnScrimClicked(object sender, MouseButtonEventArgs e)
    {
        if (_settings.CloseOnClickOutside)
        {
            BeginClose();
        }
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        // Focus moved to another window (clicked a different app/monitor, Alt+Tab): dismiss if configured to.
        if (!_closing && !SuppressAutoClose && _settings.CloseOnClickOutside)
        {
            BeginClose();
        }
    }

    // ------------------------------------------------------------------ helpers

    private Duration Scaled(double ms) => new(TimeSpan.FromMilliseconds(ms / _speed));

    private static IEasingFunction EaseOut() => new CubicEase { EasingMode = EasingMode.EaseOut };

    private static double Clamp(double value, double min, double max, double fallback)
    {
        if (min > max) return fallback; // radial is larger than the monitor; just centre it
        return Math.Clamp(value, min, max);
    }

    private static SolidColorBrush Brush(byte r, byte g, byte b, double opacity)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b)) { Opacity = opacity };
        brush.Freeze();
        return brush;
    }

    private SolidColorBrush GlassBrush(byte r, byte g, byte b, double alpha)
    {
        // Scale translucency by the user's glass-opacity preference (higher = more solid, less see-through).
        double scaled = Math.Clamp(alpha * (0.6 + 0.4 * _settings.GlassOpacity + (BackdropImage.Source is null ? 0.25 : 0)), 0, 1);
        var brush = new SolidColorBrush(Color.FromArgb((byte)(scaled * 255), r, g, b));
        brush.Freeze();
        return brush;
    }

    private static System.Windows.Media.Effects.DropShadowEffect SoftShadow() => new()
    {
        BlurRadius = 18,
        ShadowDepth = 2,
        Direction = 270,
        Opacity = 0.35,
        Color = Colors.Black,
    };

    private static ControlTemplate BuildChromelessButtonTemplate()
    {
        var factory = new FrameworkElementFactory(typeof(ContentPresenter));
        factory.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        factory.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        var template = new ControlTemplate(typeof(Button)) { VisualTree = factory };
        template.Seal();
        return template;
    }
}
