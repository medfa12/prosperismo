// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

namespace SharpEmu.Libs.Presentation;

/// <summary>
/// Immutable firmware resources shared by every frame of a PS5 BGLayer
/// particle draw. The shader modules and sprite pixels come from the user's
/// firmware dump; SharpEmu does not persist or redistribute them.
/// </summary>
public sealed record Ps5NativeParticleResources(
    ReadOnlyMemory<byte> VertexSpirv,
    ReadOnlyMemory<byte> FragmentSpirv,
    Ps5NativeParticleTexture Particle0,
    Ps5NativeParticleTexture Particle1,
    ReadOnlyMemory<byte>? GeometrySpirv = null,
    Ps5NativeVertexStream? VertexStream = null);

public enum Ps5NativeVertexFormat
{
    Float2,
    Float3,
    Float4,
}

public sealed record Ps5NativeVertexAttribute(
    uint Location,
    uint Offset,
    Ps5NativeVertexFormat Format);

public sealed record Ps5NativeVertexStream(
    ReadOnlyMemory<byte> Data,
    uint Stride,
    IReadOnlyList<Ps5NativeVertexAttribute> Attributes)
{
    public bool IsValid =>
        !Data.IsEmpty && Stride > 0 && (uint)Data.Length % Stride == 0 &&
        Attributes.Count > 0 &&
        Attributes.Select(static attribute => attribute.Location).Distinct().Count() ==
            Attributes.Count;
}

public readonly record struct Ps5NativeViewport(
    float X,
    float Y,
    float Width,
    float Height)
{
    public bool IsValid =>
        float.IsFinite(X) && float.IsFinite(Y) &&
        float.IsFinite(Width) && float.IsFinite(Height) &&
        Width != 0.0f && Height != 0.0f;
}

/// <summary>A decoded firmware sprite supplied to the native renderer.</summary>
public sealed record Ps5NativeParticleTexture(
    int Width,
    int Height,
    ReadOnlyMemory<byte> Rgba)
{
    /// <summary>True when the dimensions and tightly packed RGBA payload agree.</summary>
    public bool IsValid =>
        Width > 0 &&
        Height > 0 &&
        Rgba.Length == (long)Width * Height * 4;
}

/// <summary>
/// Per-frame buffers produced by the recovered firmware particle evaluator.
/// The five entries preserve the native vertex shader's buffer binding order.
/// </summary>
public sealed record Ps5NativeParticleDraw(
    int Width,
    int Height,
    uint ParticleCount,
    IReadOnlyList<ReadOnlyMemory<byte>> VertexBuffers,
    Ps5NativeViewport? Viewport = null)
{
    public const int RequiredVertexBufferCount = 5;

    /// <summary>True when the draw has a usable target and the native five-buffer ABI.</summary>
    public bool IsValid =>
        Width > 0 &&
        Height > 0 &&
        ParticleCount > 0 &&
        (Viewport is null || Viewport.Value.IsValid) &&
        VertexBuffers.Count is >= RequiredVertexBufferCount and <= 16 &&
        VertexBuffers.All(static buffer => !buffer.IsEmpty);
}

/// <summary>A tightly packed, top-left-origin RGBA8 frame returned by the renderer.</summary>
public sealed record Ps5NativeParticleFrame(
    int Width,
    int Height,
    ReadOnlyMemory<byte> Rgba)
{
    public bool IsValid =>
        Width > 0 &&
        Height > 0 &&
        Rgba.Length == (long)Width * Height * 4;
}

/// <summary>Inputs to one stateful replay of Sony's large-particle compute shader.</summary>
public sealed record Ps5NativeParticleComputeRequest(
    ReadOnlyMemory<byte> ComputeSpirv,
    ReadOnlyMemory<byte> Resources,
    ReadOnlyMemory<byte> ParticleIds,
    float SampleTime,
    float SimulationStart,
    bool PreSimulation,
    float? SpawnEnd = null,
    bool SpawnWindow = false,
    bool ZeroProperties = true,
    ReadOnlyMemory<byte> InitialProperties = default,
    IReadOnlyList<ReadOnlyMemory<byte>>? ResourceFrames = null,
    bool InterleaveSmallDrawHistory = false,
    IReadOnlyList<IReadOnlyList<ReadOnlyMemory<byte>>>? ResourceBankFrames = null,
    uint TransPatternFlag = 0)
{
    public const int ResourceByteCount = 0xF8;
    public const int SmallParticleBankCount = 8;
    public const int ParticleIdByteCount = 6000 * sizeof(uint);
    public const int ParticlePropertyByteCount = 6000 * 0x44;

    public bool IsValid =>
        !ComputeSpirv.IsEmpty &&
        Resources.Length == ResourceByteCount &&
        ParticleIds.Length == ParticleIdByteCount &&
        (InitialProperties.IsEmpty || InitialProperties.Length == ParticlePropertyByteCount) &&
        float.IsFinite(SampleTime) &&
        float.IsFinite(SimulationStart) &&
        SampleTime >= SimulationStart &&
        SimulationStart >= 0.0f &&
        TransPatternFlag <= byte.MaxValue &&
        ResourceSequencesAreValid() &&
        (!SpawnEnd.HasValue || float.IsFinite(SpawnEnd.Value));

    private bool ResourceSequencesAreValid()
    {
        var expectedFrames = checked((int)MathF.Round((SampleTime - SimulationStart) * 60.0f)) + 1;
        var singleValid = ResourceFrames is null ||
            (ResourceFrames.Count == expectedFrames &&
             ResourceFrames.All(static frame => frame.Length == ResourceByteCount));
        var banksValid = ResourceBankFrames is null ||
            (ResourceBankFrames.Count == SmallParticleBankCount &&
             ResourceBankFrames.All(bank =>
                 bank.Count == expectedFrames &&
                 bank.All(static frame => frame.Length == ResourceByteCount)));
        return singleValid && banksValid &&
            (ResourceFrames is null || ResourceBankFrames is null);
    }
}

/// <summary>
/// Backend-neutral boundary for the recovered native BGLayer draw. A backend
/// owns its GPU resources after initialization and accepts only the changing
/// firmware buffers for each frame.
/// </summary>
public interface IPs5NativeParticleRenderer : IAsyncDisposable
{
    ValueTask InitializeAsync(
        Ps5NativeParticleResources resources,
        CancellationToken cancellationToken = default);

    ValueTask<Ps5NativeParticleFrame> RenderAsync(
        Ps5NativeParticleDraw draw,
        CancellationToken cancellationToken = default);

    ValueTask<Ps5NativeParticleFrame> RenderSequenceAsync(
        IReadOnlyList<Ps5NativeParticleDraw> draws,
        CancellationToken cancellationToken = default);
}

/// <summary>Recovered sequential ONE/ONE/ADD composition for standalone pass readbacks.</summary>
public static class Ps5NativeParticleCompositor
{
    /// <summary>
    /// Adds <paramref name="overlay"/> over <paramref name="baseFrame"/>,
    /// subtracting the shared clear colour that is present in both standalone
    /// readbacks. This reproduces the two draws occurring in one RGBA8 target.
    /// </summary>
    public static Ps5NativeParticleFrame CompositeAdditive(
        Ps5NativeParticleFrame baseFrame,
        Ps5NativeParticleFrame overlay)
    {
        if (!baseFrame.IsValid || !overlay.IsValid ||
            baseFrame.Width != overlay.Width || baseFrame.Height != overlay.Height)
        {
            throw new ArgumentException("native particle frames must have matching valid extents");
        }

        var rgba = overlay.Rgba.ToArray();
        var baseBytes = baseFrame.Rgba.Span;
        for (var offset = 0; offset < rgba.Length; offset += 4)
        {
            rgba[offset] = (byte)Math.Clamp(baseBytes[offset] + rgba[offset] - 1, 0, 255);
            rgba[offset + 1] = (byte)Math.Clamp(baseBytes[offset + 1] + rgba[offset + 1] - 1, 0, 255);
            rgba[offset + 2] = (byte)Math.Clamp(baseBytes[offset + 2] + rgba[offset + 2] - 9, 0, 255);
            rgba[offset + 3] = Math.Max(baseBytes[offset + 3], rgba[offset + 3]);
        }

        return new Ps5NativeParticleFrame(baseFrame.Width, baseFrame.Height, rgba);
    }
}
