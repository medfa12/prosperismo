// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using SharpEmu.GUI.Controls;
using SharpEmu.GUI.SystemAssets;
using SharpEmu.Libs.Presentation;

namespace SharpEmu.GUI.Ps5Home;

/// <summary>
/// Owns the original libScePsm AreaFocus/LineFocus programs and the two Sony
/// resources they consume. Firmware bytes stay in the user's dump.
/// </summary>
internal sealed class Ps5NativeFocusRuntime : IAsyncDisposable
{
    private readonly VulkanPs5NativeFocusRenderer _renderer;

    private Ps5NativeFocusRuntime(VulkanPs5NativeFocusRenderer renderer)
    {
        _renderer = renderer;
    }

    internal static Ps5NativeFocusRuntime? TryCreate()
    {
        var root = RnpsShellAssets.LocateDumpRoot();
        if (root is null || !Ps5FocusNoiseTexture.TryGetRgba(
                out var noise,
                out var noiseWidth,
                out var noiseHeight))
        {
            return null;
        }

        var library = Path.Combine(
            root,
            "filesystems",
            "system_ex",
            "common_ex",
            "lib",
            "libScePsm.sprx");
        if (!File.Exists(library) ||
            !Ps5NativeFocusCompiler.TryCompile(
                library,
                Ps5NativeFocusShaderKind.Area,
                64,
                out var area,
                out _) ||
            !Ps5NativeFocusCompiler.TryCompile(
                library,
                Ps5NativeFocusShaderKind.Line,
                64,
                out var line,
                out _) ||
            !Ps5NativeFocusCompiler.TryCompileVertex(
                library,
                Ps5NativeFocusShaderKind.Area,
                64,
                out var areaVertex,
                out _) ||
            !Ps5NativeFocusCompiler.TryCompileVertex(
                library,
                Ps5NativeFocusShaderKind.Line,
                64,
                out var lineVertex,
                out _))
        {
            return null;
        }

        var colorTable = new byte[ShellFocusPalette.ColorTable.Count * 4];
        for (var index = 0; index < ShellFocusPalette.ColorTable.Count; index++)
        {
            var color = ShellFocusPalette.ColorTable[index];
            colorTable[index * 4] = color.R;
            colorTable[index * 4 + 1] = color.G;
            colorTable[index * 4 + 2] = color.B;
            colorTable[index * 4 + 3] = color.A;
        }

        var resources = new Ps5NativeFocusResources(
            area,
            line,
            new Ps5NativeParticleTexture(ShellFocusPalette.ColorTable.Count, 1, colorTable),
            new Ps5NativeParticleTexture(noiseWidth, noiseHeight, noise),
            areaVertex,
            lineVertex);
        var renderer = new VulkanPs5NativeFocusRenderer();
        renderer.InitializeAsync(resources).AsTask().GetAwaiter().GetResult();
        return new Ps5NativeFocusRuntime(renderer);
    }

    internal ValueTask<Ps5NativeParticleFrame> RenderAsync(
        Ps5NativeFocusRenderRequest request,
        CancellationToken cancellationToken) =>
        _renderer.RenderAsync(request, cancellationToken);

    public ValueTask DisposeAsync() => _renderer.DisposeAsync();
}
