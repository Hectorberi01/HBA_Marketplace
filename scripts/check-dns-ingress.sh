#!/usr/bin/env bash
# Vérifie que l'hôte Ingress d'un overlay résout vers l'IP attendue.
set -euo pipefail

ROOT_DIR="$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)"

usage() {
  cat >&2 <<'FIN'
usage: ./scripts/check-dns-ingress.sh <staging|prod> [--cluster]

Variables optionnelles :
  HBA_EXPECTED_INGRESS_IP   IP attendue. Par défaut : staging=193.168.145.162,
                            prod=79.137.35.129.

--cluster ajoute les contrôles kubectl : Ingress présent et certificat TLS Ready.
FIN
  exit 2
}

CIBLE="${1:-}"
[ -n "$CIBLE" ] || usage
shift || true

VERIFIER_CLUSTER=0
for arg in "$@"; do
  case "$arg" in
    --cluster) VERIFIER_CLUSTER=1 ;;
    *) usage ;;
  esac
done

case "$CIBLE" in
  staging)
    OVERLAY="$ROOT_DIR/k8s/overlays/staging"
    NAMESPACE="hba-staging"
    IP_ATTENDUE="${HBA_EXPECTED_INGRESS_IP:-193.168.145.162}"
    ;;
  prod)
    OVERLAY="$ROOT_DIR/k8s/overlays/prod"
    NAMESPACE="hba-prod"
    IP_ATTENDUE="${HBA_EXPECTED_INGRESS_IP:-79.137.35.129}"
    ;;
  *)
    usage
    ;;
esac

if ! command -v kustomize >/dev/null 2>&1; then
  echo "kustomize introuvable" >&2
  exit 1
fi

if ! command -v dig >/dev/null 2>&1; then
  echo "dig introuvable" >&2
  exit 1
fi

RENDU="$(mktemp)"
trap 'rm -f "$RENDU"' EXIT
kustomize build "$OVERLAY" > "$RENDU"

HOTE="$(
  awk '
    $1 == "host:" { print $2; exit }
    $1 == "-" && $2 == "host:" { print $3; exit }
  ' "$RENDU"
)"

if [ -z "$HOTE" ]; then
  echo "aucun host Ingress trouvé dans $OVERLAY" >&2
  exit 1
fi

echo "Overlay    : $CIBLE"
echo "Namespace  : $NAMESPACE"
echo "Ingress    : $HOTE"
echo "IP attendue: $IP_ATTENDUE"

IPS="$(dig +short A "$HOTE" | sed '/^$/d' | sort -u)"
if [ -z "$IPS" ]; then
  echo "DNS KO : $HOTE ne résout pas" >&2
  exit 1
fi

echo "DNS        : $(echo "$IPS" | tr '\n' ' ')"

if ! printf '%s\n' "$IPS" | grep -Fxq "$IP_ATTENDUE"; then
  echo "DNS KO : $HOTE ne pointe pas vers $IP_ATTENDUE" >&2
  exit 1
fi

if [ "$VERIFIER_CLUSTER" = "1" ]; then
  if ! command -v kubectl >/dev/null 2>&1; then
    echo "kubectl introuvable" >&2
    exit 1
  fi

  kubectl -n "$NAMESPACE" get ingress

  CERTIFICATS="$(kubectl -n "$NAMESPACE" get certificate -o jsonpath='{range .items[*]}{.metadata.name}{" "}{.status.conditions[?(@.type=="Ready")].status}{"\n"}{end}' 2>/dev/null || true)"
  if [ -z "$CERTIFICATS" ]; then
    echo "aucun Certificate cert-manager trouvé dans $NAMESPACE" >&2
    exit 1
  fi

  echo "$CERTIFICATS"
  if echo "$CERTIFICATS" | awk '{ if ($2 != "True") bad=1 } END { exit bad }'; then
    :
  else
    echo "certificat TLS non Ready" >&2
    exit 1
  fi
fi

echo "DNS/Ingress OK"
