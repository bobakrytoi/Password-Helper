using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using PasswordHelper.UI.Common;

namespace PasswordHelper.UI.Overlay;

public partial class OverlayWindow
{
    private sealed class RowVisual
    {
        public required Border Root { get; init; }
        public required PanelEntry Entry { get; init; }
        public bool IsVisible => Root.Visibility == Visibility.Visible;
    }

    private static readonly SolidColorBrush RowHoverBrush = Freeze(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));
    private static readonly SolidColorBrush RowSelectedBrush = Freeze(Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF));

    /// <summary>Switches from the radial view to a titled list panel (clipboard history or saved passwords).</summary>
    internal void ShowPanel(OverlayPanelModel model)
    {
        _panelVisible = true;
        _panelRows.Clear();
        _panelSelection = -1;
        _panelSearch = null;

        PanelHost.Children.Clear();
        PanelHost.Children.Add(BuildPanelCard(model));
        PanelHost.Visibility = Visibility.Visible;

        if (_animate)
        {
            RadialCanvas.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, Scaled(110)) { EasingFunction = EaseOut() });
            PanelHost.Opacity = 0;
            PanelHost.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, Scaled(150)) { EasingFunction = EaseOut() });
            var scale = new ScaleTransform(0.97, 0.97);
            PanelHost.RenderTransform = scale;
            PanelHost.RenderTransformOrigin = new Point(0.5, 0.5);
            var a = new DoubleAnimation(0.97, 1, Scaled(170)) { EasingFunction = EaseOut() };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, a);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, a);
        }
        else
        {
            RadialCanvas.Opacity = 0;
            PanelHost.Opacity = 1;
        }

        RadialCanvas.Visibility = Visibility.Hidden;
        UpdateSelection(FirstVisibleRow());
        Dispatcher.BeginInvoke(new Action(() => (_panelSearch as IInputElement)?.Focus()),
            System.Windows.Threading.DispatcherPriority.Input);
    }

    internal void ReturnToRadial()
    {
        _panelVisible = false;
        RadialCanvas.Visibility = Visibility.Visible;

        if (_animate)
        {
            PanelHost.BeginAnimation(OpacityProperty, new DoubleAnimation(PanelHost.Opacity, 0, Scaled(110)) { EasingFunction = EaseOut() });
            RadialCanvas.BeginAnimation(OpacityProperty, new DoubleAnimation(RadialCanvas.Opacity, 1, Scaled(160)) { EasingFunction = EaseOut() });
        }
        else
        {
            RadialCanvas.Opacity = 1;
        }

        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!_panelVisible)
            {
                PanelHost.Visibility = Visibility.Collapsed;
                PanelHost.Children.Clear();
                _panelRows.Clear();
            }
        }), System.Windows.Threading.DispatcherPriority.Background);

        FocusFirst();
    }

    private Border BuildPanelCard(OverlayPanelModel model)
    {
        double workWidth = _geo.WorkArea.Width / _scale;
        double workHeight = _geo.WorkArea.Height / _scale;
        double cardWidth = Math.Min(410, workWidth - 80);
        double maxHeight = Math.Max(240, workHeight * 0.72);

        var card = new Border
        {
            Width = cardWidth,
            MaxHeight = maxHeight,
            CornerRadius = new CornerRadius(16),
            Background = GlassBrush(0x17, 0x19, 0x1E, 0.86),
            BorderBrush = Brush(0xFF, 0xFF, 0xFF, 0.14),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Effect = SoftShadow(),
            Padding = new Thickness(0),
        };

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // header
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // search
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // list
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // footer

        grid.Children.Add(BuildPanelHeader(model.Title));

        if (model.ShowSearch)
        {
            FrameworkElement search = BuildSearchBox(model.SearchPlaceholder);
            Grid.SetRow(search, 1);
            grid.Children.Add(search);
        }

        var list = BuildPanelList(model);
        Grid.SetRow(list, 2);
        grid.Children.Add(list);

        if (model.FooterAction is not null)
        {
            FrameworkElement footer = BuildPanelFooter(model.FooterAction);
            Grid.SetRow(footer, 3);
            grid.Children.Add(footer);
        }

        card.Child = grid;
        return card;
    }

    private UIElement BuildPanelHeader(string title)
    {
        var grid = new Grid { Margin = new Thickness(10, 10, 10, 6) };

        var back = MakeIconButton(Icons.ChevronLeft, "Back (Esc)", () => ReturnToRadial(), danger: false);
        back.HorizontalAlignment = HorizontalAlignment.Left;
        grid.Children.Add(back);

        var heading = new TextBlock
        {
            Text = title,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush(0xF5, 0xF6, 0xF8, 1),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        grid.Children.Add(heading);

        var close = MakeIconButton(Icons.Close, "Close (Esc)", () => BeginClose(), danger: false);
        close.HorizontalAlignment = HorizontalAlignment.Right;
        grid.Children.Add(close);

        return grid;
    }

    private FrameworkElement BuildSearchBox(string placeholder)
    {
        var host = new Grid { Margin = new Thickness(12, 2, 12, 8) };
        var box = new TextBox
        {
            Style = (Style)Application.Current.FindResource(typeof(TextBox)),
            FontSize = 13,
        };
        box.TextChanged += (_, _) => ApplySearch(box.Text);
        _panelSearch = box;

        var hint = new TextBlock
        {
            Text = placeholder,
            Foreground = Brush(0x6E, 0x73, 0x7C, 1),
            Margin = new Thickness(13, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
            FontSize = 13,
        };
        box.TextChanged += (_, _) => hint.Visibility = string.IsNullOrEmpty(box.Text) ? Visibility.Visible : Visibility.Collapsed;

        host.Children.Add(box);
        host.Children.Add(hint);
        return host;
    }

    private ScrollViewer BuildPanelList(OverlayPanelModel model)
    {
        var stack = new StackPanel { Margin = new Thickness(8, 2, 8, 8) };

        foreach (PanelEntry entry in model.Entries)
        {
            Border row = BuildRow(entry);
            stack.Children.Add(row);
            _panelRows.Add(new RowVisual { Root = row, Entry = entry });
        }

        if (model.Entries.Count == 0)
        {
            stack.Children.Add(new TextBlock
            {
                Text = model.EmptyText,
                Foreground = Brush(0x6E, 0x73, 0x7C, 1),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 28, 0, 28),
                FontSize = 13,
            });
        }

        return new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = stack,
            Padding = new Thickness(0),
        };
    }

    private Border BuildRow(PanelEntry entry)
    {
        var row = new Border
        {
            CornerRadius = new CornerRadius(9),
            Background = Brushes.Transparent,
            Padding = new Thickness(10, 8, 8, 8),
            Margin = new Thickness(0, 1, 0, 1),
            Cursor = Cursors.Hand,
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Badge (monogram or glyph).
        FrameworkElement badge = BuildRowBadge(entry);
        badge.IsHitTestVisible = false;
        Grid.SetColumn(badge, 0);
        grid.Children.Add(badge);

        var text = new StackPanel { Margin = new Thickness(11, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
        text.Children.Add(new TextBlock
        {
            Text = entry.Primary,
            Foreground = Brush(0xF5, 0xF6, 0xF8, 0.96),
            FontSize = 13,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        if (!string.IsNullOrEmpty(entry.Secondary))
        {
            text.Children.Add(new TextBlock
            {
                Text = entry.Secondary,
                Foreground = Brush(0xA2, 0xA7, 0xB0, 1),
                FontSize = 11.5,
                Margin = new Thickness(0, 1, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
        }

        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Opacity = 0 };
        foreach (PanelAction action in entry.Actions)
        {
            actions.Children.Add(MakeIconButton(action.IconPath, action.Tooltip, action.Invoke, action.Danger, compact: true));
        }

        Grid.SetColumn(actions, 2);
        grid.Children.Add(actions);

        row.Child = grid;

        row.MouseEnter += (_, _) => { SelectRow(row); actions.Opacity = 1; };
        row.MouseLeave += (_, _) => { if (!IsRowSelected(row)) actions.Opacity = 0; };
        row.MouseLeftButtonUp += (_, e) =>
        {
            if (IsWithin(e.OriginalSource as DependencyObject, actions)) return; // an action button was clicked
            entry.OnActivate?.Invoke();
        };
        return row;
    }

    private FrameworkElement BuildRowBadge(PanelEntry entry)
    {
        double size = 34;
        if (entry.Monogram is not null)
        {
            return new Border
            {
                Width = size,
                Height = size,
                CornerRadius = new CornerRadius(9),
                Background = GlassBrush(0x2A, 0x2F, 0x39, 0.9),
                BorderBrush = Brush(0xFF, 0xFF, 0xFF, 0.12),
                BorderThickness = new Thickness(1),
                Child = new TextBlock
                {
                    Text = entry.Monogram,
                    Foreground = Brush(0xDC, 0xE4, 0xF0, 1),
                    FontSize = 15,
                    FontWeight = FontWeights.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
        }

        return new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(9),
            Background = GlassBrush(0x20, 0x23, 0x2A, 0.8),
            Child = new System.Windows.Shapes.Path
            {
                Data = Icons.Get(entry.GlyphPath ?? Icons.Clipboard),
                Stroke = Brush(0xC7, 0xD2, 0xE3, 0.92),
                StrokeThickness = 1.7,
                Stretch = Stretch.Uniform,
                Width = 17,
                Height = 17,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
            },
        };
    }

    private FrameworkElement BuildPanelFooter(PanelAction action)
    {
        var button = new Button
        {
            Style = (Style)Application.Current.FindResource(action.Danger ? "DangerButton" : "GhostButton"),
            Content = action.Tooltip,
            Margin = new Thickness(12, 2, 12, 12),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        button.Click += (_, _) => action.Invoke();
        return button;
    }

    private Button MakeIconButton(string iconPath, string tooltip, Action invoke, bool danger, bool compact = false)
    {
        double side = compact ? 30 : 34;
        var icon = new System.Windows.Shapes.Path
        {
            Data = Icons.Get(iconPath),
            Stroke = danger ? Brush(0xE5, 0x79, 0x6F, 1) : Brush(0xA2, 0xA7, 0xB0, 1),
            StrokeThickness = 1.7,
            Stretch = Stretch.Uniform,
            Width = compact ? 15 : 16,
            Height = compact ? 15 : 16,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
        };

        var bd = new Border { Width = side, Height = side, CornerRadius = new CornerRadius(8), Background = Brushes.Transparent, Child = icon };
        var button = new Button
        {
            Template = ChromelessButton,
            Background = Brushes.Transparent,
            Content = bd,
            Cursor = Cursors.Hand,
            ToolTip = tooltip,
            Focusable = false,
        };
        button.Click += (_, _) => invoke();
        button.MouseEnter += (_, _) => { bd.Background = danger ? Brush(0xE5, 0x79, 0x6F, 0.16) : Brush(0xFF, 0xFF, 0xFF, 0.08); if (!danger) icon.Stroke = Brush(0xF5, 0xF6, 0xF8, 1); };
        button.MouseLeave += (_, _) => { bd.Background = Brushes.Transparent; icon.Stroke = danger ? Brush(0xE5, 0x79, 0x6F, 1) : Brush(0xA2, 0xA7, 0xB0, 1); };
        return button;
    }

    // ------------------------------------------------------------------ panel interaction

    private void ApplySearch(string query)
    {
        query = query.Trim();
        foreach (RowVisual row in _panelRows)
        {
            bool match = query.Length == 0 ||
                         row.Entry.Primary.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                         row.Entry.Secondary.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                         row.Entry.SearchText.Contains(query, StringComparison.OrdinalIgnoreCase);
            row.Root.Visibility = match ? Visibility.Visible : Visibility.Collapsed;
        }

        UpdateSelection(FirstVisibleRow());
    }

    private void HandlePanelKey(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down:
                MovePanelSelection(1);
                e.Handled = true;
                break;
            case Key.Up:
                MovePanelSelection(-1);
                e.Handled = true;
                break;
            case Key.Enter:
                if (_panelSelection >= 0 && _panelSelection < _panelRows.Count)
                {
                    _panelRows[_panelSelection].Entry.OnActivate?.Invoke();
                    e.Handled = true;
                }

                break;
        }
    }

    private void MovePanelSelection(int direction)
    {
        List<int> visible = VisibleRowIndexes();
        if (visible.Count == 0) return;

        int pos = visible.IndexOf(_panelSelection);
        int nextPos = pos < 0 ? (direction > 0 ? 0 : visible.Count - 1) : (pos + direction + visible.Count) % visible.Count;
        UpdateSelection(visible[nextPos]);
        _panelRows[_panelSelection].Root.BringIntoView();
    }

    private void UpdateSelection(int index)
    {
        for (int i = 0; i < _panelRows.Count; i++)
        {
            bool selected = i == index;
            _panelRows[i].Root.Background = selected ? RowSelectedBrush : Brushes.Transparent;
            if (_panelRows[i].Root.Child is Grid g && g.Children.Count > 0)
            {
                foreach (object child in g.Children)
                {
                    if (child is StackPanel sp && sp.Orientation == Orientation.Horizontal)
                    {
                        sp.Opacity = selected ? 1 : 0;
                    }
                }
            }
        }

        _panelSelection = index;
    }

    private void SelectRow(Border row)
    {
        int index = _panelRows.FindIndex(r => ReferenceEquals(r.Root, row));
        if (index >= 0) UpdateSelection(index);
    }

    private bool IsRowSelected(Border row) =>
        _panelSelection >= 0 && _panelSelection < _panelRows.Count && ReferenceEquals(_panelRows[_panelSelection].Root, row);

    private int FirstVisibleRow()
    {
        for (int i = 0; i < _panelRows.Count; i++)
        {
            if (_panelRows[i].IsVisible) return i;
        }

        return -1;
    }

    private List<int> VisibleRowIndexes()
    {
        var list = new List<int>();
        for (int i = 0; i < _panelRows.Count; i++)
        {
            if (_panelRows[i].IsVisible) list.Add(i);
        }

        return list;
    }

    private static bool IsWithin(DependencyObject? source, DependencyObject ancestor)
    {
        for (DependencyObject? current = source; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (ReferenceEquals(current, ancestor)) return true;
        }

        return false;
    }

    private static SolidColorBrush Freeze(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
