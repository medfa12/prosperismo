// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System.Text.Json;

namespace SharpEmu.GUI.SystemAssets;

/// <summary>
/// One React Native PlayStation (rnps) system-shell application found in a
/// firmware dump, as described by its <c>manifest.json</c>.
/// </summary>
/// <param name="TitleId">System title id, e.g. "NPXS40002".</param>
/// <param name="ApplicationName">Internal app name from the manifest, e.g. "rnps-home".</param>
/// <param name="Version">applicationVersion from the manifest, e.g. "4.1.0+12349".</param>
/// <param name="BundlePath">Absolute path to the signed RN bundle
/// (application.ps.bundle or main.jsbundle).</param>
public sealed record RnpsAppEntry(
    string TitleId,
    string? ApplicationName,
    string? Version,
    string BundlePath)
{
    /// <summary>reactNativePlaystationVersion from the manifest, e.g. "0.59.6-683.1".</summary>
    public string? ReactNativeVersion { get; init; }

    /// <summary>Absolute path to the manifest.json this entry was parsed from.</summary>
    public string ManifestPath { get; init; } = string.Empty;

    /// <summary>True for the background-service app under rnps/bgs (ppr-bgs).</summary>
    public bool IsBackgroundService { get; init; }
}

/// <summary>
/// A shell icon (icon0.png) registered by an rnps app under its appdb/
/// directory. These describe the built-in "pseudo apps" the shell shows in
/// the home screen (TV &amp; Video, Music, PS Store, ...), with localized names
/// in the sibling param.json.
/// </summary>
/// <param name="HostTitleId">The rnps app that hosts the appdb entry, e.g. "NPXS40016".</param>
/// <param name="TitleId">The title the icon represents; for "default" entries this is the host itself.</param>
/// <param name="IconPath">Absolute path to the 512x512 icon0.png.</param>
/// <param name="ParamJsonPath">Absolute path to the sibling param.json with localizedParameters, if present.</param>
public sealed record RnpsShellIcon(
    string HostTitleId,
    string TitleId,
    string IconPath,
    string? ParamJsonPath);

/// <summary>
/// Optional, read-only access to the PS5 system-shell ("rnps") assets inside a
/// user-provided decrypted firmware dump. Everything in the dump is Sony
/// proprietary content: it is only ever read from the user's disk at runtime
/// and never redistributed with the emulator. All lookups degrade gracefully;
/// when no dump is present every method returns empty results and
/// <see cref="IsAvailable"/> is false. See docs/rnps-shell.md.
/// </summary>
public static class RnpsShellAssets
{
    /// <summary>Environment variable that points at the firmware dump root.</summary>
    public const string DumpEnvironmentVariable = "SHARPEMU_FW_DUMP";

    /// <summary>Conventional dump location probed relative to the current and executable directories.</summary>
    public const string DefaultDumpRelativePath = "games/PS5_4.03_reconstructed";

    private static readonly string[] RnpsRootSegments = { "filesystems", "system_ex", "rnps" };
    private static readonly string[] VshAssetSegments = { "filesystems", "system_ex", "vsh_asset" };

    // Bundle file names in preference order; every shipping app has exactly one.
    private static readonly string[] BundleFileNames = { "application.ps.bundle", "main.jsbundle" };

    // BGLayer's two light textures, in the order they are layered.
    private static readonly string[] BgLayerLightFileNames =
    {
        "Sce.Vsh.ShellUI.BGLayer.Particle0.gnf",
        "Sce.Vsh.ShellUI.BGLayer.Particle1.gnf",
    };

    /// <summary>True when a firmware dump with an rnps shell tree was located.</summary>
    public static bool IsAvailable => GetRnpsRoot(LocateDumpRoot()) is not null;

    /// <summary>
    /// Locates the firmware dump root: SHARPEMU_FW_DUMP first, then
    /// games/PS5_4.03_reconstructed relative to the current directory and to
    /// the executable directory. Returns null when nothing usable exists.
    /// </summary>
    public static string? LocateDumpRoot()
    {
        var legacy = LocateDumpRoot(
            Environment.GetEnvironmentVariable(DumpEnvironmentVariable),
            new[] { Environment.CurrentDirectory, AppContext.BaseDirectory });
        if (legacy is not null)
        {
            return legacy;
        }

        var oracle = Ps5OraclePaths.LocateOracleRoot();
        return Ps5OraclePaths.LocateFirmwareRoot(oracle);
    }

    // Testable core: the env value and probe bases are injected.
    internal static string? LocateDumpRoot(string? environmentValue, IEnumerable<string?> probeBases)
    {
        if (!string.IsNullOrWhiteSpace(environmentValue))
        {
            var fromEnv = TryNormalizeDumpRoot(environmentValue);
            if (fromEnv is not null)
            {
                return fromEnv;
            }
        }

        foreach (var baseDirectory in probeBases)
        {
            if (string.IsNullOrWhiteSpace(baseDirectory))
            {
                continue;
            }

            var candidate = TryNormalizeDumpRoot(Path.Combine(baseDirectory, DefaultDumpRelativePath));
            if (candidate is not null)
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// Enumerates the rnps shell applications (apps/NPXS400xx plus the bgs
    /// background service) of a dump. Entries without a manifest or without a
    /// bundle (empty stub directories exist in real dumps) are skipped.
    /// Returns an empty list when the dump or the rnps tree is missing.
    /// </summary>
    /// <param name="dumpRoot">Dump root override; defaults to <see cref="LocateDumpRoot"/>.</param>
    public static IReadOnlyList<RnpsAppEntry> EnumerateApps(string? dumpRoot = null)
    {
        var rnpsRoot = GetRnpsRoot(dumpRoot ?? LocateDumpRoot());
        if (rnpsRoot is null)
        {
            return Array.Empty<RnpsAppEntry>();
        }

        var entries = new List<RnpsAppEntry>();

        var appsRoot = Path.Combine(rnpsRoot, "apps");
        if (Directory.Exists(appsRoot))
        {
            foreach (var appDirectory in SafeEnumerateDirectories(appsRoot))
            {
                if (TryReadApp(appDirectory, isBackgroundService: false) is { } entry)
                {
                    entries.Add(entry);
                }
            }
        }

        // The bgs directory name carries a trailing space in some dumps, so it
        // is matched by trimmed name rather than joined directly.
        foreach (var directory in SafeEnumerateDirectories(rnpsRoot))
        {
            if (!Path.GetFileName(directory).Trim().Equals("bgs", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var appDirectory in SafeEnumerateDirectories(directory))
            {
                if (TryReadApp(appDirectory, isBackgroundService: true) is { } entry)
                {
                    entries.Add(entry);
                }
            }
        }

        entries.Sort(static (a, b) => string.CompareOrdinal(a.TitleId, b.TitleId));
        return entries;
    }

    /// <summary>
    /// Enumerates the shell icons (512x512 icon0.png) that rnps apps register
    /// under apps/&lt;host&gt;/appdb/&lt;titleId&gt;/. Returns an empty list when
    /// unavailable.
    /// </summary>
    /// <param name="dumpRoot">Dump root override; defaults to <see cref="LocateDumpRoot"/>.</param>
    public static IReadOnlyList<RnpsShellIcon> EnumerateShellIcons(string? dumpRoot = null)
    {
        var rnpsRoot = GetRnpsRoot(dumpRoot ?? LocateDumpRoot());
        if (rnpsRoot is null)
        {
            return Array.Empty<RnpsShellIcon>();
        }

        var appsRoot = Path.Combine(rnpsRoot, "apps");
        if (!Directory.Exists(appsRoot))
        {
            return Array.Empty<RnpsShellIcon>();
        }

        var icons = new List<RnpsShellIcon>();
        foreach (var appDirectory in SafeEnumerateDirectories(appsRoot))
        {
            var hostTitleId = Path.GetFileName(appDirectory);
            var appDb = Path.Combine(appDirectory, "appdb");
            if (!Directory.Exists(appDb))
            {
                continue;
            }

            foreach (var entryDirectory in SafeEnumerateDirectories(appDb))
            {
                var iconPath = Path.Combine(entryDirectory, "icon0.png");
                if (!File.Exists(iconPath))
                {
                    continue;
                }

                var entryName = Path.GetFileName(entryDirectory);
                var titleId = entryName.Equals("default", StringComparison.OrdinalIgnoreCase)
                    ? hostTitleId
                    : entryName;

                var paramPath = Path.Combine(entryDirectory, "param.json");
                icons.Add(new RnpsShellIcon(
                    hostTitleId,
                    titleId,
                    iconPath,
                    File.Exists(paramPath) ? paramPath : null));
            }
        }

        icons.Sort(static (a, b) => string.CompareOrdinal(a.TitleId, b.TitleId));
        return icons;
    }

    /// <summary>
    /// Reads the title shown by Home from an appdb <c>param.json</c>. The
    /// requested language wins, then the file's <c>defaultLanguage</c>, then the
    /// first localized record. A malformed or missing file falls back to the
    /// title id rather than preventing the tile from rendering.
    /// </summary>
    public static string ReadShellTitle(RnpsShellIcon icon, string preferredLanguage = "en-US")
    {
        ArgumentNullException.ThrowIfNull(icon);
        if (string.IsNullOrWhiteSpace(icon.ParamJsonPath))
        {
            return icon.TitleId;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(icon.ParamJsonPath));
            if (!document.RootElement.TryGetProperty("localizedParameters", out var localized) ||
                localized.ValueKind != JsonValueKind.Object)
            {
                return icon.TitleId;
            }

            if (TryReadLocalizedTitle(localized, preferredLanguage) is { Length: > 0 } preferred)
            {
                return preferred;
            }

            if (localized.TryGetProperty("defaultLanguage", out var defaultLanguage) &&
                defaultLanguage.ValueKind == JsonValueKind.String &&
                TryReadLocalizedTitle(localized, defaultLanguage.GetString()) is { Length: > 0 } fallback)
            {
                return fallback;
            }

            foreach (var property in localized.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.Object &&
                    property.Value.TryGetProperty("titleName", out var title) &&
                    title.ValueKind == JsonValueKind.String &&
                    title.GetString() is { Length: > 0 } any)
                {
                    return any;
                }
            }
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            // The icon remains usable even if its metadata is not.
        }

        return icon.TitleId;
    }

    private static string? TryReadLocalizedTitle(JsonElement localized, string? language)
    {
        if (string.IsNullOrWhiteSpace(language) ||
            !localized.TryGetProperty(language, out var record) ||
            record.ValueKind != JsonValueKind.Object ||
            !record.TryGetProperty("titleName", out var title) ||
            title.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return title.GetString();
    }

    /// <summary>
    /// Path to a hub background from vsh_asset (bg_&lt;titleId&gt;.dds, falling back
    /// to bg_hub_default.dds), or null. The images are 3840x2160 BC7 DDS
    /// (DXGI_FORMAT_BC7_UNORM) and need a BC7 decoder before display.
    /// </summary>
    /// <param name="titleId">System title id, e.g. "NPXS40047"; null for the default background.</param>
    /// <param name="dumpRoot">Dump root override; defaults to <see cref="LocateDumpRoot"/>.</param>
    public static string? GetHubBackgroundPath(string? titleId = null, string? dumpRoot = null)
    {
        var root = dumpRoot ?? LocateDumpRoot();
        if (root is null)
        {
            return null;
        }

        var vshAsset = Path.Combine(root, Path.Combine(VshAssetSegments));
        if (!Directory.Exists(vshAsset))
        {
            return null;
        }

        if (!string.IsNullOrEmpty(titleId))
        {
            var specific = Path.Combine(vshAsset, $"bg_{titleId}.dds");
            if (File.Exists(specific))
            {
                return specific;
            }
        }

        var fallback = Path.Combine(vshAsset, "bg_hub_default.dds");
        return File.Exists(fallback) ? fallback : null;
    }

    /// <summary>
    /// Paths to the BGLayer light textures from vsh_asset, in layer order. The
    /// shell's home background is a still wallpaper with these drifting over it;
    /// each file is a 480x270 BC7 GNF surface holding an out-of-focus light
    /// field (see <c>SystemAssets/Textures/GnfImage.cs</c>). Missing files are
    /// skipped, so the result is empty when no dump is present.
    /// </summary>
    /// <param name="dumpRoot">Dump root override; defaults to <see cref="LocateDumpRoot"/>.</param>
    public static IReadOnlyList<string> GetBgLayerLightPaths(string? dumpRoot = null)
    {
        var root = dumpRoot ?? LocateDumpRoot();
        if (root is null)
        {
            return Array.Empty<string>();
        }

        var vshAsset = Path.Combine(root, Path.Combine(VshAssetSegments));
        if (!Directory.Exists(vshAsset))
        {
            return Array.Empty<string>();
        }

        var paths = new List<string>(BgLayerLightFileNames.Length);
        foreach (var fileName in BgLayerLightFileNames)
        {
            var candidate = Path.Combine(vshAsset, fileName);
            if (File.Exists(candidate))
            {
                paths.Add(candidate);
            }
        }

        return paths.Count == 0 ? Array.Empty<string>() : paths;
    }

    /// <summary>
    /// Opens a dump asset for shared read access, or returns null when the
    /// file is missing or unreadable. Callers own the returned stream.
    /// </summary>
    public static Stream? OpenAsset(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        try
        {
            return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }

    private static string? TryNormalizeDumpRoot(string candidate)
    {
        try
        {
            var fullPath = Path.GetFullPath(candidate);
            return Directory.Exists(fullPath) ? fullPath : null;
        }
        catch (Exception exception) when (exception is IOException or ArgumentException or System.Security.SecurityException or NotSupportedException)
        {
            return null;
        }
    }

    private static string? GetRnpsRoot(string? dumpRoot)
    {
        if (dumpRoot is null)
        {
            return null;
        }

        var rnpsRoot = Path.Combine(dumpRoot, Path.Combine(RnpsRootSegments));
        return Directory.Exists(rnpsRoot) ? rnpsRoot : null;
    }

    private static RnpsAppEntry? TryReadApp(string appDirectory, bool isBackgroundService)
    {
        var manifestPath = Path.Combine(appDirectory, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            return null;
        }

        string? bundlePath = null;
        foreach (var bundleFileName in BundleFileNames)
        {
            var candidate = Path.Combine(appDirectory, bundleFileName);
            if (File.Exists(candidate))
            {
                bundlePath = candidate;
                break;
            }
        }

        if (bundlePath is null)
        {
            return null;
        }

        string? applicationName = null;
        string? version = null;
        string? reactNativeVersion = null;
        var titleId = Path.GetFileName(appDirectory).Trim();

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(manifestPath));
            var manifest = document.RootElement;
            if (manifest.ValueKind == JsonValueKind.Object)
            {
                applicationName = GetString(manifest, "applicationName");
                version = GetString(manifest, "applicationVersion");
                reactNativeVersion = GetString(manifest, "reactNativePlaystationVersion");
                if (GetString(manifest, "titleId") is { Length: > 0 } manifestTitleId)
                {
                    titleId = manifestTitleId;
                }
            }
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            // A corrupt or unreadable manifest still yields a usable entry
            // keyed by the directory name.
        }

        return new RnpsAppEntry(titleId, applicationName, version, bundlePath)
        {
            ReactNativeVersion = reactNativeVersion,
            ManifestPath = manifestPath,
            IsBackgroundService = isBackgroundService,
        };
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static IEnumerable<string> SafeEnumerateDirectories(string directory)
    {
        try
        {
            return Directory.EnumerateDirectories(directory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }
}
