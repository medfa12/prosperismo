// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System.Buffers.Binary;
using System.Text.Json;
using SharpEmu.Libs.Presentation;
using SharpEmu.Libs.Textures;

namespace SharpEmu.GUI.SystemAssets.Shell;

/// <summary>
/// Plays byte-exact steady-state small-particle snapshots through Sony's
/// translated vertex/pixel pair. Every frame keeps all non-empty banks in one
/// Vulkan render pass so the recovered ONE/ONE/ADD order is preserved.
/// </summary>
internal sealed class Ps5NativeSmallParticleCacheFrameSource : IPs5NativeParticleFrameSource
{
    internal const string CacheEnvironmentVariable = "SHARPEMU_PS5_NATIVE_SMALL_DRAW_CACHE";

    private readonly string[] _frameRoots;
    private readonly double _framesPerSecond;
    private readonly int[] _rawStates;
    private readonly Ps5NativeParticleFrame?[] _renderedFrames;
    private readonly SemaphoreSlim _renderGate = new(1, 1);
    private readonly VulkanPs5NativeParticleRenderer _renderer = new();

    private Ps5NativeSmallParticleCacheFrameSource(
        string[] frameRoots,
        double framesPerSecond,
        int[] rawStates,
        Ps5NativeParticleResources resources)
    {
        _frameRoots = frameRoots;
        _framesPerSecond = framesPerSecond;
        _rawStates = rawStates;
        _renderedFrames = new Ps5NativeParticleFrame?[frameRoots.Length];
        _renderer.InitializeAsync(resources).GetAwaiter().GetResult();
    }

    internal static Ps5NativeSmallParticleCacheFrameSource? TryCreateFromEnvironment()
    {
        var root = Environment.GetEnvironmentVariable(CacheEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(root))
        {
            root = SharpEmu.GUI.SystemAssets.Ps5OraclePaths.LocateNativeDrawCache(
                SharpEmu.GUI.SystemAssets.Ps5OraclePaths.LocateOracleRoot());
        }
        if (string.IsNullOrWhiteSpace(root))
        {
            return null;
        }

        try
        {
            root = Path.GetFullPath(root);
            var manifestPath = Path.Combine(root, "sequence.json");
            var vertexPath = Path.Combine(root, "particle.vert.spv");
            var fragmentPath = Path.Combine(root, "particle.frag.spv");
            if (!File.Exists(manifestPath) || !File.Exists(vertexPath) || !File.Exists(fragmentPath))
            {
                return null;
            }

            using var manifest = JsonDocument.Parse(File.ReadAllBytes(manifestPath));
            var rootElement = manifest.RootElement;
            var framesPerSecond = rootElement.GetProperty("framesPerSecond").GetDouble();
            var rawState = rootElement.GetProperty("rawState").GetInt32();
            var selector = rootElement.TryGetProperty("selector", out var selectorElement)
                ? selectorElement.GetString()
                : null;
            if (!double.IsFinite(framesPerSecond) || framesPerSecond <= 0 || rawState is < 1 or > 6)
            {
                return null;
            }

            var frames = Directory.GetDirectories(root, "frame-*")
                .Order(StringComparer.Ordinal)
                .Where(IsCompleteFrame)
                .ToArray();
            if (frames.Length == 0)
            {
                return null;
            }

            var lightPaths = RnpsShellAssets.GetBgLayerLightPaths();
            if (lightPaths.Count < 2)
            {
                return null;
            }
            var rgba0 = GnfImage.TryLoadRgba(lightPaths[0], out var width0, out var height0);
            var rgba1 = GnfImage.TryLoadRgba(lightPaths[1], out var width1, out var height1);
            if (rgba0 is null || rgba1 is null || width0 != width1 || height0 != height1)
            {
                return null;
            }

            return new Ps5NativeSmallParticleCacheFrameSource(
                frames,
                framesPerSecond,
                string.Equals(selector, "spread_expanded", StringComparison.Ordinal)
                    ? [1, 2]
                    : [rawState],
                new Ps5NativeParticleResources(
                    File.ReadAllBytes(vertexPath),
                    File.ReadAllBytes(fragmentPath),
                    new Ps5NativeParticleTexture(width0, height0, rgba0),
                    new Ps5NativeParticleTexture(width1, height1, rgba1)));
        }
        catch (Exception)
        {
            return null;
        }
    }

    public bool SupportsState(ShellGlobalBackgroundState state) =>
        ShellBackgroundComposition.NativeParticleRouteFor(state).RawState is int rawState &&
        _rawStates.Contains(rawState);

    public async ValueTask<Ps5NativeParticleFrame?> RenderAsync(
        Ps5NativeParticleFrameRequest request,
        CancellationToken cancellationToken = default)
    {
        var route = ShellBackgroundComposition.NativeParticleRouteFor(request.State);
        if (route.RawState is not int rawState || !_rawStates.Contains(rawState) ||
            request.Width <= 0 || request.Height <= 0)
        {
            return null;
        }

        await _renderGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var index = (int)Math.Floor(request.Elapsed.TotalSeconds * _framesPerSecond);
            index = ((index % _frameRoots.Length) + _frameRoots.Length) % _frameRoots.Length;
            if (_renderedFrames[index] is { } cached &&
                cached.Width == request.Width && cached.Height == request.Height)
            {
                return cached;
            }

            var frame = await Task.Run(
                () => RenderFrame(_frameRoots[index], request.Width, request.Height, cancellationToken),
                cancellationToken).ConfigureAwait(false);
            _renderedFrames[index] = frame;
            return frame;
        }
        finally
        {
            _renderGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _renderGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await _renderer.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            _renderGate.Release();
            _renderGate.Dispose();
        }
    }

    private Ps5NativeParticleFrame RenderFrame(
        string frameRoot,
        int width,
        int height,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var sharedProperties = File.ReadAllBytes(Path.Combine(frameRoot, "properties.bin"));
        var draws = Directory.GetDirectories(frameRoot, "bank-*")
            .Order(StringComparer.Ordinal)
            .Select(bankRoot => Enumerable.Range(0, 6)
                .Select(index => (ReadOnlyMemory<byte>)(index == 2
                    ? sharedProperties
                    : File.ReadAllBytes(Path.Combine(bankRoot, $"buffer{index}.bin"))))
                .ToArray())
            .Where(static buffers => IsActiveDrawResource(buffers[1].Span))
            .Select(buffers => new Ps5NativeParticleDraw(
                width,
                height,
                BinaryPrimitives.ReadUInt32LittleEndian(buffers[1].Span[0x20..]),
                buffers))
            .Where(static draw => draw.ParticleCount > 0)
            .ToArray();

        return _renderer.RenderSequenceAsync(draws, cancellationToken).GetAwaiter().GetResult();
    }

    private static bool IsActiveDrawResource(ReadOnlySpan<byte> resource) =>
        resource.Length >= 0x140 &&
        BinaryPrimitives.ReadUInt32LittleEndian(resource[0x20..]) > 0 &&
        BinaryPrimitives.ReadUInt32LittleEndian(resource[0x28..]) > 0 &&
        BinaryPrimitives.ReadUInt32LittleEndian(resource[0x2C..]) > 0;

    private static bool IsCompleteFrame(string frameRoot)
    {
        var banks = Directory.GetDirectories(frameRoot, "bank-*");
        return File.Exists(Path.Combine(frameRoot, "properties.bin")) &&
            banks.Length == Ps5NativeParticleComputeRequest.SmallParticleBankCount &&
            banks.All(bank => new[] { 0, 1, 3, 4, 5 }
                .All(index => File.Exists(Path.Combine(bank, $"buffer{index}.bin"))));
    }
}
