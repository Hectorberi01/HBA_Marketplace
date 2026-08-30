#!/usr/bin/env python3
"""
═══════════════════════════════════════════════════════════════════════════════
L'INVENTAIRE DES ROUTES HTTP, DERIVE DU CODE.

    ./scripts/lister-routes-api.py            JSON sur la sortie standard

CE QU'IL LIT, ET DANS QUEL ORDRE :

  1. les groupes — `app.MapGroup("/api/v1/auth")` — et leur imbrication ;
  2. les endpoints — `group.MapPost("/login", LoginAsync)` ;
  3. la chaine d'appel qui suit, jusqu'au `;` : `AllowAnonymous`,
     `RequireAuthorization`, `RequireRateLimiting`, `AllowIdempotency` ;
  4. la signature du gestionnaire, pour le type du corps de requete ;
  5. le `sender.Send(new XCommand(...))` du gestionnaire, puis la declaration de
     `XCommand` ailleurs dans le depot, pour lire `IRequest<Result<TReponse>>`.

CE QU'IL NE PEUT PAS DIRE, ET QUI EST ECRIT TEL QUEL DANS LA SORTIE :

  • LE CODE HTTP EXACT. Les gestionnaires rendent `Task<IResult>` et decident a
    l'execution : `Match(x => Ok(x))` cote succes, une correspondance
    Error -> statut cote echec. La signature ne porte donc AUCUN statut. Ce
    qu'on sait est structurel : 200/201 en succes, et la table d'erreurs du §25.

  • LES ROUTES CONSTRUITES. Un chemin assemble par concatenation ou lu dans une
    constante n'est pas reconnu — seules les chaines litterales le sont. Le
    compte des `Map*` trouves et celui des routes resolues sont tous deux
    rapportes : leur ecart mesure exactement ce qui echappe a ce script.

  • CE QUE LA PASSERELLE EXPOSE. Ce script lit les SERVICES. La passerelle peut
    ne pas router une route, ou la routrer sous un autre prefixe.
═══════════════════════════════════════════════════════════════════════════════
"""
import io
import json
import os
import re
import sys

RACINE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
IGNORES = ("obj", "bin", "_to_delete", "node_modules", ".git", "tests")

# QUATRE FABRIQUES DE GROUPE, PAS UNE.
#
# CE QUI ETAIT CASSE : ce script ne reconnaissait que `MapGroup`. Le depot en
# emploie surtout des variantes maison — `MapAuthenticatedGroup`,
# `MapAdminGroup`, `MapSellerGroup`, `MapOperationsGroup` — qui posent le prefixe
# ET l'autorisation d'un coup. Sur 433 routes resolues, seules celles des neuf
# `MapGroup` litteraux recevaient leur prefixe : les autres sortaient avec un
# chemin tronque du genre `/coupon`, faux et silencieusement plausible.
GROUPE = re.compile(r'\bvar\s+(\w+)\s*=\s*(\w+)\s*\.\s*Map(\w*)Group\(\s*"([^"]*)"')

# Ce que la fabrique impose, avant toute chaine d'appel.
ACCES_PAR_FABRIQUE = {
    "": "hérité du groupe",
    "Authenticated": "jeton",
    "Admin": "jeton + rôle Admin",
    "Seller": "jeton + rôle Seller",
    "Operations": "jeton + rôle Operations",
}
ENDPOINT = re.compile(r'\b(\w+)\s*\.\s*Map(Get|Post|Put|Delete|Patch)\(\s*"([^"]*)"\s*,\s*([\w\.]+)')
MAP_TOUS = re.compile(r'\.\s*Map(?:Get|Post|Put|Delete|Patch)\(')
TAG = re.compile(r'WithTags\(\s*"([^"]*)"')
SIGNATURE = re.compile(r'(?:private|internal|public)\s+static\s+(?:async\s+)?Task<[^>]*>\s+(\w+)\s*\(([^)]*)\)', re.S)
ENVOI = re.compile(r'\bSend\(\s*new\s+(\w+)')
# `IQuery<T>` ET `ICommand<T>`, PAS `IRequest<Result<T>>`.
#
# CE QUI ETAIT CASSE : la premiere version cherchait `IRequest<Result<T>>`, la
# forme brute de MediatR. Le depot emploie ses propres marqueurs — `IQuery<T>`,
# `ICommand<T>`, `ICommand` sans generique pour les gestes qui ne rendent rien.
# Zero type de reponse etait donc trouve, sur quatre cents routes.
DECLARATION = re.compile(
    r'\brecord\s+(\w+)\s*(?:\([^)]*\))?\s*:\s*'
    r'I(?:Query|Command|Request)\s*(?:<\s*(?:Result<\s*)?([\w<>,\[\] ]+?)\s*>?\s*>)?\s*;')


def fichiers():
    for base in ("services", "apps"):
        for dossier, sous, noms in os.walk(os.path.join(RACINE, base)):
            sous[:] = [d for d in sous if d not in IGNORES]
            for nom in noms:
                if nom.endswith(".cs"):
                    yield os.path.join(dossier, nom)


def service_de(chemin):
    """`services/common/identity-service/...` -> `identity-service`."""
    rel = os.path.relpath(chemin, RACINE).split(os.sep)
    if rel[0] == "apps":
        return rel[1]
    return rel[2] if len(rel) > 2 else rel[-1]


def reponses_du_depot():
    """Commande/requete -> type de reponse, lu dans `IRequest<Result<T>>`."""
    table = {}
    for chemin in fichiers():
        texte = io.open(chemin, encoding="utf-8", errors="replace").read()
        for nom, reponse in DECLARATION.findall(texte):
            # Un `ICommand` sans generique ne rend rien : le §25 emballe alors un
            # corps vide. On le dit, plutot que de laisser un trou.
            valeur = reponse.strip()
            # LES CHEVRONS SE REFERMENT.
            #
            # `PagedResult<ProductSummary>>` : la regex mange le dernier chevron
            # avec celui du marqueur, et rendait « PagedResult<ProductSummary ».
            # Un type tronque est pire qu'un type absent : il a l'air juste.
            valeur += ">" * max(0, valeur.count("<") - valeur.count(">"))
            table[nom] = valeur if valeur else "(aucun corps)"
    return table


def corps_des_gestionnaires(texte):
    """Nom du gestionnaire -> (parametres, corps jusqu'au gestionnaire suivant)."""
    trouves = {}
    positions = [(m.group(1), m.group(2), m.end()) for m in SIGNATURE.finditer(texte)]
    for i, (nom, params, fin) in enumerate(positions):
        suite = positions[i + 1][2] if i + 1 < len(positions) else len(texte)
        trouves[nom] = (params, texte[fin:suite])
    return trouves


def corps_de_requete(params):
    """Le premier parametre qui ressemble a un DTO de corps."""
    for morceau in params.split(","):
        morceau = morceau.strip()
        if not morceau:
            continue
        parties = morceau.split()
        if len(parties) < 2:
            continue
        type_ = parties[0].lstrip("[").split("]")[-1]
        if type_.endswith(("Request", "Command", "Dto", "Payload")):
            return type_
    return None


PASSERELLE = os.path.join(
    RACINE, "apps", "api-gateway", "src", "HBA.Gateway.Api", "appsettings.json")
COMPOSE_PROD = os.path.join(RACINE, "docker-compose.prod.yml")


def motifs_de_la_passerelle():
    """Les chemins que la passerelle route, et vers quel amont.

    UNE ROUTE DE SERVICE N'EST PAS UNE ROUTE PUBLIQUE. La passerelle ne fait pas
    passe-plat : elle declare cinquante-cinq motifs. Ce qui n'en releve d'aucun
    existe dans le service, ecoute sur le reseau interne, et n'est joignable
    d'aucun client. C'est exactement le genre d'ecart qu'on ne voit pas en lisant
    le code d'un service.
    """
    try:
        config = json.load(io.open(PASSERELLE, encoding="utf-8"))
    except (OSError, ValueError):
        return []
    motifs = []
    for nom, route in (config.get("ReverseProxy", {}).get("Routes", {}) or {}).items():
        public = (route.get("Match") or {}).get("Path")
        if not public:
            continue

        # ═══════════════════════════════════════════════════════════════════
        # LE CHEMIN PUBLIC N'EST PAS CELUI DU SERVICE.
        #
        # CE QUI ETAIT CASSE : ce script comparait le chemin du service au motif
        # D'ENTREE de la passerelle. Or dix-huit routes sur cinquante-cinq
        # portent un `Transforms.PathPattern` qui REECRIT le chemin avant de le
        # transmettre — `/api/payments/**` devient `/api/financial/payments/**`.
        #
        # Le service, lui, sert le chemin REECRIT. Comparer a l'entree classait
        # donc ces routes « interne seulement » alors qu'elles sont publiques :
        # les webhooks de paiement, entre autres, que FedaPay doit joindre.
        #
        # On compare desormais au motif REECRIT, et l'on rend l'URL publique
        # construite depuis le motif d'ENTREE. Les deux sont necessaires : l'un
        # pour reconnaitre, l'autre pour afficher.
        # ═══════════════════════════════════════════════════════════════════
        interne = public
        for transformation in (route.get("Transforms") or []):
            if "PathPattern" in transformation:
                interne = transformation["PathPattern"]

        amont = route.get("ClusterId")
        genre = "prefixe" if "{**" in interne else "exact"
        motifs.append({
            "interne": interne.split("{**")[0] if genre == "prefixe" else interne,
            "public": public.split("{**")[0] if genre == "prefixe" else public,
            "genre": genre,
            "nom": nom,
            "amont": amont,
            "politique": route.get("AuthorizationPolicy"),
            "ordre": route.get("Order", 1000),
        })

    # L'ORDRE DE YARP D'ABORD, LE PREFIXE LE PLUS LONG ENSUITE.
    #
    # YARP tranche les gabarits qui se chevauchent par `Order` croissant :
    # `/api/payments/webhooks/**` (5) gagne sur `/api/payments/**` (10), et c'est
    # ce qui laisse un PSP appeler sans jeton. Trier par longueur seule
    # inverserait ce choix sur les routes de meme longueur.
    motifs.sort(key=lambda m: (m["ordre"], -len(m["interne"])))
    return motifs


def exposition(chemin, motifs):
    for m in motifs:
        if m["genre"] == "exact":
            if chemin != m["interne"]:
                continue
            public = m["public"]
        else:
            if not chemin.startswith(m["interne"]):
                continue
            public = m["public"] + chemin[len(m["interne"]):]
        return {
            "exposee": True,
            "chemin_public": public,
            "route": m["nom"],
            "amont": m["amont"],
            "politique_passerelle": m["politique"],
        }
    return {"exposee": False, "chemin_public": None, "route": None,
            "amont": None, "politique_passerelle": None}


def services_deployes():
    """Les services que `docker-compose.prod.yml` porte reellement."""
    try:
        import yaml
        compose = yaml.safe_load(io.open(COMPOSE_PROD, encoding="utf-8").read())
        return set(compose.get("services") or {})
    except Exception:
        return None


def main():
    reponses = reponses_du_depot()
    motifs = motifs_de_la_passerelle()
    deployes = services_deployes()
    routes = []
    total_map = 0

    for chemin in fichiers():
        texte = io.open(chemin, encoding="utf-8", errors="replace").read()
        if not MAP_TOUS.search(texte):
            continue
        total_map += len(MAP_TOUS.findall(texte))

        # Les groupes, resolus transitivement (un groupe peut naitre d'un groupe).
        prefixes = {}
        etiquettes = {}
        acces_groupe = {}
        for m in GROUPE.finditer(texte):
            variable, parent, fabrique, chemin_groupe = m.groups()
            fin = texte.find(";", m.end())
            declaration = texte[m.start():fin if fin > 0 else m.end()]
            etiquette = TAG.search(declaration)
            prefixes[variable] = prefixes.get(parent, "") + chemin_groupe
            etiquettes[variable] = etiquette.group(1) if etiquette else etiquettes.get(parent)
            impose = ACCES_PAR_FABRIQUE.get(fabrique, "jeton + " + fabrique)
            if impose == "hérité du groupe":
                impose = acces_groupe.get(parent, "anonyme")
                if "RequireAuthorization" in declaration:
                    impose = "jeton"
            acces_groupe[variable] = impose

        gestionnaires = corps_des_gestionnaires(texte)

        for m in ENDPOINT.finditer(texte):
            variable, methode, sous_chemin, gestionnaire = m.groups()
            prefixe = prefixes.get(variable, "")
            if not prefixe and not sous_chemin.startswith("/"):
                continue                      # chemin relatif sans groupe connu
            complet = (prefixe + sous_chemin) or "/"

            fin = texte.find(";", m.end())
            chaine = texte[m.end():fin if fin > 0 else m.end()]

            if "AllowAnonymous()" in chaine:
                acces = "anonyme"
            elif "RequireAuthorization(" in chaine:
                politique = re.search(r'RequireAuthorization\(\s*([^)]*)\)', chaine)
                arg = (politique.group(1).strip() if politique else "")
                acces = "jeton + " + arg if arg else "jeton"
            elif "RequireAuthorization()" in chaine:
                acces = "jeton"
            else:
                acces = acces_groupe.get(variable, "hérité du groupe")

            params, corps = gestionnaires.get(gestionnaire.split(".")[-1], ("", ""))
            envoi = ENVOI.search(corps)
            commande = envoi.group(1) if envoi else None

            routes.append({
                "service": service_de(chemin),
                "methode": methode.upper(),
                "chemin": complet,
                "etiquette": etiquettes.get(variable),
                "acces": acces,
                "limiteur": bool(re.search(r'RequireRateLimiting', chaine)),
                "idempotent": "AllowIdempotency()" in chaine,
                "gestionnaire": gestionnaire,
                "requete": corps_de_requete(params),
                "commande": commande,
                "reponse": reponses.get(commande) if commande else None,
                "fichier": os.path.relpath(chemin, RACINE),
                "deploye": None if deployes is None
                           else (service_de(chemin) in deployes),
                **exposition(complet, motifs),
            })

    routes.sort(key=lambda r: (r["service"], r["chemin"], r["methode"]))
    json.dump({
        "routes": routes,
        "resolues": len(routes),
        "appels_map_trouves": total_map,
        "motifs_passerelle": len(motifs),
        "base_publique": "https://api.hba-express.com",
    }, sys.stdout, ensure_ascii=False, indent=1)
    return 0


if __name__ == "__main__":
    sys.exit(main())
