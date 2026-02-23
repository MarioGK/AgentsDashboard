#!/usr/bin/env bash
set -euo pipefail

REPO_ROOT=""
SOURCE_FILE=""
EVENT_TS=""
ERROR_LINE=""
FIX_COOLDOWN_SECONDS="${FIX_COOLDOWN_SECONDS:-10}"
EVENT_KIND="runtime-log"
CONTEXT_FILE=""
CONTEXT_LINES="${AUTOFIX_CONTEXT_LINES:-120}"
UNIFIED_ERROR_LOG=""
AUTOFIX_DISPATCH_LOG=""
AUTOFIX_STATE_FILE=""
CODEX_CLI="${CODEX_CLI:-codex}"
CODEX_CLI_ARGS="${CODEX_CLI_ARGS:-}"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --repo-root)
      REPO_ROOT="$2"
      shift 2
      ;;
    --source)
      SOURCE_FILE="$2"
      shift 2
      ;;
    --timestamp)
      EVENT_TS="$2"
      shift 2
      ;;
    --line)
      ERROR_LINE="$2"
      shift 2
      ;;
    --cooldown)
      FIX_COOLDOWN_SECONDS="$2"
      shift 2
      ;;
    --unified-log)
      UNIFIED_ERROR_LOG="$2"
      shift 2
      ;;
    --dispatch-log)
      AUTOFIX_DISPATCH_LOG="$2"
      shift 2
      ;;
    --state-file)
      AUTOFIX_STATE_FILE="$2"
      shift 2
      ;;
    --event-kind)
      EVENT_KIND="$2"
      shift 2
      ;;
    --context-file)
      CONTEXT_FILE="$2"
      shift 2
      ;;
    --context-lines)
      CONTEXT_LINES="$2"
      shift 2
      ;;
    --cli-args)
      CODEX_CLI_ARGS="$2"
      shift 2
      ;;
    *)
      shift
      ;;
  esac
done

if [[ -z "$REPO_ROOT" || -z "$SOURCE_FILE" || -z "$EVENT_TS" || -z "$ERROR_LINE" ]]; then
  echo "Usage: fix-dispatcher.sh --repo-root <path> --source <file> --timestamp <ts> --line <line>" >&2
  exit 2
fi

if ! [[ "$CONTEXT_LINES" =~ ^[0-9]+$ ]]; then
  CONTEXT_LINES=120
fi

LOG_DIR="$REPO_ROOT/data/logs"
UNIFIED_ERROR_LOG="${UNIFIED_ERROR_LOG:-${LOG_DIR}/autofix-unified-errors.log}"
AUTOFIX_DISPATCH_LOG="${AUTOFIX_DISPATCH_LOG:-/tmp/logs/autofix-dispatch.log}"
AUTOFIX_STATE_FILE="${AUTOFIX_STATE_FILE:-${LOG_DIR}/autofix-state.log}"

mkdir -p "$LOG_DIR"
mkdir -p "$(dirname "$AUTOFIX_DISPATCH_LOG")"
touch "$AUTOFIX_DISPATCH_LOG"
touch "$AUTOFIX_STATE_FILE"

if command -v sha256sum >/dev/null 2>&1; then
  ERROR_HASH="$(printf '%s' "$ERROR_LINE" | sha256sum | awk '{print $1}')"
else
  ERROR_HASH="$(printf '%s' "$ERROR_LINE" | cksum | awk '{print $1}')"
fi
ERROR_KEY="${EVENT_KIND}|${SOURCE_FILE}|${ERROR_HASH}"
CODEx_EXIT_CODE=0
FIX_OUTPUT=""
CONTEXT_SNIPPET="  <no context data>"

if [[ -n "$CONTEXT_FILE" && -f "$CONTEXT_FILE" ]]; then
  CONTEXT_SNIPPET="$(tail -n "$CONTEXT_LINES" "$CONTEXT_FILE" 2>/dev/null | sed 's/\r//g' | sed 's/^/  /' || true)"
fi

if [[ -z "$CONTEXT_SNIPPET" ]]; then
  CONTEXT_SNIPPET="  <no context data>"
fi

log_dispatch() {
  local status="$1"
  local output="$2"
  local output_line="${output//$'\n'/ }"

  printf '%s | source=%s | kind=%s | status=%s | line_hash=%s | exit=%s | output=%s\n' \
    "$(date -u +'%Y-%m-%dT%H:%M:%S.%3NZ')" \
    "$SOURCE_FILE" \
    "$EVENT_KIND" \
    "$status" \
    "$ERROR_HASH" \
    "$CODEx_EXIT_CODE" \
    "$output_line" >> "$AUTOFIX_DISPATCH_LOG"
}

if ! command -v "$CODEX_CLI" >/dev/null 2>&1; then
  log_dispatch "skipped" "codex binary not found"
  exit 0
fi

if command -v flock >/dev/null 2>&1; then
  LOCK_FILE="${LOG_DIR}/.autofix-fix.lock"
  exec 9>"$LOCK_FILE"
  if ! flock -n 9; then
    exit 0
  fi
  CLEANUP_LOCK_METHOD="flock"
else
  LOCK_DIR="${LOG_DIR}/.autofix-fix.lock"
  if ! mkdir "$LOCK_DIR" 2>/dev/null; then
    exit 0
  fi
  CLEANUP_LOCK_METHOD="mkdir"
fi

cleanup_lock() {
  if [[ "$CLEANUP_LOCK_METHOD" == "mkdir" ]]; then
    rmdir "$LOCK_DIR" 2>/dev/null || true
  fi
}
trap cleanup_lock EXIT

now_ts="$(date +%s)"
cutoff_ts=$((now_ts - FIX_COOLDOWN_SECONDS))

if [[ -f "$AUTOFIX_STATE_FILE" ]]; then
  previous_ts="$(awk -F'|' -v key="$ERROR_KEY" '$1==key {print $2; exit}' "$AUTOFIX_STATE_FILE" 2>/dev/null || true)"
  if [[ "$previous_ts" =~ ^[0-9]+$ && $((now_ts - previous_ts)) -le "$FIX_COOLDOWN_SECONDS" ]]; then
    CODEx_EXIT_CODE=0
    log_dispatch "skipped" "dedupe window active"
    exit 0
  fi
fi

awk -F'|' -v cutoff="$cutoff_ts" 'NF==2 && $2>=cutoff {print}' "$AUTOFIX_STATE_FILE" > "${AUTOFIX_STATE_FILE}.tmp" || true
mv "${AUTOFIX_STATE_FILE}.tmp" "$AUTOFIX_STATE_FILE"
echo "${ERROR_KEY}|${now_ts}" >> "$AUTOFIX_STATE_FILE"

FIX_PROMPT="You are operating on repository: ${REPO_ROOT}.

A ${EVENT_KIND} event was observed in this repository.

Timestamp: ${EVENT_TS}
Source file: ${SOURCE_FILE}

Log line:
${ERROR_LINE}

Recent context:
${CONTEXT_SNIPPET}

Make the smallest safe code change in this repository to address this event and keep behavior stable.
Run the most relevant validation command if possible.
Use only minimal, low-risk edits."

run_codex_attempt() {
  local -a args=("$@")
  local output_file
  local output

  output_file="$(mktemp "${LOG_DIR}/.autofix-codex-output.XXXXXX")"
  if command -v timeout >/dev/null 2>&1; then
    timeout 900 "$CODEX_CLI" "${args[@]}" "$FIX_PROMPT" > "$output_file" 2>&1
    CODEx_EXIT_CODE=$?
  else
    "$CODEX_CLI" "${args[@]}" "$FIX_PROMPT" > "$output_file" 2>&1
    CODEx_EXIT_CODE=$?
  fi

  output="$(cat "$output_file")"
  FIX_OUTPUT="${output//$'\n'/ }"
  rm -f "$output_file"
}

base_attempt_args=(
  "--yolo"
  "exec"
  "--model"
  "gpt-5.3-codex-spark"
  "-c"
  "reasoning_effort=\"xhigh\""
)

if [[ -n "$CODEX_CLI_ARGS" ]]; then
  read -r -a EXTRA_ARGS <<< "$CODEX_CLI_ARGS"
  base_attempt_args+=("${EXTRA_ARGS[@]}")
fi

run_codex_attempt "${base_attempt_args[@]}"
if [[ "$CODEx_EXIT_CODE" -eq 0 ]]; then
  log_dispatch "dispatched" "success: codex ${CODEX_CLI} executed"
  exit 0
fi

log_dispatch "failed" "${FIX_OUTPUT}"
exit 0
