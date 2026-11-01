# Actual Windows admin-opening failure

## Observed cause (not inferred from helper tests)

The actual production application received the user's planner menu click.
WPF had already changed `ContextMenu.IsOpen` to false, but its closing animation
had not yet delivered `ContextMenu.Closed`. The pet's `menuOpen` flag therefore
remained true. The old Background-dispatch callback checked that flag and
silently discarded the request. The real trace recorded:

| Event | Observed state |
| --- | --- |
| admin.menu.click | menuOpen=true, IsOpen=false, pressed=false, dragging=false |
| admin.menu.deferred | menuOpen still true |
| admin.menu.dropped | no call to AdminWindowManager.Show |
| pet.menu.closed | arrived roughly 150–170 ms after the discarded callback |

There was no admin creation, navigation or WebView2 exception in that failing
path: none of those stages was reached. The earlier native helper directly
called the window manager and therefore missed the actual context-menu race.
The initially observed process used the expected production-final path, not a
separately installed older build. One additional transient process entry could
not be identified before it exited; its path/version must not be guessed.

## Narrow corrections

- `CatWindow.cs` / `AdminWindowManager.AfterMenuClosed`: the explicit Open Admin
  action waits for the real Closed event, detaches its one-shot handler, then
  dispatches Show. It does not discard an explicit request based on the stale
  menu flag. Other guarded pet/personality actions are unchanged.
- `Admin/AdminWindow.cs`: hidden windows are shown again, minimized windows
  restored, bounds fitted to the nearest monitor's work area via existing native
  positioning/DPI code, then activation requested. Admin remains a normal,
  taskbar-visible, non-topmost window without a transparent-pet owner.
- `WindowPositionService.cs`: the admin-only fit method constrains size using
  monitor physical dimensions converted through the current window DPI, then
  reuses the existing Clamp implementation. Pet movement/sprites are unchanged.
- `App.xaml.cs`: Windows Event Log independently showed an unhandled
  `InvalidOperationException` when Exit re-entered `Window.Close` inside Closing
  after synchronously completing host shutdown. Final Close is now dispatched
  after the original event unwinds. A subsequent real user Exit logged code 0.

No React, SQLite, application-command or bridge protocol rewrite was needed.

## Diagnostics

`Admin/AdminDiagnostics.cs` writes bounded per-process JSONL under
`%LOCALAPPDATA%/ChihDesktop/logs/admin-opening-YYYYMMDD-PID.jsonl`. Startup records
the executable, informational version and assembly module ID (important because
same-version working-tree builds share a version string). Additional events cover
instance blocking, startup stages, actual menu clicks/closing, construction,
Show/activation/visibility/state/bounds/DPI, asset existence, Runtime version,
initialization, navigation status, resource errors and browser process failure.
Bridge diagnostics record only successful operation names or controlled error
codes, never request/response bodies.

Exception logging includes type/HRESULT and method names, not exception messages,
source filenames, parameters, task/reminder contents or user preference values.
Logs are local technical diagnostics, not release assets. The normal manager
failure path shows a native error with the log location rather than silently
ignoring an exception. No console is required.

## Real Windows verification

The transparent pet was not discoverable by the Windows automation helper.
The user performed the actual right-click/menu actions. On the fixed production
build, the user confirmed the visible result; the assistant also inspected the
real rendered React admin window. The corresponding process trace showed:

1. menu.click → pet.menu.closed → menu.after_closed → admin.open.request;
2. created/shown/active, Normal, ShowInTaskbar=true, Topmost=false, Owner=null;
3. visible work-area placement at 125% DPI;
4. WebView2 Runtime 156.0.4314.50, successful initialization;
5. local frontend navigation succeeded, HTTP 200;
6. successful native planner.list and reminders.list replies, with no data logged.

Three actual close/reopen cycles were observed. A second executable launch logged
instance.blocked and clean exit while the original process remained running;
single-instance behavior blocks rather than redirects a new process. The current
monitor was verified; other DPI/monitor arrangements and an actual minimized/
hidden restoration interaction remain NOT TESTED manually.

Debug and Release builds passed with zero warnings/errors. Both full .NET suites
passed **187 tests** (184 existing + delayed-Closed, already-closed and sanitized-
diagnostic regression cases). The actual menu-to-visible-window verification is
separate from those automated results. Root startup/frontend build and fixed
self-contained payload validation passed; all 15 source and embedded PNG hashes
match the original baseline. SpriteBuilder was never executed. The earlier
installer was not rebuilt and its checksum is unchanged.

## Which executable to use

The user-verified build is `artifacts/admin-diagnostics/fixed/ChihDesktop.exe`.
The original `artifacts/admin-integration/production-final` launch directory was
also updated with the exact same verified payload after confirming no process
was running from that directory. Its previous validated contents are preserved
under `artifacts/admin-diagnostics/before-menu-fix`. Both updated payloads passed
the resource/privacy/dependency checks and have matching desktop DLL bytes.

Exit an existing copy before launching a different path; otherwise the new copy
will be blocked. Right-click Chih → Open Admin / Планировщик Чиха… . If it fails
again, inspect the log whose PID/executable matches the running application:

- No menu.click: wrong process/build or click not delivered.
- menu.click without after_closed: inspect menu lifecycle events.
- open.failed: construction/show exception type and method chain are recorded.
- assets indexExists=false: incomplete distribution.
- runtime.missing / initialize.failed / navigation failure: follow the native
  recovery message and technical status; do not reset profiles to troubleshoot.

No user task/reminder/settings edit or reset was performed during diagnosis.
Running the actual application retains its ordinary reminder housekeeping.
No production photographs/sprites, phone automations, installer, remote, push
or release were changed. Do not commit or publish the local diagnostic logs or
screenshots of private application data.

## Later Windows acceptance refresh

The historical reproduction above remains evidence for the original bug. The
current acceptance code now uses the Closed-event dispatcher for every menu action,
including Settings and personality commands. Current menu trace stages are
pet.command.request/start/complete/failed; the admin window and WebView2 stages
remain available. See WINDOWS-UAT.md for current verification and final artifacts.
