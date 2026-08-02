# Generic game validation and visual evidence

`scripts/game-test.py` runs SharpEmu against any local game dump the user
explicitly supplies. It does not select a title, level, publisher sequence, or
expected log line on its own.

## First run

Use only a lawfully dumped/decrypted `eboot.bin` stored outside Git:

```bash
python3 scripts/game-test.py doctor \
  --game /absolute/path/to/your/own/eboot.bin

python3 scripts/game-test.py test \
  --game /absolute/path/to/your/own/eboot.bin \
  --game-label my-game \
  --tag first-menu \
  --milestone BOOT="GAME: boot complete" \
  --milestone MENU="GAME: Level has started: main_menu" \
  --timeout 180
```

Replace the example milestone substrings with lines that the target game or
instrumented runtime actually emits. Repeat `--milestone LABEL=SUBSTRING` as
needed. `--expect SUBSTRING` is a shorter alternative when generated labels are
acceptable. A test succeeds only after every configured milestone has appeared
and the stability window has elapsed.

For exploratory input without required milestones:

```bash
python3 scripts/game-test.py run \
  --game /absolute/path/to/your/own/eboot.bin \
  --game-label my-game \
  --tag manual-input
```

## Output contract

Each run writes to the ignored directory
`artifacts/game-runs/<game-label>/<timestamp>-<tag>/`:

- `run.json` records the exact binary, game path, options, attempts, and verdict;
- `attempt-XX.log` is the emulator output;
- `attempt-XX-timeline.json` aligns milestones and capture diagnostics;
- raw frame PNGs preserve the timestamped source evidence;
- `attempt-XX-contact-sheet.png` combines the full visual timeline;
- `attempt-XX-milestones.png` selects frames nearest named milestones.

Attach the milestone sheet, `run.json`, and the smallest relevant log excerpt to
an agent. Use the full contact sheet when the transition between milestones is
important.

## Capture backends

- macOS: `screencapture`, selecting the largest layer-zero window owned by the
  exact SharpEmu process.
- Windows: PowerShell and `System.Drawing`, using the process main window.
- Linux/X11: `xdotool` and ImageMagick `import`.
- All platforms: FFmpeg creates labeled contact sheets.

Run `python3 scripts/game-test.py --help` and the subcommand help for every
option. Keep artifacts local unless you have permission to share the captured
game content.
