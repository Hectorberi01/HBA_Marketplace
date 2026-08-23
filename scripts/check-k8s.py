#!/usr/bin/env python3
"""
═══════════════════════════════════════════════════════════════════════════════
LES MANIFESTS DISENT-ILS ENCORE CE QUE LE CAHIER INFRASTRUCTURE EXIGE ?

CE CONTRÔLE CONSTRUIT VRAIMENT LES OVERLAYS, IL NE LIT PAS LES FICHIERS.

Lire `base/` ne prouve rien : c'est l'overlay qui décide, et un patch peut
défaire silencieusement ce que la base garantissait. Un `op: replace` sur
`/spec/template/spec/containers/0/securityContext` passe la revue de code sans
qu'on le remarque, et le résultat ne se voit qu'en construisant.

Ce que le script vérifie, et pourquoi chaque règle existe :

  • non-root (§19)         — huit images du dépôt tournaient en root sans que rien
                             ne le signale ; le SecurityContext est ce qui l'empêche
                             de revenir.
  • trois sondes (§7)      — une readiness manquante fait recevoir du trafic à un
                             pod qui n'a pas fini de démarrer, pendant chaque
                             déploiement.
  • requests + limits (§7) — sans requests le scheduler place à l'aveugle ; sans
                             limits un pod affame ses voisins.
  • pas de `latest` en prod (§13) — un `kubectl apply` rejoué doit redéployer la
                             MÊME image, sinon le rollback ne veut rien dire.
  • deny-all présent (§5)  — sans lui, tout pod parle à tout pod.
  • aucun secret en clair (§12) — le pire des défauts silencieux : ça marche.

Usage :
    python3 scripts/check-k8s.py
    python3 scripts/check-k8s.py dev
═══════════════════════════════════════════════════════════════════════════════
"""
from __future__ import annotations

import os
import shutil
import subprocess
import sys

try:
    import yaml
except ImportError:  # pragma: no cover
    print("PyYAML absent — pip install pyyaml")
    sys.exit(1)

RACINE = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
OVERLAYS = ("dev", "staging", "prod")

# Placeholders attendus : ce sont des NOMS de clés, jamais des valeurs.
CLES_SECRETES = (
    "SIGNINGKEY", "APIKEY", "PASSWORD", "SECRET", "CONNECTIONSTRINGS",
)


def construire(overlay: str) -> list[dict]:
    chemin = os.path.join(RACINE, "k8s", "overlays", overlay)

    sortie = subprocess.run(
        ["kustomize", "build", chemin],
        capture_output=True, text=True, check=False)

    if sortie.returncode != 0:
        raise RuntimeError(f"kustomize build a échoué :\n{sortie.stderr.strip()}")

    return [d for d in yaml.safe_load_all(sortie.stdout) if d]


def verifier(overlay: str, objets: list[dict]) -> list[str]:
    fautes: list[str] = []

    deployments = [o for o in objets if o["kind"] == "Deployment"]
    if not deployments:
        fautes.append("aucun Deployment — l'overlay ne déploie rien")

    # LES StatefulSet AUSSI, ET C'EST UN OUBLI QUI ÉTAIT DÉJÀ EN PLACE.
    #
    # La première version de ce contrôle ne regardait que les Deployments. Redis
    # et MinIO sont des StatefulSets : ils échappaient donc entièrement à la
    # vérification non-root, alors que ce sont précisément les deux pods qui
    # tiennent des données. Un datastore en root est un plus mauvais défaut qu'un
    # service web en root.
    for d in deployments + [o for o in objets if o["kind"] == "StatefulSet"]:
        nom = d["metadata"]["name"]
        spec = d["spec"]["template"]["spec"]
        pod_sc = spec.get("securityContext", {})

        if not pod_sc.get("runAsNonRoot"):
            fautes.append(f"{nom} : runAsNonRoot absent (§19)")

        for c in spec.get("containers", []):
            sc = c.get("securityContext", {})

            if sc.get("allowPrivilegeEscalation") is not False:
                fautes.append(f"{nom} : allowPrivilegeEscalation n'est pas false (§19)")

            if "ALL" not in sc.get("capabilities", {}).get("drop", []):
                fautes.append(f"{nom} : capacités Linux non abandonnées (§19)")

            for sonde in ("livenessProbe", "readinessProbe", "startupProbe"):
                if sonde not in c:
                    fautes.append(f"{nom} : {sonde} absente (§7)")

            # La confusion qui redémarre un service en bonne santé.
            live = c.get("livenessProbe", {}).get("httpGet", {}).get("path")
            ready = c.get("readinessProbe", {}).get("httpGet", {}).get("path")

            if live and "ready" in live:
                fautes.append(
                    f"{nom} : la liveness sonde « {live} » — un service qui a perdu "
                    f"sa base serait redémarré en boucle pendant l'incident (§7)")

            if ready and ready.endswith("/live"):
                fautes.append(
                    f"{nom} : la readiness sonde « {ready} » — un pod dont la base "
                    f"est absente recevrait du trafic (§7)")

            res = c.get("resources", {})
            if not res.get("requests"):
                fautes.append(f"{nom} : requests absentes (§7)")
            if not res.get("limits"):
                fautes.append(f"{nom} : limits absentes (§7)")

            image = c.get("image", "")
            if overlay == "prod" and (image.endswith(":latest") or ":" not in image):
                fautes.append(
                    f"{nom} : image « {image} » sans tag immuable — un apply rejoué "
                    f"ne redéploierait pas la même version (§13)")

    politiques = [o for o in objets if o["kind"] == "NetworkPolicy"]
    noms = {p["metadata"]["name"] for p in politiques}

    if not any(p["spec"].get("podSelector") == {} and "Ingress" in p["spec"].get("policyTypes", [])
               for p in politiques):
        fautes.append("aucune NetworkPolicy deny-by-default sur l'ingress (§5)")

    # Couper l'egress sans rouvrir le DNS casse tout, et le symptôme ment.
    if any(p["spec"].get("podSelector") == {} and "Egress" in p["spec"].get("policyTypes", [])
           for p in politiques) and "allow-dns" not in noms:
        fautes.append(
            "egress refusé sans règle DNS — les services échoueraient à résoudre "
            "leurs dépendances, et l'erreur désignerait le service visé (§5)")

    for s in (o for o in objets if o["kind"] == "Secret"):
        donnees = {**s.get("stringData", {}), **s.get("data", {})}
        for cle, valeur in donnees.items():
            if valeur and any(m in cle.upper() for m in CLES_SECRETES):
                fautes.append(
                    f"Secret {s['metadata']['name']} : « {cle} » porte une valeur "
                    f"en clair — le §12 l'interdit, et Git la garde après suppression")

    return fautes


def hotes(objets: list[dict]) -> set[str]:
    """Tous les noms d'hôte servis par les Ingress d'un overlay."""
    trouves: set[str] = set()

    for ing in (o for o in objets if o["kind"] == "Ingress"):
        for regle in ing["spec"].get("rules", []):
            if regle.get("host"):
                trouves.add(regle["host"])
        for tls in ing["spec"].get("tls", []):
            trouves.update(tls.get("hosts", []))

    return trouves


def verifier_ingress(par_overlay: dict[str, set[str]]) -> list[str]:
    """
    DEUX ENVIRONNEMENTS NE DOIVENT JAMAIS PARTAGER UN NOM D'HÔTE (§2).

    Le §2 exige des namespaces, secrets, bases et buckets distincts par
    environnement. Un domaine partagé fait entrer du trafic de production dans un
    cluster de validation — et rien ne le signale avant les journaux, parce que
    les deux répondent 200.

    Un copier-coller d'overlay est le chemin le plus court vers ce défaut : on
    duplique `staging` pour créer `prod`, on change le namespace et les replicas,
    et on oublie l'hôte.
    """
    fautes: list[str] = []

    for a, hotes_a in par_overlay.items():
        for b, hotes_b in par_overlay.items():
            if a >= b:
                continue
            partages = hotes_a & hotes_b
            if partages:
                fautes.append(
                    f"{a} et {b} servent le même hôte {sorted(partages)} — "
                    f"le §2 exige des environnements séparés")

    return fautes


def main() -> int:
    if not shutil.which("kustomize"):
        print("   kustomize absent — contrôle ignoré.")
        print("     https://kubectl.docs.kubernetes.io/installation/kustomize/")
        # Non bloquant : l'outil n'est pas une dépendance de compilation.
        return 0

    voulus = [a for a in sys.argv[1:] if a in OVERLAYS] or list(OVERLAYS)
    total = 0
    par_overlay: dict[str, set[str]] = {}
    placeholders: list[str] = []

    for overlay in voulus:
        try:
            objets = construire(overlay)
        except RuntimeError as erreur:
            print(f"❌ {overlay} : {erreur}")
            total += 1
            continue

        par_overlay[overlay] = hotes(objets)
        placeholders += [f"{overlay} : {h}" for h in par_overlay[overlay]
                         if h.endswith(".example")]

        fautes = verifier(overlay, objets)
        deployments = sum(1 for o in objets if o["kind"] == "Deployment")

        if fautes:
            print(f"❌ {overlay} ({len(objets)} objets, {deployments} Deployments)")
            for faute in sorted(set(fautes)):
                print(f"     {faute}")
            total += len(set(fautes))
        else:
            print(f"✓ {overlay} — {len(objets)} objets, {deployments} Deployments")

    # LES TOPICS DOIVENT SUIVRE LES `[HbaEvent]` DU CODE.
    #
    # Un topic manquant n'échoue pas : le broker le crée à la volée avec UNE
    # partition et le facteur de réplication par défaut. Le service publie, tout
    # paraît fonctionner, et la perte d'un broker perd des messages. C'est le
    # défaut le plus silencieux de tout le data plane.
    generateur = os.path.join(RACINE, "scripts", "k8s-kafka-topics.py")
    if os.path.isfile(generateur):
        rendu = subprocess.run(
            [sys.executable, generateur, "--verifie"],
            capture_output=True, text=True, check=False)

        if rendu.returncode != 0:
            print()
            print("❌ Topics Kafka")
            for ligne in rendu.stdout.strip().splitlines():
                print(f"   {ligne}")
            total += 1

    croisees = verifier_ingress(par_overlay)
    if croisees:
        print()
        print("❌ Ingress")
        for faute in croisees:
            print(f"     {faute}")
        total += len(croisees)

    # INFORMATIF, PAS BLOQUANT — mais impossible à ne pas voir.
    #
    # `.example` est réservé (RFC 2606) et ne résoudra jamais : c'est ce qui rend
    # le placeholder honnête. Le faire échouer bloquerait le lot de quiconque ne
    # déploie pas, alors que le vrai domaine n'est pas encore décidé.
    if placeholders:
        print()
        print(f"  ⓘ {len(placeholders)} hôte(s) encore en domaine réservé — à remplacer avant tout déploiement :")
        for p in sorted(placeholders):
            print(f"       {p}")

    print()
    print(f"{len(voulus)} overlay(s) construit(s), {total} écart(s) au cahier.")
    return 1 if total else 0


if __name__ == "__main__":
    sys.exit(main())
