// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using Avalonia.Media.Imaging;
using SharpEmu.GUI.SystemAssets;
using SharpEmu.GUI.SystemAssets.Rco;
using SharpEmu.GUI.SystemAssets.Textures;

namespace SharpEmu.GUI.Ps5Home;

/// <summary>
/// Read-only view of the ShellUI resource containers in a user's firmware
/// dump. Proprietary payloads are decoded in memory and are never extracted,
/// cached on disk, or copied into the repository.
/// </summary>
public sealed class Ps5ShellResourcePack
{
    public const string ResourceDirectoryOverrideVariable = "SHARPEMU_PS5_SHELL_RESOURCE_DIR";

    public const string RelativeResourceDirectory =
        @"filesystems\system_ex\app\NPXS40087\psm\Application\resource";

    public const string BaseFileName = "Sce.Vsh.ShellUI.Base.rco";
    public const string BgLayerFileName = "Sce.Vsh.ShellUI.BGLayer.rco";

    private readonly RcoContainer _base;
    private readonly RcoContainer _bgLayer;

    private Ps5ShellResourcePack(
        string directoryPath,
        string basePath,
        string bgLayerPath,
        RcoContainer baseContainer,
        RcoContainer bgLayerContainer)
    {
        DirectoryPath = directoryPath;
        BasePath = basePath;
        BgLayerPath = bgLayerPath;
        _base = baseContainer;
        _bgLayer = bgLayerContainer;
    }

    public string DirectoryPath { get; }

    public string BasePath { get; }

    public string BgLayerPath { get; }

    public int BaseEntryCount => _base.Entries.Count;

    public int BgLayerEntryCount => _bgLayer.Entries.Count;

    /// <summary>Resolves the external resource directory without opening it.</summary>
    public static string? ResolveDirectory(string? dumpRoot = null)
    {
        var overridden = Environment.GetEnvironmentVariable(ResourceDirectoryOverrideVariable);
        if (!string.IsNullOrWhiteSpace(overridden))
        {
            try
            {
                var full = Path.GetFullPath(overridden.Trim());
                if (Directory.Exists(full))
                {
                    return full;
                }
            }
            catch (Exception exception) when (exception is IOException or ArgumentException or NotSupportedException)
            {
                // Fall through to normal dump discovery.
            }
        }

        var root = dumpRoot ?? RnpsShellAssets.LocateDumpRoot();
        if (string.IsNullOrWhiteSpace(root))
        {
            return null;
        }

        try
        {
            var candidate = Path.GetFullPath(Path.Combine(root, RelativeResourceDirectory));
            return Directory.Exists(candidate) ? candidate : null;
        }
        catch (Exception exception) when (exception is IOException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>
    /// Opens Base and BGLayer together. Requiring both avoids labelling a
    /// partial resource directory as a complete firmware resource pack.
    /// </summary>
    public static Ps5ShellResourcePack? TryOpen(string? dumpRoot = null)
    {
        var directory = ResolveDirectory(dumpRoot);
        if (directory is null)
        {
            return null;
        }

        var basePath = Path.Combine(directory, BaseFileName);
        var bgLayerPath = Path.Combine(directory, BgLayerFileName);
        if (!File.Exists(basePath) || !File.Exists(bgLayerPath))
        {
            return null;
        }

        try
        {
            return new Ps5ShellResourcePack(
                directory,
                basePath,
                bgLayerPath,
                RcoContainer.Open(basePath),
                RcoContainer.Open(bgLayerPath));
        }
        catch (RcoFormatException)
        {
            return null;
        }
    }

    /// <summary>Decodes a PNG or BC7 DDS image from Base.rco in memory.</summary>
    public Bitmap? TryLoadBaseBitmap(string name, bool prefer4k = false) =>
        TryLoadBitmap(_base, name, prefer4k);

    /// <summary>
    /// Decodes a PNG or BC7 DDS image from BGLayer.rco in memory. In 4.03 this
    /// container is VR/gaze furniture, not the normal Home background.
    /// </summary>
    public Bitmap? TryLoadBgLayerBitmap(string name, bool prefer4k = false) =>
        TryLoadBitmap(_bgLayer, name, prefer4k);

    private static Bitmap? TryLoadBitmap(RcoContainer container, string name, bool prefer4k)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var preferredSource = prefer4k ? "src_4k" : "src";
        var entry = container.Entries.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, name, StringComparison.Ordinal) &&
            string.Equals(candidate.SrcLabel, preferredSource, StringComparison.Ordinal));
        entry ??= container.Entries.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, name, StringComparison.Ordinal));
        if (entry is null)
        {
            return null;
        }

        try
        {
            var payload = container.ReadEntryData(entry);
            if (payload.Length < 4)
            {
                return null;
            }

            if (payload[0] == 0x89 && payload[1] == 0x50 && payload[2] == 0x4e && payload[3] == 0x47)
            {
                return new Bitmap(new MemoryStream(payload, writable: false));
            }

            if (payload[0] == (byte)'D' && payload[1] == (byte)'D' &&
                payload[2] == (byte)'S' && payload[3] == (byte)' ')
            {
                var image = DdsImage.Load(payload);
                return image.IsSupported ? image.ToBitmap() : null;
            }
        }
        catch (Exception exception) when (
            exception is IOException or ArgumentException or NotSupportedException)
        {
            return null;
        }

        return null;
    }
}
