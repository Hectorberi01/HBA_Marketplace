#!/usr/bin/env python3
"""
═══════════════════════════════════════════════════════════════════════════════
L'INFRASTRUCTURE EST LE SEUL CODE DU DÉPÔT QUE PERSONNE N'EXÉCUTE EN BOUCLE.

ET C'EST CE QUI LA REND DANGEREUSE.

Un service cassé se voit au premier `dotnet build`. Un module Terraform cassé se
voit le jour où l'on provisionne — c'est-à-dire au pire moment possible, souvent
sous pression, souvent par quelqu'un qui ne l'a pas écrit. Entre-temps le fichier
a l'air correct, et personne n'a de raison d'en douter.

Ce contrôle est donc le substitut du `terraform plan` que ce dépôt ne peut pas
lancer (pas d'identifiants OVH), et du `ansible-playbook --check` qu'il ne peut
pas lancer non plus (pas de machines).

CE QU'IL VÉRIFIE — chacun correspond à une panne qui ne se signale pas :

  Terraform
    • le HCL se parse ;
    • chaque `module { source = ... }` désigne un dossier qui existe ;
    • chaque argument passé à un module correspond à une `variable` déclarée
      — un nom mal orthographié fait échouer `init`, mais bien plus tard ;
    • chaque variable SANS défaut du module est effectivement fournie ;
    • chaque `var.X` d'un fichier est déclaré dans SON dossier ;
    • chaque environnement a un `backend` distant, et un `key` qui lui est propre
      — deux environnements partageant la même clé d'état s'écrasent l'un l'autre,
      et le second `apply` DÉTRUIT les ressources du premier ;
    • aucun secret littéral (`*.tfvars` réels commités, mot de passe en clair).

  Ansible
    • le YAML se charge ;
    • chaque rôle nommé par un playbook existe ;
    • chaque `hosts:` désigne un groupe présent dans les inventaires d'exemple
      — un groupe inexistant ne produit PAS d'erreur : Ansible affiche
      « skipping: no hosts matched » et sort en 0. Le rôle n'a jamais tourné ;
    • chaque `notify:` désigne un handler qui existe — même défaut, même silence :
      le handler n'est simplement jamais appelé, et sshd n'est jamais rechargé ;
    • chaque `template: src:` existe dans `templates/` du rôle.

  Compose
    • chaque service déclare `OpenTelemetry__Endpoint` dans son bloc
      `environment:` — sans lui, `TelemetryExtensions` n'ajoute PAS
      l'exportateur OTLP et le service n'envoie ni trace, ni métrique, ni
      journal corrélé. Il démarre, il sert, il passe ses tests. Treize
      services sur quatorze étaient dans ce cas, et personne ne l'a vu
      pendant des semaines : c'est exactement la panne qui ne se signale pas
      que ce fichier existe pour attraper.

  Cohérence entre les deux
    • `--disable-network-policy` n'apparaît nulle part : il rendrait
      `k8s/base/policies/` inerte sans rien supprimer.

Usage :
    python3 scripts/check-infra.py
═══════════════════════════════════════════════════════════════════════════════
"""
from __future__ import annotations

import glob
import os
import re
import subprocess
import sys

RACINE = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
TERRAFORM = os.path.join(RACINE, "infra", "terraform")
ANSIBLE = os.path.join(RACINE, "infra", "ansible")
# `infra/docker/` A ETE RETIRE. CE CHEMIN POINTAIT SUR LUI.
#
# Le controle ci-dessous lisait `infra/docker/compose.*.yml` : une pile que
# ni le Makefile, ni les workflows, ni aucun script ne lancaient. Il etait
# vert sur un fichier mort pendant que la pile reellement demarree,
# `docker-compose.dev.yml`, n'etait jamais regardee.
COMPOSE = os.path.join(RACINE, "docker-compose.dev.yml")
INIT_SQL = os.path.join(RACINE, "infra", "postgres", "init",
                        "001-create-databases.sql")

REF_VAR = re.compile(r"\bvar\.([A-Za-z_][A-Za-z0-9_]*)")

# Les clés d'un bloc `module` qui ne sont pas des variables du module cible.
META_MODULE = {"source", "version", "count", "for_each", "providers", "depends_on"}

# Clés injectées par python-hcl2 lui-même, qui ne viennent pas du fichier.
INTERNES_HCL2 = {"__is_block__", "__comments__", "__start_line__", "__end_line__"}

# Un littéral qui ressemble à un secret. On cherche l'AFFECTATION, pas le mot :
# un commentaire qui parle de mots de passe est légitime, une valeur ne l'est pas.
SECRET_LITTERAL = re.compile(
    r'^\s*(password|secret|token|access_key|secret_key|private_key)\s*=\s*"[^"$]{6,}"',
    re.IGNORECASE | re.MULTILINE)


def court(chemin: str) -> str:
    return os.path.relpath(chemin, RACINE)


def denude(valeur):
    """
    python-hcl2 ≥ 5 CONSERVE LES GUILLEMETS DES ÉTIQUETTES ET DES LITTÉRAUX.

    Un bloc `variable "region"` ressort sous la clé `'"region"'`, guillemets
    compris. Sans ce nettoyage, AUCUN nom ne correspond jamais — et le contrôle
    signalerait vingt-quatre défauts inexistants tout en n'attrapant plus rien
    de réel. C'est la panne classique du contrôle qui crie sans regarder.
    """
    if isinstance(valeur, str) and len(valeur) >= 2 and valeur[0] == valeur[-1] == '"':
        return valeur[1:-1]
    return valeur


# ON RETIRE LES COMMENTAIRES AVANT DE CHERCHER UN DRAPEAU INTERDIT.
#
# Le rôle `k3s-serveur` porte un encadré qui explique pourquoi
# `--disable-network-policy` ne doit PAS être posé. Chercher dans le texte brut
# ferait échouer le contrôle sur sa propre documentation — et la correction
# évidente serait de supprimer l'encadré, c'est-à-dire exactement la mauvaise.
COMMENTAIRE = re.compile(r"(?:(?<=^)|(?<=\s))(?:#|//).*$", re.MULTILINE)


def sans_commentaires(texte: str) -> str:
    return COMMENTAIRE.sub("", texte)


# ═══════════════════════════════════════════════════════════════════════════════
# TERRAFORM
# ═══════════════════════════════════════════════════════════════════════════════

def blocs(document: dict, genre: str) -> dict:
    """`{'variable': [{'a': {...}}, {'b': {...}}]}` → `{'a': {...}, 'b': {...}}`."""
    rendu = {}
    for entree in document.get(genre, []) or []:
        if not isinstance(entree, dict):
            continue
        for etiquette, corps in entree.items():
            if etiquette in INTERNES_HCL2:
                continue
            rendu[denude(etiquette)] = corps
    return rendu


def variables_du_dossier(dossier: str, cache: dict) -> dict:
    """Les `variable` déclarées par TOUS les .tf d'un dossier."""
    if dossier in cache:
        return cache[dossier]

    import hcl2

    declarees: dict = {}
    for fichier in sorted(glob.glob(os.path.join(dossier, "*.tf"))):
        try:
            with open(fichier, encoding="utf-8") as f:
                declarees.update(blocs(hcl2.load(f), "variable"))
        except Exception:
            pass

    cache[dossier] = declarees
    return declarees


def controler_terraform() -> list[str]:
    try:
        import hcl2  # noqa: F401
    except ImportError:
        print("  python-hcl2 absent — partie Terraform ignorée "
              "(pip install python-hcl2).")
        return []

    import hcl2

    fautes: list[str] = []
    cache: dict = {}
    cles_etat: dict[str, str] = {}

    fichiers = sorted(glob.glob(os.path.join(TERRAFORM, "**", "*.tf"), recursive=True))
    if not fichiers:
        return ["infra/terraform : aucun fichier .tf"]

    for chemin in fichiers:
        dossier = os.path.dirname(chemin)

        try:
            with open(chemin, encoding="utf-8") as f:
                brut = f.read()
            document = hcl2.loads(brut)
            code = sans_commentaires(brut)
        except Exception as erreur:
            fautes.append(f"{court(chemin)} : HCL invalide — "
                          f"{str(erreur).splitlines()[0]}")
            continue

        if SECRET_LITTERAL.search(code):
            fautes.append(f"{court(chemin)} : une valeur ressemble à un secret "
                          f"en clair — les identifiants passent par "
                          f"l'environnement, jamais par le dépôt")

        if "--disable-network-policy" in code:
            fautes.append(f"{court(chemin)} : « --disable-network-policy » rendrait "
                          f"k8s/base/policies/ inerte SANS rien supprimer")

        # ── les `var.X` référencés sont-ils déclarés ici ? ────────────────────
        declarees = variables_du_dossier(dossier, cache)
        for nom in sorted(set(REF_VAR.findall(code))):
            if nom not in declarees:
                fautes.append(f"{court(chemin)} : `var.{nom}` n'est déclaré par "
                              f"aucun fichier de {court(dossier)}/")

        # ── l'état est-il distant, et propre à cet environnement ? ────────────
        for tf in document.get("terraform", []) or []:
            for backend in (tf.get("backend") or []):
                if not isinstance(backend, dict):
                    continue
                for etiquette, config in backend.items():
                    if etiquette in INTERNES_HCL2 or not isinstance(config, dict):
                        continue
                    genre = denude(etiquette)
                    if genre == "local":
                        fautes.append(f"{court(chemin)} : backend « local » — "
                                      f"l'état contient des secrets en clair et "
                                      f"deux opérateurs divergeraient sans le voir")
                        continue
                    cle = (f"{denude(config.get('bucket'))}/"
                           f"{denude(config.get('key'))}")
                    if cle in cles_etat:
                        fautes.append(
                            f"{court(chemin)} : même clé d'état que "
                            f"{cles_etat[cle]} ({cle}) — le second `apply` "
                            f"DÉTRUIRAIT les ressources du premier")
                    cles_etat[cle] = court(chemin)

        # ── le câblage des modules ────────────────────────────────────────────
        for nom, config in blocs(document, "module").items():
            source = denude(config.get("source"))
            if not isinstance(source, str):
                fautes.append(f"{court(chemin)} : module « {nom} » sans `source`")
                continue
            if not source.startswith("."):
                continue                      # module distant : hors de portée

            cible = os.path.normpath(os.path.join(dossier, source))
            if not os.path.isdir(cible):
                fautes.append(f"{court(chemin)} : module « {nom} » pointe "
                              f"{source}, qui n'existe pas")
                continue

            attendues = variables_du_dossier(cible, cache)
            fournis = {k for k in config
                       if k not in META_MODULE and k not in INTERNES_HCL2}

            for argument in sorted(fournis - set(attendues)):
                fautes.append(f"{court(chemin)} : module « {nom} » passe "
                              f"« {argument} », que {court(cible)} ne déclare pas")

            for var_nom, var_def in sorted(attendues.items()):
                if var_nom in fournis:
                    continue
                if isinstance(var_def, dict) and "default" in var_def:
                    continue
                fautes.append(f"{court(chemin)} : module « {nom} » ne fournit pas "
                              f"« {var_nom} », qui n'a pas de valeur par défaut")

    # ── les environnements ont-ils tous un backend ? ──────────────────────────
    for env in sorted(glob.glob(os.path.join(TERRAFORM, "environments", "*"))):
        if not os.path.isdir(env):
            continue
        with_backend = any(
            'backend "' in sans_commentaires(open(f, encoding="utf-8").read())
            for f in glob.glob(os.path.join(env, "*.tf")))
        if not with_backend:
            fautes.append(f"{court(env)} : aucun `backend` — l'état finirait sur "
                          f"le poste de qui applique")

    # ── un .tfvars réel commité ? ─────────────────────────────────────────────
    for fichier in glob.glob(os.path.join(TERRAFORM, "**", "*.tfvars"), recursive=True):
        fautes.append(f"{court(fichier)} : un .tfvars réel n'a rien à faire dans "
                      f"le dépôt (seuls les .tfvars.example)")

    if not fautes:
        print(f"  Terraform : {len(fichiers)} fichier(s), câblage des modules "
              f"cohérent, état distant.")
    return fautes


# ═══════════════════════════════════════════════════════════════════════════════
# ANSIBLE
# ═══════════════════════════════════════════════════════════════════════════════

def taches_a_plat(document) -> list[dict]:
    """Les tâches d'un fichier, `block`/`rescue` compris."""
    rendu: list[dict] = []
    pile = list(document or [])
    while pile:
        element = pile.pop()
        if not isinstance(element, dict):
            continue
        rendu.append(element)
        for imbrique in ("block", "rescue", "always", "tasks", "pre_tasks", "post_tasks"):
            pile.extend(element.get(imbrique) or [])
    return rendu


def controler_ansible() -> list[str]:
    try:
        import yaml
    except ImportError:
        print("  PyYAML absent — partie Ansible ignorée.")
        return []

    if not os.path.isdir(ANSIBLE):
        return []

    fautes: list[str] = []

    # ── ansible.cfg : des plugins retirés y survivent en silence ─────────────
    #
    # POURQUOI CE CONTRÔLE EXISTE.
    #
    # `stdout_callback = yaml` a fonctionné des années. Le nom court se
    # résolvait en `community.general.yaml`, supprimé en community.general
    # 12.0.0 — et le jour où la collection se met à jour, `ansible-playbook`
    # s'arrête AVANT la première tâche :
    #
    #   [ERROR]: The 'community.general.yaml' callback plugin has been removed.
    #
    # Rien dans le dépôt ne changeait ce jour-là. Le message parle d'un plugin
    # d'affichage, donc on cherche d'abord dans le playbook et l'inventaire.
    #
    # CE QUE CE CONTRÔLE NE COUVRE PAS : les autres plugins retirés, présents et
    # à venir. Il ne connaît que la liste ci-dessous, tenue à la main. Un nom
    # inconnu de cette liste passe — ce contrôle réduit la surprise, il ne la
    # supprime pas.
    RETIRES = {
        "yaml": "community.general 12.0.0 — remplacer par "
                "`stdout_callback = default` + `result_format = yaml` "
                "(ansible-core >= 2.13, aucune collection requise)",
        "community.general.yaml": "community.general 12.0.0 — remplacer par "
                "`stdout_callback = default` + `result_format = yaml`",
    }

    chemin_cfg = os.path.join(ANSIBLE, "ansible.cfg")
    if os.path.isfile(chemin_cfg):
        import configparser
        cfg = configparser.ConfigParser()
        try:
            cfg.read(chemin_cfg, encoding="utf-8")
            valeur = (cfg.get("defaults", "stdout_callback", fallback="") or "").strip()
            if valeur in RETIRES:
                fautes.append(
                    f"infra/ansible/ansible.cfg : `stdout_callback = {valeur}` est un "
                    f"plugin RETIRÉ ({RETIRES[valeur]}). `ansible-playbook` refuse de "
                    "démarrer, avant la première tâche.")
        except configparser.Error as erreur:
            fautes.append(f"infra/ansible/ansible.cfg illisible : {erreur}")

    fichiers = sorted(
        glob.glob(os.path.join(ANSIBLE, "**", "*.yml"), recursive=True) +
        glob.glob(os.path.join(ANSIBLE, "**", "*.yml.example"), recursive=True))

    charges: dict[str, object] = {}
    for chemin in fichiers:
        try:
            with open(chemin, encoding="utf-8") as f:
                brut = f.read()
            charges[chemin] = yaml.safe_load(brut)
        except yaml.YAMLError as erreur:
            fautes.append(f"{court(chemin)} : YAML invalide — "
                          f"{str(erreur).splitlines()[0]}")
            continue

        if "--disable-network-policy" in sans_commentaires(brut):
            fautes.append(f"{court(chemin)} : « --disable-network-policy » rendrait "
                          f"k8s/base/policies/ inerte SANS rien supprimer")

    roles_connus = {
        os.path.basename(d)
        for d in glob.glob(os.path.join(ANSIBLE, "roles", "*"))
        if os.path.isdir(d)
    }

    # Les groupes définis par les inventaires d'exemple.
    groupes = {"all", "localhost"}
    for chemin, document in charges.items():
        if os.sep + "inventory" + os.sep not in chemin:
            continue
        pile = [document]
        while pile:
            noeud = pile.pop()
            if not isinstance(noeud, dict):
                continue
            for cle, valeur in noeud.items():
                if cle in ("hosts", "vars"):
                    continue
                if cle == "children" and isinstance(valeur, dict):
                    groupes.update(valeur)
                    pile.extend(valeur.values())
                elif isinstance(valeur, dict):
                    groupes.add(cle)
                    pile.append(valeur)

        if os.path.basename(chemin) == "production.yml.example" and isinstance(document, dict):
            enfants = (((document.get("all") or {}).get("children") or {}))
            serveurs = (((enfants.get("serveurs") or {}).get("hosts") or {}))
            agents = (((enfants.get("agents") or {}).get("hosts") or {}))

            # La production ne doit plus documenter un faux cluster HA.
            #
            # Trois VMs avec un seul `serveur` et deux `agents` donnent de la
            # capacité, pas un quorum. Perdre le serveur coupe l'API k3s et la
            # replanification, exactement le scénario §24. Tant qu'aucun endpoint
            # stable 6443 n'existe, on garde les trois nœuds dans `serveurs`.
            if len(serveurs) < 3 or len(serveurs) % 2 == 0:
                fautes.append(
                    f"{court(chemin)} : la production doit déclarer un nombre impair "
                    "d'au moins 3 serveurs k3s pour former le quorum etcd")
            if agents:
                fautes.append(
                    f"{court(chemin)} : des agents sont déclarés alors qu'aucun "
                    "endpoint stable 6443 n'est provisionné ; les trois nœuds "
                    "de production doivent rester dans `serveurs` pour l'instant")

    # ── les playbooks ─────────────────────────────────────────────────────────
    for chemin in sorted(glob.glob(os.path.join(ANSIBLE, "playbooks", "*.yml"))):
        for jeu in charges.get(chemin) or []:
            if not isinstance(jeu, dict):
                continue

            cible = jeu.get("hosts")
            if isinstance(cible, str) and cible not in groupes:
                fautes.append(
                    f"{court(chemin)} : `hosts: {cible}` ne correspond à aucun "
                    f"groupe des inventaires — Ansible dirait « no hosts matched » "
                    f"et sortirait en 0, le jeu n'ayant jamais tourné")

            for role in jeu.get("roles") or []:
                nom = role if isinstance(role, str) else role.get("role")
                if nom and nom not in roles_connus:
                    fautes.append(f"{court(chemin)} : rôle « {nom} » introuvable "
                                  f"dans roles/")

    # ── les rôles ─────────────────────────────────────────────────────────────
    for role in sorted(roles_connus):
        base = os.path.join(ANSIBLE, "roles", role)

        taches_fichier = os.path.join(base, "tasks", "main.yml")
        if not os.path.isfile(taches_fichier):
            fautes.append(f"infra/ansible/roles/{role} : pas de tasks/main.yml")
            continue

        handlers: set[str] = set()
        handlers_fichier = os.path.join(base, "handlers", "main.yml")
        for handler in taches_a_plat(charges.get(handlers_fichier)):
            if "name" in handler:
                handlers.add(handler["name"])

        for tache in taches_a_plat(charges.get(taches_fichier)):
            avertis = tache.get("notify") or []
            if isinstance(avertis, str):
                avertis = [avertis]
            for avertissement in avertis:
                if avertissement not in handlers:
                    fautes.append(
                        f"infra/ansible/roles/{role} : `notify: {avertissement}` "
                        f"ne désigne aucun handler — il ne serait JAMAIS appelé, "
                        f"sans erreur")

            modele = (tache.get("ansible.builtin.template")
                      or tache.get("template") or {})
            source = modele.get("src") if isinstance(modele, dict) else None
            if source and not os.path.isfile(os.path.join(base, "templates", source)):
                fautes.append(f"infra/ansible/roles/{role} : template « {source} » "
                              f"absent de templates/")

    # ── un inventaire réel COMMITÉ ? ─────────────────────────────────────────
    #
    # CE CONTRÔLE SIGNALAIT LA PRÉSENCE DU FICHIER, ET C'ÉTAIT FAUX.
    #
    # Le `.example` dit lui-même « copier en `staging.yml` ». Un déploiement
    # SUPPOSE donc ce fichier sur le poste : le signaler faisait échouer
    # `make infra` dès qu'on suivait la procédure du dépôt. Un contrôle qui
    # passe au rouge quand on fait ce qu'il faut finit par être ignoré — et il
    # emmène les vrais défauts avec lui.
    #
    # Le danger n'est pas d'AVOIR le fichier, c'est de le COMMITTER. On
    # interroge donc Git, pas le disque.
    #
    # CE QUE CELA NE COUVRE PAS : un fichier ajouté à l'index sans commit passe
    # ici (`git ls-files` le voit — donc non, il est bien pris), mais un dépôt
    # absent ou un `git` introuvable rend le contrôle muet. Il est alors annoncé
    # comme tel plutôt que supposé vert.
    inventaires = glob.glob(os.path.join(ANSIBLE, "inventory", "*.yml"))

    if inventaires:
        try:
            suivis = subprocess.run(
                ["git", "ls-files", "--", os.path.join("infra", "ansible", "inventory")],
                cwd=RACINE, capture_output=True, text=True, timeout=20, check=True).stdout.split()
        except (OSError, subprocess.SubprocessError):
            fautes.append("infra/ansible/inventory : `git` indisponible — impossible de "
                          "vérifier qu'aucun inventaire réel n'est commité")
            suivis = None

        if suivis is not None:
            for chemin in sorted(suivis):
                if chemin.endswith(".yml") and not chemin.endswith(".yml.example"):
                    fautes.append(f"{chemin} : inventaire réel COMMITÉ — il porte des IP "
                                  "et des noms d'hôtes de production ; seuls les "
                                  "`.yml.example` doivent être suivis")

    # ═════════════════════════════════════════════════════════════════════════
    # TOUTE COLLECTION EMPLOYÉE DOIT ÊTRE DÉCLARÉE.
    #
    # `roles/commun` emploie `ansible.posix.sysctl` et `ansible.posix.mount`, qui
    # ne font pas partie d'`ansible-core`. Aucun `requirements.yml` ne les
    # déclarait : sur un poste où Ansible vient de `pip install ansible-core`, le
    # playbook s'arrêtait sur
    #
    #     couldn't resolve module/action 'ansible.posix.mount'
    #     Origin: roles/commun/tasks/main.yml:51:3
    #
    # LE MESSAGE DÉSIGNE UNE LIGNE DU RÔLE, ET LE RÔLE EST CORRECT — ce qui
    # manque est sur la machine, pas dans le dépôt. Et le défaut est
    # INTERMITTENT selon l'installation : `pip install ansible` embarque la
    # collection, `ansible-core` non. D'où un « ça marche chez moi » sincère.
    #
    # Ce contrôle lit les modules réellement appelés et vérifie que leur
    # collection figure dans requirements.yml. `ansible.builtin` est exclue :
    # elle est dans le cœur, par définition.
    # ═════════════════════════════════════════════════════════════════════════
    utilisees: set[str] = set()
    for chemin in glob.glob(os.path.join(ANSIBLE, "roles", "*", "**", "*.yml"), recursive=True) \
            + glob.glob(os.path.join(ANSIBLE, "playbooks", "*.yml")):
        texte = open(chemin, encoding="utf-8", errors="ignore").read()
        for trouve in re.findall(r"\b([a-z][a-z0-9_]*\.[a-z][a-z0-9_]*)\.[a-z][a-z0-9_]*\s*:", texte):
            if not trouve.startswith("ansible.builtin"):
                utilisees.add(trouve)

    chemin_req = os.path.join(ANSIBLE, "requirements.yml")
    declarees: set[str] = set()
    if os.path.isfile(chemin_req):
        try:
            contenu = yaml.safe_load(open(chemin_req, encoding="utf-8")) or {}
            for c in contenu.get("collections", []):
                declarees.add(c["name"] if isinstance(c, dict) else str(c))
        except Exception as erreur:
            fautes.append(f"infra/ansible/requirements.yml illisible : {erreur}")
    elif utilisees:
        fautes.append("infra/ansible/requirements.yml absent alors que les rôles "
                      f"emploient {', '.join(sorted(utilisees))}")

    for collection in sorted(utilisees - declarees):
        if os.path.isfile(chemin_req):
            fautes.append(f"infra/ansible : la collection « {collection} » est employée "
                          f"par un rôle mais absente de requirements.yml")

    if not fautes:
        print(f"  Ansible : {len(charges)} fichier(s), {len(roles_connus)} rôle(s), "
              f"handlers et groupes cohérents.")
    return fautes


# ═══════════════════════════════════════════════════════════════════════════════
# COMPOSE — LA PILE RÉELLEMENT LANCÉE
# ═══════════════════════════════════════════════════════════════════════════════

def controler_compose() -> list[str]:
    """
    Vérifie `docker-compose.dev.yml` — la seule pile que `make up` démarre.

    CE CONTRÔLE A CHANGÉ DE CIBLE, ET DE QUESTION.

    Il vérifiait auparavant que chaque service de `infra/docker/compose.*.yml`
    portait `OpenTelemetry__Endpoint`. Deux défauts :

      1. Ce fichier n'était lancé par personne. Le contrôle était vert sur du
         code mort, et aveugle sur la pile vivante.
      2. La question elle-même ne vaut pas pour la pile de développement :
         celle-ci n'embarque AUCUN collecteur OTLP. Exiger une adresse partout
         y produirait vingt-trois services qui échouent à se connecter toutes
         les quelques secondes. La passerelle pose `OPENTELEMETRY__ENDPOINT: ""`
         pour cette raison exacte.

    On vérifie donc la COHÉRENCE, qui est ce qui casse réellement :

      A. Un collecteur est présent dans la pile, ou aucun service n'a
         d'adresse. L'état intermédiaire — une adresse posée sur quelques
         services, sans collecteur en face — est celui qui remplit les journaux
         sans que rien ne le désigne.

      B. Chaque base `Database=hba_xxx` injectée à un service existe dans
         `infra/postgres/init/001-create-databases.sql`. C'est le défaut qui
         s'est réellement produit : `hba_promotion` était injectée et jamais
         créée. Il ne se voyait pas, parce que `Database.Migrate()` crée la
         base absente en développement — ce qui masque l'oubli jusqu'à la
         production, où `MigrateOnStartup=false`.

    CE QUE CE CONTRÔLE NE COUVRE PAS : la pile k8s. La cohérence entre
    `OPENTELEMETRY__ENDPOINT`, le collecteur OTLP et la NetworkPolicy appartient
    à `check-k8s.py`, qui construit les overlays réellement déployés.
    """
    fautes: list[str] = []

    if not os.path.isfile(COMPOSE):
        return [f"{court(COMPOSE)} introuvable — la pile de développement a été "
                "déplacée sans mettre ce contrôle à jour."]

    try:
        import yaml  # type: ignore
    except ImportError:
        return ["PyYAML absent : contrôle compose impossible "
                "(pip install pyyaml). Il n'est PAS dégradé en comptage "
                "textuel — les deux vérifications lisent la structure."]

    document = yaml.safe_load(open(COMPOSE, encoding="utf-8")) or {}
    services = document.get("services") or {}

    # ── Applicatif = construit depuis un Dockerfile .NET du dépôt ────────────
    #
    # `postgres`, `redis`, `kafka` et les interfaces d'appoint ne portent ni
    # télémétrie ni chaîne de connexion applicative. `rembg` construit bien une
    # image, mais c'est un service Python sans socle .NET : le retenir ferait
    # une faute par exécution, pour un service qui n'a rien à déclarer.
    def applicatif(service) -> bool:
        build = service.get("build") if isinstance(service, dict) else None
        if not isinstance(build, dict):
            return False
        fichier = str(build.get("dockerfile") or "")
        return fichier.startswith(("services/", "apps/"))

    def cles_env(service) -> dict:
        environnement = service.get("environment") or {}
        if isinstance(environnement, list):
            table = {}
            for ligne in environnement:
                if isinstance(ligne, str) and "=" in ligne:
                    cle, valeur = ligne.split("=", 1)
                    table[cle.strip()] = valeur
            return table
        return dict(environnement)

    metier = {nom: s for nom, s in services.items()
              if isinstance(s, dict) and applicatif(s)}

    # ── A. Collecteur et adresses OTLP ───────────────────────────────────────
    #
    # Un collecteur se reconnaît à son image, pas à son nom : le renommer ne
    # doit pas rendre ce contrôle silencieux.
    collecteur = [nom for nom, s in services.items()
                  if isinstance(s, dict)
                  and re.search(r"otel|opentelemetry|jaeger|tempo",
                                str(s.get("image") or ""), re.IGNORECASE)]

    avec_adresse = []
    for nom, service in {**metier,
                         **{n: s for n, s in services.items()
                            if isinstance(s, dict)}}.items():
        for cle, valeur in cles_env(service).items():
            if cle.upper() == "OPENTELEMETRY__ENDPOINT" and str(valeur or "").strip():
                avec_adresse.append(nom)
                break

    if not collecteur and avec_adresse:
        fautes.append(
            f"{court(COMPOSE)} : aucun collecteur OTLP dans la pile, mais "
            f"{', '.join(sorted(avec_adresse))} pose(nt) une adresse "
            "`OPENTELEMETRY__ENDPOINT` non vide — le service journalisera un "
            "échec de connexion toutes les quelques secondes, sans que rien "
            "ne désigne la cause.")

    if collecteur:
        muets = sorted(n for n in metier if n not in avec_adresse)
        if muets:
            fautes.append(
                f"{court(COMPOSE)} : un collecteur ({', '.join(collecteur)}) "
                f"tourne, mais {len(muets)} service(s) n'ont pas d'adresse OTLP "
                f"— {', '.join(muets[:5])}"
                f"{'…' if len(muets) > 5 else ''}. Ils démarrent muets, sans erreur.")

    # ── B. Bases injectées contre bases créées ───────────────────────────────
    if not os.path.isfile(INIT_SQL):
        fautes.append(f"{court(INIT_SQL)} introuvable — le montage "
                      "`/docker-entrypoint-initdb.d` de la pile pointe dans le vide.")
    else:
        creees = set(re.findall(r"CREATE\s+DATABASE\s+(hba_[a-z_]+)",
                                open(INIT_SQL, encoding="utf-8").read(),
                                re.IGNORECASE))
        injectees: dict[str, str] = {}
        for nom, service in metier.items():
            for valeur in cles_env(service).values():
                for base in re.findall(r"Database=(hba_[a-z_]+)", str(valeur)):
                    injectees.setdefault(base, nom)

        for base in sorted(set(injectees) - creees):
            fautes.append(
                f"{court(COMPOSE)} : `{base}` est injectée à "
                f"`{injectees[base]}` mais absente de {court(INIT_SQL)} — "
                "sur un volume neuf, ce service est le seul à échouer. "
                "`Database.Migrate()` masque l'oubli en développement, pas en "
                "production où `MigrateOnStartup=false`.")

    if not fautes:
        etat = f"collecteur {collecteur[0]}" if collecteur else "sans collecteur (adresses vides)"
        print(f"  Compose : {len(metier)} service(s) applicatif(s), {etat}, "
              "bases injectées toutes créées.")

    return fautes


def main() -> int:
    fautes = controler_terraform() + controler_ansible() + controler_compose()

    if fautes:
        for faute in fautes:
            print(f"  ❌ {faute}")
        print(f"  {len(fautes)} défaut(s).")
        return 1

    # CE QUI RESTE NON VÉRIFIÉ, ET QU'IL FAUT DIRE.
    print("   Syntaxe et câblage seulement : ni `terraform plan` ni "
          "`ansible-playbook` n'ont tourné.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
