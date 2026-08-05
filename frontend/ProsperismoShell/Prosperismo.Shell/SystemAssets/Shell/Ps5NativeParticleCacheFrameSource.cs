// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System.Buffers.Binary;
using System.Text.Json;
using SharpEmu.Libs.Presentation;
using SharpEmu.Libs.Textures;

namespace SharpEmu.GUI.SystemAssets.Shell;

/// <summary>
/// In-process Vulkan playback of firmware-derived draw snapshots. Unlike the
/// PNG fallback, every displayed frame is rendered from Sony's translated
/// shaders, five native buffers, and GNF sprites inside the shell process.
/// The snapshot producer is temporary until the compute evaluator itself is
/// hosted in-process, but this closes the renderer/compositor boundary first.
/// </summary>
internal sealed class Ps5NativeParticleCacheFrameSource : IPs5NativeParticleFrameSource
{
    internal const string DrawCacheEnvironmentVariable = "SHARPEMU_PS5_NATIVE_DRAW_CACHE";

    private readonly IReadOnlyList<FramePaths> _frames;
    private readonly double _framesPerSecond;
    private readonly double _nativeStart;
    private readonly Ps5NativeParticleTexture _particle0;
    private readonly Ps5NativeParticleTexture _particle1;
    private readonly ComputeInputs? _bank0Compute;
    private readonly byte[]? _bank1ComputeSpirv;
    private readonly Ps5NativeParticleFrame?[] _renderedFrames;
    private readonly Ps5NativeParticleGroupTimeline _groupTimeline =
        Ps5NativeParticleGroupTimeline.CreateColdBootLargeGroups();
    private readonly SemaphoreSlim _renderGate = new(1, 1);
    private readonly VulkanPs5NativeParticleRenderer _renderer = new();

    private Ps5NativeParticleCacheFrameSource(
        IReadOnlyList<FramePaths> frames,
        double framesPerSecond,
        double nativeStart,
        Ps5NativeParticleTexture particle0,
        Ps5NativeParticleTexture particle1,
        ComputeInputs? bank0Compute,
        byte[]? bank1ComputeSpirv)
    {
        _frames = frames;
        _framesPerSecond = framesPerSecond;
        _nativeStart = nativeStart;
        _particle0 = particle0;
        _particle1 = particle1;
        _bank0Compute = bank0Compute;
        _bank1ComputeSpirv = bank1ComputeSpirv;
        // A sequence frame has two native lifetime variants: the initial
        // seven-second two-group overlap and the surviving active group.
        _renderedFrames = new Ps5NativeParticleFrame?[frames.Count * 2];

        // Both large groups use the same original vertex/pixel pair and GNF
        // resources. Own that Vulkan device/pipeline once for the lifetime of
        // the source, just as BGLayer's renderer does, rather than rebuilding
        // it independently for every group of every frame.
        _renderer.InitializeAsync(CreateResources(frames[0].Bank1)).GetAwaiter().GetResult();
    }

    internal static Ps5NativeParticleCacheFrameSource? TryCreateFromEnvironment()
    {
        var root = Environment.GetEnvironmentVariable(DrawCacheEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(root))
        {
            var preview = Environment.GetEnvironmentVariable(Ps5NativeBackgroundLayer.FrameEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(preview) && Directory.Exists(preview))
            {
                var nested = Path.Combine(preview, "draw-cache");
                root = Directory.Exists(nested) ? nested : null;
            }
        }

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
            if (!Directory.Exists(root))
            {
                return null;
            }

            var frames = Directory.EnumerateDirectories(root, "frame-*")
                .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
                .Select(static path => new FramePaths(
                    Path.Combine(path, "bank1"),
                    Path.Combine(path, "bank0")))
                .Where(static frame => frame.IsComplete)
                .ToArray();
            if (frames.Length == 0)
            {
                return null;
            }
            if (!UsesOneFirmwareProgramPair(frames))
            {
                // A persistent BGLayer pipeline is valid only while both
                // groups and every snapshot name the same original programs.
                // Refuse a mixed cache rather than silently pinning frame 0's
                // shaders over incompatible later draws.
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

            var sequence = ReadSequence(Directory.GetParent(root)?.FullName);
            return new Ps5NativeParticleCacheFrameSource(
                frames,
                sequence.FramesPerSecond,
                sequence.NativeStart,
                new Ps5NativeParticleTexture(width0, height0, rgba0),
                new Ps5NativeParticleTexture(width1, height1, rgba1),
                ComputeInputs.TryLoad(Path.Combine(root, "compute", "bank0")),
                TryReadNonEmpty(Path.Combine(root, "compute", "bank1", "particle.spawn.spv")));
        }
        catch (Exception)
        {
            return null;
        }
    }

    public async ValueTask<Ps5NativeParticleFrame?> RenderAsync(
        Ps5NativeParticleFrameRequest request,
        CancellationToken cancellationToken = default)
    {
        var route = ShellBackgroundComposition.NativeParticleRouteFor(request.State);
        if (route.RawState != 3 ||
            request.Width <= 0 || request.Height <= 0)
        {
            return null;
        }

        await _renderGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var index = (int)Math.Floor(request.Elapsed.TotalSeconds * _framesPerSecond);
            index = ((index % _frames.Count) + _frames.Count) % _frames.Count;
            var globalTime = (float)(_nativeStart + request.Elapsed.TotalSeconds);
            var timeStep = (float)(1.0 / _framesPerSecond);
            var runBank0 = _groupTimeline.ShouldRunGroup(0, globalTime, timeStep);
            var runBank1 = _groupTimeline.ShouldRunGroup(1, globalTime, timeStep);
            var cacheIndex = index + (runBank0 ? 0 : _frames.Count);
            if (_renderedFrames[cacheIndex] is { } cached &&
                cached.Width == request.Width && cached.Height == request.Height)
            {
                return cached;
            }

            var paths = _frames[index];
            var rendered = await Task.Run(
                () => RenderFrame(
                    paths,
                    index,
                    request.Width,
                    request.Height,
                    runBank0,
                    runBank1,
                    cancellationToken),
                cancellationToken).ConfigureAwait(false);
            _renderedFrames[cacheIndex] = rendered;
            return rendered;
        }
        finally
        {
            _renderGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _renderer.DisposeAsync().ConfigureAwait(false);
        _renderGate.Dispose();
    }

    private Ps5NativeParticleFrame RenderFrame(
        FramePaths paths,
        int frameIndex,
        int width,
        int height,
        bool runBank0,
        bool runBank1,
        CancellationToken cancellationToken)
    {
        var nativeTime = (float)(_nativeStart + (frameIndex / _framesPerSecond));
        byte[]? computedBank0 = null;
        if (_bank0Compute is { } compute)
        {
            computedBank0 = GpuConformanceRunner.RunParticleCompute(
                new Ps5NativeParticleComputeRequest(
                    compute.Spirv,
                    compute.Resources,
                    compute.Ids,
                    SampleTime: nativeTime,
                    SimulationStart: 0.0f,
                    PreSimulation: true,
                    SpawnEnd: 0.116667f));
        }

        byte[]? computedBank1 = null;
        if (_bank1ComputeSpirv is { Length: > 0 } bank1Spirv)
        {
            computedBank1 = GpuConformanceRunner.RunParticleCompute(
                new Ps5NativeParticleComputeRequest(
                    bank1Spirv,
                    GpuConformanceRunner.CreateAcceptedBank1Resources(),
                    GpuConformanceRunner.CreateNativePrimaryParticleIds(),
                    SampleTime: nativeTime,
                    SimulationStart: 6.0f,
                    PreSimulation: false,
                    SpawnEnd: 6.1f,
                    SpawnWindow: true,
                    ZeroProperties: false,
                    InitialProperties: computedBank0 ?? ReadOnlyMemory<byte>.Empty));
        }

        // Both native ResourcesCs/ResourcesLargeParticleVsPs blocks point at
        // the one property allocation created at 0x944a9. Group 0 writes it,
        // then group 1 continues from those bytes, and both draw passes bind
        // the resulting shared allocation. Separate per-bank property buffers
        // were a probe convenience, not the firmware ABI.
        var sharedProperties = computedBank1 ?? computedBank0;
        var draws = new List<Ps5NativeParticleDraw>(2);
        foreach (var group in NativeLargeGroupDrawOrder(runBank0, runBank1))
        {
            draws.Add(CreateDraw(
                group == 1 ? paths.Bank1 : paths.Bank0,
                width,
                height,
                cancellationToken,
                sharedProperties));
        }

        if (draws.Count == 0)
        {
            return new Ps5NativeParticleFrame(width, height, new byte[checked(width * height * 4)]);
        }

        // RenderInternal walks the active group first and the retiring group
        // second in one render pass under ONE/ONE/ADD. Keeping both draws in
        // one persistent session removes the former host-side clear-colour
        // subtraction and is the native composition boundary itself.
        return _renderer.RenderSequenceAsync(draws, cancellationToken).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Native <c>RenderInternal</c> group order: the active group (1) is
    /// submitted before the retiring group (0), with absent groups omitted.
    /// Both returned entries belong to one render pass.
    /// </summary>
    internal static IReadOnlyList<int> NativeLargeGroupDrawOrder(
        bool runBank0,
        bool runBank1) => (runBank0, runBank1) switch
        {
            (true, true) => [1, 0],
            (true, false) => [0],
            (false, true) => [1],
            _ => Array.Empty<int>(),
        };

    private Ps5NativeParticleDraw CreateDraw(
        string directory,
        int width,
        int height,
        CancellationToken cancellationToken,
        byte[]? propertyOverride = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var buffers = Enumerable.Range(0, Ps5NativeParticleDraw.RequiredVertexBufferCount)
            .Select(index => File.ReadAllBytes(
                Path.Combine(directory, $"00dabbc0.vert.buffer{index}.bin")))
            .ToArray();
        if (propertyOverride is not null)
        {
            if (propertyOverride.Length != buffers[2].Length)
            {
                throw new InvalidDataException("computed particle property buffer has the wrong extent");
            }
            buffers[2] = propertyOverride;
        }

        // The camera aspect is runtime state, not authored particle data.
        BinaryPrimitives.WriteUInt32LittleEndian(
            buffers[1].AsSpan(0x78),
            BitConverter.SingleToUInt32Bits((float)width / height));
        var particleCount = BinaryPrimitives.ReadUInt32LittleEndian(buffers[1].AsSpan(0xAC));

        return new Ps5NativeParticleDraw(
            width,
            height,
            particleCount,
            buffers.Select(static value => (ReadOnlyMemory<byte>)value).ToArray());
    }

    private Ps5NativeParticleResources CreateResources(string directory) =>
        new(
            File.ReadAllBytes(Path.Combine(directory, "00dabbc0.vert.spv")),
            File.ReadAllBytes(Path.Combine(directory, "00daa420.frag.spv")),
            _particle0,
            _particle1);

    private static bool UsesOneFirmwareProgramPair(IReadOnlyList<FramePaths> frames)
    {
        var vertex = File.ReadAllBytes(Path.Combine(frames[0].Bank1, "00dabbc0.vert.spv"));
        var fragment = File.ReadAllBytes(Path.Combine(frames[0].Bank1, "00daa420.frag.spv"));
        return frames.SelectMany(static frame => new[] { frame.Bank1, frame.Bank0 }).All(directory =>
            File.ReadAllBytes(Path.Combine(directory, "00dabbc0.vert.spv")).SequenceEqual(vertex) &&
            File.ReadAllBytes(Path.Combine(directory, "00daa420.frag.spv")).SequenceEqual(fragment));
    }

    private static SequenceInfo ReadSequence(string? sequenceDirectory)
    {
        try
        {
            if (sequenceDirectory is null)
            {
                return new SequenceInfo(30.0, 6.0);
            }
            using var manifest = JsonDocument.Parse(
                File.ReadAllBytes(Path.Combine(sequenceDirectory, "sequence.json")));
            var value = manifest.RootElement.GetProperty("framesPerSecond").GetDouble();
            var nativeStart = manifest.RootElement.TryGetProperty("from", out var from)
                ? from.GetDouble()
                : 6.0;
            return new SequenceInfo(
                value is >= 1.0 and <= 60.0 ? value : 30.0,
                double.IsFinite(nativeStart) ? nativeStart : 6.0);
        }
        catch (Exception)
        {
            return new SequenceInfo(30.0, 6.0);
        }
    }

    private static byte[]? TryReadNonEmpty(string path)
    {
        try
        {
            var bytes = File.ReadAllBytes(path);
            return bytes.Length > 0 ? bytes : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private sealed record FramePaths(string Bank1, string Bank0)
    {
        public bool IsComplete => IsCompleteBank(Bank1) && IsCompleteBank(Bank0);

        private static bool IsCompleteBank(string directory) =>
            File.Exists(Path.Combine(directory, "00dabbc0.vert.spv")) &&
            File.Exists(Path.Combine(directory, "00daa420.frag.spv")) &&
            Enumerable.Range(0, Ps5NativeParticleDraw.RequiredVertexBufferCount).All(index =>
                File.Exists(Path.Combine(directory, $"00dabbc0.vert.buffer{index}.bin")));
    }

    private sealed record ComputeInputs(byte[] Spirv, byte[] Resources, byte[] Ids)
    {
        public static ComputeInputs? TryLoad(string directory)
        {
            try
            {
                var spirv = File.ReadAllBytes(Path.Combine(directory, "particle.spv"));
                var resources = File.ReadAllBytes(Path.Combine(directory, "resources.bin"));
                var ids = File.ReadAllBytes(Path.Combine(directory, "ids.bin"));
                var request = new Ps5NativeParticleComputeRequest(
                    spirv,
                    resources,
                    ids,
                    6.0f,
                    0.0f,
                    true);
                return request.IsValid ? new ComputeInputs(spirv, resources, ids) : null;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }

    private readonly record struct SequenceInfo(double FramesPerSecond, double NativeStart);
}
