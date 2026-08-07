// Copyright (C) 2026 Prosperismo Project
// SPDX-License-Identifier: GPL-2.0-or-later

using Silk.NET.Vulkan;
using SharpEmu.HLE;
using SharpEmu.ShaderCompiler;
using SharpEmu.ShaderCompiler.Vulkan;

namespace Prosperismo.Shell.BackgroundPoc;

/// <summary>
/// Executes <c>light_p</c>, the background's light-shaft and compositing layer.
///
/// <para>Every input is the firmware's own: the program from the eboot's
/// instruction stream, <c>texFloor</c> and <c>texVolume</c> from the GNF blobs
/// <c>createIesTex</c> embeds at <c>0x1006AE0</c> and <c>0x10029E0</c>, the
/// <c>ColorCb</c> record replayed from the seeder at <c>0xEA786</c>, and the
/// pixel-input registers from the shader's own header. See
/// docs/sony-shell/light-shaft-layer.md.</para>
/// </summary>
internal static class LightLayerProbe
{
    private const ulong ProgramAddress = 0x1000_0000;
    private const ulong VolatileCbAddress = 0x0900_0000;
    private const ulong ColorCbAddress = 0x0A00_0000;
    private const ulong TexFloorAddress = 0x1000_0000_0UL;
    private const ulong TexVolumeAddress = 0x2000_0000_0UL;
    private const ulong TexParticleAddress = 0x3000_0000_0UL;

    private const int LightPsOffset = 0x11F9700;
    private const int LightPsLength = 0x818;

    // createIesTex's two literal arguments.
    private const int TexFloorBlob = 0x1006AE0 + 0x4000;
    private const int TexVolumeBlob = 0x10029E0 + 0x4000;
    private const int GnfPayloadOffset = 0x100;
    private const int GnfPayloadLength = 0x4000;
    private const int TextureSize = 128;

    internal static int Render(
        string eboot, string outPath, string colorCbPath, byte[]? particleRgba,
        uint width, uint height)
    {
        var image = File.ReadAllBytes(eboot);
        if (!TryBuildDraw(image, colorCbPath, particleRgba, width, height, 0f,
                out var standalone, out var buildError))
        {
            Console.Error.WriteLine(buildError);
            return 1;
        }

        using var soloRunner = new ParticleComputeRunner();
        Console.WriteLine($"device  : {soloRunner.DeviceName}");
        var solo = soloRunner.RenderParticleFrame([standalone], width, height);
        FirstWaveProbe.WritePngPublic(outPath, (int)width, (int)height, solo);
        Console.WriteLine($"output  : {outPath}");
        return 0;
    }

    /// <summary>
    /// Builds the light-layer draw: Sony's <c>light_p</c>, its two embedded GNF
    /// textures, the replayed <c>ColorCb</c> record, and the particle frame as
    /// <c>texP</c>.
    /// </summary>
    internal static bool TryBuildDraw(
        byte[] image, string colorCbPath, byte[]? particleRgba,
        uint width, uint height, float time,
        out ParticleComputeRunner.ParticleDraw draw, out string error)
    {
        draw = default;
        error = string.Empty;

        // ColorCb comes from tools/dump_wave_colour_presets.py, which replays
        // the seeder rather than reading the (runtime-initialised) table.
        var colorCb = new byte[0x100];
        if (!File.Exists(colorCbPath))
        {
            error = $"missing ColorCb record: {colorCbPath}";
            return false;
        }

        var record = File.ReadAllBytes(colorCbPath);
        record.AsSpan(0, Math.Min(record.Length, colorCb.Length)).CopyTo(colorCb);

        var volatileCb = new byte[0x100];
        BitConverter.TryWriteBytes(volatileCb.AsSpan(0x00, 4), time);
        BitConverter.TryWriteBytes(volatileCb.AsSpan(0x04, 4), 1f);            // opacity
        BitConverter.TryWriteBytes(volatileCb.AsSpan(0x08, 4), 1f);            // intensity
        BitConverter.TryWriteBytes(
            volatileCb.AsSpan(0x0C, 4), particleRgba is null ? 0f : 1f);       // particleAlpha

        var floor = ReadGnf(image, TexFloorBlob);
        var volume = ReadGnf(image, TexVolumeBlob);
        var particles = particleRgba ?? new byte[width * height * 4];

        var memory = new FirstWaveProbe.FlatMemory();
        memory.AddRegion(ProgramAddress, Slice(image, LightPsOffset, LightPsLength));
        memory.AddRegion(VolatileCbAddress, volatileCb);
        memory.AddRegion(ColorCbAddress, colorCb);

        var context = new CpuContext(memory, Generation.Gen5);
        if (!Gen5ShaderTranslator.TryDecodeProgram(
                context, ProgramAddress, out var program, out var stageError))
        {
            error = $"light decode: {stageError}";
            return false;
        }

        Console.WriteLine($"decode  : OK - {program.Instructions.Count} instructions");

        // light_p takes its resources straight from user SGPRs, no SRT
        // indirection: three T#s, then the two constant buffers.
        var userData = new uint[36];
        WriteImageDescriptor(userData.AsSpan(0, 8), image, TexFloorBlob, TexFloorAddress);
        WriteImageDescriptor(userData.AsSpan(8, 8), image, TexVolumeBlob, TexVolumeAddress);
        WriteImageDescriptor(userData.AsSpan(16, 8), image, TexVolumeBlob, TexParticleAddress);
        // texP is the particle target, so it is RGBA at the frame's own size.
        userData[16 + 1] = (userData[16 + 1] & 0xFFFF_FF00u) | 3u;
        FirstWaveProbe.WriteBufferDescriptorPublic(
            System.Runtime.InteropServices.MemoryMarshal.AsBytes(userData.AsSpan(24, 4)),
            VolatileCbAddress, 0, volatileCb.Length);
        FirstWaveProbe.WriteBufferDescriptorPublic(
            System.Runtime.InteropServices.MemoryMarshal.AsBytes(userData.AsSpan(28, 4)),
            ColorCbAddress, 0, colorCb.Length);

        var state = new Gen5ShaderState(program, userData, Metadata: null, UserDataScalarRegisterBase: 0);
        if (!Gen5ShaderScalarEvaluator.TryEvaluate(context, state, out var evaluation, out stageError))
        {
            error = $"light evaluate: {stageError}";
            return false;
        }

        Console.WriteLine(
            $"evaluate: OK - {evaluation.GlobalMemoryBindings.Count} buffer(s), " +
            $"{evaluation.ImageBindings.Count} image(s)");

        // SPI_PS_INPUT_ENA/ADDR = 0x2 and NUM_INTERP = 1 are what the firmware
        // programs for light_p, read from its shader header.
        if (!Gen5SpirvTranslator.TryCompilePixelShader(
                state, evaluation, Gen5PixelOutputKind.Float, out var compiled, out stageError,
                pixelInputEnable: 0x2, pixelInputAddress: 0x2))
        {
            error = $"light spirv: {stageError}";
            return false;
        }

        Console.WriteLine($"spirv   : OK - {compiled.Spirv.Length:N0} bytes");

        var buffers = new byte[compiled.GlobalMemoryBindings.Count][];
        for (var i = 0; i < buffers.Length; i++)
        {
            var binding = compiled.GlobalMemoryBindings[i];
            var data = new byte[binding.DataLength];
            var source = binding.BaseAddress switch
            {
                VolatileCbAddress => volatileCb,
                ColorCbAddress => colorCb,
                _ => null,
            };
            source?.AsSpan(0, Math.Min(source.Length, data.Length)).CopyTo(data);
            buffers[i] = data;
        }

        // The three T#s are tagged in the low byte of word1, which is where a
        // real descriptor keeps base_address[47:40]: 1 = texFloor,
        // 2 = texVolume, 3 = texP.
        var images = new List<ParticleComputeRunner.GuestImage>();
        foreach (var binding in compiled.ImageBindings)
        {
            images.Add((binding.ResourceDescriptor[1] & 0xFF) switch
            {
                3 => new ParticleComputeRunner.GuestImage(
                    particles, width, height, Format.R8G8B8A8Unorm),
                2 => new ParticleComputeRunner.GuestImage(
                    volume, TextureSize, TextureSize, Format.R8Unorm),
                _ => new ParticleComputeRunner.GuestImage(
                    floor, TextureSize, TextureSize, Format.R8Unorm),
            });
        }

        Console.WriteLine($"images  : {images.Count} bound");
        for (var i = 0; i < compiled.ImageBindings.Count; i++)
        {
            var b = compiled.ImageBindings[i];
            var addr = ((ulong)b.ResourceDescriptor[1] << 32) | b.ResourceDescriptor[0];
            Console.WriteLine(
                $"          [{i}] pc=0x{b.Pc:X} {b.Opcode} base=0x{addr:X} " +
                $"{images[i].Width}x{images[i].Height} {images[i].Format}");
        }

        var spirvOut = Environment.GetEnvironmentVariable("LIGHT_SPIRV_OUT");
        if (!string.IsNullOrEmpty(spirvOut))
        {
            File.WriteAllBytes(spirvOut, compiled.Spirv);
        }


        var vertexPath = Environment.GetEnvironmentVariable("FULLSCREEN_VS")
            ?? throw new InvalidOperationException("FULLSCREEN_VS not set");

        draw = new ParticleComputeRunner.ParticleDraw(
            File.ReadAllBytes(vertexPath), compiled.Spirv, buffers, 3, null, false, images);
        return true;
    }

    /// <summary>
    /// Reads a GNF blob's base level.
    ///
    /// <para>The blob is <c>0x4100</c> bytes with a <c>0xF8</c> header, and the
    /// payload starts at the next 256-byte boundary — <c>0x100</c> — which is
    /// exactly <c>0x4000</c> bytes, the 128×128 single-channel base level the
    /// descriptor declares.</para>
    ///
    /// <para><b>The tile order is unresolved.</b> The descriptor's
    /// <c>sw_mode</c> is 1 (<c>SW_256B_S</c>), so the payload is swizzled in
    /// 256-byte tiles rather than linear. A total-variation search over the
    /// in-tile bit orderings puts <c>x</c> in the low four address bits, which
    /// is the 16×16 tile that mode implies, and that is what this uses — but it
    /// has not been confirmed against a reference decode, so the detail inside
    /// each tile may be wrong.</para>
    /// </summary>
    private static byte[] ReadGnf(byte[] image, int blob)
    {
        var payload = new byte[GnfPayloadLength];
        Array.Copy(image, blob + GnfPayloadOffset, payload, 0, GnfPayloadLength);

        var linear = new byte[GnfPayloadLength];
        const int tile = 16;
        var tilesPerRow = TextureSize / tile;
        for (var ty = 0; ty < tilesPerRow; ty++)
        {
            for (var tx = 0; tx < tilesPerRow; tx++)
            {
                var basis = ((ty * tilesPerRow) + tx) * tile * tile;
                for (var y = 0; y < tile; y++)
                {
                    for (var x = 0; x < tile; x++)
                    {
                        linear[(((ty * tile) + y) * TextureSize) + (tx * tile) + x] =
                            payload[basis + (y * tile) + x];
                    }
                }
            }
        }

        return linear;
    }

    private static void WriteImageDescriptor(
        Span<uint> destination, byte[] image, int blob, ulong address)
    {
        for (var i = 0; i < 8; i++)
        {
            destination[i] = BitConverter.ToUInt32(image, blob + 0x10 + (i * 4));
        }

        destination[0] = (uint)address;
        destination[1] = (destination[1] & 0xFFFF_FF00u) | (uint)(address >> 32);
    }

    private static byte[] Slice(byte[] image, int offset, int length)
    {
        var text = new byte[length];
        Array.Copy(image, offset, text, 0, length);
        return text;
    }
}
