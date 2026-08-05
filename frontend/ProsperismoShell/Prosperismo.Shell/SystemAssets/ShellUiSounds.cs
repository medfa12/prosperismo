// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using SharpEmu.GUI.SystemAssets.Audio;
using SharpEmu.GUI.SystemAssets.Rco;

namespace SharpEmu.GUI.SystemAssets;

/// <summary>
/// The system-shell interaction cues, named after the events the shell's own
/// compiled soundscript binds them to (see docs/ps5-shell-motion.md). Each value
/// maps to one <c>snd_*</c> entry in the shell's UI resource container; the
/// mapping is <see cref="ShellUiSounds.EntryNames"/>.
/// </summary>
public enum UiSoundEvent
{
    /// <summary>Focus moved to another item in a list, grid or tile row.</summary>
    FocusMove,

    /// <summary>An item was confirmed / activated.</summary>
    Enter,

    /// <summary>A screen or dialog was backed out of.</summary>
    Cancel,

    /// <summary>An options / context menu opened.</summary>
    OpenOptionMenu,

    /// <summary>An options / context menu closed.</summary>
    CloseOptionMenu,

    /// <summary>A rejected input.</summary>
    Error,

    /// <summary>A toggle switched on.</summary>
    SwitchOn,

    /// <summary>A toggle switched off.</summary>
    SwitchOff,

    /// <summary>Focus crossed into a different panel.</summary>
    ChangePanel,

    /// <summary>The shell moved between spaces / hubs.</summary>
    ChangeSpace,

    /// <summary>A modal dialog opened.</summary>
    OpenDialog,

    /// <summary>An error dialog opened.</summary>
    OpenErrorDialog,

    /// <summary>The affirmative button in a dialog was chosen.</summary>
    YesInDialog,

    /// <summary>The negative button in a dialog was chosen.</summary>
    NoInDialog,

    /// <summary>The home screen opened.</summary>
    OpenHome,

    /// <summary>The control centre opened.</summary>
    OpenControlCenter,

    /// <summary>The control centre closed.</summary>
    CloseControlCenter,

    /// <summary>A character was typed.</summary>
    TextInput,

    /// <summary>A character was deleted.</summary>
    Backspace,

    /// <summary>The on-screen keyboard opened.</summary>
    OpenOnScreenKeyboard,

    /// <summary>A slider crossed a level-meter step.</summary>
    SliderLevelMeter,

    /// <summary>A screenshot was captured.</summary>
    TakeScreenshot,

    /// <summary>A trophy notification appeared.</summary>
    TrophyToast,
}

/// <summary>
/// Plays the PS5 system shell's own UI interaction cues, read at runtime from a
/// user-provided decrypted firmware dump.
///
/// The cues live inside <c>filesystems/system_ex/vsh_asset/Sce.PlayStation.PUI_UI3.rco</c>
/// as VAG streams named after the soundscript events that trigger them
/// (<c>snd_focus_move</c>, <c>snd_enter</c>, ...). This class locates the
/// container through <see cref="RnpsShellAssets.LocateDumpRoot()"/>, extracts the
/// mapped entries with <see cref="RcoContainer"/>, decodes them with
/// <see cref="VagDecoder"/>, caches the PCM and hands it to
/// <see cref="UiSoundPlayer"/>, which lets blips overlap each other and the
/// background music.
///
/// Everything is optional and silent when absent: with no dump,
/// <see cref="Play"/> does nothing. The first <see cref="Play"/> kicks off the
/// load on a background thread and returns immediately, so the cue that started
/// the load is itself dropped; every later one sounds. Nothing here throws and
/// nothing blocks the UI thread. The audio is only ever read from the user's own
/// disk and is never redistributed with the emulator.
/// </summary>
public static class ShellUiSounds
{
    /// <summary>The shell UI resource container that holds the interaction cues.</summary>
    public const string ContainerFileName = "Sce.PlayStation.PUI_UI3.rco";

    /// <summary>
    /// Fixed gain applied on top of <see cref="Volume"/>. The cues are mastered
    /// very quietly inside the container (the loudest peaks around -15 dBFS);
    /// the shell's own mixer makes that up downstream, so the player does too
    /// rather than normalising each cue, which would flatten their intended
    /// relative loudness.
    /// </summary>
    public const float MakeupGain = 4.0f;

    private static readonly string[] VshAssetSegments = { "filesystems", "system_ex", "vsh_asset" };

    private static readonly IReadOnlyDictionary<UiSoundEvent, string> Names =
        new Dictionary<UiSoundEvent, string>
        {
            [UiSoundEvent.FocusMove] = "snd_focus_move",
            [UiSoundEvent.Enter] = "snd_enter",
            [UiSoundEvent.Cancel] = "snd_cancel",
            [UiSoundEvent.OpenOptionMenu] = "snd_open_option_menu",
            [UiSoundEvent.CloseOptionMenu] = "snd_close_option_menu",
            [UiSoundEvent.Error] = "snd_error",
            [UiSoundEvent.SwitchOn] = "snd_switch_on",
            [UiSoundEvent.SwitchOff] = "snd_switch_off",
            [UiSoundEvent.ChangePanel] = "snd_change_panel",
            [UiSoundEvent.ChangeSpace] = "snd_change_space",
            [UiSoundEvent.OpenDialog] = "snd_open_dialog",
            [UiSoundEvent.OpenErrorDialog] = "snd_open_error_dialog",
            [UiSoundEvent.YesInDialog] = "snd_yes_in_dialog",
            [UiSoundEvent.NoInDialog] = "snd_no_in_dialog",
            [UiSoundEvent.OpenHome] = "snd_open_home",
            [UiSoundEvent.OpenControlCenter] = "snd_open_control_center",
            [UiSoundEvent.CloseControlCenter] = "snd_close_control_center",
            [UiSoundEvent.TextInput] = "snd_text_input",
            [UiSoundEvent.Backspace] = "snd_backspace",
            [UiSoundEvent.OpenOnScreenKeyboard] = "snd_open_osk",
            [UiSoundEvent.SliderLevelMeter] = "snd_slider_level_meter",
            [UiSoundEvent.TakeScreenshot] = "snd_take_screenshot",
            [UiSoundEvent.TrophyToast] = "snd_trophy_toast",
        };

    private static readonly object Gate = new();
    private static IReadOnlyDictionary<UiSoundEvent, UiSoundClip>? _clips;
    private static bool _loadStarted;
    private static double _volume = 1.0;

    /// <summary>
    /// Global mute for the shell UI cues; a settings toggle can bind straight to
    /// this. Default true. Turning it off does not discard the cache, so turning
    /// it back on is instant.
    /// </summary>
    public static bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Playback volume, 0 (silent) to 2 (double). Applied on top of
    /// <see cref="MakeupGain"/>. Values outside the range are clamped.
    /// </summary>
    public static double Volume
    {
        get => _volume;
        set => _volume = double.IsFinite(value) ? Math.Clamp(value, 0.0, 2.0) : 1.0;
    }

    /// <summary>The event to container-entry-name mapping, keyed by event.</summary>
    public static IReadOnlyDictionary<UiSoundEvent, string> EntryNames => Names;

    /// <summary>True once the cues have been extracted and decoded (or found to be unavailable).</summary>
    public static bool IsLoaded => Volatile.Read(ref _clips) is not null;

    /// <summary>Number of cues actually decoded; zero when no dump is present.</summary>
    public static int LoadedCount => Volatile.Read(ref _clips)?.Count ?? 0;

    /// <summary>True once a load has been kicked off and not since <see cref="Reset"/>.</summary>
    internal static bool LoadStarted
    {
        get
        {
            lock (Gate)
            {
                return _loadStarted;
            }
        }
    }

    /// <summary>True when a dump containing the shell UI resource container was located.</summary>
    /// <param name="dumpRoot">Dump root override; defaults to <see cref="RnpsShellAssets.LocateDumpRoot()"/>.</param>
    public static bool IsAvailable(string? dumpRoot = null) => LocateContainer(dumpRoot) is not null;

    /// <summary>
    /// Absolute path to the shell UI resource container inside the dump, or null
    /// when the dump or the file is absent.
    /// </summary>
    /// <param name="dumpRoot">Dump root override; defaults to <see cref="RnpsShellAssets.LocateDumpRoot()"/>.</param>
    public static string? LocateContainer(string? dumpRoot = null)
    {
        try
        {
            var root = dumpRoot ?? RnpsShellAssets.LocateDumpRoot();
            if (root is null)
            {
                return null;
            }

            var path = Path.Combine(root, Path.Combine(VshAssetSegments), ContainerFileName);
            return File.Exists(path) ? path : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Starts extracting and decoding the cues on a background thread if that
    /// has not happened yet. Returns immediately and is safe to call repeatedly;
    /// calling it early (for example when the window loads) means the first
    /// focus move already has sound.
    /// </summary>
    /// <param name="dumpRoot">Dump root override; defaults to <see cref="RnpsShellAssets.LocateDumpRoot()"/>.</param>
    public static void Preload(string? dumpRoot = null)
    {
        lock (Gate)
        {
            if (_loadStarted)
            {
                return;
            }

            _loadStarted = true;
        }

        _ = Task.Run(() =>
        {
            var clips = LoadClips(LocateContainer(dumpRoot));
            Volatile.Write(ref _clips, clips);

            // Opening the output device costs a few hundred milliseconds; do it
            // now so the first focus move is not late.
            if (clips.Count > 0)
            {
                UiSoundPlayer.Warm();
            }
        });
    }

    /// <summary>
    /// Plays a UI cue. Does nothing when the cues are muted, unavailable, or not
    /// finished loading. Never blocks and never throws; repeated triggers layer
    /// rather than cutting each other off.
    /// </summary>
    /// <param name="soundEvent">Which interaction cue to play.</param>
    public static void Play(UiSoundEvent soundEvent)
    {
        if (!IsEnabled || !UiSoundPlayer.IsSupported)
        {
            return;
        }

        Preload();

        var clips = Volatile.Read(ref _clips);
        if (clips is not null && clips.TryGetValue(soundEvent, out var clip))
        {
            UiSoundPlayer.Play(clip, (float)Volume * MakeupGain);
        }
    }

    /// <summary>Silences any cues still sounding.</summary>
    public static void StopAll() => UiSoundPlayer.StopAll();

    /// <summary>
    /// Drops the decoded cache and the "already loaded" latch so the next
    /// <see cref="Play"/> or <see cref="Preload"/> re-reads the dump. Used by
    /// tests and by a settings change that repoints the dump root.
    /// </summary>
    public static void Reset()
    {
        lock (Gate)
        {
            _loadStarted = false;
        }

        Volatile.Write(ref _clips, null);
        UiSoundPlayer.StopAll();
    }

    /// <summary>
    /// Extracts, decodes and converts every mapped cue from a shell UI resource
    /// container. Returns an empty map when the path is null, unreadable, or not
    /// a container with the expected entries; it never throws.
    /// </summary>
    /// <param name="containerPath">Path to Sce.PlayStation.PUI_UI3.rco, or null.</param>
    public static IReadOnlyDictionary<UiSoundEvent, UiSoundClip> LoadClips(string? containerPath)
    {
        var clips = new Dictionary<UiSoundEvent, UiSoundClip>();
        if (string.IsNullOrEmpty(containerPath))
        {
            return clips;
        }

        try
        {
            var container = RcoContainer.Open(containerPath);

            // The container holds a thousand-odd entries; index the ones we want
            // by name so the mapping is a single pass.
            var wanted = new Dictionary<string, UiSoundEvent>(StringComparer.Ordinal);
            foreach (var pair in Names)
            {
                wanted[pair.Value] = pair.Key;
            }

            foreach (var entry in container.Entries)
            {
                if (entry.Name is null || !wanted.TryGetValue(entry.Name, out var soundEvent) ||
                    clips.ContainsKey(soundEvent))
                {
                    continue;
                }

                var payload = container.ReadEntryData(entry);
                if (!VagDecoder.LooksLikeVag(payload))
                {
                    continue;
                }

                var prepared = UiSoundPlayer.Prepare(VagDecoder.TryDecode(payload));
                if (prepared is not null)
                {
                    clips[soundEvent] = prepared;
                }
            }
        }
        catch (Exception)
        {
            // A missing, truncated or unexpected container just means no cues.
        }

        return clips;
    }
}
