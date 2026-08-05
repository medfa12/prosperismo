// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SharpEmu.GUI.SystemAssets.Rco;

namespace SharpEmu.GUI.SystemAssets;

/// <summary>
/// The system-shell icons the launcher can borrow, named after what they mean
/// here rather than after their container entry. The button glyphs are the
/// DualSense keyguide art the shell draws in its own footer hints; the rest are
/// the shell's pictograms.
///
/// Not every value has art: the shell keeps most of its pictogram set as SVG,
/// which nothing here can rasterise, so those icons resolve to null and the
/// caller keeps its own mark. <see cref="ShellIcons.EntryNames"/> lists the ones
/// backed by a bitmap and <see cref="ShellIcons.VectorOnlyEntryNames"/> records
/// the vector entry the others would come from. See docs/ps5-icons.md.
/// </summary>
public enum ShellIcon
{
    /// <summary>Left shoulder button (L1).</summary>
    L1,

    /// <summary>Right shoulder button (R1).</summary>
    R1,

    /// <summary>Left trigger (L2).</summary>
    L2,

    /// <summary>Right trigger (R2).</summary>
    R2,

    /// <summary>Left stick press (L3).</summary>
    L3,

    /// <summary>Right stick press (R3).</summary>
    R3,

    /// <summary>Cross face button.</summary>
    Cross,

    /// <summary>Circle face button.</summary>
    Circle,

    /// <summary>Square face button.</summary>
    Square,

    /// <summary>Triangle face button.</summary>
    Triangle,

    /// <summary>OPTIONS button.</summary>
    OptionsButton,

    /// <summary>CREATE button.</summary>
    CreateButton,

    /// <summary>PS button.</summary>
    PsButton,

    /// <summary>Left stick.</summary>
    LeftStick,

    /// <summary>Right stick.</summary>
    RightStick,

    /// <summary>Gear; the shell's settings pictogram.</summary>
    Settings,

    /// <summary>The shell's "games and apps" pictogram: a pad beside app tiles.</summary>
    Library,

    /// <summary>A bare DualSense silhouette; the shell's "game" pictogram.</summary>
    Controller,

    /// <summary>Storage volume.</summary>
    Storage,

    /// <summary>System / console.</summary>
    System,

    /// <summary>
    /// The art a tile shows when a title has none of its own. This is the
    /// console's own: the home bundle hands its Image
    /// <c>fallbackSource: { uri: "cxml://CommonAssets/iconid_texture_app_fallback" }</c>,
    /// so a tile with no icon is still a tile with Sony's placeholder in it
    /// rather than a glyph of ours standing in for one.
    /// </summary>
    AppFallback,

    /// <summary>Magnifier. Vector-only in the shell; no bitmap ships.</summary>
    Search,

    /// <summary>Play triangle. Vector-only in the shell; no bitmap ships.</summary>
    Launch,

    /// <summary>Folder. Vector-only in the shell; no bitmap ships.</summary>
    Folder,

    /// <summary>Duplicate/copy. Vector-only in the shell; no bitmap ships.</summary>
    Copy,

    /// <summary>Delete. Vector-only in the shell; no bitmap ships.</summary>
    Remove,

    /// <summary>Add to a folder. Vector-only in the shell; no bitmap ships.</summary>
    AddFolder,

    /// <summary>Refresh / re-read. Vector-only in the shell; no bitmap ships.</summary>
    Rescan,
}

/// <summary>
/// Serves the PS5 system shell's own icon art, read at runtime from a
/// user-provided decrypted firmware dump.
///
/// The art lives inside <c>filesystems/system_ex/vsh_asset/Sce.PlayStation.PUI_UI3.rco</c>
/// — the same container <see cref="ShellUiSounds"/> takes the interaction cues
/// from. This class locates it through <see cref="RnpsShellAssets.LocateDumpRoot()"/>,
/// extracts the mapped entries with <see cref="RcoContainer"/> and decodes them
/// to Avalonia bitmaps, caching both the payloads and the decoded images.
///
/// Everything is optional: with no dump <see cref="TryGet"/> returns null for
/// every icon and callers draw their own glyph instead. The first
/// <see cref="Preload"/> kicks the extraction off on a background thread and
/// returns immediately; <see cref="Loaded"/> fires when it finishes so a host can
/// swap its glyphs for the real art. Nothing here throws and nothing blocks the
/// UI thread. The art is only ever read from the user's own disk and is never
/// redistributed with the emulator.
///
/// Only the container's PNG entries are usable. Most of the shell's pictogram
/// set is SVG, which Avalonia cannot rasterise without a rendering dependency
/// this project does not take, so those icons stay unmapped and their entry
/// names are recorded in <see cref="VectorOnlyEntryNames"/> for reference.
/// </summary>
public static class ShellIcons
{
    /// <summary>The shell UI resource container that holds the icon art.</summary>
    public const string ContainerFileName = "Sce.PlayStation.PUI_UI3.rco";

    /// <summary>
    /// Source attribute carrying the high-resolution variant of an entry. Where
    /// an icon has both, this one is taken: the keyguide glyphs ship at 40x32
    /// under <c>src</c> and 80x64 under <c>src_4k</c>, and the launcher draws
    /// them large enough to want the latter.
    /// </summary>
    public const string HighResolutionSrcLabel = "src_4k";

    private static readonly string[] VshAssetSegments = { "filesystems", "system_ex", "vsh_asset" };

    // The container entries backing each icon. Keyguide glyphs are the shell's
    // own footer button art; the emoji_* entries are the pictograms it inlines
    // into text runs, and are the only bitmap form of those symbols in the dump.
    private static readonly IReadOnlyDictionary<ShellIcon, string> Names =
        new Dictionary<ShellIcon, string>
        {
            [ShellIcon.L1] = "image_keyguide_l1",
            [ShellIcon.R1] = "image_keyguide_r1",
            [ShellIcon.L2] = "image_keyguide_l2",
            [ShellIcon.R2] = "image_keyguide_r2",
            [ShellIcon.L3] = "image_keyguide_l3",
            [ShellIcon.R3] = "image_keyguide_r3",
            [ShellIcon.Cross] = "image_keyguide_cross",
            [ShellIcon.Circle] = "image_keyguide_circle",
            [ShellIcon.Square] = "image_keyguide_square",
            [ShellIcon.Triangle] = "image_keyguide_triangle",
            [ShellIcon.OptionsButton] = "image_keyguide_options",
            [ShellIcon.CreateButton] = "image_keyguide_create",
            [ShellIcon.PsButton] = "image_keyguide_ps",
            [ShellIcon.LeftStick] = "image_keyguide_left_stick",
            [ShellIcon.RightStick] = "image_keyguide_right_stick",
            [ShellIcon.Settings] = "emoji_settings",
            [ShellIcon.Library] = "emoji_game_and_apps",
            [ShellIcon.Controller] = "emoji_game",
            [ShellIcon.Storage] = "emoji_storage",
            [ShellIcon.System] = "emoji_system",

            // Not an emoji or a keyguide glyph: this is the 512 square texture
            // the shell itself uses for a title with no art, and it lives in
            // the same PUI_UI3 container as the rest.
            [ShellIcon.AppFallback] = "iconid_texture_app_fallback",
        };

    // File names of the pictograms bundled under assets/icons/shell and linked
    // in as AvaloniaResource. Optional by design: the directory may be deleted,
    // in which case TryGet falls through to the user's dump. Provenance for each
    // file is in docs/ps5-icons.md.
    private static readonly IReadOnlyDictionary<ShellIcon, string> BundledFileNames =
        new Dictionary<ShellIcon, string>
        {
            [ShellIcon.L1] = "pad-l1.png",
            [ShellIcon.R1] = "pad-r1.png",
            [ShellIcon.L2] = "pad-l2.png",
            [ShellIcon.R2] = "pad-r2.png",
            [ShellIcon.L3] = "pad-l3.png",
            [ShellIcon.R3] = "pad-r3.png",
            [ShellIcon.Cross] = "pad-cross.png",
            [ShellIcon.Circle] = "pad-circle.png",
            [ShellIcon.Square] = "pad-square.png",
            [ShellIcon.Triangle] = "pad-triangle.png",
            [ShellIcon.OptionsButton] = "pad-options.png",
            [ShellIcon.CreateButton] = "pad-create.png",
            [ShellIcon.PsButton] = "pad-ps.png",
            [ShellIcon.LeftStick] = "pad-lstick.png",
            [ShellIcon.RightStick] = "pad-rstick.png",
            [ShellIcon.Settings] = "ui-settings.png",
            [ShellIcon.Library] = "ui-library.png",
            [ShellIcon.Controller] = "ui-controller.png",
            [ShellIcon.Storage] = "ui-storage.png",
            [ShellIcon.System] = "ui-system.png",
        };

    // Icons the shell only ships as SVG. Recorded so the inventory in
    // docs/ps5-icons.md has a counterpart in code and so a future rasteriser has
    // the mapping ready; nothing reads these at runtime today.
    private static readonly IReadOnlyDictionary<ShellIcon, string> VectorNames =
        new Dictionary<ShellIcon, string>
        {
            [ShellIcon.Search] = "iconid_search",
            [ShellIcon.Launch] = "iconid_control_play",
            [ShellIcon.Folder] = "iconid_folder",
            [ShellIcon.Copy] = "iconid_copy",
            [ShellIcon.Remove] = "iconid_delete",
            [ShellIcon.AddFolder] = "iconid_add_folder",
            [ShellIcon.Rescan] = "iconid_update",
        };

    private static readonly object Gate = new();
    private static readonly Dictionary<ShellIcon, Bitmap> Decoded = new();

    private static IReadOnlyDictionary<ShellIcon, byte[]>? _payloads;
    private static bool _loadStarted;

    /// <summary>
    /// Raised once, on a background thread, after a load finishes — whether or
    /// not it found anything. A host subscribes to swap its fallback glyphs for
    /// the real art, and must marshal to the UI thread itself.
    /// </summary>
    public static event EventHandler? Loaded;

    /// <summary>The icon to container-entry-name mapping for the icons that have bitmap art.</summary>
    public static IReadOnlyDictionary<ShellIcon, string> EntryNames => Names;

    /// <summary>
    /// The icons the shell only ships in vector form, mapped to the SVG entry
    /// they would come from. These never resolve through <see cref="TryGet"/>.
    /// </summary>
    public static IReadOnlyDictionary<ShellIcon, string> VectorOnlyEntryNames => VectorNames;

    /// <summary>True once the icons have been extracted (or found to be unavailable).</summary>
    public static bool IsLoaded => Volatile.Read(ref _payloads) is not null;

    /// <summary>Number of icons actually extracted; zero when no dump is present.</summary>
    public static int LoadedCount => Volatile.Read(ref _payloads)?.Count ?? 0;

    /// <summary>True once a load has been kicked off and not since <see cref="Reset"/>.</summary>
    internal static bool LoadStarted
    {
        get
        {
            lock (Gate)
            {
                return _loadStarted;
            }
        }
    }

    /// <summary>True when a dump containing the shell UI resource container was located.</summary>
    /// <param name="dumpRoot">Dump root override; defaults to <see cref="RnpsShellAssets.LocateDumpRoot()"/>.</param>
    public static bool IsAvailable(string? dumpRoot = null) => LocateContainer(dumpRoot) is not null;

    /// <summary>
    /// Absolute path to the shell UI resource container inside the dump, or null
    /// when the dump or the file is absent.
    /// </summary>
    /// <param name="dumpRoot">Dump root override; defaults to <see cref="RnpsShellAssets.LocateDumpRoot()"/>.</param>
    public static string? LocateContainer(string? dumpRoot = null)
    {
        try
        {
            var root = dumpRoot ?? RnpsShellAssets.LocateDumpRoot();
            if (root is null)
            {
                return null;
            }

            var path = Path.Combine(root, Path.Combine(VshAssetSegments), ContainerFileName);
            return File.Exists(path) ? path : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Starts extracting and decoding the icons on a background thread if that
    /// has not happened yet. Returns immediately and is safe to call repeatedly.
    /// <see cref="Loaded"/> fires when the work is done.
    /// </summary>
    /// <param name="dumpRoot">Dump root override; defaults to <see cref="RnpsShellAssets.LocateDumpRoot()"/>.</param>
    public static void Preload(string? dumpRoot = null)
    {
        lock (Gate)
        {
            if (_loadStarted)
            {
                return;
            }

            _loadStarted = true;
        }

        _ = Task.Run(() =>
        {
            var payloads = LoadPayloads(LocateContainer(dumpRoot));

            // Decoding here keeps the first paint off the PNG decoder. It needs
            // a live rendering backend, so a very early preload can fail; the
            // payloads stay cached and TryGet retries lazily.
            foreach (var pair in payloads)
            {
                TryDecode(pair.Key, pair.Value);
            }

            Volatile.Write(ref _payloads, payloads);

            try
            {
                Loaded?.Invoke(null, EventArgs.Empty);
            }
            catch (Exception)
            {
                // A host that throws while refreshing does not take the loader
                // down with it.
            }
        });
    }

    /// <summary>
    /// The decoded art for an icon, or null when there is no dump, no bitmap
    /// entry for that icon, or the load has not finished. Never blocks on the
    /// load and never throws, so a caller can ask on every layout pass and fall
    /// back to its own glyph whenever the answer is null.
    /// </summary>
    /// <param name="icon">Which icon to fetch.</param>
    public static IImage? TryGet(ShellIcon icon)
    {
        lock (Gate)
        {
            if (Decoded.TryGetValue(icon, out var cached))
            {
                return cached;
            }
        }

        // Prefer the pictograms bundled with the build; they need no dump and no
        // extraction. The directory is optional, so a miss here is normal and
        // simply falls through to the dump.
        if (TryLoadBundled(icon) is { } bundled)
        {
            return bundled;
        }

        var payloads = Volatile.Read(ref _payloads);
        return payloads is not null && payloads.TryGetValue(icon, out var bytes)
            ? TryDecode(icon, bytes)
            : null;
    }

    /// <summary>
    /// Loads a pictogram shipped as an <c>AvaloniaResource</c> under
    /// <c>Assets/ShellIcons</c>. Returns null when the icon has no bundled file,
    /// which is expected: the directory is optional and may be deleted.
    /// </summary>
    private static IImage? TryLoadBundled(ShellIcon icon)
    {
        if (!BundledFileNames.TryGetValue(icon, out var fileName))
        {
            return null;
        }

        try
        {
            var uri = new Uri($"avares://Prosperismo.Shell/Assets/ShellIcons/{fileName}");
            if (!AssetLoader.Exists(uri))
            {
                return null;
            }

            using var stream = AssetLoader.Open(uri);
            var bitmap = new Bitmap(stream);
            lock (Gate)
            {
                Decoded[icon] = bitmap;
            }

            return bitmap;
        }
        catch (Exception)
        {
            // A missing or unreadable bundled icon is never fatal.
            return null;
        }
    }

    /// <summary>
    /// Drops the extracted payloads, the decoded bitmaps and the "already
    /// loaded" latch so the next <see cref="Preload"/> re-reads the dump. Used
    /// by tests and by a settings change that repoints the dump root.
    /// </summary>
    public static void Reset()
    {
        lock (Gate)
        {
            _loadStarted = false;
            foreach (var bitmap in Decoded.Values)
            {
                try
                {
                    bitmap.Dispose();
                }
                catch (Exception)
                {
                    // A bitmap still referenced by a live visual is left alone.
                }
            }

            Decoded.Clear();
        }

        Volatile.Write(ref _payloads, null);
    }

    /// <summary>
    /// Extracts the raw PNG bytes of every mapped icon from a shell UI resource
    /// container. Returns an empty map when the path is null, unreadable, or not
    /// a container with the expected entries; it never throws.
    ///
    /// Where an entry appears under several source attributes the
    /// <see cref="HighResolutionSrcLabel"/> variant wins, and the largest
    /// payload breaks any remaining tie. Non-PNG payloads are skipped, so an
    /// icon the dump only carries as SVG is simply absent from the result.
    /// </summary>
    /// <param name="containerPath">Path to Sce.PlayStation.PUI_UI3.rco, or null.</param>
    public static IReadOnlyDictionary<ShellIcon, byte[]> LoadPayloads(string? containerPath)
    {
        var payloads = new Dictionary<ShellIcon, byte[]>();
        if (string.IsNullOrEmpty(containerPath))
        {
            return payloads;
        }

        try
        {
            var container = RcoContainer.Open(containerPath);

            // The container holds a thousand-odd entries; index the ones we want
            // by name so the mapping is a single pass.
            var wanted = new Dictionary<string, ShellIcon>(StringComparer.Ordinal);
            foreach (var pair in Names)
            {
                wanted[pair.Value] = pair.Key;
            }

            var ranks = new Dictionary<ShellIcon, (bool HighRes, long Length)>();
            foreach (var entry in container.Entries)
            {
                if (entry.Name is null || !wanted.TryGetValue(entry.Name, out var icon))
                {
                    continue;
                }

                var highRes = string.Equals(entry.SrcLabel, HighResolutionSrcLabel, StringComparison.Ordinal);
                if (ranks.TryGetValue(icon, out var best) &&
                    (best.HighRes, best.Length).CompareTo((highRes, entry.DataLength)) >= 0)
                {
                    continue;
                }

                var payload = LooksLikePng(container.ReadEntryData(entry));
                if (payload is null)
                {
                    continue;
                }

                payloads[icon] = payload;
                ranks[icon] = (highRes, entry.DataLength);
            }
        }
        catch (Exception)
        {
            // A missing, truncated or unexpected container just means no icons.
        }

        return payloads;
    }

    /// <summary>
    /// Returns <paramref name="payload"/> when it starts with the PNG signature,
    /// else null. The container also carries SVG, DDS and audio payloads under
    /// entry names that look alike, and only PNG is decodable here.
    /// </summary>
    internal static byte[]? LooksLikePng(byte[]? payload)
    {
        if (payload is null || payload.Length < 8)
        {
            return null;
        }

        return payload[0] == 0x89 && payload[1] == 0x50 && payload[2] == 0x4E && payload[3] == 0x47 &&
               payload[4] == 0x0D && payload[5] == 0x0A && payload[6] == 0x1A && payload[7] == 0x0A
            ? payload
            : null;
    }

    private static Bitmap? TryDecode(ShellIcon icon, byte[] payload)
    {
        try
        {
            using var stream = new MemoryStream(payload, writable: false);
            var bitmap = new Bitmap(stream);

            lock (Gate)
            {
                if (Decoded.TryGetValue(icon, out var existing))
                {
                    bitmap.Dispose();
                    return existing;
                }

                Decoded[icon] = bitmap;
            }

            return bitmap;
        }
        catch (Exception)
        {
            // No rendering backend yet, or a payload the decoder rejects: the
            // caller falls back to its own glyph and a later call may succeed.
            return null;
        }
    }
}
