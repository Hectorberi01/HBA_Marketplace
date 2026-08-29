#!/usr/bin/env python3
"""Engendre un Job de migration par service deploye, a partir de son kustomization.

═══════════════════════════════════════════════════════════════════════════════
POURQUOI CE SCRIPT PLUTOT QUE SIX FICHIERS ECRITS A LA MAIN.

Un Job de migration doit recevoir EXACTEMENT le meme cablage que le Deployment
du service : la meme image, la meme chaine de connexion, les memes Secrets. Ecrit
a la main, il diverge — quelqu'un ajoute une cle au Deployment et l'oublie dans le
Job, et la migration tourne contre une configuration qui n'est plus celle du
service. Le symptome arrive plus tard, ailleurs.

Ici, le Job est DERIVE du kustomization du service : les variables d'environnement
sont recopiees telles quelles depuis le patch de Deployment.

CE QUE CES JOBS NE COUVRENT PAS :
  - ils ne sont pas idempotents au sens Kubernetes : un Job termine ne se
    relance pas. Rejouer une migration demande de le supprimer d'abord. C'est
    documente dans le RUNBOOK, et c'est voulu — un Job qui se relance tout seul
    migrerait a un moment que personne n'a choisi.
  - ils ne verrouillent rien entre eux. Deux Jobs du meme service lances
    ensemble s'appuient sur le verrou consultatif d'EF, pas sur ce fichier.
  - ils ne semment rien : le compte administrateur reste amorce au demarrage
    normal d'identity-service, ou il est idempotent.
═══════════════════════════════════════════════════════════════════════════════
"""

import os
import re
import sys

RACINE = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
SERVICES_DIR = os.path.join(RACINE, "k8s", "base", "services")
SORTIE_DIR = os.path.join(RACINE, "k8s", "base", "migrations")

ENTETE = """# ═══════════════════════════════════════════════════════════════════════════════
# ENGENDRE PAR scripts/generer-jobs-migration.py — NE PAS EDITER A LA MAIN.
#
# Le cablage vient de k8s/base/services/{service}/kustomization.yaml. Modifier le
# service, puis relancer le script : une divergence entre le Job et le Deployment
# ferait migrer contre une configuration qui n'est plus celle du service.
# ═══════════════════════════════════════════════════════════════════════════════
"""


def lire_services_deployes():
    """Les services non commentes dans k8s/base/services/kustomization.yaml."""
    chemin = os.path.join(SERVICES_DIR, "kustomization.yaml")
    with open(chemin, encoding="utf-8") as f:
        contenu = f.read()
    services = []
    for ligne in contenu.splitlines():
        if ligne.lstrip().startswith("#"):
            continue
        m = re.match(r"^\s*-\s+([a-z0-9-]+-service)\s*$", ligne)
        if m:
            services.append(m.group(1))
    return services


def extraire_env(service):
    """Le bloc `env:` du patch de Deployment, recopie tel quel."""
    chemin = os.path.join(SERVICES_DIR, service, "kustomization.yaml")
    with open(chemin, encoding="utf-8") as f:
        lignes = f.read().splitlines()

    debut = None
    for i, l in enumerate(lignes):
        if re.match(r"^\s*env:\s*$", l):
            debut = i
            break
    if debut is None:
        return None, "aucun bloc env: dans %s" % chemin

    indentation = len(lignes[debut]) - len(lignes[debut].lstrip())
    corps = []
    for l in lignes[debut + 1:]:
        if l.strip() and (len(l) - len(l.lstrip())) <= indentation:
            break
        corps.append(l)

    # On reindente a 12 espaces : le Job a une structure moins profonde que le
    # patch kustomize.
    reindente = []
    for l in corps:
        if not l.strip():
            reindente.append("")
            continue
        reindente.append(" " * 12 + l[indentation + 2:])
    return "\n".join(reindente).rstrip(), None


def image_du_service(service):
    """`hba/<service>` — le nom logique. Les overlays le remplacent par le vrai."""
    chemin = os.path.join(SERVICES_DIR, service, "kustomization.yaml")
    with open(chemin, encoding="utf-8") as f:
        contenu = f.read()
    m = re.search(r"^\s*-\s*name:\s*(hba/service)\s*$", contenu, re.MULTILINE)
    if m is None:
        return None
    return "hba/service"


def engendrer(service):
    env, erreur = extraire_env(service)
    if erreur:
        return None, erreur

    return ENTETE.format(service=service) + """apiVersion: batch/v1
kind: Job
metadata:
  name: migration
  labels:
    app.kubernetes.io/component: migration
spec:
  # Une seule tentative supplementaire. Une migration qui echoue deux fois
  # echoue pour une raison qu'un troisieme essai ne changera pas, et chaque
  # reprise rouvre la fenetre de concurrence.
  backoffLimit: 1

  # Le Job disparait une heure apres sa fin. Assez pour lire ses journaux,
  # assez peu pour ne pas accumuler des objets termines dans le namespace.
  ttlSecondsAfterFinished: 3600

  template:
    metadata:
      labels:
        app.kubernetes.io/component: migration
    spec:
      restartPolicy: Never

      securityContext:
        runAsNonRoot: true
        runAsUser: 1654
        runAsGroup: 1654
        fsGroup: 1654
        seccompProfile:
          type: RuntimeDefault

      containers:
        - name: migration
          image: hba/service:latest

          envFrom:
            - configMapRef:
                name: hba-platform
            - secretRef:
                name: hba-platform

          env:
            # LA CLE DE TOUT CE JOB. Sans elle, le conteneur demarre un serveur
            # web qui ecoute indefiniment : le Job reste `Running`, et
            # `kubectl wait --for=condition=complete` expire sur une migration
            # pourtant reussie.
            - name: DATABASE__MIGRATEONLY
              value: "true"
            - name: ASPNETCORE_ENVIRONMENT
              value: Production
""" + env + """

          resources:
            requests:
              cpu: 50m
              memory: 128Mi
            limits:
              memory: 512Mi
""", None


def main():
    services = lire_services_deployes()
    print("%d service(s) deploye(s) : %s" % (len(services), ", ".join(services)))

    os.makedirs(SORTIE_DIR, exist_ok=True)
    ressources = []
    anomalies = []

    for service in services:
        contenu, erreur = engendrer(service)
        if erreur:
            anomalies.append(erreur)
            continue
        dossier = os.path.join(SORTIE_DIR, service)
        os.makedirs(dossier, exist_ok=True)
        with open(os.path.join(dossier, "job.yaml"), "w", encoding="utf-8") as f:
            f.write(contenu)
        with open(os.path.join(dossier, "kustomization.yaml"), "w", encoding="utf-8") as f:
            f.write("""apiVersion: kustomize.config.k8s.io/v1beta1
kind: Kustomization

# Le meme prefixe que le service : `migration` devient `%s-migration`, et le Job
# se lit a cote du Deployment qu'il precede.
namePrefix: %s-

labels:
  - includeSelectors: true
    pairs:
      app.kubernetes.io/name: %s

resources:
  - job.yaml

images:
  - name: hba/service
    newName: hba/%s
""" % (service, service.replace("-service", ""), service, service))
        ressources.append(service)

    with open(os.path.join(SORTIE_DIR, "kustomization.yaml"), "w", encoding="utf-8") as f:
        f.write("""# ═══════════════════════════════════════════════════════════════════════════════
# LES JOBS DE MIGRATION NE SONT PAS DANS `k8s/base` PAR DEFAUT.
#
# Ce kustomization existe pour etre applique SEUL, avant les services :
#
#     kubectl apply -k k8s/migrations-prod
#
# L'inclure dans la base ferait tourner une migration a chaque `apply -k`, donc
# a chaque changement de configuration sans rapport. Une migration se declenche,
# elle ne se subit pas.
#
# ENGENDRE PAR scripts/generer-jobs-migration.py — NE PAS EDITER A LA MAIN.
# ═══════════════════════════════════════════════════════════════════════════════
apiVersion: kustomize.config.k8s.io/v1beta1
kind: Kustomization

resources:
""" + "".join("  - %s\n" % s for s in ressources))

    print("%d Job(s) engendre(s) dans k8s/base/migrations/" % len(ressources))
    for a in anomalies:
        print("  ANOMALIE " + a, file=sys.stderr)
    return 1 if anomalies else 0


if __name__ == "__main__":
    sys.exit(main())
