#!/usr/bin/env bash
# ═══════════════════════════════════════════════════════════════════════════════
# OÙ EN EST LE DÉPLOIEMENT DE PRODUCTION ?
#
# CE QUI ÉTAIT CASSÉ : LE RUNBOOK A HUIT ÉTAPES ET AUCUN MOYEN DE SAVOIR OÙ ON EN
# EST.
#
# Chaque étape suppose la précédente. Lancée trop tôt, une commande échoue par
# « NotFound » sur un objet que l'étape d'avant devait créer — et ce message ne
# dit jamais QUELLE étape manque. `statefulsets.apps "minio" not found` veut dire
# « l'overlay n'a pas été appliqué », mais il faut connaître le runbook par cœur
# pour le lire ainsi.
#
# Ce script ne déploie rien et ne modifie rien. Il regarde, et il dit la
# prochaine chose à faire.
#
# CE QU'IL NE COUVRE PAS :
#   • il ne vérifie pas le CONTENU des Secrets, seulement leur présence. Un
#     Secret créé avec une clé manquante passe pour fait.
#   • il ne dit pas si les pods FONCTIONNENT, seulement s'ils existent et sont
#     prêts au sens de Kubernetes.
#   • il ne regarde ni le DNS, ni la base, ni le registre d'images.
# ═══════════════════════════════════════════════════════════════════════════════
set -uo pipefail

NS="${1:-hba-prod}"

vert()  { printf '  \033[32mOK\033[0m    %s\n' "$1"; }
rouge() { printf '  \033[31mÀ FAIRE\033[0m %s\n' "$1"; }
info()  { printf '        %s\n' "$1"; }

PROCHAINE=""
retenir() { [ -z "$PROCHAINE" ] && PROCHAINE="$1"; }

if ! command -v kubectl >/dev/null 2>&1; then
  echo "kubectl est introuvable. Rien ne peut être vérifié."
  exit 2
fi

if ! kubectl version >/dev/null 2>&1; then
  echo "kubectl ne joint pas le cluster."
  echo "  Le kubeconfig pointe peut-être 127.0.0.1 : le tunnel SSH doit tourner."
  echo "    ssh -N -L 6443:127.0.0.1:6443 <utilisateur>@79.137.35.129"
  exit 2
fi

echo "═══ État du déploiement — namespace ${NS} ═══"
echo
echo "1. Les opérateurs"

if kubectl get ns ingress-nginx >/dev/null 2>&1; then
  vert "ingress-nginx installé"
else
  rouge "ingress-nginx absent"
  retenir "kubectl apply -f https://raw.githubusercontent.com/kubernetes/ingress-nginx/main/deploy/static/provider/cloud/deploy.yaml"
fi

if kubectl get crd clusterissuers.cert-manager.io >/dev/null 2>&1; then
  vert "cert-manager installé"
  if kubectl get clusterissuer letsencrypt >/dev/null 2>&1; then
    vert "ClusterIssuer letsencrypt en place"
  else
    rouge "ClusterIssuer letsencrypt absent"
    retenir "kubectl apply -f k8s/cluster/clusterissuer.yaml"
  fi
else
  rouge "cert-manager absent"
  info "c'est ce que dit « the server doesn't have a resource type clusterissuer »"
  retenir "kubectl apply -f https://github.com/cert-manager/cert-manager/releases/download/v1.21.1/cert-manager.yaml"
fi

if kubectl get crd kafkas.kafka.strimzi.io >/dev/null 2>&1; then
  vert "Strimzi installé"
else
  rouge "Strimzi absent"
  info "sans lui, apply -k échoue sur « no matches for kind Kafka »"
  retenir "kubectl apply -f \"https://strimzi.io/install/latest?namespace=${NS}\" -n ${NS}"
fi

echo
echo "2. Le namespace et les Secrets"

if kubectl get ns "${NS}" >/dev/null 2>&1; then
  vert "namespace ${NS}"
else
  rouge "namespace ${NS} absent"
  retenir "kubectl create namespace ${NS}"
  echo
  echo "Prochaine chose à faire :"
  echo "  ${PROCHAINE}"
  exit 0
fi

# Le nombre de clés compte autant que la présence : un Secret créé par une
# commande incomplète existe et ne porte pas ce qu'il faut.
verifier_secret() {
  local nom="$1" attendu="$2" quoi="$3"
  local n
  n=$(kubectl -n "${NS}" get secret "${nom}" -o go-template='{{len .data}}' 2>/dev/null)
  if [ -z "${n}" ]; then
    rouge "${nom} absent — ${quoi}"
    return 1
  fi
  if [ "${n}" -lt "${attendu}" ]; then
    rouge "${nom} : ${n} clé(s), au moins ${attendu} attendue(s) — ${quoi}"
    return 1
  fi
  vert "${nom} (${n} clés)"
  return 0
}

verifier_secret ghcr 1 "le tirage des images" \
  || retenir "runbook §2.1 — kubectl create secret docker-registry ghcr"
verifier_secret hba-platform 21 "les chaînes de connexion" \
  || retenir "runbook §2.2 — python3 scripts/db/secret-depuis-motsdepasse.py"
verifier_secret hba-identites-internes 16 "les identités gRPC et l'administrateur" \
  || retenir "runbook §2.3 — ./scripts/generer-identites-internes.sh puis create secret"
verifier_secret minio 2 "le stockage objet" \
  || retenir "runbook §2.4 — kubectl create secret generic minio"

echo
echo "3. Les migrations"

JOBS=$(kubectl -n "${NS}" get job -l app.kubernetes.io/component=migration \
       -o go-template='{{len .items}}' 2>/dev/null || echo 0)
TERMINES=$(kubectl -n "${NS}" get job -l app.kubernetes.io/component=migration \
           -o go-template='{{range .items}}{{if .status.succeeded}}x{{end}}{{end}}' 2>/dev/null | wc -c | tr -d ' ')
if [ "${JOBS}" = "0" ]; then
  rouge "aucun Job de migration"
  info "les Jobs disparaissent une heure après leur fin (ttlSecondsAfterFinished)"
  info "donc « aucun » peut vouloir dire « jamais lancés » OU « terminés depuis »"
  retenir "kubectl apply -k k8s/overlays/migrations-prod"
elif [ "${TERMINES}" -lt "${JOBS}" ]; then
  # DES JOBS QUI TOURNENT NE SONT PAS DES JOBS QUI ONT RÉUSSI.
  #
  # La première version de ce script rendait « OK » sur « 8 Jobs, dont 0
  # terminé » — un vert sur des migrations qui n'ont rien migré. C'est le défaut
  # exact que le reste du dépôt passe son temps à corriger : un contrôle qui
  # rassure vaut moins que pas de contrôle.
  rouge "${JOBS} Job(s) de migration, seulement ${TERMINES} terminé(s)"
  PODS=$(kubectl -n "${NS}" get pods -l app.kubernetes.io/component=migration \
         --no-headers 2>/dev/null)
  if [ -z "${PODS}" ]; then
    # UN JOB SANS AUCUN POD N'EST PAS UN JOB QUI ÉCHOUE — C'EST UN JOB QUI
    # N'A RIEN LANCÉ.
    #
    # Les deux se ressemblent dans `get job` (0 terminé), et se diagnostiquent
    # à l'opposé. Sans pod, il n'y a AUCUN journal à lire : `kubectl logs`
    # répondra « no pods found », ce qui envoie chercher du côté du service.
    # La cause est dans les événements du Job.
    rouge "les Jobs existent mais n'ont créé AUCUN pod"
    info "sans pod, il n'y a pas de journal : c'est describe qu'il faut lire"
    retenir "kubectl -n ${NS} describe job -l app.kubernetes.io/component=migration | tail -30"
  else
    info "état des pods de migration :"
    echo "${PODS}" | sed 's/^/          /' | head -10
    info "ImagePullBackOff ici veut dire que les images ne sont pas promues :"
    info "  le tag vaut encore REMPLACE-PAR-LA-PROMOTION"
    retenir "kubectl -n ${NS} logs job/<service>-migration   # lire la cause"
  fi
else
  vert "${JOBS} Job(s) de migration, tous terminés"
fi

echo
echo "4. Les charges"

# ON COMPARE À LA LISTE ATTENDUE, PAS À ZÉRO.
#
# Compter les Deployments du namespace ne dit rien : Strimzi installe SON
# opérateur dans `hba-prod`, ce qui suffisait à faire dire « 1 Deployment,
# 1 entièrement prêt » sur un namespace où AUCUN service n'est déployé. Le
# script rendait vert sur un cluster vide.
#
# La liste de référence est celle du dépôt — les entrées non commentées de
# `k8s/base/services/kustomization.yaml`, plus la passerelle.
ATTENDUS=$(sed -n 's/^  - \([a-z0-9-]*-service\)$/\1/p' \
           k8s/base/services/kustomization.yaml 2>/dev/null)

if [ -z "${ATTENDUS}" ]; then
  info "k8s/base/services/kustomization.yaml illisible — lancer depuis la racine du dépôt"
  ATTENDUS=""
fi

MANQUANTS=""
PAS_PRETS=""
for SVC in ${ATTENDUS}; do
  ETAT=$(kubectl -n "${NS}" get deploy "${SVC}" \
         -o go-template='{{if and .status.readyReplicas (eq .status.readyReplicas .status.replicas)}}pret{{else}}attente{{end}}' \
         2>/dev/null)
  case "${ETAT}" in
    pret)    ;;
    attente) PAS_PRETS="${PAS_PRETS} ${SVC}" ;;
    *)       MANQUANTS="${MANQUANTS} ${SVC}" ;;
  esac
done

NB_ATTENDUS=$(echo ${ATTENDUS} | wc -w | tr -d ' ')
NB_MANQUANTS=$(echo ${MANQUANTS} | wc -w | tr -d ' ')
NB_PAS_PRETS=$(echo ${PAS_PRETS} | wc -w | tr -d ' ')

if [ "${NB_MANQUANTS}" = "${NB_ATTENDUS}" ] && [ "${NB_ATTENDUS}" != "0" ]; then
  rouge "aucun des ${NB_ATTENDUS} services attendus n'est déployé"
  info "c'est ce que dit « statefulsets.apps minio not found »"
  retenir "kubectl apply -k k8s/overlays/prod"
elif [ "${NB_MANQUANTS}" != "0" ]; then
  rouge "${NB_MANQUANTS} service(s) absent(s) :${MANQUANTS}"
  retenir "kubectl apply -k k8s/overlays/prod"
elif [ "${NB_PAS_PRETS}" != "0" ]; then
  rouge "${NB_PAS_PRETS} service(s) pas encore prêt(s) :${PAS_PRETS}"
  info "les pods qui ne tournent pas :"
  kubectl -n "${NS}" get pods --field-selector=status.phase!=Running \
    --no-headers 2>/dev/null | sed 's/^/          /' | head -10
  retenir "kubectl -n ${NS} describe pod -l app.kubernetes.io/name=<service>"
else
  vert "${NB_ATTENDUS} service(s) déployé(s) et prêt(s)"
fi

if kubectl -n "${NS}" get statefulset minio >/dev/null 2>&1; then
  vert "MinIO déployé"
  if kubectl -n "${NS}" get job minio-buckets >/dev/null 2>&1; then
    vert "Job des buckets lancé"
  else
    rouge "buckets MinIO non créés"
    info "media-service démarre quand même ; l'échec vient au premier envoi"
    retenir "kubectl -n ${NS} apply -f k8s/base/data/minio/job-buckets.yaml"
  fi
elif [ "${NB_MANQUANTS}" = "0" ] && [ "${NB_ATTENDUS}" != "0" ]; then
  rouge "MinIO absent alors que les services sont là"
  retenir "kubectl apply -k k8s/overlays/prod"
fi

echo
echo "5. L'entrée publique"

if kubectl -n "${NS}" get ingress >/dev/null 2>&1 \
   && [ "$(kubectl -n "${NS}" get ingress -o go-template='{{len .items}}')" != "0" ]; then
  PRET=$(kubectl -n "${NS}" get certificate \
    -o go-template='{{range .items}}{{range .status.conditions}}{{if eq .type "Ready"}}{{.status}}{{end}}{{end}}{{end}}' 2>/dev/null)
  case "${PRET}" in
    *True*) vert "certificat TLS émis" ;;
    "")     rouge "aucun certificat — cert-manager ne l'a pas encore pris"
            retenir "kubectl -n ${NS} describe certificate" ;;
    *)      rouge "certificat pas encore prêt (${PRET})"
            info "DNS propagé ? port 80 joignable depuis l'extérieur ?"
            retenir "kubectl -n ${NS} describe certificate" ;;
  esac
else
  rouge "aucun Ingress — l'overlay n'a pas été appliqué"
  retenir "kubectl apply -k k8s/overlays/prod"
fi

echo
if [ -n "${PROCHAINE}" ]; then
  echo "Prochaine chose à faire :"
  echo "  ${PROCHAINE}"
else
  echo "Rien ne manque de ce que ce script sait vérifier."
  echo "Reste à contrôler à la main : le compte administrateur dans les journaux"
  echo "d'identity-service, et un appel réel sur https://api.hba-express.com."
fi
