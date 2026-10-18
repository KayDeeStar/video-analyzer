variable "name_prefix" {
  description = "Prefix for resource names, e.g. videoanalyzer-dev."
  type        = string
}

variable "account_id" {
  description = "AWS account id, appended to the bucket name to keep it globally unique."
  type        = string
}

variable "api_endpoint" {
  description = "HTTP API endpoint (https://xxxx.execute-api.region.amazonaws.com) served under /api/*."
  type        = string
}

variable "price_class" {
  description = "CloudFront edge locations to use. PriceClass_100 = North America and Europe (cheapest)."
  type        = string
  default     = "PriceClass_100"
}
