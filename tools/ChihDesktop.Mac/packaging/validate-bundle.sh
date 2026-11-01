#!/bin/bash
set -euo pipefail
[[ "$(uname -s)" == Darwin ]] || { echo 'Native bundle validation requires macOS.' >&2; exit 2; }
app="${1:?app required}"; publish="${2:?publish directory required}"; rid="${3:?RID required}"
version="${4:?version required}"; build="${5:?build required}"; work="${6:?fresh validation directory required}"
here="$(cd "$(dirname "$0")" && pwd -P)"
source "$here/architecture.sh"
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

# No GUI is started: inspect native load commands and reject unresolved non-system dylibs.
check_binary() {
  local binary="$1" dependency candidate
  verify_macos_architecture "$rid" "$binary"
  otool -arch "$arch" -L "$binary" > "$work/current-load-commands.txt"
  cat "$work/current-load-commands.txt" >> "$work/native-dependencies.txt"
  while IFS= read -r dependency; do
    dependency="$(printf '%s' "$dependency" | sed -E 's/^[[:space:]]*//; s/[[:space:]]+\(compatibility version.*$//')"
    case "$dependency" in
      /System/Library/*|/usr/lib/*) ;;
      @loader_path/*) candidate="$(dirname "$binary")/${dependency#@loader_path/}"; [[ -f "$candidate" ]] || { echo "Missing $candidate" >&2; return 1; };;
      @executable_path/*) candidate="$app/Contents/MacOS/${dependency#@executable_path/}"; [[ -f "$candidate" ]] || { echo "Missing $candidate" >&2; return 1; };;
      @rpath/*) candidate="${dependency#@rpath/}"; [[ -f "$app/Contents/MacOS/$candidate" || -f "$(dirname "$binary")/$candidate" ]] || { echo "Unresolved @rpath: $candidate" >&2; return 1; };;
      *) echo "Unexpected native load path: $dependency" >&2; return 1;;
    esac
  done < <(tail -n +2 "$work/current-load-commands.txt")
}
check_binary "$exe"
while IFS= read -r -d '' library; do check_binary "$library"; done < <(find "$app/Contents/MacOS" -type f -name '*.dylib' -print0)
"$exe" --self-test
"$exe" --self-test-packaging
echo 'PASS: structure/plist/icon/native dependencies/permissions/manifest/non-GUI checks. Desktop behavior NOT tested.'
