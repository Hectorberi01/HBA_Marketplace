# ═══════════════════════════════════════════════════════════════════════════════
# RÉSEAU PRIVÉ (§1, §5).
#
# TOUT CE QUI N'EST PAS L'INGRESS RESTE ICI.
#
# Le §1 est explicite : « seuls l'Ingress/API Gateway et les endpoints
# explicitement publics sont exposés à Internet. PostgreSQL, Redis, Kafka et les
# ports gRPC restent sur le réseau privé du cluster ».
#
# Le réseau privé OVH (vRack) est la première des deux barrières ; les
# NetworkPolicies de `k8s/base/policies/` sont la seconde. Aucune ne suffit seule :
# le vRack ne dit rien de ce qui se parle À L'INTÉRIEUR, et les NetworkPolicies ne
# protègent pas d'une IP publique laissée sur une VM.
# ═══════════════════════════════════════════════════════════════════════════════

terraform {
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
}

variable "service_name" {
  description = "Identifiant du projet Public Cloud OVH."
  type        = string
}

variable "environnement" {
  description = "staging ou production."
  type        = string
}

variable "region" {
  description = "Région OVH. GRA (Gravelines) est le POP le plus proche du Bénin en latence."
  type        = string
  default     = "GRA11"
}

variable "cidr" {
  description = "Plage privée du réseau."
  type        = string
  default     = "10.0.0.0/16"
}

resource "ovh_cloud_project_network_private" "hba" {
  service_name = var.service_name
  name         = "hba-${var.environnement}"
  regions      = [var.region]
  vlan_id      = var.environnement == "production" ? 100 : 200
}

resource "ovh_cloud_project_network_private_subnet" "hba" {
  service_name = var.service_name
  network_id   = ovh_cloud_project_network_private.hba.id
  region       = var.region

  start   = cidrhost(var.cidr, 10)
  end     = cidrhost(var.cidr, 200)
  network = var.cidr

  # PAS DE PASSERELLE PAR DÉFAUT SUR LE RÉSEAU PRIVÉ.
  #
  # Les nœuds sortent par leur interface publique, filtrée en amont. Une
  # passerelle ici créerait un second chemin de sortie, non filtré, que personne
  # ne surveillerait.
  no_gateway = true
  dhcp       = true
}

output "network_id" {
  value = ovh_cloud_project_network_private.hba.id
}

output "subnet_id" {
  value = ovh_cloud_project_network_private_subnet.hba.id
}

output "cidr" {
  value = var.cidr
}
