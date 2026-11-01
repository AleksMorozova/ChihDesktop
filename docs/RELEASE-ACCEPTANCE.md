# v0.3.0 manual Windows acceptance

Every test below is **NOT RUN**. Automated tests are recorded separately; they
do not establish these acceptance results. Record Windows/runtime versions,
build identifier, expected/actual results and pass/fail when executing each row.
Use a disposable standard Windows account and synthetic data. Do not test
recovery against the developer's real profile. The R2.1 installer compiled with
Inno Setup 7.1.1 is available at `artifacts/installer/ChihDesktop-Setup-0.3.0-win-x64.exe`.
Its size is 52,236,650 bytes; SHA-256 is
`6BBB85A990681DFB6277F6BB86A5F90D1BC29AC3576231113513858D24214D06`.
Compilation, input-inventory privacy review and checksum verification passed;
none of those constitutes a manual installation test. Run the rows below only
in a disposable environment. See R2.1-REVIEW.md for compiler and inventory evidence.

That installer predates the later React reminder/settings integration. Test the
new working-tree UI using `artifacts/admin-integration/production-final/ChihDesktop.exe`,
or rebuild an installer from that validated payload before including the new
integration in installer acceptance. See ADMIN-INTEGRATION.md for automated results.

| Status | Scenario and steps | Expected result |
| --- | --- | --- |
| NOT RUN | Fresh profile: launch a complete build in a disposable account, offline, without Node/SDK. Inspect local app-data paths. | Pet starts; no personal reminders/tasks or owner; no system-wide writes. Required desktop runtime is documented. |
| NOT RUN | First run: close onboarding before completion, restart, then explicitly Skip; reopen onboarding from Settings. Repeat Finish with optional steps skipped. | Pet stays usable; closing is not completion; Skip/Finish persists only after success; reopening resets no records. |
| NOT RUN | Set a synthetic name, blank name, Cyrillic text and literal markup such as `<test>`. Restart and pet Chih. | Name is literal text, generic fallback works, preferences persist; no executable markup. |
| NOT RUN | Create an explicitly enabled synthetic daily/weekly reminder; edit title/time/day and disable/re-enable. Delete another after cancelling then confirming. | Only enabled definitions fire; edits persist; cancel retains definition; delete retains history. |
| NOT RUN | Trigger a reminder, close/dismiss its bubble, snooze ten minutes, then explicitly confirm. Repeat with grouped items, confirming just one. | Closing/dismissal never completes; snooze retains UTC deadline; independent acknowledgment persists after restart. |
| NOT RUN | Create/edit a planner task, set date/priority/category, complete/reopen, restart, filter and delete with confirmation. | Real SQLite records persist; Today uses preferred zone; errors are actionable; no demo tasks in WebView2. |
| NOT RUN | On a disposable copied profile, launch the new build, right-click Chih → Open Admin, repeat opening, close/reopen; edit reminders and settings in React and native windows. | One admin window, correct real data, no demo fallback; per-item snooze/dismiss/confirmation remain distinct; stale React edits cannot overwrite native changes; pet input stays usable. |
| NOT RUN | Enable then disable sign-in startup in Settings; sign out/in each time. Use executable path containing spaces. | Only current-user ChihDesktop entry exists, correct quoting, no duplicates; disable prevents launch; no admin prompt. |
| NOT RUN | Simulate WebView2 initialization failure in disposable environment; test a machine without the Runtime. | Pet and native onboarding/settings/reminders remain usable; planner gives recovery guidance without data reset. |
| NOT RUN | Move between monitors at 100/150/200% DPI, including negative monitor coordinates; disconnect a monitor. | Cat/bubbles remain visible and sensibly sized; no clipping or offscreen admin window. |
| NOT RUN | Drag, click, pause/resume, sleep/wake, open menus and admin/settings, then Exit while a bubble is visible. Type in another application during notifications. | Smooth animation, correct click/drag distinction, no focus theft, unrelated desktop input works, clean exit. |
| NOT RUN | Copy a synthetic legacy Kyiv profile and planner database into the disposable account. Hash copies before startup. Restart, sleep/resume and cross DST/timezone boundaries using a controlled clock/test machine. | No startup rewrite/default schedule injection; IDs, schedule/history/snoozes retained; no duplicate notifications; verified backup precedes first legacy write. |
| NOT RUN | Exercise corrupt primary with valid backup, future schema, read-only/full storage and registry-write failure using copied synthetic data. | No silent reset or false durable success; diagnostics retained; clear error; valid backup preserved. |
| NOT RUN | Change preferred timezone with existing reminders and UTC snoozes. | Existing reminder zones/history stay fixed; new definitions and planner Today use preferred zone; no automatic retiming. |
| NOT RUN | After packaging exists: install/upgrade/uninstall, including opted-in startup. For an unpackaged copy, first run `ChihDesktop.exe --remove-autostart`, then remove only application files. | Startup registration removed; user data retained unless user explicitly chooses removal; no destructive profile cleanup. |
| NOT RUN | Build the Inno setup EXE; verify SHA-256, signature status/SmartScreen presentation, then install as a standard user without SDK, Node or a separate .NET runtime. Launch from Start Menu with a different working directory. | No elevation, complete self-contained launch, correct assets and local data path; optional desktop shortcut works. |
| NOT RUN | Repeat setup/uninstall while Chih is running, then Exit and retry. Launch a second instance after installation. | Setup/uninstall requests clean closure; no forced termination; second launch writes no data or duplicate windows. |
| NOT RUN | In a no-WebView2 VM, decline the optional bootstrapper; repeat online with consent, then offline with a failed bootstrapper and separately transferred Standalone Runtime. | Native pet always works; explicit recovery explanation; no blank/trapped planner; reopening planner works after Runtime installation. |
| NOT RUN | Install over a prior installed version with copied synthetic reminders, SQLite tasks, settings, UTC snoozes and opted-in startup; uninstall and reinstall. Hash profiles before setup and inspect after each step. | Setup does not rewrite profiles, upgrade keeps startup/path and stable AppId; uninstall removes startup only, retaining all data; reinstall does not silently enable startup. |

Publication requires compliance with the confirmed artwork terms in `ASSET-LICENSE.md`,
review of the exact staged/exported files and verified commit identity, and a later
inspection of the actual packaged payload and dependency notices. Passing
automated checks alone is not a release approval.

## v0.4.0 Task Companion — additional manual acceptance

These checks are **NOT RUN manually**. Automated isolated Windows helper checks do not
substitute for clicking the actual pet menu.

| Status | Action | Expected |
| --- | --- | --- |
| NOT RUN | Right-click actual pet → Мои задачи…; repeat, minimize, close/reopen, open full planner. | One visible task-list window, keyboard accessibility and correct activation. |
| NOT RUN | Repeat with WebView2 unavailable in a disposable profile. | Native task list and its completion/snooze actions remain functional. |
| NOT RUN | Create date-only, reminder-only and combined synthetic tasks; toggle upcoming. | Independent dates/instants, correct Today/overdue filtering in selected timezone. |
| NOT RUN | Schedule simultaneous reminders, auto-hide/close, restart, open native list. | One bubble, unacknowledged tasks retained, delivered tasks do not duplicate automatically. |
| NOT RUN | Snooze/dismiss/complete from bubbles and native list; edit concurrently in React. | UTC snooze retained, dismiss is not done, stale edit conflicts, completed tasks stop reminders. |
| NOT RUN | Enable quiet hours, disable notifications/titles, change zone, sleep/resume. | Pending reminders retained; no private title in bubble when disabled; UTC instants unchanged. |
| NOT RUN | Upgrade a copied legacy profile with verified backup. | Only additive task columns; JSON schedules, IDs/history/snoozes and unrelated preferences retained. |

See `TASK-COMPANION.md` for the at-least-once crash window and new settings.

## Current Windows acceptance build

See WINDOWS-UAT.md for the current v0.4.0 menu, branding, Calendar placeholder and
local installer results. All manual rows above remain NOT RUN. The automated
Windows harness opens actual WPF popups and windows but invokes click events
programmatically; it does not prove physical clicks on the new production EXE.
Use a disposable account/VM and verified copied profiles for install/upgrade and
retained-data acceptance, especially before opening existing databases.
