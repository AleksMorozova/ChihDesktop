#!/bin/bash
set -euo pipefail
[[ "$(uname -s)" == Darwin ]] || { echo 'Packaging requires macOS (iconutil/plutil/ditto/lipo).' >&2; exit 2; }
rid="${1:?Usage: build-alpha.sh osx-arm64|osx-x64 build-number}"
build="${2:?Build number required}"
here="$(cd "$(dirname "$0")" && pwd -P)"
source "$here/diagnostics.sh"
alpha_enable_diagnostics
alpha_stage prerequisites
source "$here/architecture.sh"
arch="$(mac_arch_for_rid "$rid")"
[[ "$build" =~ ^[0-9]+(\.[0-9]+){0,2}$ ]] || { echo 'Invalid numeric bundle build' >&2; exit 2; }
[[ "$(uname -m)" == "$arch" ]] || { echo 'Use a native matching-architecture runner.' >&2; exit 2; }
project="$(cd "$here/.." && pwd -P)"
repo="$(cd "$here/../../.." && pwd -P)"
cd "$project" # Select the Mac-only pinned global.json, never the WPF solution.
"$BASH" "$here/test-architecture.sh"
"$BASH" "$here/test-native-dependencies.sh"
"$BASH" "$here/test-signing.sh"
[[ ! -L "$repo/artifacts" && ! -L "$repo/artifacts/mac-alpha" ]] || { echo 'Symlink output parent rejected' >&2; exit 1; }
out="$repo/artifacts/mac-alpha/$rid"
[[ ! -e "$out" && ! -L "$out" ]] || { echo "Refusing to overwrite $out" >&2; exit 1; }
mkdir -p "$out/dist"
publish="$out/publish"
app="$out/stage/ChihDesktop.app"
mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
source_png="$repo/Assets/Branding/chih-icon.png"
[[ -f "$source_png" && ! -L "$source_png" ]] || { echo 'Approved PNG missing or symlinked' >&2; exit 1; }
sprites=("$repo"/Assets/Sprites/*.png)
[[ "${#sprites[@]}" == 15 ]] || { echo 'Exactly 15 production sprite PNGs required' >&2; exit 1; }
source_size="$(sips -g pixelWidth -g pixelHeight "$source_png")"
source_width="$(printf '%s\n' "$source_size" | awk '/pixelWidth:/ { print $2 }')"
source_height="$(printf '%s\n' "$source_size" | awk '/pixelHeight:/ { print $2 }')"
[[ "$source_width" == "$source_height" && "$source_width" -ge 1024 ]] || { echo 'Approved icon must be square and at least 1024 px; refusing to distort artwork.' >&2; exit 1; }
shasum -a 256 "$repo"/Assets/Sprites/*.png "$source_png" "$repo/Assets/Branding/chih-icon.ico" > "$out/artwork-before.sha256"

alpha_stage build-publish
dotnet restore ChihDesktop.Mac.csproj
dotnet build ChihDesktop.Mac.csproj -c Release --no-restore
dotnet run --project ChihDesktop.Mac.csproj -c Release --no-build --no-restore -- --self-test
dotnet run --project ChihDesktop.Mac.csproj -c Release --no-build --no-restore -- --self-test-packaging
version="$(dotnet msbuild ChihDesktop.Mac.csproj -nologo -getProperty:Version)"
[[ "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] || { echo 'Invalid project release version' >&2; exit 1; }
dotnet publish ChihDesktop.Mac.csproj -c Release -r "$rid" --self-contained true \
  -p:DebugType=none -p:DebugSymbols=false -o "$publish"
"$publish/ChihDesktop.Mac" --alpha-check-publish "$publish" "$rid"

# Copy only fresh publish files, not the repository or any user-data directory.
alpha_stage assemble-bundle
payload="$app/Contents/Resources/runtime"
ditto "$publish" "$payload"
rm "$payload/ChihDesktop.Mac" # Only the copied raw host; never change original publish.
dotnet msbuild ChihDesktop.Mac.csproj -nologo -t:CreateAlphaBundleHost \
  -p:Configuration=Release "-p:RuntimeIdentifier=$rid" -p:SelfContained=true \
  "-p:AlphaBundleHostPath=$app/Contents/MacOS/ChihDesktop"
chmod 755 "$app/Contents/MacOS/ChihDesktop"
cp "$here/Info.plist" "$app/Contents/Info.plist"
/usr/libexec/PlistBuddy -c "Set :CFBundleShortVersionString $version" "$app/Contents/Info.plist"
/usr/libexec/PlistBuddy -c "Set :CFBundleVersion $build" "$app/Contents/Info.plist"
cp "$repo/LICENSE" "$app/Contents/Resources/LICENSE"
cp "$repo/ASSET-LICENSE.md" "$app/Contents/Resources/ASSET-LICENSE.md"
cp "$repo/docs/MACOS-ALPHA.md" "$app/Contents/Resources/MACOS-ALPHA.md"
"$publish/ChihDesktop.Mac" --alpha-notices "$project/obj/project.assets.json" "$publish" "$rid" "$app/Contents/Resources/licenses" "$repo/LICENSE"

iconset="$out/Chih.iconset"
mkdir "$iconset"
for size in 16 32 128 256 512; do
  sips -z "$size" "$size" "$source_png" --out "$iconset/icon_${size}x${size}.png" >/dev/null
  twice=$((size * 2))
  sips -z "$twice" "$twice" "$source_png" --out "$iconset/icon_${size}x${size}@2x.png" >/dev/null
done
iconutil -c icns "$iconset" -o "$app/Contents/Resources/chih-icon.icns"
# All resources/load commands are final before signing. Preflight compares raw publish bytes.
alpha_stage pre-sign-validation
bash "$here/validate-bundle.sh" "$app" "$publish" "$rid" "$version" "$build" "$out/check-before-sign"
alpha_stage sign-bundle
bash "$here/sign-alpha.sh" "$app" "$out/signing"
# Do not write inside the bundle after signing (including manifests or diagnostics).
alpha_stage signed-manifest
manifest="$out/dist/ChihDesktop-macos-alpha-$version-$rid.bundle-manifest.json"
"$app/Contents/MacOS/ChihDesktop" --alpha-manifest "$app" "$manifest"
alpha_stage signed-validation
bash "$here/validate-bundle.sh" "$app" "$publish" "$rid" "$version" "$build" "$out/check-before-zip" "$manifest"

zip="$out/dist/ChihDesktop-macos-alpha-$version-$rid.zip"
alpha_stage zip-creation
ditto -c -k --sequesterRsrc --keepParent "$app" "$zip"
alpha_stage zip-extraction
extracted="$(mktemp -d "$out/extracted.XXXXXX")"
ditto -x -k "$zip" "$extracted"
alpha_stage extracted-validation
bash "$here/validate-bundle.sh" "$extracted/ChihDesktop.app" "$publish" "$rid" "$version" "$build" "$out/check-after-zip" "$manifest"
alpha_stage artwork-and-artifact-staging
shasum -a 256 -c "$out/artwork-before.sha256"
(
  cd "$out/dist"
  shasum -a 256 "$(basename "$zip")" > "$(basename "$zip").sha256"
)
{
  printf 'ChihDesktop macOS alpha\nVersion: %s\nBuild: %s\nRID: %s\n' "$version" "$build" "$rid"
  printf 'Source commit: %s\n' "${GITHUB_SHA:-local-working-tree}"
  printf 'Self-contained; explicitly ad-hoc signed, NOT Developer ID signed or notarized. No GUI validation performed.\n'
  printf 'codesign --verify --deep --strict passed before and after ditto ZIP extraction; spctl is diagnostic only.\n'
} > "$out/dist/build-info.txt"
echo "PASS: ready for artifact upload: $zip"
alpha_stage complete
alpha_complete
