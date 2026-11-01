# Windows installer — Phase R2.1

The target is ChihDesktop 0.4.0 for Windows x64. Installer execution must first
be tested in a disposable Windows account/VM; see RELEASE-ACCEPTANCE.md. These
scripts do not publish, push, tag, install the app, or modify personal profiles.

## Builder prerequisites

- Windows x64, .NET 10 SDK (verification used 10.0.401), Node.js 24 and npm.
- PowerShell 7.4+ (`pwsh`), also required by installer regression tests.
- Inno Setup 7 (verified with 7.1.1), or 6.4+, from [the official download page](https://jrsoftware.org/isdl.php).
  An explicit `-IsccPath` takes precedence. Otherwise ISCC.exe is resolved from
  PATH, then standard Program Files, Program Files (x86) and current-user Programs
  locations, checking Inno Setup 7 before 6. Neither global PATH nor Git config is changed.
- Network/package caches for NuGet and npm. Compiler builds also require the
  official Microsoft WebView2 bootstrapper, downloaded or supplied explicitly.
  A supplied bootstrapper must pass Microsoft Authenticode validation.

Stop this project's Vite server before `npm ci`: Windows locks its native
bundler module. Scripts fail rather than killing other applications, silently
clearing build folders or continuing after a failed tool command.

## Commands

Run from the checkout root in PowerShell. Choose a new, empty artifacts path on
each publish; outputs cannot point to LocalAppData or an installed application.

```powershell
pwsh -NoProfile -File scripts/Publish-Windows.ps1 -OutputDirectory "$PWD/artifacts/release-0.4.0/publish"
pwsh -NoProfile -File scripts/Build-Installer.ps1 -PublishDirectory "$PWD/artifacts/release-0.4.0/publish"
```

The publish script runs `npm ci --no-audit --no-fund`, `npm run build`, solution
restore/Release build, RID restore, then this exact application-only command:

```powershell
dotnet publish ChihDesktop.csproj -c Release -r win-x64 --self-contained true -p:PublishProfile=WinX64 -p:DebugType=none -p:DebugSymbols=false --no-restore -o "$PWD/artifacts/release-0.4.0/publish"
```

Do not execute that command without the preceding restore/notices/validation
steps when preparing an installer. The profile retains WPF, disables trimming,
single-file and ReadyToRun, and excludes PDBs. The release script copies project
licenses, resolved dependency notices and a SHA-256 payload manifest. The
manifest lists every payload file except itself. Validation checks runtime
assemblies, actual native SQLite loading/export availability, WPF embedded PNG
hashes, local frontend references/CSS/JS, and privacy exclusions. Installer builds
revalidate the manifest before compiling, so stale or altered payloads fail.

Optional paths for an offline/cached builder:

```powershell
pwsh -NoProfile -File scripts/Build-Installer.ps1 `
  -PublishDirectory "$PWD/artifacts/release-0.4.0/publish" `
  -IsccPath 'C:/Program Files/Inno Setup 7/ISCC.exe' `
  -WebView2BootstrapperPath "$PWD/artifacts/prerequisites/MicrosoftEdgeWebview2Setup.exe" `
  -OutputDirectory "$PWD/artifacts/installer-review"
```

Default installer outputs:

- `artifacts/installer/ChihDesktop-Setup-0.4.0-win-x64.exe`
- The same filename followed by `.sha256` (SHA-256 and basename).

The script prints full output path, size and checksum. An existing EXE is never
overwritten; choose another output directory. Failed outputs remain diagnostic
artifacts, not releasable packages. Do not distribute the initial failed or
unverified publish folders. Scripts use pinned application/package versions and
the npm lockfile, but do not promise bit-for-bit identical installers: SDK,
compiler, timestamps, dependency servicing and Evergreen bootstrapper versions
matter. Preserve the exact validated bootstrapper and checksum for repeat builds.

## Installation and data safety

Supported package baseline: Windows 10 22H2 (build 19045+) or Windows 11, x64.
ARM64 and 32-bit Windows are not targeted. Use a serviced OS. Self-contained
distribution includes the .NET runtime, WPF and native SQLite. End users need
no SDK, separate .NET runtime, Node, Docker, browser installation or Vite server.

The per-user installer requires no administrator elevation and installs into
`%LOCALAPPDATA%/Programs/ChihDesktop`. Start Menu shortcut is created; desktop
shortcut and launch-at-finish are optional. The normal application entry point,
single-instance mutex and AppContext-relative `AdminUi` paths are unchanged.

Personal data stays separately in `%LOCALAPPDATA%/ChihDesktop`: planner SQLite,
reminder JSON, settings, backups and WebView2 profile. Installer files come only
from the validated publish directory; no data is seeded or read by setup.
The stable Inno AppId is `{DBB073A1-919D-4B98-BF22-6E77BDF22C23}`. Keep it and
the default install path unchanged for later upgrades. Setup/uninstall refuses
to proceed while the application's existing mutex is present; close Chih using
Exit. It does not force-kill a process that may be saving user data.

Upgrade reuses the previous application directory. It never replaces personal
data or enables startup. An existing opt-in startup entry remains valid because
the installed executable path stays stable. An entry referring to an unrelated
portable/development location is not silently retargeted; the user can toggle
autostart in Settings after moving to the installed app. The existing validated
JSON recovery and non-destructive SQLite migration paths remain authoritative.

Uninstall removes installed binaries/shortcuts and invokes
`ChihDesktop.exe --remove-autostart` to remove only ChihDesktop's current-user
startup entry. It does **not** remove the personal data folder or WebView2
Runtime. Settings/history are retained, including the saved preference; reinstall
does not automatically restore a removed startup registration. Deliberate data
removal is a separate user decision after a verified backup. No recursive data
deletion is part of setup/uninstall.

## WebView2 and offline operation

Setup detects Evergreen Runtime in the documented per-user/per-machine registry
locations and views. If missing, an unchecked task offers Microsoft's official
Evergreen bootstrapper. The builder verifies the bootstrapper's Authenticode
signature is valid and Microsoft-signed, embeds its SHA-256, and never executes
it on the build machine. Setup checks those packaged bytes before executing
`/silent /install`, without requesting elevation itself. Microsoft's updater
determines the actual Runtime installation scope under its own rules.

The small bootstrapper needs internet to download Runtime components. It is
not an offline Runtime bundle. Failure or skipping leaves the native pet,
onboarding, settings and reminder center available. The planner displays native
recovery guidance, not a blank browser window; retry by closing/reopening it
after installation. For a disconnected machine, obtain the official Evergreen
Standalone Installer x64 on another device and transfer it separately. Runtime
availability does not imply a compatible installed browser is required.

References: [Microsoft distribution guidance](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/distribution),
[official Runtime downloads](https://developer.microsoft.com/microsoft-edge/webview2/),
[Inno per-user privileges](https://jrsoftware.org/ishelp/topic_setup_privilegesrequired.htm),
[Inno application mutex](https://jrsoftware.org/ishelp/topic_setup_appmutex.htm).

## Notices, signing and remaining acceptance

Code is MIT; Chih artwork uses ASSET-LICENSE.md. NuGet package license metadata,
available license/notice files, runtime-pack notices and React/ReactDOM/scheduler/
Zod licenses are bundled. `installer/licenses/Apache-2.0.txt` is the unmodified
license text from https://www.apache.org/licenses/LICENSE-2.0.txt for SQLitePCLRaw.
Review the actual generated dependency inventory before public binary release;
automated collection is not a legal certification. No dependency license is
overridden. Neither the app nor this installer is code-signed in R2.1. Windows
SmartScreen/Defender may warn about an unknown publisher. Do not advise users to
disable protection; verify source, expected SHA-256 and publisher before making
a trust decision. The owner-approved Assets/Branding/chih-icon.ico supplies the application, setup and shortcut icons. Test-AppIcon.ps1 validates native resource bytes in the actual binaries.

Manual install/upgrade/uninstall, real sign-in startup, no-runtime/offline setup,
Start Menu launch without development tools, DPI/monitors and sprite rendering
in a fresh account remain NOT RUN until recorded in RELEASE-ACCEPTANCE.md.
