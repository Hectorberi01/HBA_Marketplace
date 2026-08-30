# ═══════════════════════════════════════════════════════════════════════════════
# DNS ET CERTIFICATS (§6).
#
# LE DOMAINE DOIT DÉJÀ EXISTER CHEZ OVH.
#
# Ce module crée des ENREGISTREMENTS, pas une zone : `ovh_domain_zone_record`
# suppose la zone déjà déléguée. Terraform échouera clairement si ce n'est pas le
# cas — c'est le bon comportement, acheter un domaine n'est pas une opération
# qu'on veut voir dans un `apply`.
#
# CE MODULE NE POSE AUCUN CERTIFICAT.
#
# Le §6 les fait renouveler par cert-manager/ACME, DANS le cluster. Terraform
# n'intervient pas : un certificat géré ici expirerait sans que rien ne le
# renouvelle entre deux `apply`.
#
# Ce qui compte donc ici : que le A pointe le bon nœud AVANT le premier challenge
# ACME. Un challenge HTTP-01 sur un DNS qui ne résout pas encore échoue, et
# cert-manager réessaie avec un recul croissant — jusqu'à une heure d'attente pour
# un enregistrement posé deux minutes trop tard.
# ═══════════════════════════════════════════════════════════════════════════════

terraform {
  required_providers {
    ovh = {
      source  = "ovh/ovh"
      version = "~> 1.5"
    }
  }
}

variable "zone" {
  description = "Domaine racine déjà délégué chez OVH, ex. hba-express.bj."
  type        = string
}

variable "sous_domaine" {
  description = "Sous-domaine publie par l'Ingress, ex. « api » en production ou « backendapi.marketplace-staging » en staging."
  type        = string
}

variable "ip_ingress" {
  description = "IP publique du nœud qui porte l'Ingress Controller."
  type        = string
}

resource "ovh_domain_zone_record" "api" {
  zone      = var.zone
  subdomain = var.sous_domaine
  fieldtype = "A"
  target    = var.ip_ingress

  # TTL COURT — 300 s.
  #
  # Le §24 prévoit la perte d'un nœud Kubernetes. Si l'Ingress bascule, le TTL est
  # le temps pendant lequel les clients continuent d'appeler une machine morte.
  # Une heure de TTL rendrait le RTO de 60 minutes du §17 inatteignable par
  # construction.
  ttl = 300
}

# LES ADMINISTRATIONS NE SONT PAS SUR INTERNET (§5, §6).
#
# Grafana et Argo CD passent par « internal », accessible seulement par VPN ou
# bastion. Le §6 les liste séparément de `api.` pour cette raison, et le §19
# demande « accès administrateur via VPN/bastion/SSO + MFA ».
resource "ovh_domain_zone_record" "grafana" {
  zone      = var.zone
  subdomain = "grafana.internal"
  fieldtype = "A"
  target    = var.ip_ingress
  ttl       = 300
}

output "fqdn" {
  value = "${var.sous_domaine}.${var.zone}"
}
