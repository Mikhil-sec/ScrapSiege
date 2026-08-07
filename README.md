# Scrap Siege

A single-player augmented-reality tabletop war game. Point your phone at any flat surface — a desk, a dining table, the floor — and a hand-designed battlefield is projected onto it at real scale. Then out-think an AI commander by physically moving around the board: lean in to place troops precisely, pull back to read the whole fight, and step around the table to see what a wall is hiding.

Built for **RevenueCat Shipaton 2026** (Next Gen / student track).

Full game design — mechanics, level format, AI behaviour, monetization, timeline — lives in [`plan.md`](plan.md). That's the source of truth for *why* things work the way they do; this file is the practical "what's built, how to run it" overview.

> **This project pivoted on 2026-08-07.** It was previously a two-player game where each player scanned real objects on their table as terrain, synced over a LAN. That implementation was completed and works as code, but AR plane detection could not reliably produce a lockable surface across floor, cushion table or dining table, which made the shared-board step too fragile to build a match on. It is preserved on the **`two-player-archive`** branch, not deleted. See `plan.md` Section 2 for the full reasoning.

## Current status

### Working today (carried over from the pre-pivot build)

- **Scan phase** — AR plane detection (ARCore/AR Foundation), no depth sensor required. Detected surfaces get a pulsing blue boundary outline and the HUD reports their real polygon area. **Lock This Table** commits to exactly one plane, freezes detection so ARCore can't keep growing or drifting it, and hides every other plane; **Rescan** reverses that. Scan failures log a diagnostic every 2s (plane count, per-plane alignment/area/tracking state) so a grey Lock button is explainable rather than mysterious.
- **Siege phase** — a ground area and a destroyable base are placed, a NavMesh is baked (terrain carves obstacles automatically), a resource economy ticks, and tapping deploys a unit that paths toward the base. Deploy has two modes — **Direct** (shortest open route) and **Covered** (detours through cover to avoid garrison fire) — a genuine risk/speed trade-off driven by NavMesh area costs, not a cosmetic difference.
- **Muster** — free stationary garrison units auto-spawn at chokepoint/Watchtower terrain, and only damage units *not* standing in a cover lane.
- **Win condition** — the base has real HP, units damage it on arrival, destroying it ends the match.
- **RevenueCat integration** — SDK installed, dashboard configured (entitlement, offering, product), and one real Pro-gated cosmetic (a saturated terrain palette) works in Unity Editor Play Mode. See `plan.md` Section 7 for IDs and current status.
- **Designed HUD** — status card carrying the current step and next instruction, a bottom bar that cross-fades between phases, a scrap counter chip, and modal cards for the paywall and victory screen. One palette, one procedurally generated 9-sliced sprite, `Scale With Screen Size` at 1080×1920, inset to `Screen.safeArea`.

### Not built yet — the new direction

Everything specific to the single-player pivot. In `plan.md` Section 8 order:

- **Board placement** — tap to drop the map on the locked surface, drag/rotate/scale, confirm. Replaces the terrain-scanning Fortify phase.
- **`LevelDefinition` ScriptableObjects** — authored maps in normalised board space, so adding content needs no code.
- **AI commander** — rule-based opponent with its own base and economy.
- **Player base + Lose condition** — still missing; nothing currently damages the player back.
- **Vantage mechanic** — camera height drives deploy precision vs. field of view.
- **True line of sight** — enemies revealed only when actually visible from the camera, with last-known-position markers.
- Level select, star ratings, terrain art, VFX/sound, demo video and submission assets.

### Known limitations

- Only **one generic placeholder unit type** — no combat variety yet.
- Terrain visuals are flat coloured primitives, not art — intentional placeholder, deferred to the polish pass.
- **AR plane detection is this project's proven weak point** and has failed on real surfaces more than once. See `plan.md` Section 9 for mitigations and the escape hatch.
- **RevenueCat purchases don't complete on a real device yet** — the product only exists on the Test Store; real builds go through Google Play Billing, which needs a Play Console product. Code and dashboard are otherwise done.

## Tech stack

- **Engine:** Unity 6000.5.6f1 (URP), AR Foundation 6.5 + Google ARCore XR Plugin
- **Pathing:** Unity AI Navigation (NavMeshSurface/Agent/Obstacle), baked at runtime once the board is placed
- **Platform:** Android-first (no depth sensor required); iOS planned later via cloud macOS CI
- **Zero AI/ML** anywhere in the stack. The AI commander is rule-based game AI — explicit thresholds and utility scoring, no learned model — which is a deliberate design constraint, not a shortcut.

## Project structure

```
Assets/Scripts/                 - ScrapSiege.Runtime.asmdef (named assembly, referenced by Tests)
  AR/       - PlaneLockController (scan, lock/rescan, one-plane rule, scan diagnostics),
              PlaneOutlineVisualizer, CloudAnchorManager stub
  Terrain/  - TerrainObjectSpawner ("Cartoonify" placeholder visuals + NavMesh carving +
              CoverLane tagging), TerrainArchetype, TerrainObjectData, NavMeshAreas,
              TerrainClassifier + FortifyInputController (scanning - slated for replacement
              by authored-map placement, see plan.md Section 8 Week A)
  Siege/    - phase handoff, resource economy, unit deployment/pathing, win condition,
              Muster/garrison, BaseHealth
  UI/       - HudController (the one place that decides what the HUD shows), UITheme palette,
              SafeAreaFitter, UIButtonMotion
  Monetization/ - ProEntitlement.cs only: the decoupled Pro-status gate gameplay code reads
Assets/Monetization/            - deliberately OUTSIDE ScrapSiege.Runtime.asmdef (the RevenueCat
                                  SDK ships with no asmdef). MonetizationManager, PaywallController
Assets/UI/Generated/            - procedurally generated 9-sliced rounded-rect + circle sprites,
                                  tinted per use from UITheme (no external UI art dependency)
Assets/Tests/EditMode/          - TerrainClassifier tests (Window > General > Test Runner)
Assets/Prefabs/                 - GroundQuad, DummyBase, SiegeUnit, GarrisonUnit, PlaneOutline
Assets/Scenes/ARTest.unity      - the one scene the current loop runs in
```

## Running it

1. Open in Unity 6 with Android Build Support installed.
2. Open `Assets/Scenes/ARTest.unity`.
3. Build & Run to an ARCore-capable Android device (tested on a Samsung Galaxy A56 and an Honor phone with no depth sensor).
4. Sweep the camera over a surface until the readout shows a mapped area, then tap **Lock This Table**.
5. Current build then runs the legacy scan-based flow; the authored-map placement replacing it is the next task (`plan.md` Section 8, Week A).

There's no Editor Play Mode AR simulation configured — testing happens on-device. When something behaves wrong on the phone, pull logs before theorising:

```
adb logcat -d -s Unity:V | grep -A8 PlaneLock    # why the Lock button is grey
adb logcat -d -s Unity:E                          # exceptions
```

`adb` is bundled with Unity at
`C:\Program Files\Unity\Hub\Editor\6000.5.6f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe`.

## Branches

- **`main`** — the single-player game (current direction).
- **`two-player-archive`** — the abandoned two-device LAN build: Netcode for GameObjects over direct LAN, UDP host discovery, two-point shared board alignment, server-authoritative replicated terrain with half-of-table ownership, and a host-authoritative networked siege with per-player bases and resources. Complete as code, never validated end-to-end on hardware.

## License / submission context

Public repo maintained for the RevenueCat Shipaton 2026 Next Gen track submission. See `plan.md` for full submission requirements and timeline.
