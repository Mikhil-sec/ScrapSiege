# Privacy Policy

**The authoritative text is [`docs/privacy/index.html`](privacy/index.html)**, a self-contained page
ready to serve from GitHub Pages at `https://mikhil-sec.github.io/ScrapSiege/privacy/` if Pages is
ever enabled on `main` / `/docs`. This file is a pointer and a summary — if the two ever disagree,
the HTML page is the one that governs, and the disagreement is a bug to fix.

Last updated: 14 August 2026.

## Summary

Scrap Siege (`com.mikhilnaika.scrapsiege`) is a single-player AR game with no accounts and no
servers of its own. In short:

- **No accounts, no sign-in.** The app never asks for a name, email address or date of birth.
- **The camera never leaves the device.** ARCore uses camera frames and motion sensors live, in
  memory, to track a flat surface. Nothing is recorded, stored or uploaded, and there is no ML or
  recognition of any kind running on what the camera sees.
- **No ads, no analytics, no tracking.** No ad network, no analytics SDK, no crash-reporting SDK, no
  advertising identifier.
- **Progress is local.** Star ratings, the mute setting and a cached Pro flag live in `PlayerPrefs`
  and are deleted on uninstall.
- **The only data that leaves the device is subscription data**, and only if you buy Scrap Siege
  Pro: an anonymous app user ID, the Google Play receipt, device/OS/app version, and the IP address
  inherent to the request — sent to RevenueCat so the subscription can be verified, acknowledged and
  restored. Payment itself is handled entirely by Google Play; no card data ever reaches us.

## Why this file exists

Originally, because Google Play requires a public privacy-policy URL for any app with in-app
purchases. **That requirement no longer applies to this project:** Scrap Siege is submitted to
Shipaton's Next Gen track, which needs no store release, so there is no Play Console field to fill
(see `plan.md` Section 16 and the submission checklist in the root `README.md`).

It is kept anyway, and kept accurate, because the reasons that survive the change are the real ones:
the app takes actual money through a real Google Play Billing + RevenueCat integration, and this
repository is a judged artifact that a stranger reads. Publishing the page to GitHub Pages is now
optional polish rather than a requirement.

## Maintaining it

The permission table in section 5 of the policy mirrors the permission allowlist in `SECURITY.md`
section B. **If a permission is ever added to the app, both must be updated** — the allowlist is the
security control, the policy is the public statement of it, and Play compares the latter against the
manifest.

Section 2's table describes exactly what the RevenueCat SDK transmits. If `MonetizationManager` ever
starts calling `LogIn`, setting subscriber attributes, or identifying users by anything other than
the SDK's own anonymous ID, that table becomes wrong and must be updated before the next release.
