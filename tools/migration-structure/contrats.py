#!/usr/bin/env python3
"""
LES CONTRATS PUBLIES REJOIGNENT LEUR PROPRIETAIRE.

`shared/contracts/` etait un dossier que personne ne possede : « qui possede le
contrat de commande » n'avait pas de reponse dans l'arborescence. Chaque projet
rejoint le `src/` du service qui le publie, exactement comme
`HBA.Merchants.Contracts` vit deja dans `seller-service/src/`.

CE QUE ÇA NE CHANGE PAS : un contrat publie reste partage. Sept services
referencent `HBA.Ordering.Contracts` avant, sept apres. Ce qui bouge, c'est son
ADRESSE — et donc la reponse a « qui a le droit de le modifier ».

LE PIEGE, ET IL EST DANS LES DOCKERFILES.

Chaque image fait `COPY shared ./shared`, ce qui embarquait TOUS les contrats
gratuitement. Un contrat sorti de `shared/` doit desormais etre copie
explicitement — sinon `dotnet restore` echoue A L'INTERIEUR DE L'IMAGE, sur un
message qui parle d'un chemin et pas de la fonctionnalite qui en depend. Ce
script calcule la cloture transitive de chaque service et ajoute les `COPY`
manquants.
"""
import os, re, io, sys, shutil, collections

RACINE = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SKIP = {"obj", "bin", ".git", "build", "node_modules"}
SEC = "--ecrire" in sys.argv

# projet -> service proprietaire (celui qui le publie)
PROPRIETAIRES = {
    "HBA.Ordering.Contracts":        "services/marketplace/order-service",
    "HBA.Products.Contracts":        "services/marketplace/catalog-service",
    "HBA.Returns.Contracts":         "services/marketplace/return-refund-service",
    "HBA.Identity.Contracts":        "services/common/identity-service",
    "HBA.Media.Contracts":           "services/common/media-service",
    "HBA.Payments.Contracts":        "services/common/payment-service",
    "HBA.Promotions.Contracts":      "services/common/promotion-service",
    "HBA.Pricing.Promotion":         "services/common/promotion-service",
    "HBA.Users.Contracts":           "services/common/user-service",
    "HBA.DeliveryPricing.Contracts": "services/delivery/delivery-pricing-service",
    "HBA.Drivers.Contracts":         "services/delivery/driver-service",
    "HBA.Routes.Contracts":          "services/delivery/route-service",
}

# CE QUI RESTE DANS shared/contracts, ET POURQUOI :
#   HBA.Pricing.Contracts  — `IPricingModuleApi` est un PORT de plateforme :
#       cart, food-cart et order en dependent, et l'implementation
#       (`PromotionPricingModuleApi`) est ailleurs. Aucun service ne le publie,
#       donc aucun ne peut le posseder.
#   HBA.Shipping.Contracts — MORT : il decrit un service qui n'existe pas. Le
#       deplacer proprement ne le rendrait pas moins mort ; c'est une suppression
#       a arbitrer, pas un rangement.


def fichiers(base, ext):
    for d, dirs, fs in os.walk(base):
        dirs[:] = [x for x in dirs if x not in SKIP]
        for f in fs:
            if f.endswith(ext):
                yield os.path.join(d, f)


def cloture(csproj, index):
    """Tous les projets atteignables depuis celui-ci."""
    vus, pile = set(), [csproj]
    while pile:
        c = pile.pop()
        if c in vus or not os.path.exists(c):
            continue
        vus.add(c)
        s = io.open(c, encoding="utf-8", errors="replace").read()
        for m in re.finditer(r'ProjectReference\s+Include="([^"]+)"', s):
            chemin = os.path.normpath(os.path.join(os.path.dirname(c), m.group(1).replace("\\", "/")))
            pile.append(chemin)
    return vus


def main():
    plan = []
    for projet, service in sorted(PROPRIETAIRES.items()):
        source = os.path.join(RACINE, "shared", "contracts", projet)
        cible = os.path.join(RACINE, service, "src", projet)
        if not os.path.isdir(source):
            print(f"  DEJA DEPLACE : {projet}")
            continue
        plan.append((projet, source, cible))
        print(f"  {projet:32} -> {service}/src/")

    if not SEC:
        print("\nSIMULATION.")
        return

    for projet, source, cible in plan:
        os.makedirs(os.path.dirname(cible), exist_ok=True)
        shutil.move(source, cible)

    # ── les references de projet, partout
    nouvelles = {p: os.path.join(RACINE, s, "src", p, p + ".csproj") for p, s in PROPRIETAIRES.items()}
    touches = 0
    for c in list(fichiers(os.path.join(RACINE, "services"), ".csproj")) + \
             list(fichiers(os.path.join(RACINE, "bff"), ".csproj")) + \
             list(fichiers(os.path.join(RACINE, "shared"), ".csproj")) + \
             list(fichiers(os.path.join(RACINE, "tests"), ".csproj")) + \
             list(fichiers(os.path.join(RACINE, "tools"), ".csproj")):
        s = io.open(c, encoding="utf-8").read()
        avant = s
        for projet, cible in nouvelles.items():
            def remplacer(m, projet=projet, cible=cible):
                rel = os.path.relpath(cible, os.path.dirname(c)).replace("/", "\\")
                return m.group(0).replace(m.group(1), rel)
            s = re.sub(r'ProjectReference\s+Include="([^"]*' + re.escape(projet) + r'\.csproj)"',
                       remplacer, s)
        if s != avant:
            io.open(c, "w", encoding="utf-8").write(s)
            touches += 1
    print(f"{touches} csproj : references reecrites")

    # ── la solution
    sln = os.path.join(RACINE, "HBA.sln")
    s = io.open(sln, encoding="utf-8").read()
    for projet, cible in nouvelles.items():
        rel = os.path.relpath(cible, RACINE).replace("/", "\\")
        # LAMBDA ET NON CHAINE : `rel` contient des antislashs (chemin MSBuild), et
        # `re.sub` interprete `\m` comme une sequence d'echappement invalide.
        s = re.sub(r'"[^"]*' + re.escape(projet) + r'\.csproj"', lambda _m, r=rel: f'"{r}"', s)
    io.open(sln, "w", encoding="utf-8").write(s)
    print("solution : chemins reecrits")

    # ── les Dockerfiles : ce que `COPY shared` embarquait gratuitement
    ajouts = 0
    for docker in fichiers(os.path.join(RACINE, "services"), "Dockerfile") \
            if False else [p for p in
                           [os.path.join(d, f)
                            for base in ("services", "bff")
                            for d, dirs, fs in os.walk(os.path.join(RACINE, base))
                            if not any(x in d for x in SKIP)
                            for f in fs if f == "Dockerfile"]]:
        dossier_service = os.path.dirname(docker)
        api = None
        src = os.path.join(dossier_service, "src")
        if not os.path.isdir(src):
            continue
        for x in os.listdir(src):
            if x.endswith(".Api") or x.endswith(".Api"):
                candidat = os.path.join(src, x, x + ".csproj")
                if os.path.exists(candidat):
                    api = candidat
                    break
        if not api:
            continue
        atteignables = cloture(api, None)
        s = io.open(docker, encoding="utf-8").read()
        lignes = []
        for projet, cible in nouvelles.items():
            if cible not in atteignables:
                continue
            rel = os.path.relpath(os.path.dirname(cible), RACINE)
            # deja copie, soit explicitement, soit par un COPY du service entier
            if rel in s or os.path.dirname(os.path.dirname(rel)) + " " in s:
                continue
            service_entier = "COPY " + "/".join(rel.split("/")[:3]) + " "
            if service_entier in s:
                continue
            lignes.append(f"COPY {rel} ./{rel}")
        if lignes:
            entete = ("\n# CES CONTRATS ONT QUITTE `shared/`, ET `COPY shared` NE LES EMBARQUE PLUS.\n"
                      "#\n"
                      "# Sans ces lignes, `dotnet restore` echoue A L'INTERIEUR DE L'IMAGE, sur un\n"
                      "# message qui nomme un chemin et pas la fonctionnalite qui en depend.\n")
            marqueur = "COPY shared ./shared\n"
            s = s.replace(marqueur, marqueur + entete + "\n".join(lignes) + "\n", 1)
            io.open(docker, "w", encoding="utf-8").write(s)
            ajouts += len(lignes)
            print(f"  {os.path.relpath(docker, RACINE)} : +{len(lignes)} COPY")
    print(f"{ajouts} lignes COPY ajoutees")


if __name__ == "__main__":
    main()
