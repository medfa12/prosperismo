// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using Avalonia.Media;
using SharpEmu.GUI.SystemAssets;

namespace SharpEmu.GUI.Ps5Home;

/// <summary>Which SST face a piece of shell text asks for.</summary>
public enum Ps5FontFace
{
    /// <summary><c>UIFontWeight.Light</c>. The shell default; <c>new UIFont(size)</c> lands here.</summary>
    Light,

    /// <summary><c>SST-Roman</c>. Not the RN default — see the type on this class.</summary>
    Roman,

    /// <summary><c>SST-Medium</c>.</summary>
    Medium,

    /// <summary><c>UIFontWeight.Bold</c>; what <c>fontWeight: "bold"</c> selects.</summary>
    Bold,

    /// <summary><c>SST-LightItalic</c>.</summary>
    LightItalic,

    /// <summary><c>SST-Italic</c>.</summary>
    Italic,

    /// <summary><c>SST-MediumItalic</c>.</summary>
    MediumItalic,

    /// <summary><c>SST-BoldItalic</c>.</summary>
    BoldItalic,
}

/// <summary>
/// Sony's type, loaded from the user's dump at run time.
///
/// <para><b>Asset policy.</b> Not one byte of this is redistributable, so
/// nothing is embedded, copied or cached into the repository. The faces are
/// read in place from an absolute path under the dump, the path is overridable,
/// and when the dump is absent the shell says so visibly rather than quietly
/// substituting a lookalike. See <see cref="IsAvailable"/> and
/// <see cref="MissingDumpNotice"/>.</para>
///
/// <para><b>Which face is the default.</b> SST-Light, not SST-Roman.
/// <c>new UIFont(int size)</c> chains to
/// <c>this(null, GetDefaultFont(), size, UIFontStyle.Normal, UIFontWeight.Light)</c>
/// and <c>UIFontWeight</c> is exactly <c>{ Light, Bold }</c>
/// (<c>docs/ps5-shell-recovery-audit.md</c> §2.1). Getting this wrong changes
/// the overall "colour" of every page even when the sizes are right, which is
/// why it is stated here and not left to a caller's default argument.</para>
/// </summary>
public static class Ps5FontLibrary
{
    /// <summary>
    /// Environment variable that overrides where the faces are read from. Point
    /// it at a directory holding <c>SST-Light.otf</c> and friends.
    /// </summary>
    public const string DirectoryOverrideVariable = "SHARPEMU_PS5_FONT_DIR";

    /// <summary>
    /// The font directory inside a 4.03 dump, relative to the dump root.
    /// All 43 of Sony's faces live here — the eight Latin SST weights plus
    /// SSTJpPro, SSTArabic, SSTThai, SSTVietnamese, SSTTypewriter,
    /// YoonGothicProSIE and DFHEI5-SONY.
    /// </summary>
    public const string RelativeFontDirectory = @"filesystems\preinst\common\font";

    /// <summary>
    /// What to render instead of Sony's type when the dump is not present.
    /// Deliberately a notice and not a lookalike face: a shell that silently
    /// falls back to a metric-similar sans is a shell nobody can audit.
    /// </summary>
    public const string MissingDumpNotice =
        "SST unavailable: no PS5 dump found. Set " + DirectoryOverrideVariable + " to the firmware font directory.";

    private static readonly object Gate = new();
    private static readonly Dictionary<Ps5FontFace, FontFamily> Loaded = [];
    private static string? _resolvedDirectory;
    private static bool _probed;

    /// <summary>File name of each face, as shipped in <c>preinst/common/font</c>.</summary>
    /// <param name="face">Face to name.</param>
    public static string FileNameOf(Ps5FontFace face) => face switch
    {
        Ps5FontFace.Light => "SST-Light.otf",
        Ps5FontFace.Roman => "SST-Roman.otf",
        Ps5FontFace.Medium => "SST-Medium.otf",
        Ps5FontFace.Bold => "SST-Bold.otf",
        Ps5FontFace.LightItalic => "SST-LightItalic.otf",
        Ps5FontFace.Italic => "SST-Italic.otf",
        Ps5FontFace.MediumItalic => "SST-MediumItalic.otf",
        Ps5FontFace.BoldItalic => "SST-BoldItalic.otf",
        _ => throw new ArgumentOutOfRangeException(nameof(face)),
    };

    /// <summary>
    /// The face <c>fontWeight</c> selects. <c>UIFontWeight</c> has two members,
    /// so anything that is not "bold" is Light — there is no Roman rung on the
    /// RN path.
    /// </summary>
    /// <param name="fontWeight">A React Native <c>fontWeight</c> value, or null.</param>
    public static Ps5FontFace FaceForWeight(string? fontWeight) =>
        string.Equals(fontWeight, "bold", StringComparison.OrdinalIgnoreCase)
            ? Ps5FontFace.Bold
            : Ps5FontFace.Light;

    /// <summary>True once a directory containing at least SST-Light has been found.</summary>
    public static bool IsAvailable => ResolveDirectory() is not null;

    /// <summary>The directory the faces are being read from, or null when none was found.</summary>
    public static string? FontDirectory => ResolveDirectory();

    /// <summary>
    /// Forgets the resolved directory and every loaded face, so the next call
    /// probes again. For tests and for a settings change that moves the dump.
    /// </summary>
    public static void Invalidate()
    {
        lock (Gate)
        {
            _probed = false;
            _resolvedDirectory = null;
            Loaded.Clear();
        }
    }

    /// <summary>
    /// Resolves a face to a font family backed by the real file, or null when
    /// the dump is absent. Callers must handle null by showing
    /// <see cref="MissingDumpNotice"/>, not by picking another font.
    /// </summary>
    /// <param name="face">Face to load; <see cref="Ps5FontFace.Light"/> is the shell default.</param>
    public static FontFamily? TryGet(Ps5FontFace face = Ps5FontFace.Light)
    {
        lock (Gate)
        {
            if (Loaded.TryGetValue(face, out var cached))
            {
                return cached;
            }

            var directory = ResolveDirectoryLocked();
            if (directory is null)
            {
                return null;
            }

            var file = Path.Combine(directory, FileNameOf(face));
            if (!File.Exists(file))
            {
                return null;
            }

            // file:// + #family. Avalonia reads the face off disk on demand, so
            // the dump is never copied and never written to.
            var uri = new Uri(file).AbsoluteUri;
            var family = new FontFamily(new Uri(uri, UriKind.Absolute), FamilyNameOf(face));
            Loaded[face] = family;
            return family;
        }
    }

    /// <summary>
    /// The internal family name to select inside the OTF. SST ships each weight
    /// as its own family, so the file's own name is the right key.
    /// </summary>
    /// <param name="face">Face to name.</param>
    public static string FamilyNameOf(Ps5FontFace face) => face switch
    {
        Ps5FontFace.Light => "SST Light",
        Ps5FontFace.Roman => "SST",
        Ps5FontFace.Medium => "SST Medium",
        Ps5FontFace.Bold => "SST Bold",
        Ps5FontFace.LightItalic => "SST Light",
        Ps5FontFace.Italic => "SST",
        Ps5FontFace.MediumItalic => "SST Medium",
        Ps5FontFace.BoldItalic => "SST Bold",
        _ => "SST",
    };

    /// <summary>
    /// Probes the candidate directories in order and returns the first holding
    /// <c>SST-Light.otf</c>: the explicit override first, then the located dump.
    /// </summary>
    public static string? ResolveDirectory()
    {
        lock (Gate)
        {
            return ResolveDirectoryLocked();
        }
    }

    /// <summary>
    /// Candidate directories, in probe order. Exposed so a diagnostic can print
    /// exactly where the shell looked when it reports the dump missing.
    /// </summary>
    /// <param name="dumpRoot">Dump root, or null to use the located one.</param>
    public static IEnumerable<string> CandidateDirectories(string? dumpRoot = null)
    {
        var overridden = Environment.GetEnvironmentVariable(DirectoryOverrideVariable);
        if (!string.IsNullOrWhiteSpace(overridden))
        {
            yield return overridden.Trim();
        }

        var root = dumpRoot ?? RnpsShellAssets.LocateDumpRoot();
        if (!string.IsNullOrWhiteSpace(root))
        {
            yield return Path.Combine(root, RelativeFontDirectory);
        }
    }

    private static string? ResolveDirectoryLocked()
    {
        if (_probed)
        {
            return _resolvedDirectory;
        }

        _probed = true;
        foreach (var candidate in CandidateDirectories())
        {
            try
            {
                if (File.Exists(Path.Combine(candidate, FileNameOf(Ps5FontFace.Light))))
                {
                    _resolvedDirectory = candidate;
                    return _resolvedDirectory;
                }
            }
            catch (Exception)
            {
                // An unreadable or malformed candidate is simply not a match;
                // typography must never be able to take the shell down.
            }
        }

        _resolvedDirectory = null;
        return null;
    }
}
