#!/bin/bash
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd -P)"
source "$here/architecture.sh"
checks=0
fail() { echo "FAIL: $*" >&2; exit 1; }
check() { "$@" || fail "$*"; checks=$((checks + 1)); }

# Capture actual argv from the production helper; no native tool or GUI required.
captured=()
lipo() { captured=("$@"); return "${lipo_status:-0}"; }
check test "$(mac_arch_for_rid osx-arm64)" = arm64
check test "$(mac_arch_for_rid osx-x64)" = x86_64
for rid in osx-arm64 osx-x64; do
  case "$rid" in osx-arm64) expected=arm64;; osx-x64) expected=x86_64;; esac
  for binary in '/Users/test/alpha build/ChihDesktop.app/Contents/MacOS/ChihDesktop' '/Users/test/alpha build/ChihDesktop.app/Contents/MacOS/libAvaloniaNative.dylib'; do
    verify_macos_architecture "$rid" "$binary"
    check test "${#captured[@]}" = 3
    check test "${captured[0]}" = "$binary"
    check test "${captured[1]}" = -verify_arch
    check test "${captured[2]}" = "$expected"
  done
done

# Do not allow an unknown RID through to lipo or swallow a native check failure.
captured=()
if verify_macos_architecture linux-x64 '/unused path' 2>/dev/null; then fail 'Unknown RID accepted'; else status=$?; fi
check test "$status" = 2
check test "${#captured[@]}" = 0
lipo_status=7
if verify_macos_architecture osx-arm64 '/test path'; then fail 'lipo failure suppressed'; else status=$?; fi
check test "$status" = 7

# Emulate Apple's trailing architecture-list parsing to reproduce the old failure.
lipo() {
  [[ "$#" == 3 && "$1" != -verify_arch && "$2" == -verify_arch ]] || return 64
  case "$3" in arm64|x86_64) return 0;; *) return 64;; esac
}
if lipo -verify_arch arm64 '/test path'; then fail 'Old ordering accepted'; else status=$?; fi
check test "$status" = 64
check verify_macos_architecture osx-arm64 '/test path'
check verify_macos_architecture osx-x64 '/test path'
echo "PASS: $checks architecture mapping/argv/failure-propagation checks (mock lipo; native CI still required)."
