using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using PasswordHelper.Core;

namespace PasswordHelper.UI.Common;

/// <summary>
/// Base class for the app's dialog windows: borderless, rounded graphite surface with a draggable title bar and a
/// close button, consistent dark theming, and Esc-to-close. Derived windows just set <see cref="Window.Title"/>
/// and <see cref="Window.Content"/>.
/// </summary>
public abstract class ChromeWindow : Window
{
    protected ChromeWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
        FontFamily = (FontFamily)Application.Current.FindResource("AppFontFamily");

        Template = (ControlTemplate)Application.Current.FindResource("ChromeWindowTemplate");

        SourceInitialized += OnSourceInitialized;
        PreviewKeyDown += OnPreviewKeyDown;
    }

    /// <summary>When true (default) pressing Esc closes the window.</summary>
    public bool CloseOnEscape { get; set; } = true;

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        if (GetTemplateChild("PART_TitleBar") is UIElement titleBar)
        {
            titleBar.MouseLeftButtonDown += (_, e) =>
            {
                if (e.ButtonState == MouseButtonState.Pressed)
                {
                    try { DragMove(); } catch (InvalidOperationException) { /* released mid-drag */ }
                }
            };
        }

        if (GetTemplateChild("PART_Close") is Button close)
        {
            close.Click += (_, _) => OnCloseButton();
        }
    }

    protected virtual void OnCloseButton() => Close();

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        WindowEffects.UseDarkTitleBar(handle);
        WindowEffects.UseRoundedCorners(handle);
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (CloseOnEscape && e.Key == Key.Escape && !e.Handled)
        {
            Close();
        }
    }

    /// <summary>Centers the window over its owner, or on the owner's monitor when there is no owner.</summary>
    public void CenterOnOwnerOrCursor()
    {
        if (Owner is not null)
        {
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            return;
        }

        WindowStartupLocation = WindowStartupLocation.Manual;
        MonitorGeometry geo = ScreenUtil.GetMonitorFromCursor();
        double scale = geo.Scale;
        double workLeft = geo.WorkArea.Left / scale;
        double workTop = geo.WorkArea.Top / scale;
        double workWidth = geo.WorkArea.Width / scale;
        double workHeight = geo.WorkArea.Height / scale;

        Loaded += (_, _) =>
        {
            Left = workLeft + (workWidth - ActualWidth) / 2;
            Top = workTop + (workHeight - ActualHeight) / 2;
        };
    }
}
