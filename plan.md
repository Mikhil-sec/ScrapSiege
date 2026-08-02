# Scrap Siege — Project Plan

## 1. Background & Context

This project is being built for **RevenueCat Shipaton 2026**, a hackathon-style competition run by RevenueCat/Devpost.

- **Submission window:** the app's first version must be created/demoed between **August 1 and September 30, 2026**.
- **Category: Next Gen Award** — a student-only track. This means:
  - **No paid Apple/Google developer account or store listing is required.**
  - Submission is via **Devpost**, consisting of:
    - A text description of the app's features and functionality.
    - A **demo video**, max **2 minutes of essential footage**, uploaded publicly to YouTube or Vimeo. Must show the app running on the device it was built for. No third-party trademarks or copyrighted music/material without permission.
    - A link to a **public, open-source code repository**.
    - A **1024×1024 app icon**.
    - At least one **screenshot at 1179×2556px, no device frame**.
    - Either a **free trial** in the app, or a **promo code**, so judges can unlock and test the in-app purchase / premium features.
  - Judged on the video + open-source code (no store release needed), but Next Gen entries also remain in the broader Shipaton prize pool.
- **Hard technical requirement (applies to all entrants, including Next Gen):** the app must integrate the **RevenueCat SDK** to power **at least one in-app purchase**.
- **Personal goals for this project (from the builder):**
  - Must be **original** — not a re-skin of an existing well-known game or app concept.
  - Must be genuinely **impressive / "wow" level**, not a minimal-effort submission.
  - **No AI/ML features inside the app.** This is an explicit, firm constraint — no on-device ML, no generative AI, no "AI-powered" anything as a marketed or functional feature.
  - Should work across the phone brands actually common where the builder lives (**Mauritius**): **iPhone, Samsung, and Honor** are all significant. Cross-brand compatibility is a real design goal, not a nice-to-have.
  - The builder has access to **Claude (Pro) as a coding agent** and expects to lean on it heavily for implementation — so ambition/complexity should not be artificially minimized, but risk should still be managed deliberately (see Risks section).

## 2. How We Got Here (idea evolution, for context)

1. **First direction (rejected by builder):** AI-generated content apps (an ambient-sound-to-generative-art app, then an AI-illustrated autobiographical storybook app). Rejected because the builder wants **zero AI features** in the app — tired of AI being force-fit into everything.
2. **Second direction (rejected on compatibility grounds):** "Foxhunt" — a real-world radar tag game using UWB (Ultra-Wideband) precision ranging (Apple Nearby Interaction / Android Jetpack UWB). Investigated and found:
   - UWB hardware is **not** universal: present on all iPhone 11+, only present on **premium/Ultra-tier Samsung** phones, and **absent on current Honor flagships** (including the builder's own phone).
   - Apple's and Android's UWB stacks are **not interoperable** with each other out of the box.
   - Even with a tiered fallback (UWB → Bluetooth RSSI proximity), the **real-time, fast-movement nature of a tag game** made reliable cross-platform play risky to build well in the timeframe.
3. **Current direction (in progress):** Pivoted to a **static, tabletop AR battle game** instead of a fast-movement outdoor game. This removes the real-time proximity/latency problem entirely and uses **AR plane-tracking + shared anchors**, which work across nearly all modern smartphones — a much better fit for "works across iPhone, Samsung, and Honor."
4. **Originality check:** the initial tabletop AR concept mechanically resembled **Clash Royale** (two-lane, resource-based real-time unit deployment toward an opposing base). The design was reworked around mechanics that are only possible because this is AR on a real physical table (see Section 4) — specifically to avoid being "Clash Royale with an AR skin."

## 3. The Concept: Scrap Siege

**One-line pitch:** An augmented-reality tabletop battle game where two players — on any two phones, any brands — build their battlefield out of whatever's actually sitting on the table, then fight a real-time skirmish across it. The terrain is different every single match because it's made from real objects, not a designed map.

**Why this is the right project:**
- **Genuinely original mechanic**, not just an AR reskin of an existing game genre (see Section 4 for exactly how it differs from Clash Royale, which is the closest comparison).
- **No AI anywhere in the stack** — pure AR (plane detection, shared spatial anchors), real-time networking, and game logic.
- **Cross-platform by construction:** built once in Unity + AR Foundation, targets Android first (with iOS as a later addition — see Section 8) from one codebase, and uses Google's ARCore Cloud Anchors (confirmed still active and cross-platform as of 2026) to sync two devices to the *same* physical anchor point — meaning a Honor player and a Samsung player (assuming both support ARCore — most modern Honor/Samsung devices with Google Play Services do) can play on the same real table together. iOS support (an iPhone joining the same match) remains part of the long-term cross-platform pitch and can be added once a build path is available, but is not required for the core Android-to-Android build.
- **Demo-friendly:** small, tabletop scale means it can be filmed and playtested anywhere (a kitchen table), with no need for large physical spaces or special lighting rigs — unlike a full-court-scale AR idea, which was considered and rejected for exactly this reason.

## 4. Core Mechanics — What Makes This Not Clash Royale

The starting point resembled Clash Royale's skeleton (two lanes, resource-gated unit deployment, real-time push toward an opposing base). The following mechanics are built specifically around what's only possible because this is AR on a real physical surface, to make the identity genuinely different:

### Mechanic 1 — Scavenged Terrain (highest priority, must-have)
Before a match, each player has ~60–90 seconds to physically arrange real objects on their side of the table (a mug becomes a watchtower, a book becomes a wall, a phone stand becomes a bridge). The app scans the final arrangement and procedurally derives chokepoints, walls, and cover from the real object geometry. No two matches are ever on the same layout, because no two tables have the same objects on them.

#### Terrain Generation — How It Actually Works (technical detail)

The app never tries to recognize *what* a real object is (that would require object-recognition ML, which is explicitly out of scope — see "No AI, by design" below). Instead it measures each object's **shape**, buckets it into one of a handful of deterministic gameplay archetypes using geometric rules, and skins that archetype with a pre-made cartoon asset scaled to cover it. Troops don't path around the unmodified real object — they path around the cartoon archetype placed on top of it.

**Two-tier detection (mirrors the hardware-tiering approach used elsewhere in this project):**
- **Tier A — Depth-capable devices** (iPhone 12 Pro+, higher-end Android phones with a depth sensor): fully automatic. During Fortify, the app scans the scene mesh, clusters connected "bumps" above the table plane (basic computational geometry, not a learned model), and extracts each object's bounding box — height, footprint area, footprint aspect ratio (round vs. elongated), and position.
- **Tier B — No depth sensor** (most mid-range Android, including the builder's own Honor): guided manual tagging. During Fortify, the player drags a box or taps two corners around each real object on the live camera view. Plane hit-testing (works on virtually any AR-capable phone, no depth needed) gives real-world footprint coordinates. Height isn't measurable this way, so the player picks a size category (short / medium / tall) with one tap. Slightly more manual, but keeps the game playable on the phones actually common in Mauritius.

Both tiers produce the same output — a set of objects, each with a footprint and a height — and everything downstream is identical regardless of which tier produced it.

**Geometric classification (rule-based thresholds, no ML):**

| Measured shape | Archetype | Gameplay effect |
|---|---|---|
| Short, wide, low | Rubble / Cover | Partial cover — units can crouch behind it, partial line-of-sight block |
| Tall, narrow footprint | Spire / Chokepoint | Full line-of-sight block, forces units to path around |
| Long, thin, elongated footprint | Wall / Barricade | Acts as a lane divider depending on orientation |
| Medium, roughly round, no strong feature | Plain Obstacle | Units just path around it — no strategic bonus, pure terrain |
| Tallest object on the board | Watchtower (bonus tier) | Same as Spire, plus a small vision/defensive bonus for the base nearest it |

A mug becomes a Spire. A paperback lying flat becomes a Wall. A phone stand becomes a Watchtower if it's the tallest thing on the table that match. Same shape always maps to the same bucket — deterministic, tuned via playtesting thresholds, no training or inference involved.

**"Cartoonify" step:** rather than masking/erasing the real object from the camera feed (an extra rendering system that isn't needed), the app simply places an opaque, pre-made cartoon asset for the matched archetype at the object's real position, scaled to be at least as large as the measured bounding box (generous margin so the real object never peeks out from odd angles). Because AR content renders on top of camera passthrough, a same-or-larger opaque virtual object at the same spot reads visually as "the mug is now a tower" with no occlusion trickery required.

**No AI, by design:** this system was deliberately kept fully rule-based rather than adding real object recognition (e.g. on-device Vision/ML Kit labeling to know "this is specifically a mug"). Reasons: (1) it's more demo-reliable — a misclassified object live on camera is a worse failure mode than a purely geometric system that never behaves unpredictably; (2) it doesn't actually serve the core joke ("my junk became a battlefield" already lands with a generic cartoon skin — a correct label adds little); (3) it keeps the project consistent with the "zero AI" constraint that's part of this project's identity, not just a technical afterthought. If more charm is wanted later, the plan is to let the *player* name their own objects during Fortify (a one-tap text field) rather than have the app guess — zero engineering risk, zero cost, arguably funnier than a correct label.

### Mechanic 2 — Camera Height / Posture Trade-off (must-have, cheap to build)
Each player's phone occupies a real position and height relative to the shared virtual board:
- **Leaning in close** → precise unit placement, but a narrow field of view (can't see the whole board, can't react to the far side).
- **Pulling back / standing** → wide commander's view of the whole battlefield, but coarser placement, and visibly telegraphs that you're not focused on one spot.
This is a real tactical trade-off that cannot exist in a flat-screen game where both players see an identical top-down view.

### Mechanic 3 — Explore-to-Earn Resources (stretch goal)
Instead of a passive mana bar that fills over time (Clash Royale's model), resources are generated by physically scanning more of the environment — extending the usable battlefield and revealing new terrain features — rewarding physical engagement over passive waiting. **Fallback if time is tight:** a simple timer-based resource tick, kept as a safe default so the core loop is never blocked on this system.

### Match structure
1. **Fortify** (60–90s) — physically arrange objects; app scans terrain.
2. **Muster** — starting garrison auto-populates based on the chokepoints created.
3. **Siege** — real-time phase; resource-based deployment shaped by the scavenged terrain and the camera-height trade-off.
4. **Aftermath** — short cinematic zoom on the decisive push when a base falls (good demo-video beat).

### Explicitly deferred / not committed
- **Gesture-based unit summoning** (drawing a shape in the air to summon a unit, instead of tapping) — flagged as a strong "wow" candidate for the demo video, but carries real engineering risk if it needs to work reliably without leaning on ML. Treat as a **week 1 spike**: prototype it early, alongside the Cloud Anchor test, and only commit to it if it feels solid fast. Do not block the core game loop on this.

## 5. Tech Stack

- **Engine:** Unity, using **AR Foundation** (Unity/Google's cross-platform layer over ARKit and ARCore) — one C# codebase targets both iOS and Android, avoiding a native Swift + native Kotlin split.
- **Shared spatial anchors:** **ARCore Extensions for AR Foundation** → Cloud Anchors, for cross-platform (Android + iOS) shared-anchor syncing between two devices on the same table.
- **Multiplayer state sync:** lightweight realtime layer (e.g. Firebase Realtime Database, or a small Unity Netcode relay) updating a few times per second — the game does *not* need continuous high-frequency ranging like the earlier tag-game concept did, which significantly de-risks this compared to that direction.
- **Monetization:** **RevenueCat Unity SDK**, wired to at least one in-app purchase (subscription and/or one-time cosmetic packs — see Section 6).
- **Platform targets:** iOS and Android (Android build also covers Samsung Galaxy Store distribution if pursued later, though **not required** for the Next Gen category).

## 6. Monetization Design (RevenueCat)

- **Free tier:** base game, one map/terrain theme, core tower/unit set, single-player + local competitive mode.
- **Scrap Siege Pro (subscription, with a free trial so judges can test premium features per submission requirements):**
  - Additional unit/tower types
  - Cosmetic terrain/map themes (reskins of the same core battlefield — e.g. medieval, sci-fi, pirate)
  - Endless/extra game modes
  - Extra visual effects packs

This is designed as genuine value-tier gating (more content, not functionality lockout), matching what RevenueCat's judging criteria tend to reward.

## 7. Timeline (6 weeks, risk-ordered)

Front-loaded so the highest-risk, most novel technical piece (cross-device Cloud Anchors) is validated in week 1, not discovered as broken in week 4.

- **Week 1:** Unity + AR Foundation project setup, basic plane detection working. **Cloud Anchor cross-device spike** — get two Android devices (different brands if possible, e.g. Honor + a second Android phone) sharing a single anchor point successfully. iOS cross-platform testing is deferred (see Section 8 note) — Android-to-Android is the priority to validate first. Optional: quick spike on gesture-based summoning to decide go/no-go.
- **Week 2:** Single-player core loop — terrain scanning (Mechanic 1), unit placement, wave/resource economy, basic pathing.
- **Week 3:** Two-device competitive sync — broadcasting game state between the two anchored sessions; camera-height mechanic (Mechanic 2) implemented.
- **Week 4:** RevenueCat integration — offerings, entitlements, paywall UI, unlockable content wired in.
- **Week 5:** Polish — VFX, sound design, terrain-scan robustness/occlusion handling, difficulty balancing, UI/UX pass.
- **Week 6 (buffer):** Real playtests with two physically different phone brands, demo video shoot + edit, repo cleanup for public visibility, icon + screenshots + submission assets.

## 8. Known Risks & Mitigations

- **Cloud Anchor reliability on low-texture surfaces** (e.g. a plain white table can be hard to lock onto). Mitigation: design a simple printable "battle mat" (a PDF/SVG placemat with a grid/pattern) that also serves as a nice thematic prop for the demo video.
- **Cross-brand Android AR support varies.** ARCore requires Google Play Services; most modern Honor and Samsung devices sold outside China have this, but the specific test devices should be checked against Google's ARCore supported-devices list before relying on them for the demo.
- **Gesture-based summoning (stretch) could eat time without a clean non-ML solution.** Keep it strictly time-boxed to a week 1 spike; fall back to tap-to-place if it's not solid quickly.
- **Explore-to-earn resource system (stretch) adds complexity to the core loop.** Keep the simple timer-based resource tick as the default, and only layer in the fancier version if weeks 1–4 go smoothly.
- **Two-device testing logistics.** This project cannot be fully tested solo on one device — plan early to have consistent access to a second phone (ideally a different brand) throughout the build, not just at the end.
- **iOS deferred, Android-first.** No Mac is available for local Xcode builds. Development and all core testing target **Android-to-Android Cloud Anchor sync** first (e.g. Honor + a second Android phone), which is fully achievable without a Mac. An iOS build remains possible later via a cloud macOS CI service (e.g. Codemagic) for the cross-platform demo, but it is not a blocker for weeks 1–5 and should not be assumed available during core development.

## 9. Next Gen Submission Checklist (for later, keep in view throughout)

- [ ] App built and demoed (no store listing required for Next Gen)
- [ ] RevenueCat SDK integrated, powering at least one in-app purchase
- [ ] Free trial or promo code available so judges can unlock premium features
- [ ] Public, open-source code repository
- [ ] Demo video (≤2 minutes of essential footage), publicly on YouTube or Vimeo, no unlicensed third-party trademarks/music
- [ ] 1024×1024 app icon
- [ ] At least one screenshot at 1179×2556px, no device frame
- [ ] Text description of features and functionality for the Devpost submission
