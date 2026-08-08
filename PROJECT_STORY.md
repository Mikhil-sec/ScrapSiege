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
available for daily testing — none of it with a depth sensor, which ruled out every
depth-based shortcut from day one. Pathing uses Unity's NavMesh with custom area costs, so
the "safer" route through cover is a genuine distance/risk trade-off rather than a
differently-coloured line. Monetization runs on the RevenueCat Unity SDK, configured through
the RevenueCat MCP server directly against our own dashboard: one entitlement gating real
content, not a placeholder toggle.

Levels are authored in **normalised board space** — every position is a 0–1 coordinate rather
than a measurement — so a single hand-designed map projects correctly onto a coffee table or a
dining table at whatever size the player pinches it to. Adding a level is a data file and
nothing else. A small validator runs over them and checks the things that are invisible until
you play: pieces off the board, terrain walling in an objective, a map asking for more
defenders than it has positions for. It caught two real design bugs before either reached the
device.

The art is deliberately **static low-poly models with procedural animation in code, not rigged
characters**. A unit is about five centimetres tall on a real table seen through a phone — rig
deformation is invisible at that size, while gross motion isn't. So the troopers are built from
separate parts with real joint pivots, and the marching, bobbing, leaning and attack lunge are
driven from the navigation agent's actual velocity, keyed to distance travelled so a unit
stopped at a chokepoint visibly stops walking instead of moon-walking on the spot.

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
  drops camera passthrough without an explicit Renderer Feature. A Unity Inspector
  event-wiring trap made a perfectly working resource counter permanently display zero.
- **Unity's pathfinding has a hard floor we didn't know existed, and it cost us two sessions
  before we found it.** Our units kept walking a short distance and stopping, so we tried
  shrinking their navigation radius to fit our tiny board — and the setting kept silently
  reverting. We assumed the Editor was rewriting our file on shutdown and worked around that
  for a while. It wasn't. We finally proved it by writing a dozen different values directly
  and reading them straight back: anything under five centimetres came back as exactly five
  centimetres, always, on a brand-new setting too. Unity simply refuses to go smaller — a
  reasonable limit for a humanoid game, catastrophic for a board the size of a placemat. The
  fix wasn't a setting at all: we scaled the whole AR world up five times, so five real
  centimetres of pathing room became one real centimetre. Confirmed by baking real pathfinding
  data and asking it for an actual route, not by eye.
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
- **We shipped a mechanic that worked perfectly and meant nothing.** Camera-height vantage
  went to the device and did exactly what it was designed to do — and playing it revealed the
  design was wrong. Leaning in bought precision; standing back bought *information*. But
  information is passive and free, so the optimal play was to glance up once and then stay
  leaned in forever. Posture had become a glance, not a stance. The fix wasn't tuning, it was
  giving the high position an **action**: a Rally order that redirects every deployed unit
  through a new lane, available only when you're physically pulled back far enough to see the
  whole board. You can't command what you can't see.
- **Then the same test said precision felt pointless — and it was right.** Chasing it found a
  single number: the safe "cover lane" laid beside each piece of terrain extended a quarter of
  a metre in every direction. On a sixty-centimetre board that made cover *the entire table*.
  Cover was free and unmissable, so placing a unit carefully bought nothing. One value, and a
  whole mechanic came back to life.
- **A menu where every button was dead, with no error anywhere.** The buttons rendered, were
  marked interactable, had the right handlers attached, and did nothing on tap. Unity's default
  new-scene template simply doesn't include an EventSystem, and without one no UI input is
  processed at all — silently. Immediately after that, board placement had the same *flavour* of
  bug for a different reason: our own touch code counted currently-held fingers before deciding
  what to do, and a quick tap can report "pressed this frame" while already reading as released.
  Fast taps were being thrown away. Both were the kind of failure that produces no log line and
  no exception — just a thing that doesn't happen.
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

### The bug that taught us to stop trusting our own eyes

Late in development the game *looked* finished but played wrong: troopers vanished a second
after deploying, and the victory screen fired even though nobody ever saw a unit reach the
enemy base. The obvious reading — the one we'd have shipped a fix for — was that the "hit the
base" trigger radius was too generous.

It wasn't. Instrumenting the actual `NavMeshAgent` state every frame showed something no
amount of code-reading would have surfaced: `remainingDistance` silently returns **0** when an
agent has no valid path — not an error, not infinity — which is byte-for-byte identical to
"I have arrived." Our arrival check couldn't tell the difference. The units weren't reaching
the base early; they were falling off the navigation mesh on spawn and *reporting* arrival.

Chasing that honestly then exposed three more real defects hiding behind it, each of which had
been invisible because the false arrival masked them: the cover corridor a unit is supposed to
route *through* was being carved as a solid wall; the "Direct" deploy route was barred from
cover entirely, contradicting its own design; and a whole family of values — unit speed,
arrival radius, sentry range, deploy scatter — were tuned in absolute metres on a board whose
size the player chooses at runtime, so a unit crossed a 60 cm table in two seconds.

The lesson we actually took: **a plausible explanation is not a diagnosis.** Every fix in that
sequence came from measuring the running system, and the first three theories we found
convincing were all wrong.

## Accomplishments that we're proud of

- **Knowing when to cut.** The two-player build wasn't abandoned because it was hard or
  unfinished — it was abandoned because measurement showed the foundation underneath it was
  unreliable, and shipping something honest mattered more than shipping the original plan. It
  is preserved on a branch rather than deleted, and the pivot removed both of the project's
  worst dependencies at once.
- **Redesigning a mechanic that already worked**, because playing it showed it had a dominant
  strategy. It would have been easy to call vantage "done" — it shipped, it did what the spec
  said. Rally exists because we were willing to say the spec was wrong.
- **Three levels that each teach one thing.** They aren't decoration: one is a single cover
  corridor that punishes a loose drop, one hides two sentries behind a spire so you have to
  physically move to find them, and one splits the field into lanes that only rejoin deep, so
  committing wrongly costs you a Rally to fix.
- A debugging habit that consistently found true root causes on real hardware — via logcat,
  on the device — rather than guessing from code review.
- A route-choice mechanic (fast-and-exposed vs. slow-and-covered) with real tactical weight,
  driven entirely by NavMesh area costs rather than bespoke pathfinding.
- Real monetization plumbing, not a mockup: a working entitlement, offering, and a genuine
  Pro-gated feature, verified end-to-end in Editor Play Mode.
- Holding the zero-ML line all the way through, including for the opponent AI, when reaching
  for a model would have been the easy story to tell in 2026.

## What we learned

- **Bugs on real hardware often look nothing like their cause.** Repeatedly, a broken-looking
  button turned out to be something else entirely — an exception in an unrelated script, a
  missing scene object, a touch being discarded a frame early.
- **"It works" and "it's fun" are different tests, and only the device runs the second one.**
  Vantage passed every unit test and still needed redesigning the first time it was played.
  Two of our best design decisions came from playing something correct and finding it hollow.
- **A mechanic the player can't observe can't be learned.** Deploy scatter was working from the
  start and felt like nothing, because you only ever saw where a unit landed, never how precise
  you were being. Drawing the current precision as a ring on the table *before* the tap changed
  it from invisible maths into something you can feel yourself getting better at.
- **Test the riskiest assumption before building on top of it.** We validated cross-device
  networking thoroughly and the AR surface detection underneath it barely at all. The layer
  we didn't stress-test is the one that ended the direction.
- **Designing for the worst available device first** produced a more robust system than
  designing for the best one would have.
- **Verify the thing you changed, not the thing you intended to change.** Setting a value and
  assuming it took hold cost us real time more than once — an editor silently discarding a
  file edit, a reference assignment quietly doing nothing. Reading the value back afterwards is
  cheap and catches all of it.
- A defensive null-check that quietly does nothing can hide a real bug for an entire test
  session — failing loudly is almost always better than failing silently.

## What's next for Scrap Siege

- **The AI commander.** It's the missing half of the design: Rally, reacting to a threat, and
  difficulty tuning are all inert until there's an opponent making moves worth reacting to.
- **Level tuning from our first full hands-on playtest.** Playing the flagship level end to end
  for the first time showed cover was still too generous and the "one safe corridor" premise
  wasn't actually being enforced — exactly the kind of thing that only shows up once a level
  is genuinely playable rather than reasoned about on paper.
- The player-side base and a real lose condition.
- Board elevation — a raised plateau that would make line of sight genuinely three-dimensional,
  so you crouch to look along a ridge and rise to see over it. Held deliberately until the flat
  maps are proven, because navigation at tabletop scale has already bitten us twice.
- More authored levels, star ratings, sound.
- Pro level packs behind the already-built entitlement, and a Google Play Console product so
  a real purchase can complete on a device rather than only in the Editor.
- The demo video and submission assets.
