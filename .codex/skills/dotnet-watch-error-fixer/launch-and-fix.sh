#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

REPO_ROOT="${REPO_ROOT:-$(cd "${SCRIPT_DIR}/../../.." && pwd)}"
WATCH_PROJECT="${WATCH_PROJECT:-src/AgentsDashboard.ControlPlane}"
MONITOR_NORMAL_LOGS="${MONITOR_NORMAL_LOGS:-false}"
FIX_COOLDOWN_SECONDS="${FIX_COOLDOWN_SECONDS:-10}"
RESTART_DELAY_SECONDS="${RESTART_DELAY_SECONDS:-3}"
RESTART_MAX_DELAY_SECONDS="${RESTART_MAX_DELAY_SECONDS:-30}"
HEALTH_CHECK_ENABLED="${HEALTH_CHECK_ENABLED:-true}"
HEALTH_CHECK_URL="${HEALTH_CHECK_URL:-https://192.168.10.101:5266/health}"
HEALTH_CHECK_MAX_ATTEMPTS="${HEALTH_CHECK_MAX_ATTEMPTS:-10}"
HEALTH_CHECK_MIN_INTERVAL_SECONDS="${HEALTH_CHECK_MIN_INTERVAL_SECONDS:-5}"
HEALTH_CHECK_MAX_INTERVAL_SECONDS="${HEALTH_CHECK_MAX_INTERVAL_SECONDS:-10}"
HEALTH_CHECK_TIMEOUT_SECONDS="${HEALTH_CHECK_TIMEOUT_SECONDS:-5}"
HEALTH_CONTEXT_LINES="${HEALTH_CONTEXT_LINES:-120}"
HEALTH_FIX_COOLDOWN_SECONDS="${HEALTH_FIX_COOLDOWN_SECONDS:-90}"

LOG_DIR="${REPO_ROOT}/data/logs"
ERRORS_LOG_DIR="${REPO_ROOT}/data"
UNIFIED_ERROR_LOG="${UNIFIED_ERROR_LOG:-${LOG_DIR}/autofix-unified-errors.log}"
DOTNET_WATCH_STDOUT_LOG="${DOTNET_WATCH_STDOUT_LOG:-${LOG_DIR}/dotnet-watch.stdout.log}"
DOTNET_WATCH_STDERR_LOG="${DOTNET_WATCH_STDERR_LOG:-${LOG_DIR}/dotnet-watch.stderr.log}"
AUTOFIX_DISPATCH_LOG="${AUTOFIX_DISPATCH_LOG:-/tmp/logs/autofix-dispatch.log}"
AUTOFIX_STATE_FILE="${AUTOFIX_STATE_FILE:-${LOG_DIR}/autofix-state.log}"

mkdir -p "$LOG_DIR"
mkdir -p "$ERRORS_LOG_DIR"
mkdir -p "$(dirname "$AUTOFIX_DISPATCH_LOG")"

WATCH_LOG_FILE="${WATCH_LOG_FILE:-${ERRORS_LOG_DIR}/errors.log}"

append_log_file() {
  local file="$1"
  local already=false

  for existing in "${ERROR_LOGS[@]}"; do
    if [[ "$existing" == "$file" ]]; then
      already=true
      break
    fi
  done

  if [[ "$already" == "true" ]]; then
    return
  fi

  ERROR_LOGS+=("$file")
}

cleanup_log_file() {
  : > "$1"
}

shopt -s nullglob
for stale_log in "$LOG_DIR"/*.log "$LOG_DIR"/*.log.*; do
  rm -f "$stale_log"
done
shopt -u nullglob

: > "$UNIFIED_ERROR_LOG"
: > "$DOTNET_WATCH_STDOUT_LOG"
: > "$DOTNET_WATCH_STDERR_LOG"
: > "$AUTOFIX_DISPATCH_LOG"
: > "$AUTOFIX_STATE_FILE"

: > "$WATCH_LOG_FILE"
ERROR_LOGS=("$WATCH_LOG_FILE")
append_log_file "$DOTNET_WATCH_STDOUT_LOG"
append_log_file "$DOTNET_WATCH_STDERR_LOG"

if [[ "${MONITOR_NORMAL_LOGS,,}" == "true" ]]; then
  while IFS= read -r -d '' normal_log; do
    append_log_file "$normal_log"
  done < <(
    find "$LOG_DIR" -maxdepth 1 -type f \( -name '*.log' -o -name '*.log.*' \) -print0
  )
fi

log_event() {
  printf '%s | %s\n' "$(date -u +'%Y-%m-%dT%H:%M:%S.%3NZ')" "$1" >> "$AUTOFIX_DISPATCH_LOG"
}

is_fix_candidate_line() {
  local event_line="$1"
  local lower_event
  lower_event="${event_line,,}"

  case "$lower_event" in
    *"error"*|*"warn"*|*"warning"*|*"critical"*) return 0;;
    *"fail"*|*"failure"*) return 0;;
    *"exception"*) return 0;;
    *"build failed"*) return 0;;
    *"build failed."*) return 0;;
    *"unable to apply changes"*) return 0;;
    *"rude edit"*) return 0;;
    *"restart is needed"*) return 0;;
    *"cs1"*) return 0;;
    *"cs0"*) return 0;;
    *"rz"*) return 0;;
    *"not found"*) return 0;;
    *"fatal"*) return 0;;
    *"err"*) return 0;;
    *) return 1;;
  esac
}

cleanup_watchers() {
  local pid
  for pid in "${TAIL_PIDS[@]:-}"; do
    kill "$pid" 2>/dev/null || true
  done
}

terminate_existing_dotnet_watch() {
  local running_watch_pids
  local remaining_pids
  local kill_wait_count

  running_watch_pids="$(pgrep -f 'dotnet watch' || true)"
  if [[ -z "$running_watch_pids" ]]; then
    return
  fi

  log_event "Stopping existing dotnet watch processes before start."
  pkill -f 'dotnet watch' || true

  kill_wait_count=0
  while [[ $kill_wait_count -lt 10 ]]; do
    remaining_pids="$(pgrep -f 'dotnet watch' || true)"
    if [[ -z "$remaining_pids" ]]; then
      break
    fi
    sleep 1
    kill_wait_count=$((kill_wait_count + 1))
  done

  remaining_pids="$(pgrep -f 'dotnet watch' || true)"
  if [[ -n "$remaining_pids" ]]; then
    log_event "Forcing stop of remaining dotnet watch processes: ${remaining_pids//$'\n'/, }."
    pkill -9 -f 'dotnet watch' || true
  fi
}

stop_dotnet_watch_pid() {
  local watch_pid="$1"
  local kill_wait_count=0

  if [[ -z "$watch_pid" ]] || ! kill -0 "$watch_pid" 2>/dev/null; then
    return
  fi

  kill "$watch_pid" 2>/dev/null || true

  while [[ $kill_wait_count -lt 10 ]]; do
    if ! kill -0 "$watch_pid" 2>/dev/null; then
      return
    fi
    sleep 1
    kill_wait_count=$((kill_wait_count + 1))
  done

  if kill -0 "$watch_pid" 2>/dev/null; then
    kill -9 "$watch_pid" 2>/dev/null || true
  fi
}

run_health_probe() {
  local response
  LAST_HEALTH_CHECK_ERROR=""

  if [[ "${HEALTH_CHECK_ENABLED,,}" != "true" ]]; then
    return 1
  fi

  if [[ -z "$HEALTH_CHECK_URL" ]]; then
    LAST_HEALTH_CHECK_ERROR="HEALTH_CHECK_URL is not configured."
    return 1
  fi

  if ! response="$(curl --fail --max-time "$HEALTH_CHECK_TIMEOUT_SECONDS" -sk "$HEALTH_CHECK_URL" 2>&1)"; then
    LAST_HEALTH_CHECK_ERROR="${response//$'\n'/ }"
    return 1
  fi

  return 0
}

health_retry_interval_seconds() {
  local interval_range

  if [[ "$HEALTH_CHECK_MIN_INTERVAL_SECONDS" -gt "$HEALTH_CHECK_MAX_INTERVAL_SECONDS" ]]; then
    echo "$HEALTH_CHECK_MIN_INTERVAL_SECONDS"
    return
  fi

  interval_range=$((HEALTH_CHECK_MAX_INTERVAL_SECONDS - HEALTH_CHECK_MIN_INTERVAL_SECONDS + 1))
  if [[ $interval_range -le 1 ]]; then
    echo "$HEALTH_CHECK_MIN_INTERVAL_SECONDS"
    return
  fi

  echo $((HEALTH_CHECK_MIN_INTERVAL_SECONDS + (RANDOM % interval_range)))
}

build_health_context_file() {
  local failure_count="$1"
  local failure_reason="$2"
  local context_file
  local failure_ts
  local watch_pid="${CURRENT_DOTNET_WATCH_PID:-<not-running>}"

  context_file="$(mktemp "${LOG_DIR}/autofix-health-context.XXXXXX")"
  failure_ts="$(date -u +'%Y-%m-%dT%H:%M:%S.%3NZ')"

  {
    echo "Health-check recovery path engaged"
    echo "timestamp=${failure_ts}"
    echo "url=${HEALTH_CHECK_URL}"
    echo "max_attempts=${HEALTH_CHECK_MAX_ATTEMPTS}"
    echo "failure_count=${failure_count}"
    echo "failure_reason=${failure_reason}"
    echo "watch_project=${WATCH_PROJECT}"
    echo "watch_pid=${watch_pid}"
    echo "repo_root=${REPO_ROOT}"
    echo "watch_log_file=${WATCH_LOG_FILE}"
    echo "---"
    if [[ -f "$WATCH_LOG_FILE" ]]; then
      echo "--- recent ${WATCH_LOG_FILE}"
      tail -n "$HEALTH_CONTEXT_LINES" "$WATCH_LOG_FILE"
    fi
    if [[ -f "$UNIFIED_ERROR_LOG" ]]; then
      echo "--- recent ${UNIFIED_ERROR_LOG}"
      tail -n "$HEALTH_CONTEXT_LINES" "$UNIFIED_ERROR_LOG"
    fi
    if [[ -f "$DOTNET_WATCH_STDERR_LOG" ]]; then
      echo "--- recent ${DOTNET_WATCH_STDERR_LOG}"
      tail -n "$HEALTH_CONTEXT_LINES" "$DOTNET_WATCH_STDERR_LOG"
    fi
    if [[ -f "$DOTNET_WATCH_STDOUT_LOG" ]]; then
      echo "--- recent ${DOTNET_WATCH_STDOUT_LOG}"
      tail -n "$HEALTH_CONTEXT_LINES" "$DOTNET_WATCH_STDOUT_LOG"
    fi
    if [[ -f "$AUTOFIX_DISPATCH_LOG" ]]; then
      echo "--- recent ${AUTOFIX_DISPATCH_LOG}"
      tail -n "$HEALTH_CONTEXT_LINES" "$AUTOFIX_DISPATCH_LOG"
    fi
    echo "--- running dotnet processes"
    pgrep -af "dotnet" || true
  } > "$context_file"

  echo "$context_file"
}

dispatch_health_fix() {
  local fail_count="$1"
  local reason="$2"
  local context_file="$3"
  local event_ts

  event_ts="$(date -u +'%Y-%m-%dT%H:%M:%S.%3NZ')"

  "$SCRIPT_DIR/fix-dispatcher.sh" \
    --repo-root "$REPO_ROOT" \
    --source "$DOTNET_WATCH_STDERR_LOG" \
    --timestamp "$event_ts" \
    --line "Health check failed ${fail_count} consecutive attempts for ${HEALTH_CHECK_URL}. ${reason}" \
    --event-kind "health-check" \
    --context-file "$context_file" \
    --context-lines "$HEALTH_CONTEXT_LINES" \
    --cooldown "$HEALTH_FIX_COOLDOWN_SECONDS" \
    --dispatch-log "$AUTOFIX_DISPATCH_LOG" \
    --unified-log "$UNIFIED_ERROR_LOG" \
    --state-file "$AUTOFIX_STATE_FILE"
}

trap cleanup_watchers EXIT INT TERM

watch_file() {
  local source_file="$1"
  local short_source
  short_source="$(basename "$source_file")"

  while true; do
    while IFS= read -r line; do
      [[ -z "$line" ]] && continue
      local event_ts
      local payload

      event_ts="$(date -u +'%Y-%m-%dT%H:%M:%S.%3NZ')"
      payload="${line//$'\r'/}"
      printf '%s | %s | %s\n' "$event_ts" "$short_source" "$payload" >> "$UNIFIED_ERROR_LOG"
      if ! is_fix_candidate_line "$payload"; then
        continue
      fi

      "$SCRIPT_DIR/fix-dispatcher.sh" \
        --repo-root "$REPO_ROOT" \
        --source "$source_file" \
        --timestamp "$event_ts" \
        --line "$payload" \
        --event-kind "runtime-log" \
        --cooldown "$FIX_COOLDOWN_SECONDS" \
        --dispatch-log "$AUTOFIX_DISPATCH_LOG" \
        --unified-log "$UNIFIED_ERROR_LOG" \
        --state-file "$AUTOFIX_STATE_FILE" &
    done < <(tail -n 0 -F -- "$source_file")

    sleep 1
  done
}

dotnet_watch_requested_restart() {
  local start_line
  local stdout_tail

  start_line="$1"
  stdout_tail="$(sed -n "$((start_line + 1)),$ p" "$DOTNET_WATCH_STDOUT_LOG")"
  [[ "$stdout_tail" == *"Restart is needed to apply the changes."* ]]
}

LAST_HEALTH_CHECK_ERROR=""

if [[ ! "$HEALTH_CHECK_MIN_INTERVAL_SECONDS" =~ ^[0-9]+$ ]]; then
  HEALTH_CHECK_MIN_INTERVAL_SECONDS=5
fi
if [[ ! "$HEALTH_CHECK_MAX_INTERVAL_SECONDS" =~ ^[0-9]+$ ]]; then
  HEALTH_CHECK_MAX_INTERVAL_SECONDS=10
fi
if [[ ! "$HEALTH_CHECK_MAX_ATTEMPTS" =~ ^[0-9]+$ ]]; then
  HEALTH_CHECK_MAX_ATTEMPTS=10
fi
if [[ ! "$HEALTH_CHECK_TIMEOUT_SECONDS" =~ ^[0-9]+$ ]]; then
  HEALTH_CHECK_TIMEOUT_SECONDS=5
fi

CURRENT_DOTNET_WATCH_PID=""

declare -a TAIL_PIDS=()
for log_file in "${ERROR_LOGS[@]}"; do
  watch_file "$log_file" &
  TAIL_PIDS+=("$!")
done

log_event "Starting dotnet watch for ${WATCH_PROJECT}"
log_event "Monitoring log files: ${ERROR_LOGS[*]}"

attempt=0
while true; do
  terminate_existing_dotnet_watch

  pre_stdout_lines="$(wc -l < "$DOTNET_WATCH_STDOUT_LOG")"
  watch_exit=0
  restart_requested=false
  health_probe_failed=false

  DOTNET_WATCH_RESTART_ON_RUDE_EDIT=1 \
    DOTNET_WATCH_SUPPRESS_EMOJIS=1 \
    DOTNET_USE_POLLING_FILE_WATCHER=1 \
    DOTNET_WATCH_AUTO_RELOAD_WS_HOSTNAME=192.168.10.101 \
    ASPNETCORE_ENVIRONMENT=Development \
    DOTNET_WATCH_NONINTERACTIVE=1 \
    dotnet watch --non-interactive --project "$WATCH_PROJECT" \
      >> "$DOTNET_WATCH_STDOUT_LOG" \
      2>> "$DOTNET_WATCH_STDERR_LOG" &
  CURRENT_DOTNET_WATCH_PID="$!"

  log_event "dotnet watch started with pid ${CURRENT_DOTNET_WATCH_PID}"

  if [[ "${HEALTH_CHECK_ENABLED,,}" == "true" ]]; then
    health_fail_count=0
    while kill -0 "$CURRENT_DOTNET_WATCH_PID" 2>/dev/null; do
      if ! run_health_probe; then
        health_fail_count=$((health_fail_count + 1))
        log_event "Health probe failed (${health_fail_count}/${HEALTH_CHECK_MAX_ATTEMPTS}) for ${HEALTH_CHECK_URL}: ${LAST_HEALTH_CHECK_ERROR:-unknown}"
        if [[ "$health_fail_count" -ge "$HEALTH_CHECK_MAX_ATTEMPTS" ]]; then
          health_probe_failed=true
          break
        fi
      else
        if [[ "$health_fail_count" -gt 0 ]]; then
          log_event "Health probe recovered after ${health_fail_count} failed checks."
        fi
        health_fail_count=0
      fi

      sleep "$(health_retry_interval_seconds)"
    done
  fi

  if [[ "$health_probe_failed" == "true" ]]; then
    log_event "Health checks exhausted after ${HEALTH_CHECK_MAX_ATTEMPTS} attempts; launching Codex fix attempt."
    HEALTH_CONTEXT_FILE="$(build_health_context_file "$health_fail_count" "$LAST_HEALTH_CHECK_ERROR")"
    dispatch_health_fix "$health_fail_count" "$LAST_HEALTH_CHECK_ERROR" "$HEALTH_CONTEXT_FILE"
    rm -f "$HEALTH_CONTEXT_FILE"

    stop_dotnet_watch_pid "$CURRENT_DOTNET_WATCH_PID"
    if wait "$CURRENT_DOTNET_WATCH_PID"; then
      watch_exit=0
    else
      watch_exit=$?
    fi
  else
    if wait "$CURRENT_DOTNET_WATCH_PID"; then
      watch_exit=0
    else
      watch_exit=$?
    fi
  fi
  CURRENT_DOTNET_WATCH_PID=""

  if [[ "$watch_exit" -eq 0 ]] && dotnet_watch_requested_restart "$pre_stdout_lines"; then
    restart_requested=true
    log_event "dotnet watch requested restart due to reload boundary changes."
  fi

  if [[ "$watch_exit" -eq 0 && "$restart_requested" != "true" ]]; then
    log_event "dotnet watch exited cleanly. Stopping orchestrator."
    break
  fi

  if [[ "$watch_exit" -eq 130 || "$watch_exit" -eq 143 ]]; then
    if [[ "$restart_requested" == "true" ]]; then
      log_event "dotnet watch requested restart (exit ${watch_exit})."
    else
    log_event "dotnet watch terminated by request (code ${watch_exit})."
    break
    fi
  fi

  attempt=$((attempt + 1))
  delay=$((RESTART_DELAY_SECONDS * 2 ** (attempt - 1)))
  if ((delay > RESTART_MAX_DELAY_SECONDS)); then
    delay=$RESTART_MAX_DELAY_SECONDS
  fi

  log_event "dotnet watch exited with code ${watch_exit}. Restarting in ${delay}s."
  sleep "$delay"
done
