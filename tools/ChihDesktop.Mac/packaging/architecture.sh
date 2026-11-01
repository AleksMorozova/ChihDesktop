#!/bin/bash
# Shared by production validation and argument-level regression tests.
mac_arch_for_rid() {
  case "${1-}" in
    osx-arm64) printf '%s\n' arm64;;
    osx-x64) printf '%s\n' x86_64;;
    *) echo 'Unsupported macOS RID' >&2; return 2;;
  esac
}

verify_macos_architecture() {
  local target_arch
  target_arch="$(mac_arch_for_rid "${1-}")" || return $?
  # Apple's lipo consumes ALL arguments after -verify_arch as architectures.
  # Keep the input before that option, quoted as one argument even with spaces.
  lipo "${2:?Binary path required}" -verify_arch "$target_arch"
}
