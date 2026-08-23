#!/usr/bin/env bash
set -uo pipefail

# ═══════════════════════════════════════════════════════════════════════════
# ÉTAT DE LA PILE — CE QUE `logs` SEUL NE DIT PAS.
#
# POURQUOI UNE TRACE D'EXCEPTION NE SUFFIT JAMAIS.
#
# `docker compose logs` rend TOUT l'historique du conteneur, sans horodatage
# par défaut. Une trace copiée depuis cette sortie ne dit pas :
#
#   • si elle date de maintenant ou d'un `down` d'il y a deux heures ;
#   • si le conteneur tourne malgré elle ;
#   • s'il a été tué (OOM) ou s'il s'est arrêté proprement ;
#   • combien de fois il a redémarré.
#
# On peut donc passer une heure sur une exception résolue depuis longtemps.
# C'est exactement ce qui vient d'arriver sur `TaskCanceledException` : la trace
# décrit un arrêt DEMANDÉ pendant le démarrage — mais elle ne dit pas par qui,
# ni quand.
#
# CE QUE CE SCRIPT CHERCHE, DANS L'ORDRE.
#
#   1. Le conteneur tourne-t-il MAINTENANT ? Si oui, la trace est un vestige.
#   2. Code de sortie. 0 = arrêt propre (donc demandé). 137 = SIGKILL, presque
#      toujours la mémoire. 139 = SIGSEGV. 143 = SIGTERM honoré.
#   3. `OOMKilled` : Docker le sait, et c'est la seule façon de le savoir.
#   4. Horodatages : `StartedAt` / `FinishedAt` situent la trace dans le temps.
#
# Écrit dans un fichier ET à l'écran : le terminal tronque, le fichier non.
#
# Usage :
#   ./scripts/dev-doctor.sh                 tous les services
#   ./scripts/dev-doctor.sh identity-service delivery-service
#   LINES=60 ./scripts/dev-doctor.sh
# ═══════════════════════════════════════════════════════════════════════════

ROOT_DIR="$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)"
COMPOSE_FILE="$ROOT_DIR/docker-compose.dev.yml"
OUT="${OUT:-/tmp/hba-doctor.log}"
LINES="${LINES:-30}"

cd "$ROOT_DIR"
compose() { docker compose -f "$COMPOSE_FILE" "$@"; }

INFRA=(postgres redis kafka)
APPS=(
  identity-service user-service media-service notification-service
  payment-service promotion-service review-service seller-service catalog-service
  inventory-service cart-service order-service restaurant-service
  food-cart-service food-order-service
  delivery-pricing-service delivery-service dispatch-service
  driver-service tracking-service route-service proof-of-delivery-service
  gateway
)
# Retirés : les quatre squelettes food (menu, availability, kitchen-prep,
# food-review) au lot 6.4, les trois BFF (client, seller, driver) en D38. Ils
# n'existent plus sur le disque — les laisser ici faisait échouer le diagnostic
# sur des services absents, ce qui masque les vraies pannes.

if [ "$#" -gt 0 ]; then
  APPS=("$@")
  INFRA=()
fi

# `exec > >(tee)` capture tout, y compris ce qu'écrivent les sous-commandes.
exec > >(tee "$OUT") 2>&1

echo "═══ Diagnostic HBA — $(date '+%Y-%m-%d %H:%M:%S %Z') ═══"
echo

# ── Infrastructure ─────────────────────────────────────────────────────────
#
# En premier, parce qu'un service applicatif qui ne démarre pas a beaucoup plus
# souvent une base absente qu'un défaut de code.
if [ ${#INFRA[@]} -gt 0 ]; then
  echo "── Infrastructure"
  for name in "${INFRA[@]}"; do
    cid=$(compose ps -q "$name" 2>/dev/null)
    if [ -z "$cid" ]; then
      printf '  %-12s ✗ absent\n' "$name"
      continue
    fi
    read -r state health <<<"$(docker inspect \
      --format '{{.State.Status}} {{if .State.Health}}{{.State.Health.Status}}{{else}}-{{end}}' \
      "$cid" 2>/dev/null)"
    printf '  %-12s %s (santé : %s)\n' "$name" "$state" "$health"
  done
  echo
fi

# ── Services ───────────────────────────────────────────────────────────────
DOWN=()

echo "── Services"
printf '  %-24s %-10s %-6s %-6s %-5s %s\n' NOM ÉTAT CODE OOM RDÉM DEPUIS
for name in "${APPS[@]}"; do
  cid=$(compose ps -q "$name" 2>/dev/null)
  if [ -z "$cid" ]; then
    printf '  %-24s %-10s\n' "$name" "absent"
    DOWN+=("$name")
    continue
  fi

  info=$(docker inspect --format \
    '{{.State.Status}}|{{.State.ExitCode}}|{{.State.OOMKilled}}|{{.RestartCount}}|{{.State.StartedAt}}|{{.State.FinishedAt}}' \
    "$cid" 2>/dev/null)

  IFS='|' read -r status code oom restarts started finished <<<"$info"

  # Un conteneur vivant date de son démarrage ; un conteneur mort, de sa fin.
  stamp="${started:0:19}"
  [ "$status" != "running" ] && stamp="${finished:0:19}"

  printf '  %-24s %-10s %-6s %-6s %-5s %s\n' \
    "$name" "$status" "$code" "$oom" "$restarts" "$stamp"

  [ "$status" = "running" ] || DOWN+=("$name")
done
echo

# ── Lecture ────────────────────────────────────────────────────────────────
if [ ${#DOWN[@]} -eq 0 ]; then
  echo "Les ${#APPS[@]} conteneurs tournent."
  echo
  echo "Si une exception apparaît malgré tout dans les journaux, elle est"
  echo "   ANTÉRIEURE : comparer son horodatage à la colonne DEPUIS ci-dessus."
  echo "   Aucune politique « restart: » dans ce compose — un conteneur qui"
  echo "   tourne n'a donc pas redémarré après un plantage, il n'a jamais planté."
else
  echo "── Journaux des ${#DOWN[@]} conteneur(s) arrêté(s)"
  for name in "${DOWN[@]}"; do
    echo
    echo "──────── $name ────────"
    # `-t` : l'horodatage est le point de tout l'exercice.
    compose logs -t --tail="$LINES" "$name" 2>&1 | sed 's/^/  /'
  done
  echo
  echo "── Lecture des codes de sortie"
  echo "   0    arrêt propre — quelqu'un l'a DEMANDÉ (compose down, Ctrl+C,"
  echo "        ou compose interrompant un « up » dont une dépendance a échoué)."
  echo "   137  SIGKILL. Avec OOM=true : mémoire. Sinon : « docker kill »."
  echo "   139  SIGSEGV."
  echo "   143  SIGTERM honoré."
  echo "   1    exception non rattrapée — la trace ci-dessus est la vraie cause."
fi

echo
echo "Rapport complet : $OUT"
