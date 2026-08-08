# Scrap Siege

A single-player augmented-reality tabletop war game. Point your phone at any flat surface — a desk, a dining table, the floor — and a hand-designed battlefield is projected onto it at real scale. Then out-think an AI commander by physically moving around the board: lean in to place troops precisely, pull back to read the whole fight, and step around the table to see what a wall is hiding.

Built for **RevenueCat Shipaton 2026** (Next Gen / student track).

Full game design — mechanics, level format, AI behaviour, monetization, timeline — lives in [`plan.md`](plan.md). That's the source of truth for *why* things work the way they do; this file is the practical "what's built, how to run it" overview.

> **This project pivoted on 2026-08-07.** It was previously a two-player game where each player scanned real objects on their table as terrain, synced over a LAN. That implementation was completed and works as code, but AR plane detection could not reliably produce a lockable surface across floor, cushion table or dining table, which made the shared-board step too fragile to build a match on. It is preserved on the **`two-player-archive`** branch, not deleted. See `plan.md` Section 2 for the full reasoning.

## Current status

The full loop is **playable end to end on device**: main menu → level select → AR scan → lock a plane → place the board (tap, drag, pinch, twist) → confirm → the level builds → siege.

> **⚠️ One open blocker (2026-08-08).** `ProjectSettings/NavMeshAreas.asset` has `agentRadius: 0.05`, which on a 33 cm-wide board erodes wider than the gaps the levels are built around — so The Narrows currently bakes as two disconnected halves and deployed units cannot reach the enemy base. It must be set to ~`0.012` **with the Unity Editor closed** (it silently reverts otherwise). Details in `plan.md` Section 10 and `CLAUDE.md`.

### Working today

- **Main menu + level select** — level cards generated from a `LevelCatalog`, so shipping a new map is a data file and nothing else. Pro-locked levels route to the paywall through the same decoupled entitlement gate gameplay uses.
- **Scan phase** — AR plane detection (ARCore/AR Foundation), no depth sensor required. Detected surfaces get a boundary outline and the HUD reports their real polygon area. **Lock This Table** commits to one plane and freezes detection so ARCore can't keep growing or drifting it. Scan failures log a diagnostic every 2s so a grey Lock button is explainable rather than mysterious.
- **Board placement** — tap to drop the board, drag to move, pinch to resize, twist to rotate, then Confirm. A footprint outline shows the real board shape before you commit. Raycasts fall back through plane → estimated plane → feature point, because plane detection is this project's known weak spot.
- **Authored levels** — `LevelDefinition` ScriptableObjects in normalised board space (0–1 coordinates), so one map projects onto any table at any size. Three ship today; each is built to force one mechanic.
- **Vantage** — camera height above the board continuously drives deploy precision, with a ring drawn on the table showing your current scatter radius *before* you tap.
- **Rally** — redirect every deployed unit through a new lane, available only when you're physically pulled back far enough to see the whole board.
- **True line of sight** — graded (hidden / faint / partial / full) from three raycasts per enemy, with last-known-position ghosts that *drift* along the target's last heading, so stale intel is wrong rather than merely old.
- **Sentry arcs** — garrison units cover a 150° facing arc drawn on the table, so walking around the board to reach their blind side is a real tactic.
- **Route variety** — **Direct** (shortest line; threads a cover corridor only when that genuinely is shortest) vs **Covered** (detours to hug cover and avoid garrison fire). Driven by *per-agent* NavMesh area costs, so both modes can reach anywhere and differ only in what they value.
- **Low-poly art** — Blender-built trooper and terrain models with procedural march/bob/lean/lunge animation driven from navigation velocity. No rigging.
- **RevenueCat integration** — SDK installed, dashboard configured, one real Pro-gated cosmetic working in Editor Play Mode.

### Not built yet

- **AI commander** — the biggest gap. Rally and the whole "react to a threat" layer are inert without an opponent.
- **Player base + Lose condition** — the player base spawns, but nothing damages it yet.
- Star ratings, sound, board elevation, more levels, demo video and submission assets.

### Known limitations

- Only **one generic unit type** — no combat variety yet.
- **AR plane detection is this project's proven weak point.** See `plan.md` Section 10.
- **Direct-vs-Covered routing has not been retested** since the cover-lane width was corrected (it used to blanket the whole board).
- **RevenueCat purchases don't complete on a real device yet** — the product only exists on the Test Store; real builds go through Google Play Billing, which needs a Play Console product.
- **Landscape only.** All canvases are authored at 1920×1080; portrait would need re-authoring, not a settings flip.

## Tech stack

- **Engine:** Unity 6000.5.6f1 (URP, Linear colour space), AR Foundation 6.5 + Google ARCore XR Plugin
- **Pathing:** Unity AI Navigation (NavMeshSurface/Agent/Obstacle), baked at runtime once the board is placed
- **Art:** Blender low-poly, exported as FBX, animated procedurally in code rather than rigged
- **Platform:** Android-first, landscape (no depth sensor required); iOS planned later via cloud macOS CI
- **Zero AI/ML** anywhere in the shipped app. The AI commander is rule-based game AI — explicit thresholds and utility scoring, no learned model — which is a deliberate design constraint, not a shortcut. Unity's on-device inference package is removed from the manifest for this reason.

## Project structure

```
Assets/Scripts/                 - ScrapSiege.Runtime.asmdef (named assembly, referenced by Tests)
  Core/     - SiegeLayers (terrain-occluder layer), BoardPlane (the one source of board height)
  Levels/   - LevelDefinition (authored map, normalised space), LevelBuilder, LevelCatalog,
              BoardPlacementController (tap/drag/pinch/twist), LevelMatchController (match flow)
  Vantage/  - VantageMath (pure, unit-tested), VantageController, DeployReticle
  Vision/   - VisionMath (pure, unit-tested), VisionTarget, LineOfSightController, MaterialFx
  AR/       - PlaneLockController (scan, lock/rescan, one-plane rule, diagnostics),
              PlaneOutlineVisualizer
  Terrain/  - TerrainObjectSpawner (model or primitive + NavMesh carving + CoverLane tagging),
              TerrainArchetype, TerrainObjectData, NavMeshAreas, TerrainClassifier +
              FortifyInputController (the legacy scanning flow - kept as a fallback, not primary)
  Siege/    - resource economy, unit deployment, UnitAnimator (procedural), RallyController,
              Muster/garrison, GarrisonSentry + SentryArcVisualizer, BaseHealth, win condition
  UI/       - HudController (the one place that decides what the HUD shows), MainMenuController,
              UITheme palette, SafeAreaFitter, UIButtonMotion
  Monetization/ - ProEntitlement.cs only: the decoupled Pro-status gate gameplay code reads
Assets/Monetization/            - deliberately OUTSIDE ScrapSiege.Runtime.asmdef (the RevenueCat
                                  SDK ships with no asmdef). MonetizationManager, PaywallController
Assets/Levels/                  - the three authored levels + LevelCatalog
Assets/Models/                  - Blender-exported low-poly FBX (trooper + 4 terrain pieces)
Assets/Editor/BuildScript.cs    - one Android build entry point (menu, MCP, or batchmode)
Assets/UI/Generated/            - procedurally generated 9-sliced sprites, tinted from UITheme
Assets/Tests/EditMode/          - TerrainClassifier, VantageMath and VisionMath tests
Assets/Prefabs/                 - GroundQuad, DummyBase, SiegeUnit, GarrisonUnit, PlaneOutline
Assets/Scenes/MainMenu.unity    - title + level select (no AR; build index 0)
Assets/Scenes/ARTest.unity      - the AR match scene (build index 1)
```

## Running it

1. Open in Unity 6 with Android Build Support installed.
2. Build & Run to an ARCore-capable Android device. (Tested on a Samsung Galaxy Tab S6 Lite and a Galaxy A56 — neither has a depth sensor.) `Scrap Siege > Build Android APK (Development)` in the menu bar does the same thing and writes `build/ScrapSiege.apk`.
3. In the app: **PLAY** → pick a level → sweep the camera over a surface until the readout shows an area → **Lock This Table** → tap to drop the board, fit it with drag/pinch/twist → **Confirm Board**.
4. In the siege: tap the table to deploy. Lower the device for precise placement, raise it for the overview and to unlock **Rally**.

There's no Editor Play Mode AR simulation configured — testing happens on-device. When something behaves wrong on the phone, pull logs before theorising:

```
adb logcat -d -s Unity:E AndroidRuntime:E          # exceptions
adb logcat -d -s Unity:V | grep -A8 PlaneLock      # why the Lock button is grey
adb logcat -d -s Unity:V | grep -iE "LevelBuilder|selected|Rally"
```

`adb` is bundled with Unity at
`C:\Program Files\Unity\Hub\Editor\6000.5.6f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe`.

## Branches

- **`main`** — the single-player game (current direction).
- **`two-player-archive`** — the abandoned two-device LAN build: Netcode for GameObjects over direct LAN, UDP host discovery, two-point shared board alignment, server-authoritative replicated terrain with half-of-table ownership, and a host-authoritative networked siege with per-player bases and resources. Complete as code, never validated end-to-end on hardware.

## License / submission context

Public repo maintained for the RevenueCat Shipaton 2026 Next Gen track submission. See `plan.md` for full submission requirements and timeline.
