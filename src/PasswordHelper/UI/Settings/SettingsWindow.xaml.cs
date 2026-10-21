using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PasswordHelper.Core;
using PasswordHelper.Input;
using PasswordHelper.Passwords;
using PasswordHelper.UI.Common;
using PasswordHelper.Vault;

namespace PasswordHelper.UI.Settings;

/// <summary>Callbacks the settings window uses to act on the rest of the app (hotkey, vault, clipboard).</summary>
internal sealed class SettingsHost
{
    public required Func<HotkeyGesture, string?> TryApplyHotkey { get; init; }
    public required Func<HotkeyGesture> GetCurrentHotkey { get; init; }
    public required Func<VaultState> GetVaultState { get; init; }
    public required Action LockVault { get; init; }
    public required Action UnlockVault { get; init; }
    public required Action ClearClipboardHistory { get; init; }
    public required Action DeleteVaultData { get; init; }
}

/// <summary>The compact, tabbed settings window — the only conventional window in the app. Changes persist immediately.</summary>
internal sealed class SettingsWindow : ChromeWindow
{
    private readonly SettingsService _settingsService;
    private readonly SettingsHost _host;
    private TextBox? _sampleBox;

    private AppSettings S => _settingsService.Current;

    public SettingsWindow(SettingsService settings, SettingsHost host)
    {
        _settingsService = settings;
        _host = host;

        Title = "Password helper — Settings";
        Width = 760;
        Height = 580;
        MinWidth = 680;
        ResizeMode = ResizeMode.NoResize;

        var tabs = new TabControl();
        AddTab(tabs, "General", BuildGeneral);
        AddTab(tabs, "Shortcut", BuildHotkey);
        AddTab(tabs, "Passwords", BuildGeneration);
        AddTab(tabs, "Clipboard", BuildClipboard);
        AddTab(tabs, "Security", BuildSecurity);
        AddTab(tabs, "Appearance", BuildAppearance);
        AddTab(tabs, "About", BuildAbout);
        Content = tabs;
    }

    private void AddTab(TabControl tabs, string header, Func<UIElement> build)
    {
        try
        {
            tabs.Items.Add(new TabItem { Header = header, Content = Scroll(build()) });
        }
        catch (Exception ex)
        {
            Core.Log.Error($"Settings tab '{header}' failed to build", ex);
            tabs.Items.Add(new TabItem
            {
                Header = header,
                Content = new TextBlock
                {
                    Text = "This section could not be shown.",
                    Margin = new Thickness(30),
                    Foreground = Res("SecondaryTextBrush"),
                },
            });
        }
    }

    // ------------------------------------------------------------------ tabs

    private UIElement BuildGeneral()
    {
        var p = NewPage();
        p.Children.Add(Section("General"));
        p.Children.Add(Toggle("Launch at Windows startup", "Start Password helper automatically when you sign in.",
            StartupRegistration.IsEnabled(), v =>
            {
                if (!StartupRegistration.SetEnabled(v))
                {
                    MessageBox.Show(this, "The startup setting could not be changed.", "Password helper");
                }
            }));
        p.Children.Add(Toggle("Start minimized to tray", "Do not show anything on launch except the tray icon.",
            S.StartMinimized, v => Update(s => s.StartMinimized = v)));
        p.Children.Add(Toggle("Insert into the active app", "After copying, paste into the field you were using.",
            S.InsertIntoActiveApp, v => Update(s => s.InsertIntoActiveApp = v)));
        p.Children.Add(Toggle("Close overlay after choosing an item", "Dismiss the overlay once an action runs.",
            S.CloseAfterSelection, v => Update(s => s.CloseAfterSelection = v)));
        p.Children.Add(Toggle("Close overlay when clicking outside", "Dismiss the overlay if you click away from it.",
            S.CloseOnClickOutside, v => Update(s => s.CloseOnClickOutside = v)));
        p.Children.Add(Toggle("Play a sound on button clicks", "A subtle click when you press a button.",
            S.ClickSoundEnabled, v => Update(s => s.ClickSoundEnabled = v)));
        p.Children.Add(Dropdown("The overlay appears at", "Where the radial menu opens when you press the shortcut.",
            new[] { "The mouse pointer", "The center of the screen" },
            S.OverlayPlacement == OverlayPlacement.ScreenCenter ? 1 : 0,
            i => Update(s => s.OverlayPlacement = i == 1 ? OverlayPlacement.ScreenCenter : OverlayPlacement.MousePointer)));
        return p;
    }

    private UIElement BuildHotkey()
    {
        var p = NewPage();
        p.Children.Add(Section("Activation shortcut"));
        p.Children.Add(Caption("Press this shortcut anywhere to open the radial overlay. Windows cannot register a shortcut made only of modifier keys, so include a normal key such as P."));

        var recorder = new HotkeyRecorder { Current = _host.GetCurrentHotkey(), Margin = new Thickness(0, 14, 0, 0) };
        recorder.TryApply = _host.TryApplyHotkey;
        p.Children.Add(recorder);
        return p;
    }

    private UIElement BuildGeneration()
    {
        var p = NewPage();
        p.Children.Add(Section("Default password"));
        p.Children.Add(IntSlider("Length", "Number of characters in generated passwords.",
            PasswordGenerator.MinLength, 64, S.PasswordLength, v => { Update(s => s.PasswordLength = v); RefreshSample(); }));

        p.Children.Add(Toggle("Uppercase letters (A–Z)", null, S.IncludeUppercase, v => { Update(s => s.IncludeUppercase = v); RefreshSample(); }));
        p.Children.Add(Toggle("Lowercase letters (a–z)", null, S.IncludeLowercase, v => { Update(s => s.IncludeLowercase = v); RefreshSample(); }));
        p.Children.Add(Toggle("Numbers (0–9)", null, S.IncludeDigits, v => { Update(s => s.IncludeDigits = v); RefreshSample(); }));
        p.Children.Add(Toggle("Special characters", null, S.IncludeSymbols, v => { Update(s => s.IncludeSymbols = v); RefreshSample(); }));
        p.Children.Add(Toggle("Exclude look-alike characters", "Leave out easily confused characters such as I, l, 1, O and 0.",
            S.ExcludeAmbiguous, v => { Update(s => s.ExcludeAmbiguous = v); RefreshSample(); }));

        p.Children.Add(Section("Symbols to use"));
        var symbols = new TextBox { Text = S.SymbolSet, FontFamily = (FontFamily)FindResource("MonoFontFamily"), Margin = new Thickness(0, 0, 0, 4) };
        symbols.LostFocus += (_, _) =>
        {
            Update(s => s.SymbolSet = symbols.Text);
            symbols.Text = S.SymbolSet;
            RefreshSample();
        };
        p.Children.Add(symbols);

        p.Children.Add(Section("Preview"));
        var sampleRow = new Grid();
        sampleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        sampleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _sampleBox = new TextBox { IsReadOnly = true, FontFamily = (FontFamily)FindResource("MonoFontFamily"), FontSize = 13 };
        sampleRow.Children.Add(_sampleBox);
        var refresh = new Button { Content = "Refresh", Margin = new Thickness(8, 0, 0, 0) };
        refresh.Click += (_, _) => RefreshSample();
        Grid.SetColumn(refresh, 1);
        sampleRow.Children.Add(refresh);
        p.Children.Add(sampleRow);
        RefreshSample();

        return p;
    }

    private UIElement BuildClipboard()
    {
        var p = NewPage();
        p.Children.Add(Section("Clipboard history"));
        p.Children.Add(Toggle("Keep clipboard history", "Remember recent clipboard text so you can paste it again. Stored in memory only — never written to disk.",
            S.ClipboardHistoryEnabled, v => Update(s => s.ClipboardHistoryEnabled = v)));
        p.Children.Add(IntSlider("Entries to keep", "How many recent clipboard items to remember.",
            5, 100, S.ClipboardHistoryLimit, v => Update(s => s.ClipboardHistoryLimit = v)));
        p.Children.Add(Button("Clear clipboard history now", "Remove every remembered clipboard item.", "Clear", () =>
        {
            _host.ClearClipboardHistory();
        }));

        p.Children.Add(Section("Copied passwords"));
        p.Children.Add(IntSlider("Clear the clipboard after", "Seconds after copying a password before the clipboard is cleared (0 = never). Only clears if the value is still the one Password helper copied.",
            0, 300, S.ClearClipboardAfterSeconds, v => Update(s => s.ClearClipboardAfterSeconds = v), SecondsLabel));

        p.Children.Add(Section("Ignore these apps"));
        p.Children.Add(Caption("Clipboard text copied by these programs is not added to history (one executable name per line)."));
        var ignored = new TextBox
        {
            Text = string.Join(Environment.NewLine, S.ClipboardIgnoredApps),
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MinHeight = 90,
            MaxHeight = 140,
            FontFamily = (FontFamily)FindResource("MonoFontFamily"),
            FontSize = 12.5,
            VerticalContentAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 6, 0, 0),
        };
        ignored.LostFocus += (_, _) =>
        {
            var apps = ignored.Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            Update(s => s.ClipboardIgnoredApps = apps);
            ignored.Text = string.Join(Environment.NewLine, S.ClipboardIgnoredApps);
        };
        p.Children.Add(ignored);
        return p;
    }

    private UIElement BuildSecurity()
    {
        var p = NewPage();
        p.Children.Add(Section("Vault"));
        p.Children.Add(Caption($"Saved passwords are encrypted with Windows DPAPI for your account. Another account, or another PC, cannot read them."));

        VaultState state = _host.GetVaultState();
        string lockLabel = state == VaultState.Locked ? "Unlock" : "Lock";
        p.Children.Add(Button("Lock or unlock the vault", "Locking clears decrypted passwords from memory until you unlock.",
            lockLabel, () =>
            {
                if (_host.GetVaultState() == VaultState.Locked) _host.UnlockVault();
                else _host.LockVault();
            }));

        p.Children.Add(Toggle("Lock the vault when Windows locks", "Automatically lock whenever you lock your PC.",
            S.LockVaultOnWindowsLock, v => Update(s => s.LockVaultOnWindowsLock = v)));
        p.Children.Add(IntSlider("Auto-lock after inactivity", "Minutes of no vault use before it locks itself (0 = never).",
            0, 60, S.AutoLockMinutes, v => Update(s => s.AutoLockMinutes = v), MinutesLabel));

        p.Children.Add(Section("Data"));
        p.Children.Add(Button("Storage location", AppPaths.DataDirectory, "Open folder", OpenStorageFolder));
        p.Children.Add(Button("Clear clipboard history", "Remove every remembered clipboard item now.", "Clear", () => _host.ClearClipboardHistory()));
        p.Children.Add(Button("Delete all saved passwords", "Permanently delete the vault file and its backup. This cannot be undone.",
            "Delete…", () => _host.DeleteVaultData(), danger: true));
        return p;
    }

    private UIElement BuildAppearance()
    {
        var p = NewPage();
        p.Children.Add(Section("Glass"));
        p.Children.Add(Toggle("Frosted glass effect", "Blur the desktop behind the overlay. Falls back to a solid surface if unavailable.",
            S.GlassEffect, v => Update(s => s.GlassEffect = v)));
        p.Children.Add(DoubleSlider("Glass opacity", "How solid the glass panels look.", 0.4, 1.0, S.GlassOpacity, v => Update(s => s.GlassOpacity = v)));
        p.Children.Add(DoubleSlider("Blur intensity", "Strength of the background blur.", 0.0, 1.0, S.BlurIntensity, v => Update(s => s.BlurIntensity = v)));

        p.Children.Add(Section("Motion"));
        p.Children.Add(Toggle("Overlay animations", "Animate the overlay as it opens and closes.",
            S.AnimationsEnabled, v => Update(s => s.AnimationsEnabled = v)));
        p.Children.Add(Toggle("Reduced motion", "Minimise movement; show the overlay with a quick fade instead.",
            S.ReducedMotion, v => Update(s => s.ReducedMotion = v)));
        p.Children.Add(DoubleSlider("Animation speed", "Higher is faster.", 0.5, 2.0, S.AnimationSpeed, v => Update(s => s.AnimationSpeed = v)));

        p.Children.Add(Section("Radial menu"));
        p.Children.Add(Dropdown("Button size", null, new[] { "Small", "Medium", "Large" }, (int)S.ButtonSize,
            i => Update(s => s.ButtonSize = (RadialButtonSize)i)));
        p.Children.Add(IntSlider("Saved shortcuts on the ring", "How many saved passwords appear directly on the ring before a More button is used.",
            0, 16, S.MaxRadialItems, v => Update(s => s.MaxRadialItems = v)));
        p.Children.Add(DoubleSlider("Click sound volume", null, 0.0, 1.0, S.ClickSoundVolume, v => Update(s => s.ClickSoundVolume = v)));
        return p;
    }

    private UIElement BuildAbout()
    {
        var p = NewPage();
        p.Children.Add(new TextBlock { Text = "Password helper", FontSize = 22, FontWeight = FontWeights.SemiBold, Foreground = Res("PrimaryTextBrush") });
        p.Children.Add(new TextBlock { Text = "Version 1.0.0", Foreground = Res("SecondaryTextBrush"), Margin = new Thickness(0, 4, 0, 0) });
        p.Children.Add(new TextBlock
        {
            Text = "A lightweight radial password generator, encrypted vault and clipboard history for Windows.",
            TextWrapping = TextWrapping.Wrap, Foreground = Res("SecondaryTextBrush"), Margin = new Thickness(0, 16, 0, 0), LineHeight = 20,
        });
        p.Children.Add(new TextBlock { Text = SystemInfo.Describe(), Foreground = Res("TertiaryTextBrush"), FontSize = 12, Margin = new Thickness(0, 16, 0, 0) });
        p.Children.Add(Button("Diagnostic logs", AppPaths.LogDirectory, "Open folder", () => OpenFolder(AppPaths.LogDirectory)));
        p.Children.Add(new TextBlock
        {
            Text = "Logs never contain passwords, clipboard contents or vault data.",
            Foreground = Res("TertiaryTextBrush"), FontSize = 11.5, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap,
        });
        return p;
    }

    // ------------------------------------------------------------------ control builders

    private StackPanel NewPage() => new() { Margin = new Thickness(30, 24, 30, 28) };

    private ScrollViewer Scroll(UIElement content) => new()
    {
        Content = content,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        Padding = new Thickness(0, 0, 8, 0),
    };

    private TextBlock Section(string text) => new()
    {
        Text = text.ToUpperInvariant(),
        Style = (Style)FindResource("SectionLabel"),
        Margin = new Thickness(0, 18, 0, 10),
    };

    private TextBlock Caption(string text) => new()
    {
        Text = text,
        Style = (Style)FindResource("CaptionText"),
        Margin = new Thickness(0, 0, 0, 2),
    };

    private FrameworkElement Toggle(string label, string? description, bool value, Action<bool> onChanged)
    {
        var grid = RowGrid();
        grid.Children.Add(LabelStack(label, description));

        var check = new CheckBox { IsChecked = value, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
        check.Checked += (_, _) => onChanged(true);
        check.Unchecked += (_, _) => onChanged(false);
        Grid.SetColumn(check, 1);
        grid.Children.Add(check);
        return Wrap(grid);
    }

    private FrameworkElement Dropdown(string label, string? description, string[] options, int selectedIndex, Action<int> onChanged)
    {
        var grid = RowGrid();
        grid.Children.Add(LabelStack(label, description));

        var combo = new ComboBox { Width = 210, HorizontalAlignment = HorizontalAlignment.Right };
        foreach (string option in options) combo.Items.Add(option); // plain strings; WPF generates the containers
        combo.SelectedIndex = Math.Clamp(selectedIndex, 0, options.Length - 1);
        combo.SelectionChanged += (_, _) => onChanged(combo.SelectedIndex);
        Grid.SetColumn(combo, 1);
        grid.Children.Add(combo);
        return Wrap(grid);
    }

    private FrameworkElement IntSlider(string label, string? description, int min, int max, int value, Action<int> onChanged, Func<int, string>? format = null)
    {
        var value2 = new TextBlock { Foreground = Res("AccentBrush"), FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, MinWidth = 56, TextAlignment = TextAlignment.Right };
        value2.Text = format?.Invoke(value) ?? value.ToString(CultureInfo.CurrentCulture);

        var slider = new Slider
        {
            Minimum = min,
            Maximum = max,
            Value = Math.Clamp(value, min, max),
            IsSnapToTickEnabled = true,
            TickFrequency = 1,
            Width = 240,
            VerticalAlignment = VerticalAlignment.Center,
        };
        slider.ValueChanged += (_, _) =>
        {
            int v = (int)Math.Round(slider.Value);
            value2.Text = format?.Invoke(v) ?? v.ToString(CultureInfo.CurrentCulture);
            onChanged(v);
        };

        return SliderRow(label, description, slider, value2);
    }

    private FrameworkElement DoubleSlider(string label, string? description, double min, double max, double value, Action<double> onChanged)
    {
        var value2 = new TextBlock { Foreground = Res("AccentBrush"), FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, MinWidth = 56, TextAlignment = TextAlignment.Right };
        value2.Text = Percent(value, min, max);

        var slider = new Slider
        {
            Minimum = min,
            Maximum = max,
            Value = Math.Clamp(value, min, max),
            Width = 240,
            VerticalAlignment = VerticalAlignment.Center,
        };
        slider.ValueChanged += (_, _) =>
        {
            value2.Text = Percent(slider.Value, min, max);
            onChanged(slider.Value);
        };

        return SliderRow(label, description, slider, value2);
    }

    private FrameworkElement SliderRow(string label, string? description, Slider slider, TextBlock valueText)
    {
        var grid = RowGrid();
        grid.Children.Add(LabelStack(label, description));

        var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        right.Children.Add(slider);
        right.Children.Add(valueText);
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        return Wrap(grid);
    }

    private FrameworkElement Button(string label, string? description, string buttonText, Action onClick, bool danger = false)
    {
        var grid = RowGrid();
        grid.Children.Add(LabelStack(label, description));

        var button = new Button
        {
            Content = buttonText,
            MinWidth = 110,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Style = (Style)FindResource(danger ? "DangerButton" : (object)typeof(Button)),
        };
        button.Click += (_, _) => onClick();
        Grid.SetColumn(button, 1);
        grid.Children.Add(button);
        return Wrap(grid);
    }

    private Grid RowGrid()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        return grid;
    }

    private StackPanel LabelStack(string label, string? description)
    {
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 18, 0) };
        stack.Children.Add(new TextBlock { Text = label, Foreground = Res("PrimaryTextBrush"), FontSize = 13.5 });
        if (!string.IsNullOrEmpty(description))
        {
            stack.Children.Add(new TextBlock { Text = description, Foreground = Res("SecondaryTextBrush"), FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) });
        }

        return stack;
    }

    private Border Wrap(UIElement content) => new()
    {
        Child = content,
        Padding = new Thickness(0, 11, 0, 11),
        BorderBrush = Res("BorderBrush"),
        BorderThickness = new Thickness(0, 0, 0, 1),
    };

    // ------------------------------------------------------------------ helpers

    private void Update(Action<AppSettings> change) => _settingsService.Update(change);

    private void RefreshSample()
    {
        if (_sampleBox is null) return;
        PasswordPolicy policy = S.ToPasswordPolicy();
        if (PasswordGenerator.Validate(policy) is not null)
        {
            _sampleBox.Text = string.Empty;
            return;
        }

        char[] sample = PasswordGenerator.Generate(policy);
        try { _sampleBox.Text = new string(sample); } finally { Sensitive.Clear(sample); }
    }

    private void OpenStorageFolder() => OpenFolder(AppPaths.DataDirectory);

    private static void OpenFolder(string path)
    {
        try
        {
            System.IO.Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Warn("Could not open the folder", ex);
        }
    }

    private static string SecondsLabel(int s) => s <= 0 ? "Never" : s + " s";
    private static string MinutesLabel(int m) => m <= 0 ? "Never" : m + " min";
    private static string Percent(double value, double min, double max) =>
        ((int)Math.Round((value - min) / (max - min) * 100)).ToString(CultureInfo.CurrentCulture) + "%";

    private static Brush Res(string key) => (Brush)Application.Current.FindResource(key);
}
