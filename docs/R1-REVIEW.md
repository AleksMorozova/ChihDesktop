# v0.3.0 Phase R1 review

Status at R1 completion: ready for code/architecture review, **not cleared for public distribution**. No installer, release, remote, tag or repository publication was created. At that checkpoint the workspace had no Git metadata; the earlier uncommitted diff could not be reconstructed. The list below records the known R1 edits rather than claiming a Git-derived diff. Subsequent local Git initialization and release-readiness checks are recorded in [RELEASE-READINESS.md](RELEASE-READINESS.md); they do not establish earlier history or artwork permissions.

## Implemented and reviewed

- Empty clean-install planner/reminders, generic dialogue, configurable optional owner name, no public medication schedule.
- Version-1 LocalApplicationData user settings, validated atomic persistence/recovery, preserved unsupported schemas and diagnostics. Draft-schema 0 normalization is in-memory until a verified backed-up save.
- Modeless four-step native onboarding/settings, optional input/reminder, explicit Finish/Skip, persisted incomplete draft, reopening without reset, and WebView2-independent operation. React Settings opens the same native window through one allowlisted action.
- Legacy reminder versions 1/2 retain Kyiv semantics, definitions, IDs, last-fired dates, acknowledgment history and UTC snoozes. Loading performs no schedule installation or primary rewrite. Verified permanent backups protect the first subsequent legacy write, including recovery from a legacy backup.
- Invalid permanent snapshots block replacement. A separately named verified compatible backup protects the current primary if an unsupported future backup must be left untouched.
- Existing reminder zones stay fixed; preferred-zone changes affect new definitions and planner Today. Existing snoozes/history are not reinterpreted. Unsupported retiming/recurrence-kind edits are rejected, not silently accepted.
- Reminder creation, title/time/weekly-day editing, enable/disable, confirmed definition deletion, retained history, independent acknowledgment/dismissal/snooze, notification muting without dropping due records.
- Per-user opt-in autostart with normal-failure rollback and a dedicated removal option. Tests use a mock; no real registration was changed.
- Source exclusions, publish-file privacy guard, neutral documentation and synthetic fixtures. The frontend is collected before target-path assignment so no-build file-list calculation also contains its assets.
- No R1 SQLite schema migration or reminder-to-SQLite conversion. Existing planner tests remain passing.

## Actual verification

| Check | Result |
| --- | --- |
| Debug solution build | Passed, zero warnings/errors |
| Debug .NET tests | 152 passed, 0 failed/skipped |
| Release solution build | Passed, zero warnings/errors |
| Release .NET tests | 152 passed, 0 failed/skipped |
| Frontend npm ci | Passed |
| Frontend unit/workflow tests | 28 passed |
| TypeScript + production Vite build | Passed |
| Isolated automated Chrome tests | 3 passed |
| Native WebView2 + production assets + SQLite CRUD smoke | Passed with isolated database/settings/browser profile |
| Computed file manifest only | 72 files, 4 frontend entries, no detected profile/photo entries |
| Privacy guard negative canary | Correctly rejected synthetic SQLite WAL filename |
| Production artwork | 15/15 SHA256 values match the original baseline; no text/EXIF chunks found |

All existing 86 .NET tests remain, with 66 added R1 test cases. The 94/65/35 historical workflow assertion counters still run. Personal schedule fixtures were replaced by fictional data; expectations that previously required automatic schedule installation were deliberately changed to exact-retention/no-startup-rewrite assertions.

No full application was launched against the developer's real LocalApplicationData. Compatibility was tested with synthetic copies and isolated directories, not by migrating or comparing live personal records. No phone automation tools/files were used. SpriteBuilder was compiled as an existing solution project but its entry point was never executed. No sprite-generation or alignment utility was run.

The computed manifest and expected-rejection evidence are under ignored `artifacts/r1-review`; they are diagnostics only, not a release payload. Docker checks are not applicable: no Docker build/runtime definition was introduced or found for this phase.

## Known R1 file changes

General lifecycle / native UI:

- `App.xaml.cs`
- `DesktopComposition.cs`
- `CatWindow.cs`
- `PetConfiguration.cs`
- `ReminderStore.cs`
- `ReminderCenter.cs`
- `PersonalitySettingsPanel.cs`
- `ReminderDefinitionWindow.cs` (new)
- `UserSettingsStore.cs` (new)
- `UserSettingsWindow.cs` (new)
- `WindowsAutostartRegistration.cs` (new)
- `Admin/AdminBridge.cs`
- `Admin/AdminWindow.cs`

Core:

- `ChihDesktop.Core/UserSettings.cs` (new)
- `ChihDesktop.Core/Abstractions.cs`
- `ChihDesktop.Core/ReminderModels.cs`
- `ChihDesktop.Core/ReminderDataCodec.cs`
- `ChihDesktop.Core/ReminderScheduler.cs`
- `ChihDesktop.Core/ReminderCommands.cs`
- `ChihDesktop.Core/DialogueCatalog.cs`
- `ChihDesktop.Core/PersonalityService.cs`
- `ChihDesktop.Core/PlannerService.cs`

Frontend:

- `ui/chih-admin/package.json`
- `ui/chih-admin/package-lock.json`
- `ui/chih-admin/src/App.tsx`
- `ui/chih-admin/src/contracts.ts`
- `ui/chih-admin/src/browserDemo.ts`
- `ui/chih-admin/src/test/App.test.tsx`

Tests / safe verification tooling:

- `tests/ChihDesktop.Tests/ReleaseFixtures.cs` (new)
- `tests/ChihDesktop.Tests/R1SettingsTests.cs` (new)
- `tests/ChihDesktop.Tests/R1ReminderTests.cs` (new)
- `tests/ChihDesktop.Tests/R1OnboardingTests.cs` (new)
- `tests/ChihDesktop.Tests/R1PrivacyTests.cs` (new)
- `tests/ChihDesktop.Tests/ReminderRegressionTests.cs`
- `tests/ChihDesktop.Tests/PersistenceRegressionTests.cs`
- `tests/ChihDesktop.Tests/PersonalityRegressionTests.cs`
- `tests/ChihDesktop.Tests/CoreAndCompositionTests.cs`
- `tests/ChihDesktop.Tests/PlannerTests.cs`
- `tests/ChihDesktop.Tests/AdminBridgeSqliteTests.cs`
- `tools/AdminWebViewSmoke/Program.cs`

Release hygiene / documentation:

- `ChihDesktop.csproj`
- `.gitignore`
- `README.md`
- `Assets/Sprites/README.md`
- `docs/RELEASE-PRIVACY.md` (new)
- `docs/R1-REVIEW.md` (new)

Production PNGs, original private photos/references, phone automations and EF migrations were not edited. Original reference files remain locally present; exclusion is not deletion and cannot prove removal from any unavailable Git history.

## Remaining gates and manual Windows checks

1. In the actual Git repository inspect tracked files, staging, commit metadata and history. If private content was tracked, perform a separately approved index/history cleanup and rotate exposed credentials before publishing.
2. Choose a public code license and document illustrated-frame authorship/redistribution permissions. Existing asset presence and a character description are not a documented license.
3. In a disposable Windows account/copy of a profile test a genuinely fresh launch, closed/unfinished setup across restart, Skip, opt-in reminder, editing/deleting, quiet hours, snooze/dismiss/done and reopening setup. Compare copied legacy bytes/IDs/preferences before and after, including error recovery. Do not use the real developer profile for destructive testing.
4. Manually check pet movement/drag/click/pause/exit while setup/admin is open, keyboard/screen-reader focus, DPI/monitor bounds and WebView2 missing/failed fallback. Native onboarding controls and lifecycle have automated STA coverage; no manual desktop acceptance session was claimed.
5. Test actual sign-in registration/removal and executable paths containing spaces in the disposable account. Registry and JSON are not one crash-atomic transaction; inspect/repair the rare mismatch after a process crash. No real registry state was exercised by tests.
6. Before a later packaging phase, inspect a fresh computed/payload file list and test runtime prerequisites. Do not reuse old workspace ZIPs or ignored distribution artifacts.

Deferred by scope: installer/signing/distribution, GitHub publishing/integration, full React reminder/settings editor, broader localization/history browsing, and an explicit migration workflow for changing existing reminders' zones or recurrence kinds. Native setup uses the existing admin palette and remains the fallback. No unrelated infrastructure or Docker runtime was added.
