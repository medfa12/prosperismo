// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System.Runtime.InteropServices;
using System.Collections.Concurrent;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.VisualTree;

namespace SharpEmu.GUI.SystemAssets.Shell;

/// <summary>Live 4.03 Plane2 / <c>wave_bg_p</c> full-screen plate.</summary>
public sealed class Ps5NativeWavePlate : Control
{
    internal const int BitmapWidth = 480;
    internal const int BitmapHeight = 270;

    public static readonly StyledProperty<bool> MotionEnabledProperty =
        AvaloniaProperty.Register<Ps5NativeWavePlate, bool>(nameof(MotionEnabled), true);

    public static readonly StyledProperty<bool> HighContrastProperty =
        AvaloniaProperty.Register<Ps5NativeWavePlate, bool>(nameof(HighContrast));

    public static readonly StyledProperty<int> PresetIndexProperty =
        AvaloniaProperty.Register<Ps5NativeWavePlate, int>(
            nameof(PresetIndex), Ps5NativeWavePlateEvaluator.HomePresetIndex);

    public static readonly StyledProperty<int> ThemeColourIndexProperty =
        AvaloniaProperty.Register<Ps5NativeWavePlate, int>(
            nameof(ThemeColourIndex), Ps5NativeWavePlateEvaluator.SteadyNoParticleThemeIndex);

    private readonly byte[] _pixels = new byte[BitmapWidth * BitmapHeight * 4];
    private static readonly ConcurrentDictionary<int, Ps5NativeWavePlateEvaluator.FrameRenderer>
        Renderers = new();
    private WriteableBitmap? _bitmap;
    private long _frame;
    private bool _attached;
    private bool _framePending;
    private int _frameGeneration;

    public Ps5NativeWavePlate()
    {
        IsHitTestVisible = false;
        EffectiveViewportChanged += (_, _) => RequestFrame();
    }

    public bool MotionEnabled
    {
        get => GetValue(MotionEnabledProperty);
        set => SetValue(MotionEnabledProperty, value);
    }

    /// <summary>
    /// Selects the paired high-contrast native state (Home state 31, Plane2
    /// record 13) rather than restyling record 2 on the host.
    /// </summary>
    public bool HighContrast
    {
        get => GetValue(HighContrastProperty);
        set => SetValue(HighContrastProperty, value);
    }

    public int PresetIndex
    {
        get => GetValue(PresetIndexProperty);
        set => SetValue(PresetIndexProperty, value);
    }

    public int ThemeColourIndex
    {
        get => GetValue(ThemeColourIndexProperty);
        set => SetValue(ThemeColourIndexProperty, value);
    }

    internal long Frame => _frame;

    /// <summary>
    /// Re-evaluates the animation-frame route after an ancestor surface is
    /// shown or hidden.
    ///
    /// <para><see cref="IsVisible"/> is local to this control. Home and Sony
    /// Settings keep both background instances attached and switch visibility
    /// on their parent <c>Viewbox</c>, so the newly exposed plate does not get
    /// an <see cref="IsVisibleProperty"/> change of its own. The shell calls
    /// this at the page-routing boundary to start the firmware phase clock on
    /// the surface that has just become effectively visible.</para>
    /// </summary>
    internal void RefreshAnimationRoute()
    {
        if (!IsEffectivelyVisible)
        {
            return;
        }

        EnsureBitmap();
        RequestFrame();
    }

    /// <summary>
    /// Native selector route rendered by this steady Home/Settings plate.
    /// Layer-image transitions are deliberately unable to mutate it.
    /// </summary>
    internal Ps5NativePlaneRoute Route =>
        Ps5NativeWavePlateEvaluator.ResolveRoute(
            PresetIndex,
            ThemeColourIndex,
            highContrast: HighContrast);

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        EnsureBitmap();
        RequestFrame();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _attached = false;
        _frameGeneration++;
        _framePending = false;
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == MotionEnabledProperty)
        {
            RequestFrame();
        }
        else if (change.Property == HighContrastProperty ||
                 change.Property == PresetIndexProperty ||
                 change.Property == ThemeColourIndexProperty)
        {
            UpdateBitmap();
            InvalidateVisual();
            RequestFrame();
        }
        else if (change.Property == IsVisibleProperty)
        {
            if (change.GetNewValue<bool>() && MotionEnabled && this.GetVisualRoot() is not null)
            {
                RefreshAnimationRoute();
            }
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        EnsureBitmap();
        if (_bitmap is not null && Bounds.Width > 0 && Bounds.Height > 0)
        {
            context.DrawImage(_bitmap, new Rect(_bitmap.Size), new Rect(Bounds.Size));
        }
    }

    internal void AdvanceFrame()
    {
        // Local IsVisible remains true when an ancestor route is hidden. The
        // shell owns both Home and Settings backgrounds at once, so testing
        // only that property made the off-route plate repaint at 60 Hz too.
        if (!IsEffectivelyVisible)
        {
            return;
        }

        if (MotionEnabled)
        {
            _frame++;
        }

        UpdateBitmap();
        InvalidateVisual();
    }

    private void RequestFrame()
    {
        if (_framePending || !_attached || !MotionEnabled || !IsEffectivelyVisible ||
            TopLevel.GetTopLevel(this) is not { } topLevel)
        {
            return;
        }

        _framePending = true;
        int generation = _frameGeneration;
        topLevel.RequestAnimationFrame(frameTime => OnFrame(frameTime, generation));
    }

    private void OnFrame(TimeSpan _, int generation)
    {
        if (generation != _frameGeneration)
        {
            return;
        }

        _framePending = false;
        if (!_attached || !MotionEnabled || !IsEffectivelyVisible)
        {
            return;
        }

        AdvanceFrame();
        RequestFrame();
    }

    private void EnsureBitmap()
    {
        if (_bitmap is not null)
        {
            return;
        }

        _bitmap = new WriteableBitmap(
            new PixelSize(BitmapWidth, BitmapHeight),
            new Vector(96, 96),
            PixelFormat.Rgba8888,
            AlphaFormat.Unpremul);
        UpdateBitmap();
    }

    private void UpdateBitmap()
    {
        if (_bitmap is null)
        {
            return;
        }

        Ps5NativeWavePlateEvaluator.FrameRenderer renderer;
        try
        {
            int recordIndex = Route.RecordIndex;
            renderer = Renderers.GetOrAdd(
                recordIndex,
                static index => new Ps5NativeWavePlateEvaluator.FrameRenderer(
                    BitmapWidth, BitmapHeight, index));
        }
        catch (Exception)
        {
            // A custom firmware record is mandatory for a custom route. Keep
            // the last valid frame rather than substituting record 2 and
            // presenting the wrong theme as successful firmware execution.
            return;
        }

        renderer.Render(_frame, _pixels);
        using var frame = _bitmap.Lock();
        int sourceStride = BitmapWidth * 4;
        for (int y = 0; y < BitmapHeight; y++)
        {
            Marshal.Copy(_pixels, y * sourceStride, frame.Address + (y * frame.RowBytes), sourceStride);
        }
    }
}
