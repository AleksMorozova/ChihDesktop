# v0.3.0 manual Windows acceptance

Every test below is **NOT RUN**. Automated tests are recorded separately; they
do not establish these acceptance results. Record Windows/runtime versions,
build identifier, expected/actual results and pass/fail when executing each row.
Use a disposable standard Windows account and synthetic data. Do not test
recovery against the developer's real profile. No installer exists yet, so
installation/uninstallation checks await the later packaging phase.

| Status | Scenario and steps | Expected result |
| --- | --- | --- |
| NOT RUN | Fresh profile: launch a complete build in a disposable account, offline, without Node/SDK. Inspect local app-data paths. | Pet starts; no personal reminders/tasks or owner; no system-wide writes. Required desktop runtime is documented. |
| NOT RUN | First run: close onboarding before completion, restart, then explicitly Skip; reopen onboarding from Settings. Repeat Finish with optional steps skipped. | Pet stays usable; closing is not completion; Skip/Finish persists only after success; reopening resets no records. |
| NOT RUN | Set a synthetic name, blank name, Cyrillic text and literal markup such as `<test>`. Restart and pet Chih. | Name is literal text, generic fallback works, preferences persist; no executable markup. |
| NOT RUN | Create an explicitly enabled synthetic daily/weekly reminder; edit title/time/day and disable/re-enable. Delete another after cancelling then confirming. | Only enabled definitions fire; edits persist; cancel retains definition; delete retains history. |
| NOT RUN | Trigger a reminder, close/dismiss its bubble, snooze ten minutes, then explicitly confirm. Repeat with grouped items, confirming just one. | Closing/dismissal never completes; snooze retains UTC deadline; independent acknowledgment persists after restart. |
| NOT RUN | Create/edit a planner task, set date/priority/category, complete/reopen, restart, filter and delete with confirmation. | Real SQLite records persist; Today uses preferred zone; errors are actionable; no demo tasks in WebView2. |
| NOT RUN | Enable then disable sign-in startup in Settings; sign out/in each time. Use executable path containing spaces. | Only current-user ChihDesktop entry exists, correct quoting, no duplicates; disable prevents launch; no admin prompt. |
| NOT RUN | Simulate WebView2 initialization failure in disposable environment; test a machine without the Runtime. | Pet and native onboarding/settings/reminders remain usable; planner gives recovery guidance without data reset. |
| NOT RUN | Move between monitors at 100/150/200% DPI, including negative monitor coordinates; disconnect a monitor. | Cat/bubbles remain visible and sensibly sized; no clipping or offscreen admin window. |
| NOT RUN | Drag, click, pause/resume, sleep/wake, open menus and admin/settings, then Exit while a bubble is visible. Type in another application during notifications. | Smooth animation, correct click/drag distinction, no focus theft, unrelated desktop input works, clean exit. |
| NOT RUN | Copy a synthetic legacy Kyiv profile and planner database into the disposable account. Hash copies before startup. Restart, sleep/resume and cross DST/timezone boundaries using a controlled clock/test machine. | No startup rewrite/default schedule injection; IDs, schedule/history/snoozes retained; no duplicate notifications; verified backup precedes first legacy write. |
| NOT RUN | Exercise corrupt primary with valid backup, future schema, read-only/full storage and registry-write failure using copied synthetic data. | No silent reset or false durable success; diagnostics retained; clear error; valid backup preserved. |
| NOT RUN | Change preferred timezone with existing reminders and UTC snoozes. | Existing reminder zones/history stay fixed; new definitions and planner Today use preferred zone; no automatic retiming. |
| NOT RUN | After packaging exists: install/upgrade/uninstall, including opted-in startup. For an unpackaged copy, first run `ChihDesktop.exe --remove-autostart`, then remove only application files. | Startup registration removed; user data retained unless user explicitly chooses removal; no destructive profile cleanup. |

Publication requires compliance with the confirmed artwork terms in `ASSET-LICENSE.md`,
review of the exact staged/exported files and verified commit identity, and a later
inspection of the actual packaged payload and dependency notices. Passing
automated checks alone is not a release approval.
