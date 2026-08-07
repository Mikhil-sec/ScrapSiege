# Scrap Siege — Project Context for Claude Code

You are helping build **Scrap Siege**, a Unity mobile AR game for the RevenueCat Shipaton 2026 hackathon (Next Gen student track). Read this file fully before writing any code. It lives at the root of the Unity project and should be read at the start of every session.

## What this project is

A **single-player** augmented-reality tabletop battle game. The player points their phone at any flat surface — a desk, a dining table, the floor — and a hand-designed battlefield is projected onto it at real scale. They then fight a real-time skirmish against a rule-based AI commander across that board.

The hook is that **your phone's physical position is a tactical resource**: lean in close for precise unit placement, pull back for a commander's overview, and physically move around the table to see behind cover or flank a defended side. That is the thing a flat-screen game cannot copy, and it is what this project is now built around.

Full design detail — mechanics, level format, AI behaviour, monetization, timeline — is in `plan.md` in this repo. **Read `plan.md` before starting work; it is the source of truth for game design.**

### ⚠️ This project pivoted on 2026-08-07 — do not restore the old design

It was previously a **two-player** game where each player scanned real objects on their table (mugs, books) as terrain, synced over the network. Both halves of that are gone:

- **Two-device play was abandoned.** A full LAN implementation (Netcode for GameObjects, shared board alignment, replicated terrain, host-authoritative siege) was built and works as code, but AR plane detection could not reliably produce a lockable surface across floor, cushion table or dining table. Co-location was too fragile to build a match on.
- **Terrain scanning is being replaced** by pre-authored maps projected onto a surface.

That work is **not deleted** — it is preserved on the `two-player-archive` git branch (commit `5d05fc3`). Do not re-add networking to `main`, and do not reintroduce object scanning as the primary flow, unless explicitly asked.

## Hard constraints — do not violate these

- **Zero AI/ML features in the app.** No on-device ML, no object recognition, no generative anything, no learned models.
  - **This does not ban the AI opponent.** The "AI commander" is a rule-based/utility-scored opponent — ordinary game AI, hand-written logic with explicit thresholds. That is fine and is the point. What is banned is *machine learning*: neural nets, trained classifiers, ML Kit, on-device inference. Keep opponent logic readable and deterministic enough to debug.
- **Must integrate the RevenueCat Unity SDK** powering at least one real in-app purchase. This is already built and working (see `plan.md` Section 6) — do not break it.
- **Original mechanics only.** The core loop must not drift into being a plain Clash Royale clone. Since scavenged terrain is gone, the originality now rests on the **vantage/camera-height mechanic** and **true line-of-sight from the player's real viewpoint**. Protect those; they are the answer to "why is this AR and not a flat game."

## Development environment (know this before suggesting steps)

- **OS:** Windows, no Mac available. Intel Arc integrated graphics, 32GB RAM.
- **Unity:** 6000.5.6f1, URP, AR Foundation 6.5 + ARCore XR Plugin. VS Code as script editor.
- **Test devices:** a Samsung SM-A566B (Galaxy A56) and an Honor phone with **no depth sensor**. Neither has depth, so plane detection and plane raycasts are the only spatial input — never propose a depth-sensor-dependent path as primary.
- **AR plane detection is the known weak point of this project.** It has repeatedly failed to produce a usable surface on real tables. Anything that *requires* a large, high-quality plane is a risk; prefer designs that work off a small seed plane, estimated planes, or feature points.
- **Platform:** Android-first and must be fully solid standalone. iOS is a stretch via cloud macOS CI (e.g. Codemagic) — never suggest steps needing local Xcode/macOS.
- **Version control:** Git, and the repo must stay public and clean (it is a required Devpost asset). `main` = the single-player game. `two-player-archive` = the abandoned two-device build.

## Tooling available to you

- **Unity Editor MCP** (`unity-mcp`): `Unity_RunCommand` compiles and runs arbitrary C# in the live Editor (full scene/GameObject/component read-write), plus console log reads and scene/camera capture. Prefer this over walking the user through manual Editor clicks. Caveats: the sandbox **blocks the `System.Reflection` namespace**, and the project defines a namespace literally called `Image`, so always fully-qualify `UnityEngine.UI.Image`/`Button`.
- **RevenueCat MCP**: configure the dashboard directly rather than instructing the user through the website.
- **adb** is available without a PATH entry at
  `C:\Program Files\Unity\Hub\Editor\6000.5.6f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe`.
  **Use it.** Several bugs in this project were invisible in the Editor and only diagnosable from `adb logcat -d -s Unity:V`. When on-device behaviour is wrong, pull logs before theorising.

## How to work in this project

- Write real C# into the correct `Assets/` subfolders following Unity conventions.
- You **can** drive the Unity Editor via MCP, but you **cannot** trigger Gradle builds or deploy to the phone — hand off build/test steps clearly and wait for logcat or user feedback before continuing.
- Favour simple, debuggable systems. This is a solo build with a slow build-deploy loop; minimise churn per iteration.
- Guard every Inspector-wired reference with a null check that logs loudly — a missing reference silently aborting a method has already cost this project real debugging time twice.
- Subscribe to `UnityEvent<T>` **in code**, not via the Inspector dropdown: the dropdown offers a static-parameter variant that bakes in a constant and silently breaks value pass-through.
- Check `plan.md` Section 8 (Known Risks) and the gotchas list before debugging AR/NavMesh/URP symptoms — most of them have bitten this project already.

## Current state

The single-player skeleton from the pre-pivot build still runs: plane scan + lock, a working Siege phase (resource economy, tap-to-deploy, NavMesh pathing with Direct/Covered route variety, garrison sentries, a destroyable base, win condition), a designed HUD, and the RevenueCat paywall. What is **not** yet built is everything specific to the new direction — see `plan.md` Section 7 for the ordered task list. Start there.
