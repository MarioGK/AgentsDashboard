# MongoDB Backup and Restore

Backup and restore scripts for the AgentsDashboard MongoDB database.

## Quick Start

### Manual Backup

```bash
# Basic backup with defaults
./backup.sh

# Backup with custom MongoDB URI
MONGODB_URI="mongodb://user:pass@host:27017" ./backup.sh

# Backup with S3 upload
S3_ENABLED=true S3_BUCKET=my-backups ./backup.sh
```

### Manual Restore

```bash
# List available backups
./restore.sh --list

# Restore from specific file
./restore.sh /var/lib/mongodb/backups/agentsdashboard_20240214_143052.tar.gz

# Restore by timestamp
./restore.sh 20240214

# Dry run (no changes)
./restore.sh --dry-run 20240214

# Restore with drop existing collections
./restore.sh --drop 20240214
```

## Configuration

### Environment Variables

| Variable | Default | Description |
|----------|---------|-------------|
| `MONGODB_URI` | `mongodb://localhost:27017` | MongoDB connection URI |
| `DATABASE_NAME` | `agentsdashboard` | Database to backup/restore |
| `BACKUP_DIR` | `/var/lib/mongodb/backups` | Local backup directory |
| `RETENTION_COUNT` | `7` | Number of backups to keep |
| `LOG_FILE` | `${BACKUP_DIR}/backup.log` | Log file path |

### S3 Configuration

| Variable | Default | Description |
|----------|---------|-------------|
| `S3_ENABLED` | `false` | Enable S3 upload |
| `S3_BUCKET` | - | S3 bucket name (required if S3_ENABLED=true) |
| `S3_PREFIX` | `mongodb-backups` | S3 key prefix |
| `AWS_REGION` | `us-east-1` | AWS region |
| `AWS_ACCESS_KEY_ID` | - | AWS access key (or use IAM role) |
| `AWS_SECRET_ACCESS_KEY` | - | AWS secret key (or use IAM role) |

## Scheduling Automated Backups

### Cron (Linux/macOS)

Add to crontab:

```bash
# Daily backup at 2 AM
0 2 * * * /path/to/backup.sh >> /var/log/mongodb-backup.log 2>&1

# Every 6 hours
0 */6 * * * /path/to/backup.sh >> /var/log/mongodb-backup.log 2>&1
```

Edit crontab:
```bash
crontab -e
```

### Kubernetes CronJob

1. Update configuration in `cron-backup.yaml`:
   - Set `MONGODB_URI` in the Secret
   - Configure S3 settings if needed in the ConfigMap
   - Adjust schedule (default: daily at 2 AM)

2. Apply the manifest:
```bash
kubectl apply -f cron-backup.yaml
```

3. Verify the CronJob:
```bash
kubectl get cronjobs -n mongodb-backup
kubectl logs -n mongodb-backup -l app=mongodb-backup
```

### Manual Kubernetes Backup

Trigger a one-time backup:
```bash
kubectl create job --from=cronjob/mongodb-backup manual-backup-$(date +%s) -n mongodb-backup
```

Or use the included manual Job:
```bash
kubectl apply -f cron-backup.yaml
kubectl create job mongodb-backup-manual --from=cronjob/mongodb-backup -n mongodb-backup
```

## Restore Operations

### From Local Backup

```bash
# List backups
./restore.sh --list

# Restore latest backup
./restore.sh $(ls -t /var/lib/mongodb/backups/*.tar.gz | head -1 | xargs basename | sed 's/.tar.gz//')

# Restore with confirmation skip
./restore.sh --force 20240214
```

### From S3

```bash
# Download backup from S3
aws s3 cp s3://my-bucket/mongodb-backups/agentsdashboard_20240214.tar.gz /tmp/

# Restore
./restore.sh /tmp/agentsdashboard_20240214.tar.gz
```

### Kubernetes Restore

```bash
# Copy backup from PVC to local
kubectl cp mongodb-backup/pod-name:/backups/agentsdashboard_20240214.tar.gz ./backup.tar.gz

# Or use a restore Job
kubectl run mongodb-restore \
  --image=mongo:8.0 \
  --restart=OnFailure \
  -n mongodb-backup \
  --rm -it \
  --env="MONGODB_URI=mongodb://mongodb:27017" \
  --env="DATABASE_NAME=agentsdashboard" \
  -- /bin/bash -c "mongorestore --uri=\$MONGODB_URI --db=\$DATABASE_NAME /backups/agentsdashboard/ --drop"
```

## S3 Configuration Details

### Using IAM Roles (Recommended for AWS)

When running on EC2 or EKS, use IAM roles instead of access keys:

```yaml
# In cron-backup.yaml, add service account
serviceAccountName: mongodb-backup-sa
```

Create IAM policy:
```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Effect": "Allow",
      "Action": [
        "s3:PutObject",
        "s3:GetObject",
        "s3:ListBucket",
        "s3:DeleteObject"
      ],
      "Resource": [
        "arn:aws:s3:::my-bucket",
        "arn:aws:s3:::my-bucket/mongodb-backups/*"
      ]
    }
  ]
}
```

### Using Access Keys

Set environment variables:
```bash
export AWS_ACCESS_KEY_ID=AKIAIOSFODNN7EXAMPLE
export AWS_SECRET_ACCESS_KEY=wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY
export AWS_REGION=us-east-1
```

Or configure AWS CLI:
```bash
aws configure
```

## Backup Retention

The backup script automatically cleans up old backups based on `RETENTION_COUNT`:

- Local: Removes oldest `.tar.gz` files exceeding retention count
- S3: Removes oldest S3 objects exceeding retention count

Example with `RETENTION_COUNT=7`:
```
Day 1:  backup_1.tar.gz
Day 2:  backup_1.tar.gz, backup_2.tar.gz
...
Day 7:  backup_1.tar.gz, ..., backup_7.tar.gz
Day 8:  backup_2.tar.gz, ..., backup_8.tar.gz (backup_1 deleted)
```

## Monitoring and Alerting

### Check Backup Status

```bash
# View recent backups
ls -lt /var/lib/mongodb/backups/

# Check backup log
tail -f /var/lib/mongodb/backups/backup.log

# Verify backup integrity
tar -tzf /var/lib/mongodb/backups/agentsdashboard_*.tar.gz | head
```

### Kubernetes Health Checks

```bash
# Check CronJob status
kubectl describe cronjob mongodb-backup -n mongodb-backup

# View recent jobs
kubectl get jobs -n mongodb-backup

# Check job logs
kubectl logs job/mongodb-backup-<timestamp> -n mongodb-backup
```

### Prometheus Alerting Rules

```yaml
groups:
  - name: mongodb-backup
    rules:
      - alert: MongoDBBackupMissing
        expr: |
          time() - mongodb_backup_last_success_timestamp_seconds > 86400
        for: 1h
        labels:
          severity: critical
        annotations:
          summary: "MongoDB backup is missing or stale"
          description: "No successful backup in the last 24 hours"
```

## Troubleshooting

### Backup Fails

1. Check MongoDB connectivity:
```bash
mongosh "${MONGODB_URI}" --eval "db.stats()"
```

2. Check disk space:
```bash
df -h /var/lib/mongodb/backups
```

3. Check permissions:
```bash
ls -la /var/lib/mongodb/backups
```

### Restore Fails

1. Verify backup integrity:
```bash
tar -tzf backup.tar.gz
```

2. Check MongoDB connectivity:
```bash
mongosh "${MONGODB_URI}" --eval "db.stats()"
```

3. Use dry-run mode:
```bash
./restore.sh --dry-run backup.tar.gz
```

### S3 Upload Fails

1. Verify AWS credentials:
```bash
aws sts get-caller-identity
```

2. Test S3 access:
```bash
aws s3 ls s3://my-bucket/
```

3. Check bucket permissions and region

## Exit Codes

| Code | Description |
|------|-------------|
| 0 | Success |
| 1 | General error |
| 2 | Missing dependencies |
| 3 | Backup not found / Backup creation failed |
| 4 | S3 upload failed |

## Security Considerations

- Store `MONGODB_URI` with credentials securely (use Kubernetes Secrets)
- Restrict access to backup directory (`chmod 700`)
- Enable S3 bucket encryption and versioning
- Use IAM roles instead of access keys when possible
- Consider encrypting backup archives with GPG
