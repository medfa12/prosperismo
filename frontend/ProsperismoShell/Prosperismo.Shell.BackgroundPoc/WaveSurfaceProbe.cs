// Copyright (C) 2026 Prosperismo Project
// SPDX-License-Identifier: GPL-2.0-or-later

using SharpEmu.HLE;
using SharpEmu.ShaderCompiler;
using SharpEmu.ShaderCompiler.Vulkan;

namespace Prosperismo.Shell.BackgroundPoc;

/// <summary>
/// Runs the wave surface's merged local+hull program.
///
/// <para><c>fw_flow_vl</c> and <c>fw_flow_h</c> are one hardware shader: the
/// local section fetches control points, displaces them by 3D simplex noise
/// driven by <c>time</c>, and writes 32 bytes per point to LDS; the hull copies
/// LDS into the patch ring and writes six tessellation factors. On GFX10 that
/// wave is compute-like, so it runs here as a compute dispatch — one workgroup
/// per patch, one invocation per control point, which is the hardware's own
/// arrangement.</para>
///
/// <para>Resource map, decoded from the two programs:</para>
/// <list type="bullet">
/// <item><c>s[8:11]</c> — the FirstWave constant buffer; the local section
/// reads <c>time</c> at <c>+0x184</c>.</item>
/// <item><c>s[12:13]</c> — a table of two vertex-buffer V#s at <c>+0x00</c> and
/// <c>+0x10</c>: the control lattice and the boundary ring.</item>
/// <item><c>s[0:1]</c> — a global table whose first pointer leads to a
/// descriptor block holding the tessellation-factor V# at <c>+0x20</c> and the
/// patch-ring V# at <c>+0x30</c>.</item>
/// </list>
/// </summary>
internal static class WaveSurfaceProbe
{
    private const ulong ProgramAddress = 0x1000_0000;
    private const ulong ConstantsAddress = 0x0200_0000;
    private const ulong VertexTableAddress = 0x0300_0000;
    private const ulong LatticeAddress = 0x0400_0000;
    private const ulong RingAddress = 0x0500_0000;
    private const ulong GlobalTableAddress = 0x0600_0000;
    private const ulong DescriptorBlockAddress = 0x0700_0000;
    private const ulong PatchRingAddress = 0x0800_0000;
    private const ulong TessFactorAddress = 0x0900_0000;

    private const int LocalOffset = 0x11F6900;
    private const int LocalLength = 0x72C;
    private const int HullOffset = 0x11F6600;
    private const int HullLength = 0x108;

    private const int ControlPoints = 16;
    private const int PatchRingBytes = 0x4000;
    private const int TessFactorBytes = 0x1000;

    private static void WriteVertexDescriptor(
        Span<byte> destination, ulong address, int stride, int records, uint word3)
    {
        FirstWaveProbe.WriteBufferDescriptorPublic(destination, address, stride, records);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(destination[12..], word3);
    }

    internal static int Run(string eboot, string constantsPath, string seedsPath)
    {
        var image = File.ReadAllBytes(eboot);

        var merged = new byte[LocalLength + HullLength];
        Array.Copy(image, LocalOffset, merged, 0, LocalLength);
        Array.Copy(image, HullOffset, merged, LocalLength, HullLength);

        var constants = new byte[0x200];
        if (File.Exists(constantsPath))
        {
            var recovered = File.ReadAllBytes(constantsPath);
            recovered.AsSpan(0, Math.Min(recovered.Length, constants.Length)).CopyTo(constants);
        }

        // The 11x15 control lattice and the 13-pair boundary ring, exact words
        // from evidence/firstwave-host-constants-12.40.json.
        var seeds = File.ReadAllBytes(seedsPath);
        var latticeBytes = 165 * 16;
        var lattice = new byte[latticeBytes];
        var ring = new byte[26 * 16];
        seeds.AsSpan(0, Math.Min(latticeBytes, seeds.Length)).CopyTo(lattice);
        if (seeds.Length >= latticeBytes + ring.Length)
        {
            seeds.AsSpan(latticeBytes, ring.Length).CopyTo(ring);
        }

        // buffer_load_format_* fetches through the V#'s own FORMAT field, so a
        // descriptor with word 3 left at zero returns nothing and the local
        // section's normalise turns into a NaN. Word 3 is
        // dst_sel_x|y|z|w in bits 11:0 and the RDNA2 unified FORMAT in 18:12;
        // 74 is 32_32_32_FLOAT and 77 is 32_32_32_32_FLOAT.
        const uint selectXyzw = 4u | (5u << 3) | (6u << 6) | (7u << 9);
        const uint selectXyz1 = 4u | (5u << 3) | (6u << 6) | (1u << 9);
        var vertexTable = new byte[0x100];
        WriteVertexDescriptor(
            vertexTable.AsSpan(0x00, 16), LatticeAddress, 16, 165, selectXyzw | (77u << 12));
        WriteVertexDescriptor(
            vertexTable.AsSpan(0x10, 16), RingAddress, 16, 26, selectXyz1 | (74u << 12));

        var descriptorBlock = new byte[0x100];
        FirstWaveProbe.WriteBufferDescriptorPublic(
            descriptorBlock.AsSpan(0x20, 16), TessFactorAddress, 0, TessFactorBytes);
        FirstWaveProbe.WriteBufferDescriptorPublic(
            descriptorBlock.AsSpan(0x30, 16), PatchRingAddress, 0, PatchRingBytes);

        var globalTable = new byte[0x100];
        BitConverter.TryWriteBytes(globalTable.AsSpan(0x00), DescriptorBlockAddress);

        var memory = new FirstWaveProbe.FlatMemory();
        memory.AddRegion(ProgramAddress, merged);
        memory.AddRegion(ConstantsAddress, constants);
        memory.AddRegion(VertexTableAddress, vertexTable);
        memory.AddRegion(LatticeAddress, lattice);
        memory.AddRegion(RingAddress, ring);
        memory.AddRegion(GlobalTableAddress, globalTable);
        memory.AddRegion(DescriptorBlockAddress, descriptorBlock);
        memory.AddRegion(PatchRingAddress, new byte[PatchRingBytes]);
        memory.AddRegion(TessFactorAddress, new byte[TessFactorBytes]);

        var context = new CpuContext(memory, Generation.Gen5);
        if (!Gen5ShaderTranslator.TryDecodeMergedProgram(
                context, ProgramAddress, out var program, out var error))
        {
            Console.Error.WriteLine($"decode  : FAILED {error}");
            return 1;
        }

        Console.WriteLine($"decode  : OK - {program.Instructions.Count} instructions (local + hull)");

        // s0..s7 are system SGPRs for a merged local+hull wave; the six user
        // SGPRs start at s8. s3 is the merged wave info the prologue turns into
        // EXEC, and s[0:1] is the global table the hull reads its descriptor
        // block through.
        var userData = new uint[14];
        BitConverter.TryWriteBytes(
            System.Runtime.InteropServices.MemoryMarshal.AsBytes(userData.AsSpan(0, 2)),
            GlobalTableAddress);
        userData[3] = ControlPoints | (ControlPoints << 8);
        FirstWaveProbe.WriteBufferDescriptorPublic(
            System.Runtime.InteropServices.MemoryMarshal.AsBytes(userData.AsSpan(8, 4)),
            ConstantsAddress, 0, constants.Length);
        BitConverter.TryWriteBytes(
            System.Runtime.InteropServices.MemoryMarshal.AsBytes(userData.AsSpan(12, 2)),
            VertexTableAddress);

        var state = new Gen5ShaderState(
            program, userData, Metadata: null,
            ComputeSystemRegisters: new Gen5ComputeSystemRegisters(4, null, null, null),
            UserDataScalarRegisterBase: 0);

        if (!Gen5ShaderScalarEvaluator.TryEvaluate(context, state, out var evaluation, out error))
        {
            Console.Error.WriteLine($"evaluate: FAILED {error}");
            return 1;
        }

        Console.WriteLine(
            $"evaluate: OK - {evaluation.GlobalMemoryBindings.Count} buffer(s), " +
            $"{evaluation.ImageBindings.Count} image(s)");
        foreach (var binding in evaluation.GlobalMemoryBindings)
        {
            Console.WriteLine($"          base=0x{binding.BaseAddress:X8} {binding.DataLength,8:N0} bytes" +
                $"{(binding.Writable ? " (writable)" : string.Empty)}");
        }

        if (!Gen5SpirvTranslator.TryCompileComputeShader(
                state, evaluation, ControlPoints, 1, 1, out var compiled, out error,
                waveLaneCount: uint.TryParse(Environment.GetEnvironmentVariable("WAVE_LANES"), out var wl) ? wl : 32,
                // Apple GPUs cap threadgroup memory at 32 KB where the PS5 gives
                // a workgroup 64 KB. The merged wave's LDS reach here is
                // cpIndex*512 + patchId*32, so 32 KB covers it; an access past
                // the allocation would be reported by ldsAddressOutOfRange.
                ldsDwordCount: 32 * 1024 / sizeof(uint),
                // The local section reads v2 as the vertex index and v3 as the
                // LDS slot; the hull reads v1 as (controlPoint << 8) | patchId,
                // giving it a ring address of patch*512 + point*32 - the
                // 32-byte stride fw_flow_dv reads sixteen control points at.
                mergedWaveSeeding: new Gen5MergedWaveVgprSeeding(
                    VertexIndexVgpr: 2,
                    LdsSlotVgpr: 3,
                    LdsSlotStride: 16,   // patches are 16 control points apart in LDS
                    PackedIdVgpr: 1,
                    PatchId: 0)))
        {
            Console.Error.WriteLine($"spirv   : FAILED {error}");
            return 1;
        }

        Console.WriteLine($"spirv   : OK - {compiled.Spirv.Length:N0} bytes");
        var spirvOut = Environment.GetEnvironmentVariable("WAVE_SPIRV_OUT");
        if (!string.IsNullOrEmpty(spirvOut))
        {
            File.WriteAllBytes(spirvOut, compiled.Spirv);
        }

        var uploads = new byte[compiled.GlobalMemoryBindings.Count][];
        var tessIndex = -1;
        var ringIndex = -1;
        for (var i = 0; i < uploads.Length; i++)
        {
            var binding = compiled.GlobalMemoryBindings[i];
            var data = new byte[binding.DataLength];
            var source = binding.BaseAddress switch
            {
                ConstantsAddress => constants,
                VertexTableAddress => vertexTable,
                LatticeAddress => lattice,
                RingAddress => ring,
                GlobalTableAddress => globalTable,
                DescriptorBlockAddress => descriptorBlock,
                _ => null,
            };

            if (binding.BaseAddress == TessFactorAddress)
            {
                tessIndex = i;
            }
            else if (binding.BaseAddress == PatchRingAddress)
            {
                ringIndex = i;
            }

            source?.AsSpan(0, Math.Min(source.Length, data.Length)).CopyTo(data);
            uploads[i] = data;
        }

        byte[][] results;
        using (var runner = new ParticleComputeRunner())
        {
            Console.WriteLine($"device  : {runner.DeviceName}");
            results = runner.Dispatch(compiled.Spirv, uploads, 1, ControlPoints);
        }

        Console.WriteLine("dispatch: OK");

        if (tessIndex >= 0)
        {
            // The hull materialises 12.0 with v_cvt_f32_i32 from an inline
            // constant and stores six of them: four outer factors at +0x00 and
            // two inner at +0x10. Reading them back is the check that the whole
            // merged wave ran, not just compiled.
            var tess = results[tessIndex];
            var factors = new float[6];
            for (var i = 0; i < 4; i++)
            {
                factors[i] = BitConverter.ToSingle(tess, i * 4);
            }

            factors[4] = BitConverter.ToSingle(tess, 0x10);
            factors[5] = BitConverter.ToSingle(tess, 0x14);
            Console.WriteLine(
                $"tess    : outer=[{string.Join(", ", factors[..4].Select(f => f.ToString("G4")))}] " +
                $"inner=[{factors[4]:G4}, {factors[5]:G4}]");
        }

        if (ringIndex >= 0)
        {
            var ringOut = results[ringIndex];
            var written = 0;
            for (var i = 0; i < ringOut.Length; i++)
            {
                if (ringOut[i] != 0)
                {
                    written++;
                }
            }

            Console.WriteLine($"patches : {written:N0} of {ringOut.Length:N0} bytes written");
            // The hull addresses the ring as patch*512 + controlPoint*32, which
            // is the 32-byte stride fw_flow_dv reads its sixteen control points
            // at, so one patch's points are contiguous.
            var live = 0;
            for (var cp = 0; cp < ControlPoints; cp++)
            {
                var v = new float[4];
                for (var k = 0; k < 4; k++)
                {
                    v[k] = BitConverter.ToSingle(ringOut, (cp * 32) + (k * 4));
                }

                if (v[3] != 0f)
                {
                    live++;
                }

                if (cp < 4)
                {
                    Console.WriteLine(
                        $"          control point {cp}: ({v[0]:G6}, {v[1]:G6}, {v[2]:G6}, {v[3]:G6})");
                }
            }

            Console.WriteLine($"          {live} of {ControlPoints} control points written");
        }

        return 0;
    }
}
