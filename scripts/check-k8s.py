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

import glob
import os
import re
import shutil
import subprocess
import sys

# PyYAML sert au contrôle des cibles de patch, qui tourne SANS kustomize. Son
# absence ne doit pas faire échouer le reste : on dégrade, on ne casse pas.
try:
    import yaml
except ImportError:  # pragma: no cover
    yaml = None  # type: ignore[assignment]

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


def version_kustomize() -> tuple[int, ...] | None:
    """La version majeure de kustomize, ou None si elle ne se lit pas."""
    try:
        sortie = subprocess.run(["kustomize", "version"], capture_output=True, text=True, timeout=10)
    except Exception:
        return None
    brut = (sortie.stdout + sortie.stderr)
    # v4 rend « {Version:kustomize/v4.5.4 GitCommit:... } », v5 rend « v5.4.3 ».
    trouve = re.search(r"v(\d+)\.(\d+)\.(\d+)", brut)
    return tuple(int(x) for x in trouve.groups()) if trouve else None


def objets_de_base() -> set[tuple[str, str]]:
    """
    (kind, nom) que la base produira, en appliquant les `namePrefix` à la main.

    Volontairement APPROXIMATIF ET SUFFISANT : on ne réimplémente pas kustomize,
    on reconstitue la seule chose dont le contrôle ci-dessous a besoin — la liste
    des noms qu'une cible de patch peut légitimement désigner.
    """
    produits: set[tuple[str, str]] = set()

    for chemin in glob.glob(os.path.join(RACINE, "k8s", "base", "**", "*.yaml"), recursive=True):
        dossier = os.path.dirname(chemin)
        kustom = os.path.join(dossier, "kustomization.yaml")

        prefixe = ""
        if os.path.isfile(kustom):
            try:
                k = yaml.safe_load(open(kustom, encoding="utf-8")) or {}
                prefixe = k.get("namePrefix", "") or ""
            except Exception:
                prefixe = ""

        # Le gabarit `_service` n'est pas déployé tel quel : il est inclus par
        # chaque service, qui lui applique SON préfixe. On le traite donc depuis
        # les dossiers qui l'incluent, pas depuis lui-même.
        if os.path.basename(dossier) == "_service":
            continue

        try:
            texte = open(chemin, encoding="utf-8").read()
        except Exception:
            continue

        for doc in texte.split("\n---\n"):
            try:
                o = yaml.safe_load(doc)
            except Exception:
                continue
            if not isinstance(o, dict):
                continue
            kind = o.get("kind")
            nom = (o.get("metadata") or {}).get("name")
            if kind and nom:
                produits.add((kind, prefixe + nom))

    # Les objets du gabarit, une fois préfixés par chaque service qui l'inclut.
    gabarit = os.path.join(RACINE, "k8s", "base", "services", "_service")
    noyaux: set[tuple[str, str]] = set()
    for chemin in glob.glob(os.path.join(gabarit, "*.yaml")):
        for doc in open(chemin, encoding="utf-8").read().split("\n---\n"):
            try:
                o = yaml.safe_load(doc)
            except Exception:
                continue
            if isinstance(o, dict) and o.get("kind") and (o.get("metadata") or {}).get("name"):
                noyaux.add((o["kind"], o["metadata"]["name"]))

    # ON CHERCHE QUI INCLUT LE GABARIT, ON NE SUPPOSE PAS OÙ IL EST INCLUS.
    #
    # Une première version ne regardait que `k8s/base/services/*`. Elle a signalé
    # `gateway-service` comme introuvable — un FAUX POSITIF : la passerelle vit
    # dans `k8s/base/apps/gateway/` et inclut le même gabarit avec son propre
    # préfixe. Un contrôle qui invente des fautes se fait désactiver, et emporte
    # avec lui les vraies.
    for kustom in glob.glob(
            os.path.join(RACINE, "k8s", "base", "**", "kustomization.yaml"), recursive=True):
        dossier = os.path.dirname(kustom)
        if os.path.basename(dossier) == "_service":
            continue

        try:
            k = yaml.safe_load(open(kustom, encoding="utf-8")) or {}
        except Exception:
            continue

        inclut_gabarit = any(
            "_service" in str(r) for r in (k.get("resources") or []))

        if not inclut_gabarit:
            continue

        prefixe = k.get("namePrefix", "") or ""
        for kind, nom in noyaux:
            produits.add((kind, prefixe + nom))

    return produits


def verifier_cibles_de_patch() -> list[str]:
    """
    ═════════════════════════════════════════════════════════════════════════════
    UNE CIBLE DE PATCH QUI NE DÉSIGNE RIEN NE FAIT PAS ÉCHOUER LE BUILD.

    Kustomize applique un patch aux objets qui correspondent à sa cible. Zéro
    correspondance n'est PAS une erreur : le build réussit, l'objet n'est pas
    modifié, et rien ne le dit. Un `name:` mal orthographié, un service renommé,
    un patch écrit pour un objet retiré du dépôt — les trois donnent la même
    sortie qu'un patch qui a mordu.

    C'est exactement le défaut qui a laissé dix HPA de production à
    `minReplicas: 1` pendant que le dépôt affichait `replicas: 2` : le patch du
    Deployment existait, celui du HPA n'existait pas. Ici on vérifie l'inverse —
    qu'aucun patch ne vise le vide — ce qui attrape le renommage et la coquille.

    NE REMPLACE PAS `kustomize build`, et ne prétend pas le faire : ce contrôle
    tourne SANS kustomize, c'est tout son intérêt sur un poste qui ne l'a pas.
    ═════════════════════════════════════════════════════════════════════════════
    """
    produits = objets_de_base()
    fautes: list[str] = []

    for overlay in OVERLAYS:
        chemin = os.path.join(RACINE, "k8s", "overlays", overlay, "kustomization.yaml")
        if not os.path.isfile(chemin):
            continue

        try:
            k = yaml.safe_load(open(chemin, encoding="utf-8")) or {}
        except Exception as erreur:
            fautes.append(f"{overlay} : kustomization illisible ({erreur})")
            continue

        for patch in k.get("patches", []) or []:
            cible = patch.get("target") or {}
            kind, nom = cible.get("kind"), cible.get("name")

            # Sans nom, la cible vise TOUS les objets de ce genre : rien à
            # résoudre, et c'est un usage légitime (le patch de `maxReplicas`).
            if not kind or not nom:
                continue

            # Le patch de Namespace renomme l'objet lui-même : il désigne le nom
            # d'AVANT, qui est bien celui de la base.
            if (kind, nom) not in produits:
                fautes.append(
                    f"{overlay} : le patch vise {kind}/{nom}, qu'aucun objet de la base ne produit "
                    "— kustomize l'appliquera à RIEN, sans erreur.")

    return fautes


def verifier_montages_de_secret() -> list[str]:
    """
    ═════════════════════════════════════════════════════════════════════════════
    UN FICHIER MONTÉ DEPUIS UN SECRET S'ACCORDE EN QUATRE POINTS.

    Quand un service lit un secret sous forme de FICHIER — le compte de service
    Firebase de notification-service est le premier — quatre valeurs doivent
    concorder, écrites dans DEUX fichiers différents :

      1. le `secretName` du volume  ↔  le `metadata.name` du Secret ;
      2. le `name` du volume        ↔  le `name` du volumeMount ;
      3. le `mountPath`             ↔  le répertoire du chemin passé au service ;
      4. le nom du fichier attendu  ↔  une CLÉ du Secret.

    AUCUN DES QUATRE NE CASSE AU DÉPLOIEMENT S'IL EST FAUX. Le pod démarre, le
    fichier n'est pas là où le service le cherche, `FcmOptions.ResolveJson()` rend
    `null`, et le refus de démarrage annonce « Notifications:Fcm n'est pas
    configuré » — alors qu'il l'est, et que seul un nom de fichier diffère. On
    cherche une variable manquante pendant que le secret est monté deux
    répertoires plus loin.

    Le quatrième est le plus traître : un volume de Secret projette chaque clé
    comme un fichier PORTANT LE NOM DE LA CLÉ. Renommer la clé dans le Secret
    déplace le fichier, en silence.
    ═════════════════════════════════════════════════════════════════════════════
    """
    fautes: list[str] = []

    # Les Secrets déclarés dans la base, par nom → clés.
    secrets: dict[str, set[str]] = {}
    for chemin in glob.glob(os.path.join(RACINE, "k8s", "**", "*.yaml"), recursive=True):
        try:
            docs = list(yaml.safe_load_all(open(chemin, encoding="utf-8")))
        except Exception:
            continue
        for o in docs:
            if isinstance(o, dict) and o.get("kind") == "Secret":
                nom = (o.get("metadata") or {}).get("name")
                if nom:
                    secrets[nom] = set(o.get("stringData") or {}) | set(o.get("data") or {})

    for kustom in glob.glob(
            os.path.join(RACINE, "k8s", "base", "**", "kustomization.yaml"), recursive=True):
        try:
            k = yaml.safe_load(open(kustom, encoding="utf-8")) or {}
        except Exception:
            continue

        for patch in k.get("patches", []) or []:
            corps = patch.get("patch")
            if not corps or ("volumeMounts" not in corps and "secretKeyRef" not in corps):
                continue
            try:
                o = yaml.safe_load(corps)
            except Exception:
                continue
            if not isinstance(o, dict):
                continue

            spec = (((o.get("spec") or {}).get("template") or {}).get("spec") or {})
            volumes = {v.get("name"): v for v in (spec.get("volumes") or [])}

            for conteneur in spec.get("containers") or []:
                env = {e.get("name"): e.get("value")
                       for e in (conteneur.get("env") or []) if e.get("value")}

                # ── `secretKeyRef` : la clé doit exister dans le Secret ──────
                #
                # Cet échec-ci est BRUYANT — le pod reste en
                # `CreateContainerConfigError` avec la clé nommée dans
                # l'événement — contrairement au montage en fichier, qui échoue
                # en silence. On le vérifie quand même : le voir en revue coûte
                # moins que de le voir sur un cluster à moitié déployé.
                for e in conteneur.get("env") or []:
                    ref = ((e.get("valueFrom") or {}).get("secretKeyRef") or {})
                    nom_secret, cle = ref.get("name"), ref.get("key")
                    if not nom_secret or not cle:
                        continue
                    if nom_secret not in secrets:
                        fautes.append(
                            f"{os.path.relpath(kustom, RACINE)} : {e.get('name')} lit le Secret "
                            f"« {nom_secret} », qu'aucun manifeste de `k8s/` ne déclare.")
                    elif cle not in secrets[nom_secret]:
                        fautes.append(
                            f"{os.path.relpath(kustom, RACINE)} : {e.get('name')} lit la clé "
                            f"« {cle} » du Secret « {nom_secret} », qui ne porte que "
                            f"{sorted(secrets[nom_secret])}. Le pod restera en "
                            "CreateContainerConfigError.")

                for mount in conteneur.get("volumeMounts") or []:
                    nom_vol = mount.get("name")
                    chemin_mont = mount.get("mountPath")
                    volume = volumes.get(nom_vol)

                    # Un volume non déclaré : le pod ne démarre pas du tout.
                    if volume is None:
                        fautes.append(
                            f"{os.path.relpath(kustom, RACINE)} : le montage « {nom_vol} » "
                            "ne correspond à aucun volume déclaré.")
                        continue

                    secret = (volume.get("secret") or {}).get("secretName")
                    if secret is None:
                        continue  # emptyDir, configMap… hors sujet ici

                    if secret not in secrets:
                        fautes.append(
                            f"{os.path.relpath(kustom, RACINE)} : le volume « {nom_vol} » monte "
                            f"le Secret « {secret} », qu'aucun manifeste de `k8s/` ne déclare.")
                        continue

                    # Une variable qui pointe DANS ce montage doit nommer une clé.
                    for var, valeur in env.items():
                        if not valeur.startswith(str(chemin_mont) + "/"):
                            continue
                        fichier = valeur[len(str(chemin_mont)) + 1:]
                        if fichier not in secrets[secret]:
                            fautes.append(
                                f"{os.path.relpath(kustom, RACINE)} : {var} attend le fichier "
                                f"« {fichier} », mais le Secret « {secret} » ne porte que "
                                f"{sorted(secrets[secret])}. Un volume de Secret nomme chaque "
                                "fichier d'après SA CLÉ — le service ne trouvera rien, et "
                                "annoncera une configuration absente.")

    return fautes


def verifier_chaines_de_connexion() -> list[str]:
    """Le generateur du Secret et le gabarit versionne doivent declarer les memes cles.

    ═════════════════════════════════════════════════════════════════════════
    CE QUI ETAIT CASSE, ET POURQUOI CE CONTROLE EXISTE.

    Le Secret `hba-platform` n'est PAS construit par kustomize : §12 impose que
    `secret.yaml` reste vide dans Git, et les valeurs reelles sont posees hors
    depot par `scripts/db/secret-depuis-motsdepasse.py`. Deux fichiers portent
    donc la meme liste de treize cles, et rien ne les tenait ensemble.

    Une cle ajoutee au gabarit sans l'etre au generateur donne un Secret
    incomplet : le service demarre, lit une chaine vide, et la panne remonte en
    « Npgsql: host cannot be null » loin de la cause. L'inverse — une cle du
    generateur absente du gabarit — donne une cle poussee au cluster que
    personne ne sait relire.

    Le controle verifie aussi que le generateur derive l'utilisateur de la base.
    `secret.yaml` documente « UNE ROLE PAR BASE » ; onze chaines ont porte
    `Username=hector`, le superutilisateur. Le cloisonnement pose par
    `creer-bases.sh` serait reste decoratif : tout aurait demarre, tous les
    essais de connexion auraient reussi, et le premier service compromis aurait
    lu les quatorze bases.

    CE QUE CE CONTROLE NE COUVRE PAS.

    Il lit deux fichiers du depot. Il ne va pas voir le Secret reellement
    applique au cluster, ne verifie pas qu'un role existe cote Postgres, et ne
    dit rien de ses droits reels.
    ═════════════════════════════════════════════════════════════════════════
    """
    fautes: list[str] = []
    gabarit = os.path.join(RACINE, "k8s", "base", "common", "secret.yaml")
    generateur = os.path.join(RACINE, "scripts", "db", "secret-depuis-motsdepasse.py")

    for chemin in (gabarit, generateur):
        if not os.path.exists(chemin):
            return [chemin + " est introuvable"]

    with open(gabarit, encoding="utf-8") as f:
        lignes_gabarit = [l for l in f.read().splitlines()
                          if not l.lstrip().startswith("#")]
    cles_gabarit = set(re.findall(r"^\s+(CONNECTIONSTRINGS__[A-Z]+)\s*:",
                                 "\n".join(lignes_gabarit), re.MULTILINE))
    # DEFAULT est declaree pour la passerelle, qui n'a pas de base : le
    # generateur la pose a vide sans entree dans sa table.
    cles_gabarit.discard("CONNECTIONSTRINGS__DEFAULT")

    with open(generateur, encoding="utf-8") as f:
        source = f.read()
    table = re.search(r"^CLES = \[(.*?)^\]", source, re.MULTILINE | re.DOTALL)
    if table is None:
        return ["la table CLES est introuvable dans " + generateur]
    paires = re.findall(r'\(\s*"(CONNECTIONSTRINGS__[A-Z]+)"\s*,\s*"(hba_[a-z]+)"\s*\)',
                        table.group(1))
    cles_generateur = {c for c, _ in paires}

    if not paires:
        fautes.append("aucune paire lue dans la table CLES — le format a change, "
                      "ce controle ne verifie plus rien")

    for cle in sorted(cles_gabarit - cles_generateur):
        fautes.append(cle + " : declaree dans secret.yaml, absente de la table CLES "
                            "du generateur — la cle serait poussee vide")
    for cle in sorted(cles_generateur - cles_gabarit):
        fautes.append(cle + " : construite par le generateur, absente de secret.yaml "
                            "— personne ne sait qui la lit")

    # LE NAMESPACE PAR DEFAUT DU GENERATEUR DOIT ETRE CELUI DE L'OVERLAY PROD.
    #
    # La base declare `hba` ; chaque overlay le renomme (`hba-prod`, `hba-staging`,
    # `hba-dev`). Un generateur qui ecrit `hba` en dur pose le Secret dans un
    # namespace que personne ne lit — et si ce namespace existe, kubectl ne dit
    # rien : la panne apparait plus tard, en CreateContainerConfigError sur un
    # Secret introuvable.
    overlay = os.path.join(RACINE, "k8s", "overlays", "prod", "kustomization.yaml")
    if os.path.exists(overlay):
        with open(overlay, encoding="utf-8") as f:
            attendu = re.search(r"^namespace:\s*(\S+)", f.read(), re.MULTILINE)
        defaut = re.search(r'NAMESPACE = os\.environ\.get\(\s*"[A-Z_]+"\s*,\s*"([^"]+)"\s*\)',
                           source)
        if attendu is None or defaut is None:
            fautes.append("namespace de prod illisible — dans l'overlay ou dans le "
                          "generateur ; ce controle ne verifie plus rien")
        elif attendu.group(1) != defaut.group(1):
            fautes.append("le generateur pose le Secret dans le namespace %s alors que "
                          "l'overlay prod deploie dans %s — le Secret serait pose la ou "
                          "personne ne le lit"
                          % (defaut.group(1), attendu.group(1)))

    # L'utilisateur doit etre derive de la base, pas ecrit en dur.
    forme = re.search(
        r'"Host=%s;Port=%s;Database=%s;Username=%s;Password=%s"\s*%\s*\(\s*\n?\s*([^)]*)\)',
        source)
    if forme is None:
        fautes.append("la construction de la chaine de connexion n'a pas la forme "
                      "attendue dans le generateur — controle de l'utilisateur impossible")
    else:
        arguments = [a.strip() for a in forme.group(1).split(",") if a.strip()]
        # HOTE, PORT, base, base, secret : le 3e et le 4e doivent etre identiques.
        if len(arguments) != 5 or arguments[2] != arguments[3]:
            fautes.append("le generateur ne derive pas Username de Database "
                          "(arguments : %s) — un role unique passerait outre le "
                          "cloisonnement" % ", ".join(arguments))

    return fautes


def verifier_secrets_vides() -> list[str]:
    """Aucun fichier `k8s/base/common/secret*.yaml` ne doit porter de valeur.

    ═════════════════════════════════════════════════════════════════════════
    CE QUI EST ARRIVE.

    Une cle d'API Resend en clair s'est retrouvee dans `secret.yaml` — posee le
    temps d'un essai, et restee. Elle n'a pas ete commitee, mais elle etait a un
    `git add -A` de l'historique. Un secret entre une fois dans l'historique se
    revoque ; il ne se retire pas.

    §12 dit depuis le debut que ces fichiers restent vides dans Git. Rien ne
    l'imposait : la regle vivait dans un commentaire.

    CE QUE CE CONTROLE NE COUVRE PAS.

    Il ne lit que `k8s/base/common/secret*.yaml`. Un secret pose dans un
    ConfigMap, dans un overlay, ou dans n'importe quel autre fichier du depot
    passe au travers. Il ne regarde pas non plus l'historique : il dit ce qui
    est la maintenant, pas ce qui y a ete.
    ═════════════════════════════════════════════════════════════════════════
    """
    fautes: list[str] = []
    motif = os.path.join(RACINE, "k8s", "base", "common", "secret*.yaml")
    fichiers = sorted(glob.glob(motif))
    if not fichiers:
        return ["aucun fichier ne correspond a " + motif +
                " — ce controle ne verifie plus rien"]

    for chemin in fichiers:
        with open(chemin, encoding="utf-8") as f:
            for numero, ligne in enumerate(f, 1):
                if ligne.lstrip().startswith("#"):
                    continue
                m = re.match(r'^\s{2,}([A-Za-z0-9_.-]+)\s*:\s*(.*)$', ligne.rstrip("\n"))
                if not m:
                    continue
                cle, reste = m.group(1), m.group(2)
                if cle in ("name", "namespace", "app.kubernetes.io/name",
                           "app.kubernetes.io/part-of"):
                    continue
                # La valeur est entre guillemets ; ce qui suit est un commentaire.
                # C'est exactement le piege qui a fait rendre « quatorze valeurs
                # non vides » a une premiere version de ce controle, alors que le
                # fichier ne portait que des commentaires de fin de ligne.
                q = re.match(r'^"([^"]*)"', reste)
                if q is not None:
                    valeur = q.group(1)
                elif reste.startswith("#") or not reste.strip():
                    continue
                else:
                    valeur = reste.split("#")[0].strip()
                if valeur:
                    fautes.append("%s:%d : %s porte une valeur de %d caractere(s) — "
                                  "ces fichiers restent vides dans Git (§12)"
                                  % (os.path.relpath(chemin, RACINE), numero, cle,
                                     len(valeur)))
    return fautes


def main() -> int:
    # ═════════════════════════════════════════════════════════════════════════
    # CE CONTRÔLE-CI TOURNE MÊME SANS KUSTOMIZE, ET C'EST VOLONTAIRE.
    #
    # Il est placé AVANT le garde-fou ci-dessous : sur un poste sans kustomize —
    # le cas courant — tout ce fichier rendait 0 sans rien vérifier. Une cible de
    # patch qui ne désigne rien se lit dans les fichiers, sans construire quoi que
    # ce soit.
    # ═════════════════════════════════════════════════════════════════════════
    cibles = verifier_chaines_de_connexion()
    cibles += verifier_secrets_vides()
    cibles += verifier_cibles_de_patch() if yaml is not None else []
    cibles += verifier_montages_de_secret() if yaml is not None else []
    if yaml is None:
        print("   PyYAML absent — contrôle des cibles de patch ignoré (pip install pyyaml).")

    if cibles:
        print("❌ Contrôles statiques du dossier k8s")
        for faute in cibles:
            print(f"     {faute}")
        print()

    if not shutil.which("kustomize"):
        print("   kustomize absent — le reste du contrôle est ignoré.")
        print("     https://kubectl.docs.kubernetes.io/installation/kustomize/")
        # Non bloquant : l'outil n'est pas une dépendance de compilation.
        return 1 if cibles else 0

    # ═════════════════════════════════════════════════════════════════════════
    # LA VERSION SE VÉRIFIE ICI, SINON L'ÉCHEC DÉSIGNE LE MAUVAIS COUPABLE.
    #
    # `k8s/base/services/*/kustomization.yaml` emploie le transformateur `labels`
    # avec `includeTemplates`, apparu en kustomize 5. Sur une version 4, le build
    # échoue sur « json: unknown field "includeTemplates" » — un message qui
    # envoie chercher une faute de frappe dans le YAML, alors que le YAML est
    # correct et que c'est l'outil qui est trop ancien.
    #
    # `kubectl apply -k` embarque sa propre copie (v5 depuis kubectl 1.28) : un
    # poste peut donc très bien déployer correctement et voir ce contrôle échouer,
    # à cause d'un binaire `kustomize` installé séparément et jamais mis à jour.
    # ═════════════════════════════════════════════════════════════════════════
    version = version_kustomize()
    if version is not None and version[0] < 5:
        v = ".".join(str(x) for x in version)
        print(f"   kustomize {v} est trop ancien — contrôle ignoré (il en faut 5 ou plus).")
        print("     Le transformateur `labels` avec `includeTemplates` n'existe qu'à partir de la 5.")
        print("     `kubectl apply -k` embarque sa propre copie et n'est PAS concerné.")
        print("     https://kubectl.docs.kubernetes.io/installation/kustomize/")
        return 1 if cibles else 0

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
    return 1 if (total or cibles) else 0


if __name__ == "__main__":
    sys.exit(main())
