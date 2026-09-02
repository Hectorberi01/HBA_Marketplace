#!/usr/bin/env python3
# ==============================================================================
# LA FORME JSON DE CHAQUE ROUTE — POUR LES DEVELOPPEURS FRONT.
#
#     python3 scripts/schema-routes-api.py
#     -> docs/schemas-routes-api.json
#
# POURQUOI CE SCRIPT EXISTE.
#
# `lister-routes-api.py` rend le NOM du type de reponse : `PagedResult<
# ProductSummary>`, `DriverAccountDto`. Un nom de type ne dit rien a quelqu'un
# qui ecrit un composant. Il lui faut les CHAMPS, leur type, et leur casse
# reelle sur le fil.
#
# CE QU'IL FAIT : il indexe les declarations C# du depot, puis resout chaque
# type de reponse et de requete en un exemple JSON, recursivement.
#
# CE QU'IL NE FAIT PAS, ET C'EST DELIBERE :
#
#   IL N'INVENTE JAMAIS. Un type qu'il ne sait pas resoudre est rendu comme
#   `{"__non_resolu": "<nom>"}`, et compte dans le bilan final. Un JSON
#   plausible mais faux dans une documentation d'API coute plus cher qu'une case
#   vide : on le croit, on code contre, et l'ecart se decouvre a l'integration.
#
#   IL NE LIT PAS LE RUNTIME. Les projections anonymes (`new { id }`), les
#   `object` et les types construits dynamiquement n'ont pas de forme lisible
#   statiquement. Ils sont nommes, pas devines.
#
# LA CASSE EST CELLE DU FIL, PAS CELLE DU C#.
#
# Aucune `JsonSerializerOptions` ne fixe de politique de nommage dans ce depot :
# les API minimales d'ASP.NET Core emploient donc `JsonSerializerDefaults.Web`,
# c'est-a-dire **camelCase**. `SellerId` part en `sellerId`. Ce script applique
# la meme conversion — s'il ne le faisait pas, toute une equipe front ecrirait
# les mauvais noms de champs.
# ==============================================================================

import io
import json
import os
import re
import sys

RACINE = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from _lecture_csharp import sans_commentaires
from _routes_chemins import corriger as corriger_chemins  # noqa: E402

ENTREE = os.path.join(RACINE, "docs", "routes-api.json")
SORTIE = os.path.join(RACINE, "docs", "schemas-routes-api.json")

DOSSIERS = ("services", "shared", "apps")

PROFONDEUR_MAX = 6

# ── Les types du framework, rendus sans indirection ───────────────────────────
#
# La valeur est un EXEMPLE, pas un nom de type : c'est ce qu'un front verra.
PRIMITIFS = {
    "string": "texte",
    "String": "texte",
    "Guid": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "int": 0,
    "Int32": 0,
    "long": 0,
    "Int64": 0,
    "short": 0,
    "byte": 0,
    "decimal": 0.0,
    "double": 0.0,
    "float": 0.0,
    "bool": True,
    "Boolean": True,
    "DateTime": "2026-09-01T12:00:00Z",
    "DateTimeOffset": "2026-09-01T12:00:00+00:00",
    "DateOnly": "2026-09-01",
    "TimeOnly": "12:00:00",
    "TimeSpan": "00:30:00",
    "Uri": "https://exemple",
    "object": None,
    "Object": None,
}

LISTES = ("IReadOnlyList", "IReadOnlyCollection", "IEnumerable", "ICollection",
          "List", "IList", "HashSet", "ISet", "Collection")
DICTIONNAIRES = ("IReadOnlyDictionary", "IDictionary", "Dictionary",
                 "SortedDictionary")


def camel(nom):
    """`SellerId` -> `sellerId`. La conversion d'ASP.NET, reproduite ici."""
    if not nom:
        return nom
    if len(nom) > 1 and nom[0].isupper() and nom[1].isupper():
        # `IDs` -> `ids` : .NET abaisse la suite de majuscules initiale.
        i = 0
        while i < len(nom) and nom[i].isupper():
            i += 1
        if i == len(nom):
            return nom.lower()
        return nom[:i - 1].lower() + nom[i - 1:]
    return nom[0].lower() + nom[1:]


def decouper_arguments(texte):
    """Decoupe une liste d'arguments C# en respectant <>, () et []."""
    parties, courant, profondeur = [], [], 0
    for c in texte:
        if c in "<([":
            profondeur += 1
        elif c in ">)]":
            profondeur -= 1
        if c == "," and profondeur == 0:
            parties.append("".join(courant).strip())
            courant = []
        else:
            courant.append(c)
    if "".join(courant).strip():
        parties.append("".join(courant).strip())
    return parties


def bloc_equilibre(source, debut, ouvrant, fermant):
    """Rend le contenu entre `ouvrant` et son `fermant` correspondant."""
    i = source.find(ouvrant, debut)
    if i < 0:
        return None, debut
    profondeur, j = 0, i
    while j < len(source):
        if source[j] == ouvrant:
            profondeur += 1
        elif source[j] == fermant:
            profondeur -= 1
            if profondeur == 0:
                return source[i + 1:j], j + 1
        j += 1
    return None, debut


# ── Indexation des declarations ───────────────────────────────────────────────

DECL = re.compile(
    r"\b(?:public|internal)\s+(?:sealed\s+|abstract\s+|partial\s+|readonly\s+)*"
    r"(record\s+struct|record|class|enum)\s+([A-Za-z_][A-Za-z0-9_]*)"
    r"\s*(<[^>(){}]*>)?")

PROPRIETE = re.compile(
    r"public\s+([A-Za-z_][A-Za-z0-9_.<>,\[\]\?\s]*?)\s+"
    r"([A-Za-z_][A-Za-z0-9_]*)\s*\{\s*get\s*;")


def indexer():
    """Rend ({nom_simple: declaration}, {nom_simple: [declarations]}).

    La premiere valeur est l'index par defaut ; la seconde garde TOUTES les
    declarations d'un nom pour permettre une resolution par service.
    """
    types = {}
    variantes = {}
    doublons = {}
    for base in DOSSIERS:
        racine = os.path.join(RACINE, base)
        if not os.path.isdir(racine):
            continue
        for dossier, sous, fichiers in os.walk(racine):
            sous[:] = [d for d in sous if d not in ("bin", "obj", "node_modules")]
            for f in fichiers:
                if not f.endswith(".cs"):
                    continue
                chemin = os.path.join(dossier, f)
                try:
                    src = sans_commentaires(io.open(chemin, encoding="utf-8",
                                                    errors="ignore").read())
                except OSError:
                    continue
                for m in DECL.finditer(src):
                    genre, nom, generiques = m.group(1), m.group(2), m.group(3)
                    decl = analyser(src, m.end(), genre, nom, generiques,
                                    os.path.relpath(chemin, RACINE))
                    variantes.setdefault(nom, []).append(decl)
                    if nom in types:
                        doublons.setdefault(nom, []).append(chemin)
                        continue
                    types[nom] = decl
    return types, variantes, doublons


def racine_service(fichier):
    """Rend le prefixe du module qui contient ce fichier, ou None.

    `services/<domaine>/<x>-service/`, `apps/<x>/` et `shared/<x>/` sont des
    frontieres de nommage : deux types homonymes de part et d'autre sont deux
    types differents.
    """
    if not fichier:
        return None
    parts = fichier.split("/")
    if parts[0] == "services" and len(parts) >= 3:
        return "/".join(parts[:3]) + "/"
    if parts[0] in ("apps", "shared") and len(parts) >= 2:
        return "/".join(parts[:2]) + "/"
    return None


def index_pour(fichier_route, types, variantes):
    """Index specialise pour une route donnee.

    Un nom simple ne designe pas un type : `PagedResult<T>` existe en DEUX
    formes incompatibles — celle de `shared/common` porte `total` et `facets`,
    celle de la passerelle porte `totalCount` nullable et pas de total. Prendre
    la mauvaise, c'est livrer au front une pagination qui ne marche pas.

    Ordre de preference : meme fichier, puis meme module, puis `shared/`, puis
    la premiere declaration vue. Rend (index, ambigus) ou `ambigus` liste les
    noms restes indecidables — le document les affiche comme tels plutot que de
    trancher en silence.

    Ne couvre pas : deux declarations homonymes DANS le meme fichier, ni les
    espaces de noms (l'index travaille sur le nom simple, pas sur le `using`
    reellement en vigueur dans le fichier appelant).
    """
    module = racine_service(fichier_route)

    surcharges = {}
    ambigus = []
    for nom, liste in variantes.items():
        if len(liste) < 2:
            continue

        meme_fichier = [d for d in liste if d["fichier"] == fichier_route]
        meme_module = [d for d in liste
                       if module and racine_service(d["fichier"]) == module]
        partages = [d for d in liste if d["fichier"].startswith("shared/")]

        for candidats in (meme_fichier, meme_module, partages):
            if candidats:
                surcharges[nom] = candidats[0]
                if len(candidats) > 1:
                    ambigus.append(nom)
                break
        else:
            # Aucune declaration ni dans le fichier, ni dans le module, ni dans
            # shared : le nom vient d'ailleurs et rien ne permet de choisir.
            ambigus.append(nom)

    if not surcharges:
        return types, ambigus
    index = dict(types)
    index.update(surcharges)
    return index, ambigus


def analyser(src, position, genre, nom, generiques, fichier):
    """Extrait les membres d'une declaration."""
    params_generiques = []
    if generiques:
        params_generiques = [p.strip() for p in generiques.strip("<>").split(",")]

    decl = {"nom": nom, "genre": genre, "fichier": fichier,
            "generiques": params_generiques, "membres": []}

    reste = src[position:position + 20000]

    if genre == "enum":
        corps, _ = bloc_equilibre(reste, 0, "{", "}")
        if corps:
            decl["valeurs"] = [v.split("=")[0].strip()
                               for v in corps.split(",") if v.strip()]
        return decl

    # Record positionnel : la parenthese suit immediatement le nom.
    tete = reste.lstrip()
    if tete.startswith("("):
        corps, fin = bloc_equilibre(reste, 0, "(", ")")
        if corps is not None:
            for arg in decouper_arguments(corps):
                arg = arg.split("=")[0].strip()          # valeur par defaut
                arg = re.sub(r"^\[[^\]]*\]\s*", "", arg)  # attribut
                if not arg:
                    continue
                morceaux = arg.rsplit(None, 1)
                if len(morceaux) == 2:
                    decl["membres"].append({"type": morceaux[0].strip(),
                                            "nom": morceaux[1].strip()})
        reste = reste[fin:]

    # Corps : proprietes `public T Nom { get; ... }`.
    corps, _ = bloc_equilibre(reste, 0, "{", "}")
    if corps:
        deja = {m["nom"] for m in decl["membres"]}
        for m in PROPRIETE.finditer(corps):
            t, n = m.group(1).strip(), m.group(2).strip()
            # Les modificateurs precedent le type dans la capture ; on les
            # retire, et `static`/`const` disqualifient le membre : ce ne sont
            # pas des donnees d'instance, elles ne partent pas dans le JSON.
            mots = t.split()
            if any(mot in ("static", "const") for mot in mots):
                continue
            while len(mots) > 1 and mots[0] in (
                    "virtual", "override", "required", "new", "abstract",
                    "sealed", "readonly", "extern", "unsafe", "async"):
                mots.pop(0)
            t = " ".join(mots)
            if n in deja or " " in t:
                continue
            # `=>` : propriete calculee, elle EST serialisee, on la garde.
            decl["membres"].append({"type": t, "nom": n})
            deja.add(n)

    return decl


# ── Resolution d'un type en exemple JSON ──────────────────────────────────────

def resoudre(expression, types, vus, profondeur, non_resolus, substitutions=None):
    e = (expression or "").strip()
    if not e:
        return None

    substitutions = substitutions or {}
    nullable = e.endswith("?")
    if nullable:
        e = e[:-1].strip()

    if e.endswith("[]"):
        interne = resoudre(e[:-2], types, vus, profondeur + 1, non_resolus, substitutions)
        return [interne]

    if e in substitutions:
        e = substitutions[e]

    base = e.split("<")[0].split(".")[-1].strip()

    if base in PRIMITIFS:
        return PRIMITIFS[base]

    generiques = []
    if "<" in e:
        interieur, _ = bloc_equilibre(e, 0, "<", ">")
        if interieur is not None:
            generiques = decouper_arguments(interieur)

    if base in LISTES and generiques:
        return [resoudre(generiques[0], types, vus, profondeur + 1,
                         non_resolus, substitutions)]

    if base in DICTIONNAIRES and len(generiques) == 2:
        return {"cle": resoudre(generiques[1], types, vus, profondeur + 1,
                                non_resolus, substitutions)}

    if profondeur > PROFONDEUR_MAX:
        return {"__profondeur_max": base}

    decl = types.get(base)
    if decl is None:
        non_resolus.add(base)
        return {"__non_resolu": base}

    if decl["genre"] == "enum":
        valeurs = decl.get("valeurs") or []
        return valeurs[0] if valeurs else "texte"

    if base in vus:
        # RECURSION : un arbre de categories se contient lui-meme. On coupe et
        # on le DIT, plutot que de rendre un objet tronque qui passerait pour
        # complet.
        return {"__recursif": base}

    # Les parametres generiques de la declaration sont lies aux arguments
    # fournis : `PagedResult<ProductSummary>` lie `T` -> `ProductSummary`.
    liaison = dict(substitutions)
    for nom_param, argument in zip(decl.get("generiques") or [], generiques):
        liaison[nom_param] = argument

    objet = {}
    for membre in decl["membres"]:
        objet[camel(membre["nom"])] = resoudre(
            membre["type"], types, vus | {base}, profondeur + 1,
            non_resolus, liaison)
    return objet


# ── L'enveloppe reelle de la plateforme ───────────────────────────────────────

def envelopper(donnees, pagine, enveloppe="ApiEnvelope"):
    """Pose autour des donnees l'enveloppe REELLEMENT emise par la route.

    La plateforme en a DEUX, et elles ne se ressemblent pas : les services
    rendent `ApiEnvelope<T>` (`success`/`data`/`error`/`meta`), la passerelle
    rend `BffEnvelope<T>` (`data`/`warnings`). Un front qui lit `success` sur
    une reponse BFF lit `undefined`.
    """
    if enveloppe == "BffEnvelope":
        # `warnings` est vide, jamais nul : une dependance degradee y depose
        # son code, et l'ecran s'affiche quand meme, partiellement.
        return {"data": donnees, "warnings": []}
    meta = {"requestId": "0HN7...", "timestamp": "2026-09-01T12:00:00+00:00"}
    if pagine:
        meta.update({"page": 1, "pageSize": 20, "total": 137, "hasNext": True})
    return {"success": True, "data": donnees, "error": None, "meta": meta}


# ── Routes portees par des controleurs MVC ────────────────────────────────────
#
# `lister-routes-api.py` ne voit que les API minimales (`MapGet`, `MapPost`…).
# La passerelle, elle, expose son BFF par des CONTROLEURS : ses routes etaient
# donc absentes de `docs/routes-api.json`, c'est-a-dire absentes du document
# destine au front — alors que ce sont precisement les routes qu'appellent les
# applications mobiles.
#
# Ces routes ne portent pas la meme enveloppe que les services : `BffEnvelope<T>`
# rend `data` + `warnings`, sans `success` ni `meta`. Les melanger produirait un
# document faux la ou il est le plus lu.
#
# Ne couvre pas : les conventions de routage par `[controller]` autres que le
# nom de classe, l'heritage de controleur de base, ni les filtres d'autorisation
# poses globalement dans `Program.cs`.

CONTROLEURS = ["apps/api-gateway/src"]

ATTR_ROUTE = re.compile(r'\[Route\("([^"]*)"\)\]')
ATTR_CLASSE = re.compile(
    r"public\s+(?:sealed\s+|abstract\s+|partial\s+)*class\s+"
    r"([A-Za-z_][A-Za-z0-9_]*)Controller\b")
ATTR_VERBE = re.compile(r'\[Http(Get|Post|Put|Delete|Patch)(?:\("([^"]*)"\))?\]')
ATTR_METHODE = re.compile(
    r"public\s+(?:async\s+)?([A-Za-z_][A-Za-z0-9_.<>,\[\]\?]*)\s+"
    r"([A-Za-z_][A-Za-z0-9_]*)\s*\(")
CORPS_REQUETE = re.compile(
    r"\[FromBody\]\s*([A-Za-z_][A-Za-z0-9_.<>,\[\]\?]*)")


def _deballer(retour):
    """Retire `Task<>`, `ActionResult<>`, `IActionResult` du type de retour.

    Rend None quand il ne reste aucun type utile : le front doit alors lire le
    code de statut, pas un corps.
    """
    t = retour.strip()
    for enveloppe in ("Task", "ValueTask", "ActionResult", "Results"):
        prefixe = enveloppe + "<"
        while t.startswith(prefixe) and t.endswith(">"):
            t = t[len(prefixe):-1].strip()
    if t in ("IActionResult", "ActionResult", "Task", "void", "IResult"):
        return None
    return t or None


def collecter_controleurs():
    routes = []
    for base in CONTROLEURS:
        racine = os.path.join(RACINE, base)
        if not os.path.isdir(racine):
            continue
        for dossier, sous, fichiers in os.walk(racine):
            sous[:] = [d for d in sous if d not in ("bin", "obj")]
            for f in fichiers:
                if not f.endswith("Controller.cs"):
                    continue
                chemin = os.path.join(dossier, f)
                relatif = os.path.relpath(chemin, RACINE)
                src = sans_commentaires(
                    io.open(chemin, encoding="utf-8", errors="ignore").read())

                mc = ATTR_CLASSE.search(src)
                if not mc:
                    continue
                entete = src[:mc.start()]
                mr = ATTR_ROUTE.search(entete)
                if not mr:
                    continue
                prefixe = mr.group(1).replace("[controller]", mc.group(1).lower())
                acces_classe = ("anonyme" if "[AllowAnonymous]" in entete
                                else "jeton" if "[Authorize" in entete else "jeton")

                for mv in ATTR_VERBE.finditer(src, mc.end()):
                    verbe, suffixe = mv.group(1).upper(), (mv.group(2) or "")
                    # Attributs poses entre le verbe et la signature.
                    mm = ATTR_METHODE.search(src, mv.end())
                    if not mm:
                        continue
                    entre = src[mv.end():mm.start()]
                    acces = ("anonyme" if "[AllowAnonymous]" in entre
                             else "jeton" if "[Authorize" in entre else acces_classe)

                    signature, _ = bloc_equilibre(src[mm.end() - 1:], 0, "(", ")")
                    mb = CORPS_REQUETE.search(signature or "")

                    segments = [prefixe.strip("/")]
                    if suffixe.strip("/"):
                        segments.append(suffixe.strip("/"))
                    routes.append({
                        "service": "api-gateway",
                        "methode": verbe,
                        "chemin": "/" + "/".join(segments),
                        "etiquette": "Passerelle · BFF",
                        "acces": acces,
                        "gestionnaire": mm.group(2),
                        "reponse": _deballer(mm.group(1)) or "(aucun corps)",
                        "requete": mb.group(1) if mb else None,
                        "fichier": relatif,
                        "exposee": True,
                        "deploye": True,
                        "chemin_public": "/" + "/".join(segments),
                        "politique_passerelle": None,
                        "idempotent": False,
                        "limiteur": False,
                        "route": None,
                        "controleur": True,
                    })
    return routes


def main():
    if not os.path.exists(ENTREE):
        print("introuvable : %s — lancer d'abord scripts/lister-routes-api.py"
              % os.path.relpath(ENTREE, RACINE), file=sys.stderr)
        return 1

    inventaire = json.load(io.open(ENTREE, encoding="utf-8"))
    routes = list(inventaire["routes"])

    # `docs/routes-api.json` attribuait a chaque route le prefixe du DERNIER
    # groupe declare dans son fichier : les routes d'authentification etaient
    # documentees sous `/api/identity/roles/`. On recalcule le chemin depuis la
    # source avant toute chose — un exemple de reponse juste sur une URL fausse
    # ne sert a personne.
    bilan = corriger_chemins(routes)
    print("%d chemin(s) corrige(s) depuis la source, %d non rattachee(s)"
          % (bilan["corrigees"], len(bilan["non_rattachees"])))
    for x in bilan["non_rattachees"][:8]:
        print("    non rattachee : %s" % x)

    routes += collecter_controleurs()

    types, variantes, doublons = indexer()
    print("%d type(s) C# indexes" % len(types))
    if doublons:
        print("%d nom(s) declares plusieurs fois — la premiere declaration "
              "rencontree fait foi :" % len(doublons))
        for n in sorted(doublons)[:6]:
            print("    %s" % n)

    non_resolus = set()
    sorties = []

    for r in routes:
        if not r.get("exposee"):
            continue

        index, ambigus = index_pour(r.get("fichier"), types, variantes)

        rep = r.get("reponse")
        enveloppe = "ApiEnvelope"
        if rep and rep.split("<")[0].strip() == "BffEnvelope":
            enveloppe = "BffEnvelope"
            rep = rep[len("BffEnvelope<"):-1].strip()
        elif r.get("controleur"):
            # Un controleur BFF qui ne declare pas l'enveloppe rend son type nu.
            enveloppe = "aucune"

        corps_reponse = None
        if rep and rep != "(aucun corps)":
            pagine = rep.split("<")[0].strip() == "PagedResult"
            donnees = resoudre(rep, index, set(), 0, non_resolus)
            corps_reponse = (donnees if enveloppe == "aucune"
                             else envelopper(donnees, pagine, enveloppe))

        req = r.get("requete")
        corps_requete = None
        if req:
            corps_requete = resoudre(req, index, set(), 0, non_resolus)

        cites = set(re.findall(r"[A-Za-z_][A-Za-z0-9_]*",
                               "%s %s" % (rep or "", req or "")))
        ambigus_ici = sorted(cites & set(ambigus))

        sorties.append({
            "service": r.get("service"),
            "methode": r.get("methode"),
            "chemin": r.get("chemin_public") or r.get("chemin"),
            "alias_publics": r.get("alias_publics") or [],
            "etiquette": r.get("etiquette"),
            "acces": r.get("acces"),
            "gestionnaire": r.get("gestionnaire"),
            "type_reponse": rep,
            "type_requete": req,
            "corps_requete": corps_requete,
            "corps_reponse": corps_reponse,
            "enveloppe": enveloppe,
            "source": "controleur" if r.get("controleur") else "api-minimale",
            "types_ambigus": ambigus_ici,
            "politique_passerelle": r.get("politique_passerelle"),
            "idempotent": bool(r.get("idempotent")),
            "limiteur": bool(r.get("limiteur")),
            "chemin_interne": r.get("chemin"),
            "prefixe_passerelle": r.get("route"),
            "fichier": r.get("fichier"),
        })

    avec = sum(1 for s in sorties if s["corps_reponse"] is not None)
    resultat = {
        "base_publique": inventaire.get("base_publique"),
        "enveloppe": "ApiEnvelope<T> — success / data / error / meta",
        "casse": "camelCase (JsonSerializerDefaults.Web, aucune politique "
                 "explicite dans le depot)",
        "routes": sorties,
        "types_non_resolus": sorted(non_resolus),
    }

    io.open(SORTIE, "w", encoding="utf-8").write(
        json.dumps(resultat, ensure_ascii=False, indent=2))

    print()
    print("%d route(s) exposee(s), %d avec un corps de reponse resolu"
          % (len(sorties), avec))
    print("%d type(s) non resolu(s)" % len(non_resolus))
    for n in sorted(non_resolus)[:15]:
        print("    %s" % n)
    if len(non_resolus) > 15:
        print("    … et %d autre(s)" % (len(non_resolus) - 15))
    print()
    print("ecrit : %s" % os.path.relpath(SORTIE, RACINE))
    return 0


if __name__ == "__main__":
    sys.exit(main())
