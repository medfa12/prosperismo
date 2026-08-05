// Copyright (C) 2026 Prosperismo Project
// SPDX-License-Identifier: GPL-2.0-or-later

using SharpEmu.Libs.Presentation;

namespace Prosperismo.Shell.BackgroundPoc;

/// <summary>
/// Proves the PS5 animated background can be produced on macOS from oracle
/// data: the shader is decoded out of a genuine firmware eboot, translated
/// from PS5 GCN to SPIR-V by the recovered Gen5 translator, and executed by
/// Vulkan through MoltenVK. Nothing here reimplements the effect.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        var eboot = Arg(args, "--eboot");
        var outDir = Arg(args, "--out") ?? "poc-out";
        if (eboot is null || !File.Exists(eboot))
        {
            Console.Error.WriteLine("usage: BackgroundPoc --eboot <NPXS40087/eboot.bin> [--out <dir>] [--frames N]");
            return 2;
        }

        var frames = int.TryParse(Arg(args, "--frames"), out var f) ? Math.Clamp(f, 1, 240) : 8;

        if (args.Contains("--scan"))
        {
            return Scan(eboot);
        }

        if (args.Contains("--sweep"))
        {
            return Sweep(eboot,
                Convert.ToInt64((Arg(args, "--offset") ?? "C751A0").Replace("0x", string.Empty), 16),
                Convert.ToInt32((Arg(args, "--length") ?? "1C90").Replace("0x", string.Empty), 16));
        }
        const int width = 960;
        const int height = 540;

        // The compiler's baked offset is for the donor's 4.03 eboot. Allow an
        // explicit slice so any firmware version can be pointed at.
        var offsetText = Arg(args, "--offset");
        var lengthText = Arg(args, "--length");
        var offset = offsetText is null
            ? Ps5NativeRippleCompiler.FirmwareElfOffset
            : Convert.ToInt64(offsetText.Replace("0x", string.Empty), 16);
        var length = lengthText is null
            ? Ps5NativeRippleCompiler.FirmwareElfLength
            : Convert.ToInt32(lengthText.Replace("0x", string.Empty), 16);

        Console.WriteLine($"eboot   : {eboot}");
        Console.WriteLine($"slice   : offset 0x{offset:X}, 0x{length:X} bytes");

        if (!TryCompileAt(eboot, offset, length, out var program, out var error))
        {
            Console.Error.WriteLine($"translate: FAILED - {error}");
            return 1;
        }

        Console.WriteLine($"translate: OK - {program.FragmentSpirv.Length:N0} bytes of SPIR-V");

        // A neutral source plate and an empty target; the shader supplies the
        // motion, so any variation between frames comes from the firmware code.
        var source = new byte[width * height * 4];
        var target = new byte[width * height * 4];
        for (var i = 0; i < source.Length; i += 4)
        {
            source[i] = 5; source[i + 1] = 10; source[i + 2] = 22; source[i + 3] = 255;
        }

        var constants = new List<ReadOnlyMemory<byte>>();
        for (var i = 0; i < frames; i++)
        {
            var c0 = new byte[40];
            // Slot 2 is the animating input, found by sweeping the buffer
            // (--sweep): it is the only slot whose value changes the image.
            var slot = int.TryParse(Arg(args, "--timeslot"), out var s) ? s : 2;
            BitConverter.TryWriteBytes(c0.AsSpan(slot * 4, 4), i * (1f / 30f));
            constants.Add(c0);
        }

        IReadOnlyList<Ps5NativeParticleFrame> rendered;
        try
        {
            rendered = Ps5NativeRippleRenderer.RenderOpaqueFrames(
                program, width, height, source, target, constants);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"render   : FAILED - {ex.GetType().Name}: {ex.Message}");
            return 1;
        }

        Directory.CreateDirectory(outDir);
        var distinct = new HashSet<string>();
        for (var i = 0; i < rendered.Count; i++)
        {
            var frame = rendered[i];
            var path = Path.Combine(outDir, $"ripple_{i:D3}.png");
            WritePng(path, frame.Width, frame.Height, frame.Rgba.Span);
            distinct.Add(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(frame.Rgba.Span)));
        }

        Console.WriteLine($"render   : OK - {rendered.Count} frame(s) at {width}x{height}");
        Console.WriteLine($"distinct : {distinct.Count} unique frame(s)  " +
                          $"({(distinct.Count > 1 ? "ANIMATED" : "static - shader ran but did not vary")})");
        Console.WriteLine($"output   : {Path.GetFullPath(outDir)}");
        return 0;
    }

    /// <summary>
    /// Drives each float slot of the 40-byte constant buffer in turn and
    /// reports which ones change the rendered image. The shader's ABI is not
    /// documented for this firmware, so the animating input is found by
    /// observation rather than assumed.
    /// </summary>
    private static int Sweep(string eboot, long offset, int length)
    {
        if (!Ps5NativeRippleCompiler.TryCompile(eboot, offset, length, out var program, out var error))
        {
            Console.Error.WriteLine($"translate: FAILED - {error}");
            return 1;
        }

        const int width = 320;
        const int height = 180;
        var source = new byte[width * height * 4];
        var target = new byte[width * height * 4];
        for (var i = 0; i < source.Length; i += 4)
        {
            source[i] = 5; source[i + 1] = 10; source[i + 2] = 22; source[i + 3] = 255;
        }

        Console.WriteLine("slot  distinct  note");
        for (var slot = 0; slot < 10; slot++)
        {
            var constants = new List<ReadOnlyMemory<byte>>();
            foreach (var value in new[] { 0f, 0.25f, 1f, 4f, 16f })
            {
                var c0 = new byte[40];
                BitConverter.TryWriteBytes(c0.AsSpan(slot * 4, 4), value);
                constants.Add(c0);
            }

            try
            {
                var frames = Ps5NativeRippleRenderer.RenderOpaqueFrames(
                    program, width, height, source, target, constants);
                var hashes = frames
                    .Select(f => Convert.ToHexString(
                        System.Security.Cryptography.SHA256.HashData(f.Rgba.Span)))
                    .Distinct()
                    .Count();
                var nonWhite = frames.Any(f => HasContent(f.Rgba.Span));
                Console.WriteLine($"{slot,4}  {hashes,8}  {(hashes > 1 ? "VARIES" : "static")}" +
                                  $"{(nonWhite ? ", has content" : ", blank")}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{slot,4}  {"-",8}  {ex.GetType().Name}");
            }
        }

        return 0;
    }

    private static bool HasContent(ReadOnlySpan<byte> rgba)
    {
        for (var i = 0; i < rgba.Length; i += 4)
        {
            if (rgba[i] != 255 || rgba[i + 1] != 255 || rgba[i + 2] != 255)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Walks every AMDGPU shader ELF in the eboot and reports which one the
    /// ripple translator accepts. The baked offset is one firmware build's;
    /// this finds the equivalent in any other.
    /// </summary>
    private static int Scan(string eboot)
    {
        var image = File.ReadAllBytes(eboot);
        var offsets = new List<long>();
        for (var i = 0; i + 20 < image.Length; i++)
        {
            if (image[i] == 0x7F && image[i + 1] == (byte)'E' &&
                image[i + 2] == (byte)'L' && image[i + 3] == (byte)'F' &&
                BitConverter.ToUInt16(image, i + 18) == 224)
            {
                offsets.Add(i);
            }
        }

        Console.WriteLine($"scanning {offsets.Count} AMDGPU shader ELFs...");
        var accepted = 0;
        for (var k = 0; k < offsets.Count; k++)
        {
            var start = offsets[k];
            var end = k + 1 < offsets.Count ? offsets[k + 1] : image.LongLength;
            var length = (int)Math.Min(end - start, 0x8000);
            if (!Ps5NativeRippleCompiler.TryCompile(eboot, start, length, out var p, out var err))
            {
                if (!err.Contains("ABI mismatch", StringComparison.Ordinal) &&
                    !err.Contains("does not contain an ELF", StringComparison.Ordinal))
                {
                    Console.WriteLine($"  0x{start:X}  len 0x{length:X}  {err}");
                }

                continue;
            }

            accepted++;
            Console.WriteLine($"  0x{start:X}  len 0x{length:X}  ACCEPTED - " +
                              $"{p.FragmentSpirv.Length:N0} bytes SPIR-V");
        }

        Console.WriteLine(accepted > 0
            ? $"{accepted} shader(s) match the ripple ABI"
            : "no shader in this eboot matches the ripple ABI");
        return accepted > 0 ? 0 : 1;
    }

    /// <summary>
    /// Same pipeline as Ps5NativeRippleCompiler.TryCompile, with the firmware
    /// slice supplied rather than baked to one firmware version.
    /// </summary>
    private static bool TryCompileAt(
        string eboot, long offset, int length,
        out Ps5NativeRippleProgram program, out string error)
    {
        var saved = (Ps5NativeRippleCompiler.FirmwareElfOffset,
                     Ps5NativeRippleCompiler.FirmwareElfLength);
        _ = saved;
        return Ps5NativeRippleCompiler.TryCompile(eboot, offset, length, out program, out error);
    }

    private static string? Arg(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.Ordinal))
            {
                return args[i + 1];
            }
        }

        return null;
    }

    private static void WritePng(string path, int width, int height, ReadOnlySpan<byte> rgba)
    {
        var raw = new byte[(width * 4 + 1) * height];
        for (var y = 0; y < height; y++)
        {
            raw[y * (width * 4 + 1)] = 0;
            rgba.Slice(y * width * 4, width * 4).CopyTo(raw.AsSpan(y * (width * 4 + 1) + 1));
        }

        using var file = File.Create(path);
        file.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        var ihdr = new byte[13];
        BinaryPrimitives(ihdr.AsSpan(0, 4), (uint)width);
        BinaryPrimitives(ihdr.AsSpan(4, 4), (uint)height);
        ihdr[8] = 8; ihdr[9] = 6;
        WriteChunk(file, "IHDR", ihdr);
        using var deflated = new MemoryStream();
        using (var zlib = new System.IO.Compression.ZLibStream(
                   deflated, System.IO.Compression.CompressionLevel.Fastest, true))
        {
            zlib.Write(raw);
        }

        WriteChunk(file, "IDAT", deflated.ToArray());
        WriteChunk(file, "IEND", Array.Empty<byte>());
    }

    private static void BinaryPrimitives(Span<byte> destination, uint value)
    {
        destination[0] = (byte)(value >> 24); destination[1] = (byte)(value >> 16);
        destination[2] = (byte)(value >> 8); destination[3] = (byte)value;
    }

    private static void WriteChunk(Stream stream, string type, byte[] data)
    {
        var length = new byte[4];
        BinaryPrimitives(length, (uint)data.Length);
        stream.Write(length);
        var payload = new byte[4 + data.Length];
        for (var i = 0; i < 4; i++) payload[i] = (byte)type[i];
        data.CopyTo(payload, 4);
        stream.Write(payload);
        var crc = new byte[4];
        BinaryPrimitives(crc, Crc32(payload));
        stream.Write(crc);
    }

    private static uint Crc32(ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (var b in data)
        {
            crc ^= b;
            for (var k = 0; k < 8; k++)
            {
                crc = (crc & 1) != 0 ? 0xEDB88320 ^ (crc >> 1) : crc >> 1;
            }
        }

        return crc ^ 0xFFFFFFFF;
    }
}
