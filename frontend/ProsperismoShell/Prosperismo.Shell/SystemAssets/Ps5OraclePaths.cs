// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

namespace SharpEmu.GUI.SystemAssets;

/// <summary>
/// Locates the user-local Prosperismo PS5 oracle after the asset migration.
/// The resolver is read-only: it returns paths and never copies or extracts
/// firmware content. Legacy SharpEmu environment overrides remain authoritative.
/// </summary>
public static class Ps5OraclePaths
{
    public const string OracleEnvironmentVariable = "PROSPERISMO_PS5_ORACLE";
    public const string FirmwareEnvironmentVariable = "PROSPERISMO_FW_DUMP";

    public static string? LocateOracleRoot(IEnumerable<string?>? probeBases = null)
    {
        probeBases ??= new[] { Environment.CurrentDirectory, AppContext.BaseDirectory };
        return LocateOracleRoot(
            Environment.GetEnvironmentVariable(OracleEnvironmentVariable),
            probeBases);
    }

    internal static string? LocateOracleRoot(
        string? environmentValue,
        IEnumerable<string?> probeBases)
    {
        if (TryDirectory(environmentValue) is { } configured)
        {
            return IsOracle(configured) ? configured : null;
        }

        foreach (var probeBase in probeBases)
        {
            if (string.IsNullOrWhiteSpace(probeBase))
            {
                continue;
            }

            string? current;
            try
            {
                current = Path.GetFullPath(probeBase);
            }
            catch (Exception exception) when (exception is IOException or ArgumentException or NotSupportedException)
            {
                continue;
            }

            for (var depth = 0; depth < 10 && !string.IsNullOrWhiteSpace(current); depth++)
            {
                foreach (var candidate in new[]
                {
                    Path.Combine(current, "ps5oracle"),
                    Path.Combine(current, "prosperismo", "ps5oracle"),
                })
                {
                    if (TryDirectory(candidate) is { } normalized && IsOracle(normalized))
                    {
                        return normalized;
                    }
                }

                var parent = Directory.GetParent(current)?.FullName;
                if (string.Equals(parent, current, StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }
                current = parent;
            }
        }

        return null;
    }

    public static string? LocateFirmwareRoot(string? oracleRoot = null)
    {
        foreach (var candidate in new[]
        {
            Environment.GetEnvironmentVariable(FirmwareEnvironmentVariable),
            oracleRoot is { Length: > 0 }
                ? Path.Combine(oracleRoot, "sony", "PS5_4.03_reconstructed")
                : null,
            oracleRoot is { Length: > 0 }
                ? Path.Combine(oracleRoot, "sony", "300REC", "extracted")
                : null,
        })
        {
            if (TryDirectory(candidate) is { } normalized &&
                Directory.Exists(Path.Combine(normalized, "filesystems", "system_ex", "vsh_asset")))
            {
                return normalized;
            }
        }

        return null;
    }

    public static string? LocateHomeSource(string? oracleRoot = null) =>
        TryFile(oracleRoot is { Length: > 0 }
            ? Path.Combine(oracleRoot, "sony", "useful rnps", "readable_js_3.00", "NPXS40002.js")
            : null);

    public static string? LocateNativeDrawCache(string? oracleRoot = null) =>
        TryDirectory(oracleRoot is { Length: > 0 }
            ? Path.Combine(
                oracleRoot,
                "evidence",
                "shell-rendering",
                "native-small-bottom",
                "draw-cache")
            : null);

    private static bool IsOracle(string root) =>
        Directory.Exists(Path.Combine(root, "sony")) &&
        Directory.Exists(Path.Combine(root, "evidence"));

    private static string? TryDirectory(string? path) => TryPath(path, Directory.Exists);

    private static string? TryFile(string? path) => TryPath(path, File.Exists);

    private static string? TryPath(string? path, Func<string, bool> exists)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            var normalized = Path.GetFullPath(path.Trim());
            return exists(normalized) ? normalized : null;
        }
        catch (Exception exception) when (
            exception is IOException or ArgumentException or NotSupportedException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
