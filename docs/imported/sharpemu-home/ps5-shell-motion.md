<!--
Copyright (C) 2026 SharpEmu Emulator Project
SPDX-License-Identifier: GPL-2.0-or-later
-->

# PS5 shell UI motion — what the cleartext RCO containers actually hold

A design-reference distillation for driving Prosperismo's recreated shell menus.
Everything here was read out of the **unencrypted** `.rco` resource containers
in a 4.03 firmware dump; no decryption or decompiling was involved. Values were
extracted with `scripts/rco_motion.py` (and cross-checked with
`scripts/rco_dump.py`). Raw Sony bytes are **not** copied into this repo — only
the decoded schema and numbers below.

## TL;DR (honest headline)

The RCO containers do **not** carry visual animation timelines. There are no
duration/easing/keyframe/bezier/spring parameters for the shell's on-screen
motion in these files. The large keyword counts that motivated this task
(`motion` ~2256, `anim` ~1066, `duration` ~782, `tween` ~333, plus
`easing`/`transition`/`spring`/`blur`/`fade`/`loop`) are **almost entirely
incidental substring matches inside free text and asset payloads**, not
structured animation records (breakdown in [§2](#2-where-the-keyword-hits-really-come-from)).

The one piece of **genuinely structured, decodable motion-adjacent data** is the
compiled **soundscript table** in `Sce.PlayStation.PUI_UI3.rco`: it binds each
discrete UI interaction (focus move, enter, dialog open, panel change, ...) to a
sound-playback command. That table, plus the sound/effect *event vocabulary*, is
the reusable design reference and is documented in
[§3](#3-the-real-decodable-motion-data-the-soundscript-table) and
[§4](#4-interaction-event-vocabulary).

The shell's actual *visual* motion (focus glide curves, page-transition easing,
dialog scale/fade timing, background parallax) lives in the compiled React
Native JavaScript (Hermes bytecode) bundles that these RCOs are the *assets* for
— not in the RCO tree itself. That is the gap; see [§5](#5-gaps--provenance).

## 1. What an RCO is (recap)

An `.rco` is a compiled document tree over a trailing data blob (see
`docs/rco-format.md`). Four regions matter: a node tree, a **name** table, a
**label** table (the element/attribute tag vocabulary), and the **data** blob
(PNG/DDS/GNF/SVG/VAG/JSON payloads). Structured UI data — if any exists — lives
in the node tree and is expressed with tags drawn from the label table.

Decisive check: **no RCO's label vocabulary contains a single animation tag.**
Scanning every container's label table for `anim/motion/tween/dur/eas/transit/
spring/fade/loop/frame/curve/bezier/delay/keytime` returns nothing. The
compiled documents simply do not model timelines. (Run
`python scripts/rco_motion.py <file.rco>` — the "animation-related tags" line
reads NONE for every file.)

## 2. Where the keyword hits really come from

Classifying each keyword hit by the file region it lands in (name table / label
table / node tree / data blob) shows every hit is either free text or asset
bytes, never a tree/label record:

| Keyword | Real source of the matches | Example |
|---------|----------------------------|---------|
| `motion` | localisation strings & device names in the msgid JSON tables; emoji dictionary `subcategory:"emotion"` | `PS Move motion controller`, `dailymotion`, `movemotion`, `emotion` |
| `anim` | emoji dictionary `category:"Animals & Nature"` / `subcategory:"animal-*"` | `animal-mammal` |
| `duration` | a translated UI word in the msgid tables | `msgid_change_play_time_duration`, `"duration"` (translated label) |
| `tween` | the word **be·tween** inside sentences | `...synced between your PS5 and cloud...` |
| `easing` | the words incr**easing** / decr**easing** / rel**eas**e | `chart increasing`, `release it when the volume starts increasing` |
| `spring` | the emoji `hot springs` | `"name":"hot springs"` |
| `transform`, `opacity` | **static** SVG presentation attributes (PUI_UI3 ships 675 SVGs) | `transform="..."`, `fill-opacity` |
| `begin=` | Adobe **XMP** metadata packets embedded in PNGs | `<?xpacket begin="..."` |
| `blur` | msgid text (`blurred vision`) and message-key names | `msg_bluray_disc`, health-warning copy |
| `loop` | msgid text | translated UI copy |

`scripts/rco_motion.py` prints this provenance per file. For
`Sce.PlayStation.PUI_UI3.rco` every keyword resolves to `data-blob=…` (and a
handful to `name-table=…` for icon asset names like
`iconid_psvr2_motion_controller`); **zero** land in `node-tree` or
`label-table`. No RCO differs.

There is also **no Lottie/bodymovin** JSON, **no SMIL** `<animate>`/`dur=`/
`keySplines`, and **no React-Native `Animated` config** (`useNativeDriver`,
`toValue`, `withTiming`, `stiffness`, `damping`) anywhere in any container. Those
scans all come back empty.

## 3. The real decodable motion data: the soundscript table

`Sce.PlayStation.PUI_UI3.rco` is a mixed texture **+ sound** package. Alongside
its 341 PNG / 675 SVG textures it carries **45 `sound/vag` clips** and a compiled
**soundscript table**. This is the audio-feedback layer of shell motion — the
thing that fires in lock-step with each visual interaction — and it *is*
structured in the node tree with a real tag vocabulary.

### 3.1 Schema (decoded)

The soundscript label vocabulary (in tree order) is:

```
soundscripttable  soundscript  control  command  play  tgt  volume  value
soundgrouptable   soundgroup   voice_num  limit_mode
```

Decoded record shape — one `control` block per interaction sound:

```
control {
    command : play            # the only command opcode seen
    tgt     : <snd_* event>   # a reference to a sound event label (see §4)
    volume  { value : 20 }    # integer attribute
}
```

Attribute encoding in the tree (measured): an attribute is
`[labelOffset][typeCode][value]`. `command` and `tgt` use type code `0x3`
(reference to another label — `play`, or an `snd_*` event name); `volume`/`value`
use type code `0x6` (integer). The `soundgroup` layer carries `voice_num` and
`limit_mode` (polyphony / voice-stealing limits).

### 3.2 Concrete values

- **57 `control` bindings** were decoded, covering **44 distinct interaction
  events**.
- **`volume.value` = 20 on every one of the 57 bindings** (constant). Unit is
  not labelled in the container; it is a per-cue attenuation index on Sony's
  internal scale, not a duration. Treat it as "all UI cues share one nominal
  playback level."
- `command` is always `play`; no stop/fade/loop opcode appears in the decoded
  set (consistent with one-shot UI blips).

So the only hard *numbers* recoverable are: **event→sound bindings** (§4) and a
**uniform cue volume index of 20**. There are no timing numbers because the RCO
does not store the sound envelopes' durations here (the durations live inside the
VAG clips' own sample data, and the visual timing lives in the JS bundle).

## 4. Interaction event vocabulary

This is the most reusable output: the exact, ordered set of discrete shell
moments that get audio-visual feedback. Mirror these events in Prosperismo's menu
state machine and you match the shell's interaction granularity even without
Sony's curves. Two parallel naming schemes exist — the low-level sound clips
(`snd_*`, backed by real VAG payloads) and the higher-level effect events
(`psfx_*`, referenced by the UI layer). Grouped by function:

**Focus / navigation**
`snd_focus_move`, `snd_focus_move_in_keyboard`, `snd_change_panel`,
`snd_change_panel2`, `snd_change_space`, `psfx_key_top_move`,
`psfx_experience_switcher_system_view`, `psfx_experience_switcher_hub_preview`,
`psfx_hub_preview_experience_switcher`, `psfx_system_view_experience_switcher`

**Confirm / cancel**
`snd_enter`, `snd_cancel`, `snd_backspace`

**Dialogs**
`snd_open_dialog`, `snd_open_error_dialog`, `snd_yes_in_dialog`,
`snd_no_in_dialog`, `snd_neutral_in_dialog`,
`psfx_button_for_positive_in_dialog`, `psfx_button_for_negative_in_dialog`

**Menus / control center**
`snd_open_option_menu`, `snd_close_option_menu`, `snd_open_control_center`,
`snd_close_control_center`, `snd_open_home`, `psfx_open_menu_in_osk`

**Toggles / sliders**
`snd_switch_on`, `snd_switch_off`, `snd_slider_level_meter`

**Text / on-screen keyboard**
`snd_open_osk`, `snd_text_input`, `snd_error_in_text_box`,
`psfx_cancel_osk`, `psfx_cancel_osk_conversion`, `psfx_confirm_osk_conversion`,
`psfx_display_candidate_word`, `psfx_change_input_language`

**Voice / agent / recognition**
`snd_voice_recognition_for_osk_starts`, `snd_voice_recognition_for_osk_ends`,
`snd_voice_recognition_for_osk_error`, `snd_face_recognition`,
`snd_agent_wake_up`, `snd_agent_utterance_received`, `snd_agent_no_results`

**Toasts / notifications**
`snd_informative_toasts_something_to_read`,
`snd_interactive_toasts_something_to_do`,
`snd_error_toasts_something_is_broken`, `snd_trophy_toast`,
`snd_platinum_trophy_toast`

**System / session / social**
`snd_error`, `snd_log_out`, `snd_pass_code`, `snd_take_screenshot`,
`snd_join_party`, `snd_leave_party`, `snd_purchase_universal_checkout`,
`snd_tts_cannot_move_focus`, `psfx_boot_game_app`, `psfx_mic_mute`,
`psfx_start_p_in_p_split_screen`

(Full machine list: `python scripts/rco_motion.py <PUI_UI3.rco>` →
"interaction sound/effect events".)

### Related visual assets (what the motion acts on)

The name table also enumerates the focus/selection **visual furniture** the
motion animates, e.g. `image_focus_frame_2`, `image_focus_list_item`,
`image_focus_noise`, `image_scrollbar`, `image_slider_background`,
`image_popupmenu_base` (+ `_tail_top/left/bottom/triangle` nine-patch pieces),
`image_optionmenu_background`, `image_switch_base_highlight`,
`image_checkbox_base_highlight`, `image_tooltip_base_no_arrow`,
`image_webview_finger_cursor` / `image_webview_pointer_cursor`. These are the
sprites a recreation would tween; their geometry (nine-patch margins) is in the
tree, but their *motion* is not.

## 5. Gaps & provenance

**Provenance.** All values above are from
`filesystems/system_ex/vsh_asset/Sce.PlayStation.PUI_UI3.rco` (soundscript table,
event vocabulary, volume=20) and cross-file label scans across every RCO under
`system_ex/vsh_asset/` and
`system_ex/app/NPXS40087/psm/Application/resource/`. `Sce.Vsh.ShellUI.Base.rco`
adds a `layouttable` (static layout, no motion); `BGLayer`, `SystemModalDialog`,
`CaptureMenu`, `Settings.*` contain textures/strings only.

**What could be parsed:** header/section layout, full label vocabularies, name
tables, the soundscript `control{command,tgt,volume{value}}` records, and the
event catalogue. High confidence — these are label-driven records verified
against the tag table.

**What is NOT in these files (the real gap):**
- Visual timings — focus-move duration, page-transition duration/easing, dialog
  open/close scale+fade curves, background parallax/blur ramps. **Not present.**
- Any bezier / cubic-bezier / spring (stiffness, damping) parameters. **Absent.**
- Loop flags, delays, keyframe tracks. **Absent.**

Those parameters are defined in the shell's compiled React Native JS (Hermes
bytecode) bundles — the code that *loads* these RCO assets — which are outside
the cleartext RCO set and were not decoded here. Recovering Sony's exact visual
curves requires decompiling that bytecode, not the RCO tree.

**Practical guidance for Prosperismo.** Use §4 to reproduce the shell's
interaction event set (and fire the matching one-shot cue per event, all at one
nominal level). For the *visual* curves, the RCOs give no numbers; use
platform-typical defaults until the JS bundle is decoded — e.g. short focus
moves (~120–180 ms) with an ease-out, dialog open ~200–300 ms scale+fade — and
mark them as **placeholder, not sourced from Sony data**.
