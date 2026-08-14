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

## Working agreement (set 2026-08-08, narrowed 2026-08-09)

**The boundary is the physical device, not the Editor.** The user only wants to personally do "Build and Run" to his Android device (on-device testing) and GitHub commits. Editor-only builds that don't touch the device — e.g. a release AAB via `Assets/Editor/BuildScript.cs` headed for Play Console — are fine to run unattended through Unity MCP. Pull logs with adb *after* they report something, or if they explicitly ask for a deeper investigation over USB.

Useful log commands once they report an issue:
```
adb logcat -d -s Unity:E AndroidRuntime:E          # errors only
adb logcat -d -s Unity:V | grep -A8 PlaneLock      # plane detection diagnostics
adb logcat -d -s Unity:V | grep -iE "LevelBuilder|selected|Rally"
```

## Security — read `SECURITY.md` before any monetization, signing or release work

The repo is **public** and the app takes **real money**. `SECURITY.md` at the root is the standing
checklist and the findings register from the 2026-08-08 full audit. The short version:

- **Public RevenueCat SDK keys (`goog_`/`appl_`/`test_`) are meant to ship in the client and are fine in
  this repo. A secret key (`sk_…`) must never touch it** — it lives in the MCP config outside the repo.
  If one is ever committed, rotating it in the dashboard is mandatory; deleting the commit is not enough.
- **Assume anything serialized into a scene is public.** Verified: the RevenueCat key is readable in
  plaintext from `assets/bin/Data/level1` inside the APK. IL2CPP protects logic, not data.
- **Release builds are now real** (fixed 2026-08-09): `Scrap Siege > Build Android APK (RELEASE - for
  Play Store)` produces a signed, non-debuggable `build/ScrapSiege.aab`, and refuses to run at all
  without a real upload keystore configured (`Assets/Editor/BuildScript.cs`). The plain
  `(Development)` menu item is still debug-signed and debuggable on purpose, for adb testing.
- **Signing passwords do not survive an Editor restart** in this Unity version — confirmed twice. After
  reopening the Editor, re-apply `PlayerSettings.Android.keystorePass`/`keyaliasPass` before attempting a
  release build, or it fails with a clear "No keystore passwords were found" exception (never a silent
  fallback to debug signing). Password lives in a local credentials file outside the repo — see
  `SECURITY.md` Finding 4 for the exact path and the reasoning.
- **`ProjectSettings/ProjectSettings.asset` is tracked** — diff it after configuring signing, in case
  Unity wrote a keystore password into it. That is the most likely way this project leaks a credential.
  (Checked twice on 2026-08-09: it never does, in this Unity version — only
  `AndroidKeystoreName`/`AndroidKeyaliasName`/`androidUseCustomKeystore` get written.)

## Keeping context/token usage down (set 2026-08-09)

Sessions in this project have been running past 150k tokens, mostly from long build/log output landing
directly in the conversation. Concretely:

- **Don't tail whole log files into the transcript.** Filter before reading: `Select-String -Pattern` in
  PowerShell, `grep`/`tail -n` in Bash, not a bare `Get-Content -Tail 60` when you only need one line.
  Editor.log build progress is especially verbose — grep for the terminal state (`Build Finished`,
  `error CS`, `Exception`), not the whole scroll.
- **Use `Monitor` for anything that takes minutes** (a release build is ~3–20 min depending on whether
  IL2CPP artifacts are cached) instead of a foreground `Unity_RunCommand` call, which times out around
  30 min of silence anyway and gives no partial output. Grep the log for a real completion signal — a
  loose pattern like a bare `"Exception"` will false-positive on unrelated boilerplate text already in
  the file; anchor on `Build Finished, Result:` or similar.
- **Read only the file section you need.** Prefer `Grep`/targeted `Read` with `offset`/`limit` over
  reading a whole large file (this project's memory files, `plan.md`, and `SECURITY.md` are all long).
- **Hand off research-heavy digging to a subagent** (`Explore` for pure search, `general-purpose` for
  multi-step investigation) when a task is about to mean reading many files just to answer one question —
  only its summary needs to land in the main conversation.
- **Prefer a fresh session at natural phase boundaries** (end of a feature, end of a security pass, end
  of a monetization setup push) over continuing to pile work into one long session. Before ending one,
  write the handoff into memory/`CLAUDE.md`/`plan.md` so the next session starts with full context
  without re-deriving it from scratch — this file's dated "Current state" sections exist for exactly that.

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
2. **~~`SiegeUnit.prefab` has `health = 10`~~ — RESOLVED 2026-08-08, now `3`.** Not reverted to the old default of 2 but *derived*: 1 damage per 0.5 s tick against 3 HP is a 1.5 s fight, matching Mechanic 6's 1–2 s of readable combat. Re-derive it if tick rate or damage changes. (Still a good example of the "code default changed, serialized value didn't follow" trap — check the prefab, not the source.)
3. **`coverLaneMargin` is still a fixed 5 cm real per side**, so on a 33 cm-wide board one thin wall lays a lane ~39% of the board's width. Everything else in the project is a fraction of `BoardPlane.Length`; this should be too. Was flagged as the highest-value tuning change — **now deliberately deferred with the sentry overhaul**, see "Deferred on purpose" above.
5. **"Units walk through rubble" is not a bug** — `BlocksMovement` exempts `RubbleCover` on purpose, and that exemption is what un-severed all three maps (the rubble line + wall spine left a 5 mm gap that erosion sealed). It was an **art** problem: the model read as solid, so passing through looked wrong. **Remodelled 2026-08-08** into low scattered debris with visible gaps (max height 0.26 of the unit cube, was 1.0). **Never re-add the carve.**
6. **~~"Units walk through the watchtower" IS a bug~~ — RULED OUT 2026-08-08 by measurement.** A Play-mode probe reproduced the spawner's obstacle exactly and found the carve correct in all three configurations: before bake, after bake, and reparented under a rotated 3×-scaled `BoardRoot`. `centreWalkable=False` every time. The bake-order hypothesis was wrong. Almost certainly the piece actually being walked through was the rubble above. See `plan.md` Section 10 for the full table and the two remaining lesser candidates if it recurs.
4. **Unit size is not board-relative** — units stay 5.2 cm at any board size, so on a pinched-out 1.2 m board they are half their proper relative size. Only bites at non-default board sizes.

**Answered, no action needed:** the sentry *is* the red-tinted Blender trooper (`UnitTeamTint`), not the old sphere — `GarrisonUnit.prefab` still carries a legacy root `MeshFilter`/`MeshRenderer` with the Sphere mesh but **the renderer is disabled** (a vestigial `SphereCollider` also remains; both are harmless). Sentries deliberately have no `UnitAnimator`. The covered wedge was drawing at ~4% of its real range until fixed on 2026-08-08 — **that fix has not yet been visually confirmed on device**, so look for it next test.

## Current state (2026-08-08, end of session 3) — AI commander SHIPPED

Scope agreed with the user: **full mutual siege**. The AI deploys real attacking units and the player
can lose. `plan.md` Section 9 has the ordered task list; Section 5 the AI design; Mechanic 6 the combat.

**All three passes are done and device-tested.** The user tested and accepted the build; the only
defect was level 4's card briefing overflowing its background (mine — 175 chars against a ~110 budget),
now fixed at 94.

- **Pass A** — `Team` enum, frontage-limited combat, `UnitDeathEffect`, `SentryFireVisualizer`, health 10 → 3, Lose condition.
- **Pass B** — `AICommander` (Push/Intercept/Hold) + `AICommanderProfile` + level 04 "The Gauntlet" + `EnemySiegeUnit.prefab`, gated by `LevelDefinition.hasAICommander`.
- **Pass C** — rubble remodelled; watchtower carve **ruled out** by measurement.

**Next up:** sound, AI tuning + a second difficulty tier, star ratings, and — highest risk — the real
on-device RevenueCat purchase, which is a hard submission requirement still unmet.

**Authoring constraint learned the hard way:** level card briefings must stay under **~110 characters**.
The card is fixed-height and the four shipped levels sit at 94–106.

### The one design rule to not break here

**Combat is frontage-limited: one enemy may engage a unit, never several.** A unit with no *unengaged*
enemy nearby walks past and keeps going for the base. This is deliberate and load-bearing — dogpiling
makes losses scale by Lanchester's square law, so the bigger stack always wins and "always deploy max
units" becomes strictly correct, which kills positioning, vantage and cover in one move. Numbers are
meant to buy **breakthrough**, not annihilation. Cover damage reduction and a winner-recovery delay are
the secondary dampers. If combat ever starts feeling like an arithmetic race, check this rule first.

### Deferred on purpose — do not pick these up

The sentry system is being overhauled later, so everything that was **sentry-balance** work is deferred
*together*: the `coverLaneMargin` board-relative fix, re-authoring The Narrows, and two-origin garrison
bucketing in `MusterPhaseController`. **The three shipped levels are not to be touched.** Also skipped by
decision: a dedicated tip *screen* — `LevelDefinition.briefing` is already rendered on the level-select
card by `MainMenuController.PopulateCard`, so authoring the string is the whole job.

### Still not built

Star ratings, sound, board elevation, and submission assets.

## Current state (2026-08-09, end of the security-fix + Play Console monetization session)

**Scope:** fixed the three code/settings security findings that blocked a real Play upload, generated
the upload keystore, produced and verified a real signed release build, and finished the RevenueCat side
of the Play Store product. Full detail, findings register, and the exact verification commands are in
`SECURITY.md` — this is the summary.

**Done:**
- Findings 1, 2, 5 (debug-signed builds, external install location, placeholder product name) fixed in
  code/`ProjectSettings.asset`. Finding 4 (no keystore) resolved by generating one outside the repo.
  Finding 3 (client-side entitlement flag) re-verified as still correctly accepted.
- `Assets/Editor/BuildScript.cs` now has a real release path: `Scrap Siege > Build Android APK (RELEASE -
  for Play Store)` builds `build/ScrapSiege.aab` (AAB, not APK — required for Play), refuses to run
  without a real keystore, and is non-debuggable.
- Built and **verified on the real artifact** (not just settings) with `bundletool build-apks` +
  `apksigner verify --print-certs` + `aapt2 dump badging/xmltree`: real cert, not debuggable,
  `internalOnly`, labeled `ScrapSiege`.
- User created the `scrap_siege_pro`/`monthly` subscription in Play Console. Registered in RevenueCat as
  `prod759b1f896f`, attached to entitlement `entl844b33dd6b` and package `pkgef5eaf57c5e`.
- Swapped `MonetizationManager.revenueCatApiKey` in `Assets/Scenes/ARTest.unity` from the Test Store key
  to the real Play Store key `goog_BPqxjAwHxIuYgSZXpVxbuhuaLbt`, rebuilt, reverified the key swap on the
  artifact.
- Working agreement narrowed: the boundary is the physical device, not the Editor — see the section
  above. Unattended Editor-only builds through Unity MCP are fine; only device install/launch and git
  commits stay manual.

**Update (2026-08-09, versioning saga — three passes before landing):** first Internal Testing upload
was rejected — "Version code 1 has already been used." Bumped `AndroidBundleVersionCode` 1→2, but
`bundleVersion` (versionName) was still `0.1.0`, which reads as a downgrade — bumped to `0.2.0`. By then
`versionCode=2` had also already been consumed by an upload attempt, so bumped once more to
`versionCode=3` / `versionName=0.3.0` in lockstep. Reapplied signing passwords each rebuild (lost on
Editor restart, as expected), reverified `versionCode='3'` / `versionName='0.3.0'` on the final artifact
with `aapt2 dump badging`. `SECURITY.md` section B now has standing checklist items for both fields —
**always bump `bundleVersionCode` and `bundleVersion` together**, and if a version code is ever rejected
as "already used" even though nothing was knowingly uploaded with it, assume a prior attempt consumed it
and move on rather than retrying the same number.

**Not done — the next session's task queue:**
1. Upload `build/ScrapSiege.aab` (versionCode=3, versionName 0.3.0) to Play Console Internal Testing.
2. Add self as a license tester, accept the opt-in link on the test device.
3. Real on-device purchase test, report back.
4. If it works: mark the monetization submission requirement complete in `plan.md`'s checklist.
5. If it fails: check RevenueCat offerings load first (`goog_` key + product propagation can take a few
   hours after creation in Play Console — don't assume a bug immediately), then check `adb logcat` for
   the actual Billing error.
6. Housekeeping: the keystore credentials file at
   `C:\Users\naika\keystores\scrapsiege-upload-key-CREDENTIALS.txt` still has the passwords in plaintext
   — move them to a password manager and delete the file once signing is confirmed stable across at
   least one more Editor restart + release build.

See the assistant's `project_scrap_siege_monetization_handoff` memory for the same queue with more
detail, kept in sync with this section.

## Current state (2026-08-10, later session) — camera black-screen fixed and built; NOT yet device-confirmed

**Root cause found for the "camera not working, black screen" report:** not the `GL_INVALID_ENUM`
error the user saw (that fires 3 times at init, unrelated). The real cause, found by counting
occurrences across the logcat buffer rather than eyeballing a snippet: ARCore's session gets stuck
in an error state and **never calls `ArSession_resume`** (zero occurrences in two full app-run logs)
because the manifest lacked `android.permission.HIGH_SAMPLING_RATE_SENSORS`, required for ARCore's
IMU registration on apps targeting API 31+ (this one targets 36). With no resumed session the camera
device is never opened, so every frame logs `camera was passed NULL` (16k+ times per run) and plane
detection reports `planes=0` forever — indistinguishable on screen from "no good plane found," which
may retroactively explain the older "AR plane detection is a weak point" finding. Full diagnostic
detail in the assistant's `project_scrap_siege_unity_gotchas` memory.

**Fix:** new `Assets/Editor/AndroidManifestPostProcessor.cs` injects the permission into the
generated manifest post-build (same mechanism the ARCore XR plugin itself uses for CAMERA/INTERNET).
Documented in `SECURITY.md` as a new, harmless, install-time permission — no new exposure.

**Built and verified against real artifacts (2026-08-10), NOT yet run on a device:**
- `build/ScrapSiege.aab` — release, versionCode=4/versionName=0.4.0, signer `CN=ScrapSiege`
  (not debug), not debuggable, `internalOnly` install location, all 6 permissions present incl. the
  new one. Verified via `bundletool build-apks` + `aapt2 dump badging/xmltree` +
  `apksigner verify --print-certs`. **Not yet uploaded to Play Console.**
- `build/ScrapSiege.apk` — dev, debug-signed as expected, same new permission present.
  **Not yet installed/run on a device.**

**Next session's actual task queue:**
1. Install the dev APK, confirm the camera fix on-device (look for a live passthrough and
   `ArSession_resume` in a fresh logcat — `planes > 0` alone isn't proof, could just be slow real
   detection).
2. Upload the versionCode=4 AAB to Play Console Internal Testing (bump both version fields together
   if rejected as already-used, per the existing versioning lesson above).
3. License tester + real purchase test, as before.
4. Still unresolved, not investigated this session: `ARTest.unity`'s uncommitted diff collapses 26 of
   28 UI `RectTransform`s to zero size/position. Likely a harmless stale editor snapshot under the
   HUD's `LayoutGroup`s, but genuinely unverified — worth a look at the HUD on the next device test.
5. Working keystore-password flow, confirmed end to end this session: set `SCRAPSIEGE_KEYSTORE_PASS`
   as a Windows *User* env var, then if the Editor still can't see it, the fix is
   `Stop-Process -Name explorer -Force` (auto-restarts) **and** fully quitting/relaunching Unity Hub
   itself, not just the Editor window — full detail (including why) in the gotchas memory.
6. Security scan of everything currently pending for commit is clean — safe to commit/push.
   `.gitignore` hardened with explicit keystore/credential-file patterns as defense-in-depth.

## Current state (2026-08-10, evening) — first Play-installed build tested; three separate bugs found

The user installed **versionCode 4 / 0.4.0 from Play (Internal Testing)** on the Tab S6 Lite and made a
**real Google Play subscription purchase**. Google charged and the subscription is live on Google's side.
It then behaved as three separate faults, which are three *unrelated* root causes — do not treat them as
one monetization bug.

### 1. The camera/ARCore fix WORKED — confirm this before re-debugging it

`vio_estimator.cc ... Successful initialization`, `pose_manager.cc ... World pose node changing`,
`[LatestPoseTracker] Received first vio state` all present, and `camera was passed NULL` is down from
16k+ per run to **28 total**. `HIGH_SAMPLING_RATE_SENSORS` is declared *and* `granted=true` on the
installed package. **The ARCore session now resumes and tracks. That item is closed.**

### 2. "Deploying stops working after subscribing" — NOT caused by the purchase

Root cause: **`UnitDeploymentController` required a `PlaneWithinPolygon` AR raycast hit, while
`BoardPlacementController` accepts `PlaneWithinPolygon | PlaneEstimated | FeaturePoint`.** ARCore tracks
fine on this device but had **`planes=0` in all 16 `[PlaneLock]` diagnostics across the entire logcat
buffer** — it never promoted anything to a plane. So the board places happily off feature points, the
match starts, and then *every deploy tap is silently discarded forever*. No restart can help; no plane
ever appears. Corroborating evidence: **zero `EnemyBase: took` lines in the whole buffer** — no player
unit has ever reached the enemy base in any run.

The purchase looked causal only because the timeline was tight: board built 15:50:34 → Go Pro tapped
15:50:39 → purchase returned 15:50:50 → base dead 15:51:45 with no deploys. There was never a working
deploy to lose.

**Fixed:** deploy taps now intersect the **board's own transform plane** (`LevelMatchController.BoardRoot`,
new property) and are clamped to the board rectangle via `InverseTransformPoint` (local |x|,|z| <= 0.5).
No ARCore plane and no Collider involved — the slab deliberately has no Collider, so `Physics.Raycast`
was not an option. The AR raycast remains as the fallback for the legacy scan/Fortify path only. Taps
outside the board are now rejected on purpose (units deploy onto the board, not onto bare table).
**Every rejection path now logs (throttled, `[Deploy] tap ignored - ...`)** — "I tap and nothing happens"
previously produced not one line of log, which is why this survived so long.

### 3. Pro features not unlocking + Google cancelling the subscription — ONE cause, and it is dashboard-side

**RevenueCat has no Google Play service account credentials.** Three independent confirmations:
- logcat: `PurchasesError(code=InvalidCredentialsError, underlyingErrorMessage=Invalid Play Store
  credentials.)` on every `POST https://api.revenuecat.com/v1/receipts`
- MCP `get-product-store-state` → HTTP 422 `"Missing credentials for the store."`
- MCP audit log: `app_created ... credentials_provided: false`, with **no** later credential update
- and the ledger agrees: 0 active subscriptions, $0 revenue, 10 customers, **no transactions at all**

Consequences, both of the user's remaining symptoms:
- RevenueCat cannot validate the purchase token → no entitlement → no PRO ACTIVE badge and level 05
  stays locked. **The menu/entitlement code is fine** — `MainMenuController` subscribes to
  `ProEntitlement.Changed` correctly and every Inspector reference in `MainMenu.unity` is wired.
- RevenueCat cannot **acknowledge** the purchase, and Google Play auto-refunds and revokes any purchase
  unacknowledged within 3 days. That is exactly the "it got cancelled even though I used the app" report.
- Follow-on: every later Subscribe tap now fails `ITEM_ALREADY_OWNED` / `ProductAlreadyPurchasedError`,
  because Google already has the subscription.

**Only the user can fix this** — it is Google Cloud + Play Console + the RevenueCat dashboard. The
RevenueCat MCP's `update-app` play_store body accepts `package_name` and nothing else, so there is no
API path for credentials. Steps: create a GCP service account with the Android Publisher API enabled,
grant it access in Play Console, download the JSON key, upload it at **RevenueCat > Project Settings >
Google Play App Settings > Service account credentials**. **Newly created credentials can take up to 36
hours to become valid** — `Invalid Play Store credentials` during that window is expected, not a
regression.

### Client-side fixes made in the same pass

- **`autoSyncPurchases` was silently OFF in every build.** `MonetizationManager` calls
  `purchases.Configure(config)` with a `PurchasesConfiguration.Builder` config, which **bypasses every
  Inspector field** (they only feed `Purchases.Start()`'s auto-configure path, which `useRuntimeSetup`
  disables). `Builder.Build()` does `_dangerousSettings ?? new DangerousSettings(false)` — auto-sync
  **false**, the opposite of the component's own default of true. The SDK said so on every launch
  ("⚠️ Automatic syncing of purchases has been disabled") and it went unread for the feature's whole
  life. Now set explicitly via `SetDangerousSettings(new Purchases.DangerousSettings(true))`.
  **General rule: if it is passed to `Configure()`, the Inspector value is decoration.**
- **`MonetizationManager.OnApplicationFocus`** now refreshes customer info on resume. The Play purchase
  flow is a separate activity (`ProxyBillingActivity`), so every purchase — and every subscribe/cancel
  done in the Play Store app — is bracketed by a pause/resume that the app previously ignored entirely.
- **`MonetizationManager.SyncPurchases()`** added as the recovery path for a purchase the store has but
  RevenueCat does not.
- **`PaywallController` now recovers from `ProductAlreadyPurchasedError`** by calling `SyncPurchases`
  instead of showing the player "This product is already active for the user" — which, to someone who is
  being charged and has nothing, reads as a taunt. `MonetizationManager.Purchase`'s callback signature
  gained the readable error code for this (`Action<bool, string, string>`).

**Not yet device-tested:** all of the above. Built and compile-verified only (both `ScrapSiege.Runtime`
and `Assembly-CSharp` build clean).

## 2026-08-10 (later) — 0.4.0 device test results: 2 new bugs found, RevenueCat credit status unclear

**Important scoping note:** the device test that produced these findings ran the **0.4.0 build**, which
predates the three code fixes above (autosync, `OnApplicationFocus`, `SyncPurchases`, plus the deploy
raycast fix and the paywall `ProductAlreadyPurchasedError` recovery). None of those fixes have been
device-tested yet. The findings below are new, on top of them.

**Good news:** no obvious new bugs beyond the two listed. The main-menu Pro Active badge swap works
correctly on device.

### New bug A — the in-match "Go Pro" button never reflects Pro state

`MainMenuController` (main menu scene) correctly subscribes to `ProEntitlement.Changed` and swaps
`goProButton`/`proActiveBadge`. The **match scene's own `GoProButton`** (`Assets/Scenes/ARTest.unity`,
object `GoProButton`, opens `PaywallPanel`) has **no controller doing the equivalent** —
`Assets/Scripts/UI/HudController.cs` has zero references to `ProEntitlement` or Pro state at all. So a
Pro user still sees "Go Pro" during gameplay even though they already own it. Needs the same pattern as
`MainMenuController.ApplyProState()`: subscribe in `OnEnable`, hide the button (or swap to a badge) when
`ProEntitlement.IsUnlocked`, unsubscribe in `OnDisable`.

### New bug B — the paywall's feature list is stale copy, hardcoded in the scene

`Assets/Scenes/ARTest.unity` line ~6662, a static (non-code-driven) TMP_Text under `PaywallPanel`,
literally reads:
```
■ Saturated terrain palette
■ More cosmetic board themes
■ Extra visual effect packs
```
This is old marketing copy from before level 05 "The Foundry" and the Veteran AI tier existed as real
Pro perks (see `project_scrap_siege_monetization_handoff` memory, "FIXED 2026-08-10" section — that pass
built the actual Pro-only level and repaint-on-purchase, but never went back to update what the paywall
*promises*). Only the palette line is real; "more cosmetic board themes" and "extra visual effect packs"
were **never built** — confirmed by search, no such systems exist anywhere in the codebase. Two options,
not decided yet:
1. Rewrite the copy to what's actually shipped: saturated palette + level 05 "The Foundry" (harder,
   Veteran-tier AI — confirmed via `05_TheFoundry.asset:aiProfile` pointing at `AIProfile_Veteran.asset`,
   so "harder AI" is already naturally Pro-gated through the level, no separate gate needed) + Veteran AI.
2. Or actually build board themes / effect packs to match the existing promise (bigger scope, probably
   wrong call this close to submission).
**Recommendation for next session: option 1** — the promise should describe what ships, and level 05 +
Veteran AI is a perfectly good value prop that's just never been *written down* anywhere the player sees.

### Unresolved — is RevenueCat actually receiving purchase data at all?

User noticed the RevenueCat sandbox customer list still shows only one customer with a purchase, dated
to a build a day old (i.e. from before 0.4.0). Two explanations, not yet distinguished:
- Expected and boring: **the missing Google Play service account credentials** (see the "evening" section
  above — `InvalidCredentialsError` on every receipt POST) mean 0.4.0's purchase attempt(s) never landed
  as valid transactions either, consistent with everything already known.
- Or: something is still wrong even once credentials are fixed, worth a fresh look with `list-customers` /
  `list-customer-events` / `get-customer` on the specific test account, and cross-referencing against a
  fresh on-device purchase attempt's timestamp.
**Next session: check this via RevenueCat MCP once credentials are confirmed uploaded** — don't assume
it's "just" the credentials issue without checking, since a second independent bug at this stage would be
easy to miss if we stop looking after finding the first one.

## Next session task queue (as of 2026-08-10 late evening) — READ THIS FIRST

1. **User action (dashboard, not code):** upload Google Play service account credentials to RevenueCat
   (Project Settings > Google Play App Settings). This is the root cause of Pro not unlocking and of the
   subscription being auto-cancelled by Google. Confirm this got done before assuming anything else is
   broken. Allow up to 36h to propagate.
2. **Fix new bug A**: wire the in-match `GoProButton`/badge to `ProEntitlement.Changed`, same pattern as
   `MainMenuController.ApplyProState()`. Small, isolated change to `HudController.cs` or a new small
   component alongside it.
3. **Fix new bug B**: rewrite the paywall's feature-list copy in `ARTest.unity` (~line 6662) to describe
   what's actually shipped (saturated palette, level 05 "The Foundry", Veteran-tier AI) rather than
   unbuilt cosmetic promises. Simple text edit, but it's in a scene file, not a script — verify with
   `Unity_RunCommand` or a targeted YAML read, not by inspecting a script.
4. **Build a new dev APK** including the four code fixes from the "evening" session (autosync, focus
   refresh, SyncPurchases, deploy raycast fix, paywall already-owned recovery) plus bugs A and B above —
   install and device-test all of it together once credentials are confirmed live, since Pro-gated
   content can't be meaningfully tested without a real entitlement.
5. **Check RevenueCat MCP for whether purchase data is actually flowing** post-credentials-fix — see
   "Unresolved" section above. Use `list-customers`/`get-customer`/`list-customer-events` against the
   test device's `$RCAnonymousID` or the newly-identified user, correlated to a fresh purchase timestamp.
6. Everything else from the "evening" section's queue (SECURITY.md commit, keystore file cleanup) is
   still pending and lower priority than the above.

**Uncommitted as of this writing** (not pushed, per the working agreement — commits are the user's call):
`Assets/Monetization/MonetizationManager.cs`, `Assets/Monetization/PaywallController.cs`,
`Assets/Scripts/Levels/LevelMatchController.cs`, `Assets/Scripts/Siege/UnitDeploymentController.cs`,
`CLAUDE.md`. Both `ScrapSiege.Runtime` and `Assembly-CSharp` compile clean as of the last check.

## 2026-08-10 (fresh session) — credentials confirmed live, bugs A/B fixed, dev APK rebuilt, IAP requirement re-opened

**Credentials confirmed live via RevenueCat MCP, not just assumed done:** audit log shows
`appa37d9670f8`'s `credentials_provided` flipped `false → true` at `2026-08-10T13:33:43Z`, and
`get-product-store-state` for `prod759b1f896f` now returns `status: "ok"` (was a 422 "Missing
credentials for the store"). Only ~3 hours old at the time of this check — still inside the "allow up
to 36h" propagation window from the "evening" section above.

**Checked whether purchase data is flowing — confirmed it is not, and confirmed why.** Pulled all 10
customers this project has ever seen (`list-customers`, no pagination needed) and checked
`list-customer-events` / `get-customer` / `list-subscriptions` for each: **zero purchases,
subscriptions, entitlements, or events exist anywhere in this project's history**, including for the
customer that made the real Google-charged purchase in the "evening" session. This independently
confirms that purchase never reached RevenueCat as a valid receipt — not a second bug, just
confirmation of the known one. No purchase attempt has happened since credentials went live.

**Time-sensitive:** Google auto-revokes an unacknowledged purchase after 3 days; that clock started on
the original evening purchase. Only a fresh receipt — a new purchase, or the client's own
`SyncPurchases()` resending the existing token — can save it now that credentials work.
`SyncPurchases()` was written in the "evening" session but wasn't in any built APK until now —
installing and opening the fresh build promptly gives that old subscription a chance to be
acknowledged before Google auto-refunds it.

**Fixed bug A** (in-match Go Pro button never updated): `HudController.cs` gained a `goProButton`
field, subscribes to `ProEntitlement.Changed` in `OnEnable`/unsubscribes in `OnDisable` (same pattern
as `MainMenuController.ApplyProState`), and hides the button once Pro is active. Wired in
`ARTest.unity`'s `HudController` component to the existing `GoProButton` object — no new UI object
needed. There's no separate in-match "Pro Active" badge (unlike the main menu); out of scope here.

**Fixed bug B** (stale paywall copy): the feature list in `ARTest.unity` (~line 6663) now reads
"Saturated terrain palette / Level 05: The Foundry / Veteran-tier AI challenge" instead of the unbuilt
"more board themes / effect packs" promises.

**Rebuilt the dev APK** with all six pending fixes (the four from "evening" + bugs A/B): via
`Unity_RunCommand` → `BuildScript.BuildAndroidFromEditor(development: true)` →
**`build\ScrapSiege.apk`, 164 MB, 0 errors, 5 pre-existing unrelated warnings**, confirmed freshly
built (`2026-08-10 19:46` file timestamp). Editor-only, done unattended per the working agreement —
**not installed to the device**, that's the user's step.

**Corrected a stale memory:** [[project_scrap_siege_shipaton_readiness]] had claimed the IAP entry
requirement was "✅ CLEARED" based on an entitlement "returning active" that, per the zero-purchases
finding above, never actually happened. Corrected in place — **the entry requirement is still open**,
pending a real post-credentials purchase.

**Next-session queue:**
1. User installs `build/ScrapSiege.apk`, does a fresh on-device purchase test (or lets
   `SyncPurchases`/autosync recover the old one).
2. Re-check RevenueCat MCP the same way as this session for a real `gives_access: true` subscription.
   Only then mark the entry requirement cleared.
3. If still `InvalidCredentialsError` and under 36h since the credential upload, that's expected —
   wait rather than re-diagnosing.
4. Everything else (SECURITY.md commit, keystore cleanup) unchanged, lower priority.

**User's explicit instruction this session:** the AR demo video is recorded last, only once the
product is final — do not suggest or schedule it mid-timeline.

## 2026-08-10 (same session, later) — version bumped to 0.5.0, release AAB built, auto-bump automation added

User wants to push a new Play Console Internal Testing release and asked for automatic version
bumping going forward, since forgetting this caused the versionCode=1→2→3 "already used" churn
documented earlier in this file. Version was still 0.4.0 / versionCode 4 (the already-uploaded one).

**Bumped to 0.5.0 / versionCode 5** (`PlayerSettings.bundleVersion` / `PlayerSettings.Android.
bundleVersionCode`, via `Unity_RunCommand`, then `AssetDatabase.SaveAssets()`).

**Added automatic version-bumping to `Assets/Editor/BuildScript.cs`**: `RunBuild`'s release path now
calls a new `BumpVersionForNextRelease()` after every *successful* release-AAB build, which increments
the patch digit and `bundleVersionCode` by 1 and saves. Deliberately fires *after* the build, not
before, so the artifact just produced keeps the version it was asked for and only the *next* build
starts pre-bumped. Minor/major bumps (like this session's 0.4.0→0.5.0) stay a manual judgment call —
only the automatic, mechanical part (never colliding with a version Play has already seen) is
automated. If `bundleVersion` isn't in `major.minor.patch` form, it logs a warning and skips rather
than guessing.

**Built the release AAB**: `build/ScrapSiege.aab`, 39 MB, 0 errors, 5 pre-existing unrelated warnings.
Keystore passwords were re-applied first via `BuildScript.ApplyKeystorePasswordsFromEnvironment()`
(needed after every Editor restart, per the existing keystore-password lesson). **Verified directly
against the artifact, not just Editor settings**, using `bundletool dump manifest` (found at
`.../PlaybackEngines/AndroidPlayer/Tools/bundletool-all-1.17.2.jar` — `aapt2 dump badging` doesn't
understand the `.aab` container format, only bare APKs, so bundletool is the right tool for this format
specifically): confirms `versionCode="5"`, `versionName="0.5.0"`, package
`com.mikhilnaika.scrapsiege`. As a side effect of the new automation, **`PlayerSettings` now reads
0.5.1 / versionCode 6** — that is intentional, ready for whatever gets built next, and does not affect
the 0.5.0/5 artifact already sitting in `build/`.

**Not done by me**: uploading `build/ScrapSiege.aab` to Play Console Internal Testing — that's a
dashboard action with no MCP path, same as every previous release, and stays the user's step.

## ✅ 2026-08-11 — monetization phase closed: 0.5.0 tested, Pro confirmed unlocking, transactions flowing

User uploaded `build/ScrapSiege.aab` (0.5.0/versionCode 5) to Play Internal Testing, tested it, and
**saw Pro visibly unlock in-game (PRO ACTIVE badge, Level 05 available)** — not just a dashboard
transaction. Independently verified via RevenueCat MCP: `list-subscriptions` on the test customer
(`$RCAnonymousID:282e5b06bb1849dc8752b8d24e34ee1e`) shows three real sandbox purchases of
`prod759b1f896f` landing successfully in the hours around the test.

**Worth remembering for next time this needs checking:** each of those subscriptions now reads
`gives_access: false` / `status: expired` with an empty `entitlements` list — that looks like a
failure but isn't. Play's license-tester sandbox compresses a monthly subscription's renewal/expiry
cycle to ~5-minute windows for testing, so *any* sandbox purchase reads "expired" shortly after,
working or not. Don't re-diagnose this as a bug from `list-subscriptions` alone; corroborate with
either a fresh purchase checked immediately, or the user's own in-game observation.

**This closes the whole monetization arc** that ran from 2026-08-09 (Play Console/RevenueCat setup)
through the credentials saga (2026-08-10) to this confirmation. The IAP entry requirement for
Shipaton 2026 is genuinely met. Full detail in [[project_scrap_siege_monetization_handoff]] (now
closed/historical) and [[project_scrap_siege_shipaton_readiness]] (current status) — read those, not
this file's older sections, for where things stand.

**User's plan from here:** start a fresh session and reassess what's left from there. Known remaining
work per [[project_scrap_siege_shipaton_readiness]]: AI tuning, on-device sound verification, content
depth, and — deliberately last, only once the product is final — the demo video.

## Current state (2026-08-11) — Pass D: depth pass. BUILT AND EDITOR-VERIFIED, NOT DEVICE-TESTED

Six items the user asked for after the 0.5.0 device test. `plan.md` Section 9 "Pass D" has the full
design writeup and the class stat table; Section 10 has two new risk flags. This is the summary.

**Everything below compiles clean, every Inspector reference was set and read back non-null, and the
class system was exercised in Editor play mode. None of it has run on the device.**

### What shipped

1. **Unit classes** — `UnitClass` + `UnitRoster` ScriptableObjects, five classes in `Assets/Units/`:
   Trooper (1 scrap, baseline), Bulwark (2, 9 HP, soaks), Marksman (2, reach 0.17 of board),
   Saboteur (3, invisible to sentries, never stops, 4 base damage), Turret (4, stationary
   emplacement, **Pro-gated**). Shipping a sixth is an asset plus a roster entry — no prefab, script
   or scene edit. `UnitRosterBar` builds the deploy chips from the asset at runtime.
2. **Asymmetric duels.** The frontage rule is untouched (one unit fights at most one enemy). What is
   new: each side independently checks the range *it* can fire from, so a marksman stands and shoots
   while its melee opponent walks in. One range comparison, no parallel ranged-combat system.
3. **Selective Rally** — `RallyController.SetScope(UnitClass)`, null = whole army. HUD toggle switches
   between ALL and the selected deploy class. Emplacements always excluded.
4. **AR intent** — terrain heights raised hard (Tall 0.130 → **0.220** of board length; a Tall piece
   goes from 7.8 cm to 13 cm on a 0.60 m board, against a 5.2 cm unit), new per-level
   `LevelDefinition.terrainHeightScale`, **deploy now requires line of sight**, reticle turns red on
   an occluded point, HUD shows "N CONTACTS UNSEEN · MOVE TO LOOK".
5. **Navigation** — top-bar MENU button (confirm modal mid-match) and MAIN MENU on the outcome card.
   There was previously no way back to the menu at all.
6. **Level-select paging** — 3 per page, prev/next, "PAGE 1 / 2".
7. **Recorded-audio override layer** — drop a WAV named after an `Sfx` value into
   `Assets/Audio/Resources/Sfx/` and it replaces synthesis for that sound; delete it and synthesis
   returns. Six new `Sfx` values for the classes. **`docs/SOUND_SHOPPING_LIST.md` is the user's
   to-do list** — search terms, licence rules (CC0 only), length targets, and a provenance table.

### Three latent bugs found and fixed on the way — all one root cause

`RallyController` and `DeployReticle` were still requiring a `PlaneWithinPolygon` ARCore hit. Deploy
was fixed for exactly this on 2026-08-10; these two were missed. On the Tab S6 Lite — which tracks
fine but has never promoted anything to a plane — that means **every rally tap was being silently
discarded for the whole match, and the deploy precision ring never appeared at all.** Both now
intersect the placed board's own transform, same as deploy. If a fourth component ever needs a tap
point on the table, copy that pattern; do not reach for `ARRaycastManager`.

### The bug play mode caught, and why it is worth remembering

`UnitDeploymentController.Awake` sets `enabled = false` (nothing may process taps before Siege).
**Unity never calls `Start()` on a component disabled before its first frame**, so a `Start`-based
default-class selection silently never ran — the roster bar had built all five chips while
`SelectedClass` was still null. Resolution now happens in `Awake` and again in `OnEnable`. Applies to
every self-disabling controller in this project (`RallyController`, `DeployReticle`,
`AICommander`, `UnitDeploymentController` all do this): **do not put initialization in `Start` on any
of them.**

### Read before touching this work

- `plan.md` Section 10's first two bullets: `requireLineOfSight` is the highest-risk change here, with
  a written dial-down order if it plays badly. Do not conclude the AR-intent direction failed from one
  bad session — the terrain height alone delivers most of the benefit.
- The Turret being Pro-gated is a deliberate but arguable call, reversible with one checkbox on
  `Assets/Units/Turret.asset`.
- Balance across five classes is **entirely untuned**. The numbers are derived from the existing
  1 damage / 0.5 s tick convention, not from play.

## Current state (2026-08-13) — Pass E: device-report fixes + per-class art. EDITOR-VERIFIED, NOT DEVICE-TESTED

Ten items from the user's on-device test of Pass D. Full writeup in `plan.md` Section 11; this is the
summary and the things worth not re-learning.

**Everything compiles clean, every Inspector reference set this pass was read back and asserted
non-null, and all five class models were rendered before export and measured after import. None of it
has run on a phone. Pass D is still un-device-tested too.**

### The three bugs, and their single causes

- **Selective Rally never worked** — `RallyScopeButton` was authored `m_Interactable: 0` in
  `ARTest.unity`. The scope logic was always correct; the only control that reaches it could not be
  tapped. Fixed in the scene *and* forced true in `HudController.HandleRallyScopeChanged`.
- **Double click sound** — `UIButtonMotion.OnPointerDown` plays `Sfx.UiTap` for every button in the
  game, and two handlers played it again. **Rule: button-press audio belongs to `UIButtonMotion` and
  nowhere else.** A handler may only add a sound that is different from the tap. Caught the same trap
  a second time this pass (`BaseHealth` already plays `Sfx.BaseHit`, so the new base-impact FX is
  visual only).
- **Unreadable "MAIN MENU"** — `UITheme.TextOnAccent` (dark ink for amber buttons) on a dark
  `SurfaceRaised` fill. Fixed as a scene-wide rule, not one value.

### Two absolutes that should always have been board-relative

- **Unit size** was fixed at 5.2 cm regardless of board size — the last absolute size in the project,
  and the cause of "troops look giant compared to the map" on a small board.
  `SiegeUnit.ApplyBoardScale` / `GarrisonSentry.ApplyBoardScale` now key off
  `boardLength / referenceBoardLength` (0.60 m), clamped 0.55–1.8, agent radius following the model.
- **The "big red sphere" was the last-known-contact ghost**, not a tracer — sized
  `WorldScale.Metres(0.05f)`, i.e. as tall as the trooper it stood in for. Now measured from its own
  target's renderers and flattened to a disc so it reads as a map mark, not a projectile.

### Health/damage are 5x

Everything (units, both prefabs, sentry tick, `BaseHealth`, all five levels) multiplied by 5, C#
defaults moved in lockstep. **Balance is unchanged** — the point is tuning headroom, because at the old
scale the smallest step was a 33% swing. **Do not reintroduce a 1 or a 2 unless you mean "a fifth of a
hit".** Marksman reach doubled to 0.34 of board and damage cut to 4 per the user's request; **this is
the most likely thing in the pass to be over-tuned** — raise `attackTickSeconds` before touching the
reach, since the reach is the class's identity.

### Per-class models replace the primitive accessories

`Unit_Bulwark`, `Unit_Marksman`, `Unit_Saboteur`, `Unit_Turret`, `Sentry_Turret` in `Assets/Models`,
built in Blender's existing `ScrapSiege_v2` collection. Trooper keeps the shared body on purpose (it
is the baseline and keeps the fallback path exercised). `UnitClass.modelPrefab` +
`UnitClassVisual.SwapInClassModel` + `UnitAnimator.Rebind()`.

Two Blender lessons worth not repeating:
- **Never read `parent.matrix_world` for an object created in the same script** without forcing a
  depsgraph update — Blender had not re-evaluated it, so every child of a pivoted part was displaced
  by its parent's pivot and the marksman's rifle pointed at the sky.
- **Blender object names are file-global.** Renaming `Torso.001` to `Torso` while the original
  trooper owns `Torso` silently suffixes one of them, and that name is baked into the FBX that
  `UnitAnimator` looks parts up by. Stash every object to a unique placeholder first.

Also: **height is normalised at swap time, never assumed.** The shared trooper FBX imports at 1/100
scale with a −90° X root rotation; the new models import 1:1 with none. Matching conventions by hand
is exactly how you get a speck or a giant.

### Combat FX

`CombatFx.Impact()` — a pooled shard burst fired from **the code that applies the damage** (melee,
ranged, sentry, and a bigger one on base hits). Melee previously had no visual at all, so two units
fighting read as two units loitering. Pooled cubes rather than a `ParticleSystem` because sizes must
come from board length.

### Paywall copy is derived, not written

`ProFeatureCopy.BuildFeatureList()` reads `requiresPro` off `LevelCatalog` and `UnitRoster`. Both
scenes call it on paywall open. `MainMenu.unity` had been promising two systems that **do not exist**
("more cosmetic board themes", "extra visual effect packs"). Ship a Pro level and the paywall now
advertises it with no edit anywhere.

### RevenueCat status — integration is sound; dashboard Paywalls are NOT used

Offerings/purchase/restore/sync/entitlement all correct, with the decoupled `ProEntitlement` gate.
**But RevenueCat's own dashboard-designed Paywalls are supported on Unity** via
`com.revenuecat.purchases-ui-unity` (`PaywallsPresenter.Present()`), which is **not** in
`Packages/manifest.json` — only `com.revenuecat.purchases-unity` 7.4.1 is — and the dashboard has zero
paywalls configured. Adopting it buys remote copy edits and Experiments; the risk is that it renders a
**native view over the Unity view**, i.e. over a live ARCore session in the match scene, and cannot be
tested in the Editor at all. **Low-risk shape if adopted: dashboard paywall from the main menu, custom
panel retained in-match.** Awaiting the user's decision.

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

## Current state (2026-08-13, later) — Pass F: the D+E device report. EDITOR-VERIFIED, NOT DEVICE-TESTED

Five items from the user's on-device test of Passes D and E, plus the Pro cosmetic tier item 1
implied. Full writeup is `plan.md` **Section 12**; this is the summary and the lessons worth not
re-learning. **Both assemblies compile clean, the Unity console is empty, every reference set this
pass was read back non-null, and none of it has run on a phone.** Passes D and E remain
un-device-tested too — one device session now covers all three.

### The class models: the bug was in the FBX, not in the code that swaps them

Every class except the Trooper rendered as "a big top and two cube legs". The cause was a
**non-identity `matrix_parent_inverse`** on every part built as a child of `Torso`: Blender composes
that into the world matrix so the viewport and the reference render are both correct, but the FBX
export does not carry it faithfully and Unity received those parts at **0.01 scale, piled at the
feet**. The correlation was exact — every broken object had a non-identity parent-inverse, every
correct one (the entire Trooper) had identity.

**Standing rule: an object with a non-identity `matrix_parent_inverse` is not safe to export.** Bake
it into the object's own basis before exporting, top-down, and assert the world matrices did not
move. That assertion is what made this a one-shot fix — 31 objects, zero drift, then re-export.

**Knock-on that would have been a new bug:** the re-exported FBXs now import like `SiegeTrooper` does
(root scale 100, root rotation −90° X) where the old ones imported 1:1 with no rotation.
`UnitClassVisual.SwapInClassModel` was overwriting the instantiated model's rotation and scale with
identity — harmless before, and from now on it would lay every model on its back, which is the
**exact bug this project already shipped on 2026-08-08**. It now keeps whatever the importer decided
and only multiplies the magnitude. **Never reset an imported model's root rotation or scale.**

### `renderer.materials` does not guarantee a fresh instance

Enemy debris rendered magenta. Not a shader or a stripping problem: `renderer.materials` returns the
*existing* instances if anything already instanced them, `VisionTarget` always has on enemy/garrison
units, and `VisionTarget.OnDestroy` destroys them a frame later — leaving the debris holding
destroyed materials, i.e. Unity's error material. Player units were fine only because they have no
`VisionTarget`, which is why it looked enemy-specific.

**Rule: if you intend to own a material, `new Material(source)` from `sharedMaterials` and assign
back through `sharedMaterials`.** Reading `.material`/`.materials` expresses a wish, not ownership.

### Two systems polling the same touch

An armed Rally tap redirected the army *and* deployed a paid-for unit, because `RallyController` and
`UnitDeploymentController` each read `Touchscreen.current.primaryTouch` from their own `Update`.
Guarding on `armed` alone is not enough — Rally clears it inside the same Update that consumes the
tap, so the bug would appear or disappear with script execution order. `RallyController.ClaimsBoardTap`
records the **frame** instead, and stakes the claim before the tap is known to resolve.
**Any future component that reads a board tap must consult it.**

### Deployment is restricted to the player's own lines, and the rule is drawn

`LevelBuilder.DeployZoneDepth` (0.30 of board length forward of the player's edge) is the single
number: `LevelBuilder` paints the band and its limit line from it, and `UnitDeploymentController` /
`DeployReticle` read it back through `LevelMatchController`. Verified open on all five levels. The
reticle greys out past the limit in a **different** colour from a blocked sightline on purpose — one
means "move so you can see it", the other "you can see it fine, it is not your ground". The standing
HUD prompt changed too, because "Tap the table to deploy" had become a lie.

### Route variety is three layers

Per-unit approach lane (`SiegeUnit.PickApproachLane`, `laneSpreadFraction` 0.16 — **this is the dial**),
per-unit cover cost (`SiegeUnit.CoverCostVariance`, 0.7–1.5×), and spread avoidance priority (30–70).
The lane is a committed waypoint rather than steering noise, and its **onward leg is proven
`PathComplete` before it is accepted** — otherwise a unit could reach a pocket it cannot leave and
sit there with `remainingDistance` reading 0, the trap this project already lost a session to.

### Pro now sells something that is not power

`UnitClass.proModelPrefab` + `UnitClassVisual.ResolveModelPrefab` swap in a **Veteran skin** for all
five classes while the entitlement is active. Purely cosmetic, each trimmed to its base model's
measured height (the swap normalises height, so a taller veteran shrinks its own body and reads as a
downgrade). `ProFeatureCopy` counts them, so the paywall advertises the set with no hand-written
string. This is the honest counterweight to the Turret being gated — see `plan.md` Section 7.

`docs/art/unit_lineup_front.png` and `unit_lineup_veteran.png` are now **in-engine renders of what
actually ships**, not Blender previews.

## Current state (2026-08-13, later still) â€” Pass G: the D+E+F device report. EDITOR-VERIFIED, NOT DEVICE-TESTED

Five items from the user's test of the combined Passes D/E/F build. **Two of them were the same two
root causes.** Full writeup is `plan.md` **Section 13**; this is the summary and the lessons worth not
re-learning. **Both assemblies compile clean, the Unity console is empty, every reference set this
pass was read back non-null, and all five items were asserted by a Play-mode probe â€” and none of it
has run on a phone. Passes D, E and F remain un-device-tested too; one device session covers all four.**

### The model stacking had two causes, and they are the same class of mistake

"The pro cosmetics look like there are 2 models inside each other, one stays static while the other
moves", plus "the enemy troop's sword is attached to the marksman":

1. **`VisionTarget` cached the renderer array at `Awake`** â€” before `UnitClassVisual` swapped in a
   class model and disabled the shared body â€” and `ApplyAlpha` writes `renderer.enabled = visible`
   across that stale list. So the first time the player *saw* an enemy, the hidden trooper (spear
   included) was switched back on inside the class model. `EnemySiegeUnit.prefab` has `VisionTarget`
   and `SiegeUnit.prefab` does not, which is exactly why it presented as an **enemy-only** bug.
2. **`UnitAnimator.Rebind()` re-found the same hidden body.** Its lookup returns the **first** name
   match in `GetComponentsInChildren<Transform>()`, and `Visual` is child 0 while `ClassModel` is
   appended last. Clearing the fields and re-running an unscoped lookup is not the same as re-scoping
   it. **No class model in this game had ever animated, on either team.**

> **Standing rule: any component that caches renderers or child transforms at `Awake` is invalidated
> by the class-model swap and needs an explicit rebind hook.** `UnitTeamTint` and `UnitAnimator` had
> one; `VisionTarget` did not. All the hooks now live in one place at the end of
> `UnitClassVisual.SwapInClassModel` â€” add to that list, do not scatter them.

### Combat: reach-only targeting replaced the symmetric duel

A unit now targets **only what is already inside its own reach, and never chases** â€” acquisition
radius and attack range are the same number, so "close on the opponent" stopped existing. That is what
fixes "a marksman shooting a troop far away should not lock the troop onto the marksman": the Trooper
keeps advancing and stops only when something enters its own 0.06. Targeting is **one-way** â€” being
shot at neither engages you nor stops you â€” which is what lets several units work on one enemy.

**The cap survived, its value did not.** Uncapped focus fire makes losses scale by Lanchester's square
law and "deploy the maximum number of units" strictly correct; a cap of exactly one made combined arms
impossible. Now `maxAttackersPerTarget = 3` with `focusDamageFalloff = 0.6` (100% / 60% / 36%, so
three attackers total **1.96x, not 3x**) and `immediateThreatBias = 0.35` so a unit answers the enemy
walking into it rather than the marksman plinking from behind. **If combat ever feels like an
arithmetic race again, those three values are the first place to look.**

Balance moved with it, both first-guess and underived from play: Marksman `attackTickSeconds`
0.75 â†’ 0.9, Turret 0.6 â†’ 0.7. Reach and damage untouched.

### Line of sight now applies to every damage source

No damage source in this game had ever tested it. `SiegeUnit` and `GarrisonSentry` both call
`LineOfSightController.HasClearLine` â€” one implementation of the sight rule, not two â€” from **measured
mid-heights**, never transform origins (an origin-to-origin ray grazes the board slab and reads as
blocked). `SiegeUnit` re-checks every attack tick, so walking behind a wall genuinely stops the fire.

**Sentries needed one extra move or this would have broken Blind Spire.** A sentry stands on the
ground *beside* the chokepoint it garrisons (the anchor carves a NavMesh hole, so
`SamplePosition(centre)` snaps outside it), so a ground-level ray is blocked by its own tower.
`MusterPhaseController` now passes the anchor's measured top as `GarrisonSentry.SetVantage`, and
`SentryFireVisualizer` fires from the same point. **Known and deliberate:** `SentryArcVisualizer` still
draws an un-carved wedge, so the drawn arc now slightly over-promises behind tall terrain.

### Tracers come from the barrel, not from a number

`SiegeUnit` fired from `engagementRadius * 0.12` above the origin â€” a height derived from *reach*,
unrelated to the model, which put the Turret's muzzle visibly below its own gun. New `UnitMuzzle`
resolves the weapon part by name (`Rifle`, `BarrelL/C/R`, `Spear/Halberd/Blade`, `WeaponArm`) and
returns the forward-most point of its **measured** bounds, per shot, so it follows the animated arm.

### The Veteran skins are new models, not the base plus greebles

Rebuilt in a new `ScrapSiege_VET_v3` collection (v2 **moved**, not deleted, to a hidden `OLD_VET_v2`):
Standard Bearer, Aegis, Longshot, Infiltrator, Bastion â€” each a different silhouette at its base
model's **exact** height. Colour comes from three new never-tinted `MaterialSlots` roles (`U_Gold`,
`U_Steel`, `U_Glow`), because `U_Body` belongs to the team colour and that read is load-bearing.
**A first attempt used gold as slabs and buried the team colour â€” all five read as "beige unit".
Veteran palette is trim, never mass.**

Two Blender export rules earned here:
- **Author with every object rotation at identity** and tilts baked into the vertices. That removes
  the rotated-parent and `matrix_parent_inverse` traps rather than auditing for them afterwards.
- **Zero the rig root's location before exporting.** The authoring row offset was being written into
  the FBX root, so every Veteran imported four metres off the origin. Gameplay never saw it
  (`UnitClassVisual` overwrites `localPosition`), which is what makes it a trap rather than a bug.

### Per-class motion, and the other half of "the attack animation seems weird"

A Marksman was playing the spear THRUST written for the Trooper â€” a rifle-armed figure lunging to stab.
`UnitClass.motion` / `.proMotion` now carry gait plus an `AttackStyle`: **Thrust** (Trooper, Saboteur),
**Recoil** (Marksman, Turret â€” the body kicks *backward*), **Brace** (Bulwark), **Swipe** (the Standard
Bearer's halberd). Veterans get their own gait. An unauthored profile falls back to the old defaults.


---

## Current state (2026-08-14) — Pass H: the Pass G device report + closed-testing polish. EDITOR-VERIFIED, NOT DEVICE-TESTED

Full writeup is `plan.md` **Section 14**. Three device-reported items, a self-directed QoL pass, and an
abuse review of the purchase paths ahead of Play's mandatory 14-day / 12-tester closed test.
**Both assemblies compile clean (0 errors), a Play-mode probe asserted every item with measurements,
`ARTest.unity` is unmodified, and none of it has run on a phone.**

### The sentry bug had been shipping since authored levels landed

"I cannot see the sentry and it does not look like it is shooting" was **two** bugs.

**Every sentry stood inside the tower it garrisoned.** `MusterPhaseController` sampled the NavMesh at
the chokepoint's own centre, trusting the piece's `NavMeshObstacle` to have carved a hole that pushed
the sample out. Carving is a **deferred runtime update** and `SpawnGarrison` runs in the same
synchronous call as the build and the bake, so no hole exists — and even with one, the snap radius
(`0.04 x boardLength = 0.12`) is smaller than the spire half-width it would have to escape (`0.1725`).
The sentry therefore sat inside an opaque, sight-blocking spire: invisible on screen, and permanently
`Hidden` to `VisionTarget` because its own tower blocked every camera ray.

> **Rule earned: never rely on NavMesh carving having been applied in the frame the obstacle was
> created.** Anything that needs to be placed *outside* a piece of terrain must compute that from the
> footprint, not ask the NavMesh.

`TryFindStation` now stands the sentry clear of the measured footprint on the threat-facing side, and
re-checks the sampled result against both the margined footprint and
`SiegeLayers.TerrainOccluderMask` — the same mask `LineOfSightController` raycasts, so "can the player
see it" is answered by the layer that actually decides it rather than by a second archetype list.
Verified: `0.44` clear of the spire, inside **0** occluders, `HasClearLine` from a player eye **true**.

**And it had almost nothing it was allowed to shoot.** `coverLaneMargin` was the last absolute distance
in the project (Open Question 3, deferred since 2026-08-08 — closed now because the user was reporting
its symptom). At `0.05` real metres per side the wall laid a lane `0.6725` wide and each rubble pile
one `0.8105` wide, on a board `1.65` wide: cover blanketed the left edge to just past centre, and only
**14% of the board width** was both uncovered and inside the sentry's reach. Now
`0.035 x boardLength` (lanes `0.3825` / `0.5205`) with `detectionRadiusFraction` `0.20 -> 0.26`, which
puts about a third of the board width under real threat.

### The Turret has a clock now, and that matters more than its damage

`attackDamage` 5 -> 4 and `attackTickSeconds` 0.7 -> 0.8 (**7.14 -> 5.0 DPS**, -30%); `cost` 4 -> 3;
new `lifetimeSeconds` **12** with the last **1.8s** a visible breakdown. Reach and health untouched —
reach is the class's identity and tick rate is the correct dial (the standing rule from Pass E).

The timer is the real fix. Reach-only combat means nothing chases and an emplacement never advances,
so a turret placed off the AI's line of advance **cannot be answered at all** and they accumulate for
a whole match. It was already the one Pro perk touching power. A clock makes it a window of denial you
have to keep paying for. It was *already* targetable by hostile units (`FindTarget` filters only on
team/alive/reach/sight); what was missing was any way to make something reach it.

The breakdown is animated rather than a despawn, because a unit that blinks out is indistinguishable
from a bug — the same reason `UnitDeathEffect` exists. It stops firing, frees the attacker slot, sags
and topples on a per-unit axis with accelerating sparks, then comes apart through the normal death
debris. Applied to `UnitClassVisual.ActiveModelRoot`, **never** to the unit's own transform, which
carries the NavMeshAgent and is written by the facing code. An expiring turret is not counted as a
loss in `MatchStats` — it is a cost the player chose.

Verified with the lifetime temporarily at 3s/1.2s: breakdown at exactly `t=1.80s`, destroyed at
exactly `t=3.00s`, `PlayerUnitsLost` still 0.

### The square stand under every unit is hidden — for mobile classes only

Every model in `Assets/Models` has a `Base` plate (4-8% of its height, ~half its width) so it stands
up in Blender and in the `docs/art/` lineups. On a board that reads as a chess-piece base sliding
around under a soldier. `UnitClassVisual.ApplyGroundPlateRule` hides it; **emplacements keep theirs**
(a plate under a bolted-down turret is a mount) and sentries need no rule because
`MusterPhaseController` never applies a class to them.

Three things keep this from repeating the Pass F/G model bugs:

- **Disabled, not destroyed.** `Base` is a real FBX node other parts may be parented to, and
  `UnitDeathEffect` / `VisionTarget` already skip disabled renderers, so they inherit the decision.
- **Ordering is load-bearing:** after the swap path's height normalisation (measuring without the
  plate would scale every unit up by its share of the height) and before its grounding (the shared
  trooper's legs start a quarter of the way *up* its plate — measured, not assumed).
- The **no-model path** is handled: the Trooper has no `modelPrefab` and used to return before any of
  this, so `ActiveModelRoot` and the plate rule now cover the shared body too.

Verified on all five classes: hidden on the four mobile ones, visible on the Turret, foot offset
exactly `0.00000` for every one.

### Star ratings, from values that were already authored

`parTimeSeconds` / `parUnitsLost` have been on every level since levels existed and were read by
**nothing**. `LevelDefinition.StarsFor` + new `MatchStats` (clock starts at siege start, not scene
load — nobody should be graded on how long their table took to scan) + new `LevelProgress`
(`PlayerPrefs`, best-ever per level). Shown in the outcome card's existing body label and appended to
level-select card titles, so **no scene authoring was needed**. A defeat gets the summary and no
rating.

### Purchase paths are rate limited now

Entitlements were never forgeable — they come from RevenueCat's backend against a Google-signed
receipt, and `ProEntitlement` is written only from `customerInfo`. But three call sites were unbounded
in volume: **Restore had no in-flight guard at all**, focus-driven `GetCustomerInfo` fired once per
focus change, and the paywall refetched offerings on every panel open. `MonetizationManager` now
refuses overlapping store operations outright (so a future second screen cannot reintroduce it),
throttles refreshes to 20s and offerings to 10s, and `PaywallController` disables Restore for the
duration like Subscribe already did. Full reasoning in `SECURITY.md`, 2026-08-14 entry.

### Read before touching this work

- **Device-test it before building on it.** Passes D/E/F/G/H have now all been through the Editor and
  only D-G have been on a phone.
- `plan.md` Section 14 has the numbers; the balance values in it are first guesses from measurement,
  not from play.
- **The privacy policy is written but not published.** See the section below.

---

## Current state (2026-08-14, later) — the privacy policy, the last non-code publishing blocker

Full writeup is `plan.md` **Section 15**. Written immediately after Pass H, in the same uncommitted
tree.

**New files:** `docs/privacy/index.html` (the authoritative text, a self-contained static page for
GitHub Pages), `docs/PRIVACY_POLICY.md` (summary + maintenance rules), `docs/index.html` (a site root
so Pages isn't a bare 404), `docs/.nojekyll` (static deploy, no Jekyll). No Unity assets touched, no
scene touched, nothing to compile.

**Every factual claim in it was checked against the code before being made public** — no analytics/ads
SDK in `manifest.json` or `Assets/`, `MonetizationManager` never calls `LogIn` or sets subscriber
attributes (so RevenueCat only sees `$RCAnonymousID:…`), `PlayerPrefs` is written only by
`LevelProgress` and `GameAudio`, and the six permissions come from `SECURITY.md`'s artifact-verified
allowlist. `plan.md` Section 15 has the verification table.

> **Standing rule added to `SECURITY.md` section B: the permission allowlist and section 5 of the
> policy are one list written twice.** Add a permission, edit both in the same change. A shipped app
> declaring more permissions than its policy discloses is a Play policy violation, and the mismatch
> is trivially machine-checkable by Google. Same applies to section 2 if the RevenueCat identity model
> ever changes.

Contact address `miksdevstudio@gmail.com` was filled in on 2026-08-14. **Superseded the same day by
the strategy change below: with no store release there is no Play Console field and no Data Safety
form, so the policy blocks nothing.** It is kept and kept accurate because the app really does take
money and the repo is judged. Hosting it on GitHub Pages is optional polish now.

---

## ⚠️ Current state (2026-08-14, final) — SUBMISSION ROUTE CHANGED: Next Gen only, no store release

Full reasoning is `plan.md` **Section 16**. **Read this before proposing any Play Console, closed
testing, store listing or release-build work — that entire workstream is cancelled for this project.**

**The decision:** Scrap Siege is submitted **only** to the Shipaton 2026 **Next Gen** (student) award.
Google Play production — 12 testers x 14 days of closed testing, Data Safety, listing assets, review —
is abandoned. The user is spending that effort on a separate app aimed at the Influencer award.

**Why it is safe (verified against the official rules before acting, because the failure mode is
total):** every other track requires the app's first public version to go live on a real store inside
the submission window, and a closed/internal test explicitly does **not** count. Next Gen is the sole
exemption — **"no paid Apple or Google developer account or store release is required"**, replaced by
*"a demo video and a link to your public, open-source code repository, including an open-source
license file."* Two attached conditions are **not satisfiable from the repo**: active student
enrolment, and a **qualifying student/academic email on the Devpost account**.

**The RevenueCat SDK requirement is NOT waived** — and does not need to be. It was met on real
hardware with real money in August.

### Do not adopt the Test Store — it was investigated and rejected on evidence

The obvious move is swapping the `goog_` key for the project's Test Store key so a sideloaded build
demos a purchase. The dashboard already supports it (the `$rc_monthly` package carries **both** the
Play product and the Test Store product). **But Test Store needs `purchases-android` 9.9.0+, and this
project is on `com.revenuecat.purchases-unity` 7.4.1 → `purchases-hybrid-common 13.14.0` (the 8.x
line), with zero Test Store references in the installed package.** It is the deferred major SDK
upgrade, not a key swap — and it would replace a *proven real purchase* with a simulated one, risking
the one requirement that must hold. **Keep 7.4.1 and the real `goog_` key.**

### What changed in code — deliberately almost nothing

- **`LICENSE`** (new, MIT) — required explicitly by the Next Gen rules and previously **absent**.
  That was a submission defect independent of the store decision.
- **`PaywallController.LoadOffering`** — both failure paths now say *"Store unavailable. Subscribing
  needs a Google Play install + connection."* With no store release, a sideloaded APK is the normal
  delivery, and Play Billing serves no product details to a package it did not install; the old flat
  "Offer unavailable right now." read as a broken paywall. `Assembly-CSharp` compiles clean.

### What this promotes

**The demo video is now the only way a judge sees the game run**, so it is the primary deliverable
rather than the last polish item — still recorded last, per the standing instruction, but no longer
something to leave to the final days. Device-testing Pass H is still the top technical risk.
