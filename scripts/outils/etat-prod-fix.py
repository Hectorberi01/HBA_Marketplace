import re

p = "scripts/etat-prod.sh"
src = open(p, encoding="utf-8").read()

# ── 3. Les migrations ────────────────────────────────────────────────────────
ancien = '''if [ "${JOBS}" = "0" ]; then
  rouge "aucun Job de migration"
  info "les Jobs disparaissent une heure après leur fin (ttlSecondsAfterFinished)"
  info "donc « aucun » peut vouloir dire « jamais lancés » OU « terminés depuis »"
  retenir "kubectl apply -k k8s/overlays/migrations-prod"
else
  vert "${JOBS} Job(s) de migration, dont ${TERMINES} terminé(s)"
fi'''

nouveau = '''if [ "${JOBS}" = "0" ]; then
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
  info "état des pods de migration :"
  kubectl -n "${NS}" get pods -l app.kubernetes.io/component=migration \\
    --no-headers 2>/dev/null | sed 's/^/          /' | head -10
  info "ImagePullBackOff ici veut dire que les images ne sont pas promues :"
  info "  le tag vaut encore REMPLACE-PAR-LA-PROMOTION"
  retenir "kubectl -n ${NS} logs job/<service>-migration   # lire la cause"
else
  vert "${JOBS} Job(s) de migration, tous terminés"
fi'''
assert ancien in src, "bloc migrations introuvable"
src = src.replace(ancien, nouveau, 1)

# ── 4. Les charges ───────────────────────────────────────────────────────────
ancien = '''DEPLOIEMENTS=$(kubectl -n "${NS}" get deploy -o go-template='{{len .items}}' 2>/dev/null || echo 0)
if [ "${DEPLOIEMENTS}" = "0" ]; then
  rouge "aucun Deployment — l'overlay n'a pas été appliqué"
  info "c'est ce que dit « statefulsets.apps minio not found »"
  retenir "kubectl apply -k k8s/overlays/prod"
else
  PRETS=$(kubectl -n "${NS}" get deploy \\
    -o go-template='{{range .items}}{{if and .status.readyReplicas (eq .status.readyReplicas .status.replicas)}}x{{end}}{{end}}' \\
    2>/dev/null | wc -c | tr -d ' ')
  vert "${DEPLOIEMENTS} Deployment(s), ${PRETS} entièrement prêt(s)"
  if [ "${PRETS}" != "${DEPLOIEMENTS}" ]; then
    info "les pods qui ne démarrent pas :"
    kubectl -n "${NS}" get pods --field-selector=status.phase!=Running \\
      --no-headers 2>/dev/null | sed 's/^/          /' | head -10
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
  fi
fi'''

nouveau = '''# ON COMPARE À LA LISTE ATTENDUE, PAS À ZÉRO.
#
# Compter les Deployments du namespace ne dit rien : Strimzi installe SON
# opérateur dans `hba-prod`, ce qui suffisait à faire dire « 1 Deployment,
# 1 entièrement prêt » sur un namespace où AUCUN service n'est déployé. Le
# script rendait vert sur un cluster vide.
#
# La liste de référence est celle du dépôt — les entrées non commentées de
# `k8s/base/services/kustomization.yaml`, plus la passerelle.
ATTENDUS=$(sed -n 's/^  - \\([a-z0-9-]*-service\\)$/\\1/p' \\
           k8s/base/services/kustomization.yaml 2>/dev/null)

if [ -z "${ATTENDUS}" ]; then
  info "k8s/base/services/kustomization.yaml illisible — lancer depuis la racine du dépôt"
  ATTENDUS=""
fi

MANQUANTS=""
PAS_PRETS=""
for SVC in ${ATTENDUS}; do
  ETAT=$(kubectl -n "${NS}" get deploy "${SVC}" \\
         -o go-template='{{if and .status.readyReplicas (eq .status.readyReplicas .status.replicas)}}pret{{else}}attente{{end}}' \\
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
  kubectl -n "${NS}" get pods --field-selector=status.phase!=Running \\
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
fi'''
assert ancien in src, "bloc charges introuvable"
src = src.replace(ancien, nouveau, 1)

# ── 5. L'entrée publique : « pas encore d'Ingress » n'est pas neutre ─────────
ancien = '''else
  info "pas encore d'Ingress — il vient avec l'overlay"
fi'''
nouveau = '''else
  rouge "aucun Ingress — l'overlay n'a pas été appliqué"
  retenir "kubectl apply -k k8s/overlays/prod"
fi'''
assert ancien in src, "bloc ingress introuvable"
src = src.replace(ancien, nouveau, 1)

open(p, "w", encoding="utf-8").write(src)
print("etat-prod.sh corrige sur trois points")
