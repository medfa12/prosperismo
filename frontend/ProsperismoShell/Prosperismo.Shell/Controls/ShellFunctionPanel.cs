// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace SharpEmu.GUI.Controls;

/// <summary>
/// Geometry of the function-control panel, the flyout a nav-band icon opens.
///
/// Recovered from three stylesheets: the panel box and its anchor from HOME
/// m143, the header from m156, and the list rows from m155.
///
/// <code>
/// FCFocusLayer: { marginTop: 126, marginLeft: 1188 }
/// FCContainer:  { position: "absolute", width: 652,
///                 minHeight: 216, maxHeight: 810, borderRadius: 16 }
/// header:       { height: 80, flexDirection: "row", padding: 24, opacity: .7 }
/// headerIcon:   { width: 48, height: 48, borderRadius: 8 }
/// listItem:     { flexDirection: "row", minHeight: 98, alignItems: "center" }
/// rightIcon:    { width: 48, height: 48 }   marginHorizontal 16
/// </code>
///
/// The anchor is absolute, not relative to whichever icon opened the panel: the
/// console drops every function control at the same x, immediately below the
/// 126 px nav band.
/// </summary>
public static class ShellFunctionPanelMetrics
{
    /// <summary><c>FCFocusLayer.marginLeft</c>.</summary>
    public const double AnchorX = 1188.0;

    /// <summary><c>FCFocusLayer.marginTop</c>, which is SYSTEM_HEIGHT.</summary>
    public const double AnchorY = 126.0;

    /// <summary><c>FCContainer.width</c>.</summary>
    public const double Width = 652.0;

    /// <summary><c>FCContainer.minHeight</c>.</summary>
    public const double MinHeight = 216.0;

    /// <summary><c>FCContainer.maxHeight</c>.</summary>
    public const double MaxHeight = 810.0;

    /// <summary><c>FCContainer.borderRadius</c>.</summary>
    public const double CornerRadius = 16.0;

    /// <summary><c>header.height</c>.</summary>
    public const double HeaderHeight = 80.0;

    /// <summary><c>header.padding</c>.</summary>
    public const double HeaderPadding = 24.0;

    /// <summary><c>header.opacity</c>. The header is deliberately quieter than
    /// the rows under it.</summary>
    public const double HeaderOpacity = 0.7;

    /// <summary><c>headerIcon</c> and <c>rightIcon</c> are both 48 square.</summary>
    public const double IconSize = 48.0;

    /// <summary><c>headerIcon.borderRadius</c>.</summary>
    public const double HeaderIconRadius = 8.0;

    /// <summary><c>headerIconContainer.marginRight</c>.</summary>
    public const double HeaderIconMarginRight = 16.0;

    /// <summary><c>listItem.minHeight</c>. A row is at least this tall and
    /// grows with its content.</summary>
    public const double ListItemMinHeight = 98.0;

    /// <summary><c>rightIconContainer.marginHorizontal</c>.</summary>
    public const double RightIconMarginHorizontal = 16.0;

    /// <summary><c>leftIcon.marginTop</c>, with <c>alignSelf: "flex-start"</c>
    /// so a left icon hangs from the top of its row rather than centring.</summary>
    public const double LeftIconMarginTop = 21.0;

    /// <summary><c>menuListItemButtonProfileContainer.height</c>.</summary>
    public const double ProfileRowHeight = 90.0;

    /// <summary><c>menuListItemButtonProfileContainer.marginBottom</c>.</summary>
    public const double ProfileRowMarginBottom = 2.0;

    /// <summary>
    /// Height the panel settles at for <paramref name="rowCount"/> rows, held
    /// between the source's own minimum and maximum.
    /// </summary>
    public static double HeightFor(int rowCount)
    {
        double content = HeaderHeight + (Math.Max(0, rowCount) * ListItemMinHeight);
        return Math.Clamp(content, MinHeight, MaxHeight);
    }

    /// <summary>True once the rows overflow the panel and it has to scroll.</summary>
    public static bool Scrolls(int rowCount) =>
        HeaderHeight + (Math.Max(0, rowCount) * ListItemMinHeight) > MaxHeight;
}

/// <summary>One row in a <see cref="ShellFunctionPanel"/>.</summary>
public sealed record ShellFunctionPanelItem
{
    public ShellFunctionPanelItem(string title, string? glyph = null, object? tag = null)
    {
        Title = title ?? string.Empty;
        Glyph = glyph;
        Tag = tag;
    }

    /// <summary>The row's label.</summary>
    public string Title { get; init; }

    /// <summary>Mark drawn in the row's trailing 48 px icon box, if any.</summary>
    public string? Glyph { get; init; }

    /// <summary>Caller payload round-tripped through the panel's events.</summary>
    public object? Tag { get; init; }

    /// <summary>Whether the row can be chosen.</summary>
    public bool IsEnabled { get; init; } = true;
}

/// <summary>Payload for <see cref="ShellFunctionPanel"/>'s events.</summary>
public sealed class ShellFunctionPanelEventArgs : EventArgs
{
    public ShellFunctionPanelEventArgs(int index, ShellFunctionPanelItem? item)
    {
        Index = index;
        Item = item;
    }

    /// <summary>Focused row, or -1 when the panel is empty.</summary>
    public int Index { get; }

    /// <summary>The focused row, or null when the panel is empty.</summary>
    public ShellFunctionPanelItem? Item { get; }
}

/// <summary>
/// The function-control panel: the flyout a nav-band icon opens, drawn to the
/// console's own geometry (<see cref="ShellFunctionPanelMetrics"/>).
///
/// It is code-templated so it can be dropped into a window or a preview without
/// an external theme, and its navigation state is independent of the render
/// surface so it stays testable headless.
/// </summary>
public sealed class ShellFunctionPanel : TemplatedControl
{
    /// <summary>Panel fill. The shell's own dialog plate colour.</summary>
    private static readonly IBrush PlateBrush =
        new SolidColorBrush(Color.FromRgb(0x08, 0x0A, 0x0F));

    private static readonly IBrush TextBrush = Brushes.White;

    private static readonly IBrush RowHighlightBrush =
        new SolidColorBrush(Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF));

    public static readonly StyledProperty<IReadOnlyList<ShellFunctionPanelItem>?> ItemsProperty =
        AvaloniaProperty.Register<ShellFunctionPanel, IReadOnlyList<ShellFunctionPanelItem>?>(nameof(Items));

    public static readonly StyledProperty<string?> HeaderProperty =
        AvaloniaProperty.Register<ShellFunctionPanel, string?>(nameof(Header));

    public static readonly StyledProperty<int> SelectedIndexProperty =
        AvaloniaProperty.Register<ShellFunctionPanel, int>(nameof(SelectedIndex), -1);

    /// <summary>Authored pixels to host pixels, for a scaled surface.</summary>
    public static readonly StyledProperty<double> ScaleProperty =
        AvaloniaProperty.Register<ShellFunctionPanel, double>(nameof(Scale), 1.0);

    private StackPanel? _rowHost;
    private TextBlock? _headerText;
    private Border? _header;
    private readonly List<Border> _rows = new();

    public ShellFunctionPanel()
    {
        Focusable = true;
        Template = BuildTemplate();
    }

    /// <summary>Raised when the focused row changes.</summary>
    public event EventHandler<ShellFunctionPanelEventArgs>? SelectionChanged;

    /// <summary>Raised when a row is activated.</summary>
    public event EventHandler<ShellFunctionPanelEventArgs>? ItemActivated;

    public IReadOnlyList<ShellFunctionPanelItem>? Items
    {
        get => GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    public string? Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public int SelectedIndex
    {
        get => GetValue(SelectedIndexProperty);
        set => SetValue(SelectedIndexProperty, value);
    }

    public double Scale
    {
        get => GetValue(ScaleProperty);
        set => SetValue(ScaleProperty, value);
    }

    /// <summary>Rows currently held.</summary>
    public int Count => Items?.Count ?? 0;

    /// <summary>The focused row, or null.</summary>
    public ShellFunctionPanelItem? SelectedItem =>
        Items is { } items && SelectedIndex >= 0 && SelectedIndex < items.Count
            ? items[SelectedIndex]
            : null;

    /// <summary>Settled height at the current row count, in authored pixels.</summary>
    public double PanelHeight => ShellFunctionPanelMetrics.HeightFor(Count);

    /// <summary>Moves the focus by <paramref name="delta"/> rows, without wrapping.</summary>
    public void MoveFocus(int delta)
    {
        if (Count == 0)
        {
            return;
        }

        int next = Math.Clamp(SelectedIndex + delta, 0, Count - 1);
        SetSelectedIndex(next);
    }

    /// <summary>Focuses a row, clamped to the panel's range.</summary>
    public void SetSelectedIndex(int index)
    {
        if (Count == 0)
        {
            SetCurrentValue(SelectedIndexProperty, -1);
            return;
        }

        SetCurrentValue(SelectedIndexProperty, Math.Clamp(index, 0, Count - 1));
    }

    /// <summary>Activates the focused row.</summary>
    public void ActivateSelected()
    {
        if (SelectedItem is { IsEnabled: true } item)
        {
            ItemActivated?.Invoke(this, new ShellFunctionPanelEventArgs(SelectedIndex, item));
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ItemsProperty)
        {
            if (Count > 0 && SelectedIndex < 0)
            {
                SetCurrentValue(SelectedIndexProperty, 0);
            }
            else if (Count == 0)
            {
                SetCurrentValue(SelectedIndexProperty, -1);
            }

            Rebuild();
        }
        else if (change.Property == SelectedIndexProperty)
        {
            UpdateRowVisuals();
            SelectionChanged?.Invoke(this, new ShellFunctionPanelEventArgs(SelectedIndex, SelectedItem));
        }
        else if (change.Property == ScaleProperty || change.Property == HeaderProperty)
        {
            Rebuild();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Up:
                MoveFocus(-1);
                e.Handled = true;
                return;
            case Key.Down:
                MoveFocus(1);
                e.Handled = true;
                return;
            case Key.Enter:
                ActivateSelected();
                e.Handled = true;
                return;
            default:
                base.OnKeyDown(e);
                return;
        }
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _rowHost = e.NameScope.Find<StackPanel>("PART_Rows");
        _headerText = e.NameScope.Find<TextBlock>("PART_HeaderText");
        _header = e.NameScope.Find<Border>("PART_Header");
        Rebuild();
    }

    private FuncControlTemplate BuildTemplate() => new((_, ns) =>
    {
        var header = new Border
        {
            Name = "PART_Header",
            Height = ShellFunctionPanelMetrics.HeaderHeight,
            Padding = new Thickness(ShellFunctionPanelMetrics.HeaderPadding),
            Opacity = ShellFunctionPanelMetrics.HeaderOpacity,
        };
        header.RegisterInNameScope(ns);

        var headerText = new TextBlock
        {
            Name = "PART_HeaderText",
            Foreground = TextBrush,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        headerText.RegisterInNameScope(ns);
        header.Child = headerText;

        var rows = new StackPanel
        {
            Name = "PART_Rows",
            Orientation = Orientation.Vertical,
        };
        rows.RegisterInNameScope(ns);

        var stack = new StackPanel { Orientation = Orientation.Vertical };
        stack.Children.Add(header);
        stack.Children.Add(rows);

        var scroll = new ScrollViewer
        {
            Content = stack,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
        };

        var plate = new Border
        {
            Name = "PART_Plate",
            Background = PlateBrush,
            CornerRadius = new CornerRadius(ShellFunctionPanelMetrics.CornerRadius),
            ClipToBounds = true,
            Child = scroll,
        };
        plate.RegisterInNameScope(ns);
        return plate;
    });

    private void Rebuild()
    {
        if (_rowHost is null)
        {
            return;
        }

        double scale = Scale > 0 ? Scale : 1.0;

        Width = ShellFunctionPanelMetrics.Width * scale;
        MinHeight = ShellFunctionPanelMetrics.MinHeight * scale;
        MaxHeight = ShellFunctionPanelMetrics.MaxHeight * scale;

        if (_headerText is not null)
        {
            _headerText.Text = Header ?? string.Empty;
        }

        if (_header is not null)
        {
            // A panel with no header still reserves nothing: the source only
            // renders the header block when there is one to render.
            _header.IsVisible = !string.IsNullOrEmpty(Header);
            _header.Height = ShellFunctionPanelMetrics.HeaderHeight * scale;
            _header.Padding = new Thickness(ShellFunctionPanelMetrics.HeaderPadding * scale);
        }

        _rowHost.Children.Clear();
        _rows.Clear();

        var items = Items;
        if (items is null)
        {
            return;
        }

        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];

            var label = new TextBlock
            {
                Text = item.Title,
                Foreground = TextBrush,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(ShellFunctionPanelMetrics.HeaderPadding * scale, 0, 0, 0),
                Opacity = item.IsEnabled ? 1.0 : 0.4,
            };

            var iconBox = new Border
            {
                Width = ShellFunctionPanelMetrics.IconSize * scale,
                Height = ShellFunctionPanelMetrics.IconSize * scale,
                Margin = new Thickness(ShellFunctionPanelMetrics.RightIconMarginHorizontal * scale, 0),
                Child = item.Glyph is null
                    ? null
                    : new TextBlock
                    {
                        Text = item.Glyph,
                        Foreground = TextBrush,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                    },
            };

            var content = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            Grid.SetColumn(label, 0);
            content.Children.Add(label);
            Grid.SetColumn(iconBox, 1);
            content.Children.Add(iconBox);

            var row = new Border
            {
                MinHeight = ShellFunctionPanelMetrics.ListItemMinHeight * scale,
                Child = content,
            };

            int captured = i;
            row.PointerEntered += (_, _) => SetSelectedIndex(captured);
            row.PointerPressed += (_, _) =>
            {
                SetSelectedIndex(captured);
                ActivateSelected();
            };

            _rows.Add(row);
            _rowHost.Children.Add(row);
        }

        UpdateRowVisuals();
    }

    private void UpdateRowVisuals()
    {
        for (int i = 0; i < _rows.Count; i++)
        {
            _rows[i].Background = i == SelectedIndex ? RowHighlightBrush : Brushes.Transparent;
        }
    }
}
