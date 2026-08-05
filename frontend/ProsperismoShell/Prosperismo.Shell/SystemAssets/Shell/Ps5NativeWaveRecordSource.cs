// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System.Buffers.Binary;
using System.Collections.Concurrent;

namespace SharpEmu.GUI.SystemAssets.Shell;

/// <summary>One 0x70-byte authored Plane2 record from NPXS40087.</summary>
internal sealed class Ps5NativeWaveRecord
{
    internal const int FloatCount = 0x70 / sizeof(float);
    private readonly float[] _values;

    internal Ps5NativeWaveRecord(float[] values)
    {
        if (values.Length != FloatCount)
        {
            throw new ArgumentException($"A Plane2 record contains {FloatCount} floats.", nameof(values));
        }

        _values = values;
    }

    internal float this[int index] => _values[index];
}

/// <summary>
/// Reads Plane2's authored record table directly from the user's decrypted
/// 4.03 NPXS40087 ELF. The table remains in the firmware dump; only the 112
/// bytes for a requested record are read into the process.
/// </summary>
internal static class Ps5NativeWaveRecordSource
{
    internal const ulong TableVirtualAddress = 0x00bd0ed0;
    internal const int RecordStride = 0x70;
    internal const int RecordCount = 37;

    private const uint LoadSegment = 1;
    private static readonly ConcurrentDictionary<(string Path, int Index), Ps5NativeWaveRecord>
        Cache = new();

    internal static bool TryLoad(int recordIndex, out Ps5NativeWaveRecord? record)
    {
        record = null;
        if ((uint)recordIndex >= RecordCount || ResolveEbootPath() is not { } path)
        {
            return false;
        }

        try
        {
            record = Cache.GetOrAdd(
                (path, recordIndex),
                static key => ReadRecord(key.Path, key.Index));
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    internal static bool TryLoadFromEboot(
        string? ebootPath,
        int recordIndex,
        out Ps5NativeWaveRecord? record)
    {
        record = null;
        if (string.IsNullOrWhiteSpace(ebootPath) ||
            (uint)recordIndex >= RecordCount ||
            !File.Exists(ebootPath))
        {
            return false;
        }

        try
        {
            record = ReadRecord(Path.GetFullPath(ebootPath), recordIndex);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    internal static void Invalidate() => Cache.Clear();

    private static string? ResolveEbootPath()
    {
        try
        {
            var root = RnpsShellAssets.LocateDumpRoot();
            if (root is null)
            {
                return null;
            }

            var path = Path.Combine(
                root,
                "filesystems",
                "system_ex",
                "app",
                "NPXS40087",
                "eboot.bin");
            return File.Exists(path) ? Path.GetFullPath(path) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static Ps5NativeWaveRecord ReadRecord(string path, int recordIndex)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            FileOptions.RandomAccess);

        Span<byte> header = stackalloc byte[0x40];
        stream.ReadExactly(header);
        if (header[0] != 0x7f || header[1] != (byte)'E' ||
            header[2] != (byte)'L' || header[3] != (byte)'F' ||
            header[4] != 2 || header[5] != 1)
        {
            throw new InvalidDataException("NPXS40087 image is not little-endian ELF64.");
        }

        ulong programHeaderOffset = BinaryPrimitives.ReadUInt64LittleEndian(header[0x20..]);
        ushort programHeaderSize = BinaryPrimitives.ReadUInt16LittleEndian(header[0x36..]);
        ushort programHeaderCount = BinaryPrimitives.ReadUInt16LittleEndian(header[0x38..]);
        if (programHeaderSize < 0x38 || programHeaderCount == 0)
        {
            throw new InvalidDataException("NPXS40087 ELF has no usable program headers.");
        }

        ulong recordAddress = checked(TableVirtualAddress +
            ((ulong)recordIndex * RecordStride));
        ulong recordOffset = 0;
        bool found = false;
        var programHeader = new byte[programHeaderSize];
        for (int index = 0; index < programHeaderCount; index++)
        {
            ulong offset = checked(programHeaderOffset + ((ulong)index * programHeaderSize));
            if (offset > (ulong)stream.Length - programHeaderSize)
            {
                throw new InvalidDataException("NPXS40087 program header lies outside the ELF.");
            }

            stream.Position = checked((long)offset);
            stream.ReadExactly(programHeader);
            if (BinaryPrimitives.ReadUInt32LittleEndian(programHeader) != LoadSegment)
            {
                continue;
            }

            ulong fileOffset = BinaryPrimitives.ReadUInt64LittleEndian(programHeader.AsSpan(0x08));
            ulong virtualAddress = BinaryPrimitives.ReadUInt64LittleEndian(programHeader.AsSpan(0x10));
            ulong fileSize = BinaryPrimitives.ReadUInt64LittleEndian(programHeader.AsSpan(0x20));
            if (recordAddress < virtualAddress ||
                recordAddress - virtualAddress > fileSize ||
                fileSize - (recordAddress - virtualAddress) < RecordStride)
            {
                continue;
            }

            recordOffset = checked(fileOffset + (recordAddress - virtualAddress));
            found = true;
            break;
        }

        if (!found || recordOffset > (ulong)stream.Length - RecordStride)
        {
            throw new InvalidDataException("Plane2 record table is not mapped by the ELF.");
        }

        Span<byte> bytes = stackalloc byte[RecordStride];
        stream.Position = checked((long)recordOffset);
        stream.ReadExactly(bytes);
        var values = new float[Ps5NativeWaveRecord.FloatCount];
        for (int index = 0; index < values.Length; index++)
        {
            int bits = BinaryPrimitives.ReadInt32LittleEndian(bytes[(index * sizeof(float))..]);
            values[index] = BitConverter.Int32BitsToSingle(bits);
            if (!float.IsFinite(values[index]))
            {
                throw new InvalidDataException("Plane2 record contains a non-finite value.");
            }
        }

        return new Ps5NativeWaveRecord(values);
    }
}
