# ChihDesktop.Mac — isolated pet prototype

This is not the macOS release of the complete planner/reminder application.
It uses Avalonia 12.1.4 and references the unchanged net10.0 Core project.
It never opens a database, reminder JSON, settings file or startup registration.

The project lives under `tools/` deliberately: the existing WPF project excludes
that directory from compilation. Do not move it to a root sibling directory
without reviewing the WPF default compile glob. The Windows solution is unchanged.

From repository root, on a Mac with the .NET 10 SDK:

```sh
dotnet run --project tools/ChihDesktop.Mac
```

Left-click reacts; drag with the left button to reposition. Right-click (or a
trackpad secondary click) opens Pause/Resume, Sleep, Wake, Pet, Reset, Topmost and
Exit. The full cat canvas is 160×130 logical units, retaining its 320×260 sprite
aspect ratio. It walks using elapsed time; all six existing clips are embedded
unchanged. No sprites or icon artwork are generated.

On any development OS, without starting any UI:

```sh
dotnet build tools/ChihDesktop.Mac -c Debug
dotnet run --project tools/ChihDesktop.Mac -c Debug --no-build -- --self-test
dotnet build tools/ChihDesktop.Mac -c Release
dotnet run --project tools/ChihDesktop.Mac -c Release --no-build -- --self-test
```

The self-check is a diagnostic assertion program, not an xUnit suite or a macOS
GUI test. Of its 6,055 assertions, 6,000 are frame-bound checks over time samples.
It also checks all 15 approved embedded PNG hashes, RGBA dimensions, frame timing,
negative monitor origins, small displays and reuse of the Core behavior model.

An explicit `--allow-other-platforms` flag permits non-Mac visual development;
it does **not** verify macOS behavior. Ordinary launch refuses non-macOS hosts.
Multiple prototype instances are currently permitted. Keep a recovery path to
Quit through the app/Dock; no agent-only hiding or global mouse hook is installed.

```sh
dotnet publish tools/ChihDesktop.Mac -c Release -r osx-arm64 --self-contained true -p:DebugType=none -p:DebugSymbols=false -o artifacts/mac-prototype/osx-arm64
dotnet publish tools/ChihDesktop.Mac -c Release -r osx-x64 --self-contained true -p:DebugType=none -p:DebugSymbols=false -o artifacts/mac-prototype/osx-x64
```

These produce local raw publish folders, not `.app` or `.dmg` installers. Keep
the whole folder together. Transfer to the matching Mac for native execution and
signing validation; file-transfer execute bits and Gatekeeper must be checked.
Raw publish folders are not signed app bundles. The separate alpha pipeline
explicitly ad-hoc signs the completed app; it does not provide Developer ID signing
or notarization.

For the manual GitHub Actions workflow, self-contained `.app` packaging, checksums,
ad-hoc signing/Gatekeeper guidance and desktop acceptance steps, see
[macOS alpha instructions](../../docs/MACOS-ALPHA.md). The packaging uses Apple's
tools on matching native macOS runners; no CI run or macOS GUI acceptance is
implied by the portable self-checks. Packaging SDK selection is pinned by this
project's `global.json`; run packaging from this directory through the script.

See [architecture, research, adapters, packaging plan and Mac acceptance checklist](../../docs/MACOS-PROTOTYPE.md).
