#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)"
cd "$ROOT_DIR"

usage() {
  cat >&2 <<'FIN'
usage: ./scripts/preflight-k8s.sh <dev|staging|prod> [--cluster]

Contrôle l'overlay Kubernetes avant un déploiement.

  --cluster   ajoute les contrôles qui parlent au cluster courant :
              secrets présents, dry-run serveur et accès au namespace.

À lancer après une promotion Git pour prod, avant `kubectl apply -k`.
FIN
  exit 2
}

ENVIRONNEMENT="${1:-}"
[ -n "$ENVIRONNEMENT" ] || usage
shift || true

AVEC_CLUSTER=0
for arg in "$@"; do
  case "$arg" in
    --cluster) AVEC_CLUSTER=1 ;;
    *) echo "option inconnue : $arg" >&2; usage ;;
  esac
done

case "$ENVIRONNEMENT" in
  dev)     NAMESPACE="hba-dev";     OVERLAY="k8s/overlays/dev" ;;
  staging) NAMESPACE="hba-staging"; OVERLAY="k8s/overlays/staging" ;;
  prod)    NAMESPACE="hba-prod";    OVERLAY="k8s/overlays/prod" ;;
  *) echo "environnement inconnu : $ENVIRONNEMENT" >&2; usage ;;
esac

PYTHON="$ROOT_DIR/.venv/bin/python3"
[ -x "$PYTHON" ] || PYTHON="python3"

titre() { printf '\n\033[1m── %s ──\033[0m\n' "$*"; }
ok()    { printf '\033[32m✓ %s\033[0m\n' "$*"; }
ko()    { printf '\033[31m✗ %s\033[0m\n' "$*" >&2; }

TMP="$(mktemp)"
trap 'rm -f "$TMP"' EXIT

titre "Rendu Kustomize"
kustomize build "$OVERLAY" >"$TMP"
ok "$OVERLAY se construit"

titre "Contrôles statiques"
"$PYTHON" scripts/check-k8s.py "$ENVIRONNEMENT"
"$PYTHON" scripts/check-infra.py
ok "contrôles dépôt au vert"

titre "Images"
if [ "$ENVIRONNEMENT" = "prod" ]; then
  if grep -q "REMPLACE-PAR-LA-PROMOTION" "$TMP"; then
    ko "l'overlay prod porte encore le placeholder REMPLACE-PAR-LA-PROMOTION"
    echo "Lancer d'abord la promotion CI/CD ou scripts/poser-tag-prod.py <sha>." >&2
    exit 1
  fi
fi

if grep -Eq 'image: .*:(latest|main)$' "$TMP" && [ "$ENVIRONNEMENT" = "prod" ]; then
  ko "prod contient encore une image mouvante (:latest ou :main)"
  exit 1
fi
ok "tags compatibles avec $ENVIRONNEMENT"

titre "DNS"
if [ "$ENVIRONNEMENT" = "dev" ]; then
  ok "DNS ignoré en dev : l'Ingress utilise un domaine .example non publié"
else
  ./scripts/check-dns-ingress.sh "$ENVIRONNEMENT"
  ok "DNS cohérent avec l'Ingress"
fi

if [ "$AVEC_CLUSTER" = "1" ]; then
  titre "Cluster"
  kubectl get namespace "$NAMESPACE" >/dev/null
  if [ "$ENVIRONNEMENT" != "dev" ]; then
    ./scripts/check-secrets-cluster.sh "$ENVIRONNEMENT"
  fi
  kubectl apply --dry-run=server -k "$OVERLAY" >/dev/null
  ok "namespace, secrets et validation serveur OK"
fi

ok "pré-vol $ENVIRONNEMENT terminé"
