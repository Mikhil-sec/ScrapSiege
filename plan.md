# Scrap Siege — Design & Build Plan

## 1. The Hackathon

This project is being built for **RevenueCat Shipaton 2026**, a hackathon-style competition run by RevenueCat/Devpost.

- **Submission window:** 2026-08-01 to 2026-09-30.
- **Target category:** **Next Gen Award** (student-only, requires a .edu or equivalent email; judged on video + open-source code, **no store release required**, no paid developer account needed).
- **Required at submission:**
  - A **demo video**, max **2 minutes of essential footage**, publicly on YouTube or Vimeo. Must show the app running on the device it was built for. No third-party trademarks or unlicensed music.
  - A **public open-source repository**.
  - **Hard technical requirement for all entrants:** the app must integrate the **RevenueCat SDK** powering **at least one in-app purchase**.
- **Personal goals (from the builder):** must be genuinely impressive, not a minimal-effort submission; must work on the phone brands common in Mauritius (Samsung, Honor, iPhone); ambition should not be artificially minimised, but risk must be managed deliberately.

## 2. Direction History — and why the current design exists

This section is deliberately kept, because the reasoning behind the pivots is itself part of the project story.

1. **AI-generated content apps** — rejected. The builder wanted zero AI features; tired of AI being force-fit into everything.
2. **UWB-based outdoor tag game** — rejected. UWB isn't universal (absent on the builder's own Honor), and the real-time fast-movement nature made cross-platform reliability risky.
3. **Two-player AR tabletop battler with scavenged terrain** — *built, then abandoned 2026-08-07.* Players arranged real objects as terrain and fought across a network-synced board. Weeks 1–2 (terrain scanning, pathing, siege loop) and a full Week 3 LAN implementation all worked as code. **What killed it was AR plane detection**: across floor, cushion table and dining table, the app could not reliably produce a lockable surface, which made the shared-board co-location step too fragile to build a match on. Preserved on the `two-player-archive` branch.
4. **Current direction — single-player, projected maps.** Hand-designed battlefields projected onto any flat surface, fought against a rule-based AI commander. Removes the two hardest dependencies at once (cross-device co-location, and reliable scanning of arbitrary real objects) while keeping the AR-native identity.

## 3. The Concept

**One-line pitch:** A tabletop war game that only exists on *your* table — project a battlefield onto any real surface, then out-think an AI commander by physically moving around the board, leaning in to place troops precisely and pulling back to read the whole fight.

**Why this is worth building:**
- **The AR is load-bearing, not decorative.** Your physical vantage point changes what you can do and what you can see (Section 4). Take away the camera and the game stops working — which is exactly the bar an AR entry should clear.
- **Robust by construction.** It needs one flat surface and nothing else. No second device, no cloud anchor, no scanning of arbitrary objects, no internet. Every failure mode that killed the previous direction is gone.
- **Demo-friendly.** Tabletop scale, filmable anywhere, and a match can be shown start-to-finish inside a 2-minute video.

## 4. Core Mechanics — what makes this not a flat-screen game

The previous design's originality rested on scavenged terrain. With authored maps, the identity now rests on the two mechanics below. **These are the priority; protect them.**

### Mechanic 1 — Vantage (must-have, the headline)

The phone's real position relative to the board is a continuously-read gameplay input. There is no UI toggle — your body is the control.

| Posture | Placement precision | Field of view | Trade-off |
|---|---|---|---|
| **Leaned in** (low, close) | Tight — deploy lands where you tap | Narrow; can't see the far side | Precise but blind to flanks |
| **Pulled back** (high, distant) | Loose — deploy scatters within a radius | Whole board readable | Aware but imprecise |

Implementation: read camera height above the board plane each frame, map it to a deploy-scatter radius and (optionally) a subtle vignette/uncertainty overlay. Continuous, not stepped, so leaning is a fluid act rather than a mode switch.

### Mechanic 2 — True line of sight (must-have)

Enemy units are only revealed when there is a clear line from the **actual camera position** to them, blocked by Wall/Spire/Watchtower terrain. Unseen enemies leave a fading "last known position" marker.

This means peeking round a virtual wall is done by *physically leaning*, and a spire genuinely hides what's behind it from where you're standing. Implementation is a per-unit raycast from the AR camera against terrain colliders — cheap, deterministic, no ML.

### Mechanic 3 — Route variety (already built, keep)

Two deploy modes with a real risk/speed trade-off:
- **Direct** — the CoverLane NavMesh area is excluded from the agent's areaMask, so units take the shortest open route.
- **Covered** — default areaMask, so NavMesh's own cost-based pathing detours through the cheap CoverLane polygons laid down beside Rubble/Wall terrain.

`GarrisonSentry` is what makes the choice matter: it only damages units **not** standing in a CoverLane area. This system works today and needs no redesign — authored maps simply place the cover that drives it.

### Mechanic 4 — Flank by walking (nice-to-have)

Garrison sentries cover a facing arc rather than a full circle. Physically walking to another side of the table lets you deploy into a weaker arc. Cheap to add on top of Mechanic 2 and it rewards the same physical behaviour.

### Terrain archetypes (retained from the previous design)

Authored maps are built from the same five archetypes, so all the downstream systems — spawner, NavMesh obstacle carving, CoverLane tagging, garrison placement — carry over unchanged:

| Archetype | Gameplay role |
|---|---|
| Wall / Barricade | Hard block, blocks line of sight |
| Spire / Chokepoint | Hard block, tall, blocks line of sight, garrison anchor |
| Rubble / Cover | Passable, lays a CoverLane, blocks nothing visually |
| Plain Obstacle | Hard block, low |
| Watchtower | Bonus tier — garrison anchor with a wider sentry arc |

**No AI, by design:** classification is gone (maps are authored), but the constraint still holds across the whole app. The AI commander in Section 5 is rule-based game AI, not machine learning.

## 5. The AI Commander

A rule-based opponent — explicit thresholds and utility scoring, no learned model, fully debuggable.

- **Symmetric economy.** The AI ticks resources on the same schedule as the player, so difficulty comes from decision quality and tuning rather than cheating.
- **Behaviour loop** (evaluated on a slow tick, ~1s): score each candidate action — reinforce a threatened lane, push the player's weakest-defended approach, hold resources for a bigger wave — and take the best.
- **Difficulty tiers:** resource rate, reaction delay, willingness to commit, unit mix. Tune per level.
- **Readability matters.** The player should be able to *see* the AI reacting; telegraphing a push is better than optimal play. This is a demo-video game as much as a strategy game.

## 6. Levels

Hand-authored, stored as ScriptableObjects (`LevelDefinition`), so adding content needs no code:

- Terrain placements in **normalised board space** (board is 1.0 long × ~0.6 wide, scaled at runtime to whatever surface the player has). Each entry: archetype, position, rotation, size.
- Both base positions, starting resources, AI difficulty params, and star thresholds (time / units lost / base HP remaining).
- Ship ~6–8 free campaign levels plus Pro packs (Section 7).

**Placement flow:** find and lock a flat surface → tap to drop the board → drag to reposition, two-finger twist to rotate, pinch to scale → confirm. `BoardFrameVisualizer` (already written) shows the footprint before committing.

## 7. Monetization (RevenueCat)

**Already built and working** — do not break it. Full details:

- **Project:** "ScrapSiege" (`proj3a523262`). Entitlement `pro`.
- **Test Store** (`appda5538b8e2`) — product `scrap_siege_pro_monthly` ($2.99/mo), attached to `pro`, in the `default` offering's `$rc_monthly` package. Works in Unity Editor Play Mode (mock wrapper). A real Android build always goes through actual Google Play Billing regardless of which RevenueCat app the key belongs to, so Test Store products don't resolve on-device.
- **Play Store** (`appa37d9670f8`, package `com.mikhilnaika.scrapsiege`) — app entry created, **no product yet**, blocked on a Google Play Console account. Reserved naming: product ID `scrap_siege_pro`, base plan `monthly`, giving store identifier `scrap_siege_pro:monthly`.
- **Unity code:** `Assets/Monetization/` (`MonetizationManager`, `PaywallController`) sits deliberately **outside** `ScrapSiege.Runtime.asmdef` because the RevenueCat SDK ships with no asmdef. `Assets/Scripts/Monetization/ProEntitlement.cs` is the decoupled gate gameplay reads instead of touching the SDK.

**What Pro unlocks (revised for the new direction):**
- **Level packs** — the natural fit now that content is authored, and a much better IAP story than before.
- Cosmetic board themes and the saturated terrain palette (`TerrainObjectSpawner.ProColorForArchetype`, already shipped).
- Extra visual effect packs.

Still genuine value-tier gating — more content, not a functionality lockout.

## 8. Timeline (~7.5 weeks remaining, submission 2026-09-30)

Ordered so the new-direction risk is retired first and the demo video is never the thing that slips.

- **Week A (Aug 8–14) — Make the new loop exist.** `LevelDefinition` ScriptableObject + board placement flow (tap/drag/rotate/scale/confirm) replacing the Fortify phase. Spawn an authored map and get the existing Siege loop running on it end-to-end. Two throwaway test levels.
- **Week B (Aug 15–21) — Make it a game.** Player base + real **Lose** condition (missing since Week 2 of the old build). AI commander v1. Win/lose flow, level select screen.
- **Week C (Aug 22–28) — Make it AR.** Mechanic 1 (vantage) and Mechanic 2 (line of sight). These are the originality argument; do not let them slip.
- **Week D (Aug 29–Sep 4) — Content.** 6–8 authored levels, star ratings, difficulty tuning, Mechanic 4 if time allows.
- **Week E (Sep 5–11) — Monetization + store.** Gate Pro level packs behind the existing entitlement. Google Play Console product, Internal Testing track, real on-device purchase verified.
- **Week F (Sep 12–18) — Polish.** Terrain art ("Cartoonify"), VFX, sound, HUD pass.
- **Week G (Sep 19–30) — Ship.** Demo video, icon, screenshots, `PROJECT_STORY.md`, repo cleanup, buffer.

## 9. Known Risks & Mitigations

- **AR plane detection is the project's proven weak point.** It has failed repeatedly on real surfaces. Mitigations: accept a *small* seed plane rather than demanding a large validated one (threshold now 0.02 m²); fall back to estimated planes and feature points for raycasts; scan diagnostics are logged every 2s (`adb logcat -d -s Unity:V | grep PlaneLock`) so failures are explainable rather than mysterious. **If it still proves unreliable, the escape hatch is to let the player place the board at a fixed distance in front of the camera with no plane at all** — worse UX, but it cannot fail.
- **Losing scavenged terrain weakens the originality pitch.** Mitigated by making vantage + line-of-sight the headline mechanics (Section 4) and saying so explicitly in the demo video and `PROJECT_STORY.md`. Judges should see AR doing something a flat screen cannot, within the first 30 seconds.
- **Authored content is a time sink.** 6–8 good levels is the target, not 20. Build the level format early (Week A) so authoring is cheap later.
- **Google Play Console product still blocks real on-device purchases.** Not required for Next Gen submission, but needed for a fully honest monetization demo.
- **Cross-brand ARCore support varies.** Verify test devices against Google's ARCore supported-devices list before relying on them for the demo.
- **iOS deferred.** No Mac; Android-to-completion is the priority. Possible later via cloud macOS CI, not a blocker.

## 10. Judging Criteria — Next Gen Award

- Does the submission show a genuinely new or unexpected use of the platform? → **Mechanics 1 and 2 are the answer.** Lead with them.
- Does it show thoughtful technical choices, product thinking, and care in how it was built and presented? → the pivot reasoning in Section 2 is an asset here, not an embarrassment: it shows risk being measured and acted on.
- Does it integrate RevenueCat meaningfully? → level packs behind a real entitlement, already wired.

**Secondary targets:** RevenueCat **Design Award** (rides on the Week F polish pass and the already-rebuilt HUD) and the **HAMM Award** (rides on the monetization work being done thoughtfully rather than bolted on).

### Submission checklist
- [ ] App built and demoed (no store listing required for Next Gen)
- [ ] RevenueCat SDK powering at least one IAP
- [ ] Public open-source repo, reasonably clean
- [ ] Demo video ≤2 min, public, no unlicensed material
- [ ] `PROJECT_STORY.md` written up for the Devpost story field
