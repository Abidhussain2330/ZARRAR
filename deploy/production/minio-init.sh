#!/bin/sh
set -eu

: "${MINIO_ROOT_USER:?MINIO_ROOT_USER is required}"
: "${MINIO_ROOT_PASSWORD:?MINIO_ROOT_PASSWORD is required}"
: "${MINIO_ACCESS_KEY:?MINIO_ACCESS_KEY is required}"
: "${MINIO_SECRET_KEY:?MINIO_SECRET_KEY is required}"
: "${MINIO_BUCKET:?MINIO_BUCKET is required}"

[ "${#MINIO_BUCKET}" -ge 3 ] && [ "${#MINIO_BUCKET}" -le 63 ] || {
    printf '%s\n' 'MINIO_BUCKET must contain 3-63 characters.' >&2
    exit 64
}
case "$MINIO_BUCKET" in
    *[!a-z0-9.-]*|.*|*.|*..*)
        printf '%s\n' 'MINIO_BUCKET must be a lowercase DNS-style S3 bucket name.' >&2
        exit 64
        ;;
esac

mc alias set local http://minio:9000 "$MINIO_ROOT_USER" "$MINIO_ROOT_PASSWORD" >/dev/null
mc mb --ignore-existing "local/$MINIO_BUCKET" >/dev/null

cat >/tmp/zarrar-policy.json <<EOF
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Effect": "Allow",
      "Action": [
        "s3:GetBucketLocation",
        "s3:ListBucket",
        "s3:ListBucketMultipartUploads"
      ],
      "Resource": ["arn:aws:s3:::$MINIO_BUCKET"]
    },
    {
      "Effect": "Allow",
      "Action": [
        "s3:GetObject",
        "s3:PutObject",
        "s3:DeleteObject",
        "s3:AbortMultipartUpload",
        "s3:ListMultipartUploadParts"
      ],
      "Resource": ["arn:aws:s3:::$MINIO_BUCKET/*"]
    }
  ]
}
EOF

mc admin policy create local zarrar-app /tmp/zarrar-policy.json >/dev/null
mc admin user add local "$MINIO_ACCESS_KEY" "$MINIO_SECRET_KEY" >/dev/null
mc admin policy attach local zarrar-app --user="$MINIO_ACCESS_KEY" >/dev/null
mc admin user info local "$MINIO_ACCESS_KEY" >/dev/null
