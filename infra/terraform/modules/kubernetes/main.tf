# ═══════════════════════════════════════════════════════════════════════════════
# LES NŒUDS QUI PORTERONT k3s (§4).
#
# TERRAFORM CRÉE LES MACHINES. IL N'INSTALLE PAS k3s.
#
# La séparation est délibérée — voir le README de ce dossier. Ce module rend des
# VMs joignables et un inventaire ; c'est Ansible qui les transforme en cluster.
#
# LE DIMENSIONNEMENT DU §4 EST UN POINT DE DÉPART, PAS UNE CIBLE.
#
# « 2 × 4 vCPU / 8 Go » en staging, « 3 × 8 vCPU / 16 Go » en production. Le
# document le dit lui-même : à ajuster sur les métriques réelles — CPU, mémoire,
# RPS, latence gRPC, consumer lag, connexions DB.
#
# Un rappel qui ne se voit qu'à l'usage : Postgres, Kafka, Redis et MinIO tournent
# DANS ce cluster (D9). Les trois nœuds de production ne portent donc pas
# seulement quinze services applicatifs, mais aussi tout le data plane — dont
# trois instances Postgres et trois brokers Kafka avec leurs volumes.
# ═══════════════════════════════════════════════════════════════════════════════

terraform {
  required_providers {
    openstack = {
      source  = "terraform-provider-openstack/openstack"
      version = "~> 2.1"
    }
  }
}

variable "environnement" {
  type = string
}

variable "region" {
  type    = string
  default = "GRA11"
}

variable "network_id" {
  type = string
}

variable "nombre_noeuds" {
  description = "3 en production, 2 en staging (§4)."
  type        = number
}

variable "gabarit" {
  description = "Type d'instance OVH. b3-16 = 8 vCPU / 16 Go."
  type        = string
}

variable "cle_ssh_publique" {
  description = "Clé publique autorisée sur les nœuds. Le §19 interdit l'authentification par mot de passe."
  type        = string
}

variable "image" {
  type    = string
  default = "Ubuntu 24.04"
}

data "openstack_images_image_v2" "systeme" {
  name        = var.image
  most_recent = true
}

resource "openstack_compute_keypair_v2" "hba" {
  name       = "hba-${var.environnement}"
  public_key = var.cle_ssh_publique
}

resource "openstack_compute_instance_v2" "noeud" {
  count = var.nombre_noeuds

  name        = "hba-${var.environnement}-${count.index + 1}"
  image_id    = data.openstack_images_image_v2.systeme.id
  flavor_name = var.gabarit
  key_pair    = openstack_compute_keypair_v2.hba.name
  region      = var.region

  # Interface publique : c'est par là que passe l'Ingress, et RIEN d'autre.
  network { name = "Ext-Net" }

  # Interface privée : Postgres, Redis, Kafka, gRPC et le trafic entre nœuds.
  network { uuid = var.network_id }

  metadata = {
    environnement = var.environnement
    # Le premier nœud sera le serveur k3s ; les suivants, des agents. Ansible lit
    # cette étiquette plutôt que de déduire le rôle d'un numéro dans un nom.
    role = count.index == 0 ? "serveur" : "agent"
  }
}

output "noeuds" {
  description = "Ce que l'inventaire Ansible consomme."
  value = [
    for n in openstack_compute_instance_v2.noeud : {
      nom       = n.name
      role      = n.metadata.role
      ip_public = n.access_ip_v4
      # La seconde interface est la privée — l'ordre des blocs `network`
      # ci-dessus le garantit. C'est cette adresse que k3s doit annoncer, pas la
      # publique : sinon le trafic entre nœuds sortirait sur Internet et
      # reviendrait, en clair.
      ip_prive = try(n.network[1].fixed_ip_v4, null)
    }
  ]
}
