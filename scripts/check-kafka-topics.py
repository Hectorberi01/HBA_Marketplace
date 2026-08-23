#!/usr/bin/env python3
"""
═══════════════════════════════════════════════════════════════════════════════
TROIS ENDROITS NOMMENT LES SUJETS KAFKA. ILS DOIVENT DIRE LA MÊME CHOSE.

CE DÉFAUT NE CASSE RIEN — IL REND DES ÉVÉNEMENTS INERTES.

C'est ISSUE-001. Le producteur dérivait son sujet de `SERVICE_NAME`, le
consommateur s'abonnait à une liste écrite en dur, et les manifestes Kubernetes
provisionnaient un troisième schéma. Un message part, le courtier l'acquitte, et
il n'arrive nulle part. Aucune exception, aucun avertissement, aucune métrique en
rouge : le seul symptôme est « ce service ne reçoit rien », des jours plus tard,
et il ne désigne jamais le fichier fautif.

`HbaTopics` a fermé les deux premières sources en n'en laissant qu'une. Ce
contrôle empêche la troisième de re-diverger, et surveille ce que le catalogue ne
peut pas voir tout seul :

  1. `HbaTopics.DomaineParService` — la table qui fait foi.
  2. `docker-compose.dev.yml` — un service qui PUBLIE et qui manque à la table
     publie sur un sujet auquel personne n'est abonné. Le repli de
     `HbaTopics.Domaine` fabrique un nom plausible, ce qui rend le défaut plus
     discret encore : le sujet existe, il est juste seul.
  3. `k8s/overlays/*/kafka-topics.yaml` — les sujets provisionnés doivent être
     EXACTEMENT ceux que la table engendre. Un sujet en trop coûte du stockage et
     ment ; un sujet en moins est auto-créé par le courtier avec UNE partition et
     la rétention par défaut, donc sans les garanties du §9.

UN `SERVICE_NAME` N'EST PAS UNE PREUVE DE PUBLICATION.

Les trois BFF et la passerelle en ont un et ne publient rien. Les quatre
squelettes food retirés par D30 (`menu`, `availability`, `kitchen-prep`,
`food-review`) ont même un `KAFKA__PRODUCER` dans le compose, et pas une ligne de
code qui publie. Les exiger dans la table ferait provisionner des sujets pour du
code qui va disparaître — et un contrôle qui crie à tort finit ignoré.

On cherche donc une TRACE de publication dans le dossier du service :
`IIntegrationEventPublisher`, l'outbox, un événement d'intégration. Sans trace, le
service est listé À PART, en information, jamais en échec. Ce qu'on ne sait pas
trancher, on le montre.

CE CONTRÔLE NE REGARDE PAS LES DÉPLOIEMENTS KUBERNETES DE LA MÊME FAÇON.

Là-bas les `SERVICE_NAME` portent des noms de DOMAINE (`merchant-service`,
`commerce-service`…) et la découpe est plus grossière qu'en développement. Aucun
n'est une clé de la table : tous passent par le repli, qui tombe juste par
construction — `merchant-service` → `merchant`. Le sujet est donc correct, mais
chaque pod journalise « producteur non inscrit » au premier événement. C'est
listé en information, pas en échec : voir la fin du rapport.

Usage :
    python3 scripts/check-kafka-topics.py
═══════════════════════════════════════════════════════════════════════════════
"""
from __future__ import annotations

import os
import re
import sys

RACINE = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
COMPOSE = os.path.join(RACINE, "docker-compose.dev.yml")
KAFKA = os.path.join(RACINE, "shared", "common", "HBA.Shared.Infrastructure", "Kafka")
TOPICS_CS = os.path.join(KAFKA, "HbaTopics.cs")
OPTIONS_CS = os.path.join(KAFKA, "KafkaEventBusOptions.cs")
OVERLAYS = os.path.join(RACINE, "k8s", "overlays")
K8S_BASE = os.path.join(RACINE, "k8s", "base")

# `["seller-service"] = "merchant",` — la forme exacte des entrées de la table.
ENTREE = re.compile(r'\["([A-Za-z0-9._-]+)"\]\s*=\s*"([A-Za-z0-9._-]+)"')

# CES MARQUEURS DISENT « CE SERVICE PUBLIE », PAS « CE SERVICE EST COMPLET ».
#
# Ils sont volontairement larges : un faux positif ici ne coûte qu'une entrée de
# plus dans la table — donc un sujet provisionné pour rien. Un faux négatif, lui,
# rendrait un vrai producteur invisible au contrôle, ce qui est exactement le
# défaut qu'on traque.
MARQUEURS = (
    "IIntegrationEventPublisher",
    "IntegrationEvent",
    "OutboxMessage",
    "AddOutbox",
)

IGNORE = ("/obj/", "/bin/", "/Migrations/", "/node_modules/")


def table() -> dict[str, str]:
    """`HbaTopics.DomaineParService`, lue dans le `.cs`."""
    with open(TOPICS_CS, encoding="utf-8") as flux:
        return dict(ENTREE.findall(flux.read()))


def defauts() -> tuple[str, str]:
    """Le préfixe et la version par défaut de `KafkaEventBusOptions`."""
    with open(OPTIONS_CS, encoding="utf-8") as flux:
        source = flux.read()

    valeurs = []
    for propriete in ("TopicPrefix", "TopicVersion"):
        trouve = re.search(
            r"public\s+string\s+" + propriete + r'\s*\{[^}]*\}\s*=\s*"([^"]+)"', source)
        if not trouve:
            print(f"❌ {propriete} n'a plus de valeur par défaut littérale — "
                  "le contrôle ne peut plus dériver les noms de sujets.")
            return "", ""
        valeurs.append(trouve.group(1))

    return valeurs[0], valeurs[1]


def publie(dossier: str) -> bool:
    """Le dossier d'un service contient-il une trace de publication ?"""
    for chemin, _, fichiers in os.walk(dossier):
        if any(x in chemin.replace(os.sep, "/") + "/" for x in IGNORE):
            continue
        for nom in fichiers:
            if not nom.endswith(".cs"):
                continue
            with open(os.path.join(chemin, nom), encoding="utf-8", errors="ignore") as flux:
                texte = flux.read()
            if any(marqueur in texte for marqueur in MARQUEURS):
                return True
    return False


def producteurs_compose(compose: dict) -> list[tuple[str, str, str, bool]]:
    """(conteneur, producteur déclaré, dossier, publie ?) pour chaque service bâti."""
    trouves = []

    for nom, service in (compose.get("services") or {}).items():
        environnement = {str(k): str(v) for k, v in (service.get("environment") or {}).items()}

        # `Kafka:Producer` prime sur `SERVICE_NAME` — c'est l'ordre de
        # `KafkaEventNaming.Producer`, appelé par le publieur.
        producteur = environnement.get("KAFKA__PRODUCER") or environnement.get("SERVICE_NAME")
        if not producteur:
            continue

        build = service.get("build")
        dossier = ""
        if isinstance(build, dict) and build.get("dockerfile"):
            dossier = os.path.join(RACINE, os.path.dirname(build["dockerfile"]))

        trouves.append((nom, producteur, dossier,
                        bool(dossier) and os.path.isdir(dossier) and publie(dossier)))

    return sorted(trouves)


def sujets_overlay(chemin: str, yaml) -> list[str]:
    with open(chemin, encoding="utf-8") as flux:
        documents = [d for d in yaml.safe_load_all(flux) if d]

    return sorted(
        str((d.get("spec") or {}).get("topicName") or (d.get("metadata") or {}).get("name"))
        for d in documents
        if d.get("kind") == "KafkaTopic")


def service_names_k8s() -> dict[str, str]:
    """Les `SERVICE_NAME` posés par les kustomizations de `k8s/base`."""
    trouves = {}
    motif = re.compile(r"name:\s*SERVICE_NAME\s*\n\s*value:\s*([A-Za-z0-9._-]+)")

    for dossier, _, fichiers in os.walk(K8S_BASE):
        # `_service/` EST UN GABARIT, PAS UN SERVICE.
        #
        # Son `SERVICE_NAME` vaut littéralement « service » — un placeholder que
        # chaque kustomization remplace par un patch. Le lire ici ferait un
        # orphelin permanent, et un contrôle qui crie toujours finit ignoré.
        if "_service" in dossier.replace(os.sep, "/").split("/"):
            continue
        for nom in fichiers:
            if not nom.endswith((".yaml", ".yml")):
                continue
            chemin = os.path.join(dossier, nom)
            with open(chemin, encoding="utf-8", errors="ignore") as flux:
                for valeur in motif.findall(flux.read()):
                    trouves[valeur] = os.path.relpath(chemin, RACINE)

    return trouves


def applicatif(service_name: str) -> bool:
    """
    Ce `SERVICE_NAME` désigne-t-il un service applicatif ?

    UN MANIFESTE NE DIT PAS SI LE POD PUBLIE.

    Le nom d'un déploiement ne dit pas quel processus tourne dedans — la
    passerelle en est l'exemple : `api-gateway` porte un `SERVICE_NAME` et ne
    publie rien. On ne fait donc échouer que ce qui porte le suffixe
    « -service », seule convention que ce dépôt tienne pour un service métier.
    """
    return service_name.endswith("-service")


def main() -> int:
    try:
        import yaml
    except ImportError:
        # Même parti pris que check-k8s.py et check-service-addresses.py : un outil
        # absent se signale, il ne fait pas échouer la chaîne.
        print("  PyYAML absent — contrôle ignoré (pip install pyyaml).")
        return 0

    catalogue = table()
    if not catalogue:
        print(f"❌ {os.path.relpath(TOPICS_CS, RACINE)}")
        print("     aucune entrée trouvée — le format de la table a-t-il changé ?")
        print('     attendu : ["nom-service"] = "domaine",')
        return 1

    prefixe, version = defauts()
    if not prefixe:
        return 1

    attendus = sorted({f"{prefixe}.{domaine}.{version}" for domaine in catalogue.values()})
    ecarts = 0

    print(f"  {len(catalogue)} service(s) au catalogue → {len(attendus)} sujet(s) "
          f"« {prefixe}.<domaine>.{version} ».")

    # ── 1. Les producteurs du compose ─────────────────────────────────────────
    with open(COMPOSE, encoding="utf-8") as flux:
        compose = yaml.safe_load(flux)

    absents = []
    sans_trace = []

    for conteneur, producteur, dossier, trace in producteurs_compose(compose):
        if producteur in catalogue:
            continue
        if trace:
            absents.append((conteneur, producteur))
        else:
            sans_trace.append((conteneur, producteur))

    print()
    print("  ── Producteurs du compose absents du catalogue")
    if absents:
        for conteneur, producteur in absents:
            derive = producteur.replace("-service", "")
            print(f"❌ {conteneur}")
            print(f"     « {producteur} » publie et n'est pas dans HbaTopics.DomaineParService")
            print(f"     ses événements partiraient sur « {prefixe}.{derive}.{version} », "
                  "auquel personne ne s'abonne")
        ecarts += len(absents)
    else:
        print("     rien à signaler.")

    if sans_trace:
        # INFORMATIF, ET C'EST DÉLIBÉRÉ.
        #
        # Un `SERVICE_NAME` sans une ligne qui publie décrit un BFF, la passerelle
        # ou un squelette. Les compter en échec obligerait à inscrire au catalogue
        # des services qui ne publieront peut-être jamais — et à provisionner leurs
        # sujets en production.
        print()
        print("  ── Déclarés producteurs, aucune trace de publication dans le code")
        print("     (BFF, passerelle, squelettes : à trancher à la main, pas un échec)")
        for conteneur, producteur in sans_trace:
            print(f"       ⓘ {conteneur} — SERVICE_NAME/KAFKA__PRODUCER « {producteur} »")

    # ── 2. Les manifestes Kubernetes ──────────────────────────────────────────
    print()
    print("  ── Sujets provisionnés (k8s/overlays/*/kafka-topics.yaml)")

    fichiers = sorted(
        os.path.join(OVERLAYS, env, "kafka-topics.yaml")
        for env in os.listdir(OVERLAYS)
        if os.path.isfile(os.path.join(OVERLAYS, env, "kafka-topics.yaml")))

    if not fichiers:
        print("❌ aucun fichier de topics trouvé sous k8s/overlays/")
        ecarts += 1

    for chemin in fichiers:
        relatif = os.path.relpath(chemin, RACINE)
        declares = sujets_overlay(chemin, yaml)

        manquants = [s for s in attendus if s not in declares]
        surnumeraires = [s for s in declares if s not in attendus]

        if not manquants and not surnumeraires:
            print(f"     ✓ {relatif} — {len(declares)} sujet(s), conforme au catalogue")
            continue

        print(f"❌ {relatif}")
        for sujet in manquants:
            print(f"     {sujet} absent — le courtier le créera avec UNE partition "
                  "et la rétention par défaut")
        for sujet in surnumeraires:
            print(f"     {sujet} provisionné — aucun service du catalogue ne l'écrit")
        print(f"     → python3 scripts/k8s-kafka-topics.py")
        ecarts += len(manquants) + len(surnumeraires)

    # ── 3. Les `SERVICE_NAME` des déploiements, en information ────────────────
    #
    # Le repli de `HbaTopics.Domaine` retire « -service » : tant qu'il retombe sur
    # un domaine du catalogue, le sujet est le bon et il ne reste qu'un
    # avertissement au démarrage. S'il retombe AILLEURS, c'est un vrai orphelin —
    # et là on échoue.
    domaines = set(catalogue.values())
    replis, orphelins = [], []

    for valeur, fichier in sorted(service_names_k8s().items()):
        if valeur in catalogue:
            continue
        derive = valeur.replace("-service", "")
        (replis if derive in domaines else orphelins).append((valeur, derive, fichier))

    orphelins = [o for o in orphelins if applicatif(o[0])]

    if orphelins:
        print()
        print("  ── SERVICE_NAME Kubernetes hors catalogue ET hors domaines")
        for valeur, derive, fichier in orphelins:
            print(f"❌ {fichier}")
            print(f"     SERVICE_NAME « {valeur} » → « {prefixe}.{derive}.{version} », "
                  "sujet qu'aucun consommateur n'écoute")
        ecarts += len(orphelins)

    if replis:
        print()
        print("  ── SERVICE_NAME Kubernetes qui passent par le repli")
        print("     (le sujet tombe juste ; le publieur journalise « producteur non "
              "inscrit » à chaque démarrage)")
        for valeur, derive, _ in replis:
            print(f"       ⓘ {valeur} → {prefixe}.{derive}.{version}")

    print()
    print(f"{len(fichiers)} overlay(s), {len(catalogue)} service(s) au catalogue, "
          f"{ecarts} divergence(s).")
    return 1 if ecarts else 0


if __name__ == "__main__":
    sys.exit(main())
