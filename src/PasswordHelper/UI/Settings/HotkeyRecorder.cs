using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PasswordHelper.Input;
using PasswordHelper.UI.Common;

namespace PasswordHelper.UI.Settings;

/// <summary>
/// Captures a keyboard shortcut. Click "Change Shortcut", press the desired combination, then Save or Cancel.
/// Applying the shortcut is delegated via <see cref="TryApply"/>, which returns an error message (shown inline)
/// or null on success — so a shortcut that cannot be registered never silently overwrites the saved one.
/// </summary>
internal sealed class HotkeyRecorder : UserControl
{
    private readonly TextBlock _display;
    private readonly Button _changeButton;
    private readonly Button _saveButton;
    private readonly Button _cancelButton;
    private readonly Button _resetButton;
    private readonly TextBlock _error;
    private readonly StackPanel _editActions;

    private HotkeyGesture _current;
    private HotkeyGesture _pending;
    private bool _recording;
    private bool _hasPending;

    public HotkeyRecorder()
    {
        Focusable = false;

        _display = new TextBlock
        {
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.FindResource("PrimaryTextBrush"),
            VerticalAlignment = VerticalAlignment.Center,
        };

        var box = new Border
        {
            Background = (Brush)Application.Current.FindResource("InputBrush"),
            BorderBrush = (Brush)Application.Current.FindResource("BorderStrongBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 10, 14, 10),
            MinWidth = 150,
            Child = _display,
            Focusable = true,
        };
        box.PreviewKeyDown += OnRecordKey;
        box.LostKeyboardFocus += OnBoxLostFocus;
        _box = box;

        _changeButton = new Button { Content = "Change Shortcut", Margin = new Thickness(10, 0, 0, 0), MinWidth = 130 };
        _changeButton.Click += (_, _) => BeginRecording();

        _resetButton = new Button { Content = "Reset", Style = (Style)Application.Current.FindResource("GhostButton"), Margin = new Thickness(6, 0, 0, 0) };
        _resetButton.Click += (_, _) => ResetToDefault();

        _saveButton = new Button { Content = "Save", Style = (Style)Application.Current.FindResource("PrimaryButton"), Margin = new Thickness(10, 0, 0, 0), MinWidth = 80 };
        _saveButton.Click += (_, _) => Commit();

        _cancelButton = new Button { Content = "Cancel", Margin = new Thickness(6, 0, 0, 0) };
        _cancelButton.Click += (_, _) => CancelRecording();

        _editActions = new StackPanel { Orientation = Orientation.Horizontal, Visibility = Visibility.Collapsed };
        _editActions.Children.Add(_saveButton);
        _editActions.Children.Add(_cancelButton);

        var idleActions = new StackPanel { Orientation = Orientation.Horizontal };
        idleActions.Children.Add(_changeButton);
        idleActions.Children.Add(_resetButton);
        _idleActions = idleActions;

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(box);
        row.Children.Add(idleActions);
        row.Children.Add(_editActions);

        _error = new TextBlock
        {
            Foreground = (Brush)Application.Current.FindResource("DangerBrush"),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(2, 8, 0, 0),
            Visibility = Visibility.Collapsed,
        };

        var container = new StackPanel();
        container.Children.Add(row);
        container.Children.Add(_error);
        Content = container;
    }

    private readonly Border _box;
    private readonly StackPanel _idleActions;

    /// <summary>Returns an error message if the gesture cannot be applied, or null on success.</summary>
    public Func<HotkeyGesture, string?>? TryApply { get; set; }

    public HotkeyGesture Current
    {
        get => _current;
        set
        {
            _current = value;
            _display.Text = value.ToDisplayString();
        }
    }

    private void BeginRecording()
    {
        _recording = true;
        _hasPending = false;
        _error.Visibility = Visibility.Collapsed;
        _display.Text = "Press a shortcut…";
        _display.Foreground = (Brush)Application.Current.FindResource("AccentBrush");
        _idleActions.Visibility = Visibility.Collapsed;
        _editActions.Visibility = Visibility.Visible;
        _saveButton.IsEnabled = false;
        Keyboard.Focus(_box);
    }

    private void OnBoxLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        // Only abandon recording if focus left the recorder entirely. Moving focus to our own Save/Cancel
        // buttons must NOT cancel (otherwise clicking Save reverts the display before the click is handled).
        if (!_recording) return;
        if (e.NewFocus is DependencyObject target && IsWithinRecorder(target)) return;
        CancelRecording();
    }

    private bool IsWithinRecorder(DependencyObject node)
    {
        for (DependencyObject? current = node; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (ReferenceEquals(current, this)) return true;
        }

        return false;
    }

    private void OnRecordKey(object sender, KeyEventArgs e)
    {
        if (!_recording) return;
        e.Handled = true;

        if (e.Key == Key.Escape)
        {
            CancelRecording();
            return;
        }

        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        int vk = KeyInterop.VirtualKeyFromKey(key);

        HotkeyModifiers modifiers = HotkeyModifiers.None;
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) modifiers |= HotkeyModifiers.Control;
        if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0) modifiers |= HotkeyModifiers.Alt;
        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) modifiers |= HotkeyModifiers.Shift;
        if (Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin)) modifiers |= HotkeyModifiers.Win;

        if (HotkeyGesture.IsModifierKey(vk))
        {
            // Only modifiers held so far: show them and wait for a real key.
            var preview = new HotkeyGesture(modifiers, 0);
            _display.Text = modifiers == HotkeyModifiers.None ? "Press a shortcut…" : preview + "…";
            _saveButton.IsEnabled = false;
            return;
        }

        _pending = new HotkeyGesture(modifiers, vk);
        _hasPending = true;
        _display.Text = _pending.ToDisplayString();

        string? problem = _pending.Validate();
        if (problem is not null)
        {
            ShowError(problem);
            _saveButton.IsEnabled = false;
        }
        else
        {
            _error.Visibility = Visibility.Collapsed;
            _saveButton.IsEnabled = true;
        }
    }

    private void Commit()
    {
        if (!_hasPending) return;

        string? error = TryApply?.Invoke(_pending);
        if (error is not null)
        {
            ShowError(error);
            return;
        }

        Current = _pending;
        EndRecording();
    }

    private void ResetToDefault()
    {
        string? error = TryApply?.Invoke(HotkeyGesture.Default);
        if (error is not null)
        {
            ShowError(error);
            return;
        }

        Current = HotkeyGesture.Default;
        _error.Visibility = Visibility.Collapsed;
    }

    private void CancelRecording()
    {
        _display.Text = _current.ToDisplayString();
        EndRecording();
    }

    private void EndRecording()
    {
        _recording = false;
        _hasPending = false;
        _display.Foreground = (Brush)Application.Current.FindResource("PrimaryTextBrush");
        _editActions.Visibility = Visibility.Collapsed;
        _idleActions.Visibility = Visibility.Visible;
    }

    private void ShowError(string message)
    {
        _error.Text = message;
        _error.Visibility = Visibility.Visible;
    }
}
