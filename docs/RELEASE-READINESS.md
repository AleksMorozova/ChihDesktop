# v0.3.0 local release-readiness review

This is the earlier source-baseline checkpoint. For subsequent installer work
and actual package verification/blockers, see [R2.1-REVIEW.md](R2.1-REVIEW.md).

## Decision

**Cleared for the initial local source baseline commit.** Reviewed code,
documentation and all 15 production PNGs pass the source privacy review. The
owner has resolved artwork permission and explicitly approved a public Gmail
commit identity in place of noreply, understanding that it will appear in
public history. This is not approval of a Windows binary release. No remote,
installer, package, release, tag or push is authorized by this review.

The owner confirms all 15 sprites were generated with OpenAI ChatGPT from
original photographs they own of their own cat Chih, and expressly authorizes
their inclusion in the public repository and official releases. The separate
permission and restrictions are recorded in `ASSET-LICENSE.md`. Photographs
remain private. This does not override third-party terms or claim rights the
owner cannot grant.

The authenticated GitHub connector verifies username `AleksMorozova`. The owner
subsequently approved their supplied Gmail address for public commits,
explicitly replacing the earlier noreply requirement. Configure that exact
identity only in local Git configuration; global configuration is unchanged.
The email is intentionally kept out of source/documentation and will be public
in commit metadata with the owner's consent. There is no local prior history
to clean; this review cannot establish the history of other copies.

## Included and excluded files

The staged set contains application/Core/Infrastructure source, EF migrations,
WPF configuration, frontend source and npm lockfile, test and safe tooling
source, sprite baseline metadata, documentation/licenses and the exact 15
authorized production PNGs. The source baseline is ready for a local commit
using the explicitly approved identity.

Exact inventories are retained **locally only** under ignored `artifacts`:

- `release-staged-files.txt`: exact staged paths.
- `release-excluded-files.txt`: exact ignored file inventory at audit time.
- `release-production-artwork.txt`: exact production PNGs now staged.

Excluded categories: original photographs and atlases in `references/`,
`Assets/Backups/`, unverified unused `Assets/chih-preview.png`, `.vs/`, all
`bin/obj`, `node_modules`, frontend `dist`, coverage/browser reports,
`TestResults`, synthetic smoke databases/browser profiles/screenshots under
`artifacts`, logs, hash diagnostics containing local paths, database sidecars,
user reminder/settings JSON, backups and common secret/key files. Ignore rules
are safeguards, not a guarantee against force-add or new secret file names.
Source `appsettings.json` is a reviewed neutral pet template, not a user profile.
The relative sprite SHA256 baseline contains no original photographs or paths.

Candidate text was scanned for original personal dialogue/medication names,
developer home/workspace absolute paths, email addresses, common token/private
key signatures and literal credential assignments. The only match was the
synthetic `https://user@chih-admin.invalid/` rejection test in
`AdminBridgeTests.cs`; no personal email or sensitive source match was found.
This targeted scan is not proof that every possible secret format is absent.
Source security tests use synthetic malicious URLs such as `C:/private.txt`;
these are test vectors, not paths to personal files. Every candidate path was
reviewed, including binary files; only the production PNG set is intended to
become public under the owner's now-confirmed permission.

The reviewed index contains 99 text/source files and 15 unchanged production
PNGs, for 114 files total. The ignored-path inventory is retained locally
(generated caches/reports can change its count). Git's
whitespace check reported existing extra blank lines at EOF in 15 source/test
files. They were not rewritten as part of this release-only pass; this is not a
clean whitespace-check result or a build/test failure.

After the owner's artwork confirmation, the complete 114-file index was
reviewed again. No forbidden private/generated paths or literal credentials
were found. Text scan matches were only the synthetic URL rejection test and
its explanation in this document. All 15 staged PNG blobs match their source
bytes and the original SHA-256 baseline; none contains PNG text or EXIF chunks.
`ChihDesktop.csproj` includes them as WPF resources. The Release
`PersistenceRegressionTests` test was re-executed successfully (1 test,
including its persistence/artwork assertions and embedded byte checks).
The later owner-approved Gmail identity removes the earlier noreply blocker.
Only local Git identity configuration and the initial local commit are
authorized; global configuration and remotes must remain unchanged.

This pass changed `.gitignore`, `README.md`, `Assets/Sprites/README.md`,
`docs/R1-REVIEW.md`, and `docs/RELEASE-PRIVACY.md`, and added `LICENSE`,
`ASSET-LICENSE.md`, `docs/RELEASE-ACCEPTANCE.md`, and this review. Local Git
metadata and ignored verification inventories/logs were also created. No
application source, production asset or dependency version was edited.

## Executed verification

| Check | Actual result |
| --- | --- |
| Debug build | Passed; 0 warnings, 0 errors |
| Debug .NET tests | 152 passed; 0 failed/skipped |
| Release build | Passed; 0 warnings, 0 errors |
| Release .NET tests | 152 passed; 0 failed/skipped |
| Frontend unit/workflow tests | 28 passed |
| TypeScript and Vite production build | Passed; external CSS emitted |
| Automated Chrome tests | 3 passed; synthetic development and production-browser checks |
| Isolated native WebView2 / SQLite smoke | Passed; real bridge CRUD and production assets |
| Production PNG hashes | All 15 match the existing baseline |

The first sandboxed Debug build ended without useful diagnostics; the normal
SDK-access rerun succeeded as recorded above. Browser tests emitted a harmless
NO_COLOR/FORCE_COLOR environment warning. No SpriteBuilder or alignment entry
point was executed. No production JSON profile/database, reminder schedule,
registry autostart setting or phone automation was changed. No application
business logic was modified in this readiness pass.

There is no Docker workflow to check. MIT covers project source/documentation;
third-party licenses remain independent, and binary dependency notice/payload
review belongs to the later packaging phase. All disposable-account/manual
acceptance rows in `RELEASE-ACCEPTANCE.md` remain **NOT RUN**. The automated
Chrome/native smoke results must not be described as manual Windows acceptance.
