#!/usr/bin/env bash
# ==============================================================================
# DEPLOIE OU MET A JOUR UN SEUL SERVICE EN PRODUCTION.
#
# POURQUOI CE SCRIPT EXISTE.
#
# `kubectl apply -k k8s/overlays/prod` pose les VINGT workloads d'un coup. C'est
# ce qu'on veut pour un premier deploiement ; c'est exactement ce qu'on ne veut
# pas pour corriger un service. Sans outil, la seule alternative etait
# d'appliquer tout le calque et d'esperer que les dix-neuf autres ne bougent pas
# — ce qui est vrai tant que rien d'autre n'a change dans l'arbre, donc vrai
# jusqu'au jour ou ca ne l'est plus.
#
# COMMENT IL ISOLE UN SERVICE.
#
# Le calque est rendu en entier, puis `kubectl apply -l` ne retient que les
# objets portant `app.kubernetes.io/name=<etiquette>`. Le Deployment, son
# Service, son HPA, son PDB et son ServiceAccount passent ; la ConfigMap, les
# Secrets et les NetworkPolicies — qui ne portent pas cette etiquette — sont
# ignores. C'est ce qui rend l'operation reellement locale.
#
# L'ETIQUETTE N'EST PAS TOUJOURS LE NOM DU DEPLOYMENT.
#
# Pour les dix-neuf services, les deux coincident. Pour la passerelle,
# l'etiquette vaut `api-gateway` et le Deployment se nomme `gateway-service`.
# `scripts/inventaire-workloads-prod.py` lit les deux dans le rendu plutot que
# de coder l'exception en dur.
#
# LE CLUSTER VISE EST VERIFIE AVANT TOUT.
#
# Le contexte `orbstack` du poste et celui de production repondent tous deux a
# `kubectl`. S'etre trompe de contexte ne rend AUCUNE erreur : l'objet est
# applique, ailleurs. Le script compare donc l'adresse du serveur d'API a celle
# du VPS, et refuse sinon.
#
# CE QUE CE SCRIPT NE COUVRE PAS :
#   - il ne migre RIEN. Un changement de schema passe par
#     `k8s/overlays/migrations-prod` (runbook, etape 11) ;
#   - il ne touche ni Redis, ni MinIO, ni Kafka, ni les Secrets ;
#   - il ne construit ni ne publie d'image. Voir `scripts/publier-images.sh` ;
#   - il ne dit pas si le service FONCTIONNE, seulement s'il demarre et se
#     declare pret. Une sonde verte n'est pas un appel metier reussi.
# ==============================================================================
set -euo pipefail

ROOT_DIR="$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)"
cd "$ROOT_DIR"

NAMESPACE="hba-prod"
CALQUE="k8s/overlays/prod"
PROPRIETAIRE="hectorberi01"
ADRESSE_ATTENDUE="79.137.35.129"
ATTENTE="5m"

usage() {
  cat >&2 <<'FINUSAGE'
usage: ./scripts/deployer-service-prod.sh <service> [--tag <sha>] [--attendre 5m]
       ./scripts/deployer-service-prod.sh --liste

  <service>        L'etiquette du service (ex. identity-service, api-gateway).
  --tag <sha>      Pose ce tag sur l'image de CE service avant d'appliquer.
  --attendre <d>   Delai du rollout. Defaut : 5m.
  --liste          Affiche les services deployables et rend la main.
FINUSAGE
  exit 2
}

SERVICE=""
TAG=""
LISTER=0

while [ $# -gt 0 ]; do
  case "$1" in
    --liste)    LISTER=1; shift ;;
    --tag)      TAG="${2:-}"; shift 2 ;;
    --attendre) ATTENTE="${2:-5m}"; shift 2 ;;
    -h|--help)  usage ;;
    -*)         echo "option inconnue : $1" >&2; usage ;;
    *)          SERVICE="$1"; shift ;;
  esac
done

for outil in kubectl kustomize python3; do
  command -v "$outil" >/dev/null 2>&1 || { echo "$outil introuvable" >&2; exit 1; }
done

# ------------------------------------------------------------------------------
# LE CLUSTER VISE — AVANT TOUT LE RESTE.
# ------------------------------------------------------------------------------
SERVEUR="$(kubectl config view --minify -o jsonpath='{.clusters[0].cluster.server}' 2>/dev/null || true)"
case "$SERVEUR" in
  *"$ADRESSE_ATTENDUE"*) ;;
  *)
    echo "REFUS : le contexte kubectl courant ne vise pas la production." >&2
    echo "        serveur   : ${SERVEUR:-<aucun>}" >&2
    echo "        attendu   : une adresse contenant $ADRESSE_ATTENDUE" >&2
    echo "        contexte  : $(kubectl config current-context 2>/dev/null || echo '<aucun>')" >&2
    echo "        kubectl config use-context hba-prod" >&2
    exit 1
    ;;
esac

RENDU="$(mktemp)"
trap 'rm -f "$RENDU"' EXIT

# ------------------------------------------------------------------------------
# LE TAG, POSE SUR CE SERVICE SEULEMENT.
#
# `kustomize edit set image` ne touche QUE l'entree nommee — c'est ce qu'emploie
# la CD. Un `sed` global poserait le tag sur les vingt-et-une images, et
# redeploierait donc tout au prochain `apply -k`.
# ------------------------------------------------------------------------------
if [ -n "$TAG" ]; then
  [ -n "$SERVICE" ] || { echo "--tag exige un service" >&2; usage; }
  ( cd "$CALQUE" && kustomize edit set image \
      "hba/$SERVICE=ghcr.io/$PROPRIETAIRE/$SERVICE:$TAG" )
  echo "tag pose sur hba/$SERVICE : $TAG"
  echo "  (le calque est modifie — ne pas committer ce tag, voir le 13)"
fi

kustomize build "$CALQUE" > "$RENDU"

INVENTAIRE="$(python3 scripts/inventaire-workloads-prod.py "$RENDU")"

if [ "$LISTER" = 1 ] || [ -z "$SERVICE" ]; then
  echo "Services deployables (etiquette -> Deployment) :"
  printf '%s\n' "$INVENTAIRE" | sed 's/^/  /'
  [ "$LISTER" = 1 ] && exit 0
  usage
fi

DEPLOIEMENT="$(printf '%s\n' "$INVENTAIRE" | awk -F'\t' -v s="$SERVICE" '$1 == s {print $2}')"

if [ -z "$DEPLOIEMENT" ]; then
  echo "REFUS : « $SERVICE » n'est pas un service deployable de ce calque." >&2
  echo "        Les services ecartes du lot (notification-service," >&2
  echo "        return-refund-service) sont commentes dans" >&2
  echo "        k8s/base/services/kustomization.yaml, avec la raison." >&2
  echo >&2
  echo "Disponibles :" >&2
  printf '%s\n' "$INVENTAIRE" | awk -F'\t' '{print "  " $1}' >&2
  exit 1
fi

echo
echo "Service     : $SERVICE"
echo "Deployment  : $DEPLOIEMENT"
echo "Namespace   : $NAMESPACE"
echo "Serveur     : $SERVEUR"
echo

# ------------------------------------------------------------------------------
# ESSAI A BLANC COTE SERVEUR, PUIS APPLICATION.
#
# Le dry-run valide chaque objet contre les CRD reellement installees. Un echec
# ici ne coute rien ; le meme echec apres application laisse un Deployment a
# moitie mis a jour.
# ------------------------------------------------------------------------------
echo "-- essai a blanc"
kubectl apply -n "$NAMESPACE" -f "$RENDU" -l "app.kubernetes.io/name=$SERVICE" \
  --dry-run=server > /dev/null
echo "   accepte par le serveur"

echo "-- application"
kubectl apply -n "$NAMESPACE" -f "$RENDU" -l "app.kubernetes.io/name=$SERVICE"

echo
echo "-- attente du rollout (${ATTENTE})"
if kubectl -n "$NAMESPACE" rollout status "deploy/$DEPLOIEMENT" --timeout="$ATTENTE"; then
  echo
  kubectl -n "$NAMESPACE" get pods -l "app.kubernetes.io/name=$SERVICE" -o wide
  echo
  echo "$SERVICE : deploye et pret."
  echo "Une sonde verte n'est pas un appel metier reussi — eprouvez une route."
  exit 0
fi

# ------------------------------------------------------------------------------
# L'ECHEC EST LE CAS QUI COMPTE : on rend tout de suite de quoi comprendre.
# ------------------------------------------------------------------------------
echo
echo "═══ $SERVICE N'EST PAS PARTI ═══" >&2
echo
echo "-- etat des pods"
kubectl -n "$NAMESPACE" get pods -l "app.kubernetes.io/name=$SERVICE" -o wide || true
echo
echo "-- evenements"
kubectl -n "$NAMESPACE" describe pod -l "app.kubernetes.io/name=$SERVICE" \
  | sed -n '/Events:/,$p' | tail -25 || true
echo
echo "-- journaux (30 dernieres lignes)"
kubectl -n "$NAMESPACE" logs -l "app.kubernetes.io/name=$SERVICE" --tail=30 \
  --all-containers 2>/dev/null || true
echo
echo "Le message d'exception est en TETE du journal, pas en queue :" >&2
echo "  kubectl -n $NAMESPACE logs deploy/$DEPLOIEMENT | head -30" >&2
exit 1
