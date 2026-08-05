// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

namespace SharpEmu.Libs.Presentation;

/// <summary>
/// Firmware-recovered host state for the BGLayer large-particle pixel pass.
/// Keeping the raw words beside their decoded meaning prevents the live
/// renderer and the off-screen conformance probe from drifting apart.
/// </summary>
public static class Ps5NativeParticleRenderState
{
    /// <summary>
    /// The four sampler SGPR words built by <c>large_particle_p</c> before its
    /// two <c>image_sample</c> instructions.
    /// </summary>
    public static ReadOnlySpan<uint> SamplerDescriptor =>
        [0x0000_0092u, 0x0000_0000u, 0x0250_0000u, 0x0000_0000u];

    public const bool MinFilterLinear = true;
    public const bool MagFilterLinear = true;
    public const bool MipFilterNearest = true;
    public const bool ClampUToEdge = true;
    public const bool ClampVToEdge = true;
    public const bool ClampWToEdge = true;
    public const float MinLod = 0.0f;
    public const float MaxLod = 0.0f;

    /// <summary>
    /// The PSM UI renderer creates its colour context with
    /// <c>PixelFormat.Rgba</c>, whose enum value is one. The Vulkan equivalent
    /// used by the native-particle path is R8G8B8A8_UNORM.
    /// </summary>
    public const uint UiRenderTargetPixelFormat = 1u;

    public const string UiRenderTargetHostFormat = "R8G8B8A8_UNORM";

    /// <summary>
    /// Value passed at NPXS40087 vaddr 0x9722d into PSM's pixel-format mapper.
    /// It names <c>PixelFormat.Dxt3</c>; it is a scratch/image surface and must
    /// not be mistaken for an AGC <c>RenderTargetFormat</c> enum.
    /// </summary>
    public const uint Dxt3SurfacePixelFormat = 0x11u;
}
