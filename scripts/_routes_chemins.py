#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Recalcule le chemin reel de chaque route a partir du code source.

CE QUE CE MODULE REPARE.

`docs/routes-api.json` attribuait a chaque route le prefixe du DERNIER groupe
declare dans son fichier. Tant qu'un fichier ne declarait qu'un groupe, cela
passait ; seize fichiers d'endpoints en declarent plusieurs. Les 33 routes de
`identity-service` etaient ainsi toutes documentees sous
`/api/identity/roles/` — y compris `login`, `register` et `refresh`, dont le
vrai prefixe est `/api/v1/auth`. Un developpeur front suivant ce document ne
pouvait meme pas authentifier son application.

CE QU'IL FAIT.

Il relit chaque fichier source, suit la variable a laquelle chaque groupe est
affecte (`var group = app.MapAdminGroup("/api/identity/users")`), resout les
groupes imbriques, et rend le prefixe REEL de chaque appel `Map<Verbe>`. Puis
il rejoue la table de routage de la passerelle
(`apps/api-gateway/src/HBA.Gateway.Api/appsettings.json`) pour retrouver le
chemin PUBLIC correspondant.

CE QU'IL NE COUVRE PAS.

Les chemins construits par concatenation ou par constante — seuls les
litteraux sont lus. Les groupes affectes a un champ de classe plutot qu'a une
variable locale. Les routes montees par reflexion. Une route qu'il n'arrive
pas a rattacher est laissee telle quelle et signalee, jamais devinee.
"""

import io
import json
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from _lecture_csharp import sans_commentaires  # noqa: E402

RACINE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CONFIG_PASSERELLE = os.path.join(
    RACINE, "apps", "api-gateway", "src", "HBA.Gateway.Api", "appsettings.json")

VERBES = ("Get", "Post", "Put", "Patch", "Delete")

# `var x = <recepteur>.Map<Quelquechose>Group("<prefixe>")`
AFFECTATION = re.compile(
    r"\bvar\s+([A-Za-z_][A-Za-z0-9_]*)\s*=\s*"
    r"([A-Za-z_][A-Za-z0-9_]*)"
    r"((?:\s*\.\s*[A-Za-z_][A-Za-z0-9_]*\s*\([^;]*?\))+)\s*;",
    re.S)
GROUPE_DANS = re.compile(r"\.\s*Map[A-Za-z]*Group\s*\(\s*\"([^\"]*)\"")
APPEL_MAP = re.compile(
    r"\b([A-Za-z_][A-Za-z0-9_]*)\s*\.\s*Map(" + "|".join(VERBES) + r")\s*\(\s*"
    r"\"([^\"]*)\"\s*,\s*([A-Za-z_][A-Za-z0-9_]*)?")


def _prefixe_de(nom, affectations, position, vus=None):
    """Prefixe cumule de la variable `nom` au point `position`.

    On remonte a l'affectation la plus proche AVANT la position : deux methodes
    du meme fichier reutilisent presque toujours le nom `group`, et c'est
    exactement la confusion que ce module existe pour eviter.
    """
    vus = vus or set()
    if nom in vus:
        return ""
    candidats = [a for a in affectations.get(nom, []) if a["fin"] <= position]
    if not candidats:
        return ""
    a = max(candidats, key=lambda x: x["fin"])
    parent = ""
    if a["recepteur"] in affectations:
        parent = _prefixe_de(a["recepteur"], affectations, a["debut"],
                             vus | {nom})
    return parent + "".join(a["prefixes"])


def chemins_par_fichier(chemin_absolu):
    """Rend [(verbe, gestionnaire, suffixe, chemin_interne)] pour un fichier."""
    src = sans_commentaires(
        io.open(chemin_absolu, encoding="utf-8", errors="ignore").read())

    affectations = {}
    for m in AFFECTATION.finditer(src):
        nom, recepteur, chaine = m.group(1), m.group(2), m.group(3)
        prefixes = GROUPE_DANS.findall(chaine)
        if not prefixes:
            continue
        affectations.setdefault(nom, []).append({
            "debut": m.start(), "fin": m.end(),
            "recepteur": recepteur, "prefixes": prefixes,
        })

    routes = []
    for m in APPEL_MAP.finditer(src):
        variable, verbe, suffixe, gestionnaire = m.groups()
        prefixe = _prefixe_de(variable, affectations, m.start())
        complet = (prefixe.rstrip("/") + "/" + suffixe.lstrip("/"))
        if not complet.startswith("/"):
            complet = "/" + complet
        complet = re.sub(r"/{2,}", "/", complet)
        routes.append({
            "methode": verbe.upper(),
            "gestionnaire": gestionnaire,
            "suffixe": suffixe,
            "chemin": complet,
        })
    return routes


def table_passerelle():
    """Rend [(motif_public, motif_interne, politique)] depuis YARP.

    `PathPattern` absent signifie que la passerelle transmet le chemin tel
    quel : le public et l'interne sont alors identiques.
    """
    if not os.path.exists(CONFIG_PASSERELLE):
        return []
    conf = json.load(io.open(CONFIG_PASSERELLE, encoding="utf-8"))
    table = []
    for nom, r in (conf.get("ReverseProxy", {}).get("Routes", {})).items():
        public = (r.get("Match") or {}).get("Path")
        if not public:
            continue
        interne = public
        for t in r.get("Transforms") or []:
            if "PathPattern" in t:
                interne = t["PathPattern"]
        table.append({
            "nom": nom,
            "public": public,
            "interne": interne,
            "politique": r.get("AuthorizationPolicy"),
        })
    # Les motifs les plus specifiques d'abord : `/api/auth/otp/{**catch-all}`
    # doit gagner sur `/api/auth/{**catch-all}`, sinon toutes les routes OTP
    # heriteraient de la politique et de la cible de `auth`.
    table.sort(key=lambda x: -len(x["interne"].replace("{**catch-all}", "")))
    return table


def _base(motif):
    return motif.replace("{**catch-all}", "").rstrip("/")


def vers_public(chemin_interne, table):
    """Rend (chemin_public, politique, nom_de_route, alias).

    PLUSIEURS chemins publics peuvent viser la meme route interne : la
    passerelle garde des alias hérités (`/api/auth/login` reecrit vers
    `/api/v1/auth/login`, que `/api/v1/auth/{**catch-all}` sert aussi
    directement). Les deux marchent. On retient comme chemin principal celui
    qui n'est PAS une reecriture — c'est la forme versionnee, celle qui
    survivra a la suppression des alias — et on rend les autres comme alias
    plutot que de laisser le choix au hasard de l'ordre de lecture.
    """
    trouves = []
    for r in table:
        base = _base(r["interne"])
        if base and (chemin_interne == base
                     or chemin_interne.startswith(base + "/")):
            reste = chemin_interne[len(base):]
            trouves.append((_base(r["public"]) + reste, r))
    if not trouves:
        return (None, None, None, [])

    def rang(paire):
        public, r = paire
        return (0 if r["public"] == r["interne"] else 1, -len(public))

    trouves.sort(key=rang)
    principal, meilleur = trouves[0]
    # Deux entrees de la table peuvent produire le meme chemin public (un
    # motif large et un motif specifique qui se recouvrent) : le lecteur n'a
    # pas a voir deux fois la meme URL.
    alias, deja = [], {principal}
    for public, _ in trouves[1:]:
        if public not in deja:
            alias.append(public)
            deja.add(public)
    return (principal, meilleur["politique"], meilleur["nom"], alias)


def _finit_par(chemin, suffixe):
    """Le chemin se termine-t-il par ce suffixe litteral ?

    `"/"` est traite a part : c'est le montage a la racine d'un groupe, et le
    comparer apres `rstrip` reviendrait a comparer avec la chaine vide, donc a
    repondre oui a tout.
    """
    chemin = chemin or ""
    if suffixe in ("", "/"):
        return chemin.endswith("/")
    return chemin.rstrip("/").endswith(suffixe.rstrip("/"))


def _appliquer(r, neuf, table):
    ancien = r.get("chemin") or ""
    public, politique, nom, alias = vers_public(neuf, table)
    r["chemin"] = neuf
    if public:
        r["chemin_public"] = public
        r["alias_publics"] = alias
        r["route"] = nom
        if politique:
            r["politique_passerelle"] = politique
    else:
        r["chemin_public"] = neuf
        r["alias_publics"] = []
    return neuf != ancien


def corriger(routes):
    """Corrige `chemin` et `chemin_public` en place. Rend un compte-rendu."""
    table = table_passerelle()
    par_fichier = {}
    corrigees = 0
    non_rattachees = []
    en_attente = []

    for r in routes:
        fichier = r.get("fichier")
        if not fichier or r.get("controleur"):
            continue
        absolu = os.path.join(RACINE, fichier)
        if fichier not in par_fichier:
            par_fichier[fichier] = (chemins_par_fichier(absolu)
                                    if os.path.exists(absolu) else [])

        ancien = r.get("chemin") or ""
        candidats = [
            c for c in par_fichier[fichier]
            if c["methode"] == r["methode"]
            and (c["gestionnaire"] == r.get("gestionnaire")
                 or r.get("gestionnaire") in (None, "async"))
            and _finit_par(ancien, c["suffixe"])
        ]
        if len(candidats) != 1:
            candidats = [c for c in par_fichier[fichier]
                         if c["methode"] == r["methode"]
                         and c["gestionnaire"] == r.get("gestionnaire")]
        if len(candidats) != 1:
            # Un meme gestionnaire monte sous deux groupes (une route publique
            # et son double interne, par exemple) : on ne tranche pas ici, on
            # met de cote et on apparie plus bas, quand on connait le compte
            # des deux cotes.
            en_attente.append((r, fichier))
            continue

        if _appliquer(r, candidats[0]["chemin"], table):
            corrigees += 1

    # Appariement des cas ambigus : meme fichier, meme verbe, meme suffixe. Si
    # l'inventaire compte autant de lignes que la source compte de montages, la
    # correspondance est bijective et l'ordre du fichier fait foi. Sinon on
    # laisse le chemin en l'etat et on le signale — un chemin devine serait
    # pire qu'un chemin ancien, parce qu'il aurait l'air verifie.
    groupes = {}
    for r, fichier in en_attente:
        suffixe = None
        for c in par_fichier[fichier]:
            if (c["methode"] == r["methode"]
                    and _finit_par(r.get("chemin"), c["suffixe"])):
                suffixe = c["suffixe"]
                break
        groupes.setdefault((fichier, r["methode"], suffixe), []).append(r)

    for (fichier, methode, suffixe), lot in groupes.items():
        sources = [c for c in par_fichier[fichier]
                   if c["methode"] == methode and c["suffixe"] == suffixe]
        if suffixe is None or len(sources) != len(lot):
            for r in lot:
                non_rattachees.append(
                    "%s %s (%s)" % (r["methode"], r.get("chemin"),
                                    r.get("gestionnaire")))
            continue
        lot.sort(key=lambda x: x.get("chemin") or "")
        for r, c in zip(lot, sources):
            if _appliquer(r, c["chemin"], table):
                corrigees += 1

    return {"corrigees": corrigees, "non_rattachees": non_rattachees}


if __name__ == "__main__":
    import collections
    entree = os.path.join(RACINE, "docs", "routes-api.json")
    routes = json.load(io.open(entree, encoding="utf-8"))["routes"]
    avant = {(r["methode"], r["chemin"]) for r in routes}
    bilan = corriger(routes)
    print("%d chemin(s) corrige(s)" % bilan["corrigees"])
    print("%d route(s) non rattachee(s)" % len(bilan["non_rattachees"]))
    for x in bilan["non_rattachees"][:10]:
        print("    %s" % x)
    prefixes = collections.Counter(
        "/".join(r["chemin"].split("/")[:4]) for r in routes)
    for p, n in sorted(prefixes.items()):
        print("  %-46s %3d" % (p, n))
