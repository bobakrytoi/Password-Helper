using System.Windows;
using System.Windows.Controls;
using PasswordHelper.UI.Common;

namespace PasswordHelper.UI.Dialogs;

/// <summary>A themed yes/no confirmation dialog, used before destructive actions such as deleting an entry.</summary>
internal sealed class ConfirmDialog : ChromeWindow
{
    private ConfirmDialog(string title, string message, string confirmText, string cancelText, bool danger)
    {
        Title = title;
        Width = 410;
        SizeToContent = SizeToContent.Height;

        var panel = new StackPanel { Margin = new Thickness(24, 4, 24, 22) };
        panel.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("SecondaryTextBrush"),
            FontSize = 13.5,
            LineHeight = 20,
        });

        var buttons = new Grid { Margin = new Thickness(0, 24, 0, 0) };
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var cancel = new Button { Content = cancelText, MinWidth = 96 };
        cancel.Click += (_, _) => DialogResult = false;
        Grid.SetColumn(cancel, 1);
        buttons.Children.Add(cancel);

        var confirm = new Button
        {
            Content = confirmText,
            MinWidth = 110,
            Margin = new Thickness(10, 0, 0, 0),
            Style = (Style)Application.Current.FindResource(danger ? "DangerButton" : "PrimaryButton"),
            IsDefault = true,
        };
        confirm.Click += (_, _) => DialogResult = true;
        Grid.SetColumn(confirm, 2);
        buttons.Children.Add(confirm);

        panel.Children.Add(buttons);
        Content = panel;

        Loaded += (_, _) => cancel.Focus();
    }

    public static bool Show(Window? owner, string title, string message,
        string confirmText = "Confirm", string cancelText = "Cancel", bool danger = false)
    {
        var dialog = new ConfirmDialog(title, message, confirmText, cancelText, danger);
        if (owner is not null && owner.IsVisible)
        {
            dialog.Owner = owner;
            dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        else
        {
            dialog.Topmost = true;
            dialog.CenterOnOwnerOrCursor();
        }

        return dialog.ShowDialog() == true;
    }
}
