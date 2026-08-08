# Scrap Siege — Project Context for Claude Code

You are helping build **Scrap Siege**, a Unity mobile AR game for the RevenueCat Shipaton 2026 hackathon (Next Gen student track). Read this file fully before writing any code. It lives at the root of the Unity project and should be read at the start of every session.

## What this project is

A **single-player** augmented-reality tabletop battle game. The player points their phone at any flat surface, drops a hand-designed battlefield onto it at real scale, fits it to their table, and then fights a real-time skirmish across it.

The hook is that **your phone's physical position is a tactical resource**: lean in close for precise unit placement, pull back for a commander's overview *and* the ability to issue a board-wide Rally order, and physically move around the table to see behind cover. That is the thing a flat-screen game cannot copy, and it is what this project is built around.

Full design detail is in `plan.md`. **Read it before starting work; it is the source of truth for game design.**

### ⚠️ Two directions were abandoned — do not restore them

1. **Two-device play** (Netcode LAN, shared board alignment) — cut 2026-08-07. AR plane detection could not reliably produce a lockable surface across floor/cushion table/dining table, so co-location was unshippable. Preserved on the `two-player-archive` branch (commit `5d05fc3`).
2. **Terrain scanning from real objects** (the "Fortify" phase) — replaced by authored maps. The code still exists and still works, and `PlaneLockController` falls back to it if `boardPlacement` is unassigned, but **it is not the main path**. Do not make it primary again.

## Hard constraints — do not violate these

- **Zero AI/ML features in the app.** No on-device ML, no object recognition, no generative anything, no learned models.
  - **This does not ban the AI opponent.** The "AI commander" is rule-based/utility-scored — ordinary game AI with explicit thresholds. That is fine and is the point. What is banned is *machine learning*.
  - `com.unity.ai.inference` (Unity's on-device neural network runtime) has been **removed** from the manifest for this reason.
  - **`com.unity.ai.assistant` must stay.** It is editor-only tooling that ships nothing into the APK, and it is what provides the Unity MCP server. Removing it breaks the Editor connection (this already happened once).
  - `com.unity.ai.navigation` is NavMesh pathfinding, not ML. Keep it.
- **Must integrate the RevenueCat Unity SDK** powering at least one real IAP. Already built and working — do not break it.
- **Original mechanics only.** Originality rests on **vantage**, **true line of sight**, and the **Rally** order. Protect those.

## Development environment

- **OS:** Windows, no Mac. Intel Arc integrated graphics, 32GB RAM.
- **Unity:** 6000.5.6f1, URP, AR Foundation 6.5 + ARCore. **Linear colour space.** VS Code as script editor.
- **Orientation: LANDSCAPE.** All canvases are authored at a **1920x1080** reference with `matchWidthOrHeight = 1`. Autorotation to portrait is disabled. If a change needs portrait, the canvases must be re-authored — not just the setting flipped.
- **Test devices:** a Samsung Galaxy Tab S6 Lite (SM-P619, ARCore-supported) and a Galaxy A56 phone. Neither has a depth sensor, so plane detection and plane raycasts are the only spatial input.
- **AR plane detection is the known weak point.** Anything that *requires* a large, high-quality plane is a risk.
- **Platform:** Android-first. iOS is a stretch via cloud macOS CI — never suggest steps needing local Xcode.
- **Version control:** Git. `main` = the single-player game. `two-player-archive` = the abandoned two-device build.

## Tooling available to you

- **Unity Editor MCP** (`unity-mcp`): `Unity_RunCommand` compiles and runs arbitrary C# in the live Editor. Prefer it over walking the user through manual clicks. Caveats:
  - Every script edit triggers a domain reload that drops the connection for ~30–60s ("Unity not detected"). Poll `Logs/Editor.log` size until it stops growing, then retry. **Batch script edits before Editor work.**
  - The sandbox blocks `System.Reflection` and cannot reference NUnit attributes.
  - It wraps code in `namespace Unity.AI.Assistant...`, so a bare `Image` resolves to `Unity.AI.Image`. **Always write `UnityEngine.UI.Image` in full.**
  - `result.Log` does plain `{0}` substitution only — no format specifiers — and **silently drops null args**, so a literal `{0}` in the output means that argument was null. Useful tell.
- **Blender MCP**: builds and exports the low-poly art. See `plan.md` Section 8.
- **RevenueCat MCP**: configure the dashboard directly.
- **adb** at `C:\Program Files\Unity\Hub\Editor\6000.5.6f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe`. **Use it.** Several bugs here were invisible in the Editor.
- **`/deploy` skill** (`.claude/skills/deploy/`) — the build → install → launch → logcat loop.

## Working agreement (set 2026-08-08)

**The user runs builds and on-device tests themselves and reports back.** Do not build/install/launch unattended. Pull logs with adb *after* they report something, or if they explicitly ask for a deeper investigation over USB.

Useful log commands once they report an issue:
```
adb logcat -d -s Unity:E AndroidRuntime:E          # errors only
adb logcat -d -s Unity:V | grep -A8 PlaneLock      # plane detection diagnostics
adb logcat -d -s Unity:V | grep -iE "LevelBuilder|selected|Rally"
```

## How to work in this project

- Write real C# into the correct `Assets/` subfolders.
- **Verify, don't assume.** Compile-check with `dotnet build` on a globbed copy of `ScrapSiege.Runtime.csproj` when the Editor is busy — it is a genuine full compile in ~8s.
- **Read back every Inspector reference you set** and assert it is non-null. Silent nulls have caused several real bugs here.
- Guard every Inspector-wired reference with a null check that logs loudly.
- Subscribe to `UnityEvent<T>` **in code**, not via the Inspector dropdown.
- Check the gotchas in `plan.md` Section 10 before debugging AR/NavMesh/URP/UI symptoms.

## Current state (2026-08-08, end of the art-overhaul + pathing-bugfix session)

### ✅ RESOLVED 2026-08-08 — the NavMesh blocker, and why the fix is a world scale

**Fixed by scaling the simulation.** `ScrapSiege.Core.WorldScale.Scale = 5` and the XR Origin carries a uniform localScale of 5, so **one real metre = 5 Unity metres**. The engine's 0.05 m agent-radius floor now costs **1.0 cm of real table** instead of 5 cm.

**Verified with a real Unity bake and a real `NavMesh.CalculatePath`** (Edit-mode probe: level geometry rebuilt from the `LevelDefinition` assets as Not-Walkable `NavMeshModifier` volumes, baked through the scene's own `NavMeshSurface`) on a 0.60 m board:

| Level | Result | Path |
|---|---|---|
| The Narrows | `PathComplete` | straight down the centre corridor, 0.516 m |
| Blind Spire | `PathComplete` | 5 corners, 0.563 m vs 0.516 straight |
| Two Lanes | `PathComplete` | 5 corners, 0.640 m vs 0.516 straight |

**The rule when touching any distance in this project:** anything expressed as a fraction of `BoardPlane.Length` needs nothing — it scales for free. Anything in **real metres** must go through `WorldScale.Metres()`. Areas use `WorldScale.SquareMetres()`. Serialized fields stay authored in real metres so the Inspector stays readable; conversion happens at the point of use.

**Three things deliberately do NOT convert** — each would be a bug if "fixed":
- `PlaneLockController.minLockableArea` — `ARPlane.boundary` is plane-LOCAL, so `PolygonArea` already returns true real m². Converting it would demand a ~70 cm table before Lock lit up.
- `VisionTarget.sampleHeight` and `SentryArcVisualizer.surfaceOffset` — applied in local space under an already-scaled transform, so the parent applies the conversion. Converting would double-apply it.
- `UnitAnimator.stridesPerMetre` — scales **inversely** (`/ Scale`), because the unit now covers 5× more Unity metres for the same real distance.

Set `WorldScale.Scale` back to 1 (and the XR Origin with it) to return to true 1:1 — and to a severed NavMesh.

<details>
<summary>The original diagnosis, kept because the reasoning matters</summary>

### Unity will not accept an agent radius below 0.05 m

**The long-hunted `agentRadius: 0.012` target is impossible. Stop trying to set it.** Measured directly in the Editor on 2026-08-08:

- Writing `0.001 / 0.006 / 0.012 / 0.02 / 0.03 / 0.04 / 0.049` all read back as **exactly 0.05**. Writing `0.06 / 0.1 / 0.5` sticks. So **0.05 m is a hard engine floor, not a pin.**
- It is not the YAML being rewritten: the file on disk still said `0.012` while `NavMesh.GetSettingsByID(0)` returned `0.05`. **Unity clamps on load.** That is why every previous in-Editor attempt "silently reverted" — and why `agentHeight`/`agentClimb` on the same struct persisted fine (they have no such floor; height accepted 2.0).
- **A brand-new agent type via `NavMesh.CreateSettings()` is clamped identically**, so extra agent types are not an escape route.
- `NavMeshBuildSettings.ValidationReport()` is the tool that surfaces this class of problem — it also reported *"The voxel size must be larger than 0.0100"*, so the `cellSize: 0.004` plan was invalid too.

**Consequence, measured against the real level assets** (rasterised connectivity solve, player base → enemy base, erosion = agent radius; all level geometry scales linearly with board length):

| Level | Max agent radius per 1 m of board | Board length needed for 0.05 | Status at 0.60 m |
|---|---|---|---|
| The Narrows | 0.0655 | **0.76 m** | SEVERED |
| Blind Spire | 0.0439 | **1.14 m** | SEVERED |
| Two Lanes | 0.0699 | **0.72 m** | SEVERED |

Those are *hairline* thresholds — a zero-width path. Genuinely playable corridors need roughly double. **All three levels are unreachable at tabletop scale, and no amount of NavMesh tuning fixes it.**

**The fix is to scale the simulation, not the settings.** Set the XR Origin's uniform scale to `S` so real-world metres map to `S` Unity metres; the agent radius floor of 0.05 Unity m then costs only `0.05/S` real metres. `S = 5` gives a 1 cm effective radius, which matches the trooper's ~1.2 cm base — physically right, and it restores the whole original design intent. Scaling the XR Origin transform is the sanctioned mechanism (cf. `XRBodyScale`, which exists to set "the uniform local scale of the originTransform").

Everything already derived from `BoardPlane.Length` follows automatically. What must be multiplied by `S` by hand:
- `VantageController.leanedInHeight` / `pulledBackHeight` (0.20 / 0.65) — deliberately absolute real-ergonomics values, so they are exactly what breaks.
- `PlaneLockController.minLockableArea` (0.02 m²) — an **area**, so it scales by `S²`.
- Camera far clip, and any other constant still expressed in real metres.

⚠️ **Do not set a finer `cellSize` while radius is 0.05 — that bakes zero triangles** (verified the hard way).

**Also fixed in the same pass:** `NavMeshSurface.GetBuildSettings()` ends with `buildSettings.minRegionArea = minRegionArea;` — an **unconditional** overwrite from the component's own serialized field, with no "override" toggle (unlike voxel/tile size). The surface in `ARTest.unity` carried Unity's default `m_MinRegionArea: 2` — **2 m² on a 0.198 m² board** — so the project-level value had never been in effect and any isolated region was eligible to be culled outright. Now `0.001` on both.

Note the erosion reaches gameplay through **runtime `NavMeshObstacle` carving**, not the bake: the surface's `m_LayerMask` is layer 6 only, so the bake yields the plain ground rectangle and each terrain piece carves its hole at runtime, expanded by the agent radius. Reasoning about "why didn't the bake cut this" misleads.

Current settings, validated clean (0 issues): `agentRadius 0.05` (floor), `agentHeight 0.05`, `agentClimb 0.02`, `agentSlope 30`, auto voxel `0.01`, `minRegionArea 0.001`.

</details>

### Fixed and verified this session

- **Art overhaul** — scrapyard-miniature terrain on base plates, new `Terrain_PlainObstacle` and `Base_HQ` models, opaque board slab with blue/red end zones, colour-coded bases, the oversized world-space base captions retired, garrison sentries now red troopers instead of grey spheres.
- **The FBX axis-correction bug** — code was overwriting the importer's -90° X root rotation, which laid *every* model on its side and mangled proportions. One root cause behind both "the objects are unreadable" and "the trooper spawns lying on its back".
- **Trooper "splits in half then vanishes"** — `UnitAnimator` bob/lunge were authored in metres but applied in the model's local space, whose scale changed ~54× on re-export: 88 mm of bob on a 52 mm unit. Now authored as fractions of the unit's own height, resolved at `Awake` from measured bounds ÷ real `lossyScale`.
- **"Win fires but I never see them arrive"** — two independent causes: `arrivalDistance` was a fixed 0.15 m (a *quarter* of a 0.60 m board), and `NavMeshAgent.remainingDistance` silently reads 0 when the agent has no path, which is indistinguishable from arriving. Both fixed.
- **Everything absolute made board-relative** via `BoardPlane.Length` — unit speed (2 s → 16 s to cross), arrival/jitter radii, sentry range, rally + muster snap, deploy scatter, terrain heights.
- **Route variety reworked** to per-agent area cost (see `plan.md` Mechanic 4) — Direct is no longer *barred* from cover, matching the design intent.
- **Rubble no longer carves a NavMesh obstacle** — plan.md always specified it as passable cover; carving it made the "safe corridor" solid.
- **`coverLaneMargin`** serialized value was still 0.25 despite the C# default being 0.05 — fixed and verified on disk.

### Open design questions from the 2026-08-08 device test — DO NOT "fix" these blind

The user tested on device, accepted the build, and committed. Their test raised four things that are **design gaps, not bugs**, and were deliberately left alone. Read this before touching levels, cover or balance.

1. **The Narrows does not enforce its "one safe corridor" premise.** Units legitimately reach the enemy base down *both* sides of the wall. Measured on a 0.60 m board (half-width 16.5 cm, both bases centred at x = 0): the wall spine sits at x −4.8…−1.8 cm, so the left lane is 11.7 cm wide and the right lane 18.3 cm — both enormous next to a ~1 cm agent radius. Cover lanes (laid only by `RubbleCover` and `WallBarricade`) blanket everything from the left edge to x = +3.2 cm, and the single sentry — on the spire at x ≈ −5.6 cm, `detectionRadius = 0.20 × boardLength` = 12 cm — reaches only to x = +6.4 cm. **So the left route is immune (it is all cover lane) and the right route is mostly out of range.** The user's own suggestion is arguably the better level: two walls forming a *central* corridor with sentries outside it.
2. **`SiegeUnit.prefab` has `health = 10`; the C# default is `2`.** A leftover buff from early testing, so units survive 5 s of uncovered exposure instead of 1 s (sentry does 1 dmg / 0.5 s). **Reset to 2 before judging any balance.** Another instance of the "code default changed, serialized value didn't follow" trap — check the prefab, not the source.
3. **`coverLaneMargin` is still a fixed 5 cm real per side**, so on a 33 cm-wide board one thin wall lays a lane ~39% of the board's width. Everything else in the project is a fraction of `BoardPlane.Length`; this should be too. **Probably the single highest-value change for making precision/vantage matter.**
4. **Unit size is not board-relative** — units stay 5.2 cm at any board size, so on a pinched-out 1.2 m board they are half their proper relative size. Only bites at non-default board sizes.

**Answered, no action needed:** the sentry *is* the red-tinted Blender trooper (`UnitTeamTint`), not the old sphere — `GarrisonUnit.prefab` still carries a legacy root `MeshFilter`/`MeshRenderer` with the Sphere mesh but **the renderer is disabled** (a vestigial `SphereCollider` also remains; both are harmless). Sentries deliberately have no `UnitAnimator`. The covered wedge was drawing at ~4% of its real range until fixed on 2026-08-08 — **that fix has not yet been visually confirmed on device**, so look for it next test.

### Still not built

The **AI commander** (the single biggest gap — Rally and the whole "react to a threat" layer are inert without it), player-base Lose condition, star ratings, sound, board elevation, and submission assets.

See `plan.md` Section 9 for the ordered task list.
