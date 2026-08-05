// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using SharpEmu.GUI.SystemAssets;
using SharpEmu.GUI.SystemAssets.Textures;
using SharpEmu.HLE.Host;
using SharpEmu.HLE.Host.Windows;

namespace SharpEmu.GUI.BootAnimation;

/// <summary>
/// The first-launch boot sequence: the console's cold boot, rendered live over the
/// top of the shell while the shell builds itself underneath, with its own sound.
///
/// Nothing here plays a file. The picture is <see cref="BootIntroFrameSource"/>
/// running the recovered choreography at whatever rate the compositor ticks, in
/// two tiers - the diffuse buffer upscaled to fill the window, and the resolved
/// mote heads drawn over it at full control resolution, which is where the detail
/// lives. The only asset is the sound, and it is optional.
///
/// It is a child of the window's root grid, never a second window: a separate
/// window would need its own shutdown handling and has broken application exit
/// here before. It spans every row of that grid, so it covers the extended title
/// bar too, and removes itself when it is done.
///
/// It never blocks the UI thread and never delays the launch. Building the field
/// is a handful of arrays; the sound is decoded on a background thread and starts
/// when it is ready; and the shell is interactive behind it the whole time.
///
/// A press of any key, any pad button or the mouse skips: the picture fades out
/// over a beat and the sound stops on the mixer's next buffer. The hint that says
/// so appears a couple of seconds in rather than from the first frame, the way the
/// console does it.
/// </summary>
public sealed class BootIntroOverlay : Panel
{
    /// <summary>How long the sequence is up before the skip hint fades in.</summary>
    public static readonly TimeSpan HintDelay = TimeSpan.FromSeconds(2.5);

    /// <summary>Fade applied when the overlay takes the screen.</summary>
    public static readonly TimeSpan FadeIn = TimeSpan.FromSeconds(0.2);

    /// <summary>Fade applied when the sequence ends or is skipped.</summary>
    public static readonly TimeSpan FadeOut = TimeSpan.FromSeconds(0.45);

    private const int GamepadPollMilliseconds = 50;

    private readonly BootIntroSequence _sequence = new(HintDelay);
    private readonly BootIntroSurface _surface;
    private readonly StackPanel _hint;
    private readonly Image _hintGlyph;
    private readonly DispatcherTimer _gamepadTimer;

    private BootIntroFrameSource? _source;
    private TopLevel? _inputRoot;
    private DateTime _startedAt;
    private DateTime _visibleAt;
    private TimeSpan _lastFrameTime;
    private bool _hasFrameTime;
    private HostGamepadButtons _previousPadButtons;
    private bool _hasPadBaseline;
    private bool _framePending;
    private bool _captureMode;

    private BootIntroOverlay()
    {
        Background = Brushes.Black;
        Opacity = 0;
        IsHitTestVisible = false;
        ClipToBounds = true;
        Transitions = new Transitions
        {
            new DoubleTransition { Property = OpacityProperty, Duration = FadeIn },
        };

        _surface = new BootIntroSurface();

        _hintGlyph = new Image
        {
            Width = 30,
            Height = 30,
            VerticalAlignment = VerticalAlignment.Center,
            Source = ShellIcons.TryGet(ShellIcon.Cross),
        };

        _hint = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            // Low on the frame, clear of the knot.
            Margin = new Thickness(0, 0, 0, 72),
            Opacity = 0,
            IsHitTestVisible = false,
            Transitions = new Transitions
            {
                new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromSeconds(0.5) },
            },
            Children =
            {
                _hintGlyph,
                new TextBlock
                {
                    Text = "Skip",
                    FontSize = 17,
                    Foreground = new SolidColorBrush(Color.FromArgb(0xD8, 0xFF, 0xFF, 0xFF)),
                    VerticalAlignment = VerticalAlignment.Center,
                },
            },
        };

        Children.Add(_surface);
        Children.Add(_hint);

        _gamepadTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(GamepadPollMilliseconds),
        };
        _gamepadTimer.Tick += (_, _) =>
        {
            PollGamepad();

            // Backstop for the animation-frame loop: a compositor that stops
            // ticking would otherwise leave the overlay frozen on screen.
            RequestFrame();
        };
    }

    /// <summary>
    /// Raised on the UI thread once the overlay has left the visual tree, whether
    /// it played out or was skipped.
    /// </summary>
    public event EventHandler? Finished;

    /// <summary>True while the sequence is still on screen.</summary>
    public bool IsPlaying => _sequence.IsPlaying;

    /// <summary>Where the sequence has got to. Diagnostics and tests.</summary>
    public BootIntroState State => _sequence.State;

    /// <summary>
    /// Advances the real frame source without a platform animation callback.
    /// Used by the headless shell-shot harness so visual QA exercises this
    /// overlay and compositor rather than a duplicate renderer.
    /// </summary>
    internal void AdvanceForCapture(TimeSpan elapsed, TimeSpan delta, Size size)
    {
        _captureMode = true;
        if (_source is null || size.Width <= 0.0 || size.Height <= 0.0)
        {
            return;
        }

        Opacity = 1.0;
        if (_source.Resize(size.Width, size.Height))
        {
            _source.Advance(elapsed, Math.Max(0.0, delta.TotalSeconds));
            _surface.Present(recreateBitmap: true);
        }
    }

    /// <summary>
    /// Builds the overlay for this launch, or returns null when the intro is not
    /// due: already seen, or turned off. There is no longer an asset that can be
    /// missing, so a fresh profile always gets it. Latches the once-only flag when
    /// it does return one, so an interrupted intro does not come back on the next
    /// launch.
    /// </summary>
    /// <param name="settings">Launcher settings; the once-only latch lives here.</param>
    /// <returns>The overlay, or null when the intro is not due.</returns>
    public static BootIntroOverlay? TryCreate(GuiSettings? settings)
    {
        if (!BootIntroPolicy.ShouldPlay(settings))
        {
            return null;
        }

        BootIntroPolicy.MarkPlayed(settings);

        var overlay = new BootIntroOverlay();
        overlay.Begin(settings);
        return overlay;
    }

    /// <summary>
    /// Builds the overlay and puts it in front of everything else in
    /// <paramref name="root"/>, spanning the whole grid so it covers the title bar
    /// as well. Returns null when there is no intro to play. The overlay takes
    /// itself back out when it finishes.
    /// </summary>
    /// <param name="root">The window's root layout panel.</param>
    /// <param name="settings">Launcher settings; the once-only latch lives here.</param>
    /// <returns>The overlay, or null when the intro is not due.</returns>
    public static BootIntroOverlay? TryAttach(Panel? root, GuiSettings? settings)
    {
        if (root is null || TryCreate(settings) is not { } overlay)
        {
            return null;
        }

        if (root is Grid grid)
        {
            Grid.SetRow(overlay, 0);
            Grid.SetRowSpan(overlay, Math.Max(1, grid.RowDefinitions.Count));
            Grid.SetColumn(overlay, 0);
            Grid.SetColumnSpan(overlay, Math.Max(1, grid.ColumnDefinitions.Count));
        }

        overlay.ZIndex = int.MaxValue;
        root.Children.Add(overlay);
        return overlay;
    }

    /// <summary>
    /// Ends the sequence early: the sound stops now and the picture fades. Safe to
    /// call more than once and from the UI thread only.
    /// </summary>
    public void Skip() => Finish();

    private void Begin(GuiSettings? settings)
    {
        _startedAt = DateTime.UtcNow;
        _source = new BootIntroFrameSource();
        _surface.Source = _source;

        // The sound is decoded and trimmed off-thread; it starts when it is ready,
        // which on a warm machine is well inside the sequence's opening black. It
        // is trimmed to the firmware's own 6000 ms rather than to a movie's length,
        // because the picture is now that long by construction.
        _ = Task.Run(() =>
        {
            var audioPath = BootIntroPolicy.ResolveAudioPath(settings);
            if (audioPath is null)
            {
                return;
            }

            var clip = BootIntroAudio.Prepare(audioPath, BootIntroTimeline.TotalDuration);
            if (clip is null)
            {
                return;
            }

            Dispatcher.UIThread.Post(() =>
            {
                if (_sequence.IsPlaying)
                {
                    BootIntroAudio.Play(clip);
                }
            });
        });
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        _inputRoot = TopLevel.GetTopLevel(this);
        if (_inputRoot is not null)
        {
            // Tunnelling from the top level so a skip lands whatever the shell
            // has focused underneath, and handled events still count.
            _inputRoot.AddHandler(KeyDownEvent, OnAnyKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
            _inputRoot.AddHandler(PointerPressedEvent, OnAnyPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        }

        _gamepadTimer.Start();
        RequestFrame();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Teardown();
    }

    // ---- Frame loop ----

    private void RequestFrame()
    {
        if (_framePending || !_sequence.IsPlaying || TopLevel.GetTopLevel(this) is not { } topLevel)
        {
            return;
        }

        _framePending = true;
        topLevel.RequestAnimationFrame(OnFrame);
    }

    private void OnFrame(TimeSpan frameTime)
    {
        _framePending = false;
        if (_captureMode)
        {
            return;
        }

        if (!_sequence.IsPlaying)
        {
            return;
        }

        var source = _source;
        if (source is null)
        {
            Finish();
            return;
        }

        var now = DateTime.UtcNow;

        // The sequence takes the screen on its first tick. There is nothing to
        // open and nothing to wait for, so the shell is never seen and then
        // covered over again.
        if (_sequence.NotifyVisible())
        {
            _visibleAt = now;
            Opacity = 1;
            IsHitTestVisible = true;
        }

        var delta = _hasFrameTime ? (frameTime - _lastFrameTime).TotalSeconds : 1.0 / 60.0;
        _lastFrameTime = frameTime;
        _hasFrameTime = true;
        if (delta <= 0)
        {
            delta = 1.0 / 60.0;
        }

        // Paced against the wall clock rather than against the frame counter: the
        // sound is one long buffer the mixer plays at its own rate, so the picture
        // has to follow real time to stay with it. A dropped frame costs motion,
        // never sync.
        var size = Bounds.Size;
        if (source.Resize(size.Width, size.Height))
        {
            source.Advance(now - _visibleAt, delta);
            _surface.Present();
        }

        // Timed from when the sequence took the screen, so the hint arrives a beat
        // into what the viewer is actually watching.
        if (_sequence.TryShowHint(now - _visibleAt))
        {
            // The pictogram needs a live rendering backend, so a load attempted
            // while the window was still coming up can have come back empty.
            _hintGlyph.Source ??= ShellIcons.TryGet(ShellIcon.Cross);
            _hint.Opacity = 1;
        }

        if (source.IsComplete)
        {
            Finish();
            return;
        }

        RequestFrame();
    }

    // ---- Input ----

    private void OnAnyKeyDown(object? sender, KeyEventArgs e)
    {
        if (TrySkipFromInput())
        {
            e.Handled = true;
        }
    }

    private void OnAnyPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (TrySkipFromInput())
        {
            e.Handled = true;
        }
    }

    private void PollGamepad()
    {
        if (!_sequence.IsPlaying)
        {
            return;
        }

        if (!WindowsDualSenseReader.TryGetState(out var pad) && !WindowsXInputReader.TryGetState(out pad))
        {
            _hasPadBaseline = false;
            _previousPadButtons = HostGamepadButtons.None;
            return;
        }

        // Only a fresh press skips, so a button already held when the launcher
        // opened does not eat the intro before it has drawn.
        var pressed = _hasPadBaseline ? pad.Buttons & ~_previousPadButtons : HostGamepadButtons.None;
        _previousPadButtons = pad.Buttons;
        _hasPadBaseline = true;

        if (pressed != HostGamepadButtons.None)
        {
            TrySkipFromInput();
        }
    }

    // The sequence refuses a skip until a frame is up: before that the overlay is
    // invisible and the keystroke belongs to the shell underneath.
    private bool TrySkipFromInput()
    {
        if (!_sequence.TrySkip())
        {
            return false;
        }

        EndSequence();
        return true;
    }

    // ---- Teardown ----

    private void Finish()
    {
        if (_sequence.TryComplete())
        {
            EndSequence();
        }
    }

    private void EndSequence()
    {
        BootIntroAudio.Stop();
        _gamepadTimer.Stop();
        _hint.Opacity = 0;

        if (!_sequence.ShouldFadeOut)
        {
            Remove(); // nothing was ever drawn, so there is nothing to fade from
            return;
        }

        if (Transitions is { Count: > 0 } transitions &&
            transitions[0] is DoubleTransition fade)
        {
            fade.Duration = FadeOut;
        }

        Opacity = 0;
        DispatcherTimer.RunOnce(Remove, FadeOut);
    }

    private void Remove()
    {
        if (!_sequence.TryFinish())
        {
            return;
        }

        (Parent as Panel)?.Children.Remove(this);
        Teardown();

        try
        {
            Finished?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception)
        {
            // A host that throws while tidying up does not take the launcher down.
        }
    }

    private void Teardown()
    {
        _gamepadTimer.Stop();

        if (_inputRoot is not null)
        {
            _inputRoot.RemoveHandler(KeyDownEvent, OnAnyKeyDown);
            _inputRoot.RemoveHandler(PointerPressedEvent, OnAnyPointerPressed);
            _inputRoot = null;
        }

        _surface.Release();
        _source?.Dispose();
        _source = null;

        BootIntroAudio.Stop();
    }

    /// <summary>
    /// Draws the sequence in two tiers: the diffuse buffer stretched to fill the
    /// control, then the resolved mote heads over it at full control resolution.
    ///
    /// Its own control because <see cref="Panel"/> seals Render and the hint has to
    /// sit above the picture rather than beside it.
    /// </summary>
    private sealed class BootIntroSurface : Control
    {
        /// <summary>Pre-tinted head sprites. See <see cref="EnsureSprites"/> for the layout.</summary>
        private const int BlueSprites = 4;
        private const int GoldSprites = 4;
        private const int SpectrumSprites = 12;
        private const int SpriteCount = BlueSprites + GoldSprites + SpectrumSprites;

        /// <summary>Sprite edge in texels. Small: these are heads, not discs.</summary>
        private const int SpriteSize = 24;

        /// <summary>Reference height the radii are quoted against.</summary>
        private const double ReferenceHeight = 1080.0;

        /// <summary>Below this a head is not worth a draw call.</summary>
        private const double VisibleOpacity = 0.015;

        /// <summary>
        /// How much of the split a mote needs before its head is drawn from the
        /// spectrum rather than from the phase's own ramp. High on purpose: the
        /// diffuse tier underneath carries the split continuously, so the heads
        /// only have to agree with it well inside the knot. A low threshold makes
        /// the outer motes flip individually and the result is confetti.
        /// </summary>
        private const double SplitThreshold = 0.55;

        private static Bitmap?[]? s_sprites;
        private static bool s_spritesFailed;

        private WriteableBitmap? _diffuse;

        internal BootIntroFrameSource? Source { get; set; }

        /// <summary>Uploads the freshly composed diffuse buffer and asks for a redraw.</summary>
        internal void Present(bool recreateBitmap = false)
        {
            if (recreateBitmap)
            {
                // Avalonia.Headless does not invalidate a cached image when a
                // WriteableBitmap's backing memory changes. Recreate only in the
                // deterministic capture path; the live renderer keeps reusing it.
                _diffuse?.Dispose();
                _diffuse = null;
            }

            Upload();
            InvalidateVisual();
        }

        /// <summary>Drops the bitmap.</summary>
        internal void Release()
        {
            Source = null;
            _diffuse?.Dispose();
            _diffuse = null;
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);

            var size = Bounds.Size;
            if (Source is not { } source || size.Width <= 0 || size.Height <= 0)
            {
                return;
            }

            if (_diffuse is not null)
            {
                // Upscaled on purpose: the diffuse tier is trails, plate and haze,
                // and all three are soft. The detail is the heads, drawn next at
                // full resolution.
                using (context.PushRenderOptions(new RenderOptions
                {
                    BitmapInterpolationMode = BitmapInterpolationMode.HighQuality,
                }))
                {
                    context.DrawImage(_diffuse, new Rect(0, 0, size.Width, size.Height));
                }
            }

            DrawHeads(context, source, size);
        }

        private void DrawHeads(DrawingContext context, BootIntroFrameSource source, Size size)
        {
            EnsureSprites();
            if (s_sprites is not { Length: > 0 } sprites)
            {
                return;
            }

            var motes = source.Motes;
            if (motes.Length == 0)
            {
                return;
            }

            var frame = source.Frame;
            if (frame.Particles <= 0.0)
            {
                return;
            }

            var scale = Math.Clamp(size.Height / ReferenceHeight, 0.35, 2.0);
            var resources = frame.ParticleResources;

            // The native renderer has two ResourcesLargeParticleVsPs blocks.
            // Their count, value, transparency and min/max size now decide the
            // resolved tier. Until their position buffer is decoded, stable
            // small-field motes are used only as carriers for those draw records.
            DrawResourceHeads(
                context, sprites, motes, frame, size, scale,
                resources.Large0, start: 0, stride: 37);
            DrawResourceHeads(
                context, sprites, motes, frame, size, scale,
                resources.Large1, start: 17, stride: 31);
        }

        private static void DrawResourceHeads(
            DrawingContext context,
            Bitmap?[] sprites,
            ReadOnlySpan<BootIntroMote> motes,
            in BootIntroFrame frame,
            Size size,
            double scale,
            in Ps5LargeParticleDrawState resource,
            int start,
            int stride)
        {
            if (resource.NumParticles <= 0
                || resource.Transparency <= 0.0
                || resource.ParMaxSize <= 0.0)
            {
                return;
            }

            var count = Math.Min(resource.NumParticles, motes.Length);
            var valueGain = Math.Clamp(resource.ParticleColorValue / 0.1, 0.0, 1.0);

            for (var particle = 0; particle < count; particle++)
            {
                var i = (start + (particle * stride)) % motes.Length;
                ref readonly var mote = ref motes[i];
                if (mote.Envelope <= 0.02)
                {
                    continue;
                }

                var presence = mote.Depth * mote.Depth;
                var opacity = mote.Envelope
                              * (0.08 + (0.92 * presence))
                              * frame.Particles
                              * resource.Transparency
                              * valueGain;
                if (opacity <= VisibleOpacity)
                {
                    continue;
                }

                if (opacity > 1.0)
                {
                    opacity = 1.0;
                }

                var x = mote.X * size.Width;
                var y = mote.Y * size.Height;
                var diameter = resource.ParMinSize
                               + ((resource.ParMaxSize - resource.ParMinSize) * mote.Depth);
                var radius = diameter * 0.5 * scale;
                if (x + radius < 0 || y + radius < 0 || x - radius > size.Width || y - radius > size.Height)
                {
                    continue;
                }

                if (sprites[SpriteFor(frame, mote)] is not { } sprite)
                {
                    continue;
                }

                using (context.PushOpacity(opacity))
                {
                    context.DrawImage(sprite, new Rect(x - radius, y - radius, radius * 2, radius * 2));
                }
            }
        }

        // Which pre-tinted head this mote wants. The heads are quantised because a
        // sprite cannot be tinted at draw time and rebuilding the atlas per frame
        // would allocate in the hot path; the diffuse tier underneath carries the
        // exact colour, so what is quantised is only the bright point on top of an
        // already correctly coloured filament.
        private static int SpriteFor(in BootIntroFrame frame, in BootIntroMote mote)
        {
            var split = frame.Rainbow * mote.InsideKnot;
            if (split >= SplitThreshold)
            {
                var step = (int)Math.Round(mote.Spectrum * (SpectrumSprites - 1));
                return BlueSprites + GoldSprites + Math.Clamp(step, 0, SpectrumSprites - 1);
            }

            var tone = Math.Clamp((int)Math.Round(mote.Tone * (BlueSprites - 1)), 0, BlueSprites - 1);
            return frame.GoldMix >= 0.5 ? BlueSprites + tone : tone;
        }

        // Twenty sprites, built once: four along the blue ramp, four along the
        // gold, twelve around the dispersion. Cheap enough to build on the first
        // frame that it does not need a background thread, and small enough that
        // the whole atlas is under fifty kilobytes.
        private static void EnsureSprites()
        {
            if (s_sprites is not null || s_spritesFailed)
            {
                return;
            }

            try
            {
                var sprites = new Bitmap?[SpriteCount];
                var rgba = new byte[SpriteSize * SpriteSize * 4];

                for (var i = 0; i < BlueSprites; i++)
                {
                    BootIntroPalette.Blue.Sample(
                        (double)i / (BlueSprites - 1), out var r, out var g, out var b);
                    WriteSprite(rgba, r, g, b);
                    sprites[i] = DdsImageAvalonia.CreateBitmap(rgba, SpriteSize, SpriteSize);
                }

                for (var i = 0; i < GoldSprites; i++)
                {
                    BootIntroPalette.Gold.Sample(
                        (double)i / (GoldSprites - 1), out var r, out var g, out var b);
                    WriteSprite(rgba, r, g, b);
                    sprites[BlueSprites + i] = DdsImageAvalonia.CreateBitmap(rgba, SpriteSize, SpriteSize);
                }

                for (var i = 0; i < SpectrumSprites; i++)
                {
                    BootIntroPalette.Spectrum.Sample(
                        (double)i / (SpectrumSprites - 1), out var r, out var g, out var b);
                    WriteSprite(rgba, r, g, b);
                    sprites[BlueSprites + GoldSprites + i] =
                        DdsImageAvalonia.CreateBitmap(rgba, SpriteSize, SpriteSize);
                }

                s_sprites = sprites;
            }
            catch (Exception)
            {
                // Headless: no bitmaps, no heads, and the diffuse tier carries on.
                s_spritesFailed = true;
            }
        }

        // One head: a bright middle with a fast falloff and a whitened centre, so
        // it reads as a light source rather than as a coloured dot. The reference's
        // brightest pixels through the whole sequence are near, but not at, white -
        // #2176F7 in the blue stretch and a warm white in the gold - so the centre
        // is lifted toward white rather than set to it.
        private static void WriteSprite(byte[] rgba, double r, double g, double b)
        {
            var peak = Math.Max(Math.Max(r, g), Math.Max(b, 1e-6));
            var centre = (SpriteSize - 1) / 2.0;
            var inverse = 1.0 / centre;

            for (var y = 0; y < SpriteSize; y++)
            {
                for (var x = 0; x < SpriteSize; x++)
                {
                    var dx = (x - centre) * inverse;
                    var dy = (y - centre) * inverse;
                    var distance = Math.Sqrt((dx * dx) + (dy * dy));
                    var offset = ((y * SpriteSize) + x) * 4;

                    if (distance >= 1.0)
                    {
                        rgba[offset] = 0;
                        rgba[offset + 1] = 0;
                        rgba[offset + 2] = 0;
                        rgba[offset + 3] = 0;
                        continue;
                    }

                    var falloff = 1.0 - distance;
                    var alpha = falloff * falloff * falloff;

                    // The middle of a bright point is whiter than its edge, which
                    // is what stops a head reading as a flat coloured disc.
                    var white = falloff * falloff * falloff * falloff * 0.72;
                    rgba[offset] = ToByte(Lerp(r / peak, 1.0, white));
                    rgba[offset + 1] = ToByte(Lerp(g / peak, 1.0, white));
                    rgba[offset + 2] = ToByte(Lerp(b / peak, 1.0, white));
                    rgba[offset + 3] = ToByte(alpha);
                }
            }
        }

        private void Upload()
        {
            if (Source is not { } source || source.Width == 0)
            {
                return;
            }

            if (_diffuse is null ||
                _diffuse.PixelSize.Width != source.Width ||
                _diffuse.PixelSize.Height != source.Height)
            {
                _diffuse?.Dispose();
                _diffuse = null;

                try
                {
                    _diffuse = new WriteableBitmap(
                        new PixelSize(source.Width, source.Height),
                        new Vector(96, 96),
                        PixelFormat.Bgra8888,
                        AlphaFormat.Opaque);
                }
                catch (Exception)
                {
                    // No rendering platform: the field still runs, there is simply
                    // nothing to upload it into.
                    return;
                }
            }

            var rowBytes = source.Width * 4;
            try
            {
                using var frame = _diffuse.Lock();

                // Row by row only when the surface is padded; the common case is
                // one copy, and neither path allocates.
                if (frame.RowBytes == rowBytes)
                {
                    Marshal.Copy(source.PixelBuffer, 0, frame.Address, rowBytes * source.Height);
                    return;
                }

                for (var y = 0; y < source.Height; y++)
                {
                    Marshal.Copy(
                        source.PixelBuffer, y * rowBytes, frame.Address + (y * frame.RowBytes), rowBytes);
                }
            }
            catch (Exception)
            {
                // A bitmap that cannot be locked simply does not update this frame.
            }
        }

        private static double Lerp(double from, double to, double amount) => from + ((to - from) * amount);

        private static byte ToByte(double value) => (byte)Math.Clamp((value * 255.0) + 0.5, 0.0, 255.0);
    }
}

