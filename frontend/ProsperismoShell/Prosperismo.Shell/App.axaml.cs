// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace SharpEmu.GUI;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    /// Points the shell's default face at Sony's SST-Light when the user's dump
    /// carries it, leaving the open fallback in place when it does not.
    ///
    /// <para>Done here rather than in XAML because the font is read from an
    /// absolute path on the user's disk that is only known at run time. Nothing
    /// is copied out of the dump: the family is a <c>file://</c> reference and
    /// Avalonia reads the face in place.</para>
    /// </summary>
    internal void ApplyShellTypeface()
    {
        var face = Ps5Home.Ps5FontLibrary.TryGet(Ps5Home.Ps5FontFace.Light);
        if (face is not null)
        {
            Resources["ShellFontFamily"] = face;
        }
    }

    public override void OnFrameworkInitializationCompleted()
    {
        ApplyShellTypeface();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
