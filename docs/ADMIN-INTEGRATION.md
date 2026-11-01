# WPF + React admin integration

## Open the real application

Launch the newly built self-contained executable under
`artifacts/admin-integration/production-final/ChihDesktop.exe`, or the latest desktop
build under `bin/Release/net10.0-windows`. Exit an older running copy first; the
single-instance guard does not replace a running process with a new build.
Right-click the pet → **Open Admin / Планировщик Чиха…**. No Vite server, HTTP
backend, Node or separate browser is required on the target computer. The
separate normal WebView2 window uses the current user's real local profile.

Opening the menu action again reuses the window; closing and reopening creates
a new window/bridge session and retains the pet. A missing WebView2 Runtime
shows native recovery guidance and access to native settings. It does not
disable the pet, onboarding or reminders.

## What inspection found

Already connected: bundled production assets via a restricted virtual host,
typed request correlation, strict .NET/Zod validation, allowlisted operations,
bounded requests/timeouts/disposal, and real PlannerService CRUD/SQLite. The
existing menu action was labeled only in Russian. Native reminders/settings and
the singleton window manager already existed. The Reminders React page was a
placeholder; Settings only offered a native-window button.

## Changes

- `CatWindow.cs`: explicit Open Admin menu label, retaining the Russian label.
- `Admin/AdminWindow.cs`, `DesktopComposition.cs`: supply the same singleton
  services to the bridge; retain normal window lifecycle, runtime fallback and
  restricted navigation. The entry URL includes an index-content fingerprint
  so same-version rebuilds do not reuse an older cached entry page.
- `Admin/AdminApplicationApi.cs`, `Admin/AdminBridge.cs`: explicit immutable DTOs
  and nine additional allowlisted operations over existing services.
- `ChihDesktop.Core/Abstractions.cs`, `ReminderCommands.cs`: focused transactional
  reminder update/enable commands. They do not replace other definitions, quiet
  hours, occurrence history, IDs, message variants or bound zones. The targeted
  `ReminderScheduler.SettingsChanged(id)` path also preserves elapsed time for
  unrelated interval/break reminders when creating/editing/toggling/deleting a
  definition, instead of using the old bulk-editor reset of every interval.
- `ui/chih-admin/src/contracts.ts`, `RemindersPanel.tsx`, `SettingsPanel.tsx`,
  `App.tsx`, `styles.css`: working React controls with loading/empty/error/pending
  states, disabled-by-default new reminders, per-item actions and delete consent.
- `browserDemo.ts`: unsupported native operations fail explicitly in demo mode.
  Browser demo contains only labeled ephemeral sample tasks and is stripped from
  production. A failed native request never falls back to demo data.
- `tests/ChihDesktop.Tests/AdminApplicationTests.cs`, frontend
  `AdminPanels.test.tsx`/`App.test.tsx`, and `tools/AdminWebViewSmoke/Program.cs`:
  regression and real bridge/storage/lifecycle coverage with synthetic profiles.

## Protocol and persistence

The version-1 envelope remains backward-compatible: protocolVersion, requestId,
operation, payload, then a correlated success/error response. Added operations:

| Operations | Existing authority |
| --- | --- |
| reminders.list/create/update/enable/delete | IReminderCommands → existing JSON scheduler/store |
| reminders.snooze/acknowledge | Existing occurrence commands, one stable occurrence ID per action |
| settings.get/save | UserSettingsService → existing atomic JSON store/autostart adapter |
| settings.open (retained) | Native settings/onboarding window |
| planner.* (retained) | IPlannerService → EF Core SQLite |

No reflection, arbitrary paths, processes, SQL or EF entities are exposed to
JavaScript. Native reminder/settings commands run synchronously on the host
Dispatcher, as existing native editors do; SQLite work retains the infrastructure
execution boundary. Errors are returned without payload logging. Save failures
are not reported as durable success and existing rollback/recovery applies.

React settings and reminder edits carry fingerprints. A stale React edit fails with Conflict
instead of replacing newer native edits. The settings save copies only explicit
editable fields into the latest existing model; schema, onboarding completion,
pet size/speed/FPS and other unexposed values remain intact. Scheduling timezone
semantics are unchanged: existing reminder zones/history stay fixed, new
definitions bind to the preferred zone, and snoozes remain UTC instants.

The reminder page shows all definitions and up to 500 latest occurrences that
are pending or dated today in the base scheduling zone, with truncation stated
explicitly. Deleted-definition history remains stored. Dismiss is distinct from
completed. Dismissed items may be explicitly confirmed later, but cannot be
snoozed through the UI because the existing service only snoozes pending items.
Recurrence-kind and timezone reassignment of an existing definition remain
unsupported; create a new definition and disable the old one. Reminder quiet
hours and advanced pet controls remain available through native editors.

## Isolation and verification

All integration tests use synthetic temporary profiles or guarded directories
under ignored artifacts. The real App entry point was not launched against the
developer's LocalAppData. No real schedules, planner tasks, preferences, registry
startup setting, phone automation or artwork was changed. SpriteBuilder was not
executed (solution compilation of its project is unchanged).

The native smoke harness exercises actual React events, WebView2 messages,
application services and disk storage. It tests repeated Open Admin, close and
reopen, task create/edit/complete/reopen/confirmed delete, reminder create/edit/
enable/snooze/dismiss/explicit confirmation, settings save, and preservation of
an unrelated synthetic schedule. Separate helper processes verify the React-
created task after restart and final reminder/settings/deletion persistence.
No in-memory browser adapter is involved. This is automated integration testing,
not manual installed-application acceptance; no full installed-app process was
launched against a real user profile. Missing-runtime simulation remains
process-local and never uninstalls the machine's Runtime.

The earlier `artifacts/r21/production` payload and installer remain untouched;
they predate these admin changes. Installer acceptance, fresh-account/full-App
restart, real sign-in startup and manual visual/DPI/focus review remain separate
NOT RUN checklist items. No installer, release, tag, push or publication is part
of this integration change.

## Executed results

| Check | Actual result |
| --- | --- |
| Debug build | Passed; zero warnings/errors |
| Debug .NET tests | 184 passed, zero failed/skipped |
| Release build | Passed; zero warnings/errors |
| Release .NET tests | 184 passed, zero failed/skipped |
| New backend regression cases | 11; all 173 existing .NET cases retained |
| Frontend tests | 35 passed (7 new panel cases); the old placeholder expectation now asserts the connected page |
| TypeScript / production frontend | Passed |
| Automated Chrome | 3 passed; explicit demo and unavailable modes retained |
| Real WebView2/SQLite/reminder/settings/lifecycle/restart smoke | Passed with synthetic files, including separate helper-process reads |
| Missing-runtime smoke | Passed with a process-local Runtime-folder override |
| Final self-contained publish | Passed complete local frontend/native dependency/privacy/manifest validation |
| Artwork | 15/15 source and embedded SHA-256 values match the unchanged baseline |

Verification logs remain under ignored `artifacts/admin-integration-*`. A
test-only failure-injector type reference initially failed compilation and was
corrected before the successful full builds/tests above. Failure tests verify
that reminder writes roll back disk/memory and settings failures roll back the
mock autostart change rather than reporting success. The break-timer case proves
that unrelated create/edit/toggle/delete commands preserve its elapsed time.

The physical right-click on the live pet and a full installed-App restart were
not manually tested. The menu's deferred action was inspected in source; the
actual window manager and real React/native storage path were exercised in the
automated harness. For a stale reminder edit, cancel its draft and reopen the
editor from the refreshed list; for settings conflicts, refresh the settings
before reapplying edits. Tests never access the real user's app-data profile.
