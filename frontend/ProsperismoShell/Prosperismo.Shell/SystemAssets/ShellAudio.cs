// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using SharpEmu.GUI.SystemAssets.Audio;

namespace SharpEmu.GUI.SystemAssets;

/// <summary>
/// The system-shell audio tracks under filesystems/system_ex/vsh_asset/ in a
/// firmware dump. All four are ATRAC9 streams in plain RIFF/WAVE containers,
/// the same layout as a game's sce_sys/snd0.at9.
/// </summary>
public enum ShellAudioTrack
{
    /// <summary>sfx_coldboot.at9 — the cold-boot chime.</summary>
    BootChime,

    /// <summary>sfx_warmboot.at9 — the resume-from-rest chime.</summary>
    WarmBootChime,

    /// <summary>bgm_home.at9 — the home-screen background music.</summary>
    HomeBgm,

    /// <summary>bgm_onboarding.at9 — the first-boot onboarding music.</summary>
    OnboardingBgm,
}

/// <summary>
/// Optional playback of the PS5 system-shell audio (boot chime, home
/// background music) from a user-provided decrypted firmware dump. The dump
/// root comes from <see cref="RnpsShellAssets.LocateDumpRoot()"/> and decoding
/// reuses the vendored LibAtrac9 path through <see cref="At9Music"/>.
///
/// Playback goes through <see cref="UiSoundPlayer"/>'s mixer rather than
/// winmm's PlaySound, which allows only one sound per process: on PlaySound the
/// boot chime, the home bed and a game's snd0.at9 preview would each cut the
/// others off, and a bed that can be cut cannot be ducked. On the mixer they
/// layer, and the looping bed carries a gain that
/// <see cref="ShellAmbientMusic"/> drives. It is Windows-only and a no-op
/// elsewhere.
///
/// Everything degrades gracefully: when the dump or a track is absent the path
/// getters return null and the play hooks do nothing, silently. The audio is
/// only ever read from the user's own disk and is never redistributed with the
/// emulator. See docs/rnps-shell.md.
/// </summary>
public static class ShellAudio
{
    /// <summary>
    /// Fixed gain applied when a shell track is decoded, for the same reason
    /// <see cref="ShellUiSounds.MakeupGain"/> exists: the vsh_asset audio is
    /// mastered far below full scale and the console's own mixer makes it up
    /// downstream. Measured across the four tracks after the fold-down to
    /// stereo, the peaks sit between roughly -25 and -30 dBFS (972 to 1862 of
    /// 32767), so eight brings the loudest of them to about -7 dBFS and still
    /// leaves every one of them clear of clipping.
    /// </summary>
    public const float MakeupGain = 8.0f;

    private static readonly string[] VshAssetSegments = { "filesystems", "system_ex", "vsh_asset" };

    private static readonly object Sync = new();
    private static int _generation;

    /// <summary>True when a dump with at least one shell audio track was located.</summary>
    /// <param name="dumpRoot">Dump root override; defaults to <see cref="RnpsShellAssets.LocateDumpRoot()"/>.</param>
    public static bool IsAvailable(string? dumpRoot = null)
    {
        var root = dumpRoot ?? RnpsShellAssets.LocateDumpRoot();
        if (root is null)
        {
            return false;
        }

        foreach (var track in Enum.GetValues<ShellAudioTrack>())
        {
            if (GetTrackPath(track, root) is not null)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Absolute path to a shell audio track inside the dump, or null when the
    /// dump or the file is absent.
    /// </summary>
    /// <param name="track">Which vsh_asset track to resolve.</param>
    /// <param name="dumpRoot">Dump root override; defaults to <see cref="RnpsShellAssets.LocateDumpRoot()"/>.</param>
    public static string? GetTrackPath(ShellAudioTrack track, string? dumpRoot = null)
    {
        var root = dumpRoot ?? RnpsShellAssets.LocateDumpRoot();
        if (root is null)
        {
            return null;
        }

        var path = Path.Combine(root, Path.Combine(VshAssetSegments), TrackFileName(track));
        return File.Exists(path) ? path : null;
    }

    /// <summary>
    /// Decodes a RIFF/WAVE ATRAC9 file to a PCM16 WAV image using the same
    /// LibAtrac9 path as the snd0.at9 preview player, or null when the file
    /// is missing, unreadable, or not a valid ATRAC9 stream.
    /// </summary>
    public static byte[]? TryDecodeToWav(string? at9Path)
    {
        if (string.IsNullOrEmpty(at9Path))
        {
            return null;
        }

        try
        {
            return SndPreviewPlayer.DecodeAt9ToWav(File.ReadAllBytes(at9Path));
        }
        catch (Exception)
        {
            return null; // absent, corrupt, or unsupported: stay silent
        }
    }

    /// <summary>Plays the cold-boot chime once. Windows-only; no-op when the dump or track is absent.</summary>
    /// <param name="dumpRoot">Dump root override; defaults to <see cref="RnpsShellAssets.LocateDumpRoot()"/>.</param>
    public static void PlayBootChime(string? dumpRoot = null)
    {
        PlayTrack(ShellAudioTrack.BootChime, loop: false, dumpRoot);
    }

    /// <summary>
    /// Plays the home-screen background music. Looping it is the ambient bed,
    /// so that path goes through <see cref="ShellAmbientMusic"/> and obeys its
    /// enable and volume preferences. No-op when the dump or track is absent.
    /// </summary>
    /// <param name="loop">True to loop the track like the console home screen.</param>
    /// <param name="dumpRoot">Dump root override; defaults to <see cref="RnpsShellAssets.LocateDumpRoot()"/>.</param>
    public static void PlayHomeBgm(bool loop = true, string? dumpRoot = null)
    {
        if (loop)
        {
            ShellAmbientMusic.Start(dumpRoot);
            return;
        }

        PlayTrack(ShellAudioTrack.HomeBgm, loop: false, dumpRoot);
    }

    /// <summary>
    /// Decodes and plays a shell track on a background task, at full level. A
    /// looping track becomes the mixer's music bed and replaces whatever bed was
    /// there; a one-shot layers over everything already sounding.
    /// </summary>
    public static void PlayTrack(ShellAudioTrack track, bool loop, string? dumpRoot = null)
    {
        if (!UiSoundPlayer.IsSupported)
        {
            return;
        }

        var path = GetTrackPath(track, dumpRoot);
        if (path is null)
        {
            return;
        }

        int generation;
        lock (Sync)
        {
            generation = ++_generation;
        }

        _ = Task.Run(() =>
        {
            // bgm_home.at9 is ~8.9 MB of ATRAC9; decode off the caller's thread.
            var clip = At9Music.TryDecode(path, MakeupGain, forLooping: loop);
            if (clip is null)
            {
                return;
            }

            lock (Sync)
            {
                if (generation != _generation)
                {
                    return;
                }
            }

            if (loop)
            {
                UiSoundPlayer.SetMusic(clip, 1f);
            }
            else
            {
                UiSoundPlayer.Play(clip.Samples);
            }
        });
    }

    /// <summary>Stops whatever shell track is playing, bed included. Windows-only.</summary>
    public static void Stop()
    {
        lock (Sync)
        {
            _generation++;
        }

        ShellAmbientMusic.Stop();
        UiSoundPlayer.ClearMusic();
    }

    private static string TrackFileName(ShellAudioTrack track)
    {
        return track switch
        {
            ShellAudioTrack.BootChime => "sfx_coldboot.at9",
            ShellAudioTrack.WarmBootChime => "sfx_warmboot.at9",
            ShellAudioTrack.HomeBgm => "bgm_home.at9",
            ShellAudioTrack.OnboardingBgm => "bgm_onboarding.at9",
            _ => throw new ArgumentOutOfRangeException(nameof(track), track, null),
        };
    }
}
