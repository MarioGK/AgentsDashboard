#!/bin/bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SCRIPT_NAME="$(basename "${BASH_SOURCE[0]}")"
TIMESTAMP=$(date +"%Y%m%d_%H%M%S")

MONGODB_URI="${MONGODB_URI:-mongodb://localhost:27017}"
DATABASE_NAME="${DATABASE_NAME:-agentsdashboard}"
BACKUP_DIR="${BACKUP_DIR:-/var/lib/mongodb/backups}"
RETENTION_COUNT="${RETENTION_COUNT:-7}"
S3_ENABLED="${S3_ENABLED:-false}"
S3_BUCKET="${S3_BUCKET:-}"
S3_PREFIX="${S3_PREFIX:-mongodb-backups}"
AWS_REGION="${AWS_REGION:-us-east-1}"
LOG_FILE="${LOG_FILE:-${BACKUP_DIR}/backup.log}"

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
    
    for cmd in mongodump tar; do
        if ! command -v "${cmd}" &> /dev/null; then
            missing+=("${cmd}")
        fi
    done
    
    if [[ "${S3_ENABLED}" == "true" ]]; then
        if ! command -v aws &> /dev/null; then
            missing+=("aws-cli")
        fi
    fi
    
    if [[ ${#missing[@]} -gt 0 ]]; then
        error "Missing required dependencies: ${missing[*]}"
    fi
}

ensure_backup_dir() {
    if [[ ! -d "${BACKUP_DIR}" ]]; then
        info "Creating backup directory: ${BACKUP_DIR}"
        mkdir -p "${BACKUP_DIR}"
    fi
}

create_backup() {
    local backup_name="${DATABASE_NAME}_${TIMESTAMP}"
    local backup_path="${BACKUP_DIR}/${backup_name}"
    local archive_path="${BACKUP_DIR}/${backup_name}.tar.gz"
    
    info "Starting backup of database: ${DATABASE_NAME}"
    info "Backup path: ${backup_path}"
    
    mkdir -p "${backup_path}"
    
    if ! mongodump --uri="${MONGODB_URI}" \
                   --db="${DATABASE_NAME}" \
                   --out="${backup_path}" \
                   --quiet; then
        rm -rf "${backup_path}"
        error "Failed to create backup with mongodump"
    fi
    
    info "Compressing backup to: ${archive_path}"
    
    if ! tar -czf "${archive_path}" -C "${BACKUP_DIR}" "${backup_name}"; then
        rm -rf "${backup_path}" "${archive_path}"
        error "Failed to compress backup"
    fi
    
    rm -rf "${backup_path}"
    
    local size=$(du -h "${archive_path}" | cut -f1)
    info "Backup created successfully: ${archive_path} (${size})"
    
    echo "${archive_path}"
}

upload_to_s3() {
    local archive_path="$1"
    local filename=$(basename "${archive_path}")
    local s3_uri="s3://${S3_BUCKET}/${S3_PREFIX}/${filename}"
    
    info "Uploading backup to S3: ${s3_uri}"
    
    if ! aws s3 cp "${archive_path}" "${s3_uri}" \
         --region "${AWS_REGION}" \
         --only-show-errors; then
        error "Failed to upload backup to S3"
    fi
    
    info "Upload completed successfully"
}

cleanup_old_backups() {
    info "Cleaning up old backups (retention: ${RETENTION_COUNT})"
    
    local archives=()
    while IFS= read -r -d '' file; do
        archives+=("${file}")
    done < <(find "${BACKUP_DIR}" -name "${DATABASE_NAME}_*.tar.gz" -print0 | sort -z)
    
    local count=${#archives[@]}
    local to_delete=$((count - RETENTION_COUNT))
    
    if [[ ${to_delete} -gt 0 ]]; then
        info "Found ${count} backups, removing ${to_delete} oldest"
        for ((i=0; i<to_delete; i++)); do
            info "Deleting old backup: ${archives[$i]}"
            rm -f "${archives[$i]}"
        done
    else
        info "Found ${count} backups, no cleanup needed"
    fi
    
    if [[ "${S3_ENABLED}" == "true" ]]; then
        cleanup_s3_backups
    fi
}

cleanup_s3_backups() {
    info "Cleaning up old S3 backups"
    
    local s3_prefix_path="s3://${S3_BUCKET}/${S3_PREFIX}/"
    local objects=$(aws s3 ls "${s3_prefix_path}" \
                    --region "${AWS_REGION}" \
                    | grep "${DATABASE_NAME}_" \
                    | sort -k1,4)
    
    local count=$(echo "${objects}" | wc -l)
    local to_delete=$((count - RETENTION_COUNT))
    
    if [[ ${to_delete} -gt 0 ]]; then
        info "Found ${count} S3 backups, removing ${to_delete} oldest"
        echo "${objects}" | head -n ${to_delete} | while read -r line; do
            local filename=$(echo "${line}" | awk '{print $NF}')
            local s3_uri="s3://${S3_BUCKET}/${S3_PREFIX}/${filename}"
            info "Deleting old S3 backup: ${s3_uri}"
            aws s3 rm "${s3_uri}" --region "${AWS_REGION}" --only-show-errors
        done
    fi
}

print_summary() {
    local archive_path="$1"
    local end_time=$(date +%s)
    local duration=$((end_time - START_TIME))
    
    echo ""
    echo "============================================"
    echo "Backup Summary"
    echo "============================================"
    echo "Database:     ${DATABASE_NAME}"
    echo "Timestamp:    ${TIMESTAMP}"
    echo "Archive:      ${archive_path}"
    echo "Size:         $(du -h "${archive_path}" | cut -f1)"
    echo "Duration:     ${duration}s"
    echo "S3 Upload:    ${S3_ENABLED}"
    if [[ "${S3_ENABLED}" == "true" ]]; then
        echo "S3 Location:  s3://${S3_BUCKET}/${S3_PREFIX}/$(basename "${archive_path}")"
    fi
    echo "Retention:    ${RETENTION_COUNT} backups"
    echo "============================================"
}

show_usage() {
    cat << EOF
Usage: ${SCRIPT_NAME} [OPTIONS]

MongoDB Backup Script for AgentsDashboard

Options:
    --help                  Show this help message

Environment Variables:
    MONGODB_URI             MongoDB connection URI (default: mongodb://localhost:27017)
    DATABASE_NAME           Database name to backup (default: agentsdashboard)
    BACKUP_DIR              Local backup directory (default: /var/lib/mongodb/backups)
    RETENTION_COUNT         Number of backups to retain (default: 7)
    S3_ENABLED              Enable S3 upload (default: false)
    S3_BUCKET               S3 bucket name (required if S3_ENABLED=true)
    S3_PREFIX               S3 key prefix (default: mongodb-backups)
    AWS_REGION              AWS region (default: us-east-1)
    LOG_FILE                Log file path (default: \${BACKUP_DIR}/backup.log)

Examples:
    # Basic backup with defaults
    ./backup.sh

    # Backup with custom settings
    MONGODB_URI="mongodb://user:pass@host:27017" DATABASE_NAME="mydb" ./backup.sh

    # Backup with S3 upload
    S3_ENABLED=true S3_BUCKET=my-backups ./backup.sh

Exit Codes:
    0   Success
    1   General error
    2   Missing dependencies
    3   Backup creation failed
    4   S3 upload failed
EOF
}

main() {
    START_TIME=$(date +%s)
    
    if [[ "${1:-}" == "--help" ]] || [[ "${1:-}" == "-h" ]]; then
        show_usage
        exit 0
    fi
    
    info "Starting MongoDB backup process"
    info "Database: ${DATABASE_NAME}"
    info "Backup directory: ${BACKUP_DIR}"
    
    check_dependencies
    ensure_backup_dir
    
    local archive_path=$(create_backup)
    
    if [[ "${S3_ENABLED}" == "true" ]]; then
        if [[ -z "${S3_BUCKET}" ]]; then
            error "S3_BUCKET is required when S3_ENABLED=true"
        fi
        upload_to_s3 "${archive_path}"
    fi
    
    cleanup_old_backups
    print_summary "${archive_path}"
    
    info "Backup completed successfully"
    exit 0
}

main "$@"
