// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using SharpEmu.GUI.Ps5Home;

namespace SharpEmu.GUI.Controls;

public sealed record ShellSettingsDetailRow(string ItemId, string Label, string? Value = null);

public sealed record ShellSettingsDetailTab(
    string TabId,
    string Label,
    string TestId,
    bool IsSony,
    IReadOnlyList<ShellSettingsDetailRow> Rows);

/// <summary>
/// Prosperismo settings hosted by the recovered NPXS40008 vertical-tab and
/// SettingsList presentation. The behavior and geometry come from Sony's UI;
/// every setting exposed here belongs to the emulator.
/// </summary>
public sealed class ShellSettingsDetailList : Panel
{
    // Native TabViewPS owns the "wide" centre gap. The JS exposes 96 as its
    // panel-left constant but not the final absolute placement semantics. Keep
    // this provisional composition seam named honestly until TabViewPS runs.
    public const double ProvisionalContentLeft =
        ShellSettingsMetrics.TabLeft + ShellSettingsMetrics.TabWidth + ShellSettingsMetrics.TabPanelLeft;

    // MenuListItemPS owns this geometry natively. This is only the current
    // navigation/render pitch and is deliberately not published as Sony data.
    private const double DiagnosticRowPitch = 112;
    // TabViewPS owns this metric. The 980x552 retail System frame measures
    // adjacent tab baselines 55-56 px apart: 110 design units at 1920x1080.
    public const double CapturedTabPitch = 110;
    private const int VisibleRows = 8;

    private static readonly IBrush SeparatorBrush =
        new SolidColorBrush(Color.FromArgb(18, 255, 255, 255));

    public static IReadOnlyList<ShellSettingsDetailTab> Tabs { get; } =
    [
        new(
            "id_prosperismo_general",
            "General",
            "tab-general",
            false,
            [
                new("id_language", "Language", "English"),
                new("id_discord_presence", "Discord Presence"),
                new("id_auto_update", "Check for Updates on Startup"),
            ]),
        new(
            "id_prosperismo_graphics",
            "Graphics",
            "tab-graphics",
            false,
            [
                new("id_internal_resolution", "Internal Resolution", "100%"),
            ]),
        new(
            "id_prosperismo_audio_ui",
            "Audio and Interface",
            "tab-audio-ui",
            false,
            [
                new("id_title_music", "Title Music"),
                new("id_shell_motion", "Background Motion"),
                new("id_ui_sounds", "UI Sounds"),
                new("id_shell_music", "Home Music"),
                new("id_boot_intro", "Boot Animation"),
            ]),
        new(
            "id_prosperismo_emulation",
            "Emulation",
            "tab-emulation",
            false,
            [
                new("id_cpu_engine", "CPU Engine", "Native"),
                new("id_strict_dynlib", "Strict Dynamic Library Resolution"),
            ]),
        new(
            "id_prosperismo_logging",
            "Logging",
            "tab-logging",
            false,
            [
                new("id_log_level", "Log Level", "Info"),
                new("id_trace_imports", "Import Trace Limit", "0"),
                new("id_log_to_file", "Log to File"),
                new("id_log_file_path", "Log File Path", "Default"),
                new("id_override_log_file", "Use Exact Log File Path"),
            ]),
        new(
            "id_prosperismo_environment",
            "Environment",
            "tab-environment",
            false,
            [
                new("env_bthid", "Bluetooth HID Unavailable"),
                new("env_loop_guard", "Disable Import Loop Guard"),
                new("env_writable_app0", "Writable App0"),
                new("env_vk_validation", "Vulkan Validation"),
                new("env_dump_spirv", "Dump SPIR-V"),
                new("env_log_direct_memory", "Log Direct Memory"),
                new("env_log_io", "Log File I/O"),
                new("env_log_np", "Log Network Platform Calls"),
            ]),
        new(
            "id_prosperismo_about",
            "About Prosperismo",
            "tab-about",
            false,
            [
                new("id_build", "Current Build"),
                new("id_check_updates", "Check for Updates"),
                new("id_github", "GitHub"),
                new("id_discord", "Discord Community"),
            ]),
    ];

    private readonly Canvas _tabs = new();
    private readonly Canvas _rows = new();
    private readonly Dictionary<int, Control> _visibleRows = new();
    private readonly TextBlock _heading;
    private int _selectedTab;
    private int _selectedRow;
    private int _firstVisibleRow;
    private bool _rowsHaveFocus;
    private double _renderResolutionScale = 1;
    private bool _playTitleMusic = true;
    private bool _animateShellBackground = true;
    private bool _playUiSounds = true;
    private bool _playShellMusic = true;
    private bool _playBootIntro = true;
    private bool _discordPresence = true;
    private bool _checkUpdates = true;
    private bool _strictDynlib;
    private bool _logToFile;
    private bool _overrideLogFile;
    private readonly HashSet<string> _enabledEnvironmentRows = new(StringComparer.Ordinal);
    private string _languageName = "English";
    private string _logLevel = "Info";
    private int _importTraceLimit;
    private string _logFilePath = "Default";

    public ShellSettingsDetailList()
    {
        Width = Ps5DesignSpace.Width;
        Height = Ps5DesignSpace.Height;
        Background = Brushes.Transparent;
        Focusable = true;
        ClipToBounds = true;

        _heading = new TextBlock
        {
            Text = Tabs[0].Label,
            FontSize = Ps5FontScale.SizeLarge,
            Foreground = Brushes.White,
            Margin = new Thickness(96, 82, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        Children.Add(_heading);

        _tabs.Width = ShellSettingsMetrics.TabWidth;
        _tabs.Height = ShellSettingsMetrics.TabPanelHeight;
        _tabs.Margin = new Thickness(ShellSettingsMetrics.TabLeft, ShellSettingsMetrics.TabTop, 0, 0);
        _tabs.HorizontalAlignment = HorizontalAlignment.Left;
        _tabs.VerticalAlignment = VerticalAlignment.Top;
        Children.Add(_tabs);

        _rows.Width = ShellSettingsMetrics.TabPanelWidth;
        _rows.Height = ShellSettingsMetrics.TabPanelHeight;
        _rows.ClipToBounds = true;
        _rows.Margin = new Thickness(ProvisionalContentLeft, ShellSettingsMetrics.TabTop, 0, 0);
        _rows.HorizontalAlignment = HorizontalAlignment.Left;
        _rows.VerticalAlignment = VerticalAlignment.Top;
        Children.Add(_rows);

        GotFocus += (_, _) => QueueFocusRect();
        LostFocus += (_, _) => ShellFocusRing.For(this)?.Release(this);
        AttachedToVisualTree += (_, _) => QueueFocusRect();
        Rebuild();
    }

    public event EventHandler? BackRequested;
    public event EventHandler? RenderResolutionScaleChanged;
    public event EventHandler? EmulatorSettingChanged;
    public event EventHandler? LanguageCycleRequested;
    public event EventHandler? LogFilePathRequested;
    public event Action<string>? ActionRequested;

    public int SelectedTabIndex
    {
        get => _selectedTab;
        set
        {
            SetSelectedTab(value);
            // TabbedList module 73 sets _isSetFocusOnPanel for an initial tab
            // and calls TabViewPS.setFocusOnPanel() after panel mount.
            _rowsHaveFocus = true;
            QueueFocusRect();
        }
    }

    /// <summary>Whether TabViewPS-equivalent focus is in the content panel.</summary>
    internal bool IsPanelFocused => _rowsHaveFocus;

    /// <summary>The focused content row, exposed for navigation verification.</summary>
    internal int SelectedRowIndex => _selectedRow;

    public double RenderResolutionScale
    {
        get => _renderResolutionScale;
        set
        {
            _renderResolutionScale = NearestScale(value);
            RebuildRows();
        }
    }

    public bool IsShowingSonyTab => Tabs[_selectedTab].IsSony;

    public bool PlayTitleMusic { get => _playTitleMusic; set => SetOption(ref _playTitleMusic, value); }
    public bool AnimateShellBackground { get => _animateShellBackground; set => SetOption(ref _animateShellBackground, value); }
    public bool PlayUiSounds { get => _playUiSounds; set => SetOption(ref _playUiSounds, value); }
    public bool PlayShellMusic { get => _playShellMusic; set => SetOption(ref _playShellMusic, value); }
    public bool PlayBootIntro { get => _playBootIntro; set => SetOption(ref _playBootIntro, value); }
    public bool DiscordPresence { get => _discordPresence; set => SetOption(ref _discordPresence, value); }
    public bool CheckUpdates { get => _checkUpdates; set => SetOption(ref _checkUpdates, value); }
    public bool StrictDynlib { get => _strictDynlib; set => SetOption(ref _strictDynlib, value); }
    public bool LogToFile { get => _logToFile; set => SetOption(ref _logToFile, value); }
    public bool OverrideLogFile { get => _overrideLogFile; set => SetOption(ref _overrideLogFile, value); }
    public string LanguageName { get => _languageName; set => SetText(ref _languageName, value); }
    public string LogLevel { get => _logLevel; set => SetText(ref _logLevel, value); }
    public int ImportTraceLimit { get => _importTraceLimit; set { _importTraceLimit = Math.Clamp(value, 0, 4096); RebuildRows(); } }
    public string LogFilePath { get => _logFilePath; set => SetText(ref _logFilePath, value); }

    public bool IsEnvironmentEnabled(string itemId) => _enabledEnvironmentRows.Contains(itemId);

    public void SetEnvironmentEnabled(string itemId, bool enabled)
    {
        if (enabled)
        {
            _enabledEnvironmentRows.Add(itemId);
        }
        else
        {
            _enabledEnvironmentRows.Remove(itemId);
        }
        RebuildRows();
    }

    public static double CycleScale(double scale, int direction)
    {
        double[] scales = [1, .75, .5, .25];
        var current = Array.IndexOf(scales, NearestScale(scale));
        return scales[(current + (direction >= 0 ? 1 : scales.Length - 1)) % scales.Length];
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        switch (e.Key)
        {
            case Key.Escape:
            case Key.Back:
                RequestBack();
                e.Handled = true;
                return;
            case Key.Left when _rowsHaveFocus:
                MoveHorizontal(-1);
                e.Handled = true;
                return;
            case Key.Right when !_rowsHaveFocus:
                MoveHorizontal(1);
                e.Handled = true;
                return;
            case Key.Up:
                MoveVertical(-1);
                e.Handled = true;
                return;
            case Key.Down:
                MoveVertical(1);
                e.Handled = true;
                return;
            case Key.Enter:
            case Key.Space:
                ActivateSelected();
                e.Handled = true;
                return;
        }
    }

    /// <summary>Moves within the active TabViewPS column.</summary>
    public void MoveVertical(int delta)
    {
        MoveSelection(delta);
    }

    /// <summary>
    /// Moves between TabViewPS's tab column and mounted content panel. Edges
    /// clamp: left in the tab column and right in the panel are no-ops.
    /// </summary>
    public void MoveHorizontal(int direction)
    {
        if (direction < 0 && _rowsHaveFocus)
        {
            _rowsHaveFocus = false;
            QueueFocusRect();
        }
        else if (direction > 0 && !_rowsHaveFocus)
        {
            _rowsHaveFocus = true;
            QueueFocusRect();
        }
    }

    /// <summary>Activates the focused row, or enters the mounted panel.</summary>
    public void ActivateSelected()
    {
        if (_rowsHaveFocus)
        {
            ActivateSelectedRow();
        }
        else
        {
            _rowsHaveFocus = true;
            QueueFocusRect();
        }
    }

    /// <summary>Requests the route-stack back action.</summary>
    public void RequestBack() => BackRequested?.Invoke(this, EventArgs.Empty);

    private void MoveSelection(int delta)
    {
        if (_rowsHaveFocus)
        {
            SetSelectedRow(_selectedRow + Math.Sign(delta));
        }
        else
        {
            SetSelectedTab(_selectedTab + Math.Sign(delta));
        }
        QueueFocusRect();
    }

    private void SetScale(double scale)
    {
        _renderResolutionScale = scale;
        RebuildRows();
        RenderResolutionScaleChanged?.Invoke(this, EventArgs.Empty);
        QueueFocusRect();
    }

    private void ActivateSelectedRow()
    {
        switch (Tabs[_selectedTab].Rows[_selectedRow].ItemId)
        {
            case "id_internal_resolution":
                SetScale(CycleScale(_renderResolutionScale, 1));
                return;
            case "id_title_music":
                _playTitleMusic = !_playTitleMusic;
                break;
            case "id_shell_motion":
                _animateShellBackground = !_animateShellBackground;
                break;
            case "id_ui_sounds":
                _playUiSounds = !_playUiSounds;
                break;
            case "id_shell_music":
                _playShellMusic = !_playShellMusic;
                break;
            case "id_boot_intro":
                _playBootIntro = !_playBootIntro;
                break;
            case "id_discord_presence":
                _discordPresence = !_discordPresence;
                break;
            case "id_auto_update":
                _checkUpdates = !_checkUpdates;
                break;
            case "id_strict_dynlib":
                _strictDynlib = !_strictDynlib;
                break;
            case "id_log_to_file":
                _logToFile = !_logToFile;
                break;
            case "id_override_log_file":
                _overrideLogFile = !_overrideLogFile;
                break;
            case "id_language":
                LanguageCycleRequested?.Invoke(this, EventArgs.Empty);
                return;
            case "id_log_level":
                _logLevel = CycleLogLevel(_logLevel);
                break;
            case "id_trace_imports":
                _importTraceLimit = CycleImportTraceLimit(_importTraceLimit);
                break;
            case "id_log_file_path":
                LogFilePathRequested?.Invoke(this, EventArgs.Empty);
                return;
            case "id_check_updates":
            case "id_github":
            case "id_discord":
                ActionRequested?.Invoke(Tabs[_selectedTab].Rows[_selectedRow].ItemId);
                return;
            default:
                var itemId = Tabs[_selectedTab].Rows[_selectedRow].ItemId;
                if (!itemId.StartsWith("env_", StringComparison.Ordinal))
                {
                    return;
                }
                if (!_enabledEnvironmentRows.Add(itemId))
                {
                    _enabledEnvironmentRows.Remove(itemId);
                }
                break;
        }

        RebuildRows();
        EmulatorSettingChanged?.Invoke(this, EventArgs.Empty);
        QueueFocusRect();
    }

    private void Rebuild()
    {
        _tabs.Children.Clear();
        for (var i = 0; i < Tabs.Count; i++)
        {
            var tab = Tabs[i];
            var capturedIndex = i;
            var text = new TextBlock
            {
                Text = tab.Label,
                FontSize = Ps5FontScale.SizeNormal,
                Foreground = Brushes.White,
                Opacity = i == _selectedTab ? 1 : .65,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Canvas.SetLeft(text, 0);
            Canvas.SetTop(text, i * CapturedTabPitch + 25);
            text.PointerPressed += (_, e) =>
            {
                SetSelectedTab(capturedIndex);
                _rowsHaveFocus = false;
                Focus();
                e.Handled = true;
            };
            _tabs.Children.Add(text);
        }
        RebuildRows();
    }

    private void RebuildRows()
    {
        _rows.Children.Clear();
        _visibleRows.Clear();
        var rows = Tabs[_selectedTab].Rows;
        var last = Math.Min(rows.Count, _firstVisibleRow + VisibleRows);
        for (var i = _firstVisibleRow; i < last; i++)
        {
            var model = rows[i];
            var capturedIndex = i;
            var value = ValueFor(model);
            var row = new Grid
            {
                Width = ShellSettingsMetrics.TabPanelWidth,
                Height = DiagnosticRowPitch,
                Background = Brushes.Transparent,
                ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            };
            row.PointerPressed += (_, e) =>
            {
                SetSelectedRow(capturedIndex);
                _rowsHaveFocus = true;
                Focus();
                ShellFocusRing.For(this)?.SetPressed(true);
                e.Handled = true;
            };
            row.PointerReleased += (_, e) =>
            {
                ShellFocusRing.For(this)?.SetPressed(false);
                ActivateSelectedRow();
                e.Handled = true;
            };
            row.Children.Add(new TextBlock
            {
                Text = model.Label,
                FontSize = Ps5FontScale.SizeNormal,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(
                    ShellSettingsMetrics.LongTextTitleMarginLeft,
                    0,
                    ShellSettingsMetrics.LongTextTitleMarginRight,
                    0),
            });
            if (TryGetToggle(model.ItemId, out var enabled))
            {
                var toggle = new Border
                {
                    Width = 68,
                    Height = 34,
                    CornerRadius = new CornerRadius(17),
                    Background = new SolidColorBrush(enabled
                        ? Color.FromRgb(104, 119, 143)
                        : Color.FromRgb(69, 75, 86)),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, ShellSettingsMetrics.LongTextValueMarginRight, 0),
                    Child = new Border
                    {
                        Width = 28,
                        Height = 28,
                        CornerRadius = new CornerRadius(14),
                        Background = Brushes.White,
                        HorizontalAlignment = enabled
                            ? HorizontalAlignment.Right
                            : HorizontalAlignment.Left,
                        Margin = new Thickness(3),
                    },
                };
                Grid.SetColumn(toggle, 1);
                row.Children.Add(toggle);
            }
            else if (value is not null)
            {
                var valueText = new TextBlock
                {
                    Text = value,
                    FontSize = Ps5FontScale.SizeXSmall,
                    Foreground = Brushes.White,
                    Opacity = ShellSettingsMetrics.LongTextValueOpacity,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, ShellSettingsMetrics.LongTextValueMarginRight, 0),
                };
                Grid.SetColumn(valueText, 1);
                row.Children.Add(valueText);
            }

            var separator = new Border
            {
                Height = ShellSettingsMetrics.SeparatorHeight,
                Background = SeparatorBrush,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Bottom,
            };
            Grid.SetColumn(separator, 0);
            Grid.SetColumnSpan(separator, 2);
            row.Children.Add(separator);
            Canvas.SetTop(row, (i - _firstVisibleRow) * DiagnosticRowPitch);
            _rows.Children.Add(row);
            _visibleRows[i] = row;
        }
    }

    private bool TryGetToggle(string itemId, out bool enabled)
    {
        enabled = itemId switch
        {
            "id_title_music" => _playTitleMusic,
            "id_shell_motion" => _animateShellBackground,
            "id_ui_sounds" => _playUiSounds,
            "id_shell_music" => _playShellMusic,
            "id_boot_intro" => _playBootIntro,
            "id_discord_presence" => _discordPresence,
            "id_auto_update" => _checkUpdates,
            "id_strict_dynlib" => _strictDynlib,
            "id_log_to_file" => _logToFile,
            "id_override_log_file" => _overrideLogFile,
            _ => false,
        };
        if (itemId.StartsWith("env_", StringComparison.Ordinal))
        {
            enabled = _enabledEnvironmentRows.Contains(itemId);
            return true;
        }
        return itemId is "id_title_music" or "id_shell_motion" or "id_ui_sounds" or
            "id_shell_music" or "id_boot_intro" or "id_discord_presence" or
            "id_auto_update" or "id_strict_dynlib" or "id_log_to_file" or
            "id_override_log_file";
    }

    private void SetOption(ref bool field, bool value)
    {
        if (field == value)
        {
            return;
        }

        field = value;
        RebuildRows();
    }

    private void SetText(ref string field, string? value)
    {
        field = string.IsNullOrWhiteSpace(value) ? "Default" : value;
        RebuildRows();
    }

    private string? ValueFor(ShellSettingsDetailRow row) => row.ItemId switch
    {
        "id_internal_resolution" => $"{_renderResolutionScale * 100:0}%",
        "id_language" => _languageName,
        "id_log_level" => _logLevel,
        "id_trace_imports" => _importTraceLimit.ToString(System.Globalization.CultureInfo.InvariantCulture),
        "id_log_file_path" => _logFilePath,
        _ => row.Value,
    };

    public static string CycleLogLevel(string current)
    {
        string[] levels = ["Trace", "Debug", "Info", "Warning", "Error", "Critical"];
        var index = Array.FindIndex(levels, level => string.Equals(level, current, StringComparison.OrdinalIgnoreCase));
        return levels[(Math.Max(index, 0) + 1) % levels.Length];
    }

    public static int CycleImportTraceLimit(int current)
    {
        int[] values = [0, 16, 64, 256, 1024, 4096];
        var index = Array.IndexOf(values, current);
        return values[(Math.Max(index, 0) + 1) % values.Length];
    }

    private void SetSelectedTab(int value)
    {
        _selectedTab = Math.Clamp(value, 0, Tabs.Count - 1);
        _heading.Text = Tabs[_selectedTab].Label;
        _selectedRow = 0;
        _firstVisibleRow = 0;
        Rebuild();
        QueueFocusRect();
    }

    private void SetSelectedRow(int value)
    {
        _selectedRow = Math.Clamp(value, 0, Tabs[_selectedTab].Rows.Count - 1);
        if (_selectedRow < _firstVisibleRow)
        {
            _firstVisibleRow = _selectedRow;
        }
        else if (_selectedRow >= _firstVisibleRow + VisibleRows)
        {
            _firstVisibleRow = _selectedRow - VisibleRows + 1;
        }
        RebuildRows();
        QueueFocusRect();
    }

    private void QueueFocusRect() =>
        Dispatcher.UIThread.Post(PushFocusRect, DispatcherPriority.Render);

    private void PushFocusRect()
    {
        if (!IsEffectivelyVisible || !IsFocused || ShellFocusRing.For(this) is not { } ring)
        {
            return;
        }

        Rect rect;
        if (_rowsHaveFocus)
        {
            if (!_visibleRows.TryGetValue(_selectedRow, out var row) ||
                row.TransformToVisual(ring) is not { } rowTransform)
            {
                return;
            }

            // Content focus follows the arranged item, not a second copy of
            // its pitch/offset calculation. This keeps the ring matched to
            // toggles, value rows, and clipped/scrolled rows alike.
            rect = ShellFocusRingTimeline.ApplyListItemStyle(new Rect(row.Bounds.Size))
                .TransformToAABB(rowTransform);
        }
        else
        {
            if (this.TransformToVisual(ring) is not { } transform)
            {
                return;
            }

            // TabViewPS focuses the full tab slot rather than the label glyph.
            var tabRect = new Rect(ShellSettingsMetrics.TabLeft,
                ShellSettingsMetrics.TabTop + _selectedTab * CapturedTabPitch,
                ShellSettingsMetrics.TabWidth, CapturedTabPitch);
            rect = ShellFocusRingTimeline.ApplyListItemStyle(tabRect).TransformToAABB(transform);
        }
        ring.Radius = 0;
        ring.LineScale = ShellSettingsMetrics.FocusLineScale;
        ring.Claim(this, rect);
    }

    private static double NearestScale(double value) => value switch
    {
        >= .875 => 1,
        >= .625 => .75,
        >= .375 => .5,
        _ => .25,
    };
}
