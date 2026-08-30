#!/usr/bin/env python3
"""Engendre `docker-compose.prod.yml` a partir du compose de developpement.

═══════════════════════════════════════════════════════════════════════════════
POURQUOI ENGENDRER PLUTOT QU'ECRIRE.

`docker-compose.dev.yml` est la SEULE description complete des vingt services :
leurs variables, leurs dependances, leurs adresses mutuelles. Les manifestes k8s
n'en couvrent que quinze, et les cinq du lot food n'existent nulle part ailleurs.

Ecrire un second compose a la main donnerait deux descriptions de la meme
plateforme. Elles divergeraient au premier service dont on change une variable —
et la divergence ne se verrait qu'en production, sur un service qui demarre en
lisant une configuration d'il y a trois mois.

CE QUE LA TRANSFORMATION FAIT, ET C'EST TOUT :

  1. `build:` devient `image: ghcr.io/<proprietaire>/<service>:${TAG}` ;
  2. `ASPNETCORE_ENVIRONMENT` passe a Production ;
  3. les chaines de connexion pointent la base EXTERNE, avec un role par service
     et un mot de passe pris dans l'environnement ;
  4. tout secret ecrit en clair dans le compose de developpement est remplace par
     une reference `${VARIABLE}` — et le script REFUSE d'ecrire s'il en reste un ;
  5. `restart: unless-stopped`, absent en developpement ;
  6. les services d'outillage (interfaces web, amorcages) et postgres sont
     retires ;
  7. les services qui refusent de demarrer en production sont retires, avec la
     raison ecrite dans le fichier.

CE QUE CE SCRIPT NE FAIT PAS :

  - il ne valide aucune valeur : que le mot de passe soit le bon, seul Postgres
    le dira ;
  - il ne cree ni le reseau, ni les volumes, ni le proxy TLS — voir le runbook ;
  - il ne construit ni ne pousse aucune image ;
  - il n'invente aucune variable absente du compose de developpement. Un service
    qui manque une variable en production la manquait deja en developpement.
═══════════════════════════════════════════════════════════════════════════════
"""

import os
import re
import sys

RACINE = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
SOURCE = os.path.join(RACINE, "docker-compose.dev.yml")
SORTIE = os.path.join(RACINE, "docker-compose.prod.yml")

PROPRIETAIRE = "hectorberi01"

# Services d'infrastructure et d'outillage qui ne vont pas en production.
#   postgres  : la base vit sur un second VPS (§2), joignable par le tunnel.
#   *-ui      : consoles d'exploration, jamais exposees.
#   minio-init: amorcage de developpement ; en production les buckets se creent
#               une fois, a la main, et c'est dans le runbook.
HORS_PRODUCTION = {
    "postgres": "la base vit sur un second VPS, jointe par le tunnel",
    "redis-ui": "console d'exploration — jamais en production",
    "kafka-ui": "console d'exploration — jamais en production",
    "minio-init": "amorçage de développement ; en production, voir le runbook",
}

# Services qui REFUSENT de demarrer en production. Les inclure donnerait des
# conteneurs qui redemarrent en boucle.
BLOQUES = {
    "notification-service":
        "aucun adaptateur ISmsSender de production n'existe ; le SMS est le "
        "canal OTP par défaut, et NotificationsModuleInstaller lève dans les "
        "deux branches. CONSÉQUENCE : aucun courriel ni SMS ne part.",
    "return-refund-service":
        "deux adaptateurs gRPC restent des bouchons — la marchandise retournée "
        "n'est jamais remise en stock, et aucune course d'enlèvement n'est "
        "créée alors qu'un numéro est rendu au client.",
}

# Tout ce qui ne sera PAS dans `services:` — et qu'aucun `depends_on` ne doit
# donc plus nommer. Reunir les deux tables ici evite qu'on en ajoute une
# troisieme un jour sans penser aux dependances.
ECARTES = {**HORS_PRODUCTION, **BLOQUES}

# Chaque service et la base qu'il emploie. Le role porte le nom de la base :
# c'est ce que `scripts/db/creer-bases.sh` garantit.
BASES = {
    "identity-service": "hba_identity",
    "user-service": "hba_user",
    "media-service": "hba_media",
    "notification-service": "hba_communication",
    "payment-service": "hba_financial",
    "promotion-service": "hba_promotion",
    "review-service": "hba_engagement",
    "catalog-service": "hba_catalog",
    "cart-service": "hba_commerce",
    "inventory-service": "hba_inventory",
    "order-service": "hba_order",
    "seller-service": "hba_merchant",
    "return-refund-service": "hba_commerce",
    "delivery-service": "hba_delivery",
    "driver-service": "hba_delivery",
    "delivery-pricing-service": "hba_delivery",
    "route-service": "hba_delivery",
    "food-cart-service": "hba_food",
    "food-order-service": "hba_food",
    "restaurant-service": "hba_food",
}

# Variables dont la valeur est un SECRET : elles deviennent des references.
# La liste est explicite, et non un motif : un motif attrape trop ou trop peu,
# et se tromper ici met un secret de developpement en production.
SECRETS = {
    "AUTHENTICATION__SIGNINGKEY",
    "JWT__SIGNINGKEY",
    "INTERNAL__APIKEY",
    "INTERNAL__PRIVATEKEY",
    "INTERNAL__PUBLICKEYS",
    "SECURITY__SECRETPROTECTION__KEY",
    "ADMIN__PASSWORD",
    "NOTIFICATIONS__EMAIL__APIKEY",
    "MEDIA__STORAGE__ACCESSKEYID",
    "MEDIA__STORAGE__SECRETACCESSKEY",
    "MINIO_ROOT_USER",
    "MINIO_ROOT_PASSWORD",
}

# Valeurs de developpement qui ne sont PAS des secrets, mais qui seraient fausses
# en production. Elles ne declenchent aucune alarme — d'ou cette table.
REMPLACEMENTS = {
    "ADMIN__EMAIL": "hector.adjakpa@hbatechettrade.com",
}

# ═══════════════════════════════════════════════════════════════════════════════
# AUCUN PORT N'EST PUBLIE, SAUF CELUI DE LA PASSERELLE.
#
# Le compose de developpement publie Redis (6379), Kafka (9092) et les DEUX
# ports de MinIO (9000 pour l'API S3, 9001 pour la console web). C'est correct
# sur un poste : ces ports servent a inspecter l'etat pendant qu'on developpe.
#
# Sur le VPS, `ports:` publie sur TOUTES les interfaces, Internet compris —
# Docker ecrit d'ailleurs ses regles DIRECTEMENT dans nftables, en amont des
# regles d'un pare-feu configure a la main, qui ne les voit donc jamais.
#
# Un Redis sans mot de passe joignable depuis Internet se fait prendre en
# minutes ; une console MinIO ouverte donne les pieces KYB. Les services se
# parlent par le reseau `hba-backend`, ou aucune publication n'est necessaire.
#
# La passerelle garde le sien : c'est elle que le proxy TLS interroge.
PORTS_AUTORISES = {"gateway"}

# Ce qui trahit un secret laisse en clair. Le controle final s'appuie dessus.
MOTIFS_SUSPECTS = [
    (re.compile(r"Password=hba\b"), "mot de passe de développement « hba »"),
    (re.compile(r"hba-development-signing-key"), "clé de signature de développement"),
    (re.compile(r"Admin123!"), "mot de passe administrateur de développement"),
    (re.compile(r"\bminioadmin\b"), "identifiants MinIO de développement"),
    (re.compile(r"cle-interne-de-test"), "clé interne de test"),
]


# Reglages qui n'existent QUE en developpement, et dont la presence en
# production empeche le demarrage — c'est leur role.
DEV_SEULEMENT = {
    "INTERNAL__IDENTITESNONSIGNEES":
        "identites gRPC non signees : `AddHbaGrpc` leve hors Development",
}


def ancre_de_production(lignes):
    """Rejoue l'ancre `x-dev-auth` en version production.

    Les cles sont les memes — c'est le point : ce que vingt-et-un services
    attendent ne se decide pas ici. Seules les VALEURS changent, et seulement
    pour celles que `SECRETS` designe.
    """
    debut = next((i for i, l in enumerate(lignes)
                  if l.startswith("x-dev-auth:")), None)
    if debut is None:
        return []

    corps = []
    for l in lignes[debut + 1:]:
        if l.strip() and not l.startswith(" "):
            break
        m = re.match(r"^  ([A-Z][A-Z0-9_]*):\s*(.*)$", l)
        if not m:
            continue
        cle, valeur = m.group(1), m.group(2).strip()

        if cle in DEV_SEULEMENT:
            corps.append("  # %s : retire — %s.\n" % (cle, DEV_SEULEMENT[cle]))
            continue
        if cle in SECRETS:
            corps.append("  %s: ${%s:?%s est obligatoire en production}\n"
                         % (cle, cle, cle))
            continue
        if cle in REMPLACEMENTS:
            corps.append("  %s: %s\n" % (cle, REMPLACEMENTS[cle]))
            continue
        corps.append("  %s: %s\n" % (cle, valeur))

    if not corps:
        return []

    return [
        "# ═════════════════════════════════════════════════════════════════════════════\n",
        "# LES CLES PARTAGEES PAR TOUS LES SERVICES.\n",
        "#\n",
        "# Meme forme que `x-dev-auth` dans le compose de developpement, memes cles —\n",
        "# et c'est voulu : ce que les services attendent ne se decide pas ici. Seules\n",
        "# les valeurs changent, remplacees par des references obligatoires.\n",
        "#\n",
        "# AUTHENTICATION__SIGNINGKEY et JWT__SIGNINGKEY doivent porter la MEME valeur :\n",
        "# identity-service signe avec l'une, les autres verifient avec l'autre.\n",
        "#\n",
        "# INTERNAL__APIKEY doit etre IDENTIQUE partout — l'appelant la presente,\n",
        "# l'appele la compare. Une divergence rend `NotFound`, muet sur la cause.\n",
        "#\n",
        "# SECURITY__SECRETPROTECTION__KEY ne se regenere PAS : ce qu'elle a chiffre ne\n",
        "# se dechiffre pas avec la suivante.\n",
        "# ═════════════════════════════════════════════════════════════════════════════\n",
        "x-prod-auth: &prod-auth\n",
    ] + corps + ["\n"]


def bloc_de_service(lignes, debut, fin):
    """Rend (nom, lignes du bloc). `debut` est l'index de la ligne `  nom:`."""
    nom = lignes[debut].strip().rstrip(":")
    corps = []
    for l in lignes[debut + 1:fin]:
        if re.match(r"^  \S", l) or re.match(r"^\S", l):
            break
        corps.append(l)
    return nom, corps


# Les services qui fusionnent l'ancre partagee. Rempli par `transformer`, relu
# par les controles : une ancre definie que personne ne fusionne, ou l'inverse,
# ne doit pas passer inapercu.
fusions = []


def transformer(nom, corps):
    """Applique les sept transformations a un bloc de service."""
    sortie = []
    i = 0
    while i < len(corps):
        l = corps[i]

        # ═══════════════════════════════════════════════════════════════════
        # 1. `build:` EST CONSERVÉ, ET `image:` VIENT S'AJOUTER.
        #
        # La première version remplaçait l'un par l'autre, en supposant que les
        # images viendraient d'un registre. Le déploiement se fait depuis le
        # poste, sans registre : `docker compose build` sur le VPS n'aurait plus
        # rien eu à construire, et `up` aurait cherché à TIRER une image qui
        # n'existe nulle part.
        #
        # Compose accepte les deux ensemble : il construit, et nomme le résultat
        # avec `image:`. Le tag porte le SHA du commit, donc `docker images` sur
        # le VPS dit quel commit tourne — ce qu'aucun `docker ps` ne dirait.
        #
        # Et si un registre revient un jour, `compose push` fonctionne sans rien
        # changer ici.
        # ═══════════════════════════════════════════════════════════════════
        if re.match(r"^    build:\s*$", l):
            sortie.append("    image: ghcr.io/%s/%s:${HBA_TAG:?le tag d'image est obligatoire}\n"
                          % (PROPRIETAIRE, nom))
            sortie.append(l)
            i += 1
            while i < len(corps) and re.match(r"^      ", corps[i]):
                sortie.append(corps[i])
                i += 1
            continue

        # ═══════════════════════════════════════════════════════════════════
        # UNE DEPENDANCE VERS UN SERVICE ECARTE DOIT PARTIR AVEC LUI.
        #
        # CE QUI ETAIT CASSE : ce script retirait `postgres`, `minio-init`,
        # `notification-service` et `return-refund-service` de `services:`, et
        # laissait les `depends_on:` qui les nommaient. Compose refuse alors le
        # fichier entier :
        #
        #     service "order-service" depends on undefined service "postgres"
        #
        # Seize services etaient concernes. Le fichier restait du YAML valide —
        # d'ou le fait qu'aucun controle ne l'avait vu — mais Compose, lui, exige
        # que toute cible de `depends_on` existe.
        #
        # CE QUE CELA NE COUVRE PAS : rien ne remplace l'attente supprimee.
        # `postgres` n'est plus la parce que la base vit sur un second VPS, et
        # personne ne verifie qu'elle repond avant qu'un service demarre. Un
        # service qui part trop tot echouera a se connecter et sera relance par
        # `restart: unless-stopped` — bruyant, mais sans perte.
        # ═══════════════════════════════════════════════════════════════════
        if re.match(r"^    depends_on:\s*$", l):
            i += 1
            gardees = []
            while i < len(corps) and re.match(r"^      \S", corps[i]):
                cible = re.match(r"^      ([\w.-]+):\s*$", corps[i])
                if cible is None:
                    gardees.append(corps[i])
                    i += 1
                    continue
                bloc = [corps[i]]
                i += 1
                while i < len(corps) and re.match(r"^        ", corps[i]):
                    bloc.append(corps[i])
                    i += 1
                if cible.group(1) in ECARTES:
                    gardees.append("      # %s : retire — %s.\n"
                                   % (cible.group(1), ECARTES[cible.group(1)]))
                else:
                    gardees.extend(bloc)
            if any(re.match(r"^      [\w.-]+:", g) for g in gardees):
                sortie.append(l)
                sortie.extend(gardees)
            else:
                sortie.append("    # `depends_on:` retire : toutes ses cibles "
                              "sont ecartees de la production.\n")
                sortie.extend(g for g in gardees if g.lstrip().startswith("#"))
            continue

        # ═══════════════════════════════════════════════════════════════════
        # LA FUSION CHANGE D'ANCRE — ELLE NE DISPARAIT PAS.
        #
        # CE QUI ETAIT CASSE : ce bloc jetait `<<: *dev-auth` et ne mettait RIEN
        # a la place. Le raisonnement etait juste — l'ancre de developpement
        # porte trois secrets en clair — et la conclusion fausse.
        #
        # Vingt-et-un services tiraient de cette ancre AUTHENTICATION__SIGNINGKEY,
        # INTERNAL__APIKEY et SECURITY__SECRETPROTECTION__KEY. Le compose engendre
        # n'en portait plus qu'une occurrence, celle ecrite en clair dans un seul
        # service. Les vingt autres demarraient sans clé de signature, sans clé
        # interne et sans clé de chiffrement.
        #
        # On ecrit donc une ancre de PRODUCTION, faite des memes cles mais dont
        # les secrets sont des references `${...:?}`, et chaque service la fusionne.
        # ═══════════════════════════════════════════════════════════════════
        if "<<: *dev-auth" in l:
            sortie.append("      <<: *prod-auth\n")
            fusions.append(nom)
            i += 1
            continue

        # 7. Les ports publiés — seule la passerelle en garde.
        if re.match(r"^    ports:\s*$", l) and nom not in PORTS_AUTORISES:
            i += 1
            while i < len(corps) and re.match(r"^      [-#]", corps[i]):
                i += 1
            sortie.append("    # `ports:` retiré : publier sur le VPS, c'est publier sur Internet.\n")
            sortie.append("    # Les services se joignent par le réseau `hba-backend`.\n")
            continue

        # 2. L'environnement.
        m = re.match(r"^      ([A-Z][A-Z0-9_]*):\s*(.*)$", l)
        if m:
            cle, valeur = m.group(1), m.group(2).strip()

            if cle == "ASPNETCORE_ENVIRONMENT":
                sortie.append("      ASPNETCORE_ENVIRONMENT: Production\n")
                i += 1
                continue

            # 3. La base externe, un rôle par service.
            if cle == "CONNECTIONSTRINGS__DEFAULT":
                base = BASES.get(nom)
                if base is None:
                    sortie.append("      # PAS DE BASE CONNUE POUR CE SERVICE — à compléter dans BASES\n")
                    sortie.append(l)
                else:
                    sortie.append(
                        "      CONNECTIONSTRINGS__DEFAULT: "
                        "Host=${HBA_PGHOST:-10.20.0.2};Port=5432;Database=%s;"
                        "Username=%s;Password=${%s_PASSWORD:?mot de passe de %s absent}\n"
                        % (base, base, base.upper(), base))
                i += 1
                continue

            if cle in REMPLACEMENTS:
                sortie.append("      %s: %s\n" % (cle, REMPLACEMENTS[cle]))
                i += 1
                continue

            # 4. Les secrets deviennent des références.
            if cle in SECRETS:
                sortie.append("      %s: ${%s:?%s est obligatoire en production}\n"
                              % (cle, cle, cle))
                i += 1
                continue

        sortie.append(l)
        i += 1

    # 5. Redémarrage automatique — absent en développement, indispensable ici.
    if not any("restart:" in l for l in sortie):
        sortie.append("    restart: unless-stopped\n")

    # ═══════════════════════════════════════════════════════════════════════
    # 8. UN NOM DE CONTENEUR FIXE : `hba-<service>`.
    #
    # Sans lui, Compose nomme `<projet>-<service>-<numéro>` :
    # `hba-prod-identity-service-1`. Lisible pour Compose, pénible à taper et à
    # lire dans un `docker ps` de vingt-trois lignes.
    #
    # CE QUE CE NOM COÛTE, ET IL FAUT LE SAVOIR :
    #
    #   • `docker compose up --scale order-service=3` DEVIENT IMPOSSIBLE. Deux
    #     conteneurs ne peuvent pas porter le même nom ; Compose refuse avec
    #     « can't set container_name and scale ». Monter en charge demandera de
    #     retirer cette ligne — c'est un arbitrage de MVP sur une machine.
    #
    #   • LE NOM NE PORTE PLUS L'ENVIRONNEMENT. Staging et production auraient
    #     des conteneurs homonymes. Sur deux VPS distincts, sans conséquence.
    #     Sur la MÊME machine, le second `up` échouerait sur un conflit de nom —
    #     ce qui est le bon échec : le §2 interdit de les colocaliser, et sans
    #     nom fixe le préfixe de projet aurait masqué l'erreur.
    #
    # CE QUE CE NOM NE CHANGE PAS : les services se joignent toujours par le nom
    # du SERVICE — `http://identity-service:8080` — que Compose pose en alias
    # sur le réseau. Le nom du conteneur ne sert qu'aux commandes d'exploitation.
    # ═══════════════════════════════════════════════════════════════════════
    if not any("container_name:" in l for l in sortie):
        sortie.insert(0, "    container_name: hba-%s\n" % nom)

    return sortie


def main():
    if not os.path.exists(SOURCE):
        print("introuvable : %s" % SOURCE, file=sys.stderr)
        return 1

    with open(SOURCE, encoding="utf-8") as f:
        lignes = f.readlines()

    # ON BORNE LE DECOUPAGE A LA SECTION `services:`.
    #
    # Un motif `^  nom:` seul attrape aussi les entrees de `volumes:` et de
    # `networks:` — `postgres-data`, `hba-backend` — qui ont exactement la meme
    # forme. La premiere version en a fait cinq faux services, recopies tels
    # quels, puis a rajoute ses propres sections : le fichier portait deux fois
    # `volumes:` et deux fois `networks:`. Compose aurait pris la seconde et
    # ignore la premiere, ou refuse le fichier — selon la version.
    debut_services = next(
        (i for i, l in enumerate(lignes) if l.rstrip() == "services:"), None)
    if debut_services is None:
        print("le compose source n'a pas de section `services:` — format inattendu",
              file=sys.stderr)
        return 1

    fin_services = next(
        (i for i in range(debut_services + 1, len(lignes))
         if lignes[i].strip() and not lignes[i].startswith((" ", "\t"))),
        len(lignes))

    debuts = [i for i in range(debut_services + 1, fin_services)
              if re.match(r"^  [a-z][a-z0-9-]*:\s*$", lignes[i])]

    retenus, ecartes = [], []
    for d in debuts:
        nom, corps = bloc_de_service(lignes, d, fin_services)
        if nom in HORS_PRODUCTION:
            ecartes.append((nom, HORS_PRODUCTION[nom]))
            continue
        if nom in BLOQUES:
            ecartes.append((nom, BLOQUES[nom]))
            continue
        retenus.append((nom, transformer(nom, corps)))

    if not retenus:
        print("aucun service retenu — le découpage a échoué", file=sys.stderr)
        return 1

    entete = [
        "# ═══════════════════════════════════════════════════════════════════════════════\n",
        "# ENGENDRÉ PAR scripts/generer-compose-prod.py — NE PAS ÉDITER À LA MAIN.\n",
        "#\n",
        "# La source est `docker-compose.dev.yml`, seule description complète des vingt\n",
        "# services. Modifier un service là-bas, puis relancer le script : un second\n",
        "# compose écrit à la main divergerait au premier changement de variable, et la\n",
        "# divergence ne se verrait qu'en production.\n",
        "#\n",
        "# LES VALEURS NE SONT PAS ICI. Chaque secret est une référence `${VAR:?...}` :\n",
        "# Compose REFUSE de démarrer si la variable est absente, plutôt que de lancer un\n",
        "# service avec une chaîne vide. Les valeurs vivent dans un fichier d'environnement\n",
        "# hors du dépôt — voir docs/RUNBOOK-COMPOSE.md.\n",
        "#\n",
        "# CE QUE CE FICHIER NE PORTE PAS : le proxy TLS qui sert api.hba-express.com, la\n",
        "# création des buckets MinIO, et les migrations de base. Tout est dans le runbook.\n",
    ]
    if ecartes:
        entete.append("#\n")
        entete.append("# SERVICES ÉCARTÉS, ET POURQUOI :\n")
        for nom, raison in ecartes:
            entete.append("#\n")
            entete.append("#   %s\n" % nom)
            for morceau in [raison[j:j + 68] for j in range(0, len(raison), 68)]:
                entete.append("#     %s\n" % morceau)
    entete.append("# ═══════════════════════════════════════════════════════════════════════════════\n")
    entete.append("\n")

    # L'ANCRE DOIT PRECEDER SES ALIAS : YAML resout dans l'ordre du document.
    ancre = ancre_de_production(lignes)
    if not ancre:
        print("aucune ancre `x-dev-auth` dans la source — les cles partagees "
              "seraient perdues pour tous les services", file=sys.stderr)
        return 1
    entete.extend(ancre)

    entete.append("services:\n")

    corps_total = []
    for nom, corps in retenus:
        corps_total.append("\n  %s:\n" % nom)
        corps_total.extend(corps)

    queue = [
        "\nvolumes:\n",
        "  kafka-data:\n",
        "  minio-data:\n",
        "  rembg-models:\n",
        "\nnetworks:\n",
        "  hba-backend:\n",
    ]

    rendu = "".join(entete + corps_total + queue)

    # 6. LE CONTRÔLE QUI COMPTE : aucun secret de développement ne doit survivre.
    #
    # Une transformation qui rate une variable produit un fichier d'apparence
    # correcte, qui démarre, et qui signe les jetons de production avec la clé
    # publiée dans le dépôt. C'est le seul défaut de ce script qui serait à la
    # fois silencieux et grave.
    fuites = []
    for numero, ligne in enumerate(rendu.splitlines(), 1):
        if ligne.lstrip().startswith("#"):
            continue
        for motif, quoi in MOTIFS_SUSPECTS:
            if motif.search(ligne):
                cle = ligne.split(":")[0].strip()
                fuites.append("ligne %d : %s — %s" % (numero, cle, quoi))

    # Tout service applicatif doit pouvoir etre CONSTRUIT : sans registre, une
    # image sans `build` est une image que personne ne peut produire.
    import yaml as _yaml
    try:
        rendu_charge = _yaml.safe_load(rendu)
    except Exception as e:
        print("REFUS : le rendu n'est pas du YAML valide (%s)" % e, file=sys.stderr)
        return 1
    sans_build = [n for n, v in (rendu_charge.get("services") or {}).items()
                  if "image" in v and "build" not in v
                  and not str(v["image"]).startswith(("redis", "confluentinc",
                                                      "minio", "danielgatis"))]
    if sans_build:
        print("REFUS : %s ont une image mais aucun `build` — rien ne pourrait les "
              "produire sans registre." % ", ".join(sans_build), file=sys.stderr)
        return 1

    # ═══════════════════════════════════════════════════════════════════════
    # TOUTE CIBLE DE `depends_on` DOIT EXISTER.
    #
    # Compose refuse le fichier ENTIER sur une seule dependance orpheline —
    # « service X depends on undefined service Y » — et le YAML, lui, reste
    # valide, donc aucun controle de forme ne l'attrape. C'est exactement ce
    # qui est arrive en retirant `postgres` sans toucher aux seize services qui
    # l'attendaient.
    # ═══════════════════════════════════════════════════════════════════════
    definis = set(rendu_charge.get("services") or {})
    orphelines = []
    for nom, v in (rendu_charge.get("services") or {}).items():
        for cible in (v.get("depends_on") or {}):
            if cible not in definis:
                orphelines.append("%s -> %s" % (nom, cible))
    if orphelines:
        print("REFUS : %d dépendance(s) vers un service absent du rendu."
              % len(orphelines), file=sys.stderr)
        for o in orphelines:
            print("    " + o, file=sys.stderr)
        print("    Compose refuserait le fichier entier. Ajouter le service à "
              "ECARTES, ou le réintégrer.", file=sys.stderr)
        return 1

    noms = [v.get("container_name") for v in (rendu_charge.get("services") or {}).values()]
    if len(noms) != len(set(noms)) or any(n is None for n in noms):
        print("REFUS : noms de conteneur manquants ou en double — %s"
              % sorted(n for n in noms if noms.count(n) > 1 or n is None),
              file=sys.stderr)
        return 1

    for section in ("volumes:", "networks:"):
        combien = sum(1 for l in rendu.splitlines() if l.rstrip() == section)
        if combien != 1:
            print("REFUS : %d section(s) `%s` dans le rendu, une seule attendue."
                  % (combien, section), file=sys.stderr)
            return 1

    # ═══════════════════════════════════════════════════════════════════════
    # CHAQUE SERVICE QUI FUSIONNAIT L'ANCRE DOIT ENCORE LA FUSIONNER.
    #
    # Le defaut que ce controle ferme : la fusion `<<: *dev-auth` etait jetee
    # sans remplacement, et vingt-et-un services perdaient leur cle de
    # signature, leur cle interne et leur cle de chiffrement. Le fichier restait
    # du YAML valide, Compose demarrait, et les services rendaient 401 partout.
    #
    # On compare donc ce que la source demandait a ce que le rendu porte. Une
    # ancre definie que personne ne fusionne compte aussi comme un echec : elle
    # signalerait que le remplacement n'a pas eu lieu.
    # ═══════════════════════════════════════════════════════════════════════
    attendues = len(fusions)
    obtenues = sum(1 for l in rendu.splitlines() if l.strip() == "<<: *prod-auth")
    if attendues == 0 or obtenues != attendues:
        print("REFUS : %d service(s) fusionnaient l'ancre partagée, %d la fusionnent "
              "dans le rendu." % (attendues, obtenues), file=sys.stderr)
        print("    Sans elle : ni clé de signature, ni clé interne, ni clé de "
              "chiffrement.", file=sys.stderr)
        return 1

    for cle in DEV_SEULEMENT:
        if any(l.strip().startswith(cle + ":") for l in rendu.splitlines()):
            print("REFUS : %s survit au rendu — ce réglage empêche le démarrage "
                  "hors Development." % cle, file=sys.stderr)
            return 1

    if fuites:
        print("REFUS : %d secret(s) de développement survivraient à la transformation."
              % len(fuites), file=sys.stderr)
        for f in fuites:
            print("    " + f, file=sys.stderr)
        print("Ajouter la clé concernée à SECRETS, puis relancer.", file=sys.stderr)
        return 1

    with open(SORTIE, "w", encoding="utf-8") as f:
        f.write(rendu)

    print("%d service(s) retenu(s), %d écarté(s)" % (len(retenus), len(ecartes)))
    for nom, _ in ecartes:
        print("    écarté : %s" % nom)
    print("écrit : %s" % os.path.relpath(SORTIE, RACINE))
    print("aucun secret de développement n'a survécu au contrôle.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
