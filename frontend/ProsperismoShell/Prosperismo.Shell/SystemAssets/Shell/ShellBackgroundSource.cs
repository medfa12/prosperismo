// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using SharpEmu.GUI.SystemAssets.Textures;

namespace SharpEmu.GUI.SystemAssets.Shell;

/// <summary>
/// Non-visual half of <see cref="ShellBackground"/>: resolves which hub
/// background file a title id maps to inside the optional firmware dump and
/// prepares decoded, display-sized pixels. Kept free of control state so all
/// of it is unit-testable without a rendering surface.
/// </summary>
public static class ShellBackgroundSource
{
    /// <summary>Title-id alias that selects the hub default background.</summary>
    public const string DefaultKey = "default";

    /// <summary>
    /// Decode target width. The dump ships 3840x2160 backgrounds; halving them
    /// to 1920 wide keeps the on-screen quality while quartering the memory a
    /// cached backdrop bitmap costs.
    /// </summary>
    public const int TargetDecodeWidth = 1920;

    /// <summary>
    /// Resolves the hub background for a title id: bg_&lt;titleId&gt;.dds when it
    /// exists, else bg_hub_default.dds, else null. Null, empty, whitespace and
    /// "default" title ids all select the hub default; ids that could not name
    /// a file (path separators, invalid characters) are treated the same way
    /// rather than probing outside vsh_asset.
    /// </summary>
    /// <param name="titleId">System title id, e.g. "NPXS40047", or a default alias.</param>
    /// <param name="dumpRoot">Dump root override; defaults to <see cref="RnpsShellAssets.LocateDumpRoot()"/>.</param>
    public static string? ResolvePath(string? titleId, string? dumpRoot = null)
    {
        return RnpsShellAssets.GetHubBackgroundPath(NormalizeTitleId(titleId), dumpRoot);
    }

    /// <summary>
    /// True when hub background art is available, i.e. the dump was located
    /// and carries the default background every lookup can fall back to.
    /// </summary>
    /// <param name="dumpRoot">Dump root override; defaults to <see cref="RnpsShellAssets.LocateDumpRoot()"/>.</param>
    public static bool IsAvailable(string? dumpRoot = null)
    {
        return ResolvePath(titleId: null, dumpRoot) is not null;
    }

    // Maps the default aliases to null and refuses ids that cannot be a plain
    // file-name component, so bg_<id>.dds never escapes the vsh_asset folder.
    internal static string? NormalizeTitleId(string? titleId)
    {
        if (string.IsNullOrWhiteSpace(titleId))
        {
            return null;
        }

        var trimmed = titleId.Trim();
        if (trimmed.Equals(DefaultKey, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            trimmed.Contains('/') || trimmed.Contains('\\'))
        {
            return null;
        }

        return trimmed;
    }

    /// <summary>
    /// Loads a BC7 DDS background and returns RGBA8 pixels no wider than
    /// <paramref name="maxWidth"/>, or null when the file is missing, is not a
    /// decodable DDS, or decoding fails for any other reason. Never throws;
    /// the shell backdrop must degrade, not crash.
    /// </summary>
    public static byte[]? TryLoadRgba(string? path, int maxWidth, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        try
        {
            var image = DdsImage.LoadFile(path);
            if (!image.IsSupported)
            {
                return null;
            }

            var rgba = image.Decode();
            return DownscaleRgba(rgba, image.Width, image.Height, maxWidth, out width, out height);
        }
        catch (Exception)
        {
            // Anything a hostile or truncated file can provoke (bad header,
            // short pixel data, IO errors) ends in the gradient fallback.
            return null;
        }
    }

    /// <summary>
    /// Box-filters RGBA8 pixels down by the largest integer factor that keeps
    /// the result at least <paramref name="maxWidth"/> wide. Images already at
    /// or below the limit are returned unchanged.
    /// </summary>
    /// <param name="rgba">width*height*4 bytes of RGBA8.</param>
    /// <param name="width">Source width in pixels.</param>
    /// <param name="height">Source height in pixels.</param>
    /// <param name="maxWidth">Widest acceptable result.</param>
    /// <param name="scaledWidth">Width of the returned image.</param>
    /// <param name="scaledHeight">Height of the returned image.</param>
    public static byte[] DownscaleRgba(
        byte[] rgba, int width, int height, int maxWidth, out int scaledWidth, out int scaledHeight)
    {
        ArgumentNullException.ThrowIfNull(rgba);
        if (width <= 0 || height <= 0 || maxWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(
                width <= 0 ? nameof(width) : height <= 0 ? nameof(height) : nameof(maxWidth));
        }

        if (rgba.Length < (long)width * height * 4)
        {
            throw new ArgumentException(
                $"Expected {(long)width * height * 4} RGBA bytes, got {rgba.Length}.", nameof(rgba));
        }

        var factor = 1;
        while (width / (factor + 1) >= maxWidth)
        {
            factor++;
        }

        if (factor == 1)
        {
            scaledWidth = width;
            scaledHeight = height;
            return rgba;
        }

        scaledWidth = width / factor;
        scaledHeight = Math.Max(1, height / factor);
        var result = new byte[scaledWidth * scaledHeight * 4];
        var samples = factor * factor;

        for (var y = 0; y < scaledHeight; y++)
        {
            for (var x = 0; x < scaledWidth; x++)
            {
                var r = 0;
                var g = 0;
                var b = 0;
                var a = 0;
                for (var dy = 0; dy < factor; dy++)
                {
                    var row = ((y * factor + dy) * width + x * factor) * 4;
                    for (var dx = 0; dx < factor; dx++)
                    {
                        var src = row + dx * 4;
                        r += rgba[src];
                        g += rgba[src + 1];
                        b += rgba[src + 2];
                        a += rgba[src + 3];
                    }
                }

                var dst = (y * scaledWidth + x) * 4;
                result[dst] = (byte)(r / samples);
                result[dst + 1] = (byte)(g / samples);
                result[dst + 2] = (byte)(b / samples);
                result[dst + 3] = (byte)(a / samples);
            }
        }

        return result;
    }
}
