#if DEBUG
using System;
using System.Windows;
using System.Windows.Controls;

namespace PasswordHelper.Core;

/// <summary>DEBUG-only: realizes each themed control in a hidden window to find which one fails to apply its style.</summary>
internal static class DevThemeProbe
{
    public static void Run()
    {
        Probe("TextBlock", () => new TextBlock { Text = "x" });
        Probe("Button", () => new Button { Content = "x" });
        Probe("Button+typeofStyle", () => new Button { Content = "x", Style = (Style)Application.Current.FindResource(typeof(Button)) });
        Probe("PrimaryButton", () => new Button { Content = "x", Style = (Style)Application.Current.FindResource("PrimaryButton") });
        Probe("GhostButton", () => new Button { Content = "x", Style = (Style)Application.Current.FindResource("GhostButton") });
        Probe("DangerButton", () => new Button { Content = "x", Style = (Style)Application.Current.FindResource("DangerButton") });
        Probe("IconButton", () => new Button { Content = "x", Style = (Style)Application.Current.FindResource("IconButton") });
        Probe("TextBox", () => new TextBox { Text = "x" });
        Probe("PasswordBox", () => new PasswordBox());
        Probe("CheckBox", () => new CheckBox { IsChecked = true });
        Probe("ComboBox", () =>
        {
            var c = new ComboBox();
            c.Items.Add("one");
            c.Items.Add("two");
            c.SelectedIndex = 0;
            return c;
        });
        Probe("Slider", () => new Slider { Minimum = 0, Maximum = 10, Value = 5 });
        Probe("TabControl", () =>
        {
            var t = new TabControl();
            t.Items.Add(new TabItem { Header = "h", Content = new TextBlock { Text = "c" } });
            return t;
        });
        Probe("SectionLabel", () => new TextBlock { Text = "x", Style = (Style)Application.Current.FindResource("SectionLabel") });
        Probe("CaptionText", () => new TextBlock { Text = "x", Style = (Style)Application.Current.FindResource("CaptionText") });
        Probe("ScrollViewerWithStack", () =>
        {
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = "x" });
            sp.Children.Add(new CheckBox { IsChecked = true });
            return new ScrollViewer { Content = sp };
        });
    }

    private static void Probe(string name, Func<UIElement> make)
    {
        Window? window = null;
        try
        {
            UIElement element = make();
            window = new Window
            {
                Width = 120,
                Height = 90,
                WindowStyle = WindowStyle.None,
                ShowInTaskbar = false,
                ShowActivated = false,
                Left = -4000,
                Top = -4000,
                Content = element,
            };
            window.Show();
            window.UpdateLayout();
            Log.Info($"THEME-PROBE ok: {name}");
        }
        catch (Exception ex)
        {
            Log.Error($"THEME-PROBE FAIL: {name} -> {ex.GetType().Name}: {ex.Message}", ex, includeExceptionMessage: true);
        }
        finally
        {
            try { window?.Close(); } catch { /* ignore */ }
        }
    }
}
#endif
