# macOS alpha packaging review — 2026-11-01

## Implemented scope

The isolated Avalonia prototype is preserved. The manual-only workflow at
`.github/workflows/macos-alpha.yml` packages native Apple Silicon and Intel builds
on matching macOS 15 runners. SDK 10.0.401 is pinned only for the Mac project.
Neither the Windows solution nor installer is built by this workflow.

`tools/ChihDesktop.Mac/packaging/build-alpha.sh` creates the complete self-contained
bundle, a ten-size ICNS from the approved PNG, licenses/notices, a file manifest,
and a permission-preserving ZIP plus SHA-256. Native validation runs before and
after extraction. Output is isolated under ignored `artifacts/mac-alpha/`; existing
output is refused rather than overwritten. No DMG, signing, notarization or release
publication is implemented. See [tester instructions](MACOS-ALPHA.md).

## Executed locally on Windows

| Check | Actual result |
| --- | --- |
| Mac project Debug and Release build | Passed, zero warnings/errors at build time |
| Existing prototype self-check, each configuration | 6,055 assertions passed (6,000 sampled frame-bound assertions) |
| New portable packaging self-check, each configuration | 14 parser/negative assertions passed |
| Fresh self-contained osx-arm64 and osx-x64 publishes | Both passed; NU1900 warnings because NuGet vulnerability service was unreachable |
| Raw published payload inspection, both RIDs | Passed required files, Mach-O CPU, self-contained configuration and privacy filename checks |
| Wrong architecture negative check | Correctly rejected ARM64 payload requested as x64 |
| Dependency/runtime notice collection, both RIDs | Passed using restored package metadata and runtime-pack notices |
| Bash syntax, both scripts | Passed with Git Bash; scripts use LF line endings |
| Workflow syntax | actionlint 1.7.12 passed; optional shellcheck/pyflakes disabled, Bash syntax checked separately |
| Existing Windows tests, Debug | 214 passed, 0 failed, 0 skipped |
| Existing Windows tests, Release | 214 passed, 0 failed, 0 skipped |
| Production sprites and approved branding | All 15 PNGs plus PNG/ICO branding SHA-256 unchanged |
| Existing Windows installer | SHA-256 unchanged: `8477C6D96C50EC01EA9E337319AC1E25603B27FFABFA482E3931F262942239EF` |
| Pre-existing source comparison | Only Mac Program.cs, MacPetWindow.cs and README.md changed; Windows/Core/Infrastructure sources unchanged |

The first Windows test attempt was aborted because the sandbox prevented VSTest
from connecting to its testhost. Both configurations passed when rerun outside
that restriction. Git Bash likewise required permission to initialize its Windows
runtime; no packaging script was executed as part of the syntax check.

## Not executed / release gates

- **GitHub Actions: NOT RUN.** Workflow and prototype remain local changes; nothing
  was pushed, tagged, published or dispatched. A maintainer must review and make
  the workflow available on the default branch before manual dispatch.
- **Native macOS packaging: NOT RUN here.** No claim of an existing valid `.app`,
  ICNS or downloadable alpha ZIP. Apple tools and native runtime execution are
  checked by the proposed workflow, pending its first actual run.
- **macOS GUI: NOT TESTED.** Transparency, hit testing, dragging, animations,
  context menu, petting, focus, Dock/Quit, Spaces, multiple monitors and Retina
  require a real Mac tester. CI success does not satisfy these checks.
- No original photographs, user profiles, reminder files, databases or credentials
  are packaging inputs. Filename exclusion checks are a guard, not a substitute
  for final release content review.
- Public signed release still requires Apple Developer ID/signing and hardened
  runtime review, notarization/stapling, downloaded-build Gatekeeper testing,
  dependency licensing review and hardware/OS acceptance. macOS 14 is the declared
  minimum, not a claim of tested desktop compatibility.

SpriteBuilder was not executed. Production artwork, user data, reminder schedules,
phone automations and remote repository settings were not changed.
