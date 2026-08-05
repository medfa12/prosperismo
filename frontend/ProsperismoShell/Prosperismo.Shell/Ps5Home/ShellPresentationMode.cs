// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

namespace SharpEmu.GUI.Ps5Home;

public enum ShellPresentationMode
{
    Sony,
    Desktop,
}

/// <summary>Startup selection between the console surface and desktop chrome.</summary>
public static class ShellPresentation
{
    public const string EnvironmentVariable = "SHARPEMU_UI_MODE";

    /// <summary>
    /// Parses frontend-only launch forms so a shortcut can start the console
    /// surface like Steam Big Picture without relying on environment setup.
    /// Emulator CLI arguments are deliberately left untouched.
    /// </summary>
    public static bool TryParseLaunchArguments(
        IReadOnlyList<string> args,
        out ShellPresentationMode mode)
    {
        mode = ShellPresentationMode.Sony;
        if (args.Count != 1)
        {
            return false;
        }

        var value = args[0].Trim();
        if (value.Equals("--sony-ui", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("--big-picture", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("--ui=sony", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (value.Equals("--desktop-ui", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("--ui=desktop", StringComparison.OrdinalIgnoreCase))
        {
            mode = ShellPresentationMode.Desktop;
            return true;
        }

        return false;
    }

    /// <summary>Applies an explicit frontend choice for this process only.</summary>
    public static void SelectForProcess(ShellPresentationMode mode) =>
        Environment.SetEnvironmentVariable(
            EnvironmentVariable,
            mode == ShellPresentationMode.Desktop ? "desktop" : "sony");

    public static ShellPresentationMode Current =>
        Parse(Environment.GetEnvironmentVariable(EnvironmentVariable));

    public static ShellPresentationMode Parse(string? value) =>
        string.Equals(value?.Trim(), "desktop", StringComparison.OrdinalIgnoreCase)
            ? ShellPresentationMode.Desktop
            : ShellPresentationMode.Sony;
}
