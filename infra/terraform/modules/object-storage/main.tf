# ═══════════════════════════════════════════════════════════════════════════════
# STOCKAGE OBJET (§11, §18).
#
# DEUX USAGES DISTINCTS, ET IL NE FAUT PAS LES CONFONDRE.
#
#   • les MÉDIAS applicatifs — images produit, pièces vendeur, preuves de
#     livraison — vivent dans MinIO, DANS le cluster (voir `k8s/base/data/minio`) ;
#   • les SAUVEGARDES Postgres vivent ICI, chez OVH, HORS du cluster.
#
# C'EST LA SECONDE QUI JUSTIFIE CE MODULE.
#
# Sauvegarder le cluster dans le cluster ne sauvegarde rien : le PITR
# fonctionnerait parfaitement jusqu'au jour où l'on en a besoin, c'est-à-dire le
# jour où le cluster est perdu. C'est le seul endroit de l'infrastructure où l'on
# paie délibérément une dépendance externe.
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
  default = "GRA"
}

resource "openstack_objectstorage_container_v1" "sauvegardes" {
  name   = "hba-${var.environnement}-backups"
  region = var.region

  # VERSIONING (§18) — ET IL PROTÈGE D'AUTRE CHOSE QUE D'UNE PANNE.
  #
  # Une panne de disque est déjà couverte par la réplication du fournisseur. Ce
  # que le versioning couvre, c'est l'ERREUR HUMAINE et le rançongiciel : un
  # `rm -rf` sur le bucket, ou un chiffrement malveillant, laissent les versions
  # antérieures intactes.
  versioning = true

  metadata = {
    environnement = var.environnement
    usage         = "postgres-wal-et-basebackup"
  }
}

resource "openstack_objectstorage_container_v1" "etat_terraform" {
  name   = "hba-${var.environnement}-tfstate"
  region = var.region

  # L'ÉTAT TERRAFORM CONTIENT DES SECRETS EN CLAIR.
  #
  # Mots de passe générés, clés d'accès, jetons : tout ce qu'une ressource rend
  # est stocké tel quel. D'où un bucket dédié, versionné, et jamais Git.
  versioning = true
}

output "bucket_sauvegardes" {
  value = openstack_objectstorage_container_v1.sauvegardes.name
}

output "bucket_etat" {
  value = openstack_objectstorage_container_v1.etat_terraform.name
}
