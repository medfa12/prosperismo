// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using Avalonia.Media.Imaging;
using SharpEmu.Libs.Textures;

namespace SharpEmu.GUI.SystemAssets.Textures;

/// <summary>
/// A decoded still backdrop: row-major RGBA8 plus the size it ended up.
/// </summary>
/// <param name="Rgba">Width*Height*4 bytes, R,G,B,A order.</param>
/// <param name="Width">Width in pixels after any downscale.</param>
/// <param name="Height">Height in pixels after any downscale.</param>
/// <param name="SourcePath">The .dds this came from, for diagnostics.</param>
public sealed record ShellStaticBackgroundImage(byte[] Rgba, int Width, int Height, string SourcePath);

/// <summary>
/// The console's own still hub wallpapers (bg_hub_default.dds and the per-app
/// bg_NPXS4xxxx.dds beside it), for people who want the shell to hold still.
///
/// A frozen simulation is not a still image: stopping the animated backdrop
/// leaves whatever frame it happened to be on, which is arbitrary. This loads
/// the wallpaper the console itself shows instead, so motion-off is a
/// deliberate look rather than a paused one.
///
/// The files are 3840x2160 BC7, which is 33 MB of RGBA at full size and a
/// second or two of CPU to decode, so <see cref="Load"/> downscales to a
/// sensible width, caches the result, and is meant to be called off the UI
/// thread. Nothing throws and nothing is logged: with no dump every entry point
/// returns null and the caller keeps its gradient.
///
/// The BC7 and DDS decoding is the existing <see cref="DdsImage"/> and
/// <see cref="Bc7Decoder"/> path from the shell icon work; nothing new was
/// written for it here.
/// </summary>
public static class ShellStaticBackground
{
    /// <summary>
    /// Width the wallpaper is reduced to by default. A backdrop sits behind
    /// blurred UI at desktop-window sizes, so full 4K costs memory nobody sees.
    /// </summary>
    public const int DefaultMaxWidth = 1920;

    private static readonly object Gate = new();
    private static ShellStaticBackgroundImage? _cached;
    private static string? _cachedKey;

    /// <summary>True when a dump with a usable still wallpaper was located.</summary>
    /// <param name="titleId">System title id for a per-app wallpaper; null for the default.</param>
    /// <param name="dumpRoot">Dump root override; defaults to the located dump.</param>
    public static bool IsAvailable(string? titleId = null, string? dumpRoot = null) =>
        LocatePath(titleId, dumpRoot) is not null;

    /// <summary>
    /// Absolute path to the still wallpaper, falling back from the per-app
    /// image to bg_hub_default.dds, or null when the dump is absent.
    /// </summary>
    /// <param name="titleId">System title id for a per-app wallpaper; null for the default.</param>
    /// <param name="dumpRoot">Dump root override; defaults to the located dump.</param>
    public static string? LocatePath(string? titleId = null, string? dumpRoot = null)
    {
        try
        {
            return RnpsShellAssets.GetHubBackgroundPath(titleId, dumpRoot);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Decodes the still wallpaper to RGBA8, downscaled to at most
    /// <paramref name="maxWidth"/>. Blocking and slow enough to matter: call it
    /// from <see cref="LoadAsync"/> or another background thread. Repeated calls
    /// for the same image return the cached result. Returns null when no dump,
    /// no file, or an unsupported format.
    /// </summary>
    /// <param name="titleId">System title id for a per-app wallpaper; null for the default.</param>
    /// <param name="maxWidth">Longest edge to keep, in pixels; 0 or less keeps full size.</param>
    /// <param name="dumpRoot">Dump root override; defaults to the located dump.</param>
    public static ShellStaticBackgroundImage? Load(
        string? titleId = null, int maxWidth = DefaultMaxWidth, string? dumpRoot = null)
    {
        var path = LocatePath(titleId, dumpRoot);
        if (path is null)
        {
            return null;
        }

        var key = path + "|" + maxWidth.ToString(System.Globalization.CultureInfo.InvariantCulture);
        lock (Gate)
        {
            if (_cachedKey == key && _cached is not null)
            {
                return _cached;
            }
        }

        var image = LoadFile(path, maxWidth);
        if (image is null)
        {
            return null;
        }

        lock (Gate)
        {
            _cached = image;
            _cachedKey = key;
        }

        return image;
    }

    /// <summary>Runs <see cref="Load"/> on a background task.</summary>
    /// <param name="titleId">System title id for a per-app wallpaper; null for the default.</param>
    /// <param name="maxWidth">Longest edge to keep, in pixels.</param>
    /// <param name="dumpRoot">Dump root override; defaults to the located dump.</param>
    public static Task<ShellStaticBackgroundImage?> LoadAsync(
        string? titleId = null, int maxWidth = DefaultMaxWidth, string? dumpRoot = null) =>
        Task.Run(() => Load(titleId, maxWidth, dumpRoot));

    /// <summary>
    /// Decodes a specific .dds file, bypassing dump lookup and the cache.
    /// Returns null for anything it cannot read or does not support.
    /// </summary>
    /// <param name="path">Absolute path to a BC7 DDS file.</param>
    /// <param name="maxWidth">Longest edge to keep, in pixels; 0 or less keeps full size.</param>
    public static ShellStaticBackgroundImage? LoadFile(string? path, int maxWidth = DefaultMaxWidth)
    {
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        try
        {
            var dds = DdsImage.LoadFile(path);
            if (!dds.IsSupported)
            {
                return null;
            }

            var rgba = dds.Decode();
            int width = dds.Width;
            int height = dds.Height;

            if (maxWidth > 0 && width > maxWidth)
            {
                rgba = Downscale(rgba, width, height, maxWidth, out width, out height);
            }

            return rgba.Length == 0 ? null : new ShellStaticBackgroundImage(rgba, width, height, path);
        }
        catch (Exception)
        {
            // A missing, truncated or unexpected wallpaper just means the
            // caller keeps its own backdrop.
            return null;
        }
    }

    /// <summary>
    /// Box-averages RGBA8 down to <paramref name="targetWidth"/>, keeping the
    /// aspect ratio. Averaging rather than point sampling matters here: these
    /// wallpapers are soft gradients with fine grain, and dropping pixels turns
    /// the grain into visible speckle.
    /// </summary>
    /// <param name="rgba">Row-major RGBA8 source.</param>
    /// <param name="width">Source width.</param>
    /// <param name="height">Source height.</param>
    /// <param name="targetWidth">Desired width; larger than the source is a no-op.</param>
    /// <param name="outWidth">Resulting width.</param>
    /// <param name="outHeight">Resulting height.</param>
    public static byte[] Downscale(
        byte[]? rgba, int width, int height, int targetWidth, out int outWidth, out int outHeight)
    {
        outWidth = width;
        outHeight = height;

        if (rgba is null || width <= 0 || height <= 0 || (long)width * height * 4 > rgba.Length)
        {
            return Array.Empty<byte>();
        }

        if (targetWidth <= 0 || targetWidth >= width)
        {
            return rgba;
        }

        int newWidth = targetWidth;
        int newHeight = Math.Max(1, (int)((long)height * targetWidth / width));
        var output = new byte[(long)newWidth * newHeight * 4];

        for (int y = 0; y < newHeight; y++)
        {
            int sourceTop = (int)((long)y * height / newHeight);
            int sourceBottom = Math.Max(sourceTop + 1, (int)((long)(y + 1) * height / newHeight));

            for (int x = 0; x < newWidth; x++)
            {
                int sourceLeft = (int)((long)x * width / newWidth);
                int sourceRight = Math.Max(sourceLeft + 1, (int)((long)(x + 1) * width / newWidth));

                int r = 0, g = 0, b = 0, a = 0, count = 0;
                for (int sy = sourceTop; sy < sourceBottom; sy++)
                {
                    int row = sy * width * 4;
                    for (int sx = sourceLeft; sx < sourceRight; sx++)
                    {
                        int index = row + (sx * 4);
                        r += rgba[index];
                        g += rgba[index + 1];
                        b += rgba[index + 2];
                        a += rgba[index + 3];
                        count++;
                    }
                }

                int target = ((y * newWidth) + x) * 4;
                output[target] = (byte)(r / count);
                output[target + 1] = (byte)(g / count);
                output[target + 2] = (byte)(b / count);
                output[target + 3] = (byte)(a / count);
            }
        }

        outWidth = newWidth;
        outHeight = newHeight;
        return output;
    }

    /// <summary>Drops the cached wallpaper, for a settings change or a test.</summary>
    public static void ClearCache()
    {
        lock (Gate)
        {
            _cached = null;
            _cachedKey = null;
        }
    }

    /// <summary>
    /// Wraps a decoded wallpaper in an Avalonia bitmap, or returns null if the
    /// render stack cannot make one yet. Kept separate from decoding so the
    /// decode path stays usable without a UI.
    /// </summary>
    public static WriteableBitmap? ToBitmap(this ShellStaticBackgroundImage? image)
    {
        if (image is null)
        {
            return null;
        }

        try
        {
            return DdsImageAvalonia.CreateBitmap(image.Rgba, image.Width, image.Height);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
