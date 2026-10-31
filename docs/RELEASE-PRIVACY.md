# ChihDesktop v0.3.0 privacy / release gate

This checklist does not authorize publishing. Phase R1 created no repository,
remote, GitHub release, installer or package. The subsequent release-readiness
pass initialized local Git only; see [the current review](RELEASE-READINESS.md).

## Source and history

- [x] New installations have empty task/reminder state, no named owner and no medication schedule.
- [x] Dialogue uses a configurable optional name and generic fallback, retaining the original character.
- [x] Tests use synthetic data and isolated stores; autostart tests use a mock.
- [x] Ignore rules exclude references, original photographs, asset backups, generated outputs, user JSON, databases/sidecars, logs, diagnostics and common secret files.
- [x] Inspect current candidate/index paths and staged content. Artwork permission is recorded; the owner explicitly approved their supplied Gmail for public commit metadata instead of noreply. There is no earlier local history to audit, which does not establish whether another copy has ever been published. Recheck the final commit before publication.
- [ ] Remember: `.gitignore` does not remove previously tracked files or secrets in history. If private files ever entered history, remove them from the index/history in a separate approved cleanup, rotate any exposed credentials, and inspect a clean export before publishing. No history cleanup is performed here.
- [x] Review candidate paths and scan text for personal names/schedules, developer paths, email addresses and common secret signatures; no detected sensitive source matches. Original photos/profiles/generated outputs are excluded. The commit identity is explicitly owner-approved; artwork permission is confirmed. See RELEASE-READINESS.md.
- [x] MIT code/documentation license added; artwork explicitly excluded by the separate rights notice. Third-party dependency ownership is not claimed.

## Artwork

- [x] Preserve all 15 production PNG SHA256 values against `tools/PersistenceChecks/sprite-baseline.json`.
- [x] Embed only `Assets/Sprites/*.png`; do not execute SpriteBuilder or alignment/generation utilities.
- [x] Leave original photographs/references and private alignment backups on the development machine; exclude them from source exports and release assets.
- [x] Record the owner's confirmation: all 15 frames were generated with OpenAI ChatGPT from their own cat photographs, which they own. The owner authorizes the public repository and official releases; separate artwork terms are documented in ASSET-LICENSE.md without overriding third-party rights.
- [ ] Review a clean source export and the actual future release payload, rather than assuming build exclusions protect an arbitrary workspace ZIP.

## User-data safety

- [x] Keep general settings under LocalApplicationData, with atomic writes, schema validation and backup recovery.
- [x] Do not replace existing reminder files with clean defaults or install a new schedule on startup.
- [x] Preserve legacy Kyiv behavior, IDs, history and UTC snoozes. Verify original bytes before rotating a permanent legacy backup on the first subsequent write.
- [x] Keep planner SQLite schema/data separate from JSON reminders; no R1 database migration or reminder import.
- [x] Persist onboarding completion only after a successful Finish/Skip; retain an incomplete new-profile draft across restart.
- [x] New reminders require explicit creation/enabling. Deleted definitions retain history and cannot reuse historical identities.
- [x] Autostart is per-user, opt-in and removable using the dedicated command-line option; tests never mutate real registry state.
- [ ] Manually test upgrades with copied legacy profiles, corrupt/future schemas and full/readonly storage in a disposable Windows account.
- [ ] Verify recovery of the rare registry/JSON mismatch after a crash during an autostart settings change.

## Distribution / manual acceptance

- [x] Public defaults and the native setup path require no account, SDK, Node, Docker or network access at runtime.
- [x] Project exclusions and the computed-publish privacy guard reject private file types and profile paths. This is a build safeguard, not an installer or a substitute for payload inspection.
- [ ] On a disposable account test first launch, close/resume setup, explicit Skip, opt-in reminder creation, save errors, reminder CRUD, snooze/dismiss/confirm, and reopening setup from Settings.
- [ ] Check pet animation/drag/click/pause/exit, mixed DPI/monitors, keyboard focus, screen readers and modeless setup placement.
- [ ] Test WebView2 installed/missing paths and native fallback. End-user prerequisites need a later packaging phase.
- [ ] Test actual sign-in autostart enable/disable, executable paths containing spaces and uninstall removal without deleting user data.
- [x] Debug/Release builds: zero warnings/errors, 152 .NET tests passed each; 28 frontend tests, production build, 3 automated Chrome tests and isolated native WebView2/SQLite smoke passed. See RELEASE-READINESS.md. Manual acceptance remains separate.
- [ ] Inspect final future payload for databases, JSON profiles, logs, backups, certificates/keys, original photos and source maps.

No Dockerfile or runtime Docker requirement is introduced. Phone automations, GitHub integration and cloud services are outside R1 and remain untouched.
