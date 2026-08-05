// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SharpEmu.GUI.Ps5Home;

namespace SharpEmu.GUI.Controls;

/// <summary>
/// The line pass of the focus highlight, as a distance-field band rather than a
/// stroked rounded rectangle.
/// </summary>
/// <remarks>
/// The line was a <c>DrawRectangle</c> with a <see cref="Pen"/>, which is the one
/// thing the recovered shader is not. A pen follows an outline at a fixed width;
/// the shader evaluates a signed distance and shades a band around the zero
/// crossing. The difference shows at the corners, where a stroke's width is
/// measured along the path and a distance band's is measured perpendicular to it
/// - so a stroke thickens through the arc and the band does not.
///
/// Same field as <see cref="ShellFocusWash"/>, sampled differently: the wash
/// takes everything inside the zero crossing and decays outward, the band takes
/// a fixed width either side of it.
///
/// The band width is <c>ThicknessPixel + lerp(ThicknessPixel, 0, Showing)</c>,
/// which is why the line arrives wider than it settles, and the rect it is
/// measured against is inflated by <c>thickness + offset</c> so the band sits
/// outside the focused item rather than on it.
/// </remarks>
internal static class ShellFocusBand
{
    /// <summary>
    /// Grid the field is evaluated on before upscaling. Higher than the wash's
    /// because a band is a narrow feature and undersampling shows as a wobble
    /// along the edge rather than as a soft gradient.
    /// </summary>
    private const int Grid = 192;

    private static WriteableBitmap? _cache;
    private static double _cacheAspect = double.NaN;
    private static double _cacheBodyRatio = double.NaN;
    private static double _cacheRadiusRatio = double.NaN;
    private static double _cacheBandRatio = double.NaN;
    private static int _cacheNoiseFrame = -1;

    /// <summary>
    /// Draws the band for <paramref name="body"/>.
    /// </summary>
    /// <param name="context">Target.</param>
    /// <param name="body">The focus rect, already inflated by thickness + offset.</param>
    /// <param name="radius">Corner radius, inherited from the focused widget.</param>
    /// <param name="bandWidth">Band width from the timeline.</param>
    /// <param name="alpha">Line opacity from the timeline.</param>
    public static void Render(
        DrawingContext context,
        Rect body,
        double radius,
        double bandWidth,
        double alpha,
        double clock)
    {
        if (alpha <= 0.004 || bandWidth <= 0.0 || body.Width <= 1.0 || body.Height <= 1.0)
        {
            return;
        }

        // The band straddles the edge, so the surface needs room on both sides
        // of it plus a pixel for the antialiasing ramp.
        var margin = (bandWidth * 0.5) + 1.0;
        var target = body.Inflate(margin);

        var bitmap = GetOrBuild(target, body, radius, bandWidth, clock);
        if (bitmap is null)
        {
            return;
        }

        using (context.PushOpacity(Math.Clamp(alpha, 0.0, 1.0)))
        {
            context.DrawImage(bitmap, new Rect(bitmap.Size), target);
        }
    }

    private static WriteableBitmap? GetOrBuild(
        Rect target,
        Rect body,
        double radius,
        double bandWidth,
        double clock)
    {
        var aspect = target.Width / target.Height;
        var targetMinHalf = Math.Min(target.Width, target.Height) / 2.0;
        if (targetMinHalf <= 0.0)
        {
            return null;
        }

        var bodyRatio = Math.Min(body.Width, body.Height) / 2.0 / targetMinHalf;
        var radiusRatio = radius / targetMinHalf;
        var bandRatio = bandWidth / targetMinHalf;
        // FocusRenderManager advances this effect on SecPerFrame (60 Hz).
        // Quantising the CPU surface to 30 Hz made the slow 0.25-rad/s orbit
        // read as a frozen outline, especially on compact focus targets.
        int noiseFrame = ShellFocusPalette.AnimationFrame(clock);

        if (_cache is not null
            && Math.Abs(aspect - _cacheAspect) < 0.01
            && Math.Abs(bodyRatio - _cacheBodyRatio) < 0.005
            && Math.Abs(radiusRatio - _cacheRadiusRatio) < 0.01
            && Math.Abs(bandRatio - _cacheBandRatio) < 0.005
            && noiseFrame == _cacheNoiseFrame)
        {
            return _cache;
        }

        var built = Build(aspect, bodyRatio, radiusRatio, bandRatio, clock);
        if (built is null)
        {
            return null;
        }

        _cache = built;
        _cacheAspect = aspect;
        _cacheBodyRatio = bodyRatio;
        _cacheRadiusRatio = radiusRatio;
        _cacheBandRatio = bandRatio;
        _cacheNoiseFrame = noiseFrame;
        return built;
    }

    private static WriteableBitmap? Build(
        double aspect,
        double bodyRatio,
        double radiusRatio,
        double bandRatio,
        double clock)
    {
        int w = Grid;
        int h = Grid;
        if (aspect > 1.0)
        {
            h = Math.Max(16, (int)Math.Round(Grid / aspect));
        }
        else if (aspect > 0.0)
        {
            w = Math.Max(16, (int)Math.Round(Grid * aspect));
        }

        WriteableBitmap bitmap;
        try
        {
            bitmap = new WriteableBitmap(
                new PixelSize(w, h),
                new Vector(96, 96),
                PixelFormat.Bgra8888,
                AlphaFormat.Premul);
        }
        catch (Exception)
        {
            return null;
        }

        double halfW = (w / 2.0) * bodyRatio;
        double halfH = (h / 2.0) * bodyRatio;
        double minHalf = Math.Min(w, h) / 2.0;
        double radius = radiusRatio * minHalf;
        double band = Math.Max(bandRatio * minHalf, 0.5);
        double half = band * 0.5;

        using (var buffer = bitmap.Lock())
        {
            unsafe
            {
                for (int y = 0; y < h; y++)
                {
                    var row = (byte*)buffer.Address + (y * buffer.RowBytes);
                    double py = (y + 0.5) - (h / 2.0);

                    for (int x = 0; x < w; x++)
                    {
                        // WriteableBitmap memory is pooled and is not promised
                        // to start cleared. The band occupies only a few pixels;
                        // leaving the rest untouched can composite a stale
                        // scanline across the overlay host.
                        var o = x * 4;
                        row[o + 0] = 0;
                        row[o + 1] = 0;
                        row[o + 2] = 0;
                        row[o + 3] = 0;

                        double px = (x + 0.5) - (w / 2.0);

                        var sd = Ps5FocusField.RoundedBoxDistance(px, py, halfW, halfH, radius);

                        // Distance from the band's centreline, not from the
                        // shape - that is what makes the width perpendicular.
                        var d = Math.Abs(sd);
                        var coverage = 1.0 - Ps5FocusField.SmoothStep(half, half + 1.0, d);
                        if (coverage <= 0.0)
                        {
                            continue;
                        }

                        double stX = halfW > 0.0 ? px / halfW : 0.0;
                        double stY = halfH > 0.0 ? py / halfH : 0.0;
                        var (u, v) = ShellFocusPalette.NoiseUv(stX, stY, clock);
                        double noise = Ps5FocusNoiseTexture.Sample(u, v);
                        double tableCoordinate = ShellFocusPalette.LineTableCoordinate(noise);
                        var tint = ShellFocusPalette.LineColorFor(tableCoordinate);

                        // The line shader uses the noise-derived table
                        // coordinate twice: first for RGB, then as the input
                        // to its three-piece tone curve. The resulting scalar
                        // is lerped up from LineMinOpacity and only then
                        // multiplied by the rounded-box coverage. Clamping the
                        // geometric coverage itself to LineMinOpacity made the
                        // whole outline uniformly thick and luminous.
                        double tone = ShellFocusPalette.LineToneCurve(tableCoordinate);
                        double noiseAlpha = ShellFocusPalette.LineMinOpacity
                            + ((1.0 - ShellFocusPalette.LineMinOpacity) * tone);
                        double shaped = Ps5FocusField.ApplyAlphaGamma(
                            coverage * noiseAlpha,
                            ShellFocusPalette.LineAlphaGamma);

                        byte a = (byte)Math.Clamp(shaped * 255.0, 0.0, 255.0);
                        row[o + 0] = (byte)(tint.B * a / 255);
                        row[o + 1] = (byte)(tint.G * a / 255);
                        row[o + 2] = (byte)(tint.R * a / 255);
                        row[o + 3] = a;
                    }
                }
            }
        }

        return bitmap;
    }
}
