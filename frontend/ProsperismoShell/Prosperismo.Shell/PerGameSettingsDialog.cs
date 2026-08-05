// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace SharpEmu.GUI;

public sealed class PerGameSettingsDialog : Window
{
    private static readonly string[] LogLevels =
        { "Trace", "Debug", "Info", "Warning", "Error", "Critical" };

    private static readonly string[] EnvToggles =
    {
        "SHARPEMU_BTHID_UNAVAILABLE",
        "SHARPEMU_DISABLE_IMPORT_LOOP_GUARD",
        "SHARPEMU_WRITABLE_APP0",
        "SHARPEMU_VK_VALIDATION",
        "SHARPEMU_DUMP_SPIRV",
        "SHARPEMU_LOG_DIRECT_MEMORY",
        "SHARPEMU_LOG_IO",
        "SHARPEMU_LOG_NP",
    };

    private readonly string _titleId;
    private readonly Border _surface;
    private bool _hideStarted;

    private readonly SettingRow _logLevelRow;
    private readonly ComboBox _logLevel = new() { ItemsSource = LogLevels, Width = 160 };

    private readonly SettingRow _traceRow;
    private readonly NumericUpDown _trace = new()
    {
        Minimum = 0, Maximum = 4096, Increment = 16, Width = 160, FormatString = "0",
    };

    private readonly SettingRow _strictRow;
    private readonly ToggleSwitch _strict = new();

    private readonly SettingRow _logToFileRow;
    private readonly ToggleSwitch _logToFile = new();

    private readonly SettingRow _envRow;
    private readonly StackPanel _envList = new() { Orientation = Orientation.Vertical, Spacing = 8, Margin = new(0, 4, 0, 0) };
    private readonly List<(string Name, ToggleSwitch Box)> _envBoxes = new();

    public PerGameSettingsDialog(string titleId, string displayName, GuiSettings global)
    {
        _titleId = titleId;
        var loc = Localization.Instance;

        Title = loc.Format("PerGame.Title", displayName, titleId);
        Width = 520;
        MaxHeight = 720;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;

        // The shell darkens to the flat home basemat behind a modal instead of
        // scrimming the live scene.
        Background = new SolidColorBrush(Color.Parse("#020408"));

        _strict.OnContent = _logToFile.OnContent = loc.Get("Common.On");
        _strict.OffContent = _logToFile.OffContent = loc.Get("Common.Off");

        _logLevelRow = Row(loc.Get("Options.LogLevel.Label"), loc.Get("Options.LogLevel.Desc"), _logLevel);
        _traceRow = Row(loc.Get("Options.TraceImports.Label"), loc.Get("Options.TraceImports.Desc"), _trace);
        _strictRow = Row(loc.Get("Options.Strict.Label"), loc.Get("Options.Strict.Desc"), _strict);
        _logToFileRow = Row(loc.Get("Options.LogToFile.Label"), loc.Get("Options.LogToFile.Desc"), _logToFile);
        _envRow = new SettingRow
        {
            Label = loc.Get("PerGame.EnvToggles.Label"),
            Description = loc.Get("PerGame.EnvToggles.Desc"),
            ShowOverride = true,
        };

        foreach (var name in EnvToggles)
        {
            var box = new ToggleSwitch { OnContent = name, OffContent = name };
            _envBoxes.Add((name, box));
            _envList.Children.Add(box);
        }

        var content = new StackPanel { Orientation = Orientation.Vertical, Spacing = 12, Margin = new(16) };
        content.Children.Add(new TextBlock
        {
            Text = loc.Get("PerGame.InheritNote"),
            // Secondary text, white at 70%. Avalonia reads 8-digit hex as
            // #AARRGGBB, so the alpha leads.
            Foreground = new SolidColorBrush(Color.Parse("#B3FFFFFF")),
            FontSize = 12,
        });
        content.Children.Add(Card(loc.Get("Options.Section.Emulation"), _strictRow));
        content.Children.Add(Card(loc.Get("Options.Section.Logging"), _logLevelRow, _traceRow, _logToFileRow));
        content.Children.Add(Card(loc.Get("Options.Section.Environment"), _envRow, _envList));

        var save = new Button { Content = loc.Get("Common.Save"), Classes = { "accent" } };
        var cancel = new Button { Content = loc.Get("Common.Cancel"), Classes = { "ghost" } };
        save.Click += (_, _) => { Persist(); Close(); };
        cancel.Click += (_, _) => Close();

        var buttonBar = new Border
        {
            // Weak divider, white at 10%.
            BorderBrush = new SolidColorBrush(Color.Parse("#1AFFFFFF")),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new(16),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                HorizontalAlignment = HorizontalAlignment.Right,
                Children = { cancel, save },
            },
        };

        var root = new Grid { RowDefinitions = new RowDefinitions("*,Auto") };
        var scroller = new ScrollViewer { Content = content };
        Grid.SetRow(scroller, 0);
        Grid.SetRow(buttonBar, 1);
        root.Children.Add(scroller);
        root.Children.Add(buttonBar);

        // The dialog body is a shell modal card: action-card surface, the weak
        // white hairline, BORDER_RADIUS 16.
        _surface = new Border { Classes = { "ps5Card" }, Margin = new Thickness(12), Child = root };
        Content = _surface;

        Opened += (_, _) => _ = ShellMotion.ShowSurfaceAsync(_surface);
        Closing += OnSurfaceClosing;

        LoadValues(global);
        _envRow.PropertyChanged += (_, e) =>
        {
            if (e.Property == SettingRow.IsOverriddenProperty)
            {
                _envList.IsEnabled = _envRow.IsOverridden;
            }
        };
        _envList.IsEnabled = _envRow.IsOverridden;
    }

    /// <summary>
    /// Holds the window open for the shell's modal hide (300 ms, linear) and
    /// then lets the close through. Shutdown paths close immediately.
    /// </summary>
    private void OnSurfaceClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_hideStarted || e.CloseReason != WindowCloseReason.WindowClosing)
        {
            return;
        }

        e.Cancel = true;
        _hideStarted = true;
        _ = HideThenCloseAsync();
    }

    private async Task HideThenCloseAsync()
    {
        await ShellMotion.HideSurfaceAsync(_surface);
        Close();
    }

    private static SettingRow Row(string label, string description, Control value) => new()
    {
        Label = label,
        Description = description,
        ShowOverride = true,
        Content = value,
    };

    private static Border Card(string title, params Control[] rows)
    {
        var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 14 };
        stack.Children.Add(new TextBlock { Text = title, Classes = { "sectionTitle" } });
        foreach (var row in rows)
        {
            stack.Children.Add(row);
        }

        var card = new Border { Child = stack };
        card.Classes.Add("card");
        return card;
    }

    private void LoadValues(GuiSettings global)
    {
        _logLevel.SelectedItem = Array.IndexOf(LogLevels, global.LogLevel) >= 0 ? global.LogLevel : "Info";
        _trace.Value = global.ImportTraceLimit;
        _strict.IsChecked = global.StrictDynlibResolution;
        _logToFile.IsChecked = global.LogToFile;
        foreach (var (name, box) in _envBoxes)
        {
            box.IsChecked = global.EnvironmentToggles.Contains(name);
        }

        var existing = PerGameSettings.Load(_titleId);
        if (existing is null)
        {
            return;
        }

        if (existing.LogLevel is { } level && Array.IndexOf(LogLevels, level) >= 0)
        {
            _logLevelRow.IsOverridden = true;
            _logLevel.SelectedItem = level;
        }

        if (existing.ImportTraceLimit is { } t) { _traceRow.IsOverridden = true; _trace.Value = t; }
        if (existing.StrictDynlibResolution is { } s) { _strictRow.IsOverridden = true; _strict.IsChecked = s; }
        if (existing.LogToFile is { } l) { _logToFileRow.IsOverridden = true; _logToFile.IsChecked = l; }
        if (existing.EnvironmentToggles is { } env)
        {
            _envRow.IsOverridden = true;
            foreach (var (name, box) in _envBoxes)
            {
                box.IsChecked = env.Contains(name);
            }
        }
    }

    private void Persist()
    {
        var settings = new PerGameSettings
        {
            LogLevel = _logLevelRow.IsOverridden ? _logLevel.SelectedItem as string : null,
            ImportTraceLimit = _traceRow.IsOverridden ? (int)(_trace.Value ?? 0) : null,
            StrictDynlibResolution = _strictRow.IsOverridden ? _strict.IsChecked == true : null,
            LogToFile = _logToFileRow.IsOverridden ? _logToFile.IsChecked == true : null,
            EnvironmentToggles = _envRow.IsOverridden
                ? _envBoxes.Where(e => e.Box.IsChecked == true).Select(e => e.Name).ToList()
                : null,
        };
        settings.Save(_titleId);
    }
}
