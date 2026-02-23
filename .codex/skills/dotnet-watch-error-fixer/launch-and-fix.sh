#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

REPO_ROOT="${REPO_ROOT:-$(cd "${SCRIPT_DIR}/../../.." && pwd)}"
WATCH_PROJECT="${WATCH_PROJECT:-src/AgentsDashboard.ControlPlane}"
AUTOFIX_LOG_DIR="${AUTOFIX_LOG_DIR:-/tmp/autofix/logs}"
WATCH_LOG_FILE="${WATCH_LOG_FILE:-${AUTOFIX_LOG_DIR}/errors.log}"
MONITOR_NORMAL_LOGS="${MONITOR_NORMAL_LOGS:-false}"

LOG_DIR="${LOG_DIR:-${AUTOFIX_LOG_DIR}}"
ERRORS_LOG_DIR="${ERRORS_LOG_DIR:-${AUTOFIX_LOG_DIR}}"
UNIFIED_ERROR_LOG="${UNIFIED_ERROR_LOG:-${LOG_DIR}/autofix-unified-errors.log}"
DOTNET_WATCH_STDOUT_LOG="${DOTNET_WATCH_STDOUT_LOG:-${LOG_DIR}/dotnet-watch.stdout.log}"
DOTNET_WATCH_STDERR_LOG="${DOTNET_WATCH_STDERR_LOG:-${LOG_DIR}/dotnet-watch.stderr.log}"
AUTOFIX_DISPATCH_LOG="${AUTOFIX_DISPATCH_LOG:-${LOG_DIR}/autofix-dispatch.log}"
AUTOFIX_STATE_FILE="${AUTOFIX_STATE_FILE:-${LOG_DIR}/autofix-state.log}"

AUTOFIX_LOCK_DIR="${AUTOFIX_LOCK_DIR:-/tmp/autofix}"
AUTOFIX_LOCK_FILE="${AUTOFIX_LOCK_FILE:-${AUTOFIX_LOCK_DIR}/launcher.lock}"

FIX_COOLDOWN_SECONDS="${FIX_COOLDOWN_SECONDS:-10}"
COMPILE_COOLDOWN_SECONDS="${COMPILE_COOLDOWN_SECONDS:-30}"
STARTUP_COOLDOWN_SECONDS="${STARTUP_COOLDOWN_SECONDS:-25}"
RUNTIME_COOLDOWN_SECONDS="${RUNTIME_COOLDOWN_SECONDS:-10}"
RESTART_DELAY_SECONDS="${RESTART_DELAY_SECONDS:-3}"
RESTART_MAX_DELAY_SECONDS="${RESTART_MAX_DELAY_SECONDS:-30}"

PRESTART_CLEANUP_PORTS="${PRESTART_CLEANUP_PORTS:-5266,5267,5268}"
PRESTART_CLEANUP_TIMEOUT_SECONDS="${PRESTART_CLEANUP_TIMEOUT_SECONDS:-10}"
PRESTART_CLEANUP_DOTNET_WATCH="${PRESTART_CLEANUP_DOTNET_WATCH:-true}"

HEALTH_CHECK_ENABLED="${HEALTH_CHECK_ENABLED:-true}"
HEALTH_CHECK_URL="${HEALTH_CHECK_URL:-https://localhost:5266/health}"
HEALTH_CHECK_MAX_ATTEMPTS="${HEALTH_CHECK_MAX_ATTEMPTS:-10}"
HEALTH_CHECK_MIN_INTERVAL_SECONDS="${HEALTH_CHECK_MIN_INTERVAL_SECONDS:-5}"
HEALTH_CHECK_MAX_INTERVAL_SECONDS="${HEALTH_CHECK_MAX_INTERVAL_SECONDS:-10}"
HEALTH_CHECK_TIMEOUT_SECONDS="${HEALTH_CHECK_TIMEOUT_SECONDS:-5}"
HEALTH_CONTEXT_LINES="${HEALTH_CONTEXT_LINES:-120}"
HEALTH_FIX_COOLDOWN_SECONDS="${HEALTH_FIX_COOLDOWN_SECONDS:-90}"

mkdir -p "$LOG_DIR"
mkdir -p "$ERRORS_LOG_DIR"
mkdir -p "$(dirname "$AUTOFIX_DISPATCH_LOG")"
mkdir -p "$AUTOFIX_LOCK_DIR"

AUTOFIX_LOCK_ACQUIRED=0
declare -a TAIL_PIDS=()
CURRENT_DOTNET_WATCH_PID=""
LAST_HEALTH_CHECK_ERROR=""
declare -A DISPATCH_STORM_GATES=()

declare -g FAILURE_TAXONOMY=""
declare -g FAILURE_SEVERITY=""

ERROR_LOGS=()

append_log_file() {
  local file="$1"
  local existing
  local seen=false

  for existing in "${ERROR_LOGS[@]}"; do
    if [[ "$existing" == "$file" ]]; then
      seen=true
      break
    fi
  done

  if [[ "$seen" == "true" ]]; then
    return
  fi

  ERROR_LOGS+=("$file")
}

log_event() {
  printf '%s | %s\n' "$(date -u +'%Y-%m-%dT%H:%M:%S.%3NZ')" "$1" >> "$AUTOFIX_DISPATCH_LOG"
}

acquire_launcher_lock() {
  local existing_pid

  exec 9>"$AUTOFIX_LOCK_FILE"
  if ! flock -n 9; then
    existing_pid="$(awk -F'=' '/^pid=/{print $2; exit}' "$AUTOFIX_LOCK_FILE" 2>/dev/null || true)"
    if [[ -n "$existing_pid" ]] && kill -0 "$existing_pid" 2>/dev/null; then
      echo "launch aborted: another dotnet-watch-error-fixer is already running (pid=${existing_pid})" >&2
      exit 1
    fi

    log_event "Stale lock detected on ${AUTOFIX_LOCK_FILE}; retrying lock acquisition."
    flock -u 9 || true
    exec 9>"$AUTOFIX_LOCK_FILE"
    if ! flock -n 9; then
      echo "launch aborted: cannot obtain launcher lock" >&2
      exit 1
    fi
  fi

  {
    printf 'pid=%s\n' "$$"
    printf 'ppid=%s\n' "$PPID"
    printf 'started_at=%s\n' "$(date -u +'%Y-%m-%dT%H:%M:%S.%3NZ')"
    printf 'script=%s\n' "$SCRIPT_DIR/launch-and-fix.sh"
  } > "$AUTOFIX_LOCK_FILE"
  AUTOFIX_LOCK_ACQUIRED=1
  trap release_launcher_lock EXIT INT TERM
}

release_launcher_lock() {
  if [[ "$AUTOFIX_LOCK_ACQUIRED" != "1" ]]; then
    return
  fi

  flock -u 9 || true
  exec 9>&- || true
  rm -f "$AUTOFIX_LOCK_FILE"
  AUTOFIX_LOCK_ACQUIRED=0
}

cleanup_watchers() {
  local pid
  for pid in "${TAIL_PIDS[@]:-}"; do
    kill "$pid" 2>/dev/null || true
  done
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
    *"build failed:"*) return 0;;
    *"unable to apply changes"*) return 0;;
    *"rude edit"*) return 0;;
    *"restart is needed"*) return 0;;
    *"address already in use"*) return 0;;
    *"fatal"*) return 0;;
    *"err"*) return 0;;
    *) return 1;;
  esac
}

normalize_ports() {
  local raw_port
  local -a configured_ports=()

  IFS=',' read -ra configured_ports <<< "$PRESTART_CLEANUP_PORTS"
  for raw_port in "${configured_ports[@]}"; do
    raw_port="${raw_port//[[:space:]]/}"
    if [[ -n "$raw_port" ]] && [[ "$raw_port" =~ ^[0-9]+$ ]]; then
      printf '%s\n' "$raw_port"
    fi
  done
}

find_listener_pids() {
  local port="$1"
  local pids=""

  if command -v lsof >/dev/null 2>&1; then
    pids="$(lsof -nP -iTCP:"$port" -sTCP:LISTEN -t 2>/dev/null || true)"
  elif command -v ss >/dev/null 2>&1; then
    pids="$(ss -H -ltnp "sport = :$port" 2>/dev/null | awk '
      /pid=/ {
        match($0, /pid=([0-9]+)/, m)
        if (m[1] != "") {
          print m[1]
        }
      }')"
  fi

  if [[ -z "$pids" ]]; then
    return
  fi

  printf '%s\n' "$pids" | awk 'NF>0' | sort -u
}

terminate_pid_with_wait() {
  local target_pid="$1"
  local label="$2"
  local wait_count=0
  local max_wait="${PRESTART_CLEANUP_TIMEOUT_SECONDS}"

  if ! kill -0 "$target_pid" 2>/dev/null; then
    return
  fi

  log_event "Stopping ${label} process ${target_pid}."
  kill -TERM "$target_pid" 2>/dev/null || true
  pkill -TERM -P "$target_pid" 2>/dev/null || true

  while (( wait_count < max_wait )); do
    if ! kill -0 "$target_pid" 2>/dev/null; then
      return
    fi
    sleep 1
    wait_count=$((wait_count + 1))
  done

  if kill -0 "$target_pid" 2>/dev/null; then
    log_event "Forcing kill for ${label} process ${target_pid}."
    kill -KILL "$target_pid" 2>/dev/null || true
    pkill -KILL -P "$target_pid" 2>/dev/null || true
  fi
}

terminate_existing_dotnet_watch() {
  local running_watch_pids

  running_watch_pids="$(pgrep -f 'dotnet watch' || true)"
  if [[ -z "$running_watch_pids" ]]; then
    return
  fi

  log_event "Stopping stale dotnet watch processes before launch."
  while IFS= read -r running_watch_pids; do
    if [[ -n "$running_watch_pids" ]] && [[ "$running_watch_pids" != "$$" ]]; then
      terminate_pid_with_wait "$running_watch_pids" "stale-dotnet-watch"
    fi
  done <<< "$running_watch_pids"

  local cleanup_attempts=0
  while (( cleanup_attempts < PRESTART_CLEANUP_TIMEOUT_SECONDS )); do
    running_watch_pids="$(pgrep -f 'dotnet watch' || true)"
    if [[ -z "$running_watch_pids" ]]; then
      return
    fi
    sleep 1
    cleanup_attempts=$((cleanup_attempts + 1))
  done

  log_event "Force-killing stubborn dotnet watch processes."
  pgrep -f 'dotnet watch' | while IFS= read -r running_watch_pids; do
    if [[ -n "$running_watch_pids" ]] && [[ "$running_watch_pids" != "$$" ]]; then
      terminate_pid_with_wait "$running_watch_pids" "stale-dotnet-watch"
    fi
  done
}

stop_dotnet_watch_pid() {
  local target_pid="$1"

  if [[ -z "$target_pid" ]]; then
    return
  fi

  if ! kill -0 "$target_pid" 2>/dev/null; then
    return
  fi

  terminate_pid_with_wait "$target_pid" "dotnet-watch"
}

preflight_cleanup() {
  local port
  local port_listeners

  if [[ "${PRESTART_CLEANUP_DOTNET_WATCH,,}" == "true" ]]; then
    terminate_existing_dotnet_watch
  fi

  if [[ -z "$PRESTART_CLEANUP_PORTS" ]]; then
    return
  fi

  while IFS= read -r port; do
    if [[ -z "$port" ]]; then
      continue
    fi

    port_listeners="$(find_listener_pids "$port")"
    if [[ -z "$port_listeners" ]]; then
      continue
    fi

    while IFS= read -r listener_pid; do
      if [[ -z "$listener_pid" ]] || [[ "$listener_pid" == "$$" ]]; then
        continue
      fi
      terminate_pid_with_wait "$listener_pid" "port-${port}"
    done <<< "$port_listeners"
  done <<< "$(normalize_ports)"
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

  echo "$((HEALTH_CHECK_MIN_INTERVAL_SECONDS + (RANDOM % interval_range)))"
}

build_health_context_file() {
  local failure_count="$1"
  local failure_reason="$2"
  local context_file
  local failure_ts

  context_file="$(mktemp "${LOG_DIR}/autofix-health-context.XXXXXX")"
  failure_ts="$(date -u +'%Y-%m-%dT%H:%M:%S.%3NZ')"
  {
    echo "Health-check recovery path engaged"
    echo "timestamp=${failure_ts}"
    echo "url=${HEALTH_CHECK_URL}"
    echo "max_attempts=${HEALTH_CHECK_MAX_ATTEMPTS}"
    echo "failure_count=${failure_count}"
    echo "failure_reason=${failure_reason}"
    echo "failure_taxonomy=health"
    echo "failure_severity=high"
    echo "watch_project=${WATCH_PROJECT}"
    echo "watch_pid=${CURRENT_DOTNET_WATCH_PID:-<not-running>}"
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
    --failure-taxonomy "health" \
    --failure-severity "high" \
    --failure-signature "health-check:${HEALTH_CHECK_MAX_ATTEMPTS}" \
    --context-file "$context_file" \
    --context-lines "$HEALTH_CONTEXT_LINES" \
    --cooldown "$HEALTH_FIX_COOLDOWN_SECONDS" \
    --dispatch-log "$AUTOFIX_DISPATCH_LOG" \
    --unified-log "$UNIFIED_ERROR_LOG" \
    --state-file "$AUTOFIX_STATE_FILE"
}

build_event_signature() {
  local event_line="$1"
  local normalized

  normalized="${event_line,,}"
  normalized="$(printf '%s' "$normalized" | sed -E 's/[0-9]+/N/g; s/[[:punct:]]+/ /g; s/[[:space:]]+/ /g; s/^[[:space:]]+|[[:space:]]+$//g')"
  normalized="$(printf '%s' "$normalized" | sed -E 's/^(.{0,140}).*$/\1/')"

  printf '%s' "$normalized"
}

classify_failure() {
  local event_line="$1"
  local normalized
  normalized="${event_line,,}"

  FAILURE_TAXONOMY="runtime"
  FAILURE_SEVERITY="medium"

  if [[ "$normalized" == *"build failed"* || "$normalized" == *"build failed:"* || "$normalized" == *"unable to apply changes"* || "$normalized" == *"failed to compile"* || "$normalized" == *"rude edit"* || "$normalized" == *"error cs"* ]]; then
    FAILURE_TAXONOMY="compile"
    FAILURE_SEVERITY="high"
  fi

  if [[ "$normalized" == *"hosting failed to start"* || "$normalized" == *"unhandled exception"* || "$normalized" == *"address already in use"* || "$normalized" == *fatal* || "$normalized" == *"bind to address"* ]]; then
    FAILURE_TAXONOMY="startup"
    FAILURE_SEVERITY="high"
  fi
}

should_dispatch_event() {
  local source_file="$1"
  local payload="$2"
  local cooldown_seconds="$3"
  local now_ts
  local gate_key
  local last_seen

  if [[ "$FAILURE_TAXONOMY" != "compile" ]]; then
    return 0
  fi

  gate_key="${FAILURE_TAXONOMY}|${source_file}|$(build_event_signature "$payload")"
  now_ts="$(date +%s)"
  last_seen="${DISPATCH_STORM_GATES[$gate_key]-0}"

  if [[ "$last_seen" -gt 0 ]] && (( now_ts - last_seen < cooldown_seconds )); then
    return 1
  fi

  DISPATCH_STORM_GATES["$gate_key"]="$now_ts"
  return 0
}

dispatch_runtime_fix() {
  local source_file="$1"
  local event_ts="$2"
  local payload="$3"
  local cooldown="$RUNTIME_COOLDOWN_SECONDS"

  classify_failure "$payload"

  case "$FAILURE_TAXONOMY" in
    compile)
      cooldown="$COMPILE_COOLDOWN_SECONDS"
      ;;
    startup)
      cooldown="$STARTUP_COOLDOWN_SECONDS"
      ;;
    runtime)
      cooldown="$RUNTIME_COOLDOWN_SECONDS"
      ;;
    *)
      cooldown="$FIX_COOLDOWN_SECONDS"
      ;;
  esac

  if ! should_dispatch_event "$source_file" "$payload" "$cooldown"; then
    return
  fi

  "$SCRIPT_DIR/fix-dispatcher.sh" \
    --repo-root "$REPO_ROOT" \
    --source "$source_file" \
    --timestamp "$event_ts" \
    --line "$payload" \
    --event-kind "runtime-log" \
    --failure-taxonomy "$FAILURE_TAXONOMY" \
    --failure-severity "$FAILURE_SEVERITY" \
    --failure-signature "$(build_event_signature "$payload")" \
    --cooldown "$cooldown" \
    --dispatch-log "$AUTOFIX_DISPATCH_LOG" \
    --unified-log "$UNIFIED_ERROR_LOG" \
    --state-file "$AUTOFIX_STATE_FILE" &
}

watch_file() {
  local source_file="$1"
  local short_source
  short_source="$(basename "$source_file")"

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

    dispatch_runtime_fix "$source_file" "$event_ts" "$payload"
  done < <(tail -n 0 -F -- "$source_file")
}

dotnet_watch_requested_restart() {
  local start_line="$1"
  local stdout_tail

  stdout_tail="$(sed -n "$((start_line + 1)),$ p" "$DOTNET_WATCH_STDOUT_LOG")"
  [[ "$stdout_tail" == *"Restart is needed to apply the changes."* ]]
}

sanitize_positive_integer() {
  local var_value="$1"
  local default_value="$2"

  if [[ "$var_value" =~ ^[0-9]+$ ]]; then
    printf '%s' "$var_value"
    return
  fi

  printf '%s' "$default_value"
}

initialize_runtime() {
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

  FIX_COOLDOWN_SECONDS="$(sanitize_positive_integer "${FIX_COOLDOWN_SECONDS}" 10)"
  COMPILE_COOLDOWN_SECONDS="$(sanitize_positive_integer "${COMPILE_COOLDOWN_SECONDS}" 30)"
  STARTUP_COOLDOWN_SECONDS="$(sanitize_positive_integer "${STARTUP_COOLDOWN_SECONDS}" 25)"
  RUNTIME_COOLDOWN_SECONDS="$(sanitize_positive_integer "${RUNTIME_COOLDOWN_SECONDS}" 10)"
  RESTART_DELAY_SECONDS="$(sanitize_positive_integer "${RESTART_DELAY_SECONDS}" 3)"
  RESTART_MAX_DELAY_SECONDS="$(sanitize_positive_integer "${RESTART_MAX_DELAY_SECONDS}" 30)"
  PRESTART_CLEANUP_TIMEOUT_SECONDS="$(sanitize_positive_integer "${PRESTART_CLEANUP_TIMEOUT_SECONDS}" 10)"
  HEALTH_CHECK_MAX_ATTEMPTS="$(sanitize_positive_integer "${HEALTH_CHECK_MAX_ATTEMPTS}" 10)"
  HEALTH_CHECK_MIN_INTERVAL_SECONDS="$(sanitize_positive_integer "${HEALTH_CHECK_MIN_INTERVAL_SECONDS}" 5)"
  HEALTH_CHECK_MAX_INTERVAL_SECONDS="$(sanitize_positive_integer "${HEALTH_CHECK_MAX_INTERVAL_SECONDS}" 10)"
  HEALTH_CHECK_TIMEOUT_SECONDS="$(sanitize_positive_integer "${HEALTH_CHECK_TIMEOUT_SECONDS}" 5)"
  HEALTH_CONTEXT_LINES="$(sanitize_positive_integer "${HEALTH_CONTEXT_LINES}" 120)"
  HEALTH_FIX_COOLDOWN_SECONDS="$(sanitize_positive_integer "${HEALTH_FIX_COOLDOWN_SECONDS}" 90)"
}

acquire_launcher_lock
initialize_runtime

trap cleanup_watchers EXIT INT TERM

for log_file in "${ERROR_LOGS[@]}"; do
  watch_file "$log_file" &
  TAIL_PIDS+=("$!")
done

log_event "Starting dotnet watch for ${WATCH_PROJECT}"
log_event "Monitoring log files: ${ERROR_LOGS[*]}"

attempt=0
while true; do
  preflight_cleanup

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
