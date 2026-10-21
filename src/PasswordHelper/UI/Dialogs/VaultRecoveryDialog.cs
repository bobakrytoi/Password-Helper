using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PasswordHelper.UI.Common;

namespace PasswordHelper.UI.Dialogs;

internal enum VaultRecoveryChoice
{
    Cancel,
    RestoreBackup,
    StartNew,
}

/// <summary>
/// Shown when the vault file cannot be read (damaged, encrypted by another account, or newer than this app). It
/// never destroys the existing file silently: the user explicitly chooses to restore a backup, start fresh (moving
/// the unreadable file aside), or do nothing.
/// </summary>
internal sealed class VaultRecoveryDialog : ChromeWindow
{
    private VaultRecoveryDialog(string problem, bool backupAvailable)
    {
        Title = "Vault could not be opened";
        Width = 460;
        SizeToContent = SizeToContent.Height;

        var panel = new StackPanel { Margin = new Thickness(24, 4, 24, 22) };

        panel.Children.Add(new TextBlock
        {
            Text = problem,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Application.Current.FindResource("SecondaryTextBrush"),
            FontSize = 13.5,
            LineHeight = 20,
        });

        panel.Children.Add(new TextBlock
        {
            Text = "Your existing vault file has not been changed. Choose how to continue:",
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Application.Current.FindResource("TertiaryTextBrush"),
            FontSize = 12.5,
            Margin = new Thickness(0, 12, 0, 0),
        });

        if (backupAvailable)
        {
            panel.Children.Add(MakeChoice("Restore the last backup",
                "Replace the unreadable file with the most recent good copy (the bad file is kept, renamed).",
                isPrimary: true, () => Finish(VaultRecoveryChoice.RestoreBackup)));
        }

        panel.Children.Add(MakeChoice("Start a new empty vault",
            "Move the unreadable file aside and begin with an empty vault. Nothing is deleted.",
            isPrimary: !backupAvailable, () => Finish(VaultRecoveryChoice.StartNew)));

        panel.Children.Add(MakeChoice("Leave it for now",
            "Do nothing. Saved passwords stay unavailable until the file can be read.",
            isPrimary: false, () => Finish(VaultRecoveryChoice.Cancel)));

        Content = panel;
    }

    public VaultRecoveryChoice Choice { get; private set; } = VaultRecoveryChoice.Cancel;

    private void Finish(VaultRecoveryChoice choice)
    {
        Choice = choice;
        DialogResult = choice != VaultRecoveryChoice.Cancel;
        Close();
    }

    private Button MakeChoice(string title, string description, bool isPrimary, System.Action onClick)
    {
        var content = new StackPanel();
        content.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = FontWeights.SemiBold,
            FontSize = 13.5,
            Foreground = (Brush)Application.Current.FindResource(isPrimary ? "PrimaryTextBrush" : "PrimaryTextBrush"),
        });
        content.Children.Add(new TextBlock
        {
            Text = description,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Foreground = (Brush)Application.Current.FindResource("SecondaryTextBrush"),
            Margin = new Thickness(0, 3, 0, 0),
        });

        var button = new Button
        {
            Content = content,
            Margin = new Thickness(0, 14, 0, 0),
            Padding = new Thickness(16, 12, 16, 12),
            HorizontalContentAlignment = HorizontalAlignment.Left,
        };
        if (isPrimary)
        {
            button.BorderBrush = (Brush)Application.Current.FindResource("BorderFocusBrush");
        }

        button.Click += (_, _) => onClick();
        return button;
    }

    public static VaultRecoveryChoice Show(Window? owner, string problem, bool backupAvailable)
    {
        var dialog = new VaultRecoveryDialog(problem, backupAvailable);
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

        dialog.ShowDialog();
        return dialog.Choice;
    }
}
