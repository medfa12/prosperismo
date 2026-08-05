// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System.Text.Json;

namespace SharpEmu.GUI.Ps5Home;

/// <summary>
/// Resolves the home backdrop a title carries in its own <c>sce_sys</c>.
///
/// <para><b>Why this exists.</b> <c>bg_hub_default.dds</c> is not the PS5 home
/// background — the name says what it is, a default for titles that have no
/// artwork of their own. The home background is the <em>focused title's</em>
/// artwork, which is why it changes as the highlight travels the tile row, why
/// every system app ships its own <c>bg_NPXS400*.dds</c>, and why the shell
/// almost never opens <c>bg_hub_default</c> against a real library.</para>
///
/// <para><b>Which pic is the backdrop — measured, not assumed.</b> The obvious
/// guess is <c>pic0</c>. The files say otherwise:</para>
/// <list type="bullet">
///   <item><description>Every system app on the 4.03 dump that ships artwork at
///   all ships <c>pic1</c> and <b>no <c>pic0</c> exists anywhere among
///   them</b> — NPXS40074 and NPXS40099 carry <c>pic1.png</c>, NPXS40106
///   <c>pic1.dds</c>, NPXS40140 <c>pic1.DDS</c>. A backdrop slot that the
///   console's own apps never populate is not the backdrop slot.</description></item>
///   <item><description>Game discs ship all three, and for ASTRO BOT
///   <c>pic0.dds</c> and <c>pic1.dds</c> are byte-identical (SHA-256
///   <c>73d7349f…</c>), i.e. <c>pic0</c> is a duplicate of <c>pic1</c>, not an
///   independent image. Superliminal's three differ.</description></item>
///   <item><description>psdevwiki's file-structure table calls <c>Pic1.png</c>
///   the "Startup image file (background image)".</description></item>
/// </list>
///
/// <para>So the order below is <c>pic1</c> first. <c>pic0</c> and <c>pic2</c>
/// follow as fallbacks because games do ship them and a title with an unusual
/// layout should still get its own art rather than the hub default.</para>
///
/// <para><b>Format.</b> All observed <c>.dds</c> backdrops are 3840x2160 with a
/// single mip. Games use DDS DX10 / <c>dxgiFormat 98</c> = BC7_UNORM, which is
/// the same format as <c>bg_hub_default</c>, so the existing BC7 decoder reads
/// them with no new work. One system app (NPXS40106) uses DXT1 instead, and
/// two ship PNG, so the loader must not assume BC7.</para>
/// </summary>
public static class Ps5TitleArtwork
{
    /// <summary>
    /// Home-backdrop file names in probe order. See the type remarks for the
    /// evidence that <c>pic1</c> and not <c>pic0</c> leads this list.
    /// </summary>
    public static readonly IReadOnlyList<string> BackdropCandidates = new[]
    {
        "pic1.dds",
        "pic1.png",
        "pic0.dds",
        "pic0.png",
        "pic2.dds",
        "pic2.png",
    };

    /// <summary>
    /// The one backdrop-related key that <c>param.json</c> actually carries:
    /// <c>backgroundBasematType</c>. See <see cref="TryReadBasematType"/>.
    /// </summary>
    public const string BasematTypeKey = "backgroundBasematType";

    /// <summary>
    /// Resolves the home backdrop inside a title's <c>sce_sys</c> directory, or
    /// null when the title ships none. Probing is case-insensitive by way of
    /// enumerating the directory once: the dump really does contain
    /// <c>pic1.DDS</c> in upper case (NPXS40140), which a plain
    /// <see cref="File.Exists"/> would still find on Windows but would miss on
    /// a case-sensitive filesystem.
    /// </summary>
    /// <param name="sceSysDirectory">A title's <c>sce_sys</c> folder.</param>
    public static string? ResolveBackdrop(string? sceSysDirectory)
    {
        if (string.IsNullOrWhiteSpace(sceSysDirectory) || !Directory.Exists(sceSysDirectory))
        {
            return null;
        }

        Dictionary<string, string> present;
        try
        {
            present = Directory
                .EnumerateFiles(sceSysDirectory)
                .GroupBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            // An unreadable or vanished directory is a title without artwork,
            // not a crash.
            return null;
        }

        foreach (var candidate in BackdropCandidates)
        {
            if (present.TryGetValue(candidate, out var path))
            {
                return path;
            }
        }

        return null;
    }

    /// <summary>
    /// Resolves the home backdrop for a title given the path to its executable,
    /// which is what the library scan records. Looks in <c>sce_sys</c> beside
    /// the executable, the same place the title's <c>snd0.at9</c> preview and
    /// <c>param.json</c> live.
    /// </summary>
    /// <param name="executablePath">Path to the title's eboot.</param>
    public static string? ResolveBackdropForExecutable(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return null;
        }

        string? directory;
        try
        {
            directory = Path.GetDirectoryName(executablePath);
        }
        catch (Exception)
        {
            return null;
        }

        return directory is null
            ? null
            : ResolveBackdrop(Path.Combine(directory, "sce_sys"));
    }

    /// <summary>
    /// Reads <c>backgroundBasematType</c> out of a <c>param.json</c> payload, or
    /// null when the key is absent or the JSON will not parse.
    ///
    /// <para><b>What the survey found.</b> This is the <em>only</em> key in any
    /// <c>param.json</c> on the dump that says anything about the backdrop.
    /// Across 41 system <c>param.json</c> files exactly three carry it —
    /// Explore (NPXS40063) with <c>EllipseNarrow</c>, Game Library (NPXS40071)
    /// and App Library (NPXS40139) with <c>Linear</c> — and no shipped game
    /// carries it at all. There is no background colour, no blur variant and no
    /// "suppress the background" flag in title metadata.</para>
    ///
    /// <para><b>What it controls, and what it does not.</b> It names the
    /// <em>basemat</em> — the gradient laid over the backdrop — and not the
    /// image. <c>Sce.Vsh.ShellUI.BGLayer</c> keeps the two on separate calls:
    /// <c>SetBackgroundBasemat(BasematType, Color, Duration)</c> against
    /// <c>SetBackgroundTransition(… NextImageUri, NextBlurImageUri,
    /// NextFallbackImageUri, OverlayImageUri, BasematType …)</c>. The three
    /// enum values seen so far are <c>Linear</c>, <c>EllipseNarrow</c> and
    /// <c>EllipseWide</c>; the full member list has not been recovered, so this
    /// returns the raw string and does not pretend to be an enum.</para>
    ///
    /// <para><b>Not yet honoured by the renderer.</b> The plate draws no
    /// basemat at all today — the only mat in the shell is the tile row's
    /// per-tile darkening, which is a different thing that happens to share the
    /// word. Wiring the Linear and Ellipse variants needs their geometry, which
    /// is unrecovered, and inventing an ellipse would be worse than leaving
    /// this parsed, documented and unused.</para>
    /// </summary>
    /// <param name="paramJson">Raw <c>param.json</c> bytes.</param>
    public static string? TryReadBasematType(ReadOnlySpan<byte> paramJson)
    {
        if (paramJson.IsEmpty)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(paramJson.ToArray());
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (!document.RootElement.TryGetProperty(BasematTypeKey, out var value) ||
                value.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var text = value.GetString();
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
