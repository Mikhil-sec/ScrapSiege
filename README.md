# Scrap Siege

An augmented-reality tabletop battle game — two players build their battlefield out of whatever's actually sitting on their table (a mug, a book, a phone stand), then fight a real-time skirmish across it. No two matches are ever on the same map, because no two tables have the same junk on them.

Built for **RevenueCat Shipaton 2026** (Next Gen / student track).

Full game design — mechanics, terrain classification rules, monetization, timeline — lives in [`plan.md`](plan.md). That's the source of truth for *why* things work the way they do; this file is the practical "what's built, how to run it" overview.

## Current status

**Working end-to-end today:**
- AR plane detection (ARCore/AR Foundation), validated on Android with no depth sensor required.
- **Fortify phase** — tap two corners of a real object on the live camera view, pick a height (Short/Medium/Tall), and the app classifies it via rule-based geometry (no ML) into one of five terrain archetypes: Wall/Barricade, Spire/Chokepoint, Rubble Cover, Plain Obstacle, or Watchtower (bonus tier for the tallest object on the board). A colored placeholder primitive is spawned over the real object.
- Undo last object / delete-mode (tap a placed object to remove it) during Fortify.
- A blue outline showing the actual boundary of the detected table plane.
- **Siege phase** — once Fortify is done, a synthetic ground area and a stand-in "enemy base" are placed, a NavMesh is baked over the table (terrain objects carve out obstacles automatically), a simple resource economy ticks over time, and tapping the table deploys a unit that paths around your terrain toward the base.

**Known limitations (by design, for now):**
- Only **one generic placeholder unit type** exists — no combat, no unit variety yet.
- The "enemy base" is a fixed stand-in, not a real opponent — **two-device play (Cloud Anchor sync) hasn't been built yet**, so this is currently a solo/practice loop only.
- Terrain visuals are flat colored primitives, not real art — intentional placeholder per the design doc's "Cartoonify" step, deferred to a later polish pass.
- Pathing currently always takes the shortest route; there's no "safe route through cover vs. fast route around the outside" behavior yet.

**Not started yet:**
- RevenueCat SDK integration / in-app purchase (a hard submission requirement).
- Two-device Cloud Anchor cross-device sync.
- Camera-height trade-off mechanic.
- Auto-garrison-on-chokepoints (Muster phase, per plan.md).
- Demo video, app icon, screenshots, and other Devpost submission assets.

## Tech stack

- **Engine:** Unity 6 (URP), AR Foundation + Google ARCore XR Plugin
- **Pathing:** Unity AI Navigation (NavMeshSurface/NavMeshAgent/NavMeshObstacle), baked at runtime once Fortify ends
- **Platform:** Android-first (no depth sensor required — manual box-tagging works on any ARCore-capable phone); iOS planned later via cloud macOS CI
- **Zero AI/ML** anywhere in the stack — terrain classification is pure computational geometry (bounding box, height, aspect ratio), by explicit design constraint

## Project structure

```
Assets/Scripts/
  AR/       - AR session/plane helpers (PlaneOutlineVisualizer, CloudAnchorManager stub, debug HUD)
  Terrain/  - Fortify phase: classification, spawning, corner-tap input handling
  Siege/    - Siege phase: phase handoff, resource economy, unit deployment, unit pathing
Assets/Prefabs/
  GroundQuad, DummyBase, SiegeUnit  - runtime-instantiated during Siege
Assets/Scenes/
  ARTest.unity - the one scene the whole current loop runs in
```

## Running it

1. Open the project in Unity 6 with Android Build Support installed.
2. Open `Assets/Scenes/ARTest.unity`.
3. Build & Run to an ARCore-capable Android device (tested on a Samsung Galaxy Tab and an Honor phone with no depth sensor).
4. Point the camera at a table, wait for the blue plane outline to appear, tap two corners of a real object + pick its height, repeat for more objects, tap **Done**, then tap the table to deploy units.

There's no Editor Play Mode AR simulation configured yet — testing happens on-device.

## License / submission context

Public repo maintained for the RevenueCat Shipaton 2026 Next Gen track submission. See `plan.md` for full submission requirements and timeline.
