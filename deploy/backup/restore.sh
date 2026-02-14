#!/bin/bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SCRIPT_NAME="$(basename "${BASH_SOURCE[0]}")"

MONGODB_URI="${MONGODB_URI:-mongodb://localhost:27017}"
DATABASE_NAME="${DATABASE_NAME:-agentsdashboard}"
BACKUP_DIR="${BACKUP_DIR:-/var/lib/mongodb/backups}"
DRY_RUN="${DRY_RUN:-false}"
FORCE="${FORCE:-false}"
DROP_EXISTING="${DROP_EXISTING:-false}"
LOG_FILE="${LOG_FILE:-${BACKUP_DIR}/restore.log}"

log() {
    local level="$1"
    shift
    local message="$*"
    local timestamp=$(date +"%Y-%m-%d %H:%M:%S")
    echo "[${timestamp}] [${level}] ${message}"
    if [[ -d "$(dirname "${LOG_FILE}")" ]]; then
        echo "[${timestamp}] [${level}] ${message}" >> "${LOG_FILE}"
    fi
}

info() { log "INFO" "$@"; }
warn() { log "WARN" "$@"; }
error() { log "ERROR" "$@"; exit 1; }

check_dependencies() {
    local missing=()
    
    for cmd in mongorestore tar; do
        if ! command -v "${cmd}" &> /dev/null; then
            missing+=("${cmd}")
        fi
    done
    
    if [[ ${#missing[@]} -gt 0 ]]; then
        error "Missing required dependencies: ${missing[*]}"
    fi
}

find_backup() {
    local input="$1"
    
    if [[ -f "${input}" ]]; then
        echo "${input}"
        return 0
    fi
    
    if [[ "${input}" == *".tar.gz" ]] && [[ -f "${BACKUP_DIR}/$(basename "${input}")" ]]; then
        echo "${BACKUP_DIR}/$(basename "${input}")"
        return 0
    fi
    
    local pattern="${DATABASE_NAME}_${input}*.tar.gz"
    local matches=$(find "${BACKUP_DIR}" -name "${pattern}" 2>/dev/null | sort -r | head -1)
    
    if [[ -n "${matches}" ]]; then
        echo "${matches}"
        return 0
    fi
    
    pattern="${DATABASE_NAME}_${input}.tar.gz"
    if [[ -f "${BACKUP_DIR}/${pattern}" ]]; then
        echo "${BACKUP_DIR}/${pattern}"
        return 0
    fi
    
    return 1
}

list_available_backups() {
    info "Available backups in ${BACKUP_DIR}:"
    echo ""
    
    local archives=$(find "${BACKUP_DIR}" -name "${DATABASE_NAME}_*.tar.gz" -printf "%f\n" 2>/dev/null | sort -r)
    
    if [[ -z "${archives}" ]]; then
        echo "No backups found"
        return 1
    fi
    
    echo "${archives}" | while read -r archive; do
        local path="${BACKUP_DIR}/${archive}"
        local size=$(du -h "${path}" 2>/dev/null | cut -f1)
        local mtime=$(stat -c %y "${path}" 2>/dev/null | cut -d. -f1)
        printf "  %-40s %8s  %s\n" "${archive}" "${size}" "${mtime}"
    done
    
    echo ""
}

extract_backup() {
    local archive_path="$1"
    local extract_dir="${BACKUP_DIR}/restore_temp_${TIMESTAMP}"
    
    info "Extracting backup to: ${extract_dir}"
    
    mkdir -p "${extract_dir}"
    
    if ! tar -xzf "${archive_path}" -C "${extract_dir}"; then
        rm -rf "${extract_dir}"
        error "Failed to extract backup archive"
    fi
    
    local backup_dir=$(find "${extract_dir}" -mindepth 1 -maxdepth 1 -type d | head -1)
    
    if [[ -z "${backup_dir}" ]]; then
        rm -rf "${extract_dir}"
        error "Could not find backup directory in archive"
    fi
    
    echo "${backup_dir}"
}

restore_database() {
    local backup_dir="$1"
    
    info "Restoring database: ${DATABASE_NAME}"
    info "Source: ${backup_dir}"
    
    local args=(
        --uri="${MONGODB_URI}"
        --db="${DATABASE_NAME}"
        --dir="${backup_dir}/${DATABASE_NAME}"
    )
    
    if [[ "${DROP_EXISTING}" == "true" ]]; then
        args+=(--drop)
        warn "DROP_EXISTING is enabled - existing collections will be dropped!"
    fi
    
    if [[ "${DRY_RUN}" == "true" ]]; then
        args+=(--dry-run)
        info "DRY RUN MODE - No changes will be made"
    fi
    
    if ! mongorestore "${args[@]}"; then
        error "Database restore failed"
    fi
    
    info "Database restore completed successfully"
}

cleanup_temp() {
    local extract_dir="${BACKUP_DIR}/restore_temp_${TIMESTAMP}"
    
    if [[ -d "${extract_dir}" ]]; then
        info "Cleaning up temporary files"
        rm -rf "${extract_dir}"
    fi
}

confirm_restore() {
    local archive_path="$1"
    
    if [[ "${FORCE}" == "true" ]]; then
        return 0
    fi
    
    echo ""
    echo "============================================"
    echo "WARNING: You are about to restore the database"
    echo "============================================"
    echo "Database:     ${DATABASE_NAME}"
    echo "Backup file:  ${archive_path}"
    echo "Drop existing: ${DROP_EXISTING}"
    echo "Dry run:      ${DRY_RUN}"
    echo ""
    
    if [[ "${DROP_EXISTING}" == "true" ]]; then
        echo "!!! CAUTION: DROP_EXISTING is enabled !!!"
        echo "All existing data in ${DATABASE_NAME} will be replaced!"
        echo ""
    fi
    
    read -p "Are you sure you want to continue? [y/N] " -n 1 -r
    echo ""
    
    if [[ ! $REPLY =~ ^[Yy]$ ]]; then
        info "Restore cancelled by user"
        exit 0
    fi
}

print_summary() {
    local archive_path="$1"
    local end_time=$(date +%s)
    local duration=$((end_time - START_TIME))
    
    echo ""
    echo "============================================"
    echo "Restore Summary"
    echo "============================================"
    echo "Database:     ${DATABASE_NAME}"
    echo "Backup file:  ${archive_path}"
    echo "Drop existing: ${DROP_EXISTING}"
    echo "Dry run:      ${DRY_RUN}"
    echo "Duration:     ${duration}s"
    echo "============================================"
}

show_usage() {
    cat << EOF
Usage: ${SCRIPT_NAME} [OPTIONS] <backup>

MongoDB Restore Script for AgentsDashboard

Arguments:
    backup                  Backup file path or timestamp (YYYYMMDD or YYYYMMDD_HHMMSS)

Options:
    -h, --help              Show this help message
    -l, --list              List available backups
    -n, --dry-run           Perform a dry run without making changes
    -f, --force             Skip confirmation prompt
    --drop                  Drop existing collections before restore

Environment Variables:
    MONGODB_URI             MongoDB connection URI (default: mongodb://localhost:27017)
    DATABASE_NAME           Database name to restore (default: agentsdashboard)
    BACKUP_DIR              Backup directory (default: /var/lib/mongodb/backups)
    DRY_RUN                 Enable dry run mode (default: false)
    FORCE                   Skip confirmation (default: false)
    DROP_EXISTING           Drop existing collections (default: false)
    LOG_FILE                Log file path (default: \${BACKUP_DIR}/restore.log)

Examples:
    # List available backups
    ./restore.sh --list

    # Restore from specific file
    ./restore.sh /path/to/backup.tar.gz

    # Restore by timestamp
    ./restore.sh 20240214
    ./restore.sh 20240214_143052

    # Dry run restore
    ./restore.sh --dry-run 20240214

    # Force restore with drop
    ./restore.sh --force --drop 20240214

Exit Codes:
    0   Success
    1   General error
    2   Missing dependencies
    3   Backup not found
    4   Restore failed
EOF
}

main() {
    START_TIME=$(date +%s)
    TIMESTAMP=$(date +"%Y%m%d_%H%M%S")
    
    local backup_input=""
    
    while [[ $# -gt 0 ]]; do
        case "$1" in
            -h|--help)
                show_usage
                exit 0
                ;;
            -l|--list)
                check_dependencies
                list_available_backups
                exit 0
                ;;
            -n|--dry-run)
                DRY_RUN=true
                shift
                ;;
            -f|--force)
                FORCE=true
                shift
                ;;
            --drop)
                DROP_EXISTING=true
                shift
                ;;
            -*)
                error "Unknown option: $1"
                ;;
            *)
                backup_input="$1"
                shift
                ;;
        esac
    done
    
    if [[ -z "${backup_input}" ]]; then
        error "No backup specified. Use --list to see available backups."
    fi
    
    check_dependencies
    
    info "Starting MongoDB restore process"
    info "Database: ${DATABASE_NAME}"
    
    local archive_path
    if ! archive_path=$(find_backup "${backup_input}"); then
        error "Backup not found: ${backup_input}"
    fi
    
    info "Found backup: ${archive_path}"
    
    confirm_restore "${archive_path}"
    
    local backup_dir=$(extract_backup "${archive_path}")
    
    restore_database "${backup_dir}"
    
    cleanup_temp
    
    print_summary "${archive_path}"
    
    info "Restore completed successfully"
    exit 0
}

main "$@"
