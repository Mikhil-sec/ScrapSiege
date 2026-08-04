# Project Story (Devpost submission draft)

This is the living draft for the Devpost "Project Story" field. Update it as real
milestones land — keep it concise; this is judged material, not a dev log. Full
technical/design detail stays in `plan.md`; this file is the story judges actually read.

## Inspiration

We wanted something genuinely original for RevenueCat Shipaton's Next Gen track — not
another AI-wrapper app. Two earlier directions were rejected before this one: an
AI-generated-content app (killed on principle — we wanted zero AI features, not another
"AI-powered" pitch), and a real-world radar tag game using Ultra-Wideband ranging (killed
because UWB hardware is inconsistent across iPhone, Samsung, and Honor — the exact three
brands common where we live). That second failure led to the real spark: AR plane-tracking
works across nearly every modern phone, no special hardware needed. Instead of a
fast-moving outdoor game fighting hardware fragmentation, what if the "arena" was just
whatever's already sitting on a table — and the battle was slow and tactical instead of
twitchy? Scrap Siege is what came out of asking that question.

## What it does

Two players arrange real objects from their table — mugs, books, phone stands, whatever's
around — as terrain. The app scans the arrangement and classifies each object by its
**shape** (never what it actually is) into a gameplay archetype: tall and narrow becomes a
chokepoint, short and wide becomes cover, long and thin becomes a wall. No two matches are
ever on the same layout, because no two tables have the same junk on them. Once terrain is
set, a short Muster phase auto-garrisons defenders at the chokepoints players built, then
players fight a real-time skirmish — deploying units with a resource economy, choosing
between a fast open route or a slower route that hugs cover terrain to avoid garrison fire,
until one base falls.

## How we built it

Unity 6 with AR Foundation as the cross-platform layer over ARCore/ARKit, targeting Android
first since that's the phone actually available for daily testing (an Honor device with no
depth sensor — this shaped the whole terrain-detection design toward manual, tap-to-tag
input as the primary path, not a fallback for lesser hardware). Terrain classification is
pure rule-based computational geometry — bounding box, height category, footprint aspect
ratio — with zero machine learning anywhere in the pipeline, by design. Pathing uses Unity's
NavMesh with custom area costs so a "safer" route through cover terrain is a genuine
distance/risk trade-off, not just a different-looking line. Monetization runs on the
RevenueCat Unity SDK, configured through the RevenueCat MCP server directly against our own
dashboard — one entitlement gates a real cosmetic feature (a second terrain color palette),
not a placeholder toggle. We leaned heavily on Claude Code as a coding partner, including
letting it operate the live Unity Editor directly through a Unity MCP connection once one was
available, but held every gameplay claim to the same bar regardless of who typed the code:
nothing is "done" until it's been tested on the real device and, where behavior looked wrong,
confirmed against `adb logcat` rather than guessed from reading the code.

## Challenges we ran into

- **Hardware fragmentation killed our first concept outright** (UWB), which is what pushed
  us toward AR in the first place.
- **Our only daily test device has no depth sensor**, so the "ideal" automatic terrain
  scanning was never an option — the manual tap-to-tag system had to be the real primary
  path from day one.
- **A OneDrive-synced project folder silently broke Unity's build pipeline** through file
  locking; the fix was relocating the whole project to local-only storage.
- **Several bugs were invisible from code alone and only showed up on-device**: URP quietly
  drops camera passthrough without an explicit Renderer Feature; NavMesh settings tuned for
  humanoid-scale games were wildly wrong at tabletop scale (a default 5cm agent radius was
  blocking gaps that looked wide open on screen); a Unity Inspector event-wiring mistake made
  a working resource counter always display zero.
- **The sneakiest one:** a full win condition was implemented and the base correctly reached
  zero health and fired its destroy event — but a missing Inspector reference crashed the
  very next line before the win screen could ever show. It took pulling `adb logcat`
  mid-test to prove the win logic was actually fine and find the real, one-line cause.
- **RevenueCat integration surfaced a genuine Unity lifecycle trap:** the SDK's internal
  state is only allocated in its own `Start()`, so configuring it from another script's
  `Awake()` — which runs earlier for the whole scene, before any `Start()` — threw a null
  reference that only appeared on-device. A second, independent failure (auto-configuring
  itself with empty keys unless a specific flag is set) was stacked on top of the first,
  so the fix needed both a forced execution order and a one-line Inspector flag, not just one
  patch. Diagnosed the same way as everything else here: real device, real logcat, no
  guessing.
- **A real purchase still can't complete on a physical device without a Google Play Console
  product** — RevenueCat's free Test Store is enough to validate every line of purchase-flow
  code in the Unity Editor, but real Android devices always go through actual Google Play
  Billing, which won't recognize a product that only exists in a test sandbox.

## Accomplishments that we're proud of

- A fully playable single-player core loop, confirmed working end-to-end on real
  hardware — not just in the Unity Editor: scan real clutter into terrain, watch the
  classification hold up, deploy troops, and watch a base actually fall.
- Terrain classification is 100% deterministic geometry, with real design discipline behind
  keeping it that way instead of reaching for an easy ML shortcut.
- A route-choice mechanic (fast-and-exposed vs. slow-and-covered) that gives real tactical
  weight to how players physically build their terrain, not just how they spend resources.
- A debugging habit that consistently found true root causes on real hardware instead of
  guessing from code review alone.
- Real monetization plumbing, not a mockup — a working entitlement, offering, and a genuine
  Pro-gated cosmetic feature, verified end-to-end in Editor Play Mode ahead of the real
  store listing that Next Gen doesn't even require.

## What we learned

- Bugs on real hardware often look nothing like their actual cause — several "this feature
  is just broken" moments turned out to be a single miscalibrated Editor setting, invisible
  from reading the script.
- Designing for the worst available device first (no depth sensor) instead of the best one
  produced a more robust system overall, not a lesser one.
- A defensive null-check that quietly does nothing instead of crashing can hide a real bug
  for an entire test session — failing loudly is almost always better than failing silently.

## What's next for Scrap Siege

- A Google Play Console product to let the already-built purchase flow actually complete on
  a real device, not just in the Editor.
- Two-device Cloud Anchor sync and the camera-height tactical trade-off (blocked on getting
  a second Android test device).
- An art and animation polish pass over the current placeholder terrain and paywall visuals.
- A demo video, public repo cleanup, and full submission assets.
- A possible redesign of the garrison from an automatic freebie into a capturable, contested
  point — once a real opponent exists to contest it against.
