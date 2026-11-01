#!/bin/bash
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd -P)"
source "$here/sign-alpha.sh"
temp_base="$(cd "${TMPDIR:-/tmp}" && pwd -P)"
test_root="$(mktemp -d "$temp_base/chih-sign-tests.XXXXXX")"
cleanup() { case "$test_root" in "$temp_base"/chih-sign-tests.*) rm -rf -- "$test_root";; *) exit 1;; esac; }
trap cleanup EXIT
app="$test_root/alpha with spaces/ChihDesktop.app"
mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
printf 'synthetic plist' > "$app/Contents/Info.plist"
for name in ChihDesktop createdump libAvaloniaNative.dylib managed.dll; do printf 'synthetic code/data' > "$app/Contents/MacOS/$name"; done
# Git Bash only recognizes executable text fixtures when they have a shebang.
printf '#!/bin/sh\nexit 0\n' > "$app/Contents/MacOS/ChihDesktop"
chmod +x "$app/Contents/MacOS/ChihDesktop"
events="$test_root/events.txt"
: > "$events"
checks=0
fail() { echo "FAIL: $*" >&2; exit 1; }
pass() { "$@" || fail "$*"; checks=$((checks + 1)); }
reject() { if "$@" > "$test_root/rejected.txt" 2>&1; then fail "Accepted: $*"; fi; checks=$((checks + 1)); }
file() { case "${2-}" in */ChihDesktop|*/createdump|*.dylib) echo 'Mach-O 64-bit executable arm64';; *) echo 'data';; esac; }
plutil() { [[ "$1" == -lint && -s "$2" ]]; }
codesign() {
  local args=("$@") target="${!#}" item
  case "$1" in
    --display) printf '%s\n' "${mock_identity:-Signature=adhoc}"; return 0;;
    --force)
      [[ "$2" == --sign && "$3" == - && "$4" == --timestamp=none ]] || return 80
      for item in "$@"; do [[ "$item" != --deep ]] || return 81; done
      [[ "${mock_sign_fail-}" != "$target" ]] || return 82
      if [[ "$target" == "$app" ]]; then
        [[ "$#" == 7 && "$5" == --identifier && "$6" == org.chihdesktop.alpha ]] || return 83
        mkdir -p "$app/Contents/_CodeSignature"
        printf 'synthetic CodeResources' > "$app/Contents/_CodeSignature/CodeResources"
        printf 'seal\t%s\n' "$target" >> "$events"
      else
        [[ "$#" == 5 ]] || return 84
        printf 'leaf\t%s\n' "$target" >> "$events"
      fi;;
    --verify)
      [[ "${mock_verify_fail-}" != "$target" ]] || return 85
      if [[ "$target" == "$app" ]]; then
        [[ "$#" == 5 && "$2" == --deep && "$3" == --strict && "$4" == --verbose=2 ]] || return 86
        printf 'verify-bundle\t%s\n' "$target" >> "$events"
      else
        [[ "$#" == 4 && "$2" == --strict && "$3" == --verbose=2 ]] || return 87
        printf 'verify-native\t%s\n' "$target" >> "$events"
      fi;;
    *) return 88;;
  esac
}
spctl() { [[ "$1" == --assess && "$2" == --type && "$3" == execute && "$4" == --verbose=4 && "$5" == "$app" ]] || return 89; echo 'rejected: not Developer ID'; return 3; }

pass alpha_sign_bundle "$app" "$test_root/signing"
pass test "$(grep -c '^leaf' "$events")" = 2
pass test "$(grep -c '^seal' "$events")" = 1
pass grep -q -F $'leaf\t'"$app/Contents/MacOS/createdump" "$events"
pass grep -q -F $'leaf\t'"$app/Contents/MacOS/libAvaloniaNative.dylib" "$events"
if grep -q managed.dll "$events"; then fail 'Managed resource treated as native code'; fi
checks=$((checks + 1))
pass test "$(grep -E '^(leaf|seal)' "$events" | tail -n 1)" = $'seal\t'"$app"
pass test -s "$app/Contents/_CodeSignature/CodeResources"
pass test "$(grep -c '^verify-native' "$events")" = 5
pass alpha_verify_signature "$app" "$test_root/extracted-check"
mock_verify_fail="$app"
reject alpha_verify_signature "$app" "$test_root/rejected-extracted"
unset mock_verify_fail
mock_identity='Authority=Developer ID (synthetic)'
reject alpha_verify_signature "$app" "$test_root/wrong-identity"
unset mock_identity
mock_sign_fail="$app/Contents/MacOS/createdump"
reject alpha_sign_bundle "$app" "$test_root/leaf-failure"
unset mock_sign_fail
mock_sign_fail="$app"
reject alpha_sign_bundle "$app" "$test_root/seal-failure"
unset mock_sign_fail
mock_verify_fail="$app"
reject alpha_sign_bundle "$app" "$test_root/verification-failure"
unset mock_verify_fail
pass alpha_gatekeeper_diagnostic "$app" "$test_root/signing"
pass grep -q 'Assessment exit status: 3' "$test_root/signing/spctl.txt"
rm "$app/Contents/_CodeSignature/CodeResources"
reject alpha_verify_signature "$app" "$test_root/missing-seal"
echo "PASS: $checks signing-order/argv/resource-seal/failure/Gatekeeper-diagnostic checks (mock tools; native macOS verification still required)."
