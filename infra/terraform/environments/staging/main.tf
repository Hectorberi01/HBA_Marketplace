# ═══════════════════════════════════════════════════════════════════════════════
# STAGING (§2, §4).
#
# DEUX NŒUDS, ET C'EST UN CHOIX QUI SE PAIE AILLEURS.
#
# Le §4 donne « 2 × 4 vCPU / 8 Go » pour staging. Avec deux nœuds, un seul
# serveur k3s : l'etcd n'est pas en quorum, et la perte du premier nœud perd le
# plan de contrôle. C'est acceptable ICI — staging se reconstruit — et cela ne
# l'est pas en production, d'où trois nœuds là-bas.
#
# L'ŒUF ET LA POULE DU BACKEND : LIRE AVANT LE PREMIER `init`.
#
# Le bucket `hba-staging-tfstate` est créé PAR ce code, et le backend ci-dessous
# veut y écrire. Au tout premier passage il n'existe pas encore. La séquence est
# donc :
#
#   1. commenter le bloc `backend "s3"` ;
#   2. `terraform init && terraform apply -target=module.object_storage` ;
#   3. décommenter le backend ;
#   4. `terraform init -migrate-state` — Terraform pousse l'état local vers OVH ;
#   5. SUPPRIMER `terraform.tfstate` du poste : il contient des secrets en clair.
#
# Sauter l'étape 5 est le défaut le plus courant de cette manœuvre : l'état
# local reste, quelqu'un applique depuis son poste sans backend, et les deux
# états divergent en silence.
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

  # L'ÉTAT NE VA NI EN LOCAL NI DANS GIT (§25).
  #
  # `use_lockfile` sérialise les `apply` : sans lui, deux exécutions concurrentes
  # écrivent chacune leur version et la seconde efface la première — une VM
  # créée puis oubliée de l'état, que plus rien ne décrit ni ne détruit.
  #
  # Les `skip_*` sont nécessaires parce que le backend S3 est celui d'AWS et que
  # OVH n'implémente ni STS ni la validation de région : sans eux, `init` échoue
  # sur une vérification qui n'a pas de sens hors AWS.
  backend "s3" {
    bucket = "hba-staging-tfstate"
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

# ─────────────────────────────────────────────────────────────────────────────
# Variables d'entrée. Aucune valeur par défaut sensible : voir
# `terraform.tfvars.example`.
# ─────────────────────────────────────────────────────────────────────────────

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
  environnement = "staging"
}

# ─────────────────────────────────────────────────────────────────────────────

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

  # PLAGES DISJOINTES ENTRE ENVIRONNEMENTS.
  #
  # 10.0/16 en staging, 10.1/16 en production. Deux environnements sur la même
  # plage empêchent tout appairage futur et, surtout, rendent une règle de
  # pare-feu recopiée d'un côté à l'autre silencieusement fausse.
  cidr = "10.0.0.0/16"
}

module "kubernetes" {
  source           = "../../modules/kubernetes"
  environnement    = local.environnement
  region           = var.region
  network_id       = module.network.network_id
  nombre_noeuds    = 2
  gabarit          = "b3-8" # 4 vCPU / 8 Go (§4)
  cle_ssh_publique = var.cle_ssh_publique
}

module "dns" {
  source       = "../../modules/dns"
  zone         = var.zone_dns
  sous_domaine = "backendapi.marketplace-staging"

  # LE PREMIER NŒUD PORTE L'INGRESS.
  #
  # C'est celui qu'Ansible désigne « serveur ». Tant qu'aucune IP flottante n'est
  # posée, la bascule du §24 (perte d'un nœud) demande une modification DNS
  # manuelle — TTL 300 s, donc cinq minutes de propagation, à compter dans le RTO.
  ip_ingress = module.kubernetes.noeuds[0].ip_public
}

# ─────────────────────────────────────────────────────────────────────────────
# Sorties : ce qu'Ansible et l'opérateur consomment.
# ─────────────────────────────────────────────────────────────────────────────

output "noeuds" {
  description = "Alimente `infra/ansible/inventory/staging.yml`."
  value       = module.kubernetes.noeuds
}

output "fqdn_api" {
  value = module.dns.fqdn
}

output "bucket_sauvegardes" {
  description = "À reporter dans le `destinationPath` du Cluster CNPG."
  value       = module.object_storage.bucket_sauvegardes
}
