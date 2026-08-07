# Scrap Siege

An augmented-reality tabletop battle game — two players build their battlefield out of whatever's actually sitting on their table (a mug, a book, a phone stand), then fight a real-time skirmish across it. No two matches are ever on the same map, because no two tables have the same junk on them.

Built for **RevenueCat Shipaton 2026** (Next Gen / student track).

Full game design — mechanics, terrain classification rules, monetization, timeline — lives in [`plan.md`](plan.md). That's the source of truth for *why* things work the way they do; this file is the practical "what's built, how to run it" overview.

## Current status

**Working end-to-end today:**

- **Scan phase** — AR plane detection (ARCore/AR Foundation), validated on Android with no depth sensor required. Mapped surfaces are drawn with a pulsing blue boundary outline and the HUD reports their real polygon area (`0.34 m²`). When the table looks right, **Lock This Table** commits to exactly one plane: detection freezes so ARCore can't keep growing or drifting the board mid-match, and every other plane is hidden. **Rescan** reverses it and returns to detection (it also clears any terrain already placed, since that terrain was positioned against the discarded plane). The locked plane's outline turns solid amber.
- **Fortify phase** — tap two corners of a real object on the live camera view, pick a height (Short/Medium/Tall), and the app classifies it via rule-based geometry (no ML) into one of five terrain archetypes: Wall/Barricade, Spire/Chokepoint, Rubble Cover, Plain Obstacle, or Watchtower (bonus tier for the tallest object on the board). A colored placeholder primitive is spawned over the real object. Corner taps are restricted to the locked plane.
- Undo last object / delete-mode (tap a placed object to remove it) during Fortify.
- **Muster phase** — free stationary garrison units auto-spawn at chokepoint/Watchtower terrain once Fortify ends, rewarding good terrain-building.
- **Siege phase** — a synthetic ground area and a stand-in "enemy base" (with real hit points) are placed, a NavMesh is baked over the table (terrain objects carve out obstacles automatically), a resource economy ticks over time, and tapping the table deploys a unit that paths toward the base. Deploy has two modes — Direct (fast, open route) and Covered (slower, hugs cover terrain to avoid garrison fire) — a genuine risk/speed trade-off, not just a cosmetic path difference.
- **Win condition** — the base has real HP, deployed units damage it on arrival, and destroying it stops the match and fires a win event.
- **RevenueCat integration** — SDK installed, dashboard configured (entitlement, offering, product), and one real Pro-gated cosmetic feature (a second terrain color palette) works correctly in Unity Editor Play Mode. See `plan.md` Section 6 for exact IDs and current status.
- **Designed HUD** — one status card carrying the current step and the next instruction, a bottom action bar that swaps between the three phases, a scrap counter chip, and modal cards for the paywall and the victory screen. Built on a single palette and one generated 9-sliced rounded-rect sprite (`Assets/UI/Generated/`), scaled with `Scale With Screen Size` at 1080×1920 and inset to `Screen.safeArea` so nothing sits under a notch or gesture bar.

**Known limitations (by design, for now):**

- Only **one generic placeholder unit type** exists — no combat, no unit variety yet.
- The "enemy base" is a fixed stand-in, not a real opponent — **two-device play (Cloud Anchor sync) hasn't been built yet**, so this is currently a solo/practice loop only. No Lose condition exists yet for the same reason.
- Terrain visuals are flat colored primitives, not real art — intentional placeholder per the design doc's "Cartoonify" step, deferred to a later polish pass.
- **RevenueCat purchases don't yet complete on a real Android device** — the product only exists on RevenueCat's Test Store app, and real device builds always go through actual Google Play Billing, which needs a real Google Play Console product. That account/product setup is in progress; the code and dashboard side are otherwise complete.

**Not started yet:**

- Two-device Cloud Anchor cross-device sync.
- Camera-height trade-off mechanic.
- Demo video, app icon, screenshots, and other Devpost submission assets.

## Tech stack

- **Engine:** Unity 6 (URP), AR Foundation + Google ARCore XR Plugin
- **Pathing:** Unity AI Navigation (NavMeshSurface/NavMeshAgent/NavMeshObstacle), baked at runtime once Fortify ends
- **Platform:** Android-first (no depth sensor required — manual box-tagging works on any ARCore-capable phone); iOS planned later via cloud macOS CI
- **Zero AI/ML** anywhere in the stack — terrain classification is pure computational geometry (bounding box, height, aspect ratio), by explicit design constraint

## Project structure

```
Assets/Scripts/                    - ScrapSiege.Runtime.asmdef (named assembly, referenced by Tests)
  AR/       - Scan phase: PlaneLockController (lock/rescan, one-plane rule), PlaneOutlineVisualizer,
              CloudAnchorManager stub
  Terrain/  - Fortify phase: classification, spawning, corner-tap input handling, NavMeshAreas
  Siege/    - Siege phase: phase handoff, resource economy, unit deployment/pathing, win condition,
              Muster/garrison, BaseHealth
  UI/       - HudController (the one place that decides what the HUD shows), UITheme palette,
              SafeAreaFitter, UIButtonMotion
  Monetization/ - ProEntitlement.cs only: the decoupled Pro-status gate gameplay code reads
Assets/Monetization/                - deliberately OUTSIDE ScrapSiege.Runtime.asmdef - see plan.md
                                       Section 6 for why. MonetizationManager.cs, PaywallController.cs
Assets/UI/Generated/                - procedurally generated 9-sliced rounded-rect + circle sprites,
                                       tinted per use from UITheme (no external UI art dependency)
Assets/Tests/EditMode/              - automated tests for TerrainClassifier (Window > Test Runner)
Assets/Prefabs/
  GroundQuad, DummyBase, SiegeUnit, GarrisonUnit  - runtime-instantiated during Siege
  PlaneOutline - the ARPlaneManager plane prefab
Assets/Scenes/
  ARTest.unity - the one scene the whole current loop runs in
```

## Running it

1. Open the project in Unity 6 with Android Build Support installed.
2. Open `Assets/Scenes/ARTest.unity`.
3. Build & Run to an ARCore-capable Android device (tested on a Samsung Galaxy Tab and an Honor phone with no depth sensor).
4. Sweep the camera over a table until a blue outline appears and the readout shows a mapped area, then tap **Lock This Table** (**Rescan** later if it grabbed the wrong surface).
5. Tap two corners of a real object + pick its height, repeat for more objects, tap **Done**.
6. Pick **Direct** or **Covered**, then tap the table to deploy units at the enemy base.

There's no Editor Play Mode AR simulation configured yet — testing happens on-device.

## License / submission context

Public repo maintained for the RevenueCat Shipaton 2026 Next Gen track submission. See `plan.md` for full submission requirements and timeline.
