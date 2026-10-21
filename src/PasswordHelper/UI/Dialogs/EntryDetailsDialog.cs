using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using PasswordHelper.Core;
using PasswordHelper.UI.Common;
using PasswordHelper.Vault;

namespace PasswordHelper.UI.Dialogs;

internal enum EntryDetailsResult
{
    None,
    Edit,
    Delete,
}

/// <summary>
/// Read-only view of a saved entry with copy actions. The password is masked and only revealed on request; copy
/// actions are delegated to the caller so they go through the clipboard manager (marking, auto-clear) consistently.
/// </summary>
internal sealed class EntryDetailsDialog : ChromeWindow
{
    private readonly VaultEntry _entry;
    private readonly Action _copyUsername;
    private readonly Action _copyPassword;
    private TextBlock? _passwordDisplay;
    private bool _revealed;

    private EntryDetailsDialog(VaultEntry entry, Action copyUsername, Action copyPassword)
    {
        _entry = entry;
        _copyUsername = copyUsername;
        _copyPassword = copyPassword;

        Title = "Entry details";
        Width = 460;
        SizeToContent = SizeToContent.Height;
        Content = BuildContent();
    }

    public EntryDetailsResult Result { get; private set; } = EntryDetailsResult.None;

    private UIElement BuildContent()
    {
        var panel = new StackPanel { Margin = new Thickness(24, 2, 24, 20) };

        panel.Children.Add(new TextBlock
        {
            Text = _entry.Title,
            FontSize = 19,
            FontWeight = FontWeights.SemiBold,
            Foreground = Res("PrimaryTextBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis,
        });

        if (!string.IsNullOrEmpty(_entry.Username))
        {
            panel.Children.Add(BuildField("USERNAME", _entry.Username, Icons.Copy, "Copy username", _copyUsername));
        }

        panel.Children.Add(BuildPasswordField());

        if (!string.IsNullOrEmpty(_entry.Url))
        {
            panel.Children.Add(BuildField("WEBSITE", _entry.Url, Icons.Link, "Open website", OpenUrl));
        }

        if (!string.IsNullOrEmpty(_entry.Notes))
        {
            panel.Children.Add(new TextBlock { Text = "NOTES", Style = (Style)Res2("SectionLabel"), Margin = new Thickness(0, 16, 0, 6) });
            panel.Children.Add(new Border
            {
                Background = Res("InputBrush"),
                BorderBrush = Res("BorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 9, 12, 9),
                Child = new TextBlock
                {
                    Text = _entry.Notes,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = Res("SecondaryTextBrush"),
                    FontSize = 13,
                    MaxHeight = 140,
                },
            });
        }

        panel.Children.Add(new TextBlock
        {
            Text = $"Created {Local(_entry.CreatedUtc)}  ·  Updated {Local(_entry.ModifiedUtc)}",
            Foreground = Res("TertiaryTextBrush"),
            FontSize = 11.5,
            Margin = new Thickness(0, 16, 0, 0),
        });

        panel.Children.Add(BuildFooter());
        return panel;
    }

    private UIElement BuildField(string label, string value, string actionIcon, string actionTip, Action action)
    {
        var container = new StackPanel { Margin = new Thickness(0, 16, 0, 0) };
        container.Children.Add(new TextBlock { Text = label, Style = (Style)Res2("SectionLabel"), Margin = new Thickness(0, 0, 0, 6) });

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var box = new Border
        {
            Background = Res("InputBrush"),
            BorderBrush = Res("BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 9, 12, 9),
            Child = new TextBlock
            {
                Text = value,
                Foreground = Res("PrimaryTextBrush"),
                FontSize = 13,
                TextTrimming = TextTrimming.CharacterEllipsis,
            },
        };
        grid.Children.Add(box);

        var button = IconActionButton(actionIcon, actionTip, action);
        button.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(button, 1);
        grid.Children.Add(button);

        container.Children.Add(grid);
        return container;
    }

    private UIElement BuildPasswordField()
    {
        var container = new StackPanel { Margin = new Thickness(0, 16, 0, 0) };
        container.Children.Add(new TextBlock { Text = "PASSWORD", Style = (Style)Res2("SectionLabel"), Margin = new Thickness(0, 0, 0, 6) });

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _passwordDisplay = new TextBlock
        {
            Text = new string('•', 12),
            Foreground = Res("PrimaryTextBrush"),
            FontFamily = (FontFamily)Res2("MonoFontFamily"),
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var box = new Border
        {
            Background = Res("InputBrush"),
            BorderBrush = Res("BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 9, 12, 9),
            Child = _passwordDisplay,
        };
        grid.Children.Add(box);

        var reveal = IconActionButton(Icons.Eye, "Show password", ToggleReveal);
        reveal.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(reveal, 1);
        grid.Children.Add(reveal);

        var copy = IconActionButton(Icons.Copy, "Copy password", _copyPassword);
        copy.Margin = new Thickness(6, 0, 0, 0);
        Grid.SetColumn(copy, 2);
        grid.Children.Add(copy);

        container.Children.Add(grid);
        return container;
    }

    private void ToggleReveal()
    {
        if (_passwordDisplay is null) return;
        if (!_revealed)
        {
            char[] chars = _entry.Password.Reveal();
            try { _passwordDisplay.Text = new string(chars); } finally { Sensitive.Clear(chars); }
        }
        else
        {
            _passwordDisplay.Text = new string('•', 12);
        }

        _revealed = !_revealed;
    }

    private UIElement BuildFooter()
    {
        var grid = new Grid { Margin = new Thickness(0, 22, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var delete = new Button { Content = "Delete", MinWidth = 90, Style = (Style)Res2("DangerButton") };
        delete.Click += (_, _) => { Result = EntryDetailsResult.Delete; DialogResult = true; };
        Grid.SetColumn(delete, 0);
        grid.Children.Add(delete);

        var edit = new Button { Content = "Edit", MinWidth = 110, Style = (Style)Res2("PrimaryButton") };
        edit.Click += (_, _) => { Result = EntryDetailsResult.Edit; DialogResult = true; };
        Grid.SetColumn(edit, 2);
        grid.Children.Add(edit);

        return grid;
    }

    private void OpenUrl()
    {
        string url = _entry.Url.Trim();
        if (!url.Contains("://", StringComparison.Ordinal)) url = "https://" + url;
        if (Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Log.Warn("Could not open the website", ex);
            }
        }
    }

    private Button IconActionButton(string iconPath, string tip, Action action)
    {
        var icon = new Path
        {
            Data = Icons.Get(iconPath),
            Stroke = Res("SecondaryTextBrush"),
            StrokeThickness = 1.7,
            Stretch = Stretch.Uniform,
            Width = 17,
            Height = 17,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
        };
        var button = new Button { Style = (Style)Res2("IconButton"), Content = icon, ToolTip = tip, Width = 40, Height = 40 };
        button.Click += (_, _) => action();
        return button;
    }

    private static Brush Res(string key) => (Brush)Application.Current.FindResource(key);
    private static object Res2(string key) => Application.Current.FindResource(key);
    private static string Local(DateTime utc) => utc.ToLocalTime().ToString("d MMM yyyy", CultureInfo.CurrentCulture);

    public static EntryDetailsResult Show(Window? owner, VaultEntry entry, Action copyUsername, Action copyPassword)
    {
        var dialog = new EntryDetailsDialog(entry, copyUsername, copyPassword);
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
        return dialog.Result;
    }
}
