// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using SharpEmu.GUI.SystemAssets;

namespace SharpEmu.GUI.Ps5Home;

/// <summary>
/// Locates an external readable NPXS40002 Home bundle for provenance and
/// comparison. The host never copies it into the tree and does not claim to
/// execute it; the 4.03 signed bundle remains the input to the emulator path.
/// </summary>
public sealed record Ps5HomeSourceBundle(string Path, long Length)
{
    public const string PathOverrideVariable = "SHARPEMU_PS5_HOME_SOURCE";

    public const string ConventionalRelativePath =
        @"games\useful rnps\readable_js_3.00\NPXS40002.js";

    public static Ps5HomeSourceBundle? TryLocate(IEnumerable<string?>? probeBases = null)
    {
        var overridden = Environment.GetEnvironmentVariable(PathOverrideVariable);
        if (TryDescribe(overridden) is { } explicitBundle)
        {
            return explicitBundle;
        }

        probeBases ??= new[] { Environment.CurrentDirectory, AppContext.BaseDirectory };
        foreach (var probeBase in probeBases)
        {
            if (string.IsNullOrWhiteSpace(probeBase))
            {
                continue;
            }

            if (TryDescribe(System.IO.Path.Combine(probeBase, ConventionalRelativePath)) is { } bundle)
            {
                return bundle;
            }
        }

        return TryDescribe(Ps5OraclePaths.LocateHomeSource(Ps5OraclePaths.LocateOracleRoot()));
    }

    private static Ps5HomeSourceBundle? TryDescribe(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            var fullPath = System.IO.Path.GetFullPath(path.Trim());
            if (!File.Exists(fullPath))
            {
                return null;
            }

            return new Ps5HomeSourceBundle(fullPath, new FileInfo(fullPath).Length);
        }
        catch (Exception exception) when (
            exception is IOException or ArgumentException or NotSupportedException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
