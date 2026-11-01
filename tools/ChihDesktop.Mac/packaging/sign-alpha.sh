#!/bin/bash
# Self-contained alpha payload: explicitly sign native leaves, then the app.
# No Developer ID, hardened-runtime claim, notarization or --deep signing.
alpha_is_macho() { [[ "$(file -b "$1")" == *Mach-O* ]]; }

alpha_signature_inventory() {
  local target="$1" log="$2" status
  if codesign --display --verbose=4 "$target" > "$log" 2>&1; then status=0; else status=$?; fi
  printf '\nInventory exit status: %s (diagnostic only)\n' "$status" >> "$log"
}

alpha_verify_signature() {
  local app="$1" work="$2" binary count=0
  mkdir -p "$work" || return 1
  [[ -s "$app/Contents/_CodeSignature/CodeResources" && ! -L "$app/Contents/_CodeSignature/CodeResources" ]] || {
    echo 'Missing Contents/_CodeSignature/CodeResources resource seal' >&2; return 1;
  }
  plutil -lint "$app/Contents/_CodeSignature/CodeResources" > "$work/codesign-resource-seal.txt" 2>&1 || return 1
  codesign --display --verbose=4 "$app" > "$work/codesign-display.txt" 2>&1 || return 1
  grep -q '^Signature=adhoc$' "$work/codesign-display.txt" || { echo 'Expected an explicit ad-hoc alpha signature' >&2; return 1; }
  if ! codesign --verify --deep --strict --verbose=2 "$app" > "$work/codesign-verify.txt" 2>&1; then
    cat "$work/codesign-verify.txt" >&2; return 1
  fi
  # Include extensionless helpers such as createdump, not only *.dylib.
  while IFS= read -r -d '' binary; do
    if alpha_is_macho "$binary"; then
      codesign --verify --strict --verbose=2 "$binary" >> "$work/codesign-native-verify.txt" 2>&1 || return 1
      count=$((count + 1))
    fi
  done < <(find "$app/Contents" -type f -print0)
  [[ "$count" -gt 0 ]] || { echo 'No native code found to verify' >&2; return 1; }
}

alpha_gatekeeper_diagnostic() {
  local app="$1" work="$2" status
  if spctl --assess --type execute --verbose=4 "$app" > "$work/spctl.txt" 2>&1; then status=0; else status=$?; fi
  printf '\nAssessment exit status: %s (diagnostic only; ad-hoc is not Developer ID/notarization)\n' "$status" >> "$work/spctl.txt"
  cat "$work/spctl.txt" || return 1
  return 0 # A policy rejection is not a failed integrity check.
}

alpha_sign_bundle() {
  local app="$1" work="$2" binary count=0 index=0 inherited_status
  [[ -f "$app/Contents/Info.plist" && -x "$app/Contents/MacOS/ChihDesktop" ]] || return 1
  # Managed PE files in this native-code directory make outer codesign fail.
  [[ "$(find "$app/Contents/MacOS" -type f | wc -l | tr -d ' ')" == 1 ]] || {
    echo 'Contents/MacOS must contain only the native launcher; put managed data under Resources/runtime.' >&2; return 1;
  }
  [[ ! -e "$work" ]] || { echo 'Signing diagnostics directory must be fresh' >&2; return 1; }
  mkdir -p "$work" || return 1
  # Capture inherited identities before replacing them; never assume publish == app signing.
  alpha_signature_inventory "$app" "$work/codesign-inherited-bundle.txt"
  find "$app" -name CodeResources -print > "$work/codesign-inherited-resource-locations.txt"
  if codesign --verify --deep --strict --verbose=2 "$app" > "$work/codesign-inherited-verify.txt" 2>&1; then inherited_status=0; else inherited_status=$?; fi
  printf '\nInherited verification exit status: %s (diagnostic only, before signing)\n' "$inherited_status" >> "$work/codesign-inherited-verify.txt"
  while IFS= read -r -d '' binary; do
    if alpha_is_macho "$binary"; then
      index=$((index + 1))
      alpha_signature_inventory "$binary" "$work/codesign-inherited-$index.txt"
      printf '%s\n' "$binary" >> "$work/codesign-native-files.txt"
      [[ "$binary" != "$app/Contents/MacOS/ChihDesktop" ]] || continue
      codesign --force --sign - --timestamp=none "$binary" >> "$work/codesign-sign.txt" 2>&1 || return 1
      codesign --verify --strict --verbose=2 "$binary" >> "$work/codesign-sign.txt" 2>&1 || return 1
      count=$((count + 1))
    fi
  done < <(find "$app/Contents" -type f -print0)
  [[ "$count" -gt 0 ]] || { echo 'No nested native code found to sign' >&2; return 1; }
  # This final write signs the main executable and seals Info.plist and all resources.
  if codesign --force --sign - --timestamp=none --identifier org.chihdesktop.alpha "$app" >> "$work/codesign-sign.txt" 2>&1; then :; else
    local status=$?
    echo "Outer bundle codesign failed (exit $status):" >&2
    tail -n 8 "$work/codesign-sign.txt" >&2
    return "$status"
  fi
  alpha_verify_signature "$app" "$work"
}

if [[ "${BASH_SOURCE[0]}" == "$0" ]]; then
  set -euo pipefail
  here="$(cd "$(dirname "$0")" && pwd -P)"
  source "$here/diagnostics.sh"
  alpha_enable_diagnostics
  alpha_stage sign-bundle
  [[ "$(uname -s)" == Darwin ]] || { echo 'Ad-hoc signing requires macOS codesign.' >&2; exit 2; }
  alpha_sign_bundle "${1:?app required}" "${2:?fresh diagnostic directory required}"
fi
