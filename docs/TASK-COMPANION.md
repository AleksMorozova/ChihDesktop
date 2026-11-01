# v0.4.0 Task Companion

## Architecture audit and reused components

Before this change, `PlannerTask` had a nullable **calendar** `DueDate`, not a timed
deadline. `PlannerService` already validated inputs and returned immutable DTOs.
`SqlitePlannerTaskStore` used the serialized `PlannerDatabaseExecutor`, off the WPF
Dispatcher, with revision/concurrency checks and explicit persistence failures.
The database had one migration, `20261031154318_InitialPlanner`.

`ReminderCommands` / `ReminderScheduler` owned independent recurring JSON reminders.
`ReminderController` owned one focus-safe `IReminderPresenter`, auto-hide, quiet-hour
checks, interaction suspension, and resume polling. Personality used local catalogs,
owner-name templates, shuffle bags and cooldowns. User settings were versioned JSON.
React already used the native allowlisted bridge and planner services; browser demo
mode was explicit and separate. None of these services is duplicated here.

The earlier real-process admin log was reviewed: the user-confirmed launch recorded
menu click, deferred `Closed`, a visible active normal window at 125% DPI, then local
navigation HTTP 200. The menu animation race correction is retained. Both menu entries
wait for `ContextMenu.Closed`; merely dispatching a callback is not sufficient.
This prior evidence is not a manual verification of the new task menu.

## Data and migration

`PlannerTaskInput.RemindAtUtc` is optional and independent of `DueDate`. We deliberately
retain the existing DateOnly due-date semantics instead of introducing a misleading
`DueAt` UTC timestamp. New and old tasks do not acquire reminders automatically.

The additive EF migration **20261101083212_TaskCompanion** adds:

| SQLite column | Meaning |
| --- | --- |
| RemindAtUtcTicks | Optional one-time instant |
| SnoozedUntilUtcTicks | Optional absolute UTC snooze deadline |
| ReminderStatus | Disabled (0), Pending (1), Delivered (2), Dismissed (3) |
| DeliveredAtUtcTicks | Durable delivery receipt |

Existing rows, IDs, revisions, dates and completion state remain intact. Defaults
are null / Disabled. No JSON reminders are imported, copied, or retimed. The migration
was exercised only against isolated synthetic databases during implementation.
The developer's database was not opened or upgraded by these checks. Startup will
apply the additive migration on the user's later explicit launch. Back up a copied
profile before acceptance testing an upgrade. Older binaries cannot be assumed to
understand the new migration; do not downgrade a real profile or run migration Down.

Local reminder input goes through `TaskReminderTime.Resolve` in Core with an explicit
selected timezone. DST gaps and overlaps are rejected with an actionable error;
choose an unambiguous time. The bridge rejects offset-less reminder instants. UTC
timestamps and snoozes never move when the preferred zone changes. Today uses the
preferred zone; changing it can change calendar task filtering, not reminder instants.

## Delivery and recovery

`TaskCompanionDelivery` shares the existing reminder timer and bubble. It queries at
most once per 30 seconds while eligible, presents one task at a time, and waits for
the shared bubble to become free. Ordinary JSON reminders have priority. Pending
tasks stay pending during quiet hours, dragging/menu interaction, disabled task/global
notifications, or settings recovery. Resume uses the current injected TimeProvider
instant; overdue one-time reminders remain eligible without a replay flood.

The UI shows the bubble **before** committing its delivery receipt. Actions remain
disabled until that receipt succeeds. There is a small at-least-once crash window:
if the process dies after showing the bubble but before the SQLite commit, the
reminder may be shown again on restart. There is no intentional mark-before-show
gap that silently discards a pending notification. Display is not proof that a
person saw it, and receipt persistence cannot be atomic with desktop rendering.

Delivered reminders do not automatically re-toast after restart. They remain in
**Мои задачи…** until acted on. Closing or auto-hiding never completes or dismisses
the task. Dismiss changes reminder state only; Snooze sets Pending plus ten minutes
in UTC. Mark Done explicitly completes the task and disarms the reminder, including
after reopening. Deleting a task prevents further delivery. Editing the reminder
instant or re-enabling it deliberately creates a new pending delivery; other edits
preserve its state. Snooze/dismiss and delivery use revisions; stale editors receive
conflicts rather than overwriting delivery state. Save failures do not claim success.

A concurrent edit/completion between snapshot retrieval and the receipt can briefly
invalidate a displayed bubble; the revision check removes it and never revives the
old task state. Periodic refresh also removes a visible task completed/edited elsewhere.
No claim of exactly-once desktop delivery is made.

The shared `ReminderOccurrence` presentation DTO is ephemeral for task bubbles.
It is never inserted into `ReminderStore`. Completion events carry no title or payload.
Warnings contain operation codes or exception types, not descriptions or task titles.

## Native and React interfaces

Right-click Chih → **Мои задачи…** opens a normal, resizable, modeless WPF task list.
It shows today's incomplete and overdue tasks and outstanding delivered reminders.
The optional toggle also shows future and undated tasks. Controls complete a task,
snooze/dismiss a reminder, refresh, or open the full admin window. One window is reused;
closing releases it so it can reopen. Window bounds are fitted to monitor work area.
SQLite I/O is asynchronous and off the Dispatcher. No WebView2 is required for this list.

React's editor supports opt-in one-time reminder input, explicit zone selection,
preserving an existing UTC instant unchanged, disabling it, status display and snooze.
`planner.resolveTime` and `planner.reminder` are narrowly allowlisted; JavaScript cannot
write delivery receipts. Native mode always uses real services/SQLite. Browser-only
demo mode does not gain access to native reminder scheduling or real profiles.

## Preferences and personality

Settings saves now use **schema 2**. Schemas 0 and 1 are validated and normalized only
in memory, with backward-compatible defaults for four new fields. No file is reset or
silently rewritten on load. The existing store validates and backs up the old primary
before the first explicit save. Older binaries reject schema 2 as unsupported rather
than treating its new fields as corrupt. Use a verified profile backup to roll back;
do not overwrite the current profile with an old executable.

| Field | Default |
| --- | --- |
| TaskReminderNotificationsEnabled | true, but inert until a user explicitly configures a reminder |
| TaskSummarySpeechEnabled | false |
| ShowTaskTitlesInBubbles | true; disable for private, generic desktop bubbles |
| TaskCompletionReactionsEnabled | true |

These are the existing store's PascalCase JSON names; bridge DTOs use camelCase.

Both native and React Settings edit the same UserSettingsService/store, retaining
unrelated settings. Task-specific completion/summary/reminder phrases use the existing
catalog, configurable owner template and shuffle mechanism. Completion reactions and
opt-in summaries share personality cooldowns and respect quiet/fullscreen conditions.
Summaries are at most every 30 minutes, only for actual current desktop-list tasks;
completion events are coalesced, so reactions are intentionally occasional. No task
content is invented. Disabling titles hides them in desktop bubbles, not the task list
or editor that the user explicitly opens. Changing settings hides any active task
bubble immediately; future delivery rechecks preferences after database I/O.

## Reviewable changes

- Core: PlannerTask, PlannerService, TaskCompanion policy/time conversion,
  UserSettings, PersonalityService and DialogueCatalog.
- Infrastructure: PlannerDbContext rows, additive migration/designer and snapshot.
- Desktop: TaskListWindow/manager, TaskCompanionDelivery, ReminderController/factory,
  ReminderBubble, CatWindow, native settings/store and DI registrations.
- Bridge/frontend: AdminBridge, AdminApplicationApi, contracts, task editor/status,
  React Settings and focused tests. Version metadata is 0.4.0.
- Tests: TaskCompanionTests; retained planner/personality/privacy assertions adjusted
  only for two migrations, additional categories and the intentional version bump.
  The original 35 personality checks still execute for the original nine categories;
  new categories have separate tests. The preserved legacy assertions remain covered.
- Windows integration helper: native list reuse/reopen including missing WebView2,
  React reminder configuration and separate-process SQLite persistence.

## Manual acceptance still required

All following checks remain **NOT RUN manually** for v0.4.0:

1. On a disposable/copied profile, right-click the actual pet → Мои задачи… repeatedly,
   close/reopen/minimize/restore; open admin through both paths.
2. Schedule two synthetic tasks together. Observe a single non-focus-stealing bubble,
   Open Task, Snooze, Dismiss, Done, auto-hide, and retrieval from the native list.
3. Sleep/resume, restart, quiet hours, disabled notifications, and title privacy toggles.
4. Complete a task from React while its bubble is visible; test stale edits/write failure.
5. Mixed-DPI/multi-monitor movement, offscreen recovery, drag while a bubble is present.
6. Copy a legacy profile, verify original recurring schedules/history/snoozes and planner
   IDs/revisions, then test the additive migration and shutdown. Do not test on original data.

The independent automated WebView2 smoke test exercises real Windows windows and a
synthetic profile, but does not click the real pet context menu. No new installer,
tag, remote, publication or push is part of this phase.

## Executed verification (2026-11-01)

| Check | Actual result |
| --- | --- |
| Debug solution build | Passed, 0 warnings / 0 errors |
| Debug `dotnet test ChihDesktop.slnx --no-build --no-restore` | 208 passed, 0 failed, 0 skipped |
| Release solution build | Passed, 0 warnings / 0 errors |
| Release complete .NET suite | 208 passed, 0 failed, 0 skipped |
| Frontend Vitest | 40 passed |
| TypeScript / Vite production build | Passed; included in desktop builds and publish |
| Automated Chrome | 3 passed |
| Real WebView2 / SQLite smoke | Passed, 85 logged PASS checks including native-list lifecycle and reminder-only task persistence across a separate process |
| Missing-WebView2 simulation | Passed, 9 logged PASS checks including native-list visibility/reuse/reopen |
| Self-contained payload privacy/dependencies/resources/manifest | Passed |
| Source PNGs | All 15 match the pre-change snapshot and approved baseline |
| Embedded PNGs | All 15 match approved bytes in the final production payload |
| `git diff --check` | Passed after EOF-only whitespace cleanup |

Local logs are under `artifacts/task-companion/` and are excluded from Git. One
intermediate Release run failed the existing weekly scheduling assertion. Its
synthetic output showed restored original definitions, consistent with a persistence
rollback; the exact I/O cause was not captured. The isolated scheduling rerun and
subsequent full Release runs passed. `release-tests-intermediate-failure.log` is
retained, and the weekly loop now asserts `PersistenceError` directly so recurrence
will expose its underlying failure. This intermittent test/storage issue is not
claimed fixed; production recurring scheduling/persistence code was not changed.

The final test executable is:

`artifacts/task-companion/production-final/ChihDesktop.exe`

It is self-contained Windows x64, version 0.4.0, with locally bundled React files.
It was validated without launching the real App against the developer's profile.
Older artifact folders are not the final schema-2 build. Close any running older
Chih instance before manual testing, because the single-instance guard blocks a
second process. Use a disposable Windows account or a verified copied profile for
upgrade acceptance. The existing installer was not rebuilt.

### Source-file inventory for this phase

Existing uncommitted R2.1/admin-runtime work was preserved; the entire working-tree
diff also contains those earlier changes. This phase touched these files:

- `ChihDesktop.Core/PlannerTask.cs`, `PlannerService.cs`, `TaskCompanion.cs`,
  `UserSettings.cs`, `PersonalityService.cs`, `DialogueCatalog.cs`.
- `ChihDesktop.Infrastructure/PlannerDbContext.cs`,
  `Migrations/20261101083212_TaskCompanion.cs`, its `.Designer.cs`, and
  `Migrations/PlannerDbContextModelSnapshot.cs`.
- `ChihDesktop.csproj`, `DesktopComposition.cs`, `CatWindow.cs`,
  `ReminderController.cs`, `ReminderBubble.cs`, `TaskCompanionDelivery.cs`,
  `TaskListWindow.cs`, `UserSettingsWindow.cs`, `UserSettingsStore.cs`.
- `Admin/AdminBridge.cs`, `Admin/AdminApplicationApi.cs`.
- `ui/chih-admin/package.json`, `package-lock.json`, `src/App.tsx`,
  `src/contracts.ts`, `src/SettingsPanel.tsx`, `src/test/App.test.tsx`,
  `src/test/contracts.test.ts`, `src/test/AdminPanels.test.tsx`.
- `tests/ChihDesktop.Tests/TaskCompanionTests.cs`, `PlannerTests.cs`,
  `PersonalityRegressionTests.cs`, `ReminderRegressionTests.cs`,
  `R1SettingsTests.cs`, `R1PrivacyTests.cs`.
- `tools/AdminWebViewSmoke/Program.cs`, `README.md`,
  `docs/RELEASE-ACCEPTANCE.md`, `docs/TASK-COMPANION.md`.

No production sprite, SpriteBuilder source/invocation, existing recurring schedule,
installer source, phone automation, remote or publication was changed by this phase.
