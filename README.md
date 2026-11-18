# NoSvMania

A companion window for **osu!stable** that mirrors the 4K osu!mania map you are playing with the SV
stripped out (or normalized to one constant speed), shows your key presses, and uses your skin.
It never touches the game: it only reads the current map/time through [tosu](https://github.com/tosuapp/tosu)
and reads which of your 4 keys are held.

## Setup

1. Install the **.NET 8 SDK** (https://dotnet.microsoft.com/download).
2. Double-click `run.bat`. On first run it downloads tosu into `tosu\` (via `scripts\install-tosu.ps1`),
   starts it, then starts the companion.
3. Start osu!stable and open a 4K mania map. The companion detects it automatically.

tosu is downloaded at setup time instead of being committed to this repo, so you always get the
current release and its own license stays with it.

## Controls (click the companion window first)

| Key | Action |
| --- | --- |
| `F1` | Open / close settings |
| `1`-`4` | Rebind lane 1-4, then press the new key |
| `Up` / `Down` | Scroll speed (1-40) |
| `N` | Toggle Strip SV / Normalize |
| `B` | Toggle "BPM scales scroll" |
| `Left` / `Right` | Clock offset in 5 ms steps |

Everything is saved to `settings.json` next to the exe. Resize the window freely.

## What it does

- **Strip SV mode:** deletes every green line except the ones that cancel a BPM change. A green line is
  kept when its SV is within `CompensationTolerance` (default 3%) of `baseBpm / currentBpm`. The base BPM is the
  BPM the map spends the most time at. Every other green line is replaced by 1.0x.
- **Normalize mode:** constant scroll speed for the entire map, regardless of BPM or SV.
- **Mods:** Mirror flips the lanes, DT/NC (1.5x) and HT (0.75x) change the clock. Read automatically.
- **Skin:** reads `skin.ini` `[Mania]` (the section with `Keys: 4`): `ColumnWidth`, `HitPosition`,
  note / hold head / body / tail and key images, with `@2x` support. Missing images fall back to plain shapes.

## Known limitations / things to verify

- This was written without being able to compile or run it. Expect to fix a small error or two on first build.
- **Scroll speed is approximate.** The 1-40 scale uses the same ms-on-screen formula lazer uses. Adjust the
  speed until it feels like your in-game speed. Note size matches your skin's column widths scaled by
  `window height / 480`, so to match in-game size 1:1, resize the window to your game's height.
- **BPM compensation assumes stable scales scroll speed with BPM** relative to the main BPM. If it looks wrong on
  a map with BPM changes, press `B` to turn that off, and tell me what you saw.
- The tosu field names (`beatmap.time.live`, `folders.*`, `files.beatmap`, `play.mods.number`, ...) follow tosu's
  v2 schema. If the map or skin is not detected, open `http://127.0.0.1:24050/json/v2` while osu! is running and
  adjust `TosuClient.TryParse`. You can also set `SkinPathOverride` in `settings.json`.
- `ColumnStart`, stage images, lighting and judgement effects from the skin are not drawn.
- Windows only (key state and text rendering use Windows APIs).
- Use it for practice. It reads game state while you play, so check osu!'s rules before using it anywhere that matters.
