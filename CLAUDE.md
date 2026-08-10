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
