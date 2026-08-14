# Security — Scrap Siege

This repo is **public** and the app takes **real money through Google Play Billing**. That combination
means two separate questions have to be answered before every release, and they have different answers:

1. **What is in the repo?** — anyone can read it, including full git history on every branch.
2. **What is in the APK/AAB?** — anyone can unzip it. Assume everything shipped is readable.

This file is the standing checklist plus the register of what has actually been checked, when, and what
was found. **Re-run the checklist before every Play Console upload and before any change that touches
keys, signing, or the entitlement gate.**

---

## The one rule that matters most

**A RevenueCat _public SDK key_ is meant to ship in the client. A RevenueCat _secret key_ (`sk_…`) must
never touch this repo or the app.**

Per RevenueCat's own docs, there are exactly two key types:

| Key | Prefix | Where it belongs | Why |
|---|---|---|---|
| Public SDK key | `goog_`, `appl_`, `test_` | In the app. Fine in the scene, fine in the public repo. | Configures the SDK and can only make non-potent subscriber changes. |
| Secret API key | `sk_` | **Server-side only. Never here.** | Project-wide. Can delete subscribers and grant entitlements outright. |

The secret key is what the RevenueCat MCP server uses. It lives in the Claude/MCP configuration outside
this repo and must stay there. If one ever appears in a commit, **rotating it in the RevenueCat
dashboard is mandatory** — deleting the commit is not enough, because the repo is public and may have
been cloned or indexed.

---

## Yes, the APK is readable — this was verified, not assumed

Unzipping `build/ScrapSiege.apk` and searching the payload finds the RevenueCat public key in
**plaintext** in `assets/bin/Data/level1`. Anything serialized into a Unity scene or prefab is
recoverable in about thirty seconds with `unzip` and `grep`.

The build does use **IL2CPP** (`ProjectSettings.asset` → `scriptingBackend: Android: 1`), so gameplay
C# is compiled to native ARM64 and there are no `.dll` files to drop into a decompiler. That raises the
cost of reading *logic*, but it does nothing for *data*: string literals still sit in
`global-metadata.dat`, and serialized Inspector values sit in the scene files.

**So the working assumption is: any value typed into the Inspector is public.** That is acceptable for
the RevenueCat public key by design. It would not be acceptable for a secret key, a backend URL with an
embedded token, or a signing credential.

---

## Standing checklist

### A. Before every commit

- [ ] `git status --porcelain -uall` is clean of anything unexpected — no `.jks`, `.keystore`, `.env`,
      `google-services.json`, `.apk`, `.aab`.
- [ ] No `sk_` string anywhere in the working tree:
      `grep -rIl "sk_[A-Za-z0-9]\{15,\}" --exclude-dir=Library --exclude-dir=Temp --exclude-dir=build .`
- [ ] `ProjectSettings/ProjectSettings.asset` is **tracked** — if you have just configured signing,
      diff it before committing and confirm no keystore or alias **password** was written into it.

### B. Before every Play Console upload

- [ ] Build is a **release** build, not a development build. Verify on the artifact itself:
      `aapt2 dump xmltree <apk> --file AndroidManifest.xml | grep debuggable`
      → must return **nothing**. `debuggable=true` is both a Play rejection and a real vulnerability.
- [ ] Signed with the **upload keystore**, not the Android debug certificate:
      `apksigner verify --print-certs <apk>` → must **not** say `CN=Android Debug`.
- [ ] Scene carries the **Play Store** public key (`goog_…`), not the Test Store key (`test_…`).
      A `test_` key in a store build means no product will ever resolve on-device.
- [ ] Permissions are still only: `INTERNET`, `CAMERA`, `com.android.vending.BILLING`,
      `ACCESS_NETWORK_STATE`, `HIGH_SAMPLING_RATE_SENSORS`, and Unity's
      `DYNAMIC_RECEIVER_NOT_EXPORTED_PERMISSION`.
      `aapt2 dump badging <apk> | grep uses-permission`. Anything new needs a justification.
      **`HIGH_SAMPLING_RATE_SENSORS` (added 2026-08-10)** is injected by
      `Assets/Editor/AndroidManifestPostProcessor.cs`, not declared by any package. It is a
      *normal* (install-time, auto-granted) permission, not a dangerous one — it grants no access
      to the user's identity, location, or files, only the right to sample the IMU above 200 Hz,
      which ARCore's motion tracking requires on targetSdk 31+. Without it ARCore refuses to
      start and the camera never opens (see the 2026-08-10 finding below).
- [ ] **If the permission list above changed, section 5 of `docs/privacy/index.html` changed with
      it.** The allowlist and the public policy's permission table are the same list stated twice.
      A shipped app whose manifest declares more than its privacy policy discloses is a Play policy
      violation, and the discrepancy is trivially machine-checkable by Google.
- [ ] Grep the built artifact for anything that should not be in it before uploading.
- [ ] `AndroidBundleVersionCode` (`ProjectSettings.asset`) is **higher than every previous upload**,
      including failed/rejected ones — Play Console tracks the highest version code it has ever seen per
      app, not per-release. Verify with `aapt2 dump badging <apk-from-aab> | grep versionCode`. Bump via
      `PlayerSettings.Android.bundleVersionCode` in the Editor, not by hand-editing the asset while the
      Editor is open.
- [ ] `PlayerSettings.bundleVersion` (`versionName`, e.g. `0.2.0`) moved up **together with**
      `bundleVersionCode` — they're independent fields and Unity does not bump one when you bump the
      other. A higher `versionCode` with a stale `versionName` still installs fine, but reads as a
      downgrade to any human looking at the Play Console listing or device app info.

### C. Whenever the entitlement gate changes

- [ ] Nothing of real value is gated on `ProEntitlement.IsUnlocked` alone (see Finding 3).
- [ ] **Current gates (2026-08-10):** the saturated terrain palette, the main-menu PRO badge, and
      **level 05 "The Foundry"** (`LevelDefinition.requiresPro`). All three are client-side only, which
      remains within Finding 3's accepted risk: the entitlement unlocks a cosmetic palette and one
      offline single-player level, so the worst case is that someone who patches the APK plays a level
      they did not pay for. There is no server-authoritative state, no shared economy and no other
      user's data behind this gate. **Re-evaluate the moment anything behind it has real value** —
      multiplayer, cloud saves, or anything consumable.

### D. Keystore custody (once it exists)

- [ ] The `.jks` lives **outside** the repo directory entirely.
- [ ] It is backed up somewhere you will still have in a year — **losing the upload key means losing the
      ability to update the app**, and that is unrecoverable without a Google key-reset request.
- [ ] Passwords are in a password manager, never in a file in this project, never in a chat message.
- [ ] **Known Unity behavior (this version, confirmed 2026-08-09):** `keystorePass`/`keyaliasPass` do
      NOT persist across an Editor restart, even with `useCustomKeystore` staying `true`. After
      reopening the Editor, re-apply both passwords **before** attempting a release build — otherwise it
      fails cleanly with "No keystore passwords were found" (Finding 1's guard catches this; it will not
      silently fall back to debug signing).
- [ ] **Re-apply them from the environment, not from a file** (added 2026-08-10):
      `Scrap Siege > Apply Release Keystore Passwords (from environment)` reads `SCRAPSIEGE_KEYSTORE_PASS`
      (and optionally `SCRAPSIEGE_KEYALIAS_PASS`). Set these as Windows *user* environment variables and
      restart Unity Hub so the Editor inherits them. This exists so the recurring "re-enter the password"
      chore stops pointing at a plaintext credentials file — the repo is public and a signing password
      must never be one careless `git add` away from being published.
- [ ] **Development builds no longer touch the keystore at all** (added 2026-08-10). `BuildScript`
      turns `useCustomKeystore` off for the duration of a dev build and restores it afterwards, so a USB
      APK is debug-signed and never prompts. This closes a real hazard as well as an annoyance: being
      blocked on a password prompt during ordinary dev work is exactly the pressure that leads someone
      to paste the real upload password somewhere convenient.

---

## Findings register

### 2026-08-14 — abuse/rate-limit review of the purchase paths ("could a bot hammer the subscription call?")

Asked directly by the user before closed testing. Reviewed every path that reaches Google Play
Billing or the RevenueCat backend. **No forgeable-entitlement finding. Three unbounded-volume
findings, all fixed.**

#### First, what is NOT a risk — so nobody re-audits it

- **A bot cannot mint an entitlement by calling anything a million times.** `ProEntitlement` is only
  ever written from `MonetizationManager.ApplyCustomerInfo`, which reads
  `customerInfo.Entitlements.Active` — a value produced by **RevenueCat's servers** after they
  validate a **Google-signed** purchase token. There is no client-side "grant" call, no local receipt
  parsing, and no code path where a failed or absent purchase results in `SetUnlocked(true)`.
  Volume changes nothing about that: a million rejected calls grant a million nothings.
- **There is no server of ours to exhaust.** This project owns no backend. The only endpoints in play
  are Google Play Billing and RevenueCat, both of which do their own rate limiting and neither of
  which is "our system" to break.
- **The API key in the scene is a publishable client key, not a secret** (already recorded in the
  2026-08-08 audit). Its capability ceiling is: read offerings, read *the calling customer's own*
  info, and initiate a purchase that Google still has to authorise. It cannot grant entitlements,
  issue refunds, read another customer, or change dashboard configuration.
- **A modded APK can flip the local flag, and that is accepted.** Anyone repackaging the app can force
  `ProEntitlement.IsUnlocked` true and get the Pro level and the Turret. This is a single-player game
  with no server authority and nothing to steal from other players; the cost of that is one person
  playing a level for free. Defending it would need a backend the project does not have and does not
  need. **Do not add obfuscation theatre for this.**

#### Finding 1 (fixed) — Restore Purchases had no in-flight guard at all

`PaywallController.OnSubscribePressed` disabled its own button for the duration of a purchase.
`OnRestorePressed` did not. Every tap issued another `Purchases.RestorePurchases` network call, with
nothing bounding the rate — a held finger, an accessibility key-repeat, or a scripted tapper produced
an unbounded stream. The realistic damage is not fraud but **rate limiting landing on a real player
trying to restore a subscription they paid for**, plus racing callbacks all writing
`ProEntitlement` in an order nobody chose.

**Fix:** `MonetizationManager` now refuses overlapping store operations outright — one flag covering
Purchase, Restore and SyncPurchases, cleared in the callback. Enforced at the manager rather than only
in the UI, so a second screen wired to these methods later cannot reintroduce it. `PaywallController`
also disables the Restore button for the duration, matching Subscribe.

#### Finding 2 (fixed) — customer-info refresh fired once per app focus change

`OnApplicationFocus(true)` called `RefreshCustomerInfo()` unconditionally. That hook exists for a good
reason (the Play purchase flow runs in its own activity, so every purchase is bracketed by a
pause/resume) but an app can be focused and unfocused as fast as the OS can switch windows, and a
device stuck cycling — or a script doing it deliberately — turns that into an unbounded call stream
under this project's own key.

**Fix:** `MayRefresh()` throttles background refreshes to one per 20s and drops any request while one
is already in flight. **Correctness is unaffected:** the SDK caches customer info for minutes anyway,
and genuine entitlement changes arrive through the purchase/restore callbacks, which are deliberately
**not** throttled — only the speculative refresh is.

#### Finding 3 (fixed) — the paywall refetched offerings on every panel open

The paywall is a toggled panel, not a loaded screen, so `OnEnable` -> `RefreshOffering` ->
`GetOfferings` ran every time it opened. "Open, close, repeat" was a free unbounded call stream.

**Fix:** `FetchOfferings` serves a cached result for 10s. The callback contract is unchanged, so
callers cannot tell which path they got.

#### Not changed, and why

- **No client-side purchase-attempt counter or lockout.** Google Play's own flow already requires
  user authorisation per purchase and refuses a second purchase of a subscription the account owns
  (`ProductAlreadyPurchasedError`, which this app handles by syncing). A homegrown lockout would add a
  way to accidentally lock a paying customer out of buying.
- **No throttle on `SyncPurchases` beyond the in-flight guard.** It is the recovery path for a player
  who paid and has no entitlement; making it harder to reach would trade a real user's money for a
  hypothetical bot's bandwidth.
- **`LevelProgress` (new this pass) is PlayerPrefs and is not a security boundary.** It stores star
  ratings only. Nothing gated by the entitlement is stored there and it is never consulted about paid
  access. Stated explicitly in the file so it does not drift into being one.

> **Status: fixed in code, compile-verified, NOT yet exercised against the live store.** The next real
> device test should confirm a purchase and a restore still complete normally — the throttles are
> deliberately generous (20s / 10s) precisely so they cannot interfere with a genuine flow, but that
> is an assertion until someone buys something.

#### Open, and NOT a code issue: the privacy policy is written but not yet published

Google Play requires a privacy-policy URL for any app that has in-app purchases, and it is checked
before the production track. This is a hosted page plus a Play Console field, not a code change, but
it is a hard blocker on publishing and belongs on the closed-testing checklist.

**Written 2026-08-14:** `docs/privacy/index.html` (authoritative) plus `docs/PRIVACY_POLICY.md`
(summary + maintenance notes). Its contents were derived from the code, not from a template — the
declared permissions, the absence of any analytics/ads SDK, the `PlayerPrefs` keys, and the fact that
`MonetizationManager` never calls `LogIn` or sets subscriber attributes (so RevenueCat only ever sees
its own anonymous ID) were each checked before being asserted publicly.

**Still to do, and none of it is code:** enable GitHub Pages (`main` / `/docs`), replace the single
`CONTACT_EMAIL_PLACEHOLDER` token, enter the URL in Play Console, and complete the Data Safety form
consistently with the policy.

> **New standing checklist item — a privacy policy is a security control here, not just paperwork.**
> Section B's permission allowlist and section 5 of the policy are the same list stated twice, once
> privately and once publicly. **Any permission added to the app must be added to both**, in the same
> change. Likewise, if `MonetizationManager` ever identifies users by anything other than the SDK's
> anonymous ID, section 2 of the policy becomes a false public statement and must be corrected before
> that build ships. A privacy policy that understates what an app collects is a Play policy violation
> and, in some jurisdictions, a regulatory one.

---

### 2026-08-10 — new permission added: `HIGH_SAMPLING_RATE_SENSORS` (not a vulnerability)

Recorded here because section B's permission allowlist is a security control and this changes it,
not because the permission itself is a risk.

**Why:** on-device logcat showed ARCore creating and configuring its session, then failing with
`Failed to register sensor to queue 0`, moving to an internal error state (`ArPresto::Moving from
ArPrestoStatus 103 to 200`), and **never calling `ArSession_resume`** — confirmed by `ArSession_resume`
appearing **zero** times in the whole log buffer across two separate app runs. With no resumed
session the camera is never opened (no camera-open event in `CameraService`), so every frame logs
`camera was passed NULL` — **16,286 and 11,180 times** in the two runs — and plane detection reports
`planes=0` forever. On screen that is a black passthrough, which reads as a rendering bug.

Apps targeting API 31+ are capped at 200 Hz of sensor sampling without `HIGH_SAMPLING_RATE_SENSORS`,
and this app targets 36. The installed APK declared only `INTERNET`, `CAMERA`, `BILLING`,
`ACCESS_NETWORK_STATE` and Unity's receiver permission — the ARCore XR plugin injects only `CAMERA`
and `INTERNET` (`FindOrCreateTagWithAttribute` in its build code), so nothing ever added the sensor
permission.

**Security assessment: no new exposure.** It is a normal install-time permission, auto-granted, with
no runtime prompt and no access to identity, location, contacts, or storage. It widens nothing an
AR app does not already do with `CAMERA`.

> **Status: fix built and verified on the artifact (2026-08-10); NOT yet confirmed on-device.**
> `aapt2 dump badging` on both the release AAB's universal APK and the dev APK confirms
> `android.permission.HIGH_SAMPLING_RATE_SENSORS` is present, injected by
> `Assets/Editor/AndroidManifestPostProcessor.cs`. Still needs a real device run to confirm
> `ArSession_resume` now fires and `camera was passed NULL` stops — that is the actual proof the
> camera works again, not just that the permission made it into the manifest.

### 2026-08-10 — release AAB (versionCode=4/0.4.0) rebuilt and verified with the new permission

Rebuilt `build/ScrapSiege.aab` after the `HIGH_SAMPLING_RATE_SENSORS` manifest fix above, using the
`SCRAPSIEGE_KEYSTORE_PASS` environment-variable signing path (see `SECURITY.md` section D) — no
password touched a file or this chat. Verified with `bundletool build-apks --mode=universal` →
`aapt2 dump badging/xmltree` + `apksigner verify --print-certs` on the resulting universal APK:
not debuggable, `internalOnly` install location, `application-label='ScrapSiege'`,
`versionCode='4' versionName='0.4.0'`, signer `CN=ScrapSiege` (not `CN=Android Debug`), and all six
expected permissions present including the new sensor one. A parallel dev APK build was verified
debug-signed as expected (`CN=Android Debug`) with the same new permission present. **Neither
artifact has been uploaded or installed to a device yet as of this writing** — that is the next step
(release AAB → Play Console Internal Testing; dev APK → sideload for the camera fix).

**Tooling note for a future session:** `bundletool` (used above) is not vendored in this repo and
was not found anywhere on disk this session, despite being used in the 2026-08-09 verification —
it must have lived in a temp location that didn't survive. Re-downloaded to `tools/bundletool.jar`
(sibling to the project, **outside** `Scrap/` so it can't accidentally get added to the tracked
repo — it's a build tool, not project source). If it's gone again next time, re-fetch from
`https://github.com/google/bundletool/releases`; Unity's bundled JDK
(`Editor\Data\PlaybackEngines\AndroidPlayer\OpenJDK\bin\java.exe`) can run it directly.

**Signing-password plumbing note:** `bundletool --ks-pass`/`--key-pass` reject `env:` — they only
accept `pass:<literal>` (unsafe, puts the password in a visible command) or `file:<path>`. The
working pattern used here: read the password via
`[Environment]::GetEnvironmentVariable('SCRAPSIEGE_KEYSTORE_PASS','User')` in PowerShell (this reads
the registry directly, sidestepping any stale process's environment block — see the note below),
write it to a temp file with no shell ever echoing the value, pass `file:<temp path>` to bundletool,
then delete the temp file immediately after. Also hit and worth remembering: paths passed through
Git Bash to a native Windows `.exe` need `MSYS_NO_PATHCONV=1` or an already-Windows-style path
(`C:/...`) — a bare POSIX path (`/c/Users/...`) got mangled mid-flag and bundletool reported the
file as not found.

**Windows environment-variable propagation note (cost real time this session):** setting a *User*
environment variable via System Properties updates the registry immediately, but does **not**
propagate to already-running processes, and critically, restarting an app that was launched from a
still-running **`explorer.exe`** doesn't help either — the new process inherits `explorer.exe`'s own
still-stale environment block. What actually worked: `Stop-Process -Name explorer -Force` (it
auto-restarts and picks up the refreshed registry value), **then** fully quitting and relaunching
**Unity Hub itself** (not just the Editor window) so the whole process chain is fresh. A full
log-off/log-on also works and is the guaranteed fallback if restarting Explorer alone doesn't. Any
of my own long-running shell tool processes from earlier in a session will *also* stay stale after
this — don't trust `$env:` / `${VAR}` in an old shell; re-derive via the PowerShell registry read
above instead of assuming a "restart Unity" fixed it.

### 2026-08-09 (second pass) — first Internal Testing upload rejected on version code, fixed

First upload attempt of `build/ScrapSiege.aab` to Play Console Internal Testing failed with "Version
code 1 has already been used. Try another version code." — Play tracks the highest version code ever
*attempted*, not just successfully published ones, so the default `AndroidBundleVersionCode: 1` Unity
ships with was already burned. Fixed: bumped to `2` via `PlayerSettings.Android.bundleVersionCode`,
reapplied the signing passwords (lost on the Editor restart earlier this session, as expected — see
below), rebuilt, and reverified `versionCode='2'` in the artifact with `aapt2 dump badging`. Added a
standing checklist item under section B so this doesn't get re-hit blind next release.

**Follow-up same day:** the human-readable `versionName` (`PlayerSettings.bundleVersion`) had not moved
off `0.1.0` when `versionCode` was bumped to `2` — Play Console uses `versionCode` alone for update
ordering so this wasn't a functional blocker, but a `versionCode 2` release still labeled `0.1.0` reads
as a downgrade to anyone (including future-me) inspecting the Play Console listing or device settings.
Bumped `bundleVersion` to `0.2.0` to match, rebuilt, reverified both `versionCode='2'` and
`versionName='0.2.0'` on the artifact. **Going forward, bump both together and keep `versionName` on a
normal semver ladder (`0.2.0` → `0.3.0` → …) each time `versionCode` increments** — added to the
checklist below.

**Follow-up again same day:** the `versionCode=2` build above had already been consumed by an upload
attempt before the `versionName` fix landed, so it couldn't be reused either. Bumped once more —
`versionCode` 2 → `3`, `versionName` kept in lockstep at `0.3.0` — rebuilt, reverified `versionCode='3'`
/ `versionName='0.3.0'` on the artifact. **The AAB in `build/ScrapSiege.aab` as of this writing is
versionCode=3 / versionName 0.3.0 and has not yet been uploaded.**

### 2026-08-09 — Findings 1, 2, 4, 5 fixed and verified on a real signed artifact; Play Store product live

All five open/pending findings from the 2026-08-08 audit are now closed. In order: added the release
build path + build-time signing guard (Finding 1), forced internal-only install location (Finding 2),
generated the upload keystore outside the repo and confirmed no password landed in `ProjectSettings.asset`
(Finding 4), renamed the product label (Finding 5), then built a real signed release **AAB**
(`build/ScrapSiege.aab` — Play requires AAB, not APK, for apps first published after Aug 2021) and
verified every fix against the actual artifact with `bundletool build-apks` + `apksigner verify
--print-certs` + `aapt2 dump badging/xmltree`: real cert (not `CN=Android Debug`), not debuggable,
`internalOnly` install location, label `ScrapSiege`. Findings 3 re-verified unchanged (still accepted,
still just the cosmetic gate).

**Also done this session, downstream of the security fixes:** created the `scrap_siege_pro`/`monthly`
subscription in Play Console, registered it in RevenueCat (`prod759b1f896f`, attached to entitlement
`entl844b33dd6b` and package `pkgef5eaf57c5e`), and swapped `MonetizationManager.revenueCatApiKey` in
`Assets/Scenes/ARTest.unity` from the Test Store key to the real Play Store key
(`goog_BPqxjAwHxIuYgSZXpVxbuhuaLbt`) — confirmed present in the rebuilt AAB, Test Store key confirmed gone.
The AAB has **not yet been uploaded to Play Console Internal Testing** as of this writing — that upload,
and the first real on-device license-tester purchase, are the next steps. See
[[project_scrap_siege_monetization_handoff]] (memory) for the up-to-date task queue.

**New operational note, not a security finding:** this Unity version does not persist
`PlayerSettings.Android.keystorePass`/`keyaliasPass` across an Editor restart (confirmed twice — see
Finding 4 below). Every time the Editor is closed and reopened, signing must be re-applied from the
credentials file before a release build will succeed. `Assets/Editor/BuildScript.cs`'s build-time guard
catches the failure mode cleanly (a clear exception, not a silent debug-signed fallback) but doesn't fix
the need to re-enter the password.

### Audit 2026-08-08 — full scan before Play Console / billing work

Method: `.gitignore` review; secret-pattern scan over all tracked files; the same scan replayed across
**every commit on every branch**; `--diff-filter=A` sweep for sensitive filetypes ever added; review of
the monetization and entitlement code; and direct inspection of the built `build/ScrapSiege.apk` with
`aapt2` and `apksigner`.

#### Clean — verified, not assumed

- **No secret has ever been committed.** No `sk_`, `AIza…`, `ghp_`, `AKIA…`, bearer token, or PEM
  private key in any tracked file, in any commit, on `main` or `two-player-archive`.
- **No credential-bearing file has ever been added** — no keystore, `.jks`, `.p12`, `.pem`, `.env`, or
  `google-services.json` in the entire history.
- **`.gitignore` is the standard Unity template** and correctly excludes `build/`, `Library/`, `Temp/`,
  `Logs/`, `UserSettings/`, `*.apk`, `*.aab`. The working tree has **zero** untracked files, so nothing
  is sitting one careless `git add -A` away from exposure.
- **The app makes no network calls of its own** — no `UnityWebRequest`, `HttpClient`, sockets, or
  third-party analytics. The only traffic is RevenueCat's SDK and ARCore. Nothing to intercept.
- **No `PlayerPrefs` usage at all**, so no locally-editable state to tamper with.
- **APK permissions are minimal and each is justified**: `INTERNET` (RevenueCat), `CAMERA` (AR),
  `BILLING` (purchases), `ACCESS_NETWORK_STATE`, plus Unity's own receiver permission.
- **ARM64-only** (`AndroidTargetArchitectures: 2`), `minSdk 26`, `targetSdk 36` — meets current Play
  requirements.
- **`CloudAnchorManager.cs` holds no credentials** — it is an unwired stub from the abandoned two-player
  work. Dead code, not a leak.

> **Known false positive:** `ProjectSettings.asset` contains
> `ps4Passcode: frAQBc8Wsa1xVPfvJcrgRYwTiizs2trQ`. That is Unity's hard-coded default, identical in
> every Unity project ever created, and relates to a platform this game does not target. **It is not a
> secret. Do not "fix" it and do not panic about it in a future audit.**

> **Known false positive (found 2026-08-09, scanning the release AAB):** a raw `grep` for `sk_` inside
> `assets/bin/Data/*` of the built APK hits `sk_ColorMatrix`, `sk_StencilRef`, `sk_LitStencilReadMask`
> and similar. These are Unity URP/shader property name strings (the `sk_` prefix is a shader-keyword
> convention), not RevenueCat secret keys — a real leaked secret key looks like `sk_<40+ random
> alphanumeric chars>`, not a readable identifier. **Do not panic about `sk_`-prefixed shader symbol
> names in a future artifact scan; only flag a match that is actually random-looking.**

> **Known false positive (found 2026-08-09, same scan):** a `grep` for `test_` also hits
> `test_synchronised`, a RevenueCat SDK internal identifier (reads as an English phrase), not the old
> Test Store API key. The real key, when present, reads as `test_<20ish random alphanumeric chars>` with
> no vowel-spaced words in it — same rule as the `sk_` false positive above.

#### Finding 1 — HIGH — the shipped APK is debuggable and debug-signed

`build/ScrapSiege.apk` carries `android:debuggable=true` and is signed
`CN=Android Debug, O=Android, C=US`. A debuggable app lets anyone attach a debugger to the live process
on an ordinary unrooted device and read or rewrite its memory — including entitlement state. Google Play
also rejects debuggable uploads outright.

**Cause:** `Assets/Editor/BuildScript.cs` defaults to `development: true` on every path — the menu item,
the Unity MCP entry point, and batch mode unless `-scrapReleaseBuild` is passed. That default was the
right call while adb logcat was the only way to diagnose on-device bugs, and it should stay the default
for *testing* builds. What is missing is a distinct, obvious release path.

**Status: VERIFIED FIXED 2026-08-09.** Added a dedicated `Scrap Siege/Build Android APK (RELEASE - for
Play Store)` menu item calling `BuildAndroidFromEditor(development: false)`, and `RunBuild` now throws
before building at all if `development == false` and `PlayerSettings.Android.useCustomKeystore` is
false. Release builds now produce an **AAB** (`build/ScrapSiege.aab`), not an APK — Google Play rejects
bare APK uploads for any app first published after August 2021. Confirmed on the real artifact: extracted
a universal APK from the AAB with `bundletool build-apks`, then `apksigner verify --print-certs` showed
`CN=ScrapSiege` (the real upload cert, not `CN=Android Debug`), and `aapt2 dump xmltree ...
AndroidManifest.xml \| grep debuggable` returned nothing. The development default is unchanged for
adb-testing builds.

#### Finding 2 — MEDIUM — app installs to external storage

`AndroidPreferredInstallLocation: 1` (`preferExternal`), confirmed in the APK manifest as
`install-location:'preferExternal'`. Code on external or adopted storage is more exposed to tampering
than internal-only storage, and it is the wrong posture for an app processing payments. There is no
reason for this game to prefer external storage.

**Status: VERIFIED FIXED 2026-08-09.** `ProjectSettings.asset` now sets `AndroidPreferredInstallLocation:
2` (`ForceInternal`), and `aapt2 dump badging` on the built release APK confirms
`install-location:'internalOnly'`.

#### Finding 3 — MEDIUM — Pro entitlement is enforced client-side only, and that is currently fine

`ProEntitlement.IsUnlocked` is a plain static `bool` set from `MonetizationManager.ApplyCustomerInfo`.
On a rooted device with a memory editor it can be flipped. There is no server-side re-check.

**This is an accepted risk, not a defect,** because the only thing behind the gate is a cosmetic terrain
palette (`TerrainObjectSpawner.ProColorForArchetype`). Nothing of value can be stolen by flipping it,
and RevenueCat still validates the actual receipt server-side, so no fraudulent *purchase* is possible.

**The rule this creates:** if a Pro feature is ever added that has real value — server-delivered
content, virtual currency, a competitive advantage — it must not be gated on this flag alone.
**Status: ACCEPTED, with the rule above.** Re-verified 2026-08-09 — still just the cosmetic terrain
palette, no code change needed.

#### Finding 4 — LOW, but the biggest *new* risk — no signing config yet

`AndroidKeystoreName` and `AndroidKeyaliasName` are empty and `androidUseCustomKeystore: 0`. Nothing is
leaked today precisely *because* nothing is configured. The risk arrives with the work about to be done.

`ProjectSettings/ProjectSettings.asset` **is tracked in git**. Depending on Unity version and on whether
"remember password" is used, Unity may write keystore and alias passwords into that file. **Diff it
immediately after configuring signing and before committing.** This is the single most likely way this
project leaks a real credential.

**Status: RESOLVED 2026-08-09.** Keystore generated at `C:\Users\naika\keystores\scrapsiege-upload.jks`
(outside the repo, per this checklist), alias `scrapsiege-upload`, and wired into
`PlayerSettings.Android` via an Editor script that read the password from a local credentials file and
never printed it into the session. **Diffed `ProjectSettings.asset` immediately after, as required:**
it gained `AndroidKeystoreName`, `AndroidKeyaliasName`, and `androidUseCustomKeystore: 1` — **no
password field was written anywhere in the file.** Empirically, this Unity version (6000.5.6f1) does not
persist `keystorePass`/`keyaliasPass` to disk at all, so the risk this finding worried about doesn't
materialize here — but it also means the passwords are session-only in the Editor's memory and need
re-entry (from the credentials file, before it's deleted) after every Editor restart. **Confirmed twice
in practice 2026-08-09**: the Editor was closed/reopened mid-session and the very next release build
failed with "No keystore passwords were found" until signing was re-applied. Finding 1's build-time guard
now also enforces `useCustomKeystore` at build time, not just as a checklist item — so this failure mode
is a clear exception, never a silent fallback to debug signing.

#### Finding 5 — LOW — placeholder identity strings

`companyName: DefaultCompany` and `productName: Scrap`, so the APK's user-visible label is "Scrap".
Not a security issue, but it is what appears on the device and in the Play listing. Worth fixing before
the first upload, since it is nearly free now. Note the package name `com.mikhilnaika.scrapsiege` is
already correct and **can never be changed after the first upload**.

**Status: VERIFIED FIXED 2026-08-09.** `productName` is now `ScrapSiege` in `ProjectSettings.asset`, and
`aapt2 dump badging` on the built release APK confirms `application-label:'ScrapSiege'`. `companyName`
left as `DefaultCompany` — not user-visible and out of scope for this finding.

---

## Things that are deliberately NOT problems

Recorded so future audits do not re-litigate them:

- **The RevenueCat public key being visible in the repo and extractable from the APK.** By design. See
  the table at the top.
- **Verbose `Debug.Log` output and `usePlayerLog: 1`.** Since Android 4.1 an app cannot read another
  app's logcat without root, and nothing sensitive is logged. Keep it for development builds.
- **`exported=true` on two manifest components.** Those are the launcher activity and Unity's standard
  entry points — an app with no launchable activity does not run.
- **IL2CPP not obfuscating strings.** Obfuscation is not a security control for values that must be
  present at runtime anyway. The correct control is not shipping secrets, which this project does.
