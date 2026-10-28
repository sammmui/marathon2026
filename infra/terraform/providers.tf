terraform {
  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 5.0"
    }
  }
}

provider "aws" {
  region = var.aws_region
}

# Reserved for ACM certificates once a custom domain is added (CloudFront requires
# certs to live in us-east-1 regardless of the distribution's region).
provider "aws" {
  alias  = "eu-north-1"
  region = "eu-north-1"
}
