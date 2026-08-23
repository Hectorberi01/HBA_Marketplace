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
import sys

RACINE = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
TERRAFORM = os.path.join(RACINE, "infra", "terraform")
ANSIBLE = os.path.join(RACINE, "infra", "ansible")
COMPOSE = os.path.join(RACINE, "infra", "docker")

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

    # ── un inventaire réel commité ? ──────────────────────────────────────────
    for chemin in glob.glob(os.path.join(ANSIBLE, "inventory", "*.yml")):
        fautes.append(f"{court(chemin)} : un inventaire réel porte des IP de "
                      f"production — seuls les .yml.example sont suivis")

    if not fautes:
        print(f"  Ansible : {len(charges)} fichier(s), {len(roles_connus)} rôle(s), "
              f"handlers et groupes cohérents.")
    return fautes


# ═══════════════════════════════════════════════════════════════════════════════
# COMPOSE — L'EXPORT DE TÉLÉMÉTRIE
# ═══════════════════════════════════════════════════════════════════════════════

def controler_compose() -> list[str]:
    """
    Vérifie que chaque service de la pile exporte sa télémétrie.

    POURQUOI CE CONTRÔLE EXISTE.

    `ServiceHostExtensions` appelle `AddHbaTelemetry` pour tous les services —
    l'appel EST centralisé, et son encadré s'en félicite. Mais
    `TelemetryExtensions` ne branche l'exportateur OTLP que si une adresse est
    lisible :

        var hasEndpoint = Uri.TryCreate(options.Endpoint, UriKind.Absolute, out var endpoint);
        if (hasEndpoint) { metrics.AddOtlpExporter(...); }

    Sans adresse, le service est muet SANS ERREUR. Treize des quatorze
    processus l'étaient : seule la passerelle portait l'adresse, dans son
    `appsettings.json`. Prometheus, Grafana, Loki et le collecteur tournaient
    pour un seul service.

    ON CHERCHE DANS `environment:`, PAS DANS `env_file`.

    Les `env/*.env` sont déclarés `required: false` : un service dont le
    fichier a été retiré repartirait muet. Seul le bloc `environment:` est une
    source à laquelle le service ne peut pas échapper.
    """
    fautes: list[str] = []

    # PAS DE `yaml` EN DÉPENDANCE DURE.
    #
    # Ce script tourne déjà sans `python-hcl2` en dégradant la partie
    # Terraform. Il ne doit pas devenir impossible à lancer pour un contrôle
    # de plus. Sans PyYAML, on cherche la clé en texte brut : moins fin, mais
    # suffisant — la ligne est écrite à l'identique dans les quatorze blocs.
    fichiers = sorted(glob.glob(os.path.join(COMPOSE, "compose.*.yml")))
    fichiers = [f for f in fichiers
                if os.path.basename(f) not in {"compose.infrastructure.yml",
                                               "compose.monitoring.yml"}]

    if not fichiers:
        return ["infra/docker : aucun compose.*.yml trouvé — chemin déplacé ?"]

    try:
        import yaml  # type: ignore
    except ImportError:
        yaml = None

    total = 0

    for fichier in fichiers:
        texte = open(fichier, encoding="utf-8").read()

        if yaml is None:
            # Repli textuel : on compte les services et les occurrences.
            services = len(re.findall(r"^  [a-z][a-z0-9-]*:$", texte, re.MULTILINE))
            poses = texte.count("OpenTelemetry__Endpoint:")
            total += services

            if poses < services:
                fautes.append(
                    f"{court(fichier)} : {services} service(s) déclaré(s), "
                    f"{poses} `OpenTelemetry__Endpoint` — "
                    "un service sans adresse OTLP est muet sans erreur "
                    "(PyYAML absent : comptage textuel, pip install pyyaml pour le détail).")
            continue

        document = yaml.safe_load(texte) or {}

        for nom, service in (document.get("services") or {}).items():
            # Une entrée sans image ni build n'est pas un processus applicatif
            # (ancre, réseau nommé comme un service…).
            if not isinstance(service, dict) or not (service.get("image") or service.get("build")):
                continue

            total += 1
            environnement = service.get("environment") or {}

            # Compose accepte la forme liste `- CLE=valeur` autant que la table.
            if isinstance(environnement, list):
                cles = {ligne.split("=", 1)[0].strip() for ligne in environnement
                        if isinstance(ligne, str)}
            else:
                cles = set(environnement)

            if "OpenTelemetry__Endpoint" not in cles:
                fautes.append(
                    f"{court(fichier)} : le service `{nom}` n'a pas "
                    "`OpenTelemetry__Endpoint` dans son bloc `environment:` — "
                    "il démarrera muet, sans trace ni métrique, et sans erreur.")

    if not fautes:
        print(f"  Compose : {total} service(s), export OTLP déclaré partout.")

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
