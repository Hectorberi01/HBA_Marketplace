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

import json
import os
import re
import subprocess
import sys

RACINE = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
SOURCE = os.path.join(RACINE, "docker-compose.dev.yml")
SORTIE = os.path.join(RACINE, "docker-compose.prod.yml")

PROPRIETAIRE = "hectorberi01"

# ══════════════════════════════════════════════════════════════════════════════
# LE NOM DU SERVICE COMPOSE N'EST PAS TOUJOURS LE NOM DE L'IMAGE PUBLIEE.
#
# CE DEFAUT AURAIT FAIT ECHOUER LE PREMIER `compose pull` DE PRODUCTION.
#
# Le generateur derivait le nom de l'image du nom du SERVICE COMPOSE. Or la CI
# publie sous le nom de DOSSIER du service : `apps/api-gateway` donne
# `ghcr.io/hectorberi01/api-gateway`, tandis que le compose nomme ce service
# `gateway`. Le fichier engendre reclamait donc
# `ghcr.io/hectorberi01/gateway:<sha>`, qui n'existe dans aucun registre.
#
# LE SYMPTOME AURAIT DESIGNE LES DROITS, PAS LE NOM : un depot prive absent rend
# « denied », exactement comme un jeton sans portee. On aurait cherche du cote
# du `docker login`.
#
# C'est le meme double vocabulaire que partout ailleurs dans cette plateforme —
# le module se nomme par domaine, le depot par dossier. On le traduit ici, une
# fois, et `verifier_images_publiees` ci-dessous refuse toute autre divergence.
# ══════════════════════════════════════════════════════════════════════════════
NOMS_IMAGES = {"gateway": "api-gateway"}

# ══════════════════════════════════════════════════════════════════════════════
# LES SERVICES CONSTRUITS SUR PLACE — CEUX QUE LA CI NE PUBLIE PAS.
#
# `rembg` n'a pas de Dockerfile connu de `images-affectees` : il n'est jamais construit
# par la CI, et son image n'existe dans aucun registre. Il garde donc son
# `build:` et se construit sur le VPS depuis `infra/rembg` — c'est une image
# Python legere, pas les vingt hotes .NET.
#
# Pour tous les autres, `build:` est RETIRE en production : les images viennent
# du registre. Le garder ferait construire sur le VPS une image deja publiee —
# une heure de CPU, et surtout un binaire different de celui qui a ete signe.
# ══════════════════════════════════════════════════════════════════════════════
CONSTRUITS_SUR_PLACE = {"rembg"}

# Services d'infrastructure et d'outillage qui ne vont pas en production.
#   postgres  : la base vit sur un second VPS (§2), joignable par le tunnel.
#   *-ui      : consoles d'exploration, jamais exposees.
#   minio-init: amorcage de developpement ; en production les buckets se creent
#               une fois, a la main, et c'est dans le runbook.
HORS_PRODUCTION = {
    "postgres": "la base vit sur un second VPS, jointe par le tunnel",
    "redis-ui": "console d'exploration — jamais en production",
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

# ═══════════════════════════════════════════════════════════════════════════════
# CE QUE LA PRODUCTION AJOUTE, ET QUE LE DEVELOPPEMENT N'A JAMAIS EU.
#
# `REMPLACEMENTS` corrige une valeur presente ; il fallait aussi pouvoir en
# AJOUTER. `payment-service` en est le cas : aucune cle `PAYMENTS__*` n'existe
# dans le compose de developpement — il tourne sur des passerelles simulees —
# et `PaymentsModuleInstaller` refuse de demarrer en production sans elles.
#
# Ce qui est SECRET devient une reference obligatoire ; ce qui ne l'est pas est
# ecrit en clair, parce que sa valeur EST la decision et doit se relire.
#
# `BASEURL` en live et une cle `sk_live_…` vont ensemble : `KeyMatchesEnvironment`
# refuse le demarrage si l'un dit « bac a sable » et l'autre « production ». Le
# pire cas qu'il ferme n'est pas une panne, c'est de l'argent reel envoye la ou
# l'on croyait faire un essai.
#
# `WEBHOOKSECRET` n'est pas optionnel en production : sans lui, les notifications
# du prestataire sont REJETEES — `AllowUnsignedWhenSecretMissing` est ignore hors
# developpement. Un encaissement partirait sans jamais revenir, et la commande
# resterait « en attente de paiement » alors que l'acheteur a paye.
# ═══════════════════════════════════════════════════════════════════════════════
# ═══════════════════════════════════════════════════════════════════════════════
# L'IDENTITE gRPC : UNE CLE PRIVEE PAR HOTE, UN REGISTRE PUBLIC PARTAGE.
#
# CE QUI ETAIT CASSE : le compose n'en portait AUCUNE. Sans `Internal:PrivateKey`,
# `InternalCallClientInterceptor` leve `FailedPrecondition: Internal identity not
# configured.` — a l'emission, avant meme le reseau. Tout appel entre services
# echoue donc, aujourd'hui, sur la pile deployee.
#
# Et rien ne l'aurait dit au demarrage : le seul garde du socle refuse le drapeau
# `IdentitesNonSignees` hors Development. L'ABSENCE de cle, elle, passe le
# demarrage sans un mot et ne se voit qu'au premier appel inter-services.
#
# LE NOM DE LA VARIABLE VIENT DU PROJET, PAS DU SERVICE COMPOSE.
#
# `scripts/generer-identites-internes.sh` nomme ses variables d'apres l'HOTE tel
# que la table d'autorisations le connait — `HBA.Identity.Api`, pas
# `identity-service` — et remplace les points par des soulignes. On lit donc le
# nom du projet dans l'`ENTRYPOINT` du Dockerfile plutot que de recopier vingt
# correspondances a la main : une table recopiee derive, un ENTRYPOINT non.
#
# CE QUE CELA NE COUVRE PAS : la rotation. Changer une cle demande de redemarrer
# TOUS les hotes ensemble — un hote encore sur l'ancien registre rejette les
# nouveaux appelants, et reciproquement. La rotation partielle coupe les appels
# dans les deux sens.
# ═══════════════════════════════════════════════════════════════════════════════
ENTRYPOINT_DOTNET = re.compile(r'ENTRYPOINT\s*\[\s*"dotnet"\s*,\s*"([\w.]+)\.dll"')


def projet_de_service(build):
    """`build:` d'un service -> nom du projet .NET, lu dans son Dockerfile."""
    if not isinstance(build, dict):
        return None
    chemin = build.get("dockerfile") or os.path.join(build.get("context", ""), "Dockerfile")
    try:
        with open(os.path.join(RACINE, chemin), encoding="utf-8") as f:
            texte = f.read()
    except OSError:
        return None
    trouve = ENTRYPOINT_DOTNET.search(texte)
    return trouve.group(1) if trouve else None


def variable_de_cle(projet):
    return "INTERNAL_KEY_" + projet.upper().replace(".", "_")


def build_de_bloc(corps):
    """Rend le `build:` d'un bloc de service, sous la forme d'un dictionnaire.

    ═════════════════════════════════════════════════════════════════════════
    POURQUOI PAS PyYAML.

    Ce script LISAIT le compose source avec PyYAML pour cette seule
    information. Sur une machine sans PyYAML — la situation par défaut d'un
    macOS neuf — il s'arrêtait sur un `ModuleNotFoundError` au milieu de la
    barrière de déploiement, alors que tout le reste de son travail est
    strictement ligne à ligne et n'a jamais eu besoin d'une bibliothèque.

    Les autres contrôles du dépôt s'AUTORISENT à s'ignorer quand PyYAML manque.
    Un générateur ne le peut pas : son produit est le compose de production. Il
    doit donc marcher partout, avec la bibliothèque standard seule.

    CE QUE CETTE FONCTION NE COUVRE PAS : la forme courte `build: ./chemin`,
    qui rendait déjà `None` avec PyYAML (`isinstance(build, dict)` était faux),
    et les ancres YAML à l'intérieur d'un `build:`. Le compose source n'en
    utilise pas ; si cela changeait, la clé privée du service concerné
    manquerait — et le contrôle des clés, plus bas, le dirait.
    ═════════════════════════════════════════════════════════════════════════
    """
    for i, ligne in enumerate(corps):
        if ligne.rstrip() != "    build:":
            continue
        champs = {}
        for suite in corps[i + 1:]:
            if not suite.strip():
                continue
            indentation = len(suite) - len(suite.lstrip())
            if indentation <= 4:
                break
            paire = re.match(r"\s*([a-z_]+)\s*:\s*(.*?)\s*$", suite)
            if paire:
                champs[paire.group(1)] = paire.group(2).strip("\"'")
        return champs or None
    return None


AJOUTS_ENVIRONNEMENT = {
    "payment-service": [
        ("PAYMENTS__FEDAPAY__APIKEY",
         "${PAYMENTS__FEDAPAY__APIKEY:?la cle FedaPay sk_live_... est obligatoire}"),
        ("PAYMENTS__FEDAPAY__WEBHOOKSECRET",
         "${PAYMENTS__FEDAPAY__WEBHOOKSECRET:?sans lui les notifications FedaPay sont rejetees}"),
        ("PAYMENTS__FEDAPAY__BASEURL", "https://api.fedapay.com/v1"),
        ("PAYMENTS__FEDAPAY__ENABLEPAYOUTS", '"true"'),
        ("PAYMENTS__FEDAPAY__CURRENCY", "XOF"),
        ("PAYMENTS__FEDAPAY__CALLBACKURL",
         "https://api.hba-express.com/api/payments/webhooks/fedapay"),
    ],
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
# ET LA PASSERELLE NE FAIT PLUS EXCEPTION.
#
# CE QUI ETAIT CASSE : elle publiait `8080:8080`, pour qu'un proxy TLS pose a la
# main puisse l'interroger. Sous Coolify, ce proxy existe deja et vit sur la
# meme machine — le port 8080 y est donc pris, et le demarrage echoue sur
#
#     Bind for 0.0.0.0:8080 failed: port is already allocated
#
# Le publier serait de toute facon une faute : cela exposerait l'API en CLAIR
# sur Internet, a cote du HTTPS servi par le proxy. Le proxy joint la passerelle
# par le reseau Docker, ou aucune publication n'est necessaire.
#
# `expose:` remplace `ports:` : rien n'est publie sur l'hote, mais le port reste
# DECLARE — c'est ce qui permet au proxy de savoir ou router.
#
# CE QUE CELA NE COUVRE PAS : sans domaine attribue a `gateway` dans Coolify,
# l'API n'est joignable de nulle part. Le compose seul ne suffit plus.
PORTS_AUTORISES = set()

# Services dont le port doit rester DECLARE pour que le proxy le trouve.
PORTS_EXPOSES = {"gateway": "8080"}

# ═══════════════════════════════════════════════════════════════════════════════
# LES CONSOLES : PUBLIEES SUR LA BOUCLE LOCALE, ET NULLE PART AILLEURS.
#
# `ports: ["9001:9001"]` ecoute sur 0.0.0.0 — donc sur Internet. Une console
# MinIO ouverte donne les pieces KYB des vendeurs ; Kafka UI n'a AUCUNE
# authentification, et qui l'atteint lit et ecrit tous les sujets.
#
# `127.0.0.1:9001:9001` ecoute sur la boucle locale du VPS. Aucun paquet venu du
# reseau ne l'atteint — pas parce qu'un pare-feu le filtre, mais parce que la
# socket n'est pas exposee. C'est une propriete du noyau, pas une regle qu'on
# peut oublier de charger.
#
# On y accede par un tunnel SSH :
#
#     ssh -L 9001:127.0.0.1:9001 ovh-server
#
# L'authentification devient celle de SSH : une cle sur le VPS. C'est plus fort
# que tout mot de passe qu'on poserait devant ces consoles.
#
# CE QUE CELA NE COUVRE PAS : qui a un acces SSH au VPS a ces consoles, donc les
# pieces KYB et les sujets Kafka. Le peripetre est celui des cles autorisees.
# ═══════════════════════════════════════════════════════════════════════════════
PORTS_LOOPBACK = {
    "kafka-ui": [("8090", "8080")],   # console Kafka
    "minio": [("9001", "9001")],      # console MinIO SEULE — l'API S3 (9000) reste interne
}

# Images sans `build:` qui sont legitimes : ce ne sont pas nos services.
IMAGES_TIERCES = ("redis", "confluentinc", "minio", "danielgatis", "provectuslabs",
                  "traefik")

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

    # Le registre des cles PUBLIQUES est le MEME pour tous : l'ancre est
    # exactement l'endroit pour ce genre de valeur. Les cles PRIVEES, elles,
    # different par hote et se posent service par service.
    corps.append("  INTERNAL__PUBLICKEYS: "
                 "${INTERNAL_PUBLIC_KEYS:?le registre des cles publiques gRPC "
                 "est obligatoire — scripts/generer-identites-internes.sh}\n")

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

# service compose -> nom de variable de sa cle privee. Rempli par `main` depuis
# les Dockerfile ; vide tant que rien n'a ete lu, ce qui rend `transformer`
# utilisable seul dans un test.
CLES_INTERNES = {}


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
            # LE NOM DE L'IMAGE VIENT DE `NOMS_IMAGES` QUAND IL DIFFERE.
            # Voir l'encadre de cette table : `gateway` se publie `api-gateway`.
            image = NOMS_IMAGES.get(nom, nom)
            sortie.append("    image: ghcr.io/%s/%s:${HBA_TAG:?le tag d'image est obligatoire}\n"
                          % (PROPRIETAIRE, image))
            i += 1
            bloc_build = []
            while i < len(corps) and re.match(r"^      ", corps[i]):
                bloc_build.append(corps[i])
                i += 1

            # ═══════════════════════════════════════════════════════════════
            # `build:` NE SURVIT QU'AUX SERVICES QUE LA CI NE PUBLIE PAS.
            #
            # Le garder partout faisait CONSTRUIRE sur le VPS des images deja
            # publiees et signees : une heure de CPU, et un binaire qui n'est
            # pas celui qu'on a verifie. Compose, quand `build:` est present et
            # l'image absente localement, construit au lieu de tirer — donc le
            # registre n'aurait jamais servi.
            #
            # CE QUE CE CHOIX NE COUVRE PAS : sans `build:`, un service dont
            # l'image manque au registre echoue au `pull`, franchement. C'est
            # voulu — mieux vaut ce refus qu'une construction silencieuse.
            # ═══════════════════════════════════════════════════════════════
            if nom in CONSTRUITS_SUR_PLACE:
                sortie.append(l)
                sortie.extend(bloc_build)
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
        # LES AJOUTS SE POSENT EN TETE DU BLOC, PAS EN QUEUE.
        #
        # Ecrits juste apres `environment:`, ils se lisent avant les dizaines de
        # variables heritees du developpement — et un `<<: *prod-auth` place la
        # ne les ecraserait pas, la fusion perdant contre les cles explicites.
        if re.match(r"^    environment:\s*$", l) and (
                nom in AJOUTS_ENVIRONNEMENT or nom in CLES_INTERNES):
            sortie.append(l)
            sortie.append("      # Ajoutees pour la production : le compose de "
                          "developpement ne les porte pas.\n")
            if nom in CLES_INTERNES:
                sortie.append(
                    "      INTERNAL__PRIVATEKEY: ${%s:?identite gRPC de %s absente "
                    "— scripts/generer-identites-internes.sh}\n"
                    % (CLES_INTERNES[nom], nom))
            for cle, valeur in AJOUTS_ENVIRONNEMENT.get(nom, []):
                sortie.append("      %s: %s\n" % (cle, valeur))
            i += 1
            continue

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
            if nom not in PORTS_LOOPBACK:
                sortie.append("    # `ports:` retiré : publier sur le VPS, c'est publier sur Internet.\n")
                sortie.append("    # Les services se joignent par le réseau `hba-backend`.\n")
            if nom in PORTS_LOOPBACK:
                sortie.append("    # Console : publiee sur la BOUCLE LOCALE, jamais sur 0.0.0.0.\n")
                sortie.append("    # Acces par tunnel : ssh -L <port>:127.0.0.1:<port> <hote>\n")
                sortie.append("    ports:\n")
                for hote, conteneur in PORTS_LOOPBACK[nom]:
                    sortie.append('      - "127.0.0.1:%s:%s"\n' % (hote, conteneur))
            if nom in PORTS_EXPOSES:
                sortie.append("    # `expose:` ne publie rien — il DECLARE le port, pour que le\n")
                sortie.append("    # proxy de Coolify sache ou router le domaine.\n")
                sortie.append("    expose: [\"%s\"]\n" % PORTS_EXPOSES[nom])
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


# ══════════════════════════════════════════════════════════════════════════════
# TRAEFIK — LE SEUL CONTENEUR QUI TOUCHE INTERNET.
#
# POURQUOI IL EST ENGENDRE ICI, ET PAS ECRIT A LA MAIN DANS LE COMPOSE.
#
# `docker-compose.prod.yml` porte « NE PAS EDITER A LA MAIN » : tout ajout
# manuel disparait a la prochaine regeneration. Traefik vit donc dans le
# generateur, comme l'ancre de production et les identites internes.
#
# `exposedByDefault=false` EST LA LIGNE LA PLUS IMPORTANTE DE CE BLOC.
#
# Le fournisseur Docker de Traefik publie, PAR DEFAUT, TOUT conteneur qu'il
# voit. Sans ce reglage, les vingt-quatre services — Kafka, MinIO, Redis, les
# vingt hotes .NET — deviendraient joignables depuis Internet sous des noms
# engendres, sans authentification et sans qu'aucune erreur ne le signale. Seule
# la passerelle porte `traefik.enable=true`.
#
# LE SOCKET DOCKER EST MONTE EN LECTURE SEULE, ET CE N'EST PAS ANODIN.
#
# Meme en lecture, l'API Docker rend les variables d'environnement de TOUS les
# conteneurs — donc les mots de passe de base, la cle FedaPay et les cles de
# signature. Un Traefik compromis les lit. C'est le compromis assume de la
# decouverte par etiquettes ; l'alternative (fichier statique) coute la
# decouverte automatique. A savoir, et a ne pas oublier.
#
# `--api=false` : PAS DE TABLEAU DE BORD. Celui de Traefik expose la
# configuration complete du routage. La regle de cette plateforme est
# constante : aucune console d'administration derriere l'entree publique.
#
# CE QUE CE BLOC NE COUVRE PAS :
#   - il ne limite ni le debit ni la taille des requetes. nginx-ingress posait
#     `proxy-body-size: 20m` pour les pieces KYB ; Traefik n'a pas de limite par
#     defaut, donc rien a regler — mais rien ne protege non plus d'un envoi
#     massif ;
#   - il ne fait aucune terminaison mTLS ni aucune authentification : la
#     passerelle reste seule juge des jetons ;
#   - le certificat est demande a Let's Encrypt par defi HTTP-01. Il exige que
#     le DNS pointe deja la machine ET que le port 80 reponde depuis Internet.
# ══════════════════════════════════════════════════════════════════════════════

SERVICE_PUBLIC = "gateway"
PORT_PUBLIC = "8080"
DOMAINE_PUBLIC = "${HBA_DOMAINE:?le domaine public est obligatoire}"
COURRIEL_ACME = "${HBA_ACME_EMAIL:?l'adresse pour Let's Encrypt est obligatoire}"
VERSION_TRAEFIK = "traefik:v3.3"


def etiquettes_traefik():
    """Les etiquettes de routage, posees sur le service public."""
    return [
        "    # Routage public — lu par Traefik. Ce service est le SEUL a porter\n",
        "    # `traefik.enable` : voir l'encadre du bloc traefik plus bas.\n",
        "    labels:\n",
        '      traefik.enable: "true"\n',
        "      traefik.docker.network: hba-backend\n",
        "      traefik.http.routers.hba.rule: Host(`%s`)\n" % DOMAINE_PUBLIC,
        "      traefik.http.routers.hba.entrypoints: websecure\n",
        "      traefik.http.routers.hba.tls.certresolver: lets\n",
        '      traefik.http.services.hba.loadbalancer.server.port: "%s"\n' % PORT_PUBLIC,
    ]


def traefik_service():
    """Le bloc de service Traefik, ajoute au rendu."""
    return [
        "\n  traefik:\n",
        "    container_name: hba-traefik\n",
        "    image: %s\n" % VERSION_TRAEFIK,
        "    restart: unless-stopped\n",
        "    command:\n",
        "      # Decouverte par etiquettes, et RIEN par defaut.\n",
        "      - --providers.docker=true\n",
        "      - --providers.docker.exposedByDefault=false\n",
        "      - --providers.docker.network=hba-backend\n",
        "      # 80 redirige vers 443 : aucun trafic applicatif en clair.\n",
        "      - --entryPoints.web.address=:80\n",
        "      - --entryPoints.websecure.address=:443\n",
        "      - --entryPoints.web.http.redirections.entryPoint.to=websecure\n",
        "      - --entryPoints.web.http.redirections.entryPoint.scheme=https\n",
        "      # Let's Encrypt, defi HTTP-01 sur l'entree 80 laissee ouverte\n",
        "      # pour ca — la redirection ci-dessus epargne `/.well-known/`.\n",
        "      - --certificatesResolvers.lets.acme.email=%s\n" % COURRIEL_ACME,
        "      - --certificatesResolvers.lets.acme.storage=/acme/acme.json\n",
        "      - --certificatesResolvers.lets.acme.httpChallenge.entryPoint=web\n",
        "      # Aucun tableau de bord : il exposerait tout le routage.\n",
        "      - --api=false\n",
        "      - --log.level=INFO\n",
        "      - --accesslog=true\n",
        "    ports:\n",
        '      - "80:80"\n',
        '      - "443:443"\n',
        "    volumes:\n",
        "      # Lecture seule — mais l'API Docker rend quand meme les variables\n",
        "      # d'environnement de tous les conteneurs. Voir l'encadre.\n",
        "      - /var/run/docker.sock:/var/run/docker.sock:ro\n",
        "      # Les certificats survivent aux redemarrages. Sans ce volume,\n",
        "      # chaque `up` redemanderait un certificat, et Let's Encrypt\n",
        "      # limite a cinq echecs par heure et par domaine.\n",
        "      - traefik-acme:/acme\n",
        "    networks:\n",
        "      - hba-backend\n",
    ]


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
    # LES CLES PRIVEES, DERIVEES DES DOCKERFILE.
    for d in debuts:
        nom_service, corps_service = bloc_de_service(lignes, d, fin_services)
        projet = projet_de_service(build_de_bloc(corps_service))
        if projet:
            CLES_INTERNES[nom_service] = variable_de_cle(projet)

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

        # Le routage public est POSE SUR LA PASSERELLE, pas sur Traefik.
        # Traefik decouvre ses routes par les etiquettes des conteneurs : c'est
        # ce qui permet a `traefik_service()` de ne rien savoir de la passerelle.
        if nom == SERVICE_PUBLIC:
            corps_total.extend(etiquettes_traefik())

    corps_total.extend(traefik_service())

    queue = [
        "\nvolumes:\n",
        "  kafka-data:\n",
        "  minio-data:\n",
        "  rembg-models:\n",
        "  traefik-acme:\n",
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

    # ═══════════════════════════════════════════════════════════════════════
    # LA LISTE DES IMAGES VIENT DESORMAIS DE `tools/HBA.Controls`.
    #
    # `scripts/ci-affected.py` a ete porte en C# et supprime. La source reste la
    # MEME que celle de la matrice de construction de la CI — c'est tout
    # l'interet : un nom d'image qui diverge entre le compose et la CI produit
    # un `pull` d'une image qui n'a jamais ete publiee.
    #
    # LE REFUS EST CONSERVE TEL QUEL. Si l'outil ne repond pas — SDK absent,
    # compilation cassee — ce generateur s'ARRETE. Il ne poursuit pas sans le
    # controle des noms : un compose engendre sans cette verification a l'air
    # complet et designe des images absentes du registre.
    # ═══════════════════════════════════════════════════════════════════════
    try:
        publiables = {e["service"] for e in json.loads(subprocess.check_output(
            ["dotnet", "run", "--project", os.path.join("tools", "HBA.Controls"),
             "--verbosity", "quiet", "--", "images-affectees", "--tous"],
            cwd=RACINE, text=True))}
    except (subprocess.SubprocessError, ValueError, OSError) as e:
        print("REFUS : impossible de lire la liste des images depuis "
              "tools/HBA.Controls (%s) — le controle des noms d'images ne peut "
              "pas s'executer." % type(e).__name__, file=sys.stderr)
        return 1

    # ═══════════════════════════════════════════════════════════════════════
    # CE CONTROLE A ETE RETOURNE LE 1er SEPTEMBRE 2026 — SA PREMISSE A CHANGE.
    #
    # Il exigeait qu'un service applicatif porte un `build:`, au motif qu'« une
    # image sans build est une image que personne ne peut produire ». C'etait
    # vrai TANT QU'IL N'Y AVAIT PAS DE REGISTRE. Depuis que la CI publie les
    # vingt-et-une images sur ghcr, l'inverse est vrai : garder `build:` fait
    # CONSTRUIRE sur le VPS une image deja publiee et signee — une heure de CPU,
    # et un binaire qui n'est pas celui qu'on a verifie.
    #
    # LA NOUVELLE REGLE : un service applicatif doit avoir SOIT une image
    # publiable par la CI, SOIT un `build:`. Les deux ensemble sont tolerees
    # pour `CONSTRUITS_SUR_PLACE` (rembg), qui n'est publie nulle part.
    #
    # Ce qui reste interdit, et c'est le vrai danger : une image que personne ne
    # publie ET qu'aucun `build:` ne produit. Elle echouerait au `pull`, en
    # production, sur un « denied » qui se lit comme un probleme de droits.
    # ═══════════════════════════════════════════════════════════════════════
    import yaml as _yaml
    try:
        rendu_charge = _yaml.safe_load(rendu)
    except Exception as e:
        print("REFUS : le rendu n'est pas du YAML valide (%s)" % e, file=sys.stderr)
        return 1

    orphelins = []
    for n, v in (rendu_charge.get("services") or {}).items():
        image = str(v.get("image", ""))
        if not image or image.startswith(IMAGES_TIERCES):
            continue
        if "build" in v:
            continue
        nom_image = image.split("/")[-1].split(":")[0]
        if nom_image not in publiables:
            orphelins.append(n)

    sans_build = orphelins
    if sans_build:
        print("REFUS : %s portent une image que la CI ne publie pas, et aucun "
              "`build:` ne pourrait la produire." % ", ".join(sans_build),
              file=sys.stderr)
        print("    Ajouter la traduction a NOMS_IMAGES, ou le service a "
              "CONSTRUITS_SUR_PLACE.", file=sys.stderr)
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

    # ═══════════════════════════════════════════════════════════════════════
    # CHAQUE IMAGE DU REGISTRE DOIT EXISTER DANS CE QUE LA CI PUBLIE.
    #
    # LE DEFAUT QUE CE CONTROLE FERME : le rendu reclamait
    # `ghcr.io/hectorberi01/gateway`, publie en realite sous `api-gateway`.
    # Personne ne l'a vu parce que `build:` etait conserve : Compose construisait
    # l'image au lieu de la tirer, et le nom du registre ne servait a rien.
    # Le jour ou l'on tire — c'est-a-dire le jour du deploiement — il devient
    # « denied », qui se lit comme un jeton sans droits.
    #
    # La reference est `images-affectees --tous` : la meme source que la matrice
    # de construction de la CI. Deux inventaires,
    # une seule verite.
    #
    # CE QUE CE CONTROLE NE COUVRE PAS : il verifie que l'image est PUBLIABLE,
    # pas qu'elle a ete PUBLIEE sous le tag demande. Seul le registre le sait.
    # ═══════════════════════════════════════════════════════════════════════
    # Les services construits sur place portent un nom d'image du registre par
    # commodite — `docker images` dit alors quel commit tourne — mais rien ne
    # les y publie. Ils sont donc exemptes de ce controle, et couverts par le
    # precedent, qui exige leur `build:`.
    exemptes = {NOMS_IMAGES.get(n, n) for n in CONSTRUITS_SUR_PLACE}

    prefixe = "    image: ghcr.io/%s/" % PROPRIETAIRE
    inconnues = []
    for ligne in rendu.splitlines():
        if ligne.startswith(prefixe):
            image = ligne[len(prefixe):].split(":", 1)[0]
            if image not in publiables and image not in exemptes:
                inconnues.append(image)

    if inconnues:
        print("REFUS : %d image(s) du rendu ne sont publiees par aucune Dockerfile "
              "connue de images-affectees :" % len(inconnues), file=sys.stderr)
        for i in sorted(set(inconnues)):
            print("    %s" % i, file=sys.stderr)
        print("    Ajouter la traduction a NOMS_IMAGES, ou le service a "
              "CONSTRUITS_SUR_PLACE.", file=sys.stderr)
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
