#!/bin/bash
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd -P)"
source "$here/native-dependencies.sh"
temp_base="$(cd "${TMPDIR:-/tmp}" && pwd -P)"
test_root="$(mktemp -d "$temp_base/chih-native-tests.XXXXXX")"
cleanup() { case "$test_root" in "$temp_base"/chih-native-tests.*) rm -rf -- "$test_root";; *) exit 1;; esac; }
trap cleanup EXIT
app="$test_root/alpha with spaces/ChihDesktop.app"
mac="$app/Contents/MacOS"
mkdir -p "$mac" "$app/Contents/Frameworks"
exe="$mac/ChihDesktop"; binary="$mac/libAvaloniaNative.dylib"
touch "$exe" "$binary" "$mac/libmscordaccore.dylib" "$app/Contents/Frameworks/bundled.dylib" "$test_root/outside.dylib"
checks=0
fail() { echo "FAIL: $*" >&2; exit 1; }
pass() { "$@" || fail "$*"; checks=$((checks + 1)); }
reject() { if "$@" > "$test_root/rejected.txt" 2>&1; then fail "Accepted: $*"; fi; checks=$((checks + 1)); }
dump() {
  local index=0
  while [[ "$#" -gt 0 ]]; do
    printf 'Load command %s\n          cmd %s\n      cmdsize 128\n' "$index" "$1"
    if [[ "$1" == LC_RPATH ]]; then printf '         path %s (offset 12)\n' "$2"; else printf '         name %s (offset 24)\n' "$2"; fi
    shift 2; index=$((index + 1))
  done
}
dump LC_LOAD_DYLIB /usr/lib/libSystem.B.dylib > "$test_root/exe.dump"
native_dependency_records "$test_root/exe.dump" > "$test_root/exe.records"
validate() { validate_native_dependencies "$binary" "$exe" "$app" "$test_root/library.records" "$test_root/exe.records"; }
parse() { native_dependency_records "$test_root/library.dump" > "$test_root/library.records"; }

# Reproduce the real Avalonia ID plus system dependencies. No /usr/local allowlist.
dump LC_ID_DYLIB /usr/local/lib/libAvalonia.Native.OSX.dylib LC_LOAD_DYLIB /usr/lib/libSystem.B.dylib LC_LOAD_WEAK_DYLIB /System/Library/Frameworks/AppKit.framework/Versions/C/AppKit > "$test_root/library.dump"
pass parse
pass validate
pass grep -F $'LC_ID_DYLIB\t/usr/local/lib/libAvalonia.Native.OSX.dylib' "$test_root/library.records"
for command in LC_LOAD_DYLIB LC_LOAD_WEAK_DYLIB LC_REEXPORT_DYLIB LC_LOAD_UPWARD_DYLIB LC_LAZY_LOAD_DYLIB; do
  dump LC_ID_DYLIB /usr/local/lib/libAvalonia.Native.OSX.dylib "$command" /usr/local/lib/libAvalonia.Native.OSX.dylib > "$test_root/library.dump"
  pass parse
  reject validate
done

# Real .NET diagnostic library pattern: its own declared run path resolves a sibling.
dump LC_RPATH @loader_path LC_LOAD_DYLIB @rpath/libmscordaccore.dylib > "$test_root/library.dump"
pass parse
pass validate
# A sibling exists, but no LC_RPATH: fail instead of guessing a search directory.
dump LC_LOAD_DYLIB @rpath/libmscordaccore.dylib > "$test_root/library.dump"
pass parse
reject validate
dump LC_RPATH @loader_path LC_LOAD_DYLIB @rpath/missing.dylib > "$test_root/library.dump"
pass parse
reject validate
# System run paths may refer to libraries supplied only through the dyld cache.
dump LC_RPATH /usr/lib LC_LOAD_DYLIB @rpath/libSystem.B.dylib > "$test_root/library.dump"
pass parse
pass validate
# Inherited main-executable run path, then loader and executable-relative framework paths.
dump LC_RPATH @executable_path/../Frameworks LC_LOAD_DYLIB /usr/lib/libSystem.B.dylib > "$test_root/exe.dump"
native_dependency_records "$test_root/exe.dump" > "$test_root/exe.records"
dump LC_LOAD_DYLIB @rpath/bundled.dylib > "$test_root/library.dump"
pass parse
pass validate
for path in @loader_path/../Frameworks/bundled.dylib @executable_path/../Frameworks/bundled.dylib; do
  dump LC_LOAD_DYLIB "$path" > "$test_root/library.dump"
  pass parse
  pass validate
done
for path in @loader_path/missing.dylib @executable_path/missing.dylib @loader_path/../../../../outside.dylib /usr/lib/../local/lib/bad.dylib; do
  dump LC_LOAD_DYLIB "$path" > "$test_root/library.dump"
  pass parse
  reject validate
done
dump LC_RPATH "$test_root" LC_LOAD_DYLIB /usr/lib/libSystem.B.dylib > "$test_root/library.dump"
pass parse
reject validate
# A real file reachable through an outside-bundle directory symlink is not bundled.
if [[ "$(uname -s)" == Darwin ]]; then
  ln -s "$test_root" "$app/Contents/outside"
  dump LC_LOAD_DYLIB @loader_path/../outside/outside.dylib > "$test_root/library.dump"
  pass parse
  reject validate
else
  echo 'NOT RUN: native macOS directory-symlink check (Git Bash may emulate symlinks as copies).'
fi
printf 'Load command 0\n cmd LC_LOAD_DYLIB\n name /usr/local/lib/bad.dylib\n' > "$test_root/library.dump"
reject parse
printf '' > "$test_root/library.dump"
reject parse
echo "PASS: $checks native load-command/parser/resolution checks (synthetic otool fixtures; native CI still required)."
