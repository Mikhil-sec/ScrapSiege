# Scrap Siege — Design & Build Plan

*Last updated 2026-08-08.*

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

## 5. The AI Commander (NOT YET BUILT — the biggest gap)

Rule-based, explicit thresholds and utility scoring, no learned model.

- **Symmetric economy** — the AI ticks resources on the same schedule; difficulty comes from decision quality, not cheating.
- **Behaviour loop** (~1s tick): score candidate actions — reinforce a threatened lane, push the weakest-defended approach, hold for a bigger wave — take the best.
- **Difficulty tiers:** resource rate, reaction delay, willingness to commit, unit mix.
- **Readability matters.** Telegraphing a push beats optimal play. This is a demo-video game as much as a strategy game.

Its units should get `VisionTarget` so line of sight applies to them.

## 6. Levels

Hand-authored `LevelDefinition` ScriptableObjects in **normalised board space** (x, z each 0..1; `boardAspect` sets width; **z = 0 is always the player's edge**), so one layout projects onto any table size. `LevelBuilder` maps them onto a `BoardRoot` whose **localScale is the board's length in metres**.

**Shipped levels** (`Assets/Levels/`), each built to force one mechanic:

| # | Name | Teaches |
|---|---|---|
| 01 | The Narrows | Precision — one cover corridor watched by a sentry |
| 02 | Blind Spire | Line of sight — two sentries hidden behind a centre spire |
| 03 | Two Lanes | Rally — a spine splits the field; lanes rejoin only deep |

> **Authoring gotcha:** `MusterPhaseController` fills garrison slots in **terrain array order**. In Blind Spire the two watchtowers must come *before* the spire, or the spire steals a sentry and the map's premise breaks. A validator (see Section 9) checks this.

**Placement flow:** lock a plane → tap to drop the board → drag to move, pinch to scale, twist to rotate → Confirm. A `LineRenderer` footprint outline shows the board before it is built. Raycasts fall back through `PlaneWithinPolygon | PlaneEstimated | FeaturePoint` deliberately, given this project's plane-detection history.

## 7. Monetization (RevenueCat)

**Already built and working — do not break it.**

- **Project:** "ScrapSiege" (`proj3a523262`). Entitlement `pro`.
- **Test Store** (`appda5538b8e2`) — product `scrap_siege_pro_monthly` ($2.99/mo) in the `default` offering's `$rc_monthly` package. Works in Editor Play Mode only; a real Android build always goes through Google Play Billing.
- **Play Store** (`appa37d9670f8`, `com.mikhilnaika.scrapsiege`) — app entry created, **no product yet**, blocked on a Google Play Console account. Reserved naming: product `scrap_siege_pro`, base plan `monthly` → store identifier `scrap_siege_pro:monthly`.
- **Code:** `Assets/Monetization/` sits deliberately **outside** `ScrapSiege.Runtime.asmdef` because the RevenueCat SDK ships no asmdef. `Assets/Scripts/Monetization/ProEntitlement.cs` is the decoupled gate gameplay reads.

**What Pro unlocks:** level packs (`LevelDefinition.requiresPro`, already wired through `LevelCatalog.IsUnlocked`), cosmetic board themes, and the saturated terrain palette (`TerrainObjectSpawner.ProColorForArchetype`, shipped).

## 8. Art pipeline

**Static low-poly models from Blender + procedural animation in Unity — deliberately not rigged.** Units are ~5cm on a real table viewed through a phone, so rig deformation is invisible while gross motion (leg swing, bob, lunge) is not.

- `SiegeTrooper.fbx` — 11 parts with real joint pivots (hips, shoulder, waist), upper body parented to the torso. `UnitAnimator` drives it from NavMeshAgent velocity, keyed to **distance travelled** so a stalled unit stops marching instead of moon-walking.
- `Terrain_Wall/Spire/Watchtower/Rubble.fbx` — each authored to **fill a unit cube with base at y = 0**, so the spawner's existing footprint scaling needed no maths changes.
- `TerrainObjectSpawner` falls back to primitives for any archetype with no model assigned, and overrides model materials with the archetype colour (that colour is both the gameplay signal and where the Pro palette lives).

**Lesson:** always render the **near-overhead** view before accepting a unit model. The first trooper looked fine from the front and read as an ambiguous blob from the actual gameplay angle; fixed with a bright forward-pointing crest.

## 9. Remaining work, in order

**Playable end-to-end and accepted by the user's own device test on 2026-08-08** (post world-scale fix). That test raised design gaps that are deliberately NOT fixed yet — see the callout below before touching levels, cover or balance.

1. **AI commander v1** — the biggest gap. Rally, "react to a threat", and difficulty tuning are all inert without it.
2. **Design tuning from the 2026-08-08 device test** (small, high-value, do before or alongside the AI commander):
   - Revert `SiegeUnit.prefab.health` from 10 back to 2 — a leftover test buff that makes units feel unkillable.
   - Make `coverLaneMargin` a fraction of `BoardPlane.Length` instead of a fixed real-world distance — likely the single biggest lever for making precision/vantage actually matter.
   - Re-author The Narrows so the wall forms a genuinely central corridor with sentry coverage on both flanks, instead of the current layout where both sides are viable, unpunished routes. Best done once the AI commander exists, since a reactive defender changes what "corridor" means.
3. **Player base + Lose condition** — the player base spawns already; nothing damages it yet.
4. **Level select polish** — star ratings, per-level results.
5. **Board elevation** (stretch) — a raised plateau making line of sight genuinely 3D. **Trigger: only after flat maps are confirmed solid on device AND the AI exists**, because NavMesh at tabletop scale has bitten this project twice and a NavMesh bug would otherwise be indistinguishable from an AI bug.
6. **Polish** — sound, VFX, HUD pass.
7. **Monetization finish** — Play Console product, Internal Testing, real on-device purchase.
8. **Ship** — demo video, icon, screenshots, repo cleanup.

**Level validator:** re-run after editing levels. It checks off-board pieces, base overlap, garrison-anchor-vs-cap mismatch, and zero sight-blockers — it caught two real design bugs that would otherwise have shipped.

## 10. Known Risks & Gotchas

- **AR plane detection is the proven weak point.** Mitigations in code: `minLockableArea` 0.02 m²; raycast fallback to estimated planes and feature points; throttled diagnostics every 2s (`adb logcat -d -s Unity:V | grep -A8 PlaneLock`). Escape hatch if it still fails: place the board at a fixed distance in front of the camera with no plane at all.
- **✅ RESOLVED — Unity's NavMesh `agentRadius` has a hard 0.05 m floor (it clamps on load, not a settings-rewrite bug), which severed all three levels at tabletop scale.** Measured 2026-08-08: any write below 0.05 silently reads back as exactly 0.05 — including on a brand-new agent type — while `agentHeight`/`agentClimb` on the same struct persist fine, which is what made it look like the file was being rewritten. A connectivity solve over the real levels showed the cost: The Narrows needed a 0.76 m board for a hairline path, Blind Spire 1.14 m, Two Lanes 0.72 m — all severed at the actual 0.60 m board.
  **Fix: scale the simulation, not the settings.** `ScrapSiege.Core.WorldScale.Scale = 5`, with the XR Origin at a matching uniform localScale, so one real metre is 5 Unity metres and the 0.05 floor costs **1.0 cm of real table**. Confirmed with a real Unity bake + `NavMesh.CalculatePath`: all three levels return `PathComplete`. Device-tested and accepted 2026-08-08.
  **The convention for any new distance value:** fractions of `BoardPlane.Length` need nothing; real metres go through `WorldScale.Metres()`; areas through `WorldScale.SquareMetres()`. Serialized fields stay authored in real metres, converting at the point of use. Three values deliberately do **not** convert — `minLockableArea` (`ARPlane.boundary` is plane-local, already true m²), `VisionTarget.sampleHeight` and `SentryArcVisualizer.surfaceOffset` (local space under an already-scaled parent) — and `UnitAnimator.stridesPerMetre` converts **inversely**.
  Two related bugs fixed in the same pass: `NavMeshSurface.minRegionArea` silently overrides the project setting with no opt-in flag (the live component sat at Unity's default `2 m²` on a 0.198 m² board — check the **component**, not just ProjectSettings, if a navmesh mysteriously has holes at small scale); and `SentryArcVisualizer` built its fan at the world-space `DetectionRadius` but parented it under the sentry's ~0.04 localScale, rendering the covered wedge at ~4% of its true range. Also worth remembering: the erosion that severs a map comes from runtime `NavMeshObstacle` carving, not the bake — the surface's layer mask only ever sees the bare ground rectangle, so "why didn't the bake cut this" is the wrong question.
- **`coverLaneMargin` was 0.25 → now 0.05 (fixed 2026-08-08), but is still an absolute real-world distance, unlike almost everything else in the project.** On the 33 cm-wide board this still makes a single wall's cover lane span a large fraction of the board width, which is very likely why the user found precision didn't feel like it bought much on their 2026-08-08 device test. Converting it to a fraction of `BoardPlane.Length` (matching `detectionRadiusFraction`, `arrivalDistanceFraction`, etc.) is flagged as the highest-value single tuning change — not yet done, deliberately, pending the user's confirmation.
- **🚩 The Narrows does not currently enforce its "one safe corridor" premise — a design gap found on the 2026-08-08 device test, not a bug.** Measured on a 0.60 m board: the wall spine sits left of centre, giving an 11.7 cm left lane and an 18.3 cm right lane, both wide relative to the ~1 cm agent radius. Combined cover lanes (rubble + wall) make the entire left lane immune, while the single sentry's 12 cm detection radius doesn't reach the right lane. Neither route is punished. Do not retune blind — the better fix is very likely re-authoring the level so the wall forms a genuinely central corridor with sentry coverage on both flanks, which the user independently proposed. Best done alongside or after the AI commander, since a reactive defender changes what "corridor" means.
- **`SiegeUnit.prefab.health` is 10 in the serialized prefab; the C# default is 2.** A leftover 5x buff from early testing (to tell real unit deaths apart from the since-fixed disappearing-unit bug), which made units survive 5s of uncovered sentry fire instead of 1s and masked how lethal exposure actually is. Reset to 2 before judging any combat balance.
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
- [x] RevenueCat SDK integrated (real IAP pending Play Console)
- [ ] Public open-source repo, cleaned up
- [ ] Demo video ≤2 min
- [ ] `PROJECT_STORY.md` finalised for Devpost
