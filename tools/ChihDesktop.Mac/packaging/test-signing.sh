#!/bin/bash
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd -P)"
source "$here/sign-alpha.sh"
temp_base="$(cd "${TMPDIR:-/tmp}" && pwd -P)"
test_root="$(mktemp -d "$temp_base/chih-sign-tests.XXXXXX")"
cleanup() { case "$test_root" in "$temp_base"/chih-sign-tests.*) rm -rf -- "$test_root";; *) exit 1;; esac; }
trap cleanup EXIT
app="$test_root/alpha with spaces/ChihDesktop.app"
native="$app/Contents/Resources/runtime"
mkdir -p "$app/Contents/MacOS" "$native"
printf 'synthetic plist' > "$app/Contents/Info.plist"
for name in createdump libAvaloniaNative.dylib managed.dll; do printf 'synthetic code/data' > "$native/$name"; done
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
spctl() { [[ "$1" == --assess && "$2" == --type && "$3" == execute && "$4" == --verbose=4 && "$5" == "$app" ]] || return 89; echo 'MOCK spctl: rejected: not Developer ID'; return 3; }

# Run real signing/diagnostic functions as unguarded commands in a fresh strict
# shell. Calling them only through pass/reject disables Bash errexit inside them.
if [[ "${1-}" == --scenario ]]; then
  source "$here/architecture.sh"
  source "$here/diagnostics.sh"
  export CHIH_ALPHA_TRACE=1
  alpha_enable_diagnostics
  trap 'status=$?; cleanup; alpha_report_exit "$status"' EXIT
  arch="$(mac_arch_for_rid "${2:?RID required}")"
  scenario="${3:?scenario required}"
  lipo() { [[ "$#" == 3 && "$1" == "$app/Contents/MacOS/ChihDesktop" && "$2" == -verify_arch && "$3" == "$arch" ]]; }
  alpha_stage architecture
  verify_macos_architecture "$2" "$app/Contents/MacOS/ChihDesktop"
  case "$scenario" in
    success|zip-failure|extracted-failure|pipefail-failure|substitution-failure|nounset-failure) ;;
    native-failure) mock_sign_fail="$native/createdump";;
    outer-failure) mock_sign_fail="$app";;
    verify-failure) mock_verify_fail="$app";;
    *) exit 90;;
  esac
  alpha_stage sign-bundle
  alpha_sign_bundle "$app" "$test_root/scenario-signing"
  alpha_stage gatekeeper-diagnostic
  alpha_gatekeeper_diagnostic "$app" "$test_root/scenario-signing"
  alpha_stage zip-creation
  if [[ "$scenario" == zip-failure ]]; then bash -c 'exit 51'; fi
  if [[ "$scenario" == pipefail-failure ]]; then bash -c 'exit 52' | cat; fi
  if [[ "$scenario" == substitution-failure ]]; then unused="$(bash -c 'exit 53')"; fi
  if [[ "$scenario" == nounset-failure ]]; then unset CHIH_ALPHA_MISSING_TEST_VAR; printf '%s\n' "$CHIH_ALPHA_MISSING_TEST_VAR"; fi
  alpha_stage extracted-validation
  if [[ "$scenario" == extracted-failure ]]; then mock_verify_fail="$app"; fi
  alpha_verify_signature "$app" "$test_root/scenario-extracted"
  echo STAGED
  exit 0
fi

pass alpha_sign_bundle "$app" "$test_root/signing"
pass test "$(grep -c '^leaf' "$events")" = 2
pass test "$(grep -c '^seal' "$events")" = 1
pass grep -q -F $'leaf\t'"$native/createdump" "$events"
pass grep -q -F $'leaf\t'"$native/libAvaloniaNative.dylib" "$events"
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
mock_sign_fail="$native/createdump"
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
printf 'synthetic PE' > "$app/Contents/MacOS/Microsoft.CSharp.dll"
reject alpha_sign_bundle "$app" "$test_root/invalid-layout"
rm "$app/Contents/MacOS/Microsoft.CSharp.dll"
rm "$app/Contents/_CodeSignature/CodeResources"
reject alpha_verify_signature "$app" "$test_root/missing-seal"

for rid in osx-arm64 osx-x64; do
  for scenario in success native-failure outer-failure verify-failure zip-failure extracted-failure pipefail-failure substitution-failure nounset-failure; do
    log="$test_root/$rid-$scenario.txt"
    if bash "$0" --scenario "$rid" "$scenario" > "$log" 2>&1; then status=0; else status=$?; fi
    case "$scenario" in
      success)
        pass test "$status" = 0
        pass grep -q '^STAGED$' "$log"
        pass grep -q 'Assessment exit status: 3' "$log";;
      *)
        expected=1
        case "$scenario" in outer-failure) expected=82;; zip-failure) expected=51;; pipefail-failure) expected=52;; substitution-failure) expected=53;; esac
        pass test "$status" = "$expected"
        if [[ "$scenario" == nounset-failure ]]; then pass grep -q '^EXIT: stage=' "$log"; else pass grep -q '^FAIL: stage=' "$log"; fi
        if grep -q '^STAGED$' "$log"; then fail "Staged artifact after $scenario"; fi
        checks=$((checks + 1));;
    esac
  done
done
echo "PASS: $checks signing-order/argv/resource-seal/failure/Gatekeeper-diagnostic checks (mock tools; native macOS verification still required)."
