#!/bin/bash
set -euo pipefail
[[ "$(uname -s)" == Darwin ]] || { echo 'Native bundle validation requires macOS.' >&2; exit 2; }
app="${1:?app required}"; publish="${2:?publish directory required}"; rid="${3:?RID required}"
version="${4:?version required}"; build="${5:?build required}"; work="${6:?fresh validation directory required}"
here="$(cd "$(dirname "$0")" && pwd -P)"
source "$here/architecture.sh"
source "$here/native-dependencies.sh"
arch="$(mac_arch_for_rid "$rid")"
[[ ! -e "$work" && ! -L "$work" ]] || { echo 'Validation work directory must be fresh.' >&2; exit 1; }
mkdir -p "$work"
exe="$app/Contents/MacOS/ChihDesktop"
[[ -f "$exe" && -x "$exe" ]] || { echo 'Missing executable/execute permission' >&2; exit 1; }
[[ -f "$app/Contents/Resources/alpha-manifest.json" ]] || { echo 'Bundle manifest missing' >&2; exit 1; }
plutil -lint "$app/Contents/Info.plist"
file "$exe" | tee "$work/executable.txt"
verify_macos_architecture "$rid" "$exe"
"$exe" --alpha-check-bundle "$app" "$publish" "$rid" "$version" "$build"

# Decode the real ICNS with Apple's utility, then verify every standard/Retina size.
iconutil -c iconset "$app/Contents/Resources/chih-icon.icns" -o "$work/roundtrip.iconset"
for size in 16 32 128 256 512; do
  for density in 1 2; do
    suffix=''; [[ "$density" == 1 ]] || suffix='@2x'
    png="$work/roundtrip.iconset/icon_${size}x${size}${suffix}.png"
    [[ -f "$png" ]] || { echo "ICNS missing $png" >&2; exit 1; }
    dimensions="$(sips -g pixelWidth -g pixelHeight "$png")"
    expected=$((size * density))
    [[ "$(printf '%s\n' "$dimensions" | awk '/pixelWidth:/ { print $2 }')" == "$expected" ]]
    [[ "$(printf '%s\n' "$dimensions" | awk '/pixelHeight:/ { print $2 }')" == "$expected" ]]
  done
done

# Keep both otool views as evidence. Only -l identifies ID versus LOAD versus RPATH.
capture_binary() {
  local binary="$1" name
  name="$(basename "$binary")"
  verify_macos_architecture "$rid" "$binary"
  otool -arch "$arch" -L "$binary" > "$work/$name.otool-L.txt"
  otool -arch "$arch" -l "$binary" > "$work/$name.otool-l.txt"
  native_dependency_records "$work/$name.otool-l.txt" > "$work/$name.records.txt"
  printf '\nNative commands: %s (%s)\n' "$name" "$arch"
  cat "$work/$name.records.txt"
}
capture_binary "$exe"
while IFS= read -r -d '' library; do capture_binary "$library"; done < <(find "$app/Contents/MacOS" -type f -name '*.dylib' -print0)
# No GUI or binary patching: reject real external LOAD commands, preserve ID metadata.
check_binary() {
  local binary="$1"
  validate_native_dependencies "$binary" "$exe" "$app" "$work/$(basename "$binary").records.txt" "$work/ChihDesktop.records.txt"
}
check_binary "$exe"
while IFS= read -r -d '' library; do check_binary "$library"; done < <(find "$app/Contents/MacOS" -type f -name '*.dylib' -print0)
"$exe" --self-test
"$exe" --self-test-packaging
echo 'PASS: structure/plist/icon/native dependencies/permissions/manifest/non-GUI checks. Desktop behavior NOT tested.'
