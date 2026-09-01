#!/usr/bin/env python3
# ==============================================================================
# L'INVENTAIRE DES WORKLOADS D'UN CALQUE RENDU : etiquette -> Deployment.
#
# Lit un rendu `kustomize build` sur l'entree standard, ou un fichier passe en
# argument, et rend une ligne par Deployment :
#
#     <valeur de app.kubernetes.io/name><TAB><nom du Deployment>
#
# POURQUOI CETTE INDIRECTION EXISTE.
#
# On ne peut pas deduire l'un de l'autre. Pour les dix-neuf services,
# l'etiquette vaut le nom du Deployment (`identity-service`). Pour la
# passerelle, l'etiquette vaut `api-gateway` et le Deployment se nomme
# `gateway-service` — parce que `apps/gateway` porte `namePrefix: gateway-` sur
# un objet de base nomme `service`.
#
# Coder cette exception en dur la ferait mentir au premier renommage. On la lit
# donc dans le rendu, qui est la seule source qui les connaisse toutes les deux.
#
# CE QUE CE SCRIPT NE COUVRE PAS. Il ne regarde que les `Deployment` : les
# StatefulSet (Redis, MinIO), le Kafka de Strimzi et les Jobs n'y figurent pas.
# Un deploiement service par service ne les touche pas — ils sont poses une fois
# par `apply -k` et vivent leur vie.
# ==============================================================================

import sys

try:
    import yaml
except ImportError:
    print("PyYAML est requis : pip install pyyaml", file=sys.stderr)
    sys.exit(1)

ETIQUETTE = "app.kubernetes.io/name"


def main():
    source = open(sys.argv[1], encoding="utf-8") if len(sys.argv) > 1 else sys.stdin

    trouves = {}
    sans_etiquette = []

    for doc in yaml.safe_load_all(source):
        if not doc or doc.get("kind") != "Deployment":
            continue
        meta = doc.get("metadata") or {}
        nom = meta.get("name")
        etiquette = (meta.get("labels") or {}).get(ETIQUETTE)
        if not nom:
            continue
        if not etiquette:
            sans_etiquette.append(nom)
            continue
        trouves[etiquette] = nom

    for nom in sorted(sans_etiquette):
        print("  Deployment sans %s, injoignable service par service : %s"
              % (ETIQUETTE, nom), file=sys.stderr)

    if not trouves:
        print("ANOMALIE aucun Deployment etiquete dans le rendu.", file=sys.stderr)
        return 1

    for etiquette in sorted(trouves):
        print("%s\t%s" % (etiquette, trouves[etiquette]))
    return 0


if __name__ == "__main__":
    sys.exit(main())
