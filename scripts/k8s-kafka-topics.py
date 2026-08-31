#!/usr/bin/env python3
"""
═══════════════════════════════════════════════════════════════════════════════
LES KafkaTopic DES TROIS ENVIRONNEMENTS — MIROIR DE `HbaTopics`.

CE SCRIPT PROVISIONNAIT UN SCHÉMA QUE PERSONNE NE PUBLIAIT (ISSUE-001, lot 2.2).

Il lisait les attributs `[HbaEvent]` et rendait `hba.<env>.<domaine>.<agrégat>.v1`,
plus un `.dlq` par topic. Ce n'était PAS une erreur : c'est exactement le §19.2, et
`HbaEventNaming` construit ces noms-là. Le défaut est ailleurs — personne n'appelle
`HbaEventNaming`. Le runtime publie (`KafkaIntegrationEventPublisher`) et consomme
(`KafkaIntegrationEventConsumer`) `service.<domaine>.v1`, dérivé de `HbaTopics`.

Conséquence, et elle est silencieuse des deux côtés : les vingt-huit topics
provisionnés par environnement n'ont jamais reçu un message, pendant que les
vingt-trois réellement employés étaient auto-créés par le courtier — une partition,
réplication et rétention par défaut. Ce que ce fichier prétendait garantir (§9)
n'était garanti sur AUCUN des topics qui portent le trafic.

Ce script rend donc maintenant ce que le code publie. Le §19.2 reste la cible : le
jour où `HbaEventNaming` sera branché, c'est ici qu'on revient — la forme du fichier
ne bouge pas, seule la liste des noms.

LA SOURCE EST LE `.cs`, PAS UNE LISTE RECOPIÉE ICI.

Recopier les vingt-trois domaines dans ce script en referait une seconde vérité —
le défaut même qu'ISSUE-001 vient de fermer. On lit `HbaTopics.DomaineParService`
et les défauts de `KafkaEventBusOptions`, et rien d'autre.

Usage :
    python3 scripts/k8s-kafka-topics.py            # écrit les trois fichiers
    python3 scripts/k8s-kafka-topics.py --verifie  # échoue s'ils sont périmés
═══════════════════════════════════════════════════════════════════════════════
"""
from __future__ import annotations

import io
import os
import re
import sys

RACINE = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
ENVIRONNEMENTS = ("dev", "staging", "prod")

KAFKA = os.path.join(RACINE, "shared", "common", "HBA.Shared.Infrastructure", "Kafka")
TOPICS_CS = os.path.join(KAFKA, "HbaTopics.cs")
OPTIONS_CS = os.path.join(KAFKA, "KafkaEventBusOptions.cs")

# `["seller-service"] = "merchant",` — la table, et rien que la table : les
# commentaires du fichier citent des noms de sujets, jamais sous cette forme.
ENTREE = re.compile(r'\["([A-Za-z0-9._-]+)"\]\s*=\s*"([A-Za-z0-9._-]+)"')

# Réplication : un seul courtier partout, y compris en production depuis le
# 31 août 2026. Trois répliques coûteraient trois fois le stockage pour une
# garantie que personne n'observe, et la création échouerait de toute façon
# (« replication factor larger than available brokers »).
#
# LA PRODUCTION EST PASSÉE DE 3 À 1, ET CE N'EST PAS UN RELÂCHEMENT.
#
# Le §9 demande 3 parce que trois copies sur TROIS MACHINES survivent à la perte
# de l'une d'elles. La production tourne sur un nœud k3s unique
# (79.137.35.129) : les trois répliques vivraient sur le même disque et la panne
# dont elles protègent les emporterait ensemble. La garantie serait écrite et
# jamais obtenue.
#
# CES VALEURS SONT APPARIÉES AVEC `k8s/overlays/prod/kustomization.yaml`, qui
# descend `KafkaNodePool/broker` à 1 réplique et les cinq facteurs du `Kafka` à
# 1. Les trois réglages changent ENSEMBLE ou pas du tout : des topics à
# `replicas: 3` sur un pool à 1 restent sans leader, et les producteurs bloquent
# sans message qui nomme la cause.
#
# CE QUE CE CHANGEMENT NE COUVRE PAS. La durabilité perdue n'est pas remplacée
# ici : avec un courtier, la perte du disque perd les messages non consommés. Ce
# qui protège le dépôt reste `outbox_messages`, en base, sur un autre serveur.
# Le jour où un second nœud rejoint le cluster, remettre 3 et 2 ci-dessous ET
# dans le calque prod, puis régénérer.
REPLICAS = {"dev": 1, "staging": 1, "prod": 1}
MIN_ISR = {"dev": 1, "staging": 1, "prod": 1}

# Sept jours, comme avant : c'est la rétention que le §9 demande sur un topic
# métier. Elle est UNIFORME parce que le sujet l'est aussi — un sujet par
# producteur mélange les paiements et les positions GPS, et on ne peut pas garder
# les uns trente jours et les autres une heure. C'est le §19.2 qui débloquera ça.
RETENTION_MS = "604800000"


def defaut(source: str, propriete: str) -> str:
    """La valeur par défaut d'une propriété de `KafkaEventBusOptions`."""
    trouve = re.search(
        r'public\s+string\s+' + propriete + r'\s*\{[^}]*\}\s*=\s*"([^"]+)"', source)
    if not trouve:
        raise SystemExit(
            f"✗ {propriete} n'a plus de valeur par défaut littérale dans "
            f"{os.path.relpath(OPTIONS_CS, RACINE)} — ce script en dépend.")
    return trouve.group(1)


def domaines() -> tuple[list[str], str, str, int]:
    """Les domaines DISTINCTS de la table, plus le préfixe et la version."""
    with io.open(TOPICS_CS, encoding="utf-8") as flux:
        table = flux.read()
    with io.open(OPTIONS_CS, encoding="utf-8") as flux:
        options = flux.read()

    entrees = ENTREE.findall(table)
    if not entrees:
        raise SystemExit(
            f"✗ Aucune entrée trouvée dans {os.path.relpath(TOPICS_CS, RACINE)}. "
            "Le format a changé ? Attendu : [\"nom-service\"] = \"domaine\",")

    prefixe = defaut(options, "TopicPrefix")
    version = defaut(options, "TopicVersion")

    # DISTINCT, PARCE QUE DEUX SERVICES PEUVENT PARTAGER UN DOMAINE.
    #
    # C'est déjà le cas dans le processus financier : payments, wallet et billing
    # sont co-hébergés et publient tous sous `financial`. Provisionner deux fois le
    # même nom ferait un doublon de manifeste, que Kubernetes refuse.
    distincts = sorted({domaine for _, domaine in entrees})
    return distincts, prefixe, version, len(entrees)


def entete(env: str, total: int) -> str:
    return f"""# ═══════════════════════════════════════════════════════════════════════════════
# TOPICS KAFKA — ENVIRONNEMENT « {env} » (§9).
#
# FICHIER GÉNÉRÉ PAR `scripts/k8s-kafka-topics.py`. NE PAS ÉDITER À LA MAIN.
#
# CETTE LISTE EST LE MIROIR DE `HbaTopics.DomaineParService`.
#
# Un sujet par DOMAINE DISTINCT de la table, sous la forme
# `{{préfixe}}.{{domaine}}.{{version}}` — les défauts de `KafkaEventBusOptions`. C'est
# exactement ce que `KafkaIntegrationEventPublisher` écrit et ce à quoi
# `KafkaIntegrationEventConsumer` s'abonne : trois sources d'un même nom, une seule
# dérivation. Ajouter un service à la table et régénérer, jamais éditer ici.
#
# `scripts/check-kafka-topics.py` vérifie que ce fichier n'a pas divergé.
#
# UN SUJET ABSENT D'ICI N'ÉCHOUE PAS — IL EST CRÉÉ SANS CE QU'ON VOULAIT.
#
# `auto.create.topics.enable` vaut vrai : le premier producteur fait naître son
# sujet, avec UNE partition, la réplication par défaut et la rétention par défaut.
# Rien ne casse, tout paraît marcher, et la perte d'un courtier perd des messages.
# C'est le défaut le plus silencieux de tout le plan de données — et c'est
# précisément ce qui s'est passé pour les {total} sujets réellement employés, pendant
# que ce fichier en provisionnait vingt-huit autres que personne n'écrivait.
#
# LE SCHÉMA `hba.{env}.<domaine>.<agrégat>.v1` QUI ÉTAIT ICI N'ÉTAIT PAS UNE ERREUR.
#
# C'était le §19.2 du cahier — un sujet par AGRÉGAT, l'environnement dans le nom —
# provisionné AVANT que le runtime ne le suive. `HbaEventNaming` construit ces
# noms ; aucun appelant ne l'invoque encore. Le §19.2 RESTE LA CIBLE : il est ce
# qui permettra de garder les paiements trente jours et les positions GPS une
# heure, ce qu'un sujet par producteur interdit. Le jour où il sera branché, cette
# liste changera — nom, nombre, rétention par agrégat. Ce fichier décrit
# l'existant, pas l'ambition.
#
# AUCUN `.dlq` : RIEN NE PUBLIE VERS UN SUJET DE LETTRES MORTES.
#
# Les quatorze `.dlq` provisionnés jusqu'ici n'ont jamais reçu de message, et ne
# pouvaient pas en recevoir : le seul code qui sache en fabriquer le nom est
# `HbaEventNaming.DeadLetterTopic`, sans appelant. Les lettres mortes du dépôt
# vivent en base — `outbox_messages.DeadLetteredOnUtc`, posé par
# `OutboxRetryPolicy`, surveillé par la métrique `OutboxDeadLetter`. Côté
# consommation, `KafkaIntegrationEventConsumer` journalise « ÉVÉNEMENT ABANDONNÉ »
# après ses tentatives et passe au suivant : il ne réémet rien.
#
# Les provisionner quand même donnerait une garantie fausse — un exploitant qui
# cherche un message perdu irait regarder un topic vide au lieu de la table outbox.
# Ils reviendront ici le jour où un vrai routage DLQ existera, et ce jour-là c'est
# le §19.2 qui dira lesquels.
#
# LES TROIS ENVIRONNEMENTS PORTENT LES MÊMES NOMS DE SUJETS.
#
# `service.<domaine>.v1` ne contient pas l'environnement, parce que le runtime ne
# l'y met pas. L'isolation tient au cluster et au namespace, pas au nom — ce qui
# veut dire qu'un courtier partagé entre deux environnements les mélangerait sans
# rien dire. Le §19.2 remet l'environnement dans le nom, et c'est une de ses
# raisons d'être.
#
# Régénérer après tout ajout de service au catalogue :
#     python3 scripts/k8s-kafka-topics.py
# ═══════════════════════════════════════════════════════════════════════════════
"""


def rendre(env: str) -> str:
    distincts, prefixe, version, _ = domaines()

    blocs = []
    for domaine in distincts:
        sujet = f"{prefixe}.{domaine}.{version}"
        blocs.append(f"""---
apiVersion: kafka.strimzi.io/v1beta2
kind: KafkaTopic
metadata:
  name: {sujet}
  labels:
    strimzi.io/cluster: kafka
spec:
  topicName: {sujet}
  # Le nombre de partitions borne le parallélisme d'un groupe de consommateurs :
  # à une partition, un second pod du service resterait inactif. On peut en
  # ajouter, jamais en retirer — et l'ajout casse l'ordre par clé du déjà-écrit.
  partitions: 3
  replicas: {REPLICAS[env]}
  config:
    retention.ms: "{RETENTION_MS}"
    compression.type: lz4
    min.insync.replicas: "{MIN_ISR[env]}"
""")

    return entete(env, len(distincts)) + "".join(blocs)


def chemin(env: str) -> str:
    return os.path.join(RACINE, "k8s", "overlays", env, "kafka-topics.yaml")


def main() -> int:
    verifie = "--verifie" in sys.argv
    perimes: list[str] = []

    for env in ENVIRONNEMENTS:
        attendu = rendre(env)
        cible = chemin(env)

        actuel = io.open(cible, encoding="utf-8").read() if os.path.isfile(cible) else ""

        if actuel == attendu:
            continue

        if verifie:
            perimes.append(os.path.relpath(cible, RACINE))
        else:
            os.makedirs(os.path.dirname(cible), exist_ok=True)
            io.open(cible, "w", encoding="utf-8").write(attendu)
            print(f"  écrit {os.path.relpath(cible, RACINE)}")

    distincts, prefixe, version, services = domaines()

    if verifie and perimes:
        print(f"❌ {len(perimes)} fichier(s) de topics périmé(s) :")
        for p in perimes:
            print(f"     {p}")
        print("     → python3 scripts/k8s-kafka-topics.py")
        return 1

    print(f"  {services} service(s) → {len(distincts)} domaine(s) distinct(s) : "
          f"{len(distincts)} topics par environnement, sans DLQ "
          f"({prefixe}.<domaine>.{version}).")
    return 0


if __name__ == "__main__":
    sys.exit(main())
