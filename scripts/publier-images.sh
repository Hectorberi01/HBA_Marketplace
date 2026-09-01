#!/usr/bin/env bash
# ==============================================================================
# CONSTRUIT ET PUBLIE LES IMAGES DEPUIS UN POSTE, VERS ghcr.io.
#
# POURQUOI CE SCRIPT EXISTE.
#
# La CI construit les images avec `docker/build-push-action`, qui ne se rejoue
# pas depuis un poste. Quand la barriere de tests bloque et qu'il faut deployer
# malgre tout, il n'existait AUCUN chemin manuel — donc chacun improvise une
# boucle `docker build` a la main, avec les deux erreurs qui suivent.
#
# L'ARCHITECTURE EST LE PIEGE PRINCIPAL, ET IL EST MUET A LA CONSTRUCTION.
#
# Un Mac Apple Silicon construit en arm64 par defaut. Le VPS est en amd64. Une
# image arm64 se construit sans erreur, se pousse sans erreur, et le pod meurt
# au demarrage sur « exec format error » — un message qui ne nomme jamais
# l'architecture. D'ou `--platform linux/amd64`, non negociable ici.
#
# LA LISTE DES SERVICES EST DERIVEE, PAS RECOPIEE.
#
# Elle vient de `scripts/ci-affected.py --tous`, la source qu'emploie la CI, et
# elle est CONFRONTEE aux images declarees par `k8s/overlays/prod`. Une
# divergence arrete le script : publier vingt images pour un calque qui en
# reclame vingt-et-une laisse un pod en ImagePullBackOff, sur un message qui
# parle de droits sur le registre.
#
# LE TAG EST LE SHA DU COMMIT, ET L'ARBRE DOIT ETRE PROPRE.
#
# Le §13 impose une image immuable identifiee par SHA. Publier depuis un arbre
# modifie produirait des images dont le tag nomme un commit qui ne contient pas
# ce qu'elles portent : la trace entre le deploye et le source serait rompue,
# sans que rien ne le signale.
#
# CE QUE CE SCRIPT NE COUVRE PAS :
#   - il NE SIGNE PAS les images. La CI le fait en keyless via l'identite OIDC
#     du workflow, ce qu'un poste ne peut pas produire. Les images publiees
#     d'ici seront donc REFUSEES par la verification cosign de `cd.yml` et de
#     `deploy-branches.yml` — ce chemin est manuel de bout en bout ;
#   - il ne deploie rien. Voir `docs/RUNBOOK-K3S.md`, etapes 9 a 12 ;
#   - il ne remplace pas la barriere de tests. Ce qu'il publie n'a ete valide
#     que par ce que vous avez lance vous-meme.
# ==============================================================================
set -euo pipefail

ROOT_DIR="$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)"
cd "$ROOT_DIR"

REGISTRE="ghcr.io"
PROPRIETAIRE="hectorberi01"
PLATEFORME="linux/amd64"

usage() {
  cat >&2 <<'FINUSAGE'
usage: ./scripts/publier-images.sh [--tag <valeur>] [--seulement a,b] [--sans-pousser] [--oui]

Construit les images des services du calque prod et les pousse sur ghcr.io.

  --tag <valeur>       Tag a poser. Defaut : le SHA court du HEAD.
  --seulement a,b,c    Ne traite que ces services.
  --sans-pousser       Construit sans publier — pour eprouver la construction.
  --oui                N'attend pas la confirmation.

« latest » est refuse (§13). L'arbre doit etre propre, sauf --tag explicite.
FINUSAGE
  exit 2
}

TAG=""
SEULEMENT=""
POUSSER=1
CONFIRMER=1

while [ $# -gt 0 ]; do
  case "$1" in
    --tag)          TAG="${2:-}"; shift 2 ;;
    --seulement)    SEULEMENT="${2:-}"; shift 2 ;;
    --sans-pousser) POUSSER=0; shift ;;
    --oui)          CONFIRMER=0; shift ;;
    -h|--help)      usage ;;
    *)              echo "option inconnue : $1" >&2; usage ;;
  esac
done

for outil in docker git python3; do
  command -v "$outil" >/dev/null 2>&1 || { echo "$outil introuvable" >&2; exit 1; }
done

docker buildx version >/dev/null 2>&1 || {
  echo "REFUS : docker buildx est absent." >&2
  echo "        Sans lui, --platform est ignore et les images seraient arm64." >&2
  exit 1
}

if [ -z "$TAG" ]; then
  if [ -n "$(git status --porcelain)" ]; then
    echo "REFUS : l'arbre de travail n'est pas propre." >&2
    echo "        Le tag serait le SHA d'un commit qui ne contient pas ce que" >&2
    echo "        les images porteraient. Committez, ou passez --tag <valeur>." >&2
    exit 1
  fi
  TAG="$(git rev-parse --short=12 HEAD)"
fi

if [ "$TAG" = "latest" ]; then
  echo "REFUS : « latest » est interdit en production (§13)." >&2
  exit 1
fi

LISTE="$(python3 scripts/publier-images-liste.py "$SEULEMENT")"

NOMBRE="$(printf '%s\n' "$LISTE" | grep -c . || true)"
[ "$NOMBRE" -gt 0 ] || { echo "aucun service a construire" >&2; exit 1; }

echo
echo "Registre    : $REGISTRE/$PROPRIETAIRE"
echo "Tag         : $TAG"
echo "Plateforme  : $PLATEFORME  (le VPS est en amd64)"
echo "Services    : $NOMBRE"
echo "Publication : $([ "$POUSSER" = 1 ] && echo OUI || echo "non - construction seule")"
echo

if [ "$CONFIRMER" = 1 ]; then
  printf "Continuer ? [o/N] "
  read -r reponse
  case "$reponse" in o|O|oui|OUI) ;; *) echo "abandon."; exit 0 ;; esac
fi

# LE JETON EST LU SUR L'ENTREE STANDARD, JAMAIS PASSE EN ARGUMENT : un argument
# est visible dans la table des processus de toute la machine pendant la duree
# de la commande, et reste dans l'historique du shell.
if [ "$POUSSER" = 1 ]; then
  if ! docker login "$REGISTRE" </dev/null >/dev/null 2>&1; then
    echo "Connexion a $REGISTRE (jeton GitHub, portee write:packages)"
    printf "Jeton : "
    stty -echo 2>/dev/null || true
    read -r JETON
    stty echo 2>/dev/null || true
    echo
    printf '%s' "$JETON" | docker login "$REGISTRE" -u "$PROPRIETAIRE" --password-stdin
    unset JETON
  fi
fi

ECHECS=""
FAITS=0
TAB="$(printf '\t')"

while IFS="$TAB" read -r service dockerfile; do
  [ -n "$service" ] || continue
  image="$REGISTRE/$PROPRIETAIRE/$service:$TAG"
  echo
  echo "-- $service  ($dockerfile)"

  if [ ! -f "$dockerfile" ]; then
    echo "  ECHEC : $dockerfile introuvable" >&2
    ECHECS="$ECHECS $service"
    continue
  fi

  if [ "$POUSSER" = 1 ]; then DESTINATION="--push"; else DESTINATION="--load"; fi

  # `--provenance=false` : l'attestation cree un index multi-plateforme, et un
  # kubelet ancien refuse alors de tirer l'image. La CI la pose parce que son
  # registre et son runtime la comprennent ; ici on veut une image simple.
  if docker buildx build \
       --platform "$PLATEFORME" \
       --file "$dockerfile" \
       --tag "$image" \
       --provenance=false \
       "$DESTINATION" \
       . ; then
    FAITS=$((FAITS + 1))
    echo "  ok : $image"
  else
    ECHECS="$ECHECS $service"
    echo "  ECHEC : $service" >&2
  fi
done <<FINLISTE
$LISTE
FINLISTE

echo
echo "==============================================================="
echo "$FAITS / $NOMBRE image(s) traitee(s), tag « $TAG »."

if [ -n "$ECHECS" ]; then
  echo "ECHECS :$ECHECS" >&2
  echo "Ne rien deployer tant qu'une image manque : le calque prod les reclame" >&2
  echo "toutes, et celle qui manque met son pod en ImagePullBackOff." >&2
  exit 1
fi

if [ "$POUSSER" = 1 ]; then
  echo
  echo "Poser ce tag, puis deployer :"
  echo
  echo "  for calque in k8s/overlays/prod k8s/overlays/migrations-prod; do"
  echo "    (cd \"\$calque\" && sed -i.bak \"s/REMPLACE-PAR-LA-PROMOTION/$TAG/g\" kustomization.yaml && rm -f kustomization.yaml.bak)"
  echo "  done"
  echo
  echo "  kubectl apply -k k8s/overlays/migrations-prod"
  echo "  kubectl -n hba-prod wait --for=condition=complete job --all --timeout=15m"
  echo "  kubectl apply -k k8s/overlays/prod"
  echo
  echo "Ces images ne sont PAS signees : la CD les refuserait. Chemin manuel."
fi
