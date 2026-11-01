# ChihDesktop macOS alpha — build and tester guide

This is the in-memory desktop-pet prototype, **not** the complete Windows planner.
It includes the existing Chih sprites, walking/resting/sleeping/stretching/reaction
animations, dragging, petting and a context menu. Tasks, reminder storage, React
admin, notifications and login startup are not connected. It does not import,
migrate, reset or open existing Windows user data. Multiple instances are currently
allowed, so quit the previous instance before opening another.

## Availability and manual workflow

Workflow file: `.github/workflows/macos-alpha.yml`, display name **macOS alpha**.
It has **only workflow_dispatch**: no automatic build on push, release or tag.
The agent does not push or dispatch this workflow. Download only from a successful
run of the reviewed revision. Previous artifacts do not acquire later packaging
or signing fixes automatically; request a rebuilt ZIP after a signature failure.

The workflow and its required sources/assets must first be present on the repository's
default branch for the manual Run workflow button to appear. A maintainer with
appropriate access can then:

1. Open the ChihDesktop repository on GitHub, select **Actions**.
2. Select **macOS alpha** in the workflow list.
3. Choose **Run workflow**, select the reviewed branch/ref and confirm.
4. Wait for both **Alpha (osx-arm64)** and **Alpha (osx-x64)** jobs to succeed.
5. Open the run's summary and scroll to **Artifacts**. Sign in if required for download.

No user-configured secret or Apple account is required. Checkout uses read-only
repository permissions, actions are pinned to verified commits, and upload uses the
runner's standard artifact service. The workflow does not publish a GitHub Release,
push a branch, create a tag or change repository settings. Runner availability,
Actions policy and quota still depend on the repository/account.

Official instructions: [manual workflows](https://docs.github.com/en/actions/how-tos/manage-workflow-runs/manually-run-a-workflow?tool=webui)
and [artifact download](https://docs.github.com/en/actions/how-tos/manage-workflow-runs/download-workflow-artifacts?tool=webui).

## Select the correct download

Use Apple menu → **About This Mac**:

| About This Mac | Download artifact | ZIP inside it (current app version) |
| --- | --- | --- |
| Chip: Apple M-series / Apple Silicon | `ChihDesktop-macos-alpha-osx-arm64` | `ChihDesktop-macos-alpha-0.4.0-osx-arm64.zip` |
| Processor: Intel | `ChihDesktop-macos-alpha-osx-x64` | `ChihDesktop-macos-alpha-0.4.0-osx-x64.zip` |

Use About This Mac rather than relying solely on `uname -m`, which may reflect a
Rosetta-translated Terminal on Apple Silicon. Do not choose Intel on Apple Silicon
just to work around an alpha failure. The declared minimum macOS version is **14.0**;
actual macOS desktop acceptance remains required. CI uses native macOS 15 ARM64
and Intel runners, not a simulation of all supported releases.

Artifacts expire after 14 days. Ask a maintainer to rerun the manual workflow if a
download has expired. A green run validates packaging and non-GUI checks; it does
not prove transparency, Spaces or pointer behavior on a real user's desktop.

## Extract, verify and install

GitHub may download an **outer artifact ZIP**. Extract that first. Inside are the
versioned application ZIP, its `.zip.sha256` file, an external `.bundle-manifest.json`
and `build-info.txt`. Keep the manifest outside the app; it records the final signed
bundle bytes and is not an Apple trust credential.

Before opening the application, check the repository, run/commit and build-info.
In Terminal, change into the folder containing the inner ZIP and checksum, then:

```sh
shasum -a 256 -c ChihDesktop-macos-alpha-0.4.0-osx-arm64.zip.sha256
```

For Intel, substitute `osx-x64`. Expect **OK**. A checksum detects changed bytes;
it is not an Apple signature, notarization or proof that an unknown source is safe.

1. Extract the **inner** application ZIP with Finder/Archive Utility or `ditto`.
2. Find **ChihDesktop.app**. Keep it intact; do not copy only Contents/MacOS/ChihDesktop.
3. Copy the app to Applications, or to your own `~/Applications` folder if you want
   a per-user alpha installation. Do not overwrite another build that is running.
4. Open ChihDesktop.app. No separately installed .NET SDK/runtime, Node.js, Docker
   or Windows WebView2 is needed. Keep the whole .app bundle together.

ZIPs are generated with `ditto --keepParent`, and CI extracts them again to verify
executable permission, every copied runtime file, the icon and the manifest.
No DMG is generated in this alpha phase; copying the intact .app is the installation.

## Ad-hoc signed and unnotarized — Gatekeeper

This alpha is **not Developer ID signed or notarized**. The workflow explicitly
replaces inherited native signatures with ad-hoc signatures, signs nested Mach-O
files first, then seals the complete app last. It requires
`codesign --verify --deep --strict --verbose=2` to pass on the finished app and again
on the app extracted from the actual downloadable ZIP. Ad-hoc signing supplies
integrity checks, not an identified-developer identity or Gatekeeper approval.
macOS may still refuse to open it. `spctl` results are collected as diagnostics;
a policy rejection alone is expected for this alpha and is not a signature pass.

For a trusted downloaded app, collect signature diagnostics without modifying it:

```sh
codesign --display --verbose=4 /Applications/ChihDesktop.app
codesign --verify --deep --strict --verbose=2 /Applications/ChihDesktop.app
spctl --assess --type execute --verbose=4 /Applications/ChihDesktop.app
```

The resource seal must be at `Contents/_CodeSignature/CodeResources`, not beside
the app or under Resources. A missing seal or failed strict verification is a
packaging defect: stop and request a new build, rather than editing/re-signing the
installed app or bypassing protections. CI signature/native diagnostics are in
`ChihDesktop-macos-native-diagnostics-<RID>` for the corresponding workflow run.

Only proceed if you recognize and trust the source and have verified the checksum.
After attempting to open it, use **System Settings → Privacy & Security → Open Anyway**
if macOS offers that app-specific exception, and confirm only for this app. The exact
screen varies by macOS version and organization policy. See
[Apple's safe-opening guidance](https://support.apple.com/en-us/102445).

Do **not** disable Gatekeeper system-wide, use `sudo spctl --master-disable`, remove
quarantine recursively, or bypass “will damage your computer”/malware warnings.
If macOS reports damage, a missing signature, policy restrictions or no Open Anyway
option, stop and report the exact message. Recheck the hash and request a rebuilt or
properly signed app. The ad-hoc alpha is not guaranteed to open on every managed Mac.

## Desktop acceptance checklist — not established by CI

All checks below require a real user desktop and remain **NOT RUN** until recorded:

- **Transparency:** Chih appears without an opaque rectangular background or unwanted
  shadow. Transparent padding inside the small window may still intercept clicks;
  per-pixel desktop click-through is not implemented.
- **Dragging:** left-drag moves Chih, release stops dragging, a short click reacts,
  and dragging does not accidentally count as petting. Test capture loss.
- **Animations:** observe walking both directions, idle, sitting, sleeping, stretching
  and reaction. Check paws/baseline, clipping, flicker and smooth time-based motion.
- **Menu:** secondary click/trackpad right-click and Control-click open it. Test Пауза /
  Продолжить, Спать, Проснуться, Погладить, Вернуть на экран and Поверх окон.
- **Petting:** the alpha plays the existing reaction animation. Speech/reminder
  delivery is not connected in this prototype; do not report absent speech as a new promise.
- **Displays:** drag between Retina and non-Retina displays, try negative monitor
  arrangements, disconnect a display, change scaling and Dock/menu-bar positions.
- **Spaces:** test Mission Control, switching desktops, Stage Manager and fullscreen
  apps. Topmost is not a promise to appear in every Space; no native all-Spaces shim
  is enabled. Record unexpected activation or Space switching.
- **Sleep/wake:** no large teleport or burst of movement after resume.
- **Quit:** use Выход in the pet menu or application/Dock Quit. Verify the process ends
  and no invisible window remains. Keep this recovery path available.
- **Icon:** confirm the approved pink-paw Chih icon in Finder, Dock and About/menu UI.
  The original artwork is unchanged; only standard-size ICNS renditions are generated.

## Collect diagnostics safely

The app stores no automatic application log or planner/settings data. For a diagnostic
run, quit other alpha instances and use Terminal. Adjust APP if installed elsewhere:

```sh
APP="$HOME/Applications/ChihDesktop.app"
"$APP/Contents/MacOS/ChihDesktop" --self-test > "$HOME/Desktop/chih-alpha-self-test.txt" 2>&1
"$APP/Contents/MacOS/ChihDesktop" --self-test-packaging >> "$HOME/Desktop/chih-alpha-self-test.txt" 2>&1
"$APP/Contents/MacOS/ChihDesktop" > "$HOME/Desktop/chih-alpha.log" 2>&1
```

The last command launches the pet and returns after quitting. Startup output includes
app/runtime/OS/process architecture; window output includes achieved transparency,
scale and display count. If startup crashes, also inspect Console.app → Crash Reports
for ChihDesktop. Do not change security protections just to collect a log.

Report:

```text
Workflow run URL / source commit / artifact filename:
Checksum result:
macOS version and build (sw_vers):
About This Mac chip/processor; process architecture (uname -m):
Displays, resolutions, scaling, Dock placement:
Spaces / Stage Manager / fullscreen state:
Exact steps, expected behavior, actual behavior:
Reproducibility, exact error text, diagnostic log:
```

Screenshots or short recordings help. Crop/redact unrelated documents, desktop files,
account names, home-folder paths, notifications and other private information before
sharing logs/crash reports/screenshots. The app does not load private tasks or reminders.

## Maintainer implementation and local validation

- Stable alpha bundle identity: `org.chihdesktop.alpha`; executable `ChihDesktop`;
  display name `ChihDesktop Alpha`; package APPL; minimum macOS 14.0; Retina enabled.
- Assembly/DLL/deps/runtimeconfig remain named ChihDesktop.Mac. Only the packaged
  runtime payload lives in `Contents/Resources/runtime`, including its native
  sidecars. `Contents/MacOS` contains only the native launcher. The SDK CreateAppHost
  task binds that launcher to `../Resources/runtime/ChihDesktop.Mac.dll`; no shell
  launcher, hand-edited Mach-O load commands or system .NET installation is used.
  Managed PE DLLs must not be put in MacOS: codesign treats that folder as nested
  native code. All other RID publish files are compared by hash before signing.
  Signing changes native bytes: non-Mach-O payloads
  still match publish hashes afterwards, while signatures and an external final
  manifest protect the complete signed bundle before and after ZIP extraction.
- Short version comes from the project. CFBundleVersion is the numeric workflow
  run number and attempt. Dock/normal Quit is retained (`LSUIElement=false`).
- Approved PNG is downsampled into the ten standard/Retina iconset files using sips,
  then iconutil creates ICNS. Validation converts ICNS back to an iconset and checks
  dimensions; the approved PNG/ICO and all 15 sprite hashes are checked unchanged.
- Portable validators check privacy exclusions, self-contained configuration, Mach-O
  CPUs, required native/runtime files, plist values, ICNS container and file manifest.
  macOS validation adds plutil, file/lipo, otool load commands and actual non-GUI
  execution of the bundled app. Extensionless Mach-O helpers such as createdump
  are included in architecture, dependency and signature checks. No desktop GUI
  is started by CI.
- Order: finalize resources/plist/icon/native files → unsigned preflight → explicit
  native ad-hoc signing → outer app seal → external manifest → strict verification
  → ditto ZIP → fresh extraction → repeat strict verification and manifest checks.
  No bundle writes occur after the final signing step. Signing does not use --deep;
  recursive --deep is required only for verification. No hardened runtime or
  entitlement-preservation policy is claimed for this test alpha.
- `CHIH_ALPHA_TRACE=1` temporarily enables CI stage/script/line/exit diagnostics.
  It does not enable shell xtrace, dump environment variables or print arbitrary
  command arguments. Signing failures also print the tail of the signing log;
  full diagnostics are uploaded even if packaging fails. The mock regression
  assessment is labeled `MOCK spctl` to distinguish it from native policy output.
  Each entry script explicitly marks normal completion as its final operation;
  this prevents Bash 3.2 EXIT traps from masking a nounset abort as exit zero.
  Prerequisites and their child scenarios reuse the invoking Bash interpreter;
  the workflow invokes macOS `/bin/bash` explicitly and logs its version.
- Code/artwork licenses, restored dependency license files/copyright metadata and
  runtime notices are included. This collection is not a legal certification; review
  dependencies again before a public signed release.

On a matching Mac, from repository root and a fresh output directory:

```sh
bash tools/ChihDesktop.Mac/packaging/build-alpha.sh osx-arm64 1.1
# Or, on an Intel Mac:
bash tools/ChihDesktop.Mac/packaging/build-alpha.sh osx-x64 1.1
```

Output: `artifacts/mac-alpha/<RID>/dist/`. Existing output is never overwritten.
Build scripts are Mac-only. On other development systems, from tools/ChihDesktop.Mac:

```sh
dotnet build -c Release
dotnet run -c Release --no-build -- --self-test
dotnet run -c Release --no-build -- --self-test-packaging
# Inspect an already cross-published raw payload without running native code:
dotnet run -c Release --no-build -- --alpha-check-publish ../../artifacts/mac-prototype/osx-arm64 osx-arm64
```

The scoped global.json pins SDK 10.0.401 without changing the Windows solution SDK.
The workflow builds only the Mac project and its Core reference. Windows WPF tests
remain Windows-only; they are not claimed run on macOS. Bash/workflow/static parser
checks on Windows do not substitute for the first real macOS Actions run.

## Public signed-release blockers

Successful CI is packaging evidence, not desktop acceptance. Public release still
needs real Apple Silicon/Intel GUI testing, supported-OS/DPI/Spaces acceptance,
appropriate Developer ID signing/hardened-runtime review, notarization/stapling,
Gatekeeper validation on downloaded builds, dependency/artwork review, and final
distribution/upgrade/privacy approval. No login startup, persistence migration,
cloud/AI integration or Mac planner administration is added here.
