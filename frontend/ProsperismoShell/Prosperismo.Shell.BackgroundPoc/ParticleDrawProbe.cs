// Copyright (C) 2026 Prosperismo Project
// SPDX-License-Identifier: GPL-2.0-or-later

using SharpEmu.HLE;
using SharpEmu.ShaderCompiler;
using SharpEmu.ShaderCompiler.Vulkan;

namespace Prosperismo.Shell.BackgroundPoc;

/// <summary>
/// Renders the PS5 background's particle field by executing Sony's own
/// programs: <c>particle_c</c> moves the particles, <c>particle_vv</c> expands
/// each one into a billboard quad, and <c>particle_p</c> shades it.
///
/// <para>Every number that shapes the result comes out of the firmware — the
/// programs from the eboot's instruction stream, the parameters from the
/// serialized <c>coldboot</c> pattern blob replayed at the frame's authored
/// time (see <c>tools/export_particle_frames.py</c>), and the particle ID
/// permutation from the allocator's own xorshift128+. Nothing is modelled.
/// </para>
/// </summary>
internal static class ParticleDrawProbe
{
    private const ulong ProgramAddress = 0x1000_0000;
    private const ulong SrtCsAddress = 0x0200_0000;
    private const ulong ResourcesCsAddress = 0x0300_0000;
    private const ulong PropertyAddress = 0x0400_0000;
    private const ulong IdAddress = 0x0500_0000;
    private const ulong SrtVsPsAddress = 0x0600_0000;
    private const ulong ResourcesVsPsAddress = 0x0700_0000;

    private const int RecordStride = 0x44;
    private const int RecordCount = 0x1770;

    // File offsets in the 12.40 eboot, from docs/sony-shell/particle-draw.md.
    // The slice runs past the first s_endpgm on purpose: both vertex programs
    // read a corner table embedded after their code through s_getpc_b64, and
    // particle_p's palette sits after its discard epilogue.
    private const int ParticleComputeOffset = 0x11FA100;
    private const int ParticleComputeLength = 0x71A4;
    private const int ParticleVsOffset = 0x1201D00;
    private const int ParticleVsLength = 0x700;
    private const int ParticlePsOffset = 0x1201500;
    private const int ParticlePsLength = 0x800;
    private const int PlatePsOffset = 0x11F9300;
    private const int PlatePsLength = 0x230;
    private const ulong PlateConstantsAddress = 0x0800_0000;

    private readonly record struct Group(int Kind, int Index, byte[] Compute, byte[] Draw);

    internal static int Render(
        string eboot, string framesDirectory, string outputDirectory, uint width, uint height, float fps)
    {
        var image = File.ReadAllBytes(eboot);

        // The plate is the layer the particles sit on: fw_background_p, fed the
        // constant buffer the firmware's own builder produces. Optional so the
        // particle field can still be rendered alone.
        var plateConstantsPath = Environment.GetEnvironmentVariable("PLATE_CONSTANTS");
        var fullscreenVsPath = Environment.GetEnvironmentVariable("FULLSCREEN_VS");
        ParticleComputeRunner.ParticleDraw? plate = null;
        if (!string.IsNullOrEmpty(plateConstantsPath) && !string.IsNullOrEmpty(fullscreenVsPath) &&
            File.Exists(plateConstantsPath) && File.Exists(fullscreenVsPath))
        {
            if (!BuildPlate(image, plateConstantsPath, fullscreenVsPath, out var built, out var plateError))
            {
                Console.Error.WriteLine($"plate: {plateError}");
                return 1;
            }

            plate = built;
            Console.WriteLine($"plate   : fw_background_p from {Path.GetFileName(plateConstantsPath)}");
        }

        Directory.CreateDirectory(outputDirectory);

        var frameFiles = Directory.GetFiles(framesDirectory, "*.bin").OrderBy(x => x).ToArray();
        if (frameFiles.Length == 0)
        {
            Console.Error.WriteLine($"no frame blocks in {framesDirectory}");
            return 2;
        }

        // One property bank for the whole render, exactly as the firmware
        // allocates it: 6000 records, zeroed, shared by every group. Each group
        // owns the slice [offsetParticle, offsetParticle + numParticles).
        var properties = new byte[RecordStride * RecordCount];
        var ids = FirstWaveProbe.BuildParticleIds(RecordCount);

        using var runner = new ParticleComputeRunner();
        Console.WriteLine($"device  : {runner.DeviceName}");
        Console.WriteLine($"frames  : {frameFiles.Length} at {fps} fps into {outputDirectory}");

        for (var frame = 0; frame < frameFiles.Length; frame++)
        {
            var (time, groups) = ReadFrame(frameFiles[frame]);
            var draws = new List<ParticleComputeRunner.ParticleDraw>();
            if (plate is { } basePlate)
            {
                draws.Add(basePlate);
            }

            var drawn = 0;

            foreach (var group in groups)
            {
                if (group.Kind != 0)
                {
                    // The large pair samples two GNF sprites; wire it after the
                    // small field is proven rather than half-doing both.
                    continue;
                }

                var count = BitConverter.ToUInt32(group.Compute, 0x28);
                if (count == 0)
                {
                    continue;
                }

                if (!Simulate(image, group, time, fps, properties, ids, runner, out var error))
                {
                    Console.Error.WriteLine($"frame {frame} group {group.Index}: {error}");
                    return 1;
                }

                if (!BuildDraw(image, group, properties, ids, out var draw, out error))
                {
                    Console.Error.WriteLine($"frame {frame} group {group.Index}: {error}");
                    return 1;
                }

                draws.Add(draw);
                drawn += (int)count;
            }

            byte[][][] after = [];
            var rgba = draws.Count == 0
                ? new byte[width * height * 4]
                : runner.RenderParticleFrame(draws, width, height, out after);

            // particle_vv latches renLife into the record for corner 0 when it
            // is still negative, and particle_p's life fade is
            //   smoothstep(sat(2*curLife)) * smoothstep(sat(2*(renLife - curLife)))
            // so an unlatched record shades to exactly black. The latch is a
            // guest-memory write: fold it back into the bank or every frame
            // stays dark.
            for (var d = plate is null ? 0 : 1; d < after.Length; d++)
            {
                for (var b = 0; b < after[d].Length; b++)
                {
                    if (after[d][b].Length != properties.Length)
                    {
                        continue;
                    }

                    // Each group only writes its own record range, so merge the
                    // dwords the shader actually changed. Copying a whole
                    // readback would discard every other group's latch.
                    var before = draws[d].Buffers[b];
                    for (var k = 0; k < properties.Length; k++)
                    {
                        if (after[d][b][k] != before[k])
                        {
                            properties[k] = after[d][b][k];
                        }
                    }

                    break;
                }
            }

            var clipDump = Environment.GetEnvironmentVariable("CLIP_OUT");
            var clipBuffer = int.TryParse(Environment.GetEnvironmentVariable("CLIP_BUFFER"), out var cb) ? cb : 2;
            if (!string.IsNullOrEmpty(clipDump) && after.Length > 0 && after[0].Length > clipBuffer)
            {
                File.WriteAllBytes(clipDump, after[0][clipBuffer]);
            }

            if (Environment.GetEnvironmentVariable("TRACE_VS") == "1" && after.Length > 0)
            {
                for (var d = 0; d < after.Length; d++)
                {
                    for (var b = 0; b < after[d].Length; b++)
                    {
                        var changed = 0;
                        for (var k = 0; k < after[d][b].Length; k++)
                        {
                            if (after[d][b][k] != draws[d].Buffers[b][k])
                            {
                                changed++;
                            }
                        }

                        Console.WriteLine(
                            $"    draw[{d}] buffer[{b}] {after[d][b].Length,8:N0} bytes {changed,7:N0} changed");
                    }
                }
            }

            var path = Path.Combine(outputDirectory, $"{frame:D5}.png");
            FirstWaveProbe.WritePngPublic(path, (int)width, (int)height, rgba);

            var lit = 0;
            for (var i = 0; i < rgba.Length; i += 4)
            {
                if (rgba[i] != 0 || rgba[i + 1] != 0 || rgba[i + 2] != 0)
                {
                    lit++;
                }
            }

            var touched = 0;
            for (var i = 3; i < rgba.Length; i += 4)
            {
                if (rgba[i] != 255)
                {
                    touched++;
                }
            }

            Console.WriteLine(
                $"  frame {frame:D5} t={time,7:F3} groups={draws.Count - (plate is null ? 0 : 1)} particles={drawn,5} " +
                $"lit={lit,9:N0} touched={touched,9:N0}");
        }

        return 0;
    }

    /// <summary>
    /// Runs <c>particle_c</c> for one group and folds the result back into the
    /// shared property bank.
    /// </summary>
    private static bool Simulate(
        byte[] image,
        Group group,
        float time,
        float fps,
        byte[] properties,
        byte[] ids,
        ParticleComputeRunner runner,
        out string error)
    {
        var srt = new byte[0x1000];
        BitConverter.TryWriteBytes(srt.AsSpan(0x00), ResourcesCsAddress);
        BitConverter.TryWriteBytes(srt.AsSpan(0x08, 4), time);
        BitConverter.TryWriteBytes(srt.AsSpan(0x0C, 4), 1f / fps);
        BitConverter.TryWriteBytes(srt.AsSpan(0x10, 4), 1f);
        BitConverter.TryWriteBytes(srt.AsSpan(0x14, 4), 0u);
        BitConverter.TryWriteBytes(srt.AsSpan(0x18, 4), 0u);

        var resources = new byte[0x1000];
        group.Compute.AsSpan(0, Math.Min(group.Compute.Length, resources.Length)).CopyTo(resources);
        FirstWaveProbe.WriteBufferDescriptorPublic(resources.AsSpan(0x00, 16), IdAddress, 4, RecordCount);
        FirstWaveProbe.WriteBufferDescriptorPublic(
            resources.AsSpan(0x10, 16), PropertyAddress, RecordStride, RecordCount);

        var memory = new FirstWaveProbe.FlatMemory();
        memory.AddRegion(ProgramAddress, Slice(image, ParticleComputeOffset, ParticleComputeLength));
        memory.AddRegion(SrtCsAddress, srt);
        memory.AddRegion(ResourcesCsAddress, resources);
        memory.AddRegion(PropertyAddress, properties);
        memory.AddRegion(IdAddress, ids);

        var context = new CpuContext(memory, Generation.Gen5);
        if (!Gen5ShaderTranslator.TryDecodeProgram(context, ProgramAddress, out var decoded, out error))
        {
            return false;
        }

        var userData = new uint[4];
        FirstWaveProbe.WriteBufferDescriptorPublic(
            System.Runtime.InteropServices.MemoryMarshal.AsBytes(userData.AsSpan(0, 4)),
            SrtCsAddress, 0, srt.Length);

        var state = new Gen5ShaderState(
            decoded,
            userData,
            Metadata: null,
            ComputeSystemRegisters: new Gen5ComputeSystemRegisters(4, null, null, null),
            UserDataScalarRegisterBase: 0,
            ProgramResource1: 0x0000_0090);

        if (!Gen5ShaderScalarEvaluator.TryEvaluate(context, state, out var evaluation, out error))
        {
            return false;
        }

        if (!Gen5SpirvTranslator.TryCompileComputeShader(
                state, evaluation, 64, 1, 1, out var compiled, out error, waveLaneCount: 64))
        {
            return false;
        }

        var count = BitConverter.ToUInt32(group.Compute, 0x28);
        var uploads = new byte[compiled.GlobalMemoryBindings.Count][];
        var propertyIndex = -1;
        for (var i = 0; i < uploads.Length; i++)
        {
            var binding = compiled.GlobalMemoryBindings[i];
            var data = new byte[binding.DataLength];
            var source = binding.BaseAddress switch
            {
                SrtCsAddress => srt,
                ResourcesCsAddress => resources,
                PropertyAddress => properties,
                IdAddress => ids,
                _ => null,
            };

            if (binding.BaseAddress == PropertyAddress)
            {
                propertyIndex = i;
            }

            source?.AsSpan(0, Math.Min(source.Length, data.Length)).CopyTo(data);
            uploads[i] = data;
        }

        var results = runner.Dispatch(compiled.Spirv, uploads, (count + 63) / 64, count);
        if (propertyIndex >= 0)
        {
            results[propertyIndex].AsSpan(0, Math.Min(results[propertyIndex].Length, properties.Length))
                .CopyTo(properties);
        }

        if (Environment.GetEnvironmentVariable("TRACE_SIM") == "1")
        {
            var live = 0;
            for (var r = 0; r < RecordCount; r++)
            {
                if (BitConverter.ToSingle(properties, (r * RecordStride) + 0x38) != 0f)
                {
                    live++;
                }
            }

            Console.WriteLine(
                $"    sim group {group.Index} count={count} offset={BitConverter.ToUInt32(group.Compute, 0x30)}" +
                $" -> {live:N0} live records");
        }

        error = string.Empty;
        return true;
    }

    /// <summary>
    /// Translates <c>particle_vv</c> and <c>particle_p</c> for one group.
    ///
    /// <para>The two stages reach their data differently. The vertex program
    /// takes the record buffer as a V# straight out of user data at
    /// <c>s[0:3]</c> and the SRT at <c>s[8:11]</c> — its buffer loads name
    /// <c>s[0:3]</c> with record offsets 0x00/0x1C/0x28/0x2C/0x40, which is
    /// <c>pos</c>/<c>fore</c>/<c>transPatternFlag</c>/<c>right</c>/<c>renLife</c>.
    /// The pixel program takes only the SRT, at <c>s[0:3]</c>.</para>
    /// </summary>
    private static bool BuildDraw(
        byte[] image,
        Group group,
        byte[] properties,
        byte[] ids,
        out ParticleComputeRunner.ParticleDraw draw,
        out string error)
    {
        draw = default;

        var resources = new byte[0x1000];
        group.Draw.AsSpan(0, Math.Min(group.Draw.Length, resources.Length)).CopyTo(resources);

        // +0x00 the record buffer, +0x10 the per-slot u32 the size lottery
        // seeds from. Both are runtime allocations, so the blob never writes
        // them; everything from +0x20 up is Sony's authored data.
        FirstWaveProbe.WriteBufferDescriptorPublic(
            resources.AsSpan(0x00, 16), PropertyAddress, RecordStride, RecordCount);
        FirstWaveProbe.WriteBufferDescriptorPublic(resources.AsSpan(0x10, 16), IdAddress, 4, RecordCount);

        var srt = new byte[0x1000];
        BitConverter.TryWriteBytes(srt.AsSpan(0x00), ResourcesVsPsAddress);
        BitConverter.TryWriteBytes(srt.AsSpan(0x10, 4), 0u);   // transPatternFlag
        BitConverter.TryWriteBytes(srt.AsSpan(0x14, 4), 0u);

        var memory = new FirstWaveProbe.FlatMemory();
        memory.AddRegion(ProgramAddress, Slice(image, ParticleVsOffset, ParticleVsLength));
        memory.AddRegion(SrtVsPsAddress, srt);
        memory.AddRegion(ResourcesVsPsAddress, resources);
        memory.AddRegion(PropertyAddress, properties);
        memory.AddRegion(IdAddress, ids);

        var vertexContext = new CpuContext(memory, Generation.Gen5);
        if (!Gen5ShaderTranslator.TryDecodeProgram(
                vertexContext, ProgramAddress, out var vertexProgram, out error))
        {
            error = $"vertex decode: {error}";
            return false;
        }

        // s[0:3] is NOT user data: pc=0x008C loads it from ResourcesVsPs+0x00
        // (the record buffer V#), and before that pc=0x0004 reads s3 as the NGG
        // merged wave info — vertex count in bits 7:0, primitive count in 15:8.
        // The prologue turns those into EXEC with
        // `s[126:127] = -1 >> (64 - count)`, so a zero there disables the wave.
        // The only real user data is the SRT V# at s[8:11].
        var vertexUserData = new uint[12];
        vertexUserData[3] = 0x0000_4040;
        FirstWaveProbe.WriteBufferDescriptorPublic(
            System.Runtime.InteropServices.MemoryMarshal.AsBytes(vertexUserData.AsSpan(8, 4)),
            SrtVsPsAddress, 0, srt.Length);

        var vertexState = new Gen5ShaderState(
            vertexProgram, vertexUserData, Metadata: null, UserDataScalarRegisterBase: 0);
        if (!Gen5ShaderScalarEvaluator.TryEvaluate(
                vertexContext, vertexState, out var vertexEvaluation, out error))
        {
            error = $"vertex evaluate: {error}";
            return false;
        }

        var pixelMemory = new FirstWaveProbe.FlatMemory();
        pixelMemory.AddRegion(ProgramAddress, Slice(image, ParticlePsOffset, ParticlePsLength));
        pixelMemory.AddRegion(SrtVsPsAddress, srt);
        pixelMemory.AddRegion(ResourcesVsPsAddress, resources);
        pixelMemory.AddRegion(PropertyAddress, properties);
        pixelMemory.AddRegion(IdAddress, ids);

        var pixelContext = new CpuContext(pixelMemory, Generation.Gen5);
        if (!Gen5ShaderTranslator.TryDecodeProgram(
                pixelContext, ProgramAddress, out var pixelProgram, out error))
        {
            error = $"pixel decode: {error}";
            return false;
        }

        var pixelUserData = new uint[4];
        FirstWaveProbe.WriteBufferDescriptorPublic(
            System.Runtime.InteropServices.MemoryMarshal.AsBytes(pixelUserData.AsSpan(0, 4)),
            SrtVsPsAddress, 0, srt.Length);

        var pixelState = new Gen5ShaderState(
            pixelProgram, pixelUserData, Metadata: null, UserDataScalarRegisterBase: 0);
        if (!Gen5ShaderScalarEvaluator.TryEvaluate(
                pixelContext, pixelState, out var pixelEvaluation, out error))
        {
            error = $"pixel evaluate: {error}";
            return false;
        }

        // The two stages share one storage-buffer array, so each is told where
        // its own slice starts and how long the whole array is.
        var vertexBufferCount = vertexEvaluation.GlobalMemoryBindings.Count;
        var pixelBufferCount = pixelEvaluation.GlobalMemoryBindings.Count;
        var total = vertexBufferCount + pixelBufferCount;

        if (!Gen5SpirvTranslator.TryCompileVertexShader(
                vertexState,
                vertexEvaluation,
                out var vertexShader,
                out error,
                globalBufferBase: 0,
                totalGlobalBufferCount: total,
                requiredVertexOutputCount: 6))
        {
            error = $"vertex spirv: {error}";
            return false;
        }

        // SPI_PS_INPUT_ENA/ADDR = 0x2 and SPI_PS_IN_CONTROL.NUM_INTERP = 6 are
        // the values the firmware itself programs for particle_p — read out of
        // its shader header with tools/dump_shader_registers.py, not chosen.
        if (!Gen5SpirvTranslator.TryCompilePixelShader(
                pixelState,
                pixelEvaluation,
                Gen5PixelOutputKind.Float,
                out var pixelShader,
                out error,
                globalBufferBase: vertexBufferCount,
                totalGlobalBufferCount: total,
                pixelInputEnable: 0x2,
                pixelInputAddress: 0x2))
        {
            error = $"pixel spirv: {error}";
            return false;
        }

        var spirvOut = Environment.GetEnvironmentVariable("DRAW_SPIRV_OUT");
        if (!string.IsNullOrEmpty(spirvOut))
        {
            File.WriteAllBytes($"{spirvOut}.vs.spv", vertexShader.Spirv);
            File.WriteAllBytes($"{spirvOut}.ps.spv", pixelShader.Spirv);
        }

        // Both programs reach data embedded in their own image through
        // s_getpc_b64: particle_vv's 48-byte billboard corner table at +0x500
        // and particle_p's 84-byte palette at +0x630. Those bindings resolve to
        // an address inside the program, so they have to be served from the
        // shader bytes. Uploading zeros there collapses all six corners of
        // every quad onto one point and nothing rasterises.
        var vertexText = Slice(image, ParticleVsOffset, ParticleVsLength);
        var pixelText = Slice(image, ParticlePsOffset, ParticlePsLength);

        var buffers = new byte[total][];
        var alias = new int[total];
        var byAddress = new Dictionary<ulong, int>();
        for (var i = 0; i < total; i++)
        {
            alias[i] = -1;
            var fromVertex = i < vertexBufferCount;
            var binding = fromVertex
                ? vertexShader.GlobalMemoryBindings[i]
                : pixelShader.GlobalMemoryBindings[i - vertexBufferCount];
            var data = new byte[binding.DataLength];
            var text = fromVertex ? vertexText : pixelText;

            byte[]? source;
            var offset = 0;
            if (binding.BaseAddress >= ProgramAddress &&
                binding.BaseAddress < ProgramAddress + (ulong)text.Length)
            {
                source = text;
                offset = (int)(binding.BaseAddress - ProgramAddress);
            }
            else
            {
                source = binding.BaseAddress switch
                {
                    SrtVsPsAddress => srt,
                    ResourcesVsPsAddress => resources,
                    PropertyAddress => properties,
                    IdAddress => ids,
                    _ => null,
                };
            }

            if (source is null)
            {
                error = $"unmapped binding base 0x{binding.BaseAddress:X}";
                return false;
            }

            source.AsSpan(offset, Math.Min(source.Length - offset, data.Length)).CopyTo(data);
            buffers[i] = data;

            // One guest allocation, one GPU buffer, however many descriptor
            // slots address it.
            if (byAddress.TryGetValue(binding.BaseAddress, out var firstSlot) &&
                buffers[firstSlot].Length == data.Length)
            {
                alias[i] = firstSlot;
            }
            else
            {
                byAddress[binding.BaseAddress] = i;
            }
        }

        var count = BitConverter.ToUInt32(group.Draw, 0x20);

        // DEBUG_FS swaps in a trivial fragment stage. It isolates "the vertex
        // program produced no geometry" from "the pixel program killed every
        // fragment"; it is a diagnostic, never part of a render.
        var debugFragment = Environment.GetEnvironmentVariable("DEBUG_FS");
        var fragmentSpirv = !string.IsNullOrEmpty(debugFragment) && File.Exists(debugFragment)
            ? File.ReadAllBytes(debugFragment)
            : pixelShader.Spirv;

        var debugPixel = Environment.GetEnvironmentVariable("DEBUG_PS_SPIRV");
        if (!string.IsNullOrEmpty(debugPixel) && File.Exists(debugPixel))
        {
            fragmentSpirv = File.ReadAllBytes(debugPixel);
        }

        var debugVertex = Environment.GetEnvironmentVariable("DEBUG_VS");
        var vertexSpirv = !string.IsNullOrEmpty(debugVertex) && File.Exists(debugVertex)
            ? File.ReadAllBytes(debugVertex)
            : vertexShader.Spirv;

        draw = new ParticleComputeRunner.ParticleDraw(
            vertexSpirv, fragmentSpirv, buffers, count * 6, alias);
        error = string.Empty;
        return true;
    }

    /// <summary>
    /// Translates <c>fw_background_p</c> as the frame's base layer.
    ///
    /// <para>It reads <c>FragCoord</c> out of v2/v3, so the pixel-input
    /// registers have to name PERSP_CENTER plus POS_X_FLOAT and POS_Y_FLOAT —
    /// 0x302. With PERSP_CENTER consuming v0 and v1, the position lands exactly
    /// where the shader looks. See firstwave-plate-executed.md.</para>
    /// </summary>
    private static bool BuildPlate(
        byte[] image,
        string constantsPath,
        string fullscreenVsPath,
        out ParticleComputeRunner.ParticleDraw draw,
        out string error)
    {
        draw = default;

        var constants = new byte[0x200];
        var recovered = File.ReadAllBytes(constantsPath);
        recovered.AsSpan(0, Math.Min(recovered.Length, constants.Length)).CopyTo(constants);

        var memory = new FirstWaveProbe.FlatMemory();
        memory.AddRegion(ProgramAddress, Slice(image, PlatePsOffset, PlatePsLength));
        memory.AddRegion(PlateConstantsAddress, constants);

        var context = new CpuContext(memory, Generation.Gen5);
        if (!Gen5ShaderTranslator.TryDecodeProgram(context, ProgramAddress, out var program, out error))
        {
            error = $"decode: {error}";
            return false;
        }

        var userData = new uint[4];
        FirstWaveProbe.WriteBufferDescriptorPublic(
            System.Runtime.InteropServices.MemoryMarshal.AsBytes(userData.AsSpan(0, 4)),
            PlateConstantsAddress, 0, constants.Length);

        var state = new Gen5ShaderState(program, userData, Metadata: null, UserDataScalarRegisterBase: 0);
        if (!Gen5ShaderScalarEvaluator.TryEvaluate(context, state, out var evaluation, out error))
        {
            error = $"evaluate: {error}";
            return false;
        }

        if (!Gen5SpirvTranslator.TryCompilePixelShader(
                state,
                evaluation,
                Gen5PixelOutputKind.Float,
                out var compiled,
                out error,
                pixelInputEnable: 0x302,
                pixelInputAddress: 0x302))
        {
            error = $"spirv: {error}";
            return false;
        }

        var buffers = new byte[compiled.GlobalMemoryBindings.Count][];
        for (var i = 0; i < buffers.Length; i++)
        {
            var data = new byte[compiled.GlobalMemoryBindings[i].DataLength];
            constants.AsSpan(0, Math.Min(constants.Length, data.Length)).CopyTo(data);
            buffers[i] = data;
        }

        draw = new ParticleComputeRunner.ParticleDraw(
            File.ReadAllBytes(fullscreenVsPath), compiled.Spirv, buffers, 3, null, Additive: false);
        return true;
    }

    private static byte[] Slice(byte[] image, int offset, int length)
    {
        var text = new byte[length];
        Array.Copy(image, offset, text, 0, length);
        return text;
    }

    private static (float Time, List<Group> Groups) ReadFrame(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);
        var magic = reader.ReadUInt32();
        if (magic != 0x4D524650)
        {
            throw new InvalidDataException($"{path}: not a PFRM frame block");
        }

        var groupCount = reader.ReadUInt32();
        var time = reader.ReadSingle();
        reader.ReadUInt32();

        var groups = new List<Group>((int)groupCount);
        for (var i = 0; i < groupCount; i++)
        {
            var kind = reader.ReadInt32();
            var index = reader.ReadInt32();
            var computeLength = reader.ReadInt32();
            var drawLength = reader.ReadInt32();
            groups.Add(new Group(
                kind, index, reader.ReadBytes(computeLength), reader.ReadBytes(drawLength)));
        }

        return (time, groups);
    }
}
