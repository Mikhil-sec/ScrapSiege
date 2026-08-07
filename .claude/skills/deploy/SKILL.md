---
name: deploy
description: Build Scrap Siege to Android, install it on the connected phone, launch it, and pull filtered logcat. Use when asked to deploy, build to device, test on phone, run on device, reinstall, or diagnose on-device behaviour that needs real logs.
---

# Deploy Scrap Siege to the phone

The build → install → launch → read-logs loop. This is the project's slowest and most
important feedback cycle: several bugs here were invisible in the Editor and only diagnosable
from logcat (see `CLAUDE.md`).

## Fixed paths

| Thing | Path |
|---|---|
| adb | `C:\Program Files\Unity\Hub\Editor\6000.5.6f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe` |
| Unity | `C:\Program Files\Unity\Hub\Editor\6000.5.6f1\Editor\Unity.exe` |
| Project | `c:\Dev\ScrapSiege\Scrap` |
| APK output | `c:\Dev\ScrapSiege\Scrap\build\ScrapSiege.apk` |
| App id | `com.mikhilnaika.scrapsiege` |

Set `ADB` once per session and reuse it:
```bash
ADB="/c/Program Files/Unity/Hub/Editor/6000.5.6f1/Editor/Data/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb.exe"
```

## Step 0 — check the device first

Never build before confirming there's somewhere to install to. A build takes minutes; this
takes a second.

```bash
"$ADB" devices -l
```

- No devices → stop and tell the user to plug the phone in with USB debugging enabled.
- `unauthorized` → tell the user to accept the "Allow USB debugging" prompt on the phone.
- Multiple devices → pass `-s <serial>` to every subsequent adb call.

## Step 1 — build

**Default path: build through the already-running Editor via Unity MCP.** Unity batchmode
cannot open a project whose `Library/` is locked by a running Editor, and in this project the
Editor is normally open. Use `Unity_RunCommand`:

```csharp
using UnityEngine;
using ScrapSiege.EditorTools;

internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        string summary = BuildScript.BuildAndroidFromEditor(development: true);
        result.Log(summary);
    }
}
```

It returns a one-line `SUCCESS …` / `FAILED …` / `EXCEPTION …` summary. On failure the actual
error lines come back with it — read those rather than guessing.

**Note:** the build blocks for several minutes and the MCP call may time out even though the
build is still running. If the call times out, do NOT immediately rebuild — poll for the APK
instead, then continue once it stops changing:

```bash
ls -l --time-style=full-iso c:/Dev/ScrapSiege/Scrap/build/ScrapSiege.apk 2>/dev/null
```

Check `Unity_GetConsoleLogs` for `[BuildScript]` lines to confirm how it ended.

**Fallback path (Editor closed only):** batchmode.
```bash
"/c/Program Files/Unity/Hub/Editor/6000.5.6f1/Editor/Unity.exe" \
  -quit -batchmode -nographics \
  -projectPath "c:\Dev\ScrapSiege\Scrap" \
  -executeMethod ScrapSiege.EditorTools.BuildScript.BuildAndroidBatch \
  -logFile "c:\Dev\ScrapSiege\Scrap\build\outputs\logs\batch-build.log"
```
Run it in the background and tail that log; exit code 0 = success.

If the build fails on "Active build target is … not Android", the user must switch platform in
File > Build Profiles. That reimports assets and takes minutes, so never trigger it silently —
tell them.

## Step 2 — install

```bash
"$ADB" install -r -d c:/Dev/ScrapSiege/Scrap/build/ScrapSiege.apk
```

`-r` reinstalls keeping data; `-d` allows a version downgrade. On
`INSTALL_FAILED_UPDATE_INCOMPATIBLE` (signature mismatch — happens when switching between a
debug-signed and release-signed build), uninstall first and say so, since it wipes any
RevenueCat purchase state stored locally:

```bash
"$ADB" uninstall com.mikhilnaika.scrapsiege
```

## Step 3 — clear logs, then launch

Clear *before* launching so the log contains only this run. This matters — a stale buffer has
sent debugging down the wrong path before.

```bash
"$ADB" logcat -c
"$ADB" shell monkey -p com.mikhilnaika.scrapsiege -c android.intent.category.LAUNCHER 1
```

Then tell the user plainly what to do on the phone (e.g. "sweep the table, tap Lock This
Table, place the board"), and wait for them to report back. Do not read logs before they've
had time to actually reproduce something.

## Step 4 — pull logs

```bash
"$ADB" logcat -d -s Unity:V
```

Useful narrowings:

| Goal | Command |
|---|---|
| Errors/exceptions only | `"$ADB" logcat -d -s Unity:E AndroidRuntime:E` |
| Plane-detection diagnostics | `"$ADB" logcat -d -s Unity:V \| grep -A8 PlaneLock` |
| ARCore itself | `"$ADB" logcat -d -s Unity:V ARCore:V native-ar:V` |
| RevenueCat | `"$ADB" logcat -d -s Unity:V \| grep -i -E "purchas\|revenuecat\|billing"` |
| Live follow | `"$ADB" logcat -s Unity:V` (run in background, stop when done) |

Always use `-d` (dump and exit) for one-shot reads. A bare `adb logcat` follows forever and
will hang the call.

## Reading the results

Interpret, don't dump. Quote the specific exception or log line that explains the behaviour,
say which file and line it points at, and state what you'll change. If the logs show nothing
relevant, say that explicitly rather than inventing a cause — and consider that the listener
may never have been registered at all (that exact failure mode has happened here: an exception
early in `OnEnable` silently skipped every subscription after it).

Check `CLAUDE.md`'s gotchas list before theorising about AR / NavMesh / URP / RevenueCat
symptoms; most classes of failure in this project have bitten it once already.
