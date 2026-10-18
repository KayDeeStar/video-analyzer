# Media bucket: uploaded videos, intermediate work files, job status and analysis results.
#
# Key layout:
#   uploads/{jobId}/original.<ext>   - raw upload (browser PUTs here via pre-signed URL)
#   jobs/{jobId}/job.json            - job metadata + status
#   work/{jobId}/...                 - intermediate artifacts (Rekognition output, frames, transcodes)
#   results/{jobId}/...              - final result.json + portraits

resource "aws_s3_bucket" "media" {
  bucket        = "${var.name_prefix}-media-${var.account_id}"
  force_destroy = var.force_destroy
}

resource "aws_s3_bucket_ownership_controls" "media" {
  bucket = aws_s3_bucket.media.id
  rule {
    object_ownership = "BucketOwnerEnforced"
  }
}

resource "aws_s3_bucket_public_access_block" "media" {
  bucket                  = aws_s3_bucket.media.id
  block_public_acls       = true
  block_public_policy     = true
  ignore_public_acls      = true
  restrict_public_buckets = true
}

resource "aws_s3_bucket_server_side_encryption_configuration" "media" {
  bucket = aws_s3_bucket.media.id
  rule {
    apply_server_side_encryption_by_default {
      sse_algorithm = "AES256"
    }
    bucket_key_enabled = true
  }
}

# The browser uploads parts directly to S3 with pre-signed URLs, so S3 must allow
# cross-origin PUTs and expose the ETag header (needed to complete multipart uploads).
resource "aws_s3_bucket_cors_configuration" "media" {
  bucket = aws_s3_bucket.media.id

  cors_rule {
    allowed_methods = ["PUT", "GET", "HEAD"]
    allowed_origins = var.cors_allowed_origins
    allowed_headers = ["*"]
    expose_headers  = ["ETag"]
    max_age_seconds = 3000
  }
}

resource "aws_s3_bucket_lifecycle_configuration" "media" {
  bucket = aws_s3_bucket.media.id

  rule {
    id     = "abort-incomplete-multipart-uploads"
    status = "Enabled"
    filter {}

    abort_incomplete_multipart_upload {
      days_after_initiation = 1
    }
  }

  rule {
    id     = "expire-work-files"
    status = "Enabled"
    filter {
      prefix = "work/"
    }

    expiration {
      days = var.work_retention_days
    }
  }

  dynamic "rule" {
    for_each = toset(["uploads/", "jobs/", "results/"])
    content {
      id     = "expire-${trimsuffix(rule.value, "/")}"
      status = "Enabled"
      filter {
        prefix = rule.value
      }

      expiration {
        days = var.retention_days
      }
    }
  }
}

# Send all S3 events to EventBridge; the processing pipeline (step 4) subscribes to
# "Object Created" events under uploads/.
resource "aws_s3_bucket_notification" "media" {
  bucket      = aws_s3_bucket.media.id
  eventbridge = true
}

data "aws_iam_policy_document" "media_tls_only" {
  statement {
    sid     = "DenyInsecureTransport"
    effect  = "Deny"
    actions = ["s3:*"]
    resources = [
      aws_s3_bucket.media.arn,
      "${aws_s3_bucket.media.arn}/*",
    ]
    principals {
      type        = "*"
      identifiers = ["*"]
    }
    condition {
      test     = "Bool"
      variable = "aws:SecureTransport"
      values   = ["false"]
    }
  }
}

resource "aws_s3_bucket_policy" "media" {
  bucket     = aws_s3_bucket.media.id
  policy     = data.aws_iam_policy_document.media_tls_only.json
  depends_on = [aws_s3_bucket_public_access_block.media]
}
