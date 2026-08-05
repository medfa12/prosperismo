// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using SharpEmu.GUI.SystemAssets.Textures;

namespace SharpEmu.GUI.SystemAssets.Shell;

/// <summary>
/// Keeps a few of the console's hub wallpapers decoded and to hand.
///
/// The dump ships fifteen of these and every one is a 3840x2160 BC7 file: 33 MB
/// of RGBA and a second or two of CPU each. Decoding one on the frame that
/// selects it would stall the shell, and holding all fifteen at full size would
/// cost half a gigabyte, so this does neither. Wallpapers are decoded off the UI
/// thread, downscaled on the way in, and the least recently touched one is
/// dropped once the cache is full.
///
/// A transition needs two resident at once - the one leaving and the one
/// arriving - and a user scrubbing back and forth along the strand wants the
/// ones either side of them still warm, which is what sets the default capacity.
///
/// Every entry point is safe to call with no dump present: nothing throws,
/// nothing is logged, and the caller simply never gets an image.
/// </summary>
public sealed class ShellHubWallpaperCache
{
    /// <summary>
    /// Wallpapers held decoded at once. Four covers the pair a transition needs
    /// plus the neighbours on either side of a scrub.
    /// </summary>
    public const int DefaultCapacity = 4;

    /// <summary>
    /// Width wallpapers are decoded down to.
    /// </summary>
    /// <remarks>
    /// These are soft, near-abstract images that end up behind a basemat and a
    /// row of tiles, and they are never the sharpest thing on screen. 1280 is
    /// three megabytes an entry against nineteen at native width, and at normal
    /// viewing distance the difference is not visible through the mat.
    /// </remarks>
    public const int DecodeWidth = 1280;

    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly int _capacity;
    private long _clock;

    /// <summary>Builds a cache.</summary>
    /// <param name="capacity">Wallpapers held decoded at once; clamped to at least 2.</param>
    public ShellHubWallpaperCache(int capacity = DefaultCapacity)
    {
        _capacity = Math.Max(2, capacity);
    }

    /// <summary>Wallpapers currently decoded and resident.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _entries.Count(pair => pair.Value.Image is not null);
            }
        }
    }

    /// <summary>
    /// Resolves a title id to the wallpaper file that backs it, falling back to
    /// the hub default, or null with no dump. Two title ids with no art of their
    /// own resolve to the same path and therefore to one cache entry.
    /// </summary>
    /// <param name="titleId">System title id, or null for the hub default.</param>
    /// <param name="dumpRoot">Dump root override.</param>
    public static string? ResolvePath(string? titleId, string? dumpRoot = null) =>
        ShellBackgroundSource.ResolvePath(titleId, dumpRoot);

    /// <summary>
    /// The decoded wallpaper for a path if it is resident, else null. Touching
    /// an entry marks it recently used. Never blocks on a decode.
    /// </summary>
    /// <param name="path">Wallpaper path, as returned by <see cref="ResolvePath"/>.</param>
    public ShellStaticBackgroundImage? TryGet(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        lock (_gate)
        {
            if (!_entries.TryGetValue(path, out var entry) || entry.Image is null)
            {
                return null;
            }

            entry.Touched = ++_clock;
            return entry.Image;
        }
    }

    /// <summary>
    /// Ensures a wallpaper is decoded, off the UI thread. Returns the image once
    /// it is available, or null when it could not be read. Repeated calls for a
    /// path already in flight join the decode already running rather than
    /// starting a second one.
    /// </summary>
    /// <param name="path">Wallpaper path, as returned by <see cref="ResolvePath"/>.</param>
    public Task<ShellStaticBackgroundImage?> RequestAsync(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return Task.FromResult<ShellStaticBackgroundImage?>(null);
        }

        lock (_gate)
        {
            if (_entries.TryGetValue(path, out var existing))
            {
                existing.Touched = ++_clock;
                return existing.Load;
            }

            var entry = new Entry { Touched = ++_clock };
            _entries[path] = entry;

            // Held on the entry so a second request joins this decode. The
            // continuation publishes the result under the same lock the readers
            // take, so TryGet never sees a half-populated entry.
            entry.Load = Task.Run(() =>
            {
                var image = ShellStaticBackground.LoadFile(path, DecodeWidth);
                lock (_gate)
                {
                    entry.Image = image;
                    Evict();
                }

                return image;
            });

            return entry.Load;
        }
    }

    /// <summary>Drops everything. For a settings change or a test.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
        }
    }

    // Least recently touched wins. Entries whose decode has not finished are
    // never evicted: a request in flight has a caller waiting on it, and
    // dropping it would leave that caller waiting on a task nothing completes.
    private void Evict()
    {
        while (_entries.Count > _capacity)
        {
            string? oldest = null;
            var oldestTouch = long.MaxValue;

            foreach (var pair in _entries)
            {
                if (pair.Value.Load is { IsCompleted: false })
                {
                    continue;
                }

                if (pair.Value.Touched < oldestTouch)
                {
                    oldestTouch = pair.Value.Touched;
                    oldest = pair.Key;
                }
            }

            if (oldest is null)
            {
                return;
            }

            _entries.Remove(oldest);
        }
    }

    private sealed class Entry
    {
        public ShellStaticBackgroundImage? Image;
        public Task<ShellStaticBackgroundImage?> Load = Task.FromResult<ShellStaticBackgroundImage?>(null);
        public long Touched;
    }
}
