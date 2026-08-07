# Project Story (Devpost submission draft)

This is the living draft for the Devpost "Project Story" field. Update it as real
milestones land — keep it concise; this is judged material, not a dev log. Full
technical/design detail stays in `plan.md`; this file is the story judges actually read.

## Inspiration

We wanted something genuinely original for RevenueCat Shipaton's Next Gen track — not
another AI-wrapper app. Two directions were rejected before we wrote any real code: an
AI-generated-content app (killed on principle — we wanted zero AI features, not another
"AI-powered" pitch), and a real-world radar tag game using Ultra-Wideband ranging (killed
because UWB hardware is inconsistent across iPhone, Samsung, and Honor — the exact three
brands common where we live). That second failure led to the spark: AR plane-tracking works
on nearly every modern phone with no special hardware. What if the arena was a real table,
and the battle was slow and tactical instead of twitchy?

## What it does

Scrap Siege projects a hand-designed battlefield onto any flat real surface — a desk, a
dining table, the floor — at real scale, and you fight a rule-based AI commander across it.

The part a flat screen can't copy is that **your phone's physical position is a tactical
resource**. Lean in close and your unit placement is precise but you can only see one
corner of the board. Pull back and you can read the whole fight, but your troops land
loosely. Enemy units are only revealed when there's genuine line of sight from where you're
actually standing, so a virtual wall really does hide what's behind it — and peeking round
it means physically leaning. Deploy is a further trade-off: a fast, exposed direct route, or
a slower one that hugs cover to stay out of garrison fire.

## How we built it

Unity 6 with AR Foundation over ARCore, Android-first because that's the hardware actually
available for daily testing — two phones, neither with a depth sensor, which ruled out every
depth-based shortcut from day one. Pathing uses Unity's NavMesh with custom area costs, so
the "safer" route through cover is a genuine distance/risk trade-off rather than a
differently-coloured line. Monetization runs on the RevenueCat Unity SDK, configured through
the RevenueCat MCP server directly against our own dashboard: one entitlement gating real
content, not a placeholder toggle.

There is **zero machine learning anywhere in the app**, deliberately. The AI commander is
ordinary game AI — explicit thresholds and utility scoring, hand-written and debuggable. We
think that's a feature: it never behaves unpredictably on camera, which matters when the
whole thing has to survive a two-minute demo video.

We leaned heavily on Claude Code as a coding partner, including letting it operate the live
Unity Editor directly through a Unity MCP connection, but held every claim to the same bar
regardless of who typed the code: nothing counted as done until it ran on the real device,
and where behaviour looked wrong we confirmed it against `adb logcat` instead of guessing.

## Challenges we ran into

- **Hardware fragmentation killed our first concept outright** (UWB), which is what pushed
  us toward AR in the first place.
- **A OneDrive-synced project folder silently broke Unity's build pipeline** through file
  locking; the fix was relocating the whole project to local-only storage.
- **Several bugs were invisible from code alone and only appeared on-device.** URP quietly
  drops camera passthrough without an explicit Renderer Feature. NavMesh settings tuned for
  humanoid-scale games were wildly wrong at tabletop scale — a default 5cm agent radius was
  blocking gaps that looked wide open on screen. A Unity Inspector event-wiring trap made a
  perfectly working resource counter permanently display zero.
- **The sneakiest bug of the project:** a win condition where the base correctly reached zero
  health and fired its destroy event — but a missing Inspector reference threw on the very
  next line, before the win screen could appear. Only pulling `adb logcat` mid-test proved
  the win logic was fine and found the real one-line cause. Much later, the same *class* of
  bug reappeared: a null-reference in a UI script's `OnEnable` aborted the rest of the
  method, silently unregistering half the game's event listeners on every single launch. The
  visible symptom was a button that looked broken; the button was fine.
- **RevenueCat surfaced a genuine Unity lifecycle trap:** the SDK's internal state is only
  allocated in its own `Start()`, so configuring it from another script's `Awake()` threw a
  null reference that only appeared on-device — with a second, independent failure stacked on
  top of it.
- **The biggest one: we built a two-player game and then cut it.** Weeks of work went into
  a full LAN implementation — Netcode for GameObjects over direct local network, UDP host
  discovery, a cloud-free shared coordinate frame where both players tap the same two real
  objects to agree on where the board is, server-authoritative replicated terrain with
  half-of-table ownership, and a host-authoritative siege with per-player bases and
  resources. It all worked as code. What defeated it was the layer underneath: **AR plane
  detection could not reliably produce a usable surface** across a floor, a cushioned table
  and a dining table. Every attempt to make co-location robust ran into the same foundation
  being unreliable, and a two-player match can't start until both devices agree on where the
  board is.

## Accomplishments that we're proud of

- **Knowing when to cut.** The two-player build wasn't abandoned because it was hard or
  unfinished — it was abandoned because measurement showed the foundation underneath it was
  unreliable, and shipping something honest mattered more than shipping the original plan. It
  is preserved on a branch rather than deleted, and the pivot removed both of the project's
  worst dependencies at once.
- A debugging habit that consistently found true root causes on real hardware — via logcat,
  on the device — rather than guessing from code review.
- A route-choice mechanic (fast-and-exposed vs. slow-and-covered) with real tactical weight,
  driven entirely by NavMesh area costs rather than bespoke pathfinding.
- Real monetization plumbing, not a mockup: a working entitlement, offering, and a genuine
  Pro-gated feature, verified end-to-end in Editor Play Mode.
- Holding the zero-ML line all the way through, including for the opponent AI, when reaching
  for a model would have been the easy story to tell in 2026.

## What we learned

- **Bugs on real hardware often look nothing like their cause.** Twice, a broken-looking
  button turned out to be an exception in a completely different script that had silently
  aborted a chunk of setup.
- **Test the riskiest assumption before building on top of it.** We validated cross-device
  networking thoroughly and the AR surface detection underneath it barely at all. The layer
  we didn't stress-test is the one that ended the direction.
- **Designing for the worst available device first** produced a more robust system than
  designing for the best one would have.
- A defensive null-check that quietly does nothing can hide a real bug for an entire test
  session — failing loudly is almost always better than failing silently.

## What's next for Scrap Siege

- Board placement and authored level definitions, replacing the old scanning flow.
- The AI commander, plus the player-side base and lose condition.
- The two mechanics the whole design now rests on: camera-height vantage, and true
  line-of-sight from the player's real viewpoint.
- Pro level packs behind the already-built entitlement, and a Google Play Console product so
  a real purchase can complete on a device rather than only in the Editor.
- An art and animation pass over the placeholder terrain, then the demo video and submission
  assets.
