# Scrap Siege — sound shopping list (Freesound)

**Status:** waiting on files. Nothing here is blocking — every sound below already plays, synthesized
from arithmetic by `Assets/Scripts/Audio/ProceduralSfx.cs`. This list is about replacing them one at a
time with real recordings.

---

## How this works (read once)

I built a drop-in override layer, so **you do not need me to integrate anything**. The loop is:

1. You find a clip on [freesound.org](https://freesound.org).
2. You save it as **`Assets/Audio/Resources/Sfx/<ExactName>.wav`** — the name must match the
   **Sfx name** column below, exactly, case included.
3. That sound is now the recording. Everything else keeps using synthesis.

`GameAudio` logs `using recorded clip for <Name> (overriding synthesis)` on startup for each file it
finds, so you can confirm it took effect from the Unity console without playing the sound.

If a clip turns out to be wrong for the moment, delete the file and it falls straight back to the
synthesized version. There is no half-migrated state.

### Licence rule — not negotiable

**CC0 / Public Domain only.** Filter Freesound's search to `License: Creative Commons 0`.

The repo is public and the app takes real money. CC-BY ("Attribution") is *usable* but drags an
attribution obligation into a monetized, redistributable binary, and every CC-BY file would need a
credits screen entry maintained forever. CC-BY-NC is **unusable** — the app is commercial. It is not
worth the paperwork for a jam entry; there is plenty of CC0 material.

Record what you took in the table at the bottom of this file as you go.

### Technical shape

- **Format:** WAV (Unity imports it fine; it re-encodes for Android anyway).
- **Mono** is preferred — the game plays everything 2D, so a stereo file is wasted bytes.
- **Trim the silence off the front.** A clip with 200 ms of lead-in feels laggy, and this is the
  single most common problem with library audio.
- **Length matters** and is listed per sound below. Anything much longer than the target will feel
  sluggish or will overlap with itself during a busy fight.
- Don't worry about normalising volume precisely — I can trim per-sound levels in code afterwards.

---

## The list

Ordered by how much difference each one makes. If you only do the first six, the game already sounds
dramatically better in a demo video.

### Tier 1 — biggest impact

| # | Sfx name | What it is | Target length | Search terms | What to listen for |
|---|---|---|---|---|---|
| 1 | `Deploy` | A unit is dropped onto the board | 150–300 ms | `mechanical thud`, `metal drop`, `servo clank`, `robot step` | A short mechanical *placement*, not an explosion. It fires several times in a row, so it must not have a long tail. |
| 2 | `UnitDeath` | A unit is destroyed | 250–500 ms | `metal crunch`, `scrap collapse`, `debris short`, `metal impact` | Junkyard scrap collapsing. Should read as *breaking*, not as a gunshot. |
| 3 | `SentryFire` | A garrison sentry shoots a unit | 100–250 ms | `laser zap short`, `energy shot`, `sci fi blaster`, `pew` | Fires up to ~14×/sec across the board. Must be *short and dry* — anything with reverb turns into mush. |
| 4 | `BaseHit` | A unit reaches a base and damages it | 300–600 ms | `metal impact heavy`, `hull hit`, `structural crunch` | Weightier than `UnitDeath`. This is the sound of the match being decided. |
| 5 | `Victory` | Player wins | 1.0–2.0 s | `victory jingle short`, `win fanfare`, `success chime` | Short. It plays over the outcome card, and 4 seconds of fanfare outlasts the player's interest. |
| 6 | `Defeat` | Player loses | 1.0–2.0 s | `fail jingle`, `defeat sting`, `power down` | A descending sting or a power-down. Avoid comedy "wah-wah" — the game isn't. |

### Tier 2 — the new unit classes

These are new enum values added with the unit-variety pass. They currently borrow a neighbour's
synthesized recipe, so **a marksman shot and a sentry shot sound identical today** — which is exactly
the thing that makes a game feel thin. These are the highest-leverage additions after Tier 1.

| # | Sfx name | What it is | Target length | Search terms | What to listen for |
|---|---|---|---|---|---|
| 7 | `MarksmanShot` | The Marksman firing at long range | 120–250 ms | `rifle shot short`, `sniper crack`, `railgun`, `sci fi rifle` | Must be clearly *different* from `SentryFire` — sharper, more of a crack than a zap. |
| 8 | `TurretFire` | The Turret emplacement firing | 150–300 ms | `auto turret`, `heavy blaster`, `cannon short`, `mech gun` | Heavier and slower than the marksman. Reads as a machine, not a person. |
| 9 | `HeavyDeploy` | Deploying a Bulwark or a Turret | 250–450 ms | `heavy metal drop`, `hydraulic slam`, `mech land` | Same event as `Deploy` but *heavy*. Hydraulics, weight settling. |
| 10 | `StealthDeploy` | Deploying a Saboteur | 150–300 ms | `whoosh short`, `stealth`, `cloth swish`, `sci fi teleport soft` | Quiet and quick. The point is that it sounds like nothing heard it. |
| 11 | `WaveIncoming` | The AI commits a wave (fires ~1.5 s before it appears) | 400–900 ms | `alarm short`, `warning beep`, `alert sting`, `radar ping` | A warning, not a jump-scare. It fires repeatedly across a match. |

### Tier 3 — interface polish

| # | Sfx name | What it is | Target length | Search terms | What to listen for |
|---|---|---|---|---|---|
| 12 | `UiTap` | Any button | 40–120 ms | `ui click`, `button tap`, `interface blip` | Very short, very dry. If you notice it, it's too loud or too long. |
| 13 | `ClassSelect` | Tapping a unit-class chip | 60–150 ms | `ui select`, `switch click`, `mechanical toggle` | Slightly more substantial than `UiTap`, so switching class feels like a decision. |
| 14 | `Rally` | The Rally order is issued | 400–800 ms | `horn short`, `command signal`, `war horn`, `whistle` | A *command*. A short horn or signal whistle. This is a board-wide order and should feel like one. |
| 15 | `PhaseChange` | Scan → Place → Siege transitions | 300–700 ms | `ui transition`, `whoosh ui`, `stinger short` | Neutral and non-committal — it plays three times before the game even starts. |

---

## How to pick between candidates

Bring me **1–3 options per sound** and I will listen to them in context (I can check length, trim,
peak level, and whether it collides with the other sounds) and tell you which to keep. Drop the
candidates anywhere convenient — e.g. `Assets/Audio/_candidates/Deploy_a.wav`, `Deploy_b.wav` — and
tell me which numbers you've filled. Only the winner gets renamed into
`Assets/Audio/Resources/Sfx/`.

Two things I cannot judge and you can: whether it sounds *good to you*, and whether it fits the
junkyard-scrapyard identity. Trust that over my measurements.

---

## Provenance log — fill this in as files land

Required for the public repo. One row per file that ships.

| Sfx name | Freesound ID / URL | Author | Licence | Date added |
|---|---|---|---|---|
| _(none yet)_ | | | | |

---

## Notes

- The folder `Assets/Audio/Resources/Sfx/` may not exist yet — just create it. The `Resources` part of
  the path is load-bearing (that is how Unity finds the clips at runtime); the folder can sit anywhere
  under `Assets/` as long as the `Resources/Sfx/` tail is intact.
- Total added size is worth watching: the release AAB is currently 39 MB. Fifteen short mono WAVs is
  a couple of MB at most, which is fine — but don't bring in 30-second ambient loops.
- **Music is deliberately not on this list.** A looping track is a different problem (licensing,
  size, mixing against 15 SFX) and the demo video will likely have its own. Worth revisiting only
  once all of the above is done.
