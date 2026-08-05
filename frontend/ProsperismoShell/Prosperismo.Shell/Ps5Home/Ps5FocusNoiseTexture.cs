// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SharpEmu.GUI.SystemAssets.Rco;

namespace SharpEmu.GUI.Ps5Home;

/// <summary>
/// Sony's <c>image_focus_noise</c>, read in place from
/// <c>Sce.PlayStation.PUI_UI3.rco</c>. The 4.03 asset is a 64x64 indexed PNG.
/// Nothing is extracted or persisted by this loader.
/// </summary>
internal static class Ps5FocusNoiseTexture
{
    public const string ResourceId = "image_focus_noise";

    private static readonly object Gate = new();
    private static byte[]? _payload;
    private static byte[]? _rgba;
    private static double[]? _samples;
    private static int _width;
    private static int _height;
    private static bool _probed;

    public static byte[]? TryGetPayload()
    {
        EnsureLoaded();
        return _payload;
    }

    internal static bool TryGetRgba(out ReadOnlyMemory<byte> rgba, out int width, out int height)
    {
        EnsureLoaded();
        lock (Gate)
        {
            rgba = _rgba;
            width = _width;
            height = _height;
            return _rgba is { Length: > 0 } && width > 0 && height > 0;
        }
    }

    /// <summary>
    /// Drops both a successful decode and a failed path probe. Call this when
    /// the firmware location changes so a process that started without its RCO
    /// does not remain on the constant fallback for its whole lifetime.
    /// </summary>
    internal static void Invalidate()
    {
        lock (Gate)
        {
            _payload = null;
            _rgba = null;
            _samples = null;
            _width = 0;
            _height = 0;
            _probed = false;
        }
    }

    /// <summary>
    /// Linearly samples the single-channel field with PSM's texture state.
    /// FocusRenderManager changes the filter to Linear but never changes the
    /// default ClampToEdge wrap mode.
    /// </summary>
    public static double Sample(double u, double v)
    {
        EnsureLoaded();
        if (_samples is not { Length: > 0 } samples || _width <= 0 || _height <= 0)
        {
            return 0.5;
        }

        return SampleClampLinear(samples, _width, _height, u, v);
    }

    /// <summary>
    /// PSM's normalized Linear + ClampToEdge sample. Normalized texel centres
    /// are at <c>(n + 0.5) / size</c>, hence the half-texel shift before the
    /// bilinear footprint is clamped to the edge.
    /// </summary>
    internal static double SampleClampLinear(
        IReadOnlyList<double> samples,
        int width,
        int height,
        double u,
        double v)
    {
        if (width <= 0 || height <= 0 || samples.Count < width * height)
        {
            return 0.5;
        }

        u = Math.Clamp(u, 0.0, 1.0);
        v = Math.Clamp(v, 0.0, 1.0);

        double fx = (u * width) - 0.5;
        double fy = (v * height) - 0.5;
        int floorX = (int)Math.Floor(fx);
        int floorY = (int)Math.Floor(fy);
        int x0 = Math.Clamp(floorX, 0, width - 1);
        int y0 = Math.Clamp(floorY, 0, height - 1);
        int x1 = Math.Clamp(floorX + 1, 0, width - 1);
        int y1 = Math.Clamp(floorY + 1, 0, height - 1);
        double tx = fx - Math.Floor(fx);
        double ty = fy - Math.Floor(fy);

        double a = Lerp(samples[(y0 * width) + x0], samples[(y0 * width) + x1], tx);
        double b = Lerp(samples[(y1 * width) + x0], samples[(y1 * width) + x1], tx);
        return Lerp(a, b, ty);
    }

    private static void EnsureLoaded()
    {
        lock (Gate)
        {
            if (_probed)
            {
                return;
            }

            _probed = true;
            var path = Ps5IconLibrary.ResolveContainerPath();
            if (path is null)
            {
                return;
            }

            try
            {
                var container = RcoContainer.Open(path);
                var entry = container.Entries
                    .Where(static item => string.Equals(item.Name, ResourceId, StringComparison.Ordinal))
                    .OrderByDescending(static item => string.Equals(item.SrcLabel, "src", StringComparison.Ordinal))
                    .ThenByDescending(static item => item.DataLength)
                    .FirstOrDefault();
                if (entry is null)
                {
                    return;
                }

                var payload = container.ReadEntryData(entry);
                if (payload.Length < 8 || payload[0] != 0x89 || payload[1] != 0x50)
                {
                    return;
                }

                using var source = new Bitmap(new MemoryStream(payload, writable: false));
                var size = source.PixelSize;
                var copy = new WriteableBitmap(
                    size,
                    new Vector(96, 96),
                    PixelFormat.Bgra8888,
                    AlphaFormat.Unpremul);

                var field = new double[size.Width * size.Height];
                var rgba = new byte[size.Width * size.Height * 4];
                using (var framebuffer = copy.Lock())
                {
                    source.CopyPixels(framebuffer, AlphaFormat.Unpremul);
                    unsafe
                    {
                        for (int y = 0; y < size.Height; y++)
                        {
                            byte* row = (byte*)framebuffer.Address + (y * framebuffer.RowBytes);
                            for (int x = 0; x < size.Width; x++)
                            {
                                byte* pixel = row + (x * 4);
                                field[(y * size.Width) + x] =
                                    ((pixel[2] * 0.2126) + (pixel[1] * 0.7152) + (pixel[0] * 0.0722)) / 255.0;
                                var offset = ((y * size.Width) + x) * 4;
                                rgba[offset] = pixel[2];
                                rgba[offset + 1] = pixel[1];
                                rgba[offset + 2] = pixel[0];
                                rgba[offset + 3] = pixel[3];
                            }
                        }
                    }
                }

                copy.Dispose();
                _payload = payload;
                _rgba = rgba;
                _samples = field;
                _width = size.Width;
                _height = size.Height;
            }
            catch (Exception)
            {
                _payload = null;
                _rgba = null;
                _samples = null;
                _width = 0;
                _height = 0;
            }
        }
    }

    private static double Lerp(double a, double b, double t) => a + ((b - a) * t);
}
