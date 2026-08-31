#!/usr/bin/env bash
# ==============================================================================
# VÉRIFIE QUE LES SECRETS NÉCESSAIRES EXISTENT ET SONT REMPLIS DANS LE NAMESPACE.
#
# CE CONTRÔLE NE COUVRAIT QU'UN SECRET SUR QUATRE.
#
# Il exigeait la présence de `hba-identites-internes` sans jamais regarder son
# CONTENU, et ignorait complètement `hba-paiements` et `minio`. Or c'est
# exactement là que se logent les pannes muettes de cette plateforme :
#
#   • une clé `INTERNAL_KEY_*` absente → le pod démarre, et chacun de ses appels
#     gRPC revient en `FailedPrecondition: Internal identity not configured.`
#     CHEZ L'APPELANT — le service fautif n'apparaît dans aucun journal ;
#   • `ADMIN__PASSWORD` absent → identity-service LÈVE au démarrage, ce qui est
#     le bon échec, mais après le déploiement de dix-huit autres services ;
#   • `hba-paiements` absent → payment-service refuse de démarrer, et le message
#     parle de PSP, pas de Secret ;
#   • `minio` vide → MinIO démarre avec des identifiants vides et media-service
#     échoue à l'envoi, pas au démarrage ;
#   • `redis` absent → Redis reste en `CreateContainerConfigError`, son Service
#     n'a aucun endpoint, et ce sont les DIX-NEUF services qu'on voit échouer.
#
# Les quatre sont donc contrôlés ici, contre le contrat que porte le dépôt :
# les fichiers `k8s/base/common/secret*.yaml` déclarent les clés attendues avec
# une valeur vide, et ce script compare cette liste à ce que le cluster porte.
#
# CE QUE CE CONTRÔLE NE COUVRE PAS. Il vérifie qu'une clé est PRÉSENTE et NON
# VIDE. Il ne peut pas dire qu'une valeur est la BONNE : un mot de passe faux,
# une clé privée qui ne correspond pas à la publique publiée, une chaîne de
# connexion qui pointe la mauvaise base passent tous ce contrôle. Il ne vérifie
# pas non plus `hba-notifications` : notification-service n'est pas dans le lot
# déployé (il lui manque un adaptateur SMS, ce qui est du code, pas de la
# configuration).
#
# AUCUNE VALEUR N'EST IMPRIMÉE. Le script ne rend que des NOMS de clés — c'est
# une invariante, pas une commodité : sa sortie finit dans des journaux de CI.
# ==============================================================================
set -euo pipefail

ROOT_DIR="$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)"

usage() {
  cat >&2 <<'FIN'
usage: ./scripts/check-secrets-cluster.sh <staging|prod>

Contrôle, dans le namespace ciblé :
  - hba-platform             — toutes les clés de k8s/base/common/secret.yaml,
                               aucune vide (sauf CONNECTIONSTRINGS__DEFAULT) ;
  - hba-identites-internes   — toutes les clés de secret-identites.yaml ;
  - hba-paiements            — toutes les clés de secret-paiements.yaml ;
  - minio                    — root-user et root-password ;
  - redis                    — password ;
  - ghcr                     — présence du secret docker-registry.

Aucune valeur n'est imprimée : la sortie ne contient que des noms de clés.
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

# Les clés déclarées par un fichier de contrat. Le motif exige une MAJUSCULE
# juste après l'indentation : les lignes de commentaire (`#`) et les champs de
# métadonnées (`name:`, `type:`) ne peuvent pas y répondre.
cles_contrat() {
  awk '
    /^[[:space:]]+[A-Z][A-Z0-9_]+:/ {
      gsub(":", "", $1)
      print $1
    }
  ' "$1" | sort -u
}

# LISTER LES CLES D'UN Secret DEMANDE go-template, PAS jsonpath.
#
# CETTE FONCTION RENDAIT UNE LISTE VIDE, ET LE CONTROLE CONCLUAIT « TOUT MANQUE ».
#
# `{range $k,$v := .data}` est de la syntaxe go-template. Passee a `-o jsonpath`,
# kubectl refuse la virgule (« unrecognized character in action: U+002C »),
# ecrit son erreur sur la sortie d'erreur, et rend une sortie VIDE sur la sortie
# standard. `comm -23` comparait donc le contrat a rien : les vingt-cinq cles
# etaient annoncees absentes d'un Secret qui les portait toutes.
#
# LE PIRE DEFAUT POSSIBLE POUR UN CONTROLE : il criait au lieu de se taire.
# Un faux positif de cette ampleur, la veille d'une mise en production, envoie
# refaire des Secrets qui etaient bons — et fait douter de l'outil au moment ou
# on en a le plus besoin.
#
# `2>/dev/null` est volontairement ABSENT : si la commande echoue a nouveau un
# jour, on veut voir pourquoi, pas une liste vide silencieuse.
cles_secret() {
  kubectl -n "$NAMESPACE" get secret "$1" \
    -o go-template='{{range $k,$v := .data}}{{$k}}{{"\n"}}{{end}}' | sort -u
}

decoder_base64() {
  if base64 --decode >/dev/null 2>&1 <<<""; then
    base64 --decode
  else
    base64 -D
  fi
}

ECHECS=0

signaler() {
  echo "  ÉCHEC : $1" >&2
  ECHECS=1
}

# ------------------------------------------------------------------------------
# Contrôle d'un Secret contre son fichier de contrat.
#   $1 = nom du Secret dans le cluster
#   $2 = chemin du fichier de contrat
#   $3 = clés tolérées vides, séparées par des espaces (peut être vide)
# ------------------------------------------------------------------------------
verifier_secret() {
  nom="$1"
  contrat="$2"
  tolerees=" ${3:-} "

  echo "── $nom"

  if [ ! -f "$contrat" ]; then
    signaler "contrat introuvable : ${contrat#"$ROOT_DIR"/}"
    return
  fi

  if ! kubectl -n "$NAMESPACE" get secret "$nom" >/dev/null 2>&1; then
    signaler "le Secret « $nom » n'existe pas dans $NAMESPACE"
    return
  fi

  manquantes="$(comm -23 <(cles_contrat "$contrat") <(cles_secret "$nom") || true)"
  if [ -n "$manquantes" ]; then
    echo "  clés absentes de $nom :" >&2
    echo "$manquantes" | sed 's/^/    /' >&2
    ECHECS=1
  fi

  while IFS= read -r cle; do
    case "$tolerees" in *" $cle "*) continue ;; esac
    valeur="$(kubectl -n "$NAMESPACE" get secret "$nom" \
      -o "jsonpath={.data.${cle}}" | decoder_base64)"
    if [ -z "$valeur" ]; then
      signaler "clé vide dans $nom : $cle"
    fi
  done < <(cles_contrat "$contrat")
}

echo "Namespace : $NAMESPACE"
echo

verifier_secret hba-platform \
  "$ROOT_DIR/k8s/base/common/secret.yaml" \
  "CONNECTIONSTRINGS__DEFAULT"

verifier_secret hba-identites-internes \
  "$ROOT_DIR/k8s/base/common/secret-identites.yaml" \
  ""

verifier_secret hba-paiements \
  "$ROOT_DIR/k8s/base/common/secret-paiements.yaml" \
  ""

# ------------------------------------------------------------------------------
# minio et ghcr n'ont pas de fichier de contrat : leurs clés sont imposées par
# les images, pas par ce dépôt. Elles sont donc écrites ici, en toutes lettres.
#
# `minio` : `statefulset.yaml` lit `root-user` et `root-password`, et
# media-service lit LES MÊMES clés pour ses identifiants S3. Deux lecteurs, un
# seul Secret — c'est ce qui garantit qu'ils ne divergent pas.
#
# `ghcr` : un secret `kubernetes.io/dockerconfigjson`, dont la seule clé est
# `.dockerconfigjson`. Le ServiceAccount de chaque service le porte
# (`imagePullSecrets: [ghcr]`) ; sans lui, tous les pods restent en
# `ImagePullBackOff` sur un « denied » qui se lit comme un problème de droits.
# ------------------------------------------------------------------------------
echo "── minio"
if ! kubectl -n "$NAMESPACE" get secret minio >/dev/null 2>&1; then
  signaler "le Secret « minio » n'existe pas dans $NAMESPACE"
else
  for cle in root-user root-password; do
    valeur="$(kubectl -n "$NAMESPACE" get secret minio \
      -o "jsonpath={.data.${cle}}" | decoder_base64)"
    [ -n "$valeur" ] || signaler "clé vide dans minio : $cle"
  done
fi

# `redis` : `statefulset.yaml` lit `REDIS_PASSWORD` sous la clé `password`. La
# valeur doit concorder avec `REDIS__CONNECTIONSTRING` de `hba-platform` — ce que
# ce script NE PEUT PAS vérifier, les deux formes n'étant pas comparables.
echo "── redis"
if ! kubectl -n "$NAMESPACE" get secret redis >/dev/null 2>&1; then
  signaler "le Secret « redis » n'existe pas dans $NAMESPACE"
else
  valeur="$(kubectl -n "$NAMESPACE" get secret redis \
    -o "jsonpath={.data.password}" | decoder_base64)"
  [ -n "$valeur" ] || signaler "clé vide dans redis : password"
fi

echo "── ghcr"
if ! kubectl -n "$NAMESPACE" get secret ghcr >/dev/null 2>&1; then
  signaler "le Secret « ghcr » n'existe pas dans $NAMESPACE"
else
  valeur="$(kubectl -n "$NAMESPACE" get secret ghcr \
    -o 'jsonpath={.data.\.dockerconfigjson}' | decoder_base64)"
  [ -n "$valeur" ] || signaler "clé vide dans ghcr : .dockerconfigjson"
fi

echo
if [ "$ECHECS" = "1" ]; then
  echo "Secrets INCOMPLETS — voir les échecs ci-dessus." >&2
  exit 1
fi

echo "Secrets OK — 6 Secrets présents, aucune clé requise vide."
