// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System.Text;
using SharpEmu.GUI.SystemAssets;
using SharpEmu.GUI.SystemAssets.Rco;

namespace SharpEmu.GUI.Ps5Home;

/// <summary>
/// The shell's real icon set, read out of the user's dump at run time.
///
/// <para><c>cxml://ui3/iconid_&lt;name&gt;</c> resolves to
/// <c>system_ex/vsh_asset/Sce.PlayStation.PUI_UI3.rco</c>
/// (<c>IconPS.ps.js</c> <c>getImageProps</c>). That container holds 897 texture
/// nodes: 710 <c>iconid_*</c>, 114 <c>emoji_*</c> and 73 <c>image_*</c>. Of the
/// 710 icon ids, <b>671 are SVG and 39 are PNG</b> — the widely repeated "77
/// PNG" is a blob-row count, not a node count, and the audit corrects it
/// (defect E1). This class serves the 671 vectors; the 39 raster badges are a
/// separate path and are deliberately not faked here.</para>
///
/// <para><c>Sce.PlayStation.PUI.rco</c> is <em>not</em> the icon library. Its
/// 7,192 texture nodes are 7,089 pre-rendered emoji, 57 button glyphs and 46
/// chrome nine-patches. Pointing an icon lookup at it finds nothing, which is
/// the correct outcome and a common wrong turn.</para>
///
/// <para><b>Asset policy.</b> Nothing is extracted to disk and nothing is
/// cached outside this process. The container is opened read-only, in place,
/// from the dump.</para>
/// </summary>
public sealed class Ps5IconLibrary
{
    /// <summary>The container <c>cxml://ui3/</c> names, relative to the dump root.</summary>
    // Built from segments, not a backslash literal: Path.Combine does not split
    // on '\\' on POSIX hosts, so the literal form resolved to a single bogus
    // filename there and the container was never found off Windows.
    public static readonly string RelativeContainerPath = Path.Combine(
        "filesystems", "system_ex", "vsh_asset", "Sce.PlayStation.PUI_UI3.rco");

    /// <summary>Environment variable pointing directly at a <c>PUI_UI3.rco</c>.</summary>
    public const string ContainerOverrideVariable = "SHARPEMU_PS5_UI3_RCO";

    /// <summary>The id prefix every shell pictogram carries.</summary>
    public const string IconIdPrefix = "iconid_";

    /// <summary><c>IconPS.ps.js</c>'s default draw box, before any style override.</summary>
    public const double DefaultIconSize = 64;

    private static readonly object Gate = new();
    private static Ps5IconLibrary? _shared;
    private static bool _probed;

    private readonly RcoContainer _container;
    private readonly Dictionary<string, RcoEntry> _svgEntries;
    private readonly Dictionary<string, Ps5VectorIcon?> _parsed = new(StringComparer.Ordinal);

    private Ps5IconLibrary(string path, RcoContainer container, Dictionary<string, RcoEntry> svgEntries)
    {
        ContainerPath = path;
        _container = container;
        _svgEntries = svgEntries;
    }

    /// <summary>Absolute path of the container this library is serving.</summary>
    public string ContainerPath { get; }

    /// <summary>Ids of every vector icon in the container, unordered.</summary>
    public IReadOnlyCollection<string> VectorIconIds => _svgEntries.Keys;

    /// <summary>How many vector icons were indexed. 671 on a stock 4.03 dump.</summary>
    public int VectorIconCount => _svgEntries.Count;

    /// <summary>
    /// The shared library, or null when no dump is reachable. Null is a normal,
    /// supported state: callers show a visible placeholder, never a substitute
    /// pictogram drawn by us.
    /// </summary>
    public static Ps5IconLibrary? Shared
    {
        get
        {
            lock (Gate)
            {
                if (_probed)
                {
                    return _shared;
                }

                _probed = true;
                _shared = TryOpen(ResolveContainerPath());
                return _shared;
            }
        }
    }

    /// <summary>Drops the shared library so the next access probes the dump again.</summary>
    public static void Invalidate()
    {
        lock (Gate)
        {
            _probed = false;
            _shared = null;
        }

        // The focus shader's image_focus_noise lives in this same container.
        // A failed probe is cached independently, so invalidate it with the
        // icon index when a caller changes the firmware/RCO location.
        Ps5FocusNoiseTexture.Invalidate();
    }

    /// <summary>
    /// Where the container is expected: the explicit override first, then the
    /// located dump. Returns null when neither exists.
    /// </summary>
    /// <param name="dumpRoot">Dump root override; defaults to the located one.</param>
    public static string? ResolveContainerPath(string? dumpRoot = null)
    {
        var overridden = Environment.GetEnvironmentVariable(ContainerOverrideVariable);
        if (!string.IsNullOrWhiteSpace(overridden) && File.Exists(overridden.Trim()))
        {
            return overridden.Trim();
        }

        var root = dumpRoot ?? RnpsShellAssets.LocateDumpRoot();
        if (string.IsNullOrWhiteSpace(root))
        {
            return null;
        }

        var path = Path.Combine(root, RelativeContainerPath);
        return File.Exists(path) ? path : null;
    }

    /// <summary>
    /// Opens a container and indexes its vector icons, or returns null when the
    /// path is missing or the file is not a readable RCO.
    /// </summary>
    /// <param name="path">Absolute path to a <c>PUI_UI3.rco</c>, or null.</param>
    public static Ps5IconLibrary? TryOpen(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            var container = RcoContainer.Open(path);
            var index = new Dictionary<string, RcoEntry>(StringComparer.Ordinal);
            foreach (var entry in container.Entries)
            {
                if (entry.Name is not { Length: > 0 } name ||
                    !name.StartsWith(IconIdPrefix, StringComparison.Ordinal) ||
                    entry.DataLength <= 0)
                {
                    continue;
                }

                // Key on the blob's own content, not on the declared type or on
                // the slot's position: the audit found the heap order unstable
                // (some nodes store src_4k before src), and the 39 raster icons
                // sit in the same id namespace as the 671 vectors.
                if (!LooksLikeSvg(container, entry))
                {
                    continue;
                }

                // First writer wins, which keeps src ahead of src_lv1/src_lv2.
                index.TryAdd(name, entry);
            }

            return index.Count == 0 ? null : new Ps5IconLibrary(path, container, index);
        }
        catch (Exception)
        {
            // A user's dump is untrusted input; an unreadable container means
            // "no icons", never a crash on the way to the home screen.
            return null;
        }
    }

    /// <summary>
    /// Parses and caches one vector icon. <paramref name="id"/> may be given with
    /// or without the <c>iconid_</c> prefix, since the JS registry uses the bare
    /// name and the container uses the prefixed one. Returns null when the id is
    /// not a vector icon in this container.
    /// </summary>
    /// <param name="id">Icon id, e.g. "trophies" or "iconid_trophies".</param>
    public Ps5VectorIcon? TryGet(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        var key = Normalize(id);
        lock (_parsed)
        {
            if (_parsed.TryGetValue(key, out var cached))
            {
                return cached;
            }
        }

        Ps5VectorIcon? icon = null;
        if (_svgEntries.TryGetValue(key, out var entry))
        {
            try
            {
                var bytes = _container.ReadEntryData(entry);
                var text = Encoding.UTF8.GetString(bytes);
                icon = Ps5SvgIconParser.Parse(text, key, out _);
            }
            catch (Exception)
            {
                icon = null;
            }
        }

        lock (_parsed)
        {
            _parsed[key] = icon;
        }

        return icon;
    }

    /// <summary>Prefixes a bare registry id with <c>iconid_</c> when needed.</summary>
    /// <param name="id">Icon id in either form.</param>
    public static string Normalize(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        var trimmed = id.Trim();
        return trimmed.StartsWith(IconIdPrefix, StringComparison.Ordinal)
            ? trimmed
            : IconIdPrefix + trimmed;
    }

    // The blobs are plain, uncompressed SVG, so the leading bytes are either the
    // XML declaration or the <svg element itself. Leading whitespace is allowed
    // for; a BOM is not, because none of the extracted 671 carry one.
    private static bool LooksLikeSvg(RcoContainer container, RcoEntry entry)
    {
        try
        {
            var probe = container.ReadEntryData(entry with
            {
                DataLength = Math.Min(entry.DataLength, 16),
            });
            var head = Encoding.ASCII.GetString(probe).TrimStart();
            return head.StartsWith("<?xml", StringComparison.Ordinal) ||
                   head.StartsWith("<svg", StringComparison.Ordinal);
        }
        catch (Exception)
        {
            return false;
        }
    }
}
