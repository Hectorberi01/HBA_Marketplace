#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat >&2 <<'FIN'
usage: ./scripts/rollback-k8s.sh <staging|prod> <service> [revision]

Exemples :
  ./scripts/rollback-k8s.sh staging delivery-service
  ./scripts/rollback-k8s.sh prod gateway-service 12

Le nom du service est le nom du Deployment Kubernetes.
FIN
  exit 2
}

ENVIRONNEMENT="${1:-}"
SERVICE="${2:-}"
REVISION="${3:-}"
[ -n "$ENVIRONNEMENT" ] && [ -n "$SERVICE" ] || usage

case "$ENVIRONNEMENT" in
  staging) NAMESPACE="hba-staging" ;;
  prod)    NAMESPACE="hba-prod" ;;
  *) echo "environnement inconnu : $ENVIRONNEMENT" >&2; usage ;;
esac

if [ "$SERVICE" = "all" ] || [ "$SERVICE" = "*" ]; then
  echo "Rollback global refusé : revenir service par service, dans l'ordre de l'incident." >&2
  exit 1
fi

kubectl -n "$NAMESPACE" get deploy "$SERVICE" >/dev/null

echo "Historique de $SERVICE dans $NAMESPACE :"
kubectl -n "$NAMESPACE" rollout history "deploy/$SERVICE"

if [ -n "$REVISION" ]; then
  kubectl -n "$NAMESPACE" rollout undo "deploy/$SERVICE" --to-revision="$REVISION"
else
  kubectl -n "$NAMESPACE" rollout undo "deploy/$SERVICE"
fi

kubectl -n "$NAMESPACE" rollout status "deploy/$SERVICE" --timeout=180s

if [ "$ENVIRONNEMENT" = "prod" ]; then
  cat >&2 <<'FIN'

ATTENTION : le cluster vient de revenir en arrière, mais Git ne le sait pas.
Pour rendre la production reproductible, promouvoir ensuite le SHA restauré dans
k8s/overlays/prod et k8s/overlays/migrations-prod, puis commiter cette promotion.
FIN
fi
