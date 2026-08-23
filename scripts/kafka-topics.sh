#!/usr/bin/env bash
set -euo pipefail

# ═══════════════════════════════════════════════════════════════════════════
# DÉCLARATION DES SUJETS KAFKA, DÉRIVÉS DE `HbaTopics`.
#
# POURQUOI CRÉER CE QUE LE COURTIER CRÉERAIT TOUT SEUL.
#
# `auto.create.topics.enable` vaut vrai sur l'image Confluent : le premier
# PRODUCTEUR fait naître son sujet. Rien ne casse donc de façon spectaculaire —
# et c'est précisément ce qui rend le problème coûteux.
#
# Ce qui se passe réellement sans ce script :
#
#   1. Les services s'abonnent à TOUS les sujets AU DÉMARRAGE, avant
#      qu'aucun événement n'ait été publié. Côté librdkafka,
#      `allow.auto.create.topics` vaut FAUX par défaut — contrairement au client
#      Java. L'abonnement ne crée donc rien : il échoue.
#
#   2. `KafkaIntegrationEventConsumer` attrape la `ConsumeException` et
#      journalise un avertissement. Sa boucle repart aussitôt. Chaque service
#      × chaque sujet, en continu :
#
#          Subscribed topic not available: service.identity.v1:
#          Broker: Unknown topic or partition
#
#      Ce n'est pas une panne, c'est un BROUILLARD. Les vraies erreurs Kafka —
#      celles qu'on veut voir — se noient dedans.
#
#   3. Le sujet finit par exister, créé par le premier producteur, avec les
#      réglages par défaut du courtier. Personne ne l'a décidé.
#
# Ce que ce script NE corrige PAS, et il faut le savoir : les événements publiés
# avant l'abonnement ne sont pas perdus. Le consommateur est en
# `AutoOffsetReset.Earliest` sans commit automatique — il relit depuis le début
# du sujet. Le risque ici est la lisibilité et la maîtrise des réglages, pas la
# perte de données.
#
# ET LE JOUR OÙ `auto.create.topics.enable` PASSERA À FAUX.
#
# C'est le réglage attendu en production : on n'y laisse pas une faute de frappe
# dans un nom de producteur fabriquer un sujet fantôme. Ce jour-là, une pile qui
# n'avait jamais déclaré ses sujets ne publie plus rien, et l'erreur ressemble à
# tout sauf à sa cause. Déclarer les sujets ici, c'est faire maintenant, en
# local, un geste qui sera de toute façon obligatoire ailleurs.
#
# Usage :
#   ./scripts/kafka-topics.sh              crée ce qui manque
#   ./scripts/kafka-topics.sh --list       montre l'existant, ne crée rien
#   ./scripts/kafka-topics.sh --describe   partitions et réplicas de chaque sujet
#   ./scripts/kafka-topics.sh --dry-run    dit ce qu'il ferait
#
#   PARTITIONS=6 ./scripts/kafka-topics.sh
# ═══════════════════════════════════════════════════════════════════════════

ROOT_DIR="$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)"
COMPOSE_FILE="$ROOT_DIR/docker-compose.dev.yml"
OPTIONS_FILE="$ROOT_DIR/shared/common/HBA.Shared.Infrastructure/Kafka/KafkaEventBusOptions.cs"
TOPICS_FILE="$ROOT_DIR/shared/common/HBA.Shared.Infrastructure/Kafka/HbaTopics.cs"

# TROIS PARTITIONS, ET L'ORDRE RESTE GARANTI LÀ OÙ IL COMPTE.
#
# Kafka n'ordonne QUE dans une partition. Trois partitions veulent donc dire
# trois flux concurrents — ce qui serait faux si les événements d'une même
# commande pouvaient s'y disperser.
#
# Ils ne le peuvent pas : `KafkaIntegrationEventPublisher` pose
# `Key = aggregateId`. Tous les événements d'une commande, d'un vendeur ou d'un
# compte partagent la même clé, donc la même partition, donc leur ordre. Ce qui
# se parallélise, ce sont les agrégats entre eux — exactement ce qu'on veut.
#
# Changer ce nombre APRÈS coup ne redistribue pas l'existant : les clés déjà
# écrites restent où elles sont, et l'ordre d'un agrégat à cheval sur l'ancienne
# et la nouvelle répartition n'est plus garanti. À fixer une fois.
PARTITIONS="${PARTITIONS:-3}"

# Un seul courtier en local : demander mieux ferait échouer la création avec
# « replication factor larger than available brokers ».
REPLICATION="${REPLICATION:-1}"

MODE="create"

for arg in "$@"; do
  case "$arg" in
    --list)     MODE="list" ;;
    --describe) MODE="describe" ;;
    --dry-run)  MODE="dry-run" ;;
    *) echo "Option inconnue : $arg" >&2; exit 2 ;;
  esac
done

# ── La liste des sujets n'est PAS écrite ici ───────────────────────────────
#
# UNE SEULE SOURCE, ET C'EST LE CODE — MAIS PLUS LE MÊME FICHIER QU'AVANT.
#
# Ce script lisait `KafkaEventBusOptions.SubscribeTopics`, où treize sujets
# étaient écrits en dur. ISSUE-001 (lot 2.2) a vidé cette propriété : le
# consommateur prend désormais `HbaTopics.Tous(options)`, la même table que celle
# qui décide du sujet de PUBLICATION. La liste en dur n'existe plus, et ce script
# ne trouvait donc plus rien — `dev-up.sh` créait zéro sujet, en silence, derrière
# un « Sujets non créés » que personne ne lit.
#
# On lit maintenant la table elle-même, plus le préfixe et la version par défaut
# des options. Aucun nom n'est recopié ici : un quatorzième service arrive dans la
# table, et ce script le crée sans qu'on y touche.
[ -f "$TOPICS_FILE" ] || {
  echo "✗ Introuvable : $TOPICS_FILE" >&2
  echo "  Le fichier a-t-il été déplacé ? Ce script en dépend pour la liste." >&2
  exit 1
}

[ -f "$OPTIONS_FILE" ] || {
  echo "✗ Introuvable : $OPTIONS_FILE" >&2
  echo "  Ce script y lit le préfixe et la version des sujets." >&2
  exit 1
}

# Les défauts des options : `service` et `v1`. Les lire plutôt que les écrire,
# pour la même raison que le reste.
PREFIXE="$(grep -oE 'TopicPrefix[^=]*= *"[^"]+"' "$OPTIONS_FILE" \
             | sed -E 's/.*"([^"]+)".*/\1/' | head -n 1)"
VERSION="$(grep -oE 'TopicVersion[^=]*= *"[^"]+"' "$OPTIONS_FILE" \
             | sed -E 's/.*"([^"]+)".*/\1/' | head -n 1)"

[ -n "$PREFIXE" ] && [ -n "$VERSION" ] || {
  echo "✗ Préfixe ou version introuvable dans $OPTIONS_FILE." >&2
  echo "  Attendu : TopicPrefix { get; init; } = \"service\";" >&2
  exit 1
}

# `["seller-service"] = "merchant",` → `seller-service merchant`
#
# PAS DE TABLEAU ASSOCIATIF : macOS livre bash 3.2, qui n'en a pas.
PAIRES="$(grep -oE '\["[A-Za-z0-9._-]+"\][[:space:]]*=[[:space:]]*"[A-Za-z0-9._-]+"' \
            "$TOPICS_FILE" \
          | sed -E 's/\["([^"]+)"\][[:space:]]*=[[:space:]]*"([^"]+)"/\1 \2/')"

[ -n "$PAIRES" ] || {
  echo "✗ Aucune entrée trouvée dans HbaTopics.DomaineParService." >&2
  echo "  Le format a changé ? Attendu : [\"nom-service\"] = \"domaine\"," >&2
  exit 1
}

TOPICS=()
while IFS= read -r topic; do
  [ -n "$topic" ] && TOPICS+=("$topic")
done < <(echo "$PAIRES" | awk -v p="$PREFIXE" -v v="$VERSION" '{print p "." $2 "." v}' | sort -u)

[ ${#TOPICS[@]} -gt 0 ] || {
  echo "✗ Aucun sujet dérivé de la table." >&2
  exit 1
}

# ── Contrôle de cohérence : chaque producteur a-t-il un sujet ? ────────────
#
# CE CONTRÔLE DÉRIVAIT LE SUJET COMME LE FAISAIT L'ANCIEN CODE — DONC FAUX.
#
# Il calculait `service.${producer%-service}.v1`, c'est-à-dire la dérivation
# naïve : `seller-service` → `service.seller.v1`. C'est exactement le défaut
# d'ISSUE-001 : le domaine de seller-service est `merchant`, pas `seller`. Depuis
# que la table existe, ce calcul déclarerait orphelins les six services dont le
# nom de conteneur diffère du domaine — un contrôle qui crie à tort.
#
# On interroge donc la table. Un `KAFKA__PRODUCER` qui n'y est pas décrit un
# service qui PUBLIE là où personne n'écoute, silencieux des deux côtés : le
# producteur réussit, le consommateur ne sait pas qu'il rate quelque chose.
# `scripts/check-kafka-topics.py` fait le même rapprochement, hors ligne et sans
# courtier.
check_producers() {
  local orphans=0 producer domaine
  while IFS= read -r producer; do
    [ -n "$producer" ] || continue
    domaine="$(echo "$PAIRES" | awk -v s="$producer" '$1 == s {print $2; exit}')"
    if [ -z "$domaine" ]; then
      echo "  $producer est absent de HbaTopics.DomaineParService." >&2
      echo "    Ses événements partiraient sur « $PREFIXE.${producer%-service}.$VERSION »," >&2
      echo "    auquel personne ne s'abonne." >&2
      orphans=$((orphans + 1))
    fi
  done < <(grep -E '^[[:space:]]*KAFKA__PRODUCER:' "$COMPOSE_FILE" 2>/dev/null \
             | awk '{print $2}' | sort -u)

  # Quatre de ces avertissements sont attendus : les squelettes food retirés par
  # D30 (`menu`, `availability`, `kitchen-prep`, `food-review`) ont un
  # `KAFKA__PRODUCER` dans le compose et pas une ligne qui publie. Ce script ne
  # sait pas lire le code ; `check-kafka-topics.py`, si — il sépare ceux qui
  # publient vraiment de ceux qui ne font que déclarer un nom.
  [ "$orphans" -eq 0 ] || {
    echo "  → $orphans producteur(s) hors catalogue." >&2
    echo "    Lesquels publient vraiment : python3 scripts/check-kafka-topics.py" >&2
  }
}

# ── Accès au courtier ──────────────────────────────────────────────────────
#
# Par `exec` dans le conteneur plutôt que depuis l'hôte : `kafka-topics` n'est
# pas installé sur une machine de développement ordinaire, et le port publié
# (9092, annoncé `localhost`) ne sert qu'aux clients hors réseau Docker.
kt() {
  docker compose -f "$COMPOSE_FILE" exec -T kafka \
    kafka-topics --bootstrap-server localhost:9092 "$@"
}

# DISTINGUER « KAFKA ÉTEINT » DE « KAFKA PAS ENCORE PRÊT ».
#
# Un courtier KRaft accepte les connexions bien avant de répondre aux requêtes
# de métadonnées. Sans cette attente, le script échouerait juste après un
# `up -d` — et l'erreur parlerait de sujets alors que le problème est l'heure.
wait_for_broker() {
  local attempt=0
  until kt --list >/dev/null 2>&1; do
    attempt=$((attempt + 1))
    if [ "$attempt" -ge 30 ]; then
      echo "✗ Le courtier ne répond pas après 60 s." >&2
      echo "  docker compose -f docker-compose.dev.yml ps kafka" >&2
      echo "  docker compose -f docker-compose.dev.yml logs --tail=40 kafka" >&2
      exit 1
    fi
    [ "$attempt" -eq 1 ] && printf '  Attente du courtier'
    printf '.'
    sleep 2
  done
  [ "$attempt" -gt 0 ] && printf '\n'
  return 0
}

existing_topics() { kt --list 2>/dev/null | tr -d '\r' | sed '/^$/d'; }

has_topic() {
  local needle="$1" line
  while IFS= read -r line; do
    [ "$line" = "$needle" ] && return 0
  done <<<"$2"
  return 1
}

# ── Exécution ──────────────────────────────────────────────────────────────

printf '── Sujets Kafka (%d déclarés, %d partitions, réplication %d)\n' \
  "${#TOPICS[@]}" "$PARTITIONS" "$REPLICATION"

check_producers

if [ "$MODE" = "dry-run" ]; then
  for topic in "${TOPICS[@]}"; do
    echo "  + $topic"
  done
  echo
  echo "Rien n'a été créé (--dry-run)."
  exit 0
fi

wait_for_broker

if [ "$MODE" = "describe" ]; then
  kt --describe
  exit 0
fi

EXISTING="$(existing_topics)"

if [ "$MODE" = "list" ]; then
  for topic in "${TOPICS[@]}"; do
    if has_topic "$topic" "$EXISTING"; then
      echo "  ✓ $topic"
    else
      echo "  ✗ $topic  (absent)"
    fi
  done
  # Les sujets internes (`__consumer_offsets`) sont normaux : ils appartiennent
  # à Kafka, pas à la plateforme.
  echo
  echo "Sujets présents sur le courtier :"
  echo "$EXISTING" | sed 's/^/    /'
  exit 0
fi

CREATED=0
KEPT=0

for topic in "${TOPICS[@]}"; do
  if has_topic "$topic" "$EXISTING"; then
    # ON NE TOUCHE PAS À UN SUJET EXISTANT.
    #
    # `--alter` sur les partitions est irréversible et casse l'ordre par clé.
    # Un sujet déjà là est laissé tel quel, même si son nombre de partitions
    # diffère de `$PARTITIONS` : le corriger demande une décision, pas un script.
    echo "  = $topic"
    KEPT=$((KEPT + 1))
    continue
  fi

  if kt --create --if-not-exists \
       --topic "$topic" \
       --partitions "$PARTITIONS" \
       --replication-factor "$REPLICATION" >/dev/null 2>&1; then
    echo "  + $topic"
    CREATED=$((CREATED + 1))
  else
    echo "  ✗ $topic — création refusée" >&2
    kt --create --topic "$topic" \
       --partitions "$PARTITIONS" \
       --replication-factor "$REPLICATION" 2>&1 | sed 's/^/      /' >&2 || true
  fi
done

echo
echo "$CREATED créé(s), $KEPT déjà en place."

if [ "$CREATED" -gt 0 ]; then
  echo
  echo "Les consommateurs déjà démarrés rattrapent d'eux-mêmes : librdkafka"
  echo "rafraîchit ses métadonnées périodiquement. Les avertissements"
  echo "« Unknown topic or partition » doivent cesser en moins d'une minute."
  echo "Sinon : docker compose -f docker-compose.dev.yml restart"
fi
