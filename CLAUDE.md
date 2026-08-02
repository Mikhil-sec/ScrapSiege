# Scrap Siege — Project Context for Claude Code

You are helping build **Scrap Siege**, a Unity mobile AR game for the RevenueCat Shipaton 2026 hackathon (Next Gen student track). Read this file fully before writing any code. This file should live at the root of the Unity project and be read automatically at the start of every session.

## What this project is

An augmented-reality tabletop battle game. Two players each arrange real objects from their table (mugs, books, phone stands) as terrain, the app scans the arrangement into gameplay archetypes (cover / chokepoint / barricade), and they fight a real-time skirmish across it. Full design detail — mechanics, terrain classification rules, match structure, monetization, timeline — is in `plan.md` in this same repo. **Read `plan.md` before starting work if it's present; it is the source of truth for game design.**

## Hard constraints — do not violate these

- **Zero AI/ML features in the app.** No on-device ML, no object recognition, no generative anything. Terrain classification is rule-based computational geometry only (bounding box, height, footprint aspect ratio) — never a learned model. This is a firm design constraint, not a technical shortcut.
- **Must integrate the RevenueCat Unity SDK** powering at least one real in-app purchase (subscription and/or cosmetic pack).
- **Original mechanics only** — do not let the core loop drift back toward being a plain Clash Royale clone. The terrain-scavenging, camera-height trade-off, and physical-table mechanics are the point.

## Development environment (know this before suggesting steps)

- **OS:** Windows/Linux, no Mac available.
- **GPU:** Intel Arc integrated graphics, 32GB RAM. Fine for Editor work and URP; don't suggest GPU-heavy workflows unnecessarily.
- **Primary test device:** an **Honor phone with no depth sensor** — this is the daily dev/test device. This means **Tier B (manual box-tagging) terrain detection is the primary path to get right first**, not a fallback to deprioritize. Tier A (depth-based) is a bonus for devices that support it, not the baseline.
- **Platform priority: Android first, and Android must be fully solid on its own.** iOS is a stretch — it will eventually be built via a cloud Mac CI service (e.g. Codemagic) since there's no local Mac, and tested on a borrowed iPhone late in the project. Do not assume Xcode/macOS is available; do not suggest steps that require running on a Mac.
- **Editor:** Unity 6 LTS (or 2022 LTS if compatibility issues arise), URP, AR Foundation. VS Code as the script editor.
- **Version control:** Git (this repo). The repo must stay public and reasonably clean since it's a required Devpost submission asset — commit in sensible units, not one giant dump at the end.

## How to work in this project

- Write real C# scripts into the correct `Assets/` subfolders, following standard Unity project conventions (Scripts/, Prefabs/, Materials/, Scenes/, etc.) — create folder structure as needed.
- After editing scripts, tell me to switch to the Unity Editor to let it recompile and test in Play mode or on-device — you can't run the Unity Editor or trigger builds yourself, so hand off testing steps clearly and wait for feedback (console errors, behavior on device) before continuing.
- Favor simple, debuggable systems over clever ones — this is a solo 6-week build with real hardware-testing bottlenecks (Gradle build/deploy time to the Honor phone will be the slowest part of the loop), so minimize churn per iteration.
- Follow the risk-ordered timeline in `plan.md` Section 7: Cloud Anchor cross-device spike and AR plane detection come first, before deeper gameplay systems, so the riskiest unknown is validated early.
- When a design decision in `plan.md` is ambiguous or a stretch goal, default to the simpler/fallback version named in the plan (e.g. timer-based resources over explore-to-earn, tap-to-place over gesture summoning) unless told otherwise.

## First task

Start with **Week 1** from the timeline: set up the Unity project structure, add AR Foundation + ARCore XR Plugin packages, get basic plane detection working on the Honor phone, and scaffold the Cloud Anchor cross-device spike (this can be stubbed/structured first, actual two-device testing happens once I have a second Android phone available). Confirm the plan with me before writing code, then proceed.
