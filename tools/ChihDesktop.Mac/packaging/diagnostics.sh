#!/bin/bash
# Opt-in stage/line/status diagnostics. Never print argv, environment or set -x.
alpha_stage() { ALPHA_STAGE="$1"; if [[ "${CHIH_ALPHA_TRACE:-0}" == 1 ]]; then printf 'STAGE: %s\n' "$ALPHA_STAGE"; fi; return 0; }
alpha_report_error() {
  local status="$1" script="$2" line="$3"
  printf 'FAIL: stage=%s script=%s line=%s exit=%s\n' "${ALPHA_STAGE:-startup}" "$script" "$line" "$status" >&2
  return "$status"
}
alpha_report_exit() {
  local status="$1"
  if [[ "$status" != 0 ]]; then printf 'EXIT: stage=%s exit=%s\n' "${ALPHA_STAGE:-startup}" "$status" >&2; fi
  return "$status"
}
alpha_enable_diagnostics() {
  if [[ "${CHIH_ALPHA_TRACE:-0}" == 1 ]]; then
    set -E
    trap 'alpha_report_error "$?" "${BASH_SOURCE[0]##*/}" "$LINENO"' ERR
    trap 'alpha_report_exit "$?"' EXIT
  fi
  return 0
}
