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


def service_a_des_migrations(service):
    """Le service appelle-t-il MigrateHbaDatabaseAsync ?

    ═════════════════════════════════════════════════════════════════════════
    UN JOB POUR UN SERVICE SANS MIGRATION NE SE TERMINE JAMAIS.

    `SortirApresMigrations()` n'est inséré que dans les Program.cs qui migrent.
    Un service qui porte une chaîne de connexion mais aucune migration n'a pas
    cette sortie : son conteneur démarrerait un serveur web, le Job resterait
    `Running`, et `kubectl wait --for=condition=complete` expirerait sur une
    étape qui n'avait rien à faire.

    La panne serait d'autant plus déroutante que tout va bien : le service
    fonctionne, il écoute, il répond. C'est le Job qui n'a pas de sens.
    ═════════════════════════════════════════════════════════════════════════
    """
    racines = [os.path.join(RACINE, "services", d) for d in ("common", "marketplace",
                                                             "delivery", "food")]
    for racine in racines:
        dossier = os.path.join(racine, service)
        if not os.path.isdir(dossier):
            continue
        for base, _, fichiers in os.walk(dossier):
            if "Program.cs" not in fichiers:
                continue
            with open(os.path.join(base, "Program.cs"), encoding="utf-8") as f:
                if "MigrateHbaDatabaseAsync" in f.read():
                    return True
    return False


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

      # LE MEME COMPTE QUE LE SERVICE, ET C'EST NECESSAIRE.
      #
      # Le secret de tirage `ghcr` est porte par le ServiceAccount de chaque
      # service (`_service/serviceaccount.yaml`). Un Job sans
      # `serviceAccountName` tourne sous `default`, qui ne le porte pas : le Job
      # resterait en ImagePullBackOff alors que le Deployment du meme service
      # tire la meme image sans difficulte — une difference que rien ne designe.
      #
      # `namePrefix` du kustomization renomme `service` en `<service>`, donc ce
      # nom-ci suit automatiquement.
      serviceAccountName: service

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
    deployes = lire_services_deployes()
    services = [s for s in deployes if service_a_des_migrations(s)]
    sans = [s for s in deployes if s not in services]
    print("%d service(s) deploye(s), %d avec migrations : %s"
          % (len(deployes), len(services), ", ".join(services)))
    if sans:
        print("%d sans migration, donc sans Job : %s" % (len(sans), ", ".join(sans)))

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

# `includeSelectors: false` — ET C'EST OBLIGATOIRE SUR UN JOB.
#
# Le gabarit des services emploie `includeSelectors: true`, ce qui est correct
# pour un Deployment : kustomize ecrit alors le label dans `spec.selector`.
#
# Sur un Job, `spec.selector` est engendre par le controleur et porte un
# `controller-uid` que rien d'autre ne connait. Y ecrire un selecteur a la main
# demande `manualSelector: true` — et sans lui, l'API refuse le Job, ou pire,
# l'accepte et le controleur n'adopte jamais les pods qu'il cree. Un Job qui
# existe, ne cree aucun pod, et ne dit rien.
labels:
  - includeSelectors: false
    pairs:
      app.kubernetes.io/name: %s

resources:
  - job.yaml

  # LE COMPTE DE SERVICE VIENT AVEC LE JOB, ET C'EST NECESSAIRE POUR DEUX
  # RAISONS DISTINCTES.
  #
  # 1. LA REFERENCE NE SE REECRIT QUE SI L'OBJET EST LA. `job.yaml` porte
  #    `serviceAccountName: service`, et `namePrefix` ne le renomme en
  #    `<service>` que si le ServiceAccount est dans CETTE kustomization.
  #    Sans lui, le nom reste litteralement `service` et le controleur de Job
  #    refuse : « error looking up service account hba-prod/service:
  #    serviceaccount "service" not found ». Le Job existe, ne cree AUCUN pod,
  #    et `kubectl logs` repond « no pods found » — ce qui envoie chercher du
  #    cote du service.
  #
  # 2. LES MIGRATIONS TOURNENT AVANT LE DEPLOIEMENT. Les comptes de service
  #    sont crees par `overlays/prod`, qui n'a pas encore ete applique quand les
  #    Jobs demarrent. Meme avec le bon nom, le compte n'existerait pas.
  #
  # C'EST UN REPERTOIRE, PAS UN FICHIER. Kustomize refuse un fichier hors du
  # dossier de la kustomization (« security; file ... is not in or below ... »)
  # mais accepte un repertoire qui porte sa propre kustomization. Le compte reste
  # donc defini une seule fois, partage avec `_service` — une copie divergerait
  # au premier changement, et elle porte `imagePullSecrets: [ghcr]`.
  # Appliquer ce ServiceAccount ici puis `overlays/prod` ensuite pose deux fois
  # le meme objet — `apply` est idempotent, il n'y a pas de conflit.
  - ../../services/_service/compte

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
#     kubectl apply -k k8s/overlays/migrations-prod
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

    # ═══════════════════════════════════════════════════════════════════════
    # L'OVERLAY DES MIGRATIONS EST ENGENDRE LUI AUSSI.
    #
    # Il portait sa liste d'images a la main. Ajouter un service au lot laissait
    # donc le Job sans image de production : `check-k8s.py` le refusait — bien —
    # mais il fallait aller corriger un second fichier dont personne ne se
    # souvenait. Les deux listes ne peuvent plus diverger : celle-ci est derivee
    # de l'overlay prod, filtree sur les services qui ont un Job.
    #
    # CE QUE CA NE COUVRE PAS : le TAG vient de l'overlay prod tel qu'il est au
    # moment ou ce script tourne. Promouvoir apres avoir engendre les Jobs laisse
    # cet overlay en arriere — c'est pour ca que `cd.yml` pose le tag dans les
    # deux, et que `check-k8s.py` verifie leur accord.
    # ═══════════════════════════════════════════════════════════════════════
    chemin_prod = os.path.join(RACINE, "k8s", "overlays", "prod", "kustomization.yaml")
    chemin_migr = os.path.join(RACINE, "k8s", "overlays", "migrations-prod",
                               "kustomization.yaml")
    # L'ABSENCE DE L'UN DES DEUX FICHIERS EST UNE ANOMALIE, PAS UN CAS NORMAL.
    #
    # Ce `if` etait MUET dans les deux sens : un calque renomme, deplace ou
    # simplement absent faisait sauter l'etape, et le script annoncait quand
    # meme sa reussite. Les dix-huit Jobs seraient restes sur
    # `hba/<service>:latest`, un tag qui n'est dans aucun registre — et le
    # symptome ne serait apparu qu'au `kubectl apply`, en ImagePullBackOff, qui
    # se lit comme un probleme de droits sur le registre.
    #
    # Les deux chemins existent aujourd'hui ; ce garde-fou est la pour le jour
    # ou l'un des deux bougera.
    for chemin in (chemin_prod, chemin_migr):
        if not os.path.exists(chemin):
            anomalies.append(
                "%s est absent : les Jobs restent sur des images non publiees"
                % os.path.relpath(chemin, RACINE))

    if os.path.exists(chemin_prod) and os.path.exists(chemin_migr):
        with open(chemin_prod, encoding="utf-8") as f:
            images_prod = re.findall(
                r'  - name: (hba/[a-z0-9-]+)\n    newName: (\S+)\n'
                r'    newTag:[ \t]*"?([^"\n]*?)"?[ \t]*$',
                f.read(),
                re.MULTILINE)
        vises = {"hba/" + s for s in ressources}
        retenues = [(n, nn, t) for (n, nn, t) in images_prod if n in vises]

        with open(chemin_migr, encoding="utf-8") as f:
            contenu = f.read()
        entete = contenu.split("images:")[0].rstrip("\n")
        bloc = "images:\n" + "".join(
            '  - name: %s\n    newName: %s\n    newTag: "%s"\n' % e for e in retenues)
        with open(chemin_migr, "w", encoding="utf-8") as f:
            f.write(entete + "\n\n" + bloc)
        print("overlay migrations-prod : %d image(s) posee(s)" % len(retenues))
        manquantes = sorted(vises - {n for n, _, _ in images_prod})
        for m in manquantes:
            anomalies.append("%s a un Job mais aucune image dans l'overlay prod" % m)

    print("%d Job(s) engendre(s) dans k8s/base/migrations/" % len(ressources))
    for a in anomalies:
        print("  ANOMALIE " + a, file=sys.stderr)
    return 1 if anomalies else 0


if __name__ == "__main__":
    sys.exit(main())
