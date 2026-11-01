#!/bin/bash
# otool -L mixes LC_ID_DYLIB metadata with dependencies. Classify otool -l instead.
native_dependency_records() {
  awk '
    function finish() {
      if (wanted && !value) { print "Missing name/path for " command > "/dev/stderr"; bad=1 }
    }
    $1 == "cmd" {
      finish(); command=$2; value=0; commands++
      wanted=(command ~ /^LC_(ID_DYLIB|LOAD_DYLIB|LOAD_WEAK_DYLIB|REEXPORT_DYLIB|LOAD_UPWARD_DYLIB|LAZY_LOAD_DYLIB|RPATH)$/)
    }
    wanted && (($1 == "name" && command != "LC_RPATH") || ($1 == "path" && command == "LC_RPATH")) {
      path=$0
      sub(/^[[:space:]]*(name|path)[[:space:]]+/, "", path)
      if (path !~ /[[:space:]]+\(offset [0-9]+\)[[:space:]]*$/ || value) { bad=1; next }
      sub(/[[:space:]]+\(offset [0-9]+\)[[:space:]]*$/, "", path)
      if (!length(path)) { bad=1; next }
      print command "\t" path; value=1
    }
    END { finish(); if (!commands || bad) exit 1 }
  ' "${1:?otool -l dump required}"
}

native_system_path() {
  # Do not allow traversal to turn an allowed system prefix into /usr/local etc.
  case "$1" in */../*|*/..|*/./*) return 1;; esac
  case "$1" in /System/Library/*|/usr/lib/*) return 0;; *) return 1;; esac
}

native_expand_path() {
  case "$1" in
    @loader_path) dirname "$2";;
    @loader_path/*) printf '%s/%s\n' "$(dirname "$2")" "${1#@loader_path/}";;
    @executable_path) dirname "$3";;
    @executable_path/*) printf '%s/%s\n' "$(dirname "$3")" "${1#@executable_path/}";;
    /*) printf '%s\n' "$1";;
    *) return 1;;
  esac
}

native_bundled_file() {
  local candidate="$1" root="$2" parent
  [[ -f "$candidate" && ! -L "$candidate" ]] || return 1
  parent="$(cd "$(dirname "$candidate")" && pwd -P)" || return 1
  case "$parent/$(basename "$candidate")" in "$root"/*) return 0;; *) return 1;; esac
}

validate_native_dependencies() {
  local binary="$1" executable="$2" bundle="$3" records="$4" executable_records="$5"
  local command dependency candidate runpath owner root directory record_file index
  root="$(cd "$bundle" && pwd -P)" || return 1
  # Only the image and the main executable supply run paths in this flat payload.
  # Never guess @rpath from a matching filename or another unrelated dylib.
  while IFS=$'\t' read -r command dependency; do
    case "$command" in
      LC_ID_DYLIB) continue;; # Metadata, not a request to load this path.
      LC_RPATH)
        candidate="$(native_expand_path "$dependency" "$binary" "$executable")" || {
          echo "Unsupported LC_RPATH in $binary: $dependency" >&2; return 1;
        }
        if native_system_path "${candidate%/}/"; then continue; fi
        directory="$(cd "$candidate" && pwd -P)" || { echo "Missing LC_RPATH directory in $binary: $dependency" >&2; return 1; }
        case "$directory" in "$root"|"$root"/*) ;; *) echo "External LC_RPATH in $binary: $dependency" >&2; return 1;; esac
        continue;;
      LC_LOAD_DYLIB|LC_LOAD_WEAK_DYLIB|LC_REEXPORT_DYLIB|LC_LOAD_UPWARD_DYLIB|LC_LAZY_LOAD_DYLIB) ;;
      *) echo "Unexpected dependency record in $binary: $command" >&2; return 1;;
    esac
    if native_system_path "$dependency"; then continue; fi
    case "$dependency" in
      @loader_path/*|@executable_path/*)
        candidate="$(native_expand_path "$dependency" "$binary" "$executable")" || return 1
        native_bundled_file "$candidate" "$root" || { echo "Missing/outside-bundle $command in $binary: $dependency" >&2; return 1; };;
      @rpath/*)
        candidate=''
        for index in 0 1; do
          if [[ "$index" == 0 ]]; then record_file="$records"; owner="$binary"; else record_file="$executable_records"; owner="$executable"; fi
          while IFS=$'\t' read -r command runpath; do
            [[ "$command" == LC_RPATH ]] || continue
            directory="$(native_expand_path "$runpath" "$owner" "$executable")" || return 1
            if native_system_path "$directory/${dependency#@rpath/}" || native_bundled_file "$directory/${dependency#@rpath/}" "$root"; then
              candidate="$directory/${dependency#@rpath/}"; break
            fi
          done < "$record_file"
          [[ -z "$candidate" ]] || break
        done
        [[ -n "$candidate" ]] || { echo "Unresolved @rpath in $binary: $dependency (no declared run path resolves inside bundle/system)" >&2; return 1; };;
      *) echo "Unexpected native load path in $binary ($command): $dependency" >&2; return 1;;
    esac
  done < "$records"
}
