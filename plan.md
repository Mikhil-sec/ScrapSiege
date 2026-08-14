# Scrap Siege — Design & Build Plan

*Last updated 2026-08-11 (Pass D — unit classes, selective Rally, AR intent, navigation; Section 9).*

## 1. The Hackathon

Built for **RevenueCat Shipaton 2026** (RevenueCat/Devpost).

- **Submission window:** 2026-08-01 to 2026-09-30.
- **Target category:** **Next Gen Award** (student-only; judged on video + open-source code, no store release required).
- **Required at submission:**
  - A **demo video**, max **2 minutes of essential footage**, public on YouTube/Vimeo, showing the app on the device it was built for. No third-party trademarks or unlicensed music.
  - A **public open-source repository**.
  - **The app must integrate the RevenueCat SDK powering at least one in-app purchase.**
- **Secondary targets:** RevenueCat **Design Award** and **HAMM Award**.

## 2. Direction History — and why the current design exists

1. **AI-generated content apps** — rejected. Zero-AI is a deliberate stance.
2. **UWB outdoor tag game** — rejected. UWB isn't universal.
3. **Two-player AR tabletop with scavenged terrain** — *built, then abandoned 2026-08-07.* Weeks 1–2 (scanning, pathing, siege loop) and a full LAN implementation all worked as code. **What killed it was AR plane detection**: across floor, cushion table and dining table it could not reliably produce a lockable surface, making shared-board co-location too fragile. Preserved on `two-player-archive`.
4. **Current — single-player, authored maps.** Hand-designed battlefields projected onto any flat surface. Removes the two hardest dependencies at once (cross-device co-location, and scanning arbitrary objects) while keeping the AR-native identity.

## 3. The Concept

**Pitch:** A tabletop war game that only exists on *your* table — project a battlefield onto any real surface, then out-think it by physically moving around the board, leaning in to place troops precisely and pulling back to command.

- **The AR is load-bearing, not decorative.** Your physical vantage point changes what you can do and what you can see. Take away the camera and the game stops working.
- **Robust by construction.** One flat surface and nothing else. No second device, no cloud anchor, no internet.
- **Demo-friendly.** Tabletop scale, filmable anywhere, a match fits in a 2-minute video.

## 4. Core Mechanics

### Mechanic 1 — Vantage (built)

Camera height above the board is a continuously-read input. No UI toggle; posture *is* the control.

| Posture | Placement precision | Field of view | Rally |
|---|---|---|---|
| **Leaned in** (low, close) | Tight — lands where you tap | Narrow | Unavailable |
| **Pulled back** (high) | Loose — scatters | Whole board | **Available** |

Implementation: `VantageController` maps camera-height-above-board across 0.20 m → 0.65 m into `Vantage01`, driving deploy scatter (0.005 m → 0.10 m). Exponential smoothing at 8/sec is load-bearing — raw handheld height is noisy enough that unsmoothed scatter feels random rather than skilful. `DeployReticle` draws the current scatter radius on the table *before* the tap, so precision is observable.

### Mechanic 2 — Rally (built) — the fix for vantage's dominant strategy

Vantage as originally specced only *penalised* standing back, while the information gain was passive and free — so optimal play was "glance up once, then stay leaned in permanently". Posture became a glance, not a stance.

Rally gives high vantage an **action**: redirect every deployed unit through a tapped lane. Gated on `Vantage01 ≥ 0.6` (with hysteresis so the button can't strobe), costs 1 scrap, 8s cooldown, and cancels if the player leans back in mid-order.

> **Known:** Rally currently has no *reason* to be used, because there is no opponent creating threats to react to. This is not a tuning problem — it resolves when the AI commander lands (Section 9).

### Mechanic 3 — True line of sight, graded (built)

Enemies are revealed only when genuinely visible from the real camera position. Three raycasts per target per tick at 15 Hz against **layer 8 `SiegeTerrain` only**:

| Sample points visible | Tier |
|---|---|
| 0 | Hidden (drifting ghost shown instead) |
| 1 | Faint |
| 2 | Partial |
| 3 | Full |

Grading matters: a binary flip reads as a rendering bug, while grading makes *half*-peeking meaningful. Verified against real physics — eye at 2.2 cm → Faint, 6 cm → Partial, 55 cm → Full.

**Ghosts drift** along the target's last-seen heading (capped at 3s, faded by 6s), so stale intel is actively *wrong* rather than merely old — which is what gives re-peeking value.

**`VisionTarget` is on sentries only, not the bases.** Hiding the objective makes the game feel broken.

### Mechanic 4 — Route variety (built; reworked 2026-08-08)

Both modes can walk **anywhere**. They differ only in how much they *value* cover, applied as a **per-agent** NavMesh area cost via `NavMeshAreas.ApplyCoverPreference`:

- **Direct** prices cover the same as open ground (cost 1.0), so it takes the geometrically shortest line — and will thread a cover corridor when that genuinely *is* the shortest way. A well-aimed Direct drop can use the corridor; a loose one spills into the open.
- **Covered** prices cover far cheaper (cost 0.08), so NavMesh's own pathing detours to hug the CoverLane polygons laid beside cover terrain.

`GarrisonSentry` only damages units *not* standing in a CoverLane, which is what gives the choice stakes.

> **Why this changed.** Direct used to have the CoverLane area *excluded from its areaMask*. That both contradicted the design (a Direct unit could never use the corridor, however well aimed) and broke the map — on a narrow board the cover polygons were the only link between the two halves, so Direct units had no complete path at all and stopped partway.
>
> The reason exclusion was used originally: `NavMesh.SetAreaCost` is **global** to all agents. But `NavMeshAgent.SetAreaCost` is a genuine **per-agent** override, which is what this mechanic actually wants and keeps the whole board reachable for both modes.

### Mechanic 5 — Flank by walking (built)

Sentries cover a **150° facing arc**, not a circle, drawn on the table by `SentryArcVisualizer` so the blind side is readable with no UI. `MusterPhaseController` faces them at the player's edge with ±35° jitter — **without the jitter every sentry covers the same bearing and one position flanks them all**, collapsing the mechanic.

### Mechanic 6 — Unit combat, frontage-limited (BUILDING 2026-08-08)

Units of opposing teams fight when they meet. The design problem this solves is that the obvious
implementation is a **bad** one: if every nearby unit damages the same target, losses scale by
Lanchester's square law, the bigger stack always wins, and "deploy the maximum number of units" is
strictly correct. Positioning, vantage and cover all stop mattering — an arithmetic game, not a
tactical one.

**The fix is to cap frontage, not damage.**

- A unit may be engaged by **at most one** enemy at a time. Duels, never dogpiles.
- A unit targets the nearest **unengaged** enemy inside its engagement radius. If every enemy nearby
  is already locked in a duel, it **ignores them and continues to the base**.

So a 5-vs-2 is two duels and *three units walking straight past*. Numbers buy **breakthrough**, not
annihilation — which is the correct currency for a siege game and keeps the race-to-the-objective
core intact. Defence becomes "can I plug the frontage", not "do I have more bodies": on a multi-lane
map two defenders can only cork two lanes, and **Rally is how the player finds the third**. That is
the first time Rally has had a real reason to exist.

Two secondary dampers stop degenerate cases:
- **Cover reduces damage taken.** Three units in a CoverLane beat five in the open, so positioning
  beats numbers. This is also what keeps Mechanic 4 (Direct vs Covered) meaningful.
- **A duel winner has a brief recovery** before it can re-engage, so a survivor cannot chain-kill
  its way down a queue of arrivals.

Fights last 1.5–2 s by design — long enough to read as combat, short enough not to stall the advance.
`UnitAnimator.PlayAttack()` (which already existed, fully written and never called by anything) fires
per damage tick, so a fight is 3–4 visible lunges. Death plays `UnitDeathEffect` rather than an
instant `Destroy`, because units silently vanishing was indistinguishable from the disappearing-unit
bug this project spent a session chasing.

### Terrain archetypes

| Archetype | Role | Blocks sight? | Blocks movement? |
|---|---|---|---|
| Wall / Barricade | Hard block | Yes | Yes |
| Spire / Chokepoint | Hard block, tall, garrison anchor | Yes | Yes |
| Watchtower | Garrison anchor, wider arc | Yes | Yes |
| Plain Obstacle | Hard block, low | Yes | Yes |
| Rubble / Cover | Passable, lays a CoverLane | **No** (by design) | **No** (by design) |

Sight and movement are **independent** — see `TerrainObjectSpawner.BlocksLineOfSight` / `BlocksMovement`. Rubble blocks neither, yet still lays down the cheap CoverLane area Covered mode steers by and `GarrisonSentry` treats as safe.

> **Fixed 2026-08-08:** every archetype used to carve a `NavMeshObstacle` unconditionally, including Rubble — so the "safe corridor" a unit is meant to route *through* was solid. On The Narrows the rubble line plus the wall spine left a **5 mm** gap on a 33 cm-wide board, which the bake's agent-radius erosion then sealed completely, severing the map.

## 5. The AI Commander (BUILDING 2026-08-08 — the biggest gap)

Rule-based, explicit thresholds and utility scoring, no learned model.

- **Symmetric economy** — the AI ticks resources on the same schedule; difficulty comes from decision quality, not cheating.
- **Behaviour loop** (~1s tick): score candidate actions and take the best.
- **Difficulty tiers:** resource rate, reaction delay, willingness to commit, unit mix.
- **Readability matters.** Telegraphing a push beats optimal play. This is a demo-video game as much as a strategy game.

Its units get `VisionTarget` so line of sight applies to them (the player's own unit prefab
deliberately does not have one — you always see your own army).

**Scope decided 2026-08-08: full mutual siege.** The AI deploys real attacking units at the player's
base, so the player can lose. This pulled the Lose condition forward as a hard dependency —
`LevelBuilder` already builds `PlayerBase`/`PlayerBaseHealth` and nothing ever watched it, so an AI
push had nothing to land on.

**Action set: Push / Intercept / Hold.** The original plan said "reinforce a threatened lane", which
assumed spawning extra sentries. **Intercept replaces Reinforce** — deploy toward a blocking position
against the player's strongest advance rather than at their base. It needs no sentry work and it is
the better behaviour anyway, because it directly creates the frontage contest Mechanic 6 is built
around. **Hold** banks resources toward `holdBankTarget` so pushes arrive as readable waves instead
of a one-unit trickle.

**Gated per level by `LevelDefinition.hasAICommander`.** The three shipped levels are authored for a
one-directional siege and stay exactly as they are; only the new AI level runs a commander. This is
what makes it safe to build the AI without first re-balancing everything else.

## 6. Levels

Hand-authored `LevelDefinition` ScriptableObjects in **normalised board space** (x, z each 0..1; `boardAspect` sets width; **z = 0 is always the player's edge**), so one layout projects onto any table size. `LevelBuilder` maps them onto a `BoardRoot` whose **localScale is the board's length in metres**.

**Shipped levels** (`Assets/Levels/`), each built to force one mechanic:

| # | Name | Teaches |
|---|---|---|
| 01 | The Narrows | Precision — one cover corridor watched by a sentry |
| 02 | Blind Spire | Line of sight — two sentries hidden behind a centre spire |
| 03 | Two Lanes | Rally — a spine splits the field; lanes rejoin only deep |
| 04 | The Gauntlet | The AI commander — three lanes, Recruit tier, the first level you can lose |
| 05 | The Foundry | **Pro only.** Everything at once, against the Veteran tier |

> **Authoring gotcha:** `MusterPhaseController` fills garrison slots in **terrain array order**. In Blind Spire the two watchtowers must come *before* the spire, or the spire steals a sentry and the map's premise breaks. A validator (see Section 9) checks this.

**Placement flow:** lock a plane → tap to drop the board → drag to move, pinch to scale, twist to rotate → Confirm. A `LineRenderer` footprint outline shows the board before it is built. Raycasts fall back through `PlaneWithinPolygon | PlaneEstimated | FeaturePoint` deliberately, given this project's plane-detection history.

## 7. Monetization (RevenueCat)

**Already built and working — do not break it.**

- **Project:** "ScrapSiege" (`proj3a523262`). Entitlement `pro` (`entl844b33dd6b`).
- **Test Store** (`appda5538b8e2`) — product `scrap_siege_pro_monthly` ($2.99/mo) in the `default` offering's `$rc_monthly` package (`pkgef5eaf57c5e`). Works in Editor Play Mode only; a real Android build always goes through Google Play Billing.
- **Play Store** (`appa37d9670f8`, `com.mikhilnaika.scrapsiege`) — app created in Play Console, subscription live: product `scrap_siege_pro`, base plan `monthly` → store identifier `scrap_siege_pro:monthly`, registered in RevenueCat as `prod759b1f896f` and attached to both the `pro` entitlement and the `$rc_monthly` package (2026-08-09). The scene (`Assets/Scenes/ARTest.unity`, `MonetizationManager.revenueCatApiKey`) now carries the real Play Store key `goog_BPqxjAwHxIuYgSZXpVxbuhuaLbt`, not the Test Store key.
- **Signing:** upload keystore lives outside the repo at `C:\Users\naika\keystores\scrapsiege-upload.jks` (alias `scrapsiege-upload`). `Scrap Siege > Build Android APK (RELEASE - for Play Store)` produces a signed, non-debuggable `build/ScrapSiege.aab` — verified with `bundletool`/`apksigner`/`aapt2` on 2026-08-09. Uploaded to Internal Testing and a real license-tester purchase completed 2026-08-10. Full detail and exact verification commands are in `SECURITY.md`'s findings register.

  **The keystore password resets on every Editor restart, and that used to block development builds too.** Unity persists `androidUseCustomKeystore: 1` into `ProjectSettings.asset` but deliberately never persists the passwords, so after reopening the Editor *any* Android build — including a plain USB development APK that has no business touching the upload key — failed asking for a password that no longer existed. Two fixes, both in `Assets/Editor/BuildScript.cs`:

  - **Development builds now switch `useCustomKeystore` off for the duration of the build and restore it afterwards**, falling back to the Android debug certificate. That is all `adb install` needs, and `ProjectSettings.asset` is left byte-identical. Dev builds never prompt for a password again.
  - **Release builds still require the real key**, and `Scrap Siege > Apply Release Keystore Passwords (from environment)` re-applies it from the `SCRAPSIEGE_KEYSTORE_PASS` environment variable (optionally `SCRAPSIEGE_KEYALIAS_PASS`). Environment, never a file in the repo — this repo is public. Set it as a Windows *user* environment variable and restart Unity Hub so the Editor inherits it.
- **Code:** `Assets/Monetization/` sits deliberately **outside** `ScrapSiege.Runtime.asmdef` because the RevenueCat SDK ships no asmdef. `Assets/Scripts/Monetization/ProEntitlement.cs` is the decoupled gate gameplay reads.

**What Pro unlocks:**

- **Level 05 "The Foundry"** — `requiresPro: true`, gated through `LevelCatalog.IsUnlocked`. Added rather than converting a free level, so Pro is a value tier and not a lockout. It is also the only level that runs the harder **Veteran** AI tier.
- **The saturated terrain palette** (`TerrainObjectSpawner.ProColorForArchetype`).
- **The Turret unit class** (`Assets/Units/Turret.asset`, `requiresPro`) — the one perk that touches
  gameplay, and the one flagged in Section 10 as arguable.
- **Veteran skins for all five unit classes** (added 2026-08-13, Pass F) — `UnitClass.proModelPrefab`,
  swapped in by `UnitClassVisual.ResolveModelPrefab` whenever the entitlement is active. Purely
  cosmetic, authored at the same overall height as the base model so the normalisation at swap time
  cannot turn an upgrade into a shrunken figure. **This is the perk that was missing from the tier:**
  every other Pro item is either content or power, and the only one visible to a player who is
  already mid-match was the palette. A skin set is worth paying for, is visible in a demo video, and
  cannot possibly win a match — which is the honest counterweight to the Turret being gated at all.
- A **PRO ACTIVE** badge in the main menu, replacing the Go Pro button.

The paywall copy for all of the above is **derived, not written** — `ProFeatureCopy.BuildFeatureList`
counts `requiresPro` levels, `requiresPro` classes and classes carrying a `proModelPrefab`. Ship a
sixth skin and the paywall advertises it with no edit anywhere.

**Entitlement changes must be live, and this is easy to get wrong.** The 2026-08-10 purchase test proved the plumbing worked and *still* looked broken, because:

- `TerrainObjectSpawner` keyed its material cache on `(archetype, role, isPro)`. Flipping to Pro mid-match changed which cache entry new lookups hit, while every already-spawned renderer went on holding the free-palette material. The Pro state is no longer part of that key — there is one material per slot, and `ProEntitlement.Changed` repaints them in place, so the board recolours on the same frame.
- `MainMenuController` computed each card's lock state once while building the list. It now also rebuilds on `ProEntitlement.Changed`, which matters because the paywall sits *on top of* the level select and closing it never re-enters `ShowLevelSelect()`.

`ProEntitlement.Changed` had zero subscribers for the entire time the entitlement existed. **Anything that reads `ProEntitlement.IsUnlocked` must also subscribe to `Changed`, or it will silently be a restart-only feature.**

## 8. Art pipeline

**Static low-poly models from Blender + procedural animation in Unity — deliberately not rigged.** Units are ~5cm on a real table viewed through a phone, so rig deformation is invisible while gross motion (leg swing, bob, lunge) is not.

- `SiegeTrooper.fbx` — 11 parts with real joint pivots (hips, shoulder, waist), upper body parented to the torso. `UnitAnimator` drives it from NavMeshAgent velocity, keyed to **distance travelled** so a stalled unit stops marching instead of moon-walking.
- `Terrain_Wall/Spire/Watchtower/Rubble.fbx` — each authored to **fill a unit cube with base at y = 0**, so the spawner's existing footprint scaling needed no maths changes.
- `TerrainObjectSpawner` falls back to primitives for any archetype with no model assigned, and overrides model materials with the archetype colour (that colour is both the gameplay signal and where the Pro palette lives).

**Lesson:** always render the **near-overhead** view before accepting a unit model. The first trooper looked fine from the front and read as an ambiguous blob from the actual gameplay angle; fixed with a bright forward-pointing crest.

### ⚠️ Blender FBX export settings — non-obvious and easy to get wrong

Every terrain model must import as **`size = 1.0`, `min.y = 0`, root rotation X = 0**. Check any new
export against an untouched model (`Terrain_Wall`) rather than against expectation. The settings that
produce it:

```python
bpy.ops.export_scene.fbx(
    filepath=path, use_selection=True,
    apply_unit_scale=False,            # True lands the model 100x too small
    apply_scale_options='FBX_SCALE_NONE',
    global_scale=1.0,
    bake_space_transform=True,         # folds Z-up -> Y-up into the vertices, root rotation stays 0
    object_types={'MESH'}, mesh_smooth_type='FACE',
    axis_forward='-Z', axis_up='Y', add_leaf_bones=False)
```

**Blender's default FBX settings are wrong for this project** and produce a model 100x too small
carrying a −90° X root rotation. That is survivable-looking (it still renders) but the spawner maps
`localScale` straight to metres from a unit cube, so the visual ends up a speck inside a correctly
sized collider and NavMesh obstacle. Confirmed by measurement on 2026-08-08 during the rubble
remodel; `bake_space_transform=True` is the setting that actually matters.

**Rubble was remodelled 2026-08-08** for exactly the readability reason in Section 10: it was a dense
mound with an upright slab reaching the full unit-cube height on a solid base plate, so it read as a
hard barrier and walking through it looked like a bug. Now 18 low scattered chunks, max height
**0.26** of the unit cube, with visible floor between them and the solid base plate broken into
fragments. 144 verts, down from 696. The four material slots (`SS_Plate/SS_Accent/SS_Body/SS_Metal`)
are preserved in order, because `MaterialSlots.RoleForSlot` reads them by name.

### Audio — synthesized, not recorded (added 2026-08-10)

Every sound effect is generated from arithmetic at load in `Assets/Scripts/Audio/ProceduralSfx.cs`:
decaying oscillators (sine/square/triangle/saw), low-passed noise bursts, a ~2 ms attack ramp on
each layer to kill start-of-clip clicks, and a `tanh` soft-clip on the sum. Nine sounds, all under a
second, a few milliseconds to build in total.

This is a deliberate choice, not a placeholder. Licensed third-party audio in a **public** repo is an
attribution and redistribution problem for a jam submission, and this project has no way to record
its own. Synthesis keeps the repo free of binary blobs and matches the flat-shaded art direction —
the game already looks synthetic.

`Assets/Scripts/Audio/GameAudio.cs` is the only entry point: `GameAudio.Play(Sfx.Deploy)`, callable
from anywhere. It **bootstraps itself** via `[RuntimeInitializeOnLoadMethod]` rather than being a
prefab wired into each scene — a missing scene reference would silence the game in exactly the build
nobody re-checks. Eight round-robin `AudioSource` voices so a busy siege doesn't cut itself off, and
per-sound minimum intervals on the ones that can retrigger many times a second (sentry fire, unit
death) so they read as events rather than as a buzz. Sound is **2D on purpose**: the board is a
~60 cm tabletop viewed at arm's length, so every source is effectively equidistant and 3D panning
would spend `WorldScale` care to produce something inaudible.

The UI click is hung on `UIButtonMotion.OnPointerDown`, which every interactive button already
carries for its press animation — that covers the whole UI without touching a single UnityEvent in
either scene.

**Not done:** no music, and the mix has never been heard on phone speakers, only assumed.

## 9. Remaining work, in order

**Playable end-to-end and accepted by the user's own device test on 2026-08-08** (post world-scale fix). That test raised design gaps that are deliberately NOT fixed yet — see the callout below before touching levels, cover or balance.

### ✅ Passes A–C COMPLETE and device-tested 2026-08-08

The user tested the build and accepted it — "everything looks good" — with one defect found and fixed:
**level 4's briefing overflowed its card background.** Cause was mine: 175 characters where the other
three levels sit at 97–106 and the card is fixed-height. Shortened to 94. **Level card briefings have
a ~110 character budget** — treat it as a hard constraint when authoring new levels.

### The plan as agreed, 2026-08-08 (session 3) — three passes

**Pass A — Combat foundation ✅ DONE.** Unit-vs-unit combat is no longer polish; with the AI deploying
real attackers it is the primary source of stakes, so it came first.
1. `Team { Player, Enemy }` on `SiegeUnit`. `GarrisonSentry` and `RallyController` both filter by it —
   they operate on the shared static `SiegeUnit.Active` list, so **without this Rally would redirect
   the AI's own attackers** and sentries would shoot their own side.
2. Frontage-limited engagement (Mechanic 6) with cover damage reduction and winner recovery.
3. `UnitDeathEffect` — the unit's own 12 parts detach, fall and fade over ~2 s. **Not Rigidbodies:**
   at `WorldScale.Scale = 5` Unity's 9.81 gravity reads as 1.96 real m/s², i.e. moon gravity, so
   debris integrates manually against `9.81 * WorldScale.Scale`. Deterministic, no collider
   interactions with the NavMesh, cheaper when several units die at once. Reusing the real parts
   rather than generic cubes is free, looks like the figure coming apart, and keeps the team tint so
   you can still tell whose unit died.
4. **Visible sentry fire** — a tracer from sentry to the unit it is damaging, plus a hit flash on that
   unit, driven off the damage tick itself so the visual cannot lie about the rule.
5. Balance: `SiegeUnit.prefab.health` 10 → **3**. The number is *derived*, not guessed: 1 damage per
   0.5 s tick against 3 HP is a 1.5 s fight, which is the "1–2 seconds of visible combat" the design
   calls for. It also fixes the leftover test buff and makes sentry exposure lethal again.
6. Lose condition — symmetric `OnPlayerLost` on `SiegeOutcomeController`, watching the already-built
   `PlayerBaseHealth`.

**Pass B — AI commander + its level ✅ DONE.**
7. `LevelDefinition.hasAICommander` flag; `AICommanderProfile` ScriptableObject per difficulty tier
   (`AIProfile_Recruit.asset` is the only tier so far).
8. `AICommander` — Push / Intercept / Hold, ~1 s scored tick, telegraphed pushes.
9. **Level 04 "The Gauntlet"** — three lanes from two split spines with a mid-board crossroads so Rally
   can switch lanes; one rubble patch per lane; **two watchtowers for three lanes, so one is always
   open** — the frontage rule expressed as level design. Winnable via `enemyBaseHealth 8` vs
   `playerBaseHealth 14` plus a 1.5× slower AI economy. Tip text went in the existing
   `LevelDefinition.briefing` field, already rendered on the card by `MainMenuController.PopulateCard`.

**Pass C — Art and the obstacle bug ✅ DONE.**
10. Rubble remodelled — an art fix for a *correctly* passable piece. See Section 8.
11. Watchtower carve investigated and **ruled out** — see Section 10.

### ✅ Pass D — depth pass, 2026-08-11 (unit classes, selective Rally, AR intent, navigation)

Six things the user asked for after playing the 0.5.0 build. All built, Editor-verified, **none of it
device-tested.**

**D1. Unit classes.** `UnitClass` ScriptableObject + `UnitRoster`, five classes in `Assets/Units/`.
Shipping a new one is an asset and a roster entry — no prefab, no script, no scene edit. The roster
bar (`UnitRosterBar`) builds its chips from the asset at runtime.

| Class | Cost | HP | Speed | Reach (× board) | Identity |
|---|---|---|---|---|---|
| Trooper | 1 | 3 | 1.0 | 0.06 | Baseline. Unchanged from the single unit that existed before. |
| Bulwark | 2 | 9 | 0.65 | 0.06 | Soaks. Slow attack (0.9 s), best cover multiplier (0.35). Buys time, not kills. |
| Marksman | 2 | 2 | 0.9 | **0.17** | Engages from ~3× melee reach. Free damage while the enemy closes; loses the moment it arrives. |
| Saboteur | 3 | 2 | 1.7 | 0.05 | **Invisible to sentries** and **never stops** — cannot be pinned, cannot clear a path. 4 base damage. |
| Turret | 4 | 8 | **0** | **0.24** | Emplacement. Never moves, never damages a base. Pure lane denial. **Pro-gated.** |

**The frontage rule is untouched and still governs everything** — one unit fights at most one enemy,
whatever its class. Ranged units get *reach*, never focus fire. What is new is that a duel is now
**asymmetric**: each side independently checks the range *it* can shoot from, so a marksman stands and
fires while its melee opponent walks in. That single range comparison is the whole ranged system;
there is no parallel combat path.

**D2. Selective Rally.** `RallyController.SetScope(UnitClass)` — null means the whole army, otherwise
one class. A two-state HUD toggle switches between ALL and the currently-selected deploy class. Why:
a board-wide rally was a panic button, and it actively fought the point of having classes — you could
not pull a screening line back without also pulling the saboteur that was two steps from the base.
Emplacements are always excluded (they cannot move, and counting one as redirected would charge for
an order never carried out).

**D3. AR intent — this is the important one.** Three changes that together make the camera's position
matter, in order of impact:

1. **Terrain got much taller.** `NormalisedHeightForCategory`: Short 0.035 → **0.055**, Medium 0.070 →
   **0.130**, Tall 0.130 → **0.220** of board length. At the old values a Tall piece was 7.8 cm on a
   0.60 m board against a 5.2 cm unit — half a unit taller than the thing it was meant to hide, so at
   any normal viewing angle the player looked straight over everything and Mechanic 2 ran every frame
   while changing nothing. At 0.22 a Tall piece is 13 cm and a lane behind one is genuinely dark.
   New per-level `LevelDefinition.terrainHeightScale` (default 1) for maps that want to lean harder.
   **Height affects sight and silhouette only** — terrain carves the NavMesh by footprint, so this
   cannot sever a route.
2. **Deploy now requires line of sight.** `UnitDeploymentController.requireLineOfSight` (on by
   default). A point behind terrain from where the player is *actually standing* cannot be deployed
   onto. Opening a route means physically moving, not pressing a different button. This is the
   strongest single lever the game has and also **the highest-risk change in this pass** — see the
   flag in Section 10.
3. **The deploy reticle shows it**, turning red on an occluded point, and the HUD carries an
   "N CONTACTS UNSEEN · MOVE TO LOOK" line driven by `LineOfSightController.HiddenTargetCount`. The
   count deliberately does not say *where* — the drifting ghosts do that, badly and on purpose. A
   minimap would answer the question the leaning is supposed to answer.

**D4. Navigation.** There was genuinely no way out of a level: finishing one left the player on the
outcome card with only "Play Again", and abandoning one needed the OS back gesture. Added a top-bar
MENU button (with a confirm modal mid-match, skipped once the match is decided) and a MAIN MENU button
on the outcome card.

**D5. Level-select paging.** `levelsPerPage = 3` with prev/next and a "PAGE 1 / 2" label. Paged rather
than scrolled: a page is a definite place a player can return to, and it needs no scroll-inertia
tuning on a device being held up to a table.

**D6. Recorded-audio override layer.** Drop a WAV named after an `Sfx` value into
`Assets/Audio/Resources/Sfx/` and it replaces that sound's synthesis; delete it and synthesis returns.
Six new `Sfx` values for the classes (`MarksmanShot`, `TurretFire`, `HeavyDeploy`, `StealthDeploy`,
`WaveIncoming`, `ClassSelect`), each currently borrowing a neighbour's recipe so nothing is silent.
The shopping list with search terms, licence rules and length targets is `docs/SOUND_SHOPPING_LIST.md`.

**Three latent bugs found and fixed on the way**, all the same root cause — components still requiring
a `PlaneWithinPolygon` ARCore hit on a device that tracks fine but never promotes a plane. Deploy was
fixed for this on 2026-08-10; `RallyController` and `DeployReticle` had been left on the old path, so
on the Tab S6 Lite **every rally tap was being silently discarded and the precision ring never
appeared at all.** Both now intersect the board's own transform.

### Next, in order
12. **Device-test Pass D** — nothing above has been on a device. Specifically: is `requireLineOfSight`
    playable or infuriating, do the class silhouettes read at 5 cm, does the roster bar fit.
13. **Sound** — the override layer is in; it needs files. See `docs/SOUND_SHOPPING_LIST.md`.
14. **AI tuning + a second difficulty tier** — one profile, one device test. Needs real play, and now
    also needs re-tuning against a mixed-class opponent.
15. **Star ratings / per-level results** — `parTimeSeconds` and `parUnitsLost` are authored on every level and read by nothing.
16. **Monetization finish** — ✅ **complete as of 2026-08-10.** Play Console product live, RevenueCat registration done, signed release AAB verified, uploaded to Internal Testing, and a real license-tester purchase completed on device with the `pro` entitlement returning active. The entry requirement is met and this is no longer a submission risk.

    The follow-up that purchase exposed: the entitlement resolved correctly but **nothing visibly changed**, because the only Pro feature was a terrain recolour read once at spawn time, with no subscriber to `ProEntitlement.Changed`, and every level had `requiresPro: false`. Fixed the same day — see Section 7.
17. **Sentry overhaul**, and with it the deferred cluster below.
18. **Board elevation** (stretch) — **trigger: only after flat maps are confirmed solid on device AND the AI exists**, because NavMesh at tabletop scale has bitten this project twice and a NavMesh bug would otherwise be indistinguishable from an AI bug. Both conditions are now met.
19. **Ship** — demo video, icon, screenshots, `PROJECT_STORY.md`, repo cleanup.

### Deliberately deferred, and why — do not "helpfully" pick these up

The sentry system is getting an overhaul later, and these were all **sentry-balance** work that the
overhaul would invalidate. They are deferred *together*, on purpose:

- **`coverLaneMargin` → fraction of `BoardPlane.Length`.** Still absolute (5 cm real per side). Was
  flagged as the highest-value tuning lever; it stays flagged.
- **Re-authoring The Narrows** for a genuinely central corridor.
- **Two-origin garrison bucketing** in `MusterPhaseController` (player-side vs enemy-side chokepoints).
  Not needed while the AI is gated to a level whose defences are authored deliberately.
- **The three shipped levels are not to be touched** this round.

Also skipped by decision, not oversight: a dedicated pre-launch tip *screen* (the briefing field on
the level card already does the job), and star ratings/sound/board elevation (already sequenced above).

**Level validator:** re-run after editing levels. It checks off-board pieces, base overlap, garrison-anchor-vs-cap mismatch, and zero sight-blockers — it caught two real design bugs that would otherwise have shipped.

## 10. Known Risks & Gotchas

- **🚩 NEW 2026-08-11, HIGHEST RISK IN PASS D — `requireLineOfSight` on deployment is untested by a
  human.** It is the change most likely to be *correct in principle and miserable in practice*: with
  terrain now up to 22% of board length tall, a player holding the device at a natural angle may find
  a large fraction of the board undeployable and read it as the game being broken rather than as a
  rule. Mitigations already in: the deploy reticle turns red on an occluded point (so the rule is
  visible before the tap), and every refusal now raises a player-facing message on the prompt line
  instead of failing silently. **If it plays badly, the dial order is (1) drop
  `LevelDefinition.terrainHeightScale` on the offending level, (2) lower `NormalisedHeightForCategory`
  for Tall, (3) turn `requireLineOfSight` off — it is one Inspector checkbox on
  `SiegePhaseController`'s `UnitDeploymentController`.** Do not conclude the whole AR-intent direction
  failed from one bad session; the terrain height alone delivers most of the benefit.
- **🚩 NEW 2026-08-11 — the Turret is Pro-gated, and that is a judgement call worth revisiting.** It is
  the only defensive class, and the levels where defence matters are the AI levels where the player can
  lose. That edges toward pay-to-win. It was chosen because the alternative — gating an *attacking*
  class — is worse, and because level 05 is already Pro-gated so the pattern is established. The AI's
  own pick list deliberately excludes it, so a subscription can never make the opponent stronger.
  **Reversing it is one checkbox: `Assets/Units/Turret.asset` → `requiresPro`.**
- **AR plane detection is the proven weak point.** Mitigations in code: `minLockableArea` 0.02 m²; raycast fallback to estimated planes and feature points; throttled diagnostics every 2s (`adb logcat -d -s Unity:V | grep -A8 PlaneLock`). Escape hatch if it still fails: place the board at a fixed distance in front of the camera with no plane at all.
- **✅ RESOLVED — Unity's NavMesh `agentRadius` has a hard 0.05 m floor (it clamps on load, not a settings-rewrite bug), which severed all three levels at tabletop scale.** Measured 2026-08-08: any write below 0.05 silently reads back as exactly 0.05 — including on a brand-new agent type — while `agentHeight`/`agentClimb` on the same struct persist fine, which is what made it look like the file was being rewritten. A connectivity solve over the real levels showed the cost: The Narrows needed a 0.76 m board for a hairline path, Blind Spire 1.14 m, Two Lanes 0.72 m — all severed at the actual 0.60 m board.
  **Fix: scale the simulation, not the settings.** `ScrapSiege.Core.WorldScale.Scale = 5`, with the XR Origin at a matching uniform localScale, so one real metre is 5 Unity metres and the 0.05 floor costs **1.0 cm of real table**. Confirmed with a real Unity bake + `NavMesh.CalculatePath`: all three levels return `PathComplete`. Device-tested and accepted 2026-08-08.
  **The convention for any new distance value:** fractions of `BoardPlane.Length` need nothing; real metres go through `WorldScale.Metres()`; areas through `WorldScale.SquareMetres()`. Serialized fields stay authored in real metres, converting at the point of use. Three values deliberately do **not** convert — `minLockableArea` (`ARPlane.boundary` is plane-local, already true m²), `VisionTarget.sampleHeight` and `SentryArcVisualizer.surfaceOffset` (local space under an already-scaled parent) — and `UnitAnimator.stridesPerMetre` converts **inversely**.
  Two related bugs fixed in the same pass: `NavMeshSurface.minRegionArea` silently overrides the project setting with no opt-in flag (the live component sat at Unity's default `2 m²` on a 0.198 m² board — check the **component**, not just ProjectSettings, if a navmesh mysteriously has holes at small scale); and `SentryArcVisualizer` built its fan at the world-space `DetectionRadius` but parented it under the sentry's ~0.04 localScale, rendering the covered wedge at ~4% of its true range. Also worth remembering: the erosion that severs a map comes from runtime `NavMeshObstacle` carving, not the bake — the surface's layer mask only ever sees the bare ground rectangle, so "why didn't the bake cut this" is the wrong question.
- **`coverLaneMargin` was 0.25 → now 0.05 (fixed 2026-08-08), but is still an absolute real-world distance, unlike almost everything else in the project.** On the 33 cm-wide board this still makes a single wall's cover lane span a large fraction of the board width, which is very likely why the user found precision didn't feel like it bought much on their 2026-08-08 device test. Converting it to a fraction of `BoardPlane.Length` (matching `detectionRadiusFraction`, `arrivalDistanceFraction`, etc.) is flagged as the highest-value single tuning change — not yet done, deliberately, pending the user's confirmation.
- **🚩 The Narrows does not currently enforce its "one safe corridor" premise — a design gap found on the 2026-08-08 device test, not a bug.** Measured on a 0.60 m board: the wall spine sits left of centre, giving an 11.7 cm left lane and an 18.3 cm right lane, both wide relative to the ~1 cm agent radius. Combined cover lanes (rubble + wall) make the entire left lane immune, while the single sentry's 12 cm detection radius doesn't reach the right lane. Neither route is punished. Do not retune blind — the better fix is very likely re-authoring the level so the wall forms a genuinely central corridor with sentry coverage on both flanks, which the user independently proposed. Best done alongside or after the AI commander, since a reactive defender changes what "corridor" means.
- **🚩 "Units walk through rubble" is NOT a bug — do not make rubble solid.** Reported from the 2026-08-08 device test and it is working exactly as designed: `TerrainObjectSpawner.BlocksMovement` exempts `RubbleCover` deliberately, and plan.md's archetype table has always specified rubble as passable cover. That exemption *is* the fix for the severed-map bug — rubble used to carve, and on The Narrows the rubble line plus the wall spine left a **5 mm gap** that agent-radius erosion sealed completely. Re-adding the carve re-breaks all three maps. **It is an art problem:** the model reads as a solid heap, so walking through it looks wrong. The remodel brief is *low, scattered debris with visible gaps between chunks* — something a figure would obviously clamber over. Fixing the diagnosis, not the mechanic.
- **🚩 "Units walk through the watchtower" — PARTLY DIAGNOSED 2026-08-08, bake order RULED OUT.** A Play-mode probe (`CarveProbe`, since deleted) reproduced `TerrainObjectSpawner`'s obstacle construction exactly and measured both bake orders:

  | Case | centre walkable? | path through |
  |---|---|---|
  | A: obstacle spawned **before** `BuildNavMesh` (LevelBuilder's real order) | **No** | routes around, 4 corners |
  | B: obstacle spawned **after** `BuildNavMesh` | **No** | routes around, 4 corners |

  A third case was then run to close the last gap — the probe had used an unrotated, unit-scale parent, whereas `LevelBuilder` reparents each piece under `BoardRoot` (rotated by the player's twist, uniformly scaled to board length) via `SetParent(worldPositionStays: true)`:

  | Case | centre walkable? | obstacle world scale |
  |---|---|---|
  | C: reparented under a **rotated, 3×-scaled** BoardRoot | **No** | `(0.24, 0.21, 0.24)` — exactly the intended footprint |

  **Verdict: NavMeshObstacle carving is correct in every configuration, including the real one. The watchtower is not broken.** The leading hypothesis was wrong — the fourth plausible-sounding diagnosis this project has had disproved by measurement, which is precisely why the rule exists.
  **Most likely explanation for the original report:** the piece being walked through was *rubble*, which is passable by design and — until the 2026-08-08 remodel — looked exactly like a solid barrier, so the behaviour was correct and the art was lying. Two lesser possibilities if it recurs: a unit's visual mesh brushing a corner while its agent centre correctly routes around (NavMesh hugs corners tightly — the measured detour is only 0.028), or the garrison sentry, which `MusterPhaseController` snaps to the nearest walkable point to the watchtower's *centre* and therefore stands just outside the carve, reading as "inside" the tower. **Re-test on device with the new rubble before spending more on this.**
- **`SiegeUnit.prefab.health` was 10 in the serialized prefab while the C# default said 2** — a leftover 5x test buff (to tell real unit deaths apart from the since-fixed disappearing-unit bug) that made units survive 5 s of uncovered sentry fire instead of 1 s. **Now 3, and the value is derived rather than guessed:** at 1 damage per 0.5 s tick, 3 HP is a 1.5 s fight, which is the 1–2 s of readable combat Mechanic 6 is specced for. Changing tick rate or damage means re-deriving this.
- **Landscape only.** Canvases are 1920x1080. Portrait would need re-authoring, not a setting flip.
- **`EditorSceneManager.OpenScene` invalidates asset references loaded *before* it**, silently no-opping assignments. Always OpenScene first, then load assets, then assign.
- **`GameObject.Find` skips inactive objects** — `BoardRoot` is intentionally inactive until placed.
- **A scene created from `DefaultGameObjects` has no EventSystem**, so every UI button is silently dead. Needs `InputSystemUIInputModule` specifically.
- **Don't gate touch handling on counting held touches.** A quick tap can report `wasPressedThisFrame` while `isPressed` has already gone false — this silently swallowed the board-drop tap. Read `wasPressedThisFrame` directly.
- **`NavMeshAgent.remainingDistance` silently returns 0 when the agent has no path** (off-mesh, or `pathStatus` invalid) — not `Infinity`, not an error. That is indistinguishable from "arrived", and it made units deal base damage ~3 frames after spawning. Always require `hasPath && pathStatus == PathComplete` before trusting it.
- **Never sample a spawn position with a wider area mask than the agent will use.** Doing so can place a unit on a polygon its own mask excludes; the agent then reports off-mesh and the bug above fires. (Moot now that neither mode excludes areas, but the principle stands.)
- **Anything tuned in absolute metres is a hidden assumption about board size.** Levels are normalised and land on whatever table the player picked. `BoardPlane.Length` is the shared denominator — read it and scale. Already bitten: unit speed, arrival radius, sentry range, rally snap, deploy scatter, terrain heights.
- **Device blue-light filters warm-shift `adb screencap`.** Check `adb shell settings get system blue_light_filter` before chasing a colour bug.
- The full Unity/AR/NavMesh/RevenueCat gotcha list lives in the assistant's project memory.

### Submission checklist
- [x] App builds and runs on device
- [x] RevenueCat SDK integrated, Play Console product live, signed release build verified
- [x] **Real on-device purchase completed** (2026-08-10, license tester on Internal Testing) — the entry requirement is met
- [x] Pro actually does something visible: level 05 "The Foundry" is `requiresPro`, and the terrain palette now repaints live on entitlement change instead of only after a restart
- [x] Sound (procedurally synthesized — no audio files in the repo)
- [ ] Public open-source repo, cleaned up
- [ ] Demo video ≤2 min
- [ ] `PROJECT_STORY.md` finalised for Devpost

---

## 11. Pass E — device-report fixes, per-class art, combat FX (2026-08-13)

Ten items raised by the user's own on-device test of Pass D, plus the art work they implied. **All
Editor-verified; none of it has run on a phone.**

### Three were real bugs with a single identifiable cause each

1. **Selective Rally never worked.** `RallyScopeButton` in `ARTest.unity` was authored with
   `m_Interactable: 0`, and nothing in code ever set it true — so the one control that changes the
   rally scope could not be tapped, the label read "RALLY · ALL" forever, and every rally redirected
   the whole army. The scope *logic* was correct all along. Fixed in the scene **and** asserted in
   `HudController.HandleRallyScopeChanged`, which now forces `interactable = true` on every refresh:
   there is no state in the design where widening an order back to the whole army is illegal, so the
   correct value is asserted rather than trusted to scene authoring.

2. **Double click sound.** `UIButtonMotion.OnPointerDown` already plays `Sfx.UiTap` for *every*
   button in the game. `MainMenuController.GoToPage` and `HudController.ReturnToMainMenu` each played
   it again from the handler, firing two identical taps milliseconds apart. **Standing rule now:
   button-press audio belongs to `UIButtonMotion` and nowhere else**; a handler may only add a sound
   that is *different* from the tap. (The same trap was caught and avoided a second time this pass —
   `BaseHealth.TakeDamage` already plays `Sfx.BaseHit`, so the new base-impact FX is visual only.)

3. **"MAIN MENU" unreadable on the outcome card.** Its label carried `UITheme.TextOnAccent`
   (`#1A1206`, the dark ink meant to sit on amber) on a `SurfaceRaised` (`#232B36`) fill — dark on
   dark. Fixed as a *rule* rather than one value: a scene-wide pass repaints any button whose fill is
   a dark surface and whose label is `TextOnAccent` to `Stroke` fill + `TextPrimary` label. One
   button matched in `ARTest`, none in `MainMenu`.

### Two were absolute values that should always have been relative

4. **Units were a fixed real size at any board size** — the last absolute size left in the project.
   `SiegeUnit.Awake` multiplies the prefab by `WorldScale.Scale` and stopped there, so a trooper was
   5.2 cm whether the battlefield was fitted to a dining table or a side table; on a small board the
   troops stood taller than the cover they were meant to hide behind. `SiegeUnit.ApplyBoardScale` /
   `GarrisonSentry.ApplyBoardScale` now scale by `boardLength / referenceBoardLength` (0.60 m),
   clamped to 0.55–1.8, applied once, with the NavMeshAgent's radius/height following the model so a
   shrunk unit does not shoulder its neighbours through a corridor it visually fits.

5. **The "big red sphere" is the last-known-contact ghost, not a tracer** —
   `LineOfSightController`'s marker for a hidden enemy. It was `WorldScale.Metres(0.05f)`: the same
   height as the whole trooper it stood in for. Now measured from the target's own renderers
   (`ghostWidthFraction = 0.55`) and flattened to a disc (`ghostFlatten = 0.3`) so it reads as a mark
   on the map rather than as a projectile — which matters, because a player who thinks it is a bullet
   learns nothing about leaning to look.

### Health and damage are now 5x across the board

Every health, damage and base-health value was multiplied by 5 (units, both unit prefabs, the sentry
tick, `BaseHealth`, all five levels). **Nothing about the balance changed** — every value moved
together, so fight lengths are identical. The point is tuning headroom: at the old scale the smallest
expressible step was 1 damage against 3 HP, a 33% swing, so "slightly weaker" was not a number you
could write. The C# defaults were moved in lockstep with the serialized values, since
"code default changed, serialized value didn't follow" is this project's most repeated trap.

**Marksman, per the user's request:** reach `0.17 -> 0.34` of board length (double), damage `5 -> 4`
(−25%, which is only expressible *because* of the 5x rescale). ⚠️ **This is the most likely thing in
this pass to be over-tuned** — at 0.34 a marksman engages from a third of the board away and, with the
frontage rule, gets free damage the whole time its target walks in. If it dominates, raise
`attackTickSeconds` (0.75) before touching the reach: the reach is the class's identity.

### Per-class models — the primitives are gone

Five new low-poly models in `Assets/Models`, authored in Blender in the existing `ScrapSiege_v2`
collection: `Unit_Bulwark`, `Unit_Marksman`, `Unit_Saboteur`, `Unit_Turret`, `Sentry_Turret`. The
Trooper deliberately keeps the shared body — it is the baseline, and leaving it there keeps the
fallback path exercised.

Each reads as a distinct silhouette at the **near-overhead phone angle** (checked by render before
export, per this document's own standing lesson): Bulwark = a tower shield that doubles its apparent
width; Marksman = the only figure wider than it is tall, from a long rifle with a bipod; Saboteur =
short, hunched, with a bright cluster of satchel charges on its back; Turret = a squat legless machine
with twin barrels; Sentry = a tall tripod, single long barrel, and a bright radar fin so the player
can actually find it from across the table.

`UnitClass.modelPrefab` drives the swap and `UnitClassVisual.SwapInClassModel` performs it.
**Height is normalised, never assumed**: the shared trooper FBX imports at 1/100 scale with a −90° X
root rotation while these import 1:1 with none, so the swapped model is measured and scaled to the
height of the body it replaced. `UnitAnimator.Rebind()` is the other half — `Awake` runs at
Instantiate, before the class is applied, so without it the animator would keep driving the hidden
original and the new model would stand rigid while an invisible one marched inside it.

> **Blender gotcha, cost one full rebuild:** the builder set `matrix_parent_inverse` from
> `parent.matrix_world`, which Blender had not re-evaluated — so every child of a pivoted part was
> displaced by its parent's pivot. The marksman's rifle ended up pointing at the sky. Track pivots
> explicitly or call `view_layer.update()`; never read `matrix_world` of an object created in the
> same script without forcing a depsgraph update.
>
> **Export gotcha:** Blender object names are file-global, so renaming `Torso.001` to `Torso` while
> the original trooper still owns `Torso` silently hands one of them a `.001` suffix — and that name
> is baked into the FBX, which `UnitAnimator` looks parts up by. The exporter stashes *every* object
> to a unique placeholder name first, then names only the model being exported.

### Combat FX — melee had no visual at all

`CombatFx.Impact()` adds a pooled burst of shards at the point a blow lands, now called from **all
three** damage sources (melee, ranged, sentry) plus a larger one when a unit reaches a base. Before
this, a ranged unit drew a tracer and a sentry flashed its target, but two units meeting in the middle
of the board just stood next to each other while numbers moved invisibly — which on a phone reads as
loitering, not fighting. Deliberately pooled cubes rather than a `ParticleSystem`: sizes have to come
from board length, and this project has been burned repeatedly by absolute sizes surviving into a
rescaled world. Every burst is fired **from the code that applies the damage**, never from a parallel
"is something probably being hit" check, so the effect cannot show a blow that dealt nothing —
the same rule `SentryFireVisualizer` already follows.

### Paywall copy is now derived from the real gates

`ProFeatureCopy.BuildFeatureList()` reads `LevelCatalog` for `requiresPro` levels and `UnitRoster` for
`requiresPro` classes, and appends only the perks that have no asset to count (Veteran AI, saturated
palette). Both scenes' `PaywallController`s call it on every open. `MainMenu.unity` was still
promising "more cosmetic board themes" and "extra visual effect packs" — **two systems that do not
exist in the codebase** — while saying nothing about the Turret class. Centralising the string would
have fixed today's drift and not tomorrow's; deriving it means the next Pro level advertises itself.
The subtitle was also wrong ("nothing is locked away") and is now honest.

### RevenueCat: the paywall is custom, and that is now a deliberate choice rather than an unexamined one

The in-app paywall is this project's own Unity UI. RevenueCat supplies the price (Offerings), the
purchase, restore, sync and the entitlement; Google Play draws the payment sheet. That integration is
sound — `useRuntimeSetup` + explicit `DangerousSettings(true)`, focus-driven refresh, `SyncPurchases`
recovery, `ProductAlreadyPurchasedError` handling, and a decoupled `ProEntitlement` gate so gameplay
code never touches the SDK.

**What is NOT used: RevenueCat's own dashboard-designed Paywalls.** These *are* supported on Unity via
`com.revenuecat.purchases-ui-unity` (`PaywallsPresenter.Present()`), which is not currently in
`Packages/manifest.json` — only `com.revenuecat.purchases-unity` 7.4.1 is. The dashboard has **zero**
paywalls configured. Adopting it would mean remotely-editable copy without a rebuild, and access to
Experiments/A-B testing. The reasons to be careful: it renders a **native platform view over the Unity
view**, which in the match scene means a native activity on top of a live ARCore session (this project
has already lost a day to an ARCore session that would not resume), and it cannot be tested in the
Editor at all — device builds only.

**If adopted, the low-risk shape is: RevenueCat Paywalls from the main menu (no AR session live),
custom panel retained in-match.** Not done; awaiting a decision.

### Still not device-tested — the whole of Pass D *and* Pass E

### ⚠️ Adopting RevenueCat Paywalls means a MAJOR SDK upgrade — measured 2026-08-13, do not re-derive

The user chose "dashboard paywall on the main menu only" on 2026-08-13. It was **not started**, because
checking the registry first changed the size of the job:

- The project is on `com.revenuecat.purchases-unity` **7.4.1**.
- `com.revenuecat.purchases-ui-unity` (the package that renders dashboard paywalls) **has no 7.x
  line at all**. Lowest published version is **8.4.0**; latest is 9.7.0. Both packages ship from one
  monorepo on a shared release train, so there is no compatible pairing with 7.4.1.
- Its published dependency is literally `"com.revenuecat.purchases-unity": "file:../RevenueCat"` — a
  local path leaked out of RevenueCat's own build. UPM may fail to resolve that cleanly; the
  `.unitypackage` import route documented as "Option 2" is the fallback if it does.
- SDK 8.x is a **breaking** major bump that touches `MonetizationManager` directly
  (`PurchasesConfiguration.Builder`, `DangerousSettings`, callback signatures). Docs also note 8.0.0+
  requires Unity IAP 5.0.0+ *if* Unity IAP is used side by side — this project does not use Unity IAP
  (`com.unity.purchasing` is absent from the manifest), so that clause should not apply.

**Why it was not done anyway:** this is the stack that already cleared the hackathon's hard entry
requirement after a multi-day credentials/autosync/already-owned saga. A major version bump on it is a
scope and risk decision for the user, not an implementation detail. It is revertible (manifest.json
plus `MonetizationManager.cs` and `PaywallController.cs`), and there is time before 2026-09-30.

**What WAS done, and is zero-risk:** the paywall itself is built in the dashboard —
`pw1f70650488f14606`, attached to offering `ofrngf9d92167ba` (`default`, current), **unpublished
draft**, styled to match `UITheme` exactly and listing only the four real Pro features. If the SDK
upgrade is declined, archive it; nothing in the app references it.

---

## 12. Pass F — the Pass D+E device report (2026-08-13)

Five items from the user's on-device test of Passes D and E, plus the Pro cosmetic tier that item 1
implied. **All Editor-verified; none of it has run on a phone.**

### 1. The class models were shipping half-built — one root cause, in the FBX

Every class except the Trooper rendered as "a big top and two cube legs": the torso, both legs and
the base plate at full size, and *nothing else*. The parts that carry the whole silhouette — the
Bulwark's tower shield, the Marksman's rifle, the Saboteur's charges — were present but rendering at
**1/100 scale, piled at the figure's feet**.

**Cause: a non-identity `matrix_parent_inverse` in Blender.** Every part built as a child of `Torso`
carried a parent-inverse matrix that cancelled the torso's own offset. Blender composes that into the
world matrix, so the viewport and the reference render were both correct — but the FBX exporter does
not preserve it faithfully, and what landed in Unity was a local transform with a `0.01` scale on it.
The correlation was exact: every broken object had `mpi_is_identity == False`, every correct one
(including the whole Trooper, which was authored before that builder pass) had it identity.

**Fix, at the source.** The parent-inverse was baked into each object's own basis, top-down, and reset
to identity — measured as a **zero-change** operation on all 31 objects' world matrices before
re-exporting, so "the fix did not move anything" is a fact rather than a hope. All five FBXs were
re-exported and re-imported: **0 children at non-unit scale**, 12/12/11/7/10 renderers, feet on the
plane.

> **The rule this earns:** an object with a non-identity `matrix_parent_inverse` is not safe to
> export. Blender's viewport will not tell you — it composes the matrix and looks right. Bake it
> before export, and assert the world matrices did not move.

Two consequences worth carrying:

- **The new FBXs now import like the Trooper does** — root scale 100, root rotation −90° X — where
  the previous export imported 1:1 with no rotation. `UnitClassVisual.SwapInClassModel` was
  *overwriting* the instantiated model's rotation and scale with identity, which was harmless before
  and would now lay every model on its back (the exact bug this project shipped once already, in
  2026-08-08). It now keeps whatever the importer decided and only multiplies the magnitude.
- The abandoned first build of the five models was still sitting at the origin in the `.blend`,
  overlapping the live set — the "everything is built on top of each other" the user saw. Moved to a
  hidden `OLD_v2_scratch` collection (not deleted) and the live models laid out in a row.

### 2. Route variety — three layers, because one was not enough

A NavMeshAgent asked for a destination returns the single geometrically optimal corner path, and
every agent with the same start, destination and area costs gets *the same one*. Speed variance and
arrival jitter — the only variety the system had — change when a unit arrives and where it stops.
Neither could ever change the route, so an army advanced as one file.

1. **A per-unit approach lane** (`SiegeUnit.PickApproachLane`). Each unit picks a waypoint offset
   perpendicular to its advance, `laneSpreadFraction` (0.16 of board length) to either side, at a
   random 35–65% of the way there. Chosen as a *waypoint* rather than as steering noise, because
   noise produces wandering — a unit that wobbles reads as badly driven, not as having taken a flank —
   whereas one committed waypoint gives a real second route that still respects terrain, cover costs
   and chokepoints. **The onward leg is proven `PathComplete` before the lane is accepted**, or a
   unit could reach a pocket it cannot leave and stand there with `remainingDistance` reading 0,
   which is the trap this project already lost a session to.
2. **Per-unit cover cost** (`NavMeshAreas.ApplyCoverPreference(agent, preferCover, costMultiplier)`),
   rolled once per unit as `SiegeUnit.CoverCostVariance` (0.7–1.5x). Two units sent the same way now
   genuinely disagree about which side of an obstacle is cheaper. This can only change how
   *attractive* cover is, never whether it is passable, so no amount of it can disconnect a map.
3. **Spread avoidance priority** (30–70). Equal priorities make two agents each yield to the other,
   so a pair meeting in a corridor shuffles in lockstep instead of one simply passing.

The rally waypoint and the lane waypoint share one slot: a Rally **overwrites** a lane, because a
player order outranks the unit's own routing preference and honouring the lane afterwards would make
Rally look half-obeyed.

**The dial if it is still too uniform on device is `laneSpreadFraction`** on `SiegeUnit.prefab` /
`EnemySiegeUnit.prefab`. Raise it for a wider fan; 0 restores the old single-file behaviour exactly.

### 3. Enemy debris rendered magenta — a material-ownership bug, not a shader one

`renderer.materials` does **not** guarantee a fresh instance: if something already instanced that
renderer's materials, Unity hands back the same ones. On enemy and garrison units something always
has — `VisionTarget` instances them on its first fade — and `VisionTarget.OnDestroy` destroys them a
frame later when the dying unit is destroyed. The debris was left holding destroyed materials and
fell back to Unity's magenta error material. Player units were unaffected purely because they carry
no `VisionTarget`, which is exactly why it read as an "enemy-only" bug.

`UnitDeathEffect.PrepareMaterials` now copies from `sharedMaterials` explicitly, owns the copies, and
assigns them back through `sharedMaterials`. It also carries the captured alpha forward as the fade's
ceiling, so a half-revealed enemy's debris cannot pop to full opacity — which would have shown the
player more of a unit dead than they were allowed to see of it alive.

### 4. One tap was answered twice

`RallyController` and `UnitDeploymentController` each poll `Touchscreen.current.primaryTouch` from
their own `Update`, so an armed rally tap redirected the army *and* deployed a paid-for unit.

Checking `armed` alone does not fix it: Rally clears `armed` inside the very Update that consumes the
tap, so if deployment's Update ran second it would see `false` and deploy anyway — a bug that appears
or disappears with script execution order. `RallyController.ClaimsBoardTap` therefore records the
frame the tap was consumed, and the claim is staked *before* the tap is known to resolve, since a tap
that misses the board while armed still belongs to Rally.

### 5. Deployment is now restricted to the player's own lines

A unit could be dropped anywhere on the board including the square the enemy base stood on — which
made routes, cover and the whole Direct/Covered choice optional, because you could simply put a unit
on the objective. Deployment is now limited to `LevelBuilder.DeployZoneDepth` (**0.30** of board
length) forward of the player's own edge.

**The zone is drawn from the same number that enforces it.** `LevelBuilder` paints a `DeployZone`
band running from the front of the blue end zone to the limit, capped by a bright `DeployLimit` line,
and `UnitDeploymentController` / `DeployReticle` both read the depth back through
`LevelMatchController`. An invisible restriction reads as "my tap did nothing", which is this
project's most expensive class of bug, so the rule is visible three ways: the painted band, a reticle
that greys out past the limit (deliberately **not** the same red as a blocked sightline — one means
"move so you can see it", the other means "you can see it fine, it is not your ground"), and a
refusal message. The standing HUD prompt changed with it, since "Tap the table to deploy" had become
a lie.

Checked against all five levels: the strip from the player's edge to z = −0.20 is open on every one,
and the AI already spawned from its own end (`profile.spawnOffsetFraction` off the enemy base), so
the match is now symmetric rather than the player being uniquely restricted.

### 6. Veteran skins — the Pro tier gets something that is not power

Five `Unit_*_Veteran` models in `Assets/Models`, built as derivatives of the base models in the same
`ScrapSiege_v2` collection: plumes, gorgets and cloaks on the Trooper, a horned helm and shield
device on the Bulwark, a matching pauldron and bandolier on the Marksman, a second charge rack and
bracers on the Saboteur, a third barrel and frontal armour on the Turret. See Section 7 for why a
cosmetic perk specifically was worth building, and `docs/art/unit_lineup_veteran.png` for the
comparison.

**Each is trimmed to its base model's exact height** (measured, not eyeballed) because the swap
normalises height — a taller veteran would simply shrink its own body to compensate and read as a
downgrade. The Trooper's veteran is also the first model that class has ever had: its base look is
still the shared body, deliberately, so the fallback path stays exercised.

### What Pass F did NOT touch

The Marksman's reach (0.34) and everything else flagged in Pass E as likely over-tuned is unchanged —
the user's report raised no balance complaint, and changing balance in the same pass as five
structural fixes would make the next device test unattributable. `requireLineOfSight` is likewise
still on, with its dial-down order intact in Section 10.

## 13. Pass G â€” the Pass D+E+F device report (2026-08-13, later)

Five items from the user's on-device test of the combined D/E/F build. **Two of the five turned out to
be the same two root causes**, both confirmed by measurement in the live Editor rather than inferred.
Everything below compiles clean, every reference set this pass was read back non-null, and all five
items were verified by an automated Play-mode probe â€” **and none of it has run on a phone.**

### 1. The two model-stacking bugs â€” "2 models inside each other, one static while the other moves"

Reports 2 and 4 were one pair of causes, and both are the same *class* of mistake:
**a component cached renderers or child transforms at `Awake`, before the class model existed.**

- **`VisionTarget` was switching the hidden base body back on.** `Awake` caches
  `GetComponentsInChildren<Renderer>()`; `UnitClassVisual` then disables that body when it swaps in a
  class model; and `ApplyAlpha` writes `renderer.enabled = visible` straight across the stale list. So
  the first time the player *saw* an enemy, the shared trooper â€” spear included â€” was re-enabled
  inside the class model. `EnemySiegeUnit.prefab` carries `VisionTarget` and `SiegeUnit.prefab` does
  not, which is exactly why the user saw it on the **enemy** marksman and nowhere else. Fixed with
  `VisionTarget.RefreshRenderers()`, called from the swap: it keeps only renderers that are enabled at
  that moment, releases the material instances it made from the old body, and re-derives the sample
  points from the new silhouette.
- **`UnitAnimator.Rebind()` was re-finding the same hidden body.** Its lookup walks
  `GetComponentsInChildren<Transform>()` and takes the **first** name match â€” and `Visual` is child 0
  while `ClassModel` is appended last. Clearing the fields and re-running an unscoped lookup is not
  the same as re-scoping it, so it returned the identical `Torso`/`Leg_L`/`Leg_R`/`WeaponArm` every
  time. **No class model in the game had ever animated, on either team.** `Rebind(Transform root)` now
  takes the subtree to search; null keeps the whole-hierarchy behaviour for the legacy Fortify path.

> **Standing rule earned here:** *any component that caches renderers or child transforms at `Awake`
> is invalidated by the class-model swap and needs an explicit rebind hook.* `UnitTeamTint` and
> `UnitAnimator` already had one; `VisionTarget` did not, and `UnitMuzzle` (new) has one from birth.
> The hooks are deliberately all in one place at the end of `UnitClassVisual.SwapInClassModel`.

### 2. Line of sight on unit combat and on sentries

No damage source in the game had ever tested line of sight â€” the Marksman shooting through a wall was
simply the first place it became visible, at 0.34 of the board. Both `SiegeUnit` and `GarrisonSentry`
now call `LineOfSightController.HasClearLine` (already `public static` so the sight rule has exactly
one implementation) against `SiegeLayers.TerrainOccluderMask`. Measured mid-heights, never transform
origins â€” an origin-to-origin ray grazes the board slab and would report nearly everything blocked.
`SiegeUnit` re-checks on **every attack tick**, not only at acquisition, so walking behind a wall
genuinely stops the incoming fire.

**The sentry needed one extra move to not break Blind Spire.** Sentries spawn by sampling the NavMesh
at their anchor's centre, and the anchor carves a hole â€” so a sentry stands on the ground *beside* the
tower it garrisons, and a ground-level ray would be blocked by its own tower. `MusterPhaseController`
now passes the anchor's **measured top** as `GarrisonSentry.SetVantage`, and `SentryFireVisualizer`
fires its tracer from the same point so the rule and the picture cannot disagree.

WARNING â€” **deliberate consequence:** there is now a real sight shadow behind tall terrain, which
strengthens Mechanic 3 but means `SentryArcVisualizer` draws an arc that slightly over-promises. Left
that way on purpose â€” Mechanic 3's whole position is that the HUD never tells you *where* the safe
ground is. Worth revisiting with the sentry overhaul. The cover-lane immunity rule is untouched.

### 3. Combat reworked: reach-only targeting with capped focus fire

The user asked for combat to stop being strictly 1-v-1, and for a marksman's fire to stop dragging its
victim across the board. Both come from deleting the symmetric duel rather than adding a system.

**Reach-only.** A unit only ever targets what is **already inside its own reach**, and never chases.
Acquisition radius and attack range are the same number, so "close on the opponent" no longer exists
as a state. A Trooper under long-range fire keeps advancing and stops only when something enters its
own 0.06 â€” which is what finally makes *Bulwarks in front, Marksmen behind* a real formation.

**One-way, not a duel.** Being shot at neither engages you nor stops you. Each unit picks its own
target independently, which is what lets several work on one enemy while it fights only the one it
chose.

**Capped, because uncapped focus fire is a known-bad design.** The cap survives from the original
frontage rule â€” with unlimited focus fire, losses scale by Lanchester's square law and "deploy the
maximum number of units" becomes strictly correct â€” but its value moved from 1 to **3**, because a cap
of exactly one also made combined arms impossible.

| Dial | Value | What it does |
|---|---|---|
| `maxAttackersPerTarget` | 3 | a 4th attacker is refused and walks on toward the objective |
| `focusDamageFalloff` | 0.6 | attacker 1 deals 100%, 2 deals 60%, 3 deals 36% â€” **1.96x total, not 3x** |
| `immediateThreatBias` | 0.35 | an enemy already in range of hitting *me* scores as 3x closer |

The falloff is the direct answer to the "two Bulwarks screening five ranged units" cheese the user
raised. Note the reach-only rule defuses most of it by itself: five *Troopers* stacked behind a screen
are outside their own 0.06 reach and contribute nothing. The cap exists for **Marksmen**, who at 0.34
genuinely can all reach.

**Balance moved with it**, and both are first-guess dials, underived from play: Marksman
`attackTickSeconds` 0.75 â†’ **0.9** and Turret 0.6 â†’ **0.7**, because both now get genuinely
uncontested fire. **Reach and damage are untouched** â€” the reach is the Marksman's identity and the
tick rate is the correct dial, per the standing note from Pass E.

### 4. Tracers come from the model's own barrel

`SiegeUnit` used to fire from `transform.position + up * (engagementRadius * 0.12f)` â€” a height
derived from the class's *reach*, with no relationship to where the barrel actually is. On a 0.60 m
board that put the Turret's muzzle around 1.7 cm up a ~5.7 cm figure, i.e. visibly below its own gun.
New `UnitMuzzle` resolves the weapon part by name against what the FBXs actually expose (`Rifle`,
`BarrelL`/`BarrelC`/`BarrelR`, `Spear`/`Halberd`/`Blade`, then `WeaponArm`), and returns the
**forward-most point of that renderer's bounds**, recomputed per shot so it follows the animated arm.
Multi-barrel models alternate. Measured, never typed â€” the standing rule on this prefab.

### 5. Veteran skins rebuilt as genuinely different models

The user was right that the v2 Veterans were the base model plus greebles â€” `VET_Marksman` was
`MK_Marksman` with a bandolier, ghillie, hood crest and a second pauldron bolted on. The set is
rebuilt from scratch in a new `ScrapSiege_VET_v3` Blender collection (the v2 set **moved**, not
deleted, to a hidden `OLD_VET_v2`):

| Class | Veteran | The silhouette change |
|---|---|---|
| Trooper | **Standard Bearer** | back banner, plumed helm, cape, halberd instead of a spear |
| Bulwark | **Aegis** | slotted pavise + shoulder brace, wider planted stance |
| Marksman | **Longshot** | crouched, counterweighted long barrel, deployed bipod, mantle |
| Saboteur | **Infiltrator** | hunched â€” the only figure on the board that is not upright â€” twin blades, lit charge canisters |
| Turret | **Bastion** | armoured cupola with angled cheeks, twin-linked barrels on a recoil sled, radar vane |

**Colour without breaking the team read.** `U_Body` carries the team colour and that is the single
most important thing to read on a crowded board, so a paid skin cannot buy distinctiveness with body
colour. It buys it from three new never-tinted `MaterialSlots` roles â€” `U_Gold`, `U_Steel`, `U_Glow` â€”
tuned to sit clearly apart from the existing `U_Crest` amber and `U_Metal` grey. **A first attempt
that used gold as slabs rather than trim buried the team colour and made all five read as "beige
unit"; the rebuild keeps the team-coloured mass dominant**, and gives the Aegis a team-coloured pavise
precisely because it is a huge flat side-identifier.

Each Veteran is scaled to its base model's **exact** height (they import at 1.1832 / 0.995 / 0.96 /
0.795 / 0.72, matching part for part), since the swap normalises height and a taller veteran would
shrink its own body and read as a downgrade.

**Two export lessons, both worth not repeating:**
- Author with **every object rotation left at identity** and tilts baked into the vertices. That
  removes the rotated-parent and `matrix_parent_inverse` traps outright rather than auditing for them
  afterwards â€” audited anyway, and all 90 objects came back identity.
- **Zero the rig root's location before exporting.** The rigs are laid out in a row so they can be
  authored side by side, and that row offset was being written into the FBX root, so every Veteran
  imported four metres off the origin. Gameplay never saw it (`UnitClassVisual` overwrites the swapped
  model's `localPosition`) â€” which is exactly what makes it a trap rather than a visible bug.

### 6. Per-class motion â€” and the *other* half of "the attack animation seems weird"

A Marksman was playing the spear THRUST written for the Trooper: a figure holding a rifle lunging
forward to stab with it. `UnitClass.motion` / `.proMotion` (a `UnitMotionProfile`) now carry gait and
an `AttackStyle`, read by `UnitAnimator`:

- **Thrust** â€” body drives forward, arm swings through. Trooper, Saboteur.
- **Recoil** â€” body kicks *backward*, muzzle rises. **Marksman, Turret** â€” the only honest motion for
  something that fires a tracer rather than reaching its target.
- **Brace** â€” shield shoves out, body dips behind it. Bulwark.
- **Swipe** â€” a lateral arc with a matching torso twist. The Standard Bearer's halberd.

Veterans get their own gait (heavy plod for the Aegis, fast low scuttle for the Infiltrator, marching
cadence for the Standard Bearer). An unauthored profile falls back to the shared defaults, so adding
the field changed nothing until it was authored.

### Verified â€” and how

A temporary Play-mode probe (deleted afterwards) asserted all five, because these bugs all depend on
`Awake` having run and an Edit-mode check would have passed while the real thing stayed broken:

- Marksman / Turret / Trooper with Pro active: renderers visible from the **old body = 0**, and still
  0 **after `VisionTarget.ApplyTier(Full)`** â€” the exact regression, asserted rather than eyeballed.
- The animator's bound `Torso` is under `ClassModel` for all three.
- `HasClearLine` false through a terrain box, true beside it.
- The muzzle point falls inside the `Rifle` / `BarrelL` bounds, at the tip.
- Five attackers on one target: **3** locked on, `CanAcceptAttacker` false, the other two walked on.

### What Pass G did NOT touch

The sentry overhaul and its deferred cluster (`coverLaneMargin`, re-authoring The Narrows, garrison
bucketing) â€” only the line-of-sight change above touches sentries. `requireLineOfSight` on deploy is
still on with its dial-down order in Section 10. `docs/art/unit_lineup_front.png` is unchanged because
the base models are unchanged. The RevenueCat SDK 7.4.1 to 8.x decision is still open and still the
user's call.


---

## 14. Pass H — the Pass G device report + closed-testing polish (2026-08-14)

Three items from the user's device test of Pass G, plus a self-directed quality-of-life pass and an
abuse-hardening review of the purchase paths ahead of Play's mandatory 14-day / 12-tester closed test.
**Editor-verified with a Play-mode probe (measurements below). Not yet run on a phone.**

### 1. "I cannot see the sentry, and it does not look like it is shooting" — two separate causes

Reported as one symptom; it was two independent bugs, and the first had been shipping since authored
levels landed.

**Cause A — every sentry in the game stood INSIDE the tower it garrisoned.**
`MusterPhaseController` sampled the NavMesh at the chokepoint's own centre and relied on the piece's
`NavMeshObstacle` having carved a hole to push the sample outside. Two things were wrong with that:

- Carving is a **deferred runtime update**, and `SpawnGarrison` runs in the same synchronous call as
  `LevelBuilder.Build` and `NavMeshSurface.BuildNavMesh` — so no hole exists yet.
- Even if it had, the snap radius is **smaller than the footprint it would have to escape**. Measured
  on The Narrows: spire half-width `0.1725`, snap distance `0.04 x 3.0 = 0.12`. Every reachable
  sample is still inside the tower.

So the sentry sat inside an opaque, sight-blocking spire — invisible on screen *and* permanently
`Hidden` to `VisionTarget`, because its own tower blocked every ray from the camera. Generalise:
**never rely on NavMesh carving having been applied in the frame the obstacle was created.**

Fixed with `MusterPhaseController.TryFindStation`, which places the sentry **clear of the anchor's
measured footprint, on the side the attack comes from**, then re-checks the result two ways — against
the margined footprint, and against `SiegeLayers.TerrainOccluderMask` (the same mask
`LineOfSightController` uses, so "can the player see it" is answered by the layer that decides it).
A ten-bearing ring fallback means an odd layout gets a sentry standing somewhere unusual rather than
a level that silently ships with no defender.

*Measured after the fix:* sentry `0.44` clear of the spire's centre, inside **0** occluders, and
`HasClearLine` from a plausible player eye returns **true**. Rubble is deliberately still accepted as
a station — it is knee-high, blocks neither sight nor movement, and a sentry among rubble is fine.

**Cause B — `coverLaneMargin` was the last absolute distance in the project, and it made cover free.**
This is Open Question 3 from 2026-08-08, finally closed because the user was now reporting its
downstream symptom. At `0.05` real metres per side, on a board `1.65` units wide:

| lane | before | after (`0.035 x boardLength`) |
|---|---|---|
| wall barricade | 0.6725 (41% of board width) | **0.3825** |
| each rubble pile | 0.8105 (49%) | **0.5205** |

Cover previously blanketed everything from the left edge to just past centre, leaving one uncovered
strip `0.544` wide — of which the single sentry's `0.20 x L = 0.60` reach overlapped only `0.23`, i.e.
**14% of the board's width was both in range and shootable.** That is why it never fired.

`detectionRadiusFraction` also went `0.20 -> 0.26` (reach `0.78`), so the now-genuinely-open right
flank is actually watched. Net: roughly **a third of the board width** is in-range uncovered ground,
against 14% before. The level's briefing ("one safe corridor, one sentry watching it") is true for the
first time.

**Deliberately still not done:** re-authoring The Narrows so the wall is central (Open Question 1).
That is a layout redesign, not a tuning fix, and it should be judged against these new numbers first.

### 2. The Pro Turret was overpowered — three changes, and a timer is the real one

| | before | after |
|---|---|---|
| `attackDamage` | 5 | **4** |
| `attackTickSeconds` | 0.7 | **0.8** |
| effective DPS | 7.14 | **5.0** (-30%) |
| `cost` | 4 | **3** |
| `lifetimeSeconds` | infinite | **12** (1.8s of it a visible breakdown) |

Reach (`0.24 x L`) and health (40) are untouched: **reach is the class's identity and tick rate is the
correct dial** — the standing rule from Pass E.

**Why the timer matters more than the damage.** Reach-only combat means nothing chases and an
emplacement never advances, so a turret dropped off the AI's line of advance is a unit that literally
cannot be answered, and turrets accumulate for a whole match. It was already the one Pro perk that
touched power (Section 10 has always flagged it). A clock converts it from a permanent wall into a
**window of denial** — still the only thing that holds ground you cannot watch, but a cost you have to
keep paying. Cost dropped to 3 to keep it worth buying: the economy banks 1 scrap per 2s, so 12
seconds of turret is now six seconds of income rather than eight.

**It was already targetable by hostile units** — `SiegeUnit.FindTarget` walks the shared `Active` list
and filters only on team/alive/reach/line-of-sight, so an enemy that walks within its own reach of a
turret engages it exactly like any other unit. What was missing was any way to *make* something reach
it, which the timer supplies.

**The breakdown is animated, not a despawn.** A unit that blinks out is indistinguishable from a bug,
and this project has already lost a session to exactly that symptom (it is why `UnitDeathEffect`
exists). The last 1.8s are spent visibly failing: it stops firing, frees the attacker slot it held,
sags and topples on a per-unit random axis with an accelerating spark burst, and then comes apart
through the normal death debris — so a broken turret and a killed turret end the same way. Everything
is applied to `UnitClassVisual.ActiveModelRoot`, never to the unit's own transform, because the root
carries the NavMeshAgent and is written by the facing code.

An expiring turret is **not** counted in `MatchStats.PlayerUnitsLost`. It is a cost the player chose,
and counting it would make deploying the class a guaranteed hit to the efficiency star.

*Measured:* with the lifetime temporarily set to 3s/1.2s, breakdown began at exactly `t=1.80s` and the
unit was destroyed at exactly `t=3.00s`, with `PlayerUnitsLost` still 0.

### 3. The little square stand under every unit

Every model in `Assets/Models` is authored on a plate (`Base`, 4-8% of the model's height, roughly
half its width) so it stands up in Blender and in the `docs/art/` lineup renders. On a board the unit
is already standing on something, so the plate read as a chess-piece base sliding around under a
soldier.

`UnitClassVisual.ApplyGroundPlateRule` hides it — **for mobile classes only.** Emplacements keep
theirs, because a plate under a bolted-down turret reads as a mount, which is correct. Sentries need
no rule at all: `MusterPhaseController` never applies a class to them, so the code is never reached.

Three details that make it safe rather than a re-run of the model bugs of Pass F and G:

- The renderer is **disabled, not destroyed**. `Base` is a real FBX node other parts may be parented
  to, and `UnitDeathEffect` / `VisionTarget` both already skip disabled renderers, so they inherit the
  decision for free.
- It runs **after** the swap path's height normalisation and **before** its grounding. Measuring
  without the plate would silently scale every unit up by the plate's share of its height; grounding
  afterwards is what stops the body floating where the plate used to hold it up (the shared trooper's
  legs start a quarter of the way *up* its plate — measured, not assumed).
- The no-model path is handled too. The Trooper has no `modelPrefab` and used to return before any of
  this, so `ActiveModelRoot` and the plate rule now apply to the shared body as well.

*Measured on all five classes:* plate hidden on Trooper/Bulwark/Marksman/Saboteur, visible on Turret,
and every one of them reporting a foot offset of exactly `0.00000` from the table.

### 4. Star ratings — the cheapest content in the project

`parTimeSeconds` and `parUnitsLost` have been authored on **every** level since levels existed and
were read by absolutely nothing. The design work was already done; only the arithmetic was missing.

- `LevelDefinition.StarsFor` — one star for winning, one for beating par time, one for beating par
  losses. An unauthored par awards its star rather than withholding it.
- `MatchStats` — match clock and unit-loss counters. The clock starts when the **siege** goes live,
  not at scene load, so a player is never graded on how long their table took to scan.
- `LevelProgress` — best-ever stars per level in `PlayerPrefs`, written immediately (an AR app gets
  killed from the recents list, not closed).
- Shown in the outcome card's existing body label and appended to the level-select card titles, so
  **no scene authoring was needed** — the step most likely to half-ship here.

Defeat gets the same time/lost/killed summary with no rating. Rating a loss is either a consolation
star (dishonest) or zero stars (a second punishment for the same event).

### 5. Purchase-path abuse hardening

Reviewed after the user asked whether a bot could hammer the subscription call. Findings and fixes are
in `SECURITY.md` under the 2026-08-14 entry; the short version is that **entitlements were never
forgeable** (they come from RevenueCat's backend against a Google-signed receipt), but three call
sites were unbounded in *volume*: Restore had no in-flight guard at all, focus-driven customer-info
refreshes fired once per focus change, and the paywall refetched offerings on every panel open. All
three are now throttled, and `MonetizationManager` refuses overlapping store operations outright so a
future second screen cannot reintroduce it.

### What Pass H did NOT touch

`ARTest.unity` is **unmodified** (confirmed clean). The sentry overhaul proper, re-authoring The
Narrows, garrison bucketing, and `requireLineOfSight` on deploy are all still deferred. Sound files
are still outstanding. The RevenueCat SDK 7.4.1 to 8.x decision is still open and still the user's
call. The privacy policy was written immediately after Pass H — see Section 15 — but is **not yet
published**, which keeps it a hard Play Console blocker before the production track. See the README's
closed-testing checklist.

---

## Section 15 — The privacy policy (written 2026-08-14, NOT yet published)

Not a game system, but the one item on the closed-testing checklist that can stop publication
outright, so it is recorded here with the same care as a mechanic.

**Where it lives.** `docs/privacy/index.html` is the authoritative text — a self-contained static
page styled to match `UITheme`, intended for GitHub Pages serving from `main` / `/docs`, which gives
`https://mikhil-sec.github.io/ScrapSiege/privacy/`. `docs/PRIVACY_POLICY.md` is a summary plus the
maintenance rules. `docs/.nojekyll` makes the Pages deploy static and deterministic rather than
routing through Jekyll, and `docs/index.html` keeps the site root from being a bare 404 on a repo
that judges will open.

**Why it is written from the code rather than from a template.** A privacy policy is a public,
binding statement about software behaviour, and an inaccurate one is a Play policy violation rather
than a typo. Every claim in it was checked first:

| Claim | How it was verified |
|---|---|
| No analytics, ads, or crash SDK | `Packages/manifest.json` has no UGS/Firebase/ads package; a search across `Assets/**/*.cs` for analytics, ad and Firebase symbols returns nothing but an unrelated `RejectedTapLogInterval` |
| RevenueCat only ever sees an anonymous ID | `MonetizationManager` never calls `LogIn` or sets subscriber attributes; it configures with an API key alone, so the SDK generates `$RCAnonymousID:…` |
| Local storage is only stars + mute + a cached Pro flag | The only `PlayerPrefs` call sites are `LevelProgress` and `GameAudio` |
| The six declared permissions and what each is for | `SECURITY.md` section B's allowlist, itself verified against built artifacts with `aapt2 dump badging` |
| Camera data never leaves the device | No networking outside RevenueCat (the LAN code was archived with the two-player build); ARCore Cloud Anchors are not used |

**The coupling worth remembering.** Section 5 of the policy and `SECURITY.md` section B's permission
allowlist are the same list written twice — once as an internal control, once as a public promise.
A checklist item now exists in `SECURITY.md` to keep them in step, because the failure mode is silent:
a package added later that injects a permission would make the shipped policy understate the app
without anyone editing the policy.

**What is deliberately left for the user**, because none of it is code: creating the support address
and replacing the single `CONTACT_EMAIL_PLACEHOLDER` token, enabling GitHub Pages, pasting the URL
into Play Console, and completing the Data Safety form so its answers match the policy.
