// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SharpEmu.Libs.Presentation;
using System.Text.Json;

namespace SharpEmu.GUI.SystemAssets.Shell;

/// <summary>
/// Presents frames produced by the recovered firmware particle pipeline.
/// Sony binaries and textures never enter the repository: the renderer tool
/// writes a cache frame from the user's dump and this control composites that
/// frame with the recovered additive blend.
/// </summary>
internal sealed class Ps5NativeBackgroundLayer : Image
{
    internal const string FrameEnvironmentVariable = "SHARPEMU_PS5_NATIVE_FRAME";
    internal const string PreviewEnvironmentVariable = "SHARPEMU_PS5_NATIVE_PREVIEW";

    private string? _framePathOverride;
    private ShellGlobalBackgroundState _globalState;
    private bool _motionEnabled = true;
    private bool _isFrameLoaded;
    private int _loadGeneration;
    private readonly DispatcherTimer _frameTimer;
    private IReadOnlyList<Bitmap> _frames = Array.Empty<Bitmap>();
    private int _frameIndex;
    private TimeSpan _manualAccumulator;
    private IPs5NativeParticleFrameSource? _liveSource;
    private CancellationTokenSource? _liveCancellation;
    private TimeSpan _liveElapsed;
    private bool _liveRenderPending;
    private Bitmap? _liveBitmap;

    public Ps5NativeBackgroundLayer()
    {
        IsHitTestVisible = false;
        Stretch = Stretch.UniformToFill;
        RenderOptions.SetBitmapBlendingMode(this, BitmapBlendingMode.Plus);
        _frameTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.0 / 30.0) };
        _frameTimer.Tick += (_, _) => AdvanceFrame();
        // Firmware-texture sources first: when a dump supplies Particle0/1.gnf
        // they replay Sony's own light textures and are the higher-fidelity
        // route. Without a dump both return null, so fall back to evaluating
        // the recovered simulation directly - see Ps5ProceduralParticleFrameSource.
        _liveSource = (IPs5NativeParticleFrameSource?)
            Ps5NativeSmallParticleCacheFrameSource.TryCreateFromEnvironment()
            ?? (IPs5NativeParticleFrameSource?)Ps5NativeParticleCacheFrameSource.TryCreateFromEnvironment()
            ?? new Ps5ProceduralParticleFrameSource();
        UpdateVisibility();
    }

    internal string? FramePathOverride
    {
        get => _framePathOverride;
        set
        {
            if (string.Equals(_framePathOverride, value, StringComparison.Ordinal))
            {
                return;
            }

            _framePathOverride = value;
            if (this.GetVisualRoot() is not null)
            {
                _ = LoadFrameAsync();
            }
        }
    }

    internal ShellGlobalBackgroundState GlobalState
    {
        get => _globalState;
        set
        {
            if (_globalState == value)
            {
                return;
            }

            _globalState = value;
            _liveElapsed = TimeSpan.Zero;
            UpdateVisibility();
        }
    }

    /// <summary>
    /// In-process firmware evaluator/renderer. When present it is authoritative;
    /// the file sequence remains available only as a bring-up fallback.
    /// </summary>
    internal IPs5NativeParticleFrameSource? LiveSource
    {
        get => _liveSource;
        set
        {
            if (ReferenceEquals(_liveSource, value))
            {
                return;
            }

            CancelLiveRender();
            _liveSource = value;
            _liveElapsed = TimeSpan.Zero;
            _liveRenderPending = false;
            if (value is not null)
            {
                DisposeLiveBitmap();
                IsFrameLoaded = false;
            }
            UpdateVisibility();
            QueueLiveFrame(TimeSpan.Zero);
        }
    }

    internal bool MotionEnabled
    {
        get => _motionEnabled;
        set
        {
            if (_motionEnabled == value)
            {
                return;
            }

            _motionEnabled = value;
            UpdateVisibility();
        }
    }

    internal bool IsFrameLoaded
    {
        get => _isFrameLoaded;
        private set
        {
            _isFrameLoaded = value;
            UpdateVisibility();
        }
    }

    internal void RefreshVisibility() => UpdateVisibility();

    internal bool ManualClock { get; set; }

    internal TimeSpan LiveElapsed => _liveElapsed;

    internal bool LiveRenderPending => _liveRenderPending;

    internal void AdvanceForCapture(TimeSpan delta)
    {
        if (!ManualClock || !IsVisible || delta <= TimeSpan.Zero)
        {
            return;
        }

        if (_liveSource is not null)
        {
            QueueLiveFrame(delta);
            return;
        }
        if (_frames.Count < 2)
        {
            return;
        }

        _manualAccumulator += delta;
        while (_manualAccumulator >= _frameTimer.Interval)
        {
            _manualAccumulator -= _frameTimer.Interval;
            AdvanceFrame();
        }
    }

    internal string? ResolveFramePath()
    {
        var path = string.IsNullOrWhiteSpace(_framePathOverride)
            ? Environment.GetEnvironmentVariable(FrameEnvironmentVariable)
            : _framePathOverride;

        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            path = Path.GetFullPath(path);
            return File.Exists(path) || Directory.Exists(path) ? path : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _ = LoadFrameAsync();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Interlocked.Increment(ref _loadGeneration);
        CancelLiveRender();
        _frameTimer.Stop();
        Source = null;
        DisposeLiveBitmap();
        DisposeFrames(_frames);
        _frames = Array.Empty<Bitmap>();
        IsFrameLoaded = false;
        base.OnDetachedFromVisualTree(e);
    }

    private async Task LoadFrameAsync()
    {
        var generation = Interlocked.Increment(ref _loadGeneration);
        var path = ResolveFramePath();
        var sequence = new FrameSequence(Array.Empty<Bitmap>(), 30.0);

        if (path is not null)
        {
            try
            {
                sequence = await Task.Run(() => LoadFrames(path));
            }
            catch (Exception)
            {
                sequence = new FrameSequence(Array.Empty<Bitmap>(), 30.0);
            }
        }

        if (generation != _loadGeneration || this.GetVisualRoot() is null)
        {
            DisposeFrames(sequence.Frames);
            return;
        }

        var old = _frames;
        _frames = sequence.Frames;
        _frameIndex = 0;
        _frameTimer.Interval = TimeSpan.FromSeconds(1.0 / sequence.FramesPerSecond);
        if (_liveSource is null)
        {
            Source = _frames.Count == 0 ? null : _frames[0];
            IsFrameLoaded = _frames.Count != 0;
        }
        DisposeFrames(old);
    }

    private void UpdateVisibility()
    {
        var mode = ShellBackgroundComposition.LightModeFor(_globalState);
        var route = ShellBackgroundComposition.NativeParticleRouteFor(mode);
        // A live source declares the exact raw firmware state carried by its
        // cache. The file-only fallback remains the accepted state-3 coldboot
        // sequence.
        var eligible = _motionEnabled &&
            (_liveSource?.SupportsState(_globalState) ?? route.RawState == 3);
        Opacity = eligible ? Math.Clamp(route.LayerWeight, 0.0f, 1.0f) : 0.0;
        IsVisible = eligible && (_isFrameLoaded || _liveSource is not null);
        if (eligible && !ManualClock && (_liveSource is not null || _frames.Count > 1))
        {
            _frameTimer.Start();
            if (_liveSource is not null && !_isFrameLoaded)
            {
                QueueLiveFrame(TimeSpan.Zero);
            }
        }
        else
        {
            _frameTimer.Stop();
        }
    }

    /// <summary>
    /// Drives one frame without waiting on the dispatcher timer.
    ///
    /// Headless capture (ShellShot) pumps its own clock, so the 30 Hz
    /// DispatcherTimer never ticks there and the particle layer stayed blank —
    /// captures under-reported what the live shell actually draws, which makes
    /// them useless for grading against reference footage.
    /// </summary>
    internal void AdvanceFrameForCapture() => AdvanceFrame();

    private void AdvanceFrame()
    {
        if (_liveSource is not null)
        {
            QueueLiveFrame(_frameTimer.Interval);
            return;
        }

        if (!IsVisible || _frames.Count < 2)
        {
            return;
        }

        _frameIndex = (_frameIndex + 1) % _frames.Count;
        Source = _frames[_frameIndex];
    }

    private void QueueLiveFrame(TimeSpan delta)
    {
        if (_liveSource is not { } source || !_motionEnabled || !source.SupportsState(_globalState))
        {
            return;
        }

        _liveElapsed += delta;
        // Rendering can take longer than one display tick. The firmware clock
        // must still advance while that draw is in flight; otherwise a slow
        // host plays the native animation in slow motion instead of dropping
        // intermediate frames like the console compositor does.
        if (_liveRenderPending)
        {
            return;
        }

        var width = Math.Max(1, (int)Math.Round(Bounds.Width));
        var height = Math.Max(1, (int)Math.Round(Bounds.Height));
        if (width <= 1 || height <= 1)
        {
            width = 1920;
            height = 1080;
        }

        _liveCancellation ??= new CancellationTokenSource();
        var token = _liveCancellation.Token;
        var request = new Ps5NativeParticleFrameRequest(
            _globalState,
            _liveElapsed,
            width,
            height);
        _liveRenderPending = true;
        _ = RenderLiveFrameAsync(source, request, token);
    }

    private async Task RenderLiveFrameAsync(
        IPs5NativeParticleFrameSource source,
        Ps5NativeParticleFrameRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var frame = await source.RenderAsync(request, cancellationToken).ConfigureAwait(false);
            if (frame is not { IsValid: true } || cancellationToken.IsCancellationRequested ||
                !ReferenceEquals(source, _liveSource))
            {
                return;
            }

            await Dispatcher.UIThread.InvokeAsync(() => PresentLiveFrame(frame),
                DispatcherPriority.Render, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            // Keep the last good frame or the file-sequence fallback visible.
        }
        finally
        {
            _liveRenderPending = false;
        }
    }

    private void PresentLiveFrame(Ps5NativeParticleFrame frame)
    {
        var bitmap = new WriteableBitmap(
            new PixelSize(frame.Width, frame.Height),
            new Vector(96, 96),
            PixelFormat.Rgba8888,
            AlphaFormat.Unpremul);
        using (var target = bitmap.Lock())
        {
            unsafe
            {
                fixed (byte* source = frame.Rgba.Span)
                {
                    var sourceRowBytes = frame.Width * 4;
                    for (var y = 0; y < frame.Height; y++)
                    {
                        Buffer.MemoryCopy(
                            source + (y * sourceRowBytes),
                            (byte*)target.Address + (y * target.RowBytes),
                            target.RowBytes,
                            sourceRowBytes);
                    }
                }
            }
        }

        var old = _liveBitmap;
        _liveBitmap = bitmap;
        Source = bitmap;
        IsFrameLoaded = true;
        old?.Dispose();
    }

    private void CancelLiveRender()
    {
        var cancellation = Interlocked.Exchange(ref _liveCancellation, null);
        if (cancellation is null)
        {
            return;
        }
        cancellation.Cancel();
        cancellation.Dispose();
    }

    private void DisposeLiveBitmap()
    {
        var bitmap = _liveBitmap;
        _liveBitmap = null;
        bitmap?.Dispose();
    }

    private static FrameSequence LoadFrames(string path)
    {
        if (File.Exists(path))
        {
            return new FrameSequence(new[] { new Bitmap(path) }, 30.0);
        }

        var loaded = new List<Bitmap>();
        try
        {
            var framesPerSecond = ReadFramesPerSecond(path);
            foreach (var framePath in Directory
                         .EnumerateFiles(path, "coldboot-large1-*.png")
                         .OrderBy(static candidate => candidate, StringComparer.OrdinalIgnoreCase))
            {
                loaded.Add(new Bitmap(framePath));
            }

            return new FrameSequence(loaded, framesPerSecond);
        }
        catch (Exception)
        {
            DisposeFrames(loaded);
            throw;
        }
    }

    private static void DisposeFrames(IEnumerable<Bitmap> frames)
    {
        foreach (var frame in frames)
        {
            frame.Dispose();
        }
    }

    private static double ReadFramesPerSecond(string directory)
    {
        try
        {
            var manifestPath = Path.Combine(directory, "sequence.json");
            using var manifest = JsonDocument.Parse(File.ReadAllBytes(manifestPath));
            var value = manifest.RootElement.GetProperty("framesPerSecond").GetDouble();
            return value is >= 1.0 and <= 60.0 ? value : 30.0;
        }
        catch (Exception)
        {
            return 30.0;
        }
    }

    private sealed record FrameSequence(IReadOnlyList<Bitmap> Frames, double FramesPerSecond);
}
