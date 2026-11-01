# macOS alpha packaging review — 2026-11-01

This document records the original preparation and subsequent fixes. The signing
follow-up at the end supersedes the original unsigned pipeline description.

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

## Follow-up: native dependency validator diagnosis

The CI error `/usr/local/lib/libAvalonia.Native.OSX.dylib` is caused by treating
every entry in `otool -L` as a runtime dependency. Apple's output includes the
image's own `LC_ID_DYLIB`. Portable inspection of the actual local publish files
found the following in **both** CPU slices of `libAvaloniaNative.dylib`:

- `LC_ID_DYLIB /usr/local/lib/libAvalonia.Native.OSX.dylib` — identity metadata.
- Actual `LC_LOAD_DYLIB` / `LC_LOAD_WEAK_DYLIB` commands reference only Apple
  system libraries and frameworks. No load command in either payload requests
  the `/usr/local/lib` Avalonia install name.
- The universal library is included under its published name
  `Contents/MacOS/libAvaloniaNative.dylib`; its SHA-256 in both raw publishes is
  `42765B28691538BCFBE6ED3EC39AFC5C899B43CC9B7CF0CA550F91A0F1FC6533`.
- The only non-system dependency in the inspected payloads is
  `libmscordbi.dylib` → `LC_LOAD_DYLIB @rpath/libmscordaccore.dylib`.
  That image declares `LC_RPATH @loader_path`, and its sibling target exists.

The validator now classifies `otool -l` commands explicitly, excludes only
`LC_ID_DYLIB` from dependency resolution, and resolves `@rpath` through declared
image/main-executable run paths. Real external load commands, missing targets,
external run paths and outside-bundle relative targets remain errors. This flat
payload has no other inherited dependency-chain run paths; future native layout
changes may require a fuller dyld graph resolver, rather than guessing directories.
No Mach-O bytes were changed, so no install-name patching or re-signing was needed.

Executed on Windows for this follow-up: Bash syntax checks, actionlint, 24 prior
architecture checks, 39 new parser/dependency checks, and all 17 actual binary
command sets per RID via portable byte extraction and otool-style replay. The
native directory-symlink test is not run under Git Bash; macOS runs it too.
These replay inputs are not claimed to be actual native `otool` output.

The next workflow run captures real `otool -L` and `otool -l` output plus classified
records for each image, before and after extraction, and uploads them under
`ChihDesktop-macos-native-diagnostics-<RID>` even when packaging fails.
**No new native Actions result has been obtained by the agent. Both architecture
jobs must pass before this packaging change is considered natively verified.**

## Follow-up: ad-hoc signing and extracted-artifact integrity

The tester reported “code has no resources but signature indicates they must be
present.” The previous builder never signed the assembled app or generated its
`Contents/_CodeSignature/CodeResources` resource seal. It copied/renamed binaries
with SDK/vendor signatures, added Info.plist, icons, licenses and a manifest, then
archived without running codesign verification. There was no native load-path
patching: the prior dependency fix only changed validation.

Portable inspection of the local raw payloads confirms the main apphost and
universal Avalonia dylib carry ad-hoc CodeDirectory flags (`0x2`) in both target
slices, while createdump carries a non-ad-hoc signature (`0x10000`). This is not a
signature of the complete newly assembled app. The tester's exact failing ZIP is
not available locally, so its specific signature/seal state cannot be independently
verified on Windows. The next CI captures inherited identity, verification output
and resource-seal locations before replacing signatures.

New order:

1. Finalize all publish files, executable naming/permissions, Info.plist, icon and
   resources. Compare every copied publish byte and validate native dependencies.
2. Explicitly ad-hoc sign and verify each nested Mach-O file, including extensionless
   createdump. Sign the containing app last with identifier `org.chihdesktop.alpha`.
   No --deep signing, preserved vendor entitlements, hardened-runtime policy,
   Developer ID identity or notarization is claimed.
3. Require the standard resource seal and deep/strict codesign verification.
   Write the final full-file SHA-256 manifest **outside** the app. This avoids
   circular hashes or any post-sign resource write. Managed payloads still match
   original publish hashes; signed Mach-O files retain required architectures.
4. Verify signatures/manifest/dependencies, create the actual ditto ZIP, extract
   into a fresh mktemp directory, and repeat all final checks on that extracted app.
   Any signature or manifest failure stops artifact upload. `spctl` runs only as a
   recorded policy diagnostic; rejection does not waive codesign verification.

Actual local results for this follow-up:

- Mac project Debug/Release builds and both self-contained cross-publishes passed
  with NU1900 warnings about the unavailable NuGet vulnerability service, no errors.
- 6,055 prototype assertions and 23 packaging assertions passed per configuration.
  The new assertions cover external manifests, changed native signatures, managed
  payload tampering, post-sign native tampering and a missing resource seal.
- Bash syntax, actionlint, 24 architecture checks, 39 dependency checks and 18
  mocked signing-order/failure/strict-verification/Gatekeeper diagnostic checks
  passed. The native directory-symlink test remains macOS-only.
- Existing Windows Release regression suite: 214 passed, 0 failed, 0 skipped.

**Native codesign/spctl/ditto verification for this revision: NOT RUN. No rebuilt
ZIP is claimed fixed, and neither architecture job is claimed passing.** Both native
jobs must successfully verify the final and extracted bundles, and the tester must
retry a newly downloaded ZIP. Ad-hoc integrity is not Developer ID trust and does
not guarantee Gatekeeper approval. Never remove quarantine or disable Gatekeeper
to conceal a broken signature. See [tester guidance](MACOS-ALPHA.md).

## Follow-up: actual outer-signing failure after the preflight PASS

The agent retrieved the actual job logs and diagnostic ZIPs through the connected
GitHub tools (GitHub CLI is not installed locally):
[run 41330229275](https://github.com/AleksMorozova/ChihDesktop/actions/runs/41330229275),
commit `e4d3a0f02bb1db854e57496661439bbbadfd5bda`.
Both jobs failed, and the validated application artifact upload was skipped.

The last PASS was **pre-sign** validation, not the final ZIP verification. The next
operation was signing; its redirected `codesign-sign.txt` contained the real error:

```text
codesign --force --sign - --timestamp=none --identifier org.chihdesktop.alpha ChihDesktop.app
code object is not signed at all
In subcomponent: .../Contents/MacOS/System.Diagnostics.Contracts.dll
```

Intel reported the same failure for `Contents/MacOS/Microsoft.CSharp.dll`.
The first reported PE assembly depends on traversal order. Managed PE DLLs had
been copied into the bundle's native-code directory, where outer codesign treated
them as unsigned nested code. That failing command was normalized to exit 1 by
`alpha_sign_bundle`, terminating the strict build script. No ZIP creation or
post-extraction verification was reached. The earlier “rejected: not Developer ID”
was from the mocked regression assessment, not a live Gatekeeper rejection.

The corrected layout places only the SDK-generated native launcher in MacOS.
The complete self-contained payload and native sidecars stay together under
`Contents/Resources/runtime`. The pinned SDK CreateAppHost task binds the launcher
to `../Resources/runtime/ChihDesktop.Mac.dll` before signing; .NET resolves its
self-contained runtime from that managed app root. No manually patched Mach-O
load commands, separately installed .NET, shell launcher or symlinks are required.
All native files still receive explicit signatures and architecture/dependency
checks. Managed PE files are sealed as resources instead of treated as native code.
The preflight rejects any extra MacOS entries, compares all other publish files,
and validates the generated apphost's CPU and relative entry path.

Temporary `CHIH_ALPHA_TRACE=1` records stage, script basename, line and exit status
without printing argv/environment or enabling xtrace. Outer codesign failures
also print their diagnostic tail instead of hiding it in an uploaded artifact.
Successful diagnostic helpers explicitly return zero; real integrity failures
still propagate. Codesign, manifest and ZIP round-trip checks remain mandatory.

Actual local verification for this follow-up:

- Debug/Release builds and both self-contained publishes passed with NU1900
  vulnerability-service warnings, no compilation errors.
- The SDK generated both native launchers successfully; portable inspection
  verified arm64/x86_64 headers and the embedded relative managed entry path.
- 6,055 prototype assertions and 26 packaging assertions passed per configuration.
- Bash syntax/actionlint passed; 24 architecture, 39 dependency and 73 signing/
  strict-shell checks passed. The strict-shell cases cover success despite mock
  spctl exit 3, native/outer signing failure, failed strict verification, failed ZIP,
  failed extracted validation, pipefail, command substitution and nounset for both
  RIDs. These are synthetic orchestration tests, not real codesign/ditto runs.
- Windows Release: 214 tests passed; artwork, branding and installer unchanged.

**A fresh native run on the new revision is required for both architectures.**
Re-running the old commit cannot test these local changes. No workflow was
dispatched, pushed or published by the agent, and no new CI success is claimed.
