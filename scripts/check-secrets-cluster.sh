#!/usr/bin/env bash
# Vérifie que les Secrets nécessaires existent dans le namespace ciblé.
set -euo pipefail

ROOT_DIR="$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)"

usage() {
  cat >&2 <<'FIN'
usage: ./scripts/check-secrets-cluster.sh <staging|prod>

Contrôle :
  - Secret hba-platform présent ;
  - toutes les clés déclarées dans k8s/base/common/secret.yaml présentes ;
  - aucune clé requise vide ;
  - Secret hba-identites-internes présent ;
  - secret docker-registry ghcr présent.
FIN
  exit 2
}

CIBLE="${1:-}"
[ -n "$CIBLE" ] || usage

case "$CIBLE" in
  staging) NAMESPACE="hba-staging" ;;
  prod)    NAMESPACE="hba-prod" ;;
  *)       usage ;;
esac

if ! command -v kubectl >/dev/null 2>&1; then
  echo "kubectl introuvable" >&2
  exit 1
fi

CONTRAT="$ROOT_DIR/k8s/base/common/secret.yaml"
if [ ! -f "$CONTRAT" ]; then
  echo "contrat secret introuvable : $CONTRAT" >&2
  exit 1
fi

cles_contrat() {
  awk '
    /^[[:space:]]+[A-Z][A-Z0-9_]+:/ {
      gsub(":", "", $1)
      print $1
    }
  ' "$CONTRAT" | sort -u
}

cles_secret() {
  kubectl -n "$NAMESPACE" get secret hba-platform \
    -o jsonpath='{range $k,$v := .data}{$k}{"\n"}{end}' | sort -u
}

decoder_base64() {
  if base64 --decode >/dev/null 2>&1 <<<""; then
    base64 --decode
  else
    base64 -D
  fi
}

echo "Namespace : $NAMESPACE"

kubectl -n "$NAMESPACE" get secret hba-platform >/dev/null
kubectl -n "$NAMESPACE" get secret hba-identites-internes >/dev/null
kubectl -n "$NAMESPACE" get secret ghcr >/dev/null

MANQUANTES="$(
  comm -23 <(cles_contrat) <(cles_secret)
)"

if [ -n "$MANQUANTES" ]; then
  echo "clés absentes de hba-platform :" >&2
  echo "$MANQUANTES" >&2
  exit 1
fi

VIDES=0
while IFS= read -r cle; do
  valeur="$(kubectl -n "$NAMESPACE" get secret hba-platform -o "jsonpath={.data.${cle}}" | decoder_base64)"
  if [ -z "$valeur" ] && [ "$cle" != "CONNECTIONSTRINGS__DEFAULT" ]; then
    echo "clé vide : $cle" >&2
    VIDES=1
  fi
done < <(cles_contrat)

if [ "$VIDES" = "1" ]; then
  exit 1
fi

echo "Secrets OK"
