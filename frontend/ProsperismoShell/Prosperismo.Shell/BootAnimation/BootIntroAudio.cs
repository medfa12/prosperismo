// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System.Buffers.Binary;
using SharpEmu.GUI.SystemAssets.Audio;

namespace SharpEmu.GUI.BootAnimation;

/// <summary>
/// The boot sequence's sound: a vsh_asset ATRAC9 cue decoded to stereo PCM and
/// handed to <see cref="UiSoundPlayer"/>.
///
/// The mixer is used rather than winmm's PlaySound (which the title-music
/// preview uses) for two reasons the intro needs: it stops on the next 10 ms
/// buffer, so skipping silences the sound instead of letting it ring on over the
/// shell, and it takes raw PCM, so the clip can be trimmed to the movie and
/// given a fade rather than being cut off mid-note.
///
/// The cue is 5.1 at 48 kHz. It is folded to stereo here because the mixer is
/// stereo and its own conversion would simply drop the centre channel, which on
/// this cue carries most of the sound.
///
/// Windows-only, like the rest of the audio path, and a silent no-op everywhere
/// else. Nothing here throws: a missing, corrupt or unsupported file just means
/// the movie plays without sound.
/// </summary>
public static class BootIntroAudio
{
    /// <summary>Length of the ramp applied to the tail of the trimmed clip.</summary>
    public static readonly TimeSpan FadeOut = TimeSpan.FromSeconds(0.5);

    // Fold-down weights for a WAVE-order 5.1 source (L R C LFE Ls Rs). The
    // centre and surrounds come in at -3 dB, the usual downmix; LFE is dropped
    // because the mixer has no crossover and it only muddies the fold. The trim
    // keeps the sum inside PCM16 without needing a limiter.
    private const double CentreWeight = 0.7071;
    private const double SurroundWeight = 0.7071;
    private const double DownmixTrim = 0.72;

    private const int WavHeaderBytes = 44;

    /// <summary>
    /// Decodes, folds, trims and fades a cue, ready for <see cref="Play"/>.
    /// Returns null when the path is null, the file is missing, or it is not a
    /// decodable ATRAC9 stream. Costs a few hundred milliseconds, so callers run
    /// it on a background thread.
    /// </summary>
    /// <param name="at9Path">The cue to decode, or null.</param>
    /// <param name="duration">Trim length; the movie's duration. Zero keeps the whole cue.</param>
    public static short[]? Prepare(string? at9Path, TimeSpan duration)
    {
        if (string.IsNullOrEmpty(at9Path))
        {
            return null;
        }

        byte[] wav;
        try
        {
            wav = SndPreviewPlayer.DecodeAt9ToWav(File.ReadAllBytes(at9Path));
        }
        catch (Exception)
        {
            return null; // absent, corrupt, or not ATRAC9: play the movie silently
        }

        var mixed = ToMixFormat(wav);
        if (mixed is null)
        {
            return null;
        }

        return TrimAndFade(mixed, duration, FadeOut);
    }

    /// <summary>
    /// Reads the PCM16 WAV image the ATRAC9 decoder produces and returns it in
    /// the mixer's format: interleaved stereo at
    /// <see cref="UiSoundPlayer.MixSampleRate"/>. Returns null for anything that
    /// is not the fixed 44-byte header that decoder writes.
    /// </summary>
    internal static short[]? ToMixFormat(byte[]? wav)
    {
        if (wav is null || wav.Length <= WavHeaderBytes)
        {
            return null;
        }

        int channels = BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(22));
        int sampleRate = BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(24));
        if (channels < 1 || sampleRate < 1)
        {
            return null;
        }

        int frames = (wav.Length - WavHeaderBytes) / (channels * sizeof(short));
        if (frames < 1)
        {
            return null;
        }

        var source = new short[(long)frames * channels];
        for (int i = 0; i < source.Length; i++)
        {
            source[i] = BinaryPrimitives.ReadInt16LittleEndian(
                wav.AsSpan(WavHeaderBytes + (i * sizeof(short))));
        }

        // Fold multichannel down here; the mixer's own converter would keep only
        // the first two channels. Mono and stereo go straight through it.
        var stereo = channels > 2 ? FoldToStereo(source, channels) : source;
        int stereoChannels = channels > 2 ? 2 : channels;
        var prepared = UiSoundPlayer.ToMixFormat(stereo, stereoChannels, sampleRate);
        return prepared.Length == 0 ? null : prepared;
    }

    /// <summary>
    /// Folds interleaved PCM16 with more than two channels down to stereo,
    /// assuming WAVE channel order. Channels past the first six are ignored.
    /// </summary>
    /// <param name="samples">Interleaved source PCM16.</param>
    /// <param name="channels">Source channel count, greater than two.</param>
    internal static short[] FoldToStereo(short[] samples, int channels)
    {
        if (samples.Length == 0 || channels <= 2)
        {
            return samples;
        }

        int frames = samples.Length / channels;
        var folded = new short[(long)frames * 2];

        for (int frame = 0; frame < frames; frame++)
        {
            int baseIndex = frame * channels;
            double left = samples[baseIndex];
            double right = samples[baseIndex + 1];

            if (channels >= 3)
            {
                var centre = samples[baseIndex + 2] * CentreWeight;
                left += centre;
                right += centre;
            }

            // Index 3 is LFE and is intentionally skipped.
            if (channels >= 5)
            {
                left += samples[baseIndex + 4] * SurroundWeight;
            }

            if (channels >= 6)
            {
                right += samples[baseIndex + 5] * SurroundWeight;
            }

            folded[frame * 2] = Clamp(left * DownmixTrim);
            folded[(frame * 2) + 1] = Clamp(right * DownmixTrim);
        }

        return folded;
    }

    /// <summary>
    /// Cuts interleaved stereo PCM to <paramref name="duration"/> and ramps the
    /// last <paramref name="fade"/> down to silence, so a cue longer than the
    /// movie ends with it instead of being chopped. A zero or negative duration,
    /// or one past the end of the clip, leaves the length alone and still fades.
    /// </summary>
    internal static short[] TrimAndFade(short[] samples, TimeSpan duration, TimeSpan fade)
    {
        if (samples.Length < UiSoundPlayer.MixChannels)
        {
            return samples;
        }

        int frames = samples.Length / UiSoundPlayer.MixChannels;
        int keep = frames;
        if (duration > TimeSpan.Zero)
        {
            var wanted = (long)Math.Round(duration.TotalSeconds * UiSoundPlayer.MixSampleRate);
            keep = (int)Math.Clamp(wanted, 1, frames);
        }

        var result = keep == frames
            ? samples
            : samples[..(keep * UiSoundPlayer.MixChannels)];

        int fadeFrames = fade > TimeSpan.Zero
            ? (int)Math.Min(keep, Math.Round(fade.TotalSeconds * UiSoundPlayer.MixSampleRate))
            : 0;
        if (fadeFrames <= 1)
        {
            return result;
        }

        int start = keep - fadeFrames;
        for (int frame = start; frame < keep; frame++)
        {
            double gain = (double)(keep - 1 - frame) / (fadeFrames - 1);
            for (int channel = 0; channel < UiSoundPlayer.MixChannels; channel++)
            {
                int index = (frame * UiSoundPlayer.MixChannels) + channel;
                result[index] = Clamp(result[index] * gain);
            }
        }

        return result;
    }

    /// <summary>
    /// Starts a prepared clip. Returns immediately; null or an unsupported
    /// platform does nothing.
    /// </summary>
    public static void Play(short[]? samples) => UiSoundPlayer.Play(samples);

    /// <summary>
    /// Silences the intro. This clears every mixer voice, which is what the intro
    /// wants: it owns the window while it runs, so nothing else is sounding.
    /// </summary>
    public static void Stop() => UiSoundPlayer.StopAll();

    private static short Clamp(double value) =>
        (short)Math.Clamp(Math.Round(value), short.MinValue, short.MaxValue);
}
