# Windows acceptance preparation — v0.4.0

Version 0.4.0 was explicitly confirmed by the owner: the working tree already
contains Task Companion and settings schema 2. It was not relabeled or downgraded
to the older 0.3.0 target in the original request. Earlier uncommitted work remains.

## Findings and fixes

- No ChihDesktop.exe process was running at baseline; none was terminated.
- Admin already had the proven `ContextMenu.Closed` workaround. Settings and
  personality actions still used a background dispatch followed by a stale
  `menuOpen` check. They could silently drop the user's click during popup animation.
  All actions now share the actual Closed-event boundary, command diagnostics and
  visible error handling. Input capture is released on opening the menu.
- Explicit menu petting previously inherited a 55% no-response chance and long
  cooldown intended for ordinary left clicks. It now uses the existing personality
  catalog deterministically when permitted, with a three-second cooldown. Rapid
  gestures reuse one response instead of stacking bubbles. Ordinary left-click
  petting retains its original quieter chance/cooldown. Reminder priority,
  personality opt-out, quiet hours and fullscreen restrictions remain respected.
- Added eight short petting phrases, using the existing shuffle bags and owner
  templates. No owner name is hardcoded. Sleeping safely transitions to Reacting.
  Pause remains independent of the requested pose; Resume restores progression.
- Settings, tasks, admin and the reminder center reuse and restore normal windows.
  The reminder center is no longer owned by the taskbar-hidden transparent pet:
  it has its own taskbar entry and is fitted to the monitor work area.
- The supplied PNG and ICO were readable and correct. The ICO already contained
  all seven sizes, so neither file was regenerated. ApplicationIcon, WPF windows,
  setup, shortcuts and uninstall display now use approved branding. Native PE
  resources are checked by bytes, not merely by project configuration.
- Installer version was hardcoded to 0.3.0 despite the 0.4.0 application. It now
  derives from the validated published executable and is passed to Inno explicitly.
  Inno Setup 7 detection and the explicit `-IsccPath` option were already present.
- No `.github/workflows` configuration exists in this working tree. No CI execution
  or publishing is claimed, and no untested remote workflow was introduced.

## Every context-menu entry

The following **automated Windows harness** results use the actual CatWindow,
real WPF popup opening/closing animations and the real handlers. MenuItem click
events are invoked programmatically, not with a physical mouse. All data is synthetic
in a separate profile. Manual acceptance of the new production EXE remains NOT RUN.

| Entry | Executed effect verified in harness |
| --- | --- |
| Pause / Resume | Pause state and header toggle; behavior-model regression verifies suspended progression |
| Sleep | Sleeping state |
| Wake Up | Stretching state, preserving pause preference |
| Reset Position | Recovery after deliberately moving offscreen to primary-monitor coordinates |
| Open Admin / Планировщик Чиха… | Visible window, local WebView2 navigation, reuse, minimize/restore, close/reopen; active normal admin alongside a small topmost pet |
| Мои задачи… | Visible branded native list, reuse, minimize/restore, close/reopen |
| Настройки Чиха… | Visible branded settings, reuse, minimize/restore, close/reopen |
| Напоминания — сегодня | Visible taskbar reminder window, reuse, minimize/restore, close/reopen |
| Настроить напоминания… | Same reminder window, correct Settings tab selected |
| Test Reminder Now… | Configured submenu rebuilt from real definitions; grouped preview and separate break preview exercised |
| Погладить Чиха | Reaction from sleep, visible dialogue, variation after cooldown, rapid coalescing, personality-off and reminder-priority behavior |
| Чего ты хочешь? | Visible requested phrase |
| Характер Чиха | Both directions persisted through the canonical settings service; subsequent petting respects Off |
| Тест случайной фразы | Visible random catalog response |
| Exit | Pet and service windows close; harness exits successfully without Dispatcher exceptions |

On an empty installation the preview submenu is intentionally disabled, with a
tooltip explaining how to create a reminder. An explicitly configured disabled
reminder can also be previewed. No sample schedules are installed automatically.
Preview acknowledgments are separate from real occurrences and do not set LastFired.

The harness waits before closing each popup and verifies that no command ran early.
The final run also checks that its dedicated log contains no `pet.command.failed`.
Physical walking smoothness, focus/Z-order across other applications, mixed-DPI
monitors and the production App startup/shutdown path still need manual acceptance.

## Admin, Calendar and branding

Separate real WebView2/SQLite smoke tests verify frontend loading, typed bridge
CRUD, reminder/settings persistence and fresh-process task persistence. Missing
Runtime is deliberately simulated: native windows still work and the admin gives
an actionable native error panel. No localhost production server is introduced.

Google Calendar and GitHub share an accessible responsive integration-card section.
Calendar has the exact Calendar Companion subtitle, Coming Soon status and Russian
description. Both actions are disabled. No Google/GitHub API, OAuth, tokens, event
creation or synchronization exists. Desktop and 640px screenshots were generated
from synthetic browser mode and visually inspected; neither view overflowed.

Branding: `Assets/Branding/chih-icon.png` and `.ico` remain byte-identical. ICO sizes
are **16, 24, 32, 48, 64, 128, 256**, each 32-bit. PNG chunks contain no text metadata.
All seven actual native resource images match approved bytes in both the published
ChihDesktop.exe and compiled setup EXE. WPF uses a frozen decoded frame. Shortcuts
and Apps & Features point to the application icon; actual installed shortcut,
Explorer cache and uninstaller display remain manual checks. Asset provenance and
separate distribution terms are recorded in ASSET-LICENSE.md; no unsupported claim
about the branding's creation tooling is made.

## Verification and artifacts

Local logs are under `artifacts/uat/`, excluded from Git.

| Check | Final result |
| --- | --- |
| Debug / Release solution build | Passed, zero warnings/errors |
| Complete .NET suite, each configuration | 214 passed, zero failed/skipped |
| Vitest | 42 passed |
| TypeScript + Vite production | Passed; local JS/CSS included in payload |
| Automated Chrome | 4 passed, including responsive Calendar cards |
| Real Windows menu harness | 143 logged PASS checks; final log contains no menu exceptions |
| WebView2/SQLite smoke | 85 logged PASS checks |
| Missing-runtime smoke | 9 logged PASS checks |
| Repeated isolated reminder persistence probe | 50 creations persisted and reloaded successfully |
| Sprites | All 15 match before/after snapshots, approved baseline and embedded payload bytes |
| Branding | Both supplied files unchanged; all seven native icon sizes verified |
| Inno Setup 7 | Successfully compiled; actual setup icon verified |

One intermediate menu-harness run failed during **synthetic fixture creation before
any menu tests**, with a generic seed-failure message. Its cause was not captured.
The harness now reports the underlying persistence error, and its rerun plus the
50-write probe passed. This is not claimed fixed or conclusively diagnosed. The
previous Task Companion review also recorded an intermittent persistence/scheduling
test issue; investigate recurrence before public release. Persistence validation
was not disabled, failed writes were not treated as successful, and production
reminder persistence logic was not changed to mask the failure.

Final artifacts:

- App: `artifacts/uat/production-final/ChihDesktop.exe` (keep its whole folder intact).
- Setup: `artifacts/uat/installer-final/ChihDesktop-Setup-0.4.0-win-x64.exe`.
- Size: **52,668,766 bytes**.
- SHA-256: `8477C6D96C50EC01EA9E337319AC1E25603B27FFABFA482E3931F262942239EF`.
- Matching `.exe.sha256` file is alongside setup. Both binaries are unsigned.

Inno Setup 7 compiled the final setup successfully. All **497 compiler file inputs**
were audited: the 496 validated payload files (including the manifest), plus only
the Authenticode-verified Microsoft WebView2 bootstrapper. No installer extraction,
installation or execution is claimed by this input/resource validation. Earlier
artifact directories/checksums are historical test builds, not the final release candidate.

Real application diagnostics are in `%LOCALAPPDATA%/ChihDesktop/logs/`, named
`admin-opening-<date>-<PID>.jsonl`. Check `app.start` for executable/version/MVID,
`pet.command.request` → `pet.command.start` → `pet.command.complete`/`failed`, then
`admin.open.request`, `admin.visible` and `webview.navigation.complete`.
The harness's equivalent logs stay in its isolated artifact profile. Command IDs
are static; reminder titles, tasks and personal settings are not logged.

## Exact manual acceptance sequence — NOT RUN

Use a disposable Windows account or VM; do not exercise destructive scenarios on
the developer's real profile. Interactive Windows was available for the automated
harness, but the new production EXE was not launched against personal data, and
the installer was not executed in the existing account.

1. Transfer the final setup EXE and its `.sha256` file; verify the hash. Confirm the
   unsigned-publisher presentation and decide signing before broad distribution.
2. Install as a standard user without Node, SDK, Visual Studio or Docker. Decline
   optional WebView2 installation first; verify pet, onboarding, native tasks,
   settings and reminders, and the admin's recovery explanation. Repeat with Runtime.
3. Exercise every menu row above with a real right click. Repeat openings and
   minimize/restore/close/reopen. Verify Pause/Resume movement, Sleep/Wake, Reset
   after moving displays, and Exit terminating the exact process.
4. Pet while idle/sitting/sleeping, rapidly and after cooldown, with personality Off,
   quiet hours, another fullscreen application and a real pending reminder. Confirm
   readable bubbles within monitor bounds, no overlapping bubbles or focus theft.
5. Open admin, create a synthetic task/reminder, restart and confirm persistence.
   View Calendar and GitHub placeholders. Test both light/dark themes.
6. Verify file, taskbar, titlebar, desktop shortcut, Start Menu and Apps & Features
   icons at several DPI settings. Check the uninstaller's actual icon after install.
7. In the disposable environment, use **copies** of an existing profile with verified
   backups and recorded hashes. Upgrade; compare exact recurring definitions/IDs,
   acknowledgment history, UTC snoozes, preferred zone and existing task IDs. Test
   sleep/resume and DST. The installer must not seed, reset or delete profile data.
8. Uninstall; verify startup registration cleanup and retained profile data. Test
   upgrade/uninstall while the pet is running without forced process termination.

There is no public-release approval yet: installation/upgrade/uninstall, real
shortcuts, physical interaction/DPI checks, unsigned-binary policy and the intermittent
fixture failure remain review items. Nothing was pushed, tagged or publicly released.

## Changed files for this request

Earlier dirty-tree changes were preserved. This request changed:

- Desktop: `CatWindow.cs`, `ReminderController.cs`, `ReminderCenter.cs`,
  `ReminderBubble.cs`, `ReminderDefinitionWindow.cs`, `UserSettingsWindow.cs`,
  `TaskListWindow.cs`, `Admin/AdminWindow.cs`, `ChihDesktop.csproj`; new `AppBranding.cs`.
- Core: `PersonalityService.cs`, `DialogueCatalog.cs` only (no scheduling/store refactor).
- Frontend: `App.tsx`, `styles.css`, new `IntegrationsPanel.tsx`, integration/navigation
  tests and `e2e/admin.spec.ts`.
- Build: `installer/ChihDesktop.iss`, `scripts/Build-Installer.ps1`,
  `scripts/Test-Publish.ps1`, new `scripts/Test-AppIcon.ps1`.
- Tests: new `UatTests.cs`, approved-resource guard in `R1PrivacyTests.cs`,
  `tools/AdminWebViewSmoke/Program.cs`, new `PetMenuSmoke.cs`.
- Documentation: README, ASSET-LICENSE, `Assets/Branding/README.md`, INSTALLER,
  RELEASE-ACCEPTANCE, RELEASE-PRIVACY, R2.1-REVIEW and this review.

No sprites, user databases, reminder files, personal settings or phone automations
were modified by these checks. SpriteBuilder was compiled by solution builds but
never executed. The two approved branding files were read and used unchanged.
