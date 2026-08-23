# ═══════════════════════════════════════════════════════════════════════════════
# PRODUCTION (§2, §4, §17).
#
# TROIS NŒUDS, ET LE TROISIÈME N'EST PAS DE LA CAPACITÉ.
#
# Le §4 demande « 3 × 8 vCPU / 16 Go ». Le troisième nœud existe pour le QUORUM :
# etcd a besoin d'une majorité pour élire un leader, et une majorité de deux est
# deux. Avec deux nœuds, perdre l'un fige le plan de contrôle — les pods qui
# tournent survivent, mais plus rien ne se replanifie, ce qui est exactement la
# situation du §24 (« perte d'un nœud Kubernetes ») que l'on prétend couvrir.
#
# Ce que ces trois nœuds portent, à ne pas sous-estimer : quinze services, plus
# tout le data plane (D9) — 3 instances Postgres, 3 brokers Kafka, Redis, MinIO,
# et leurs volumes. Les métriques du §17 diront vite s'il faut un quatrième nœud.
#
# MÊME MANŒUVRE D'AMORÇAGE QUE STAGING pour le bucket d'état : voir l'encadré
# de `../staging/main.tf`. À faire une fois, et à ne pas refaire de mémoire.
# ═══════════════════════════════════════════════════════════════════════════════

terraform {
  required_version = ">= 1.11"

  required_providers {
    ovh = {
      source  = "ovh/ovh"
      version = "~> 1.5"
    }
    openstack = {
      source  = "terraform-provider-openstack/openstack"
      version = "~> 2.1"
    }
  }

  backend "s3" {
    bucket = "hba-production-tfstate"
    key    = "infrastructure.tfstate"
    region = "gra"

    endpoints = {
      s3 = "https://s3.gra.io.cloud.ovh.net"
    }

    use_lockfile                = true
    use_path_style              = true
    skip_credentials_validation = true
    skip_region_validation      = true
    skip_requesting_account_id  = true
    skip_metadata_api_check     = true
    skip_s3_checksum            = true
  }
}

variable "service_name" {
  description = "Identifiant du projet Public Cloud OVH."
  type        = string
}

variable "zone_dns" {
  description = "Domaine racine déjà délégué chez OVH."
  type        = string
}

variable "cle_ssh_publique" {
  description = "Clé publique SSH autorisée sur les nœuds (§19 : pas de mot de passe)."
  type        = string
}

variable "region" {
  type    = string
  default = "GRA11"
}

provider "ovh" {
  endpoint = "ovh-eu"
}

provider "openstack" {
  region = var.region
}

locals {
  environnement = "production"
}

module "object_storage" {
  source        = "../../modules/object-storage"
  environnement = local.environnement
  region        = "GRA"
}

module "network" {
  source        = "../../modules/network"
  service_name  = var.service_name
  environnement = local.environnement
  region        = var.region

  # Disjointe de staging (10.0/16) — voir l'encadré côté staging.
  cidr = "10.1.0.0/16"
}

module "kubernetes" {
  source           = "../../modules/kubernetes"
  environnement    = local.environnement
  region           = var.region
  network_id       = module.network.network_id
  nombre_noeuds    = 3
  gabarit          = "b3-16" # 8 vCPU / 16 Go (§4)
  cle_ssh_publique = var.cle_ssh_publique
}

module "dns" {
  source       = "../../modules/dns"
  zone         = var.zone_dns
  sous_domaine = "api"
  ip_ingress   = module.kubernetes.noeuds[0].ip_public
}

output "noeuds" {
  description = "Alimente `infra/ansible/inventory/production.yml`."
  value       = module.kubernetes.noeuds
}

output "fqdn_api" {
  value = module.dns.fqdn
}

output "bucket_sauvegardes" {
  value = module.object_storage.bucket_sauvegardes
}
