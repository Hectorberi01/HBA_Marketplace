#!/usr/bin/env python3
"""
═══════════════════════════════════════════════════════════════════════════════
DÉPART À FROID : CHAQUE MIGRATION TIENT-ELLE SUR UNE BASE VIDE ?

POURQUOI CE CONTRÔLE EXISTE.

Une migration écrite à la main l'est presque toujours en REGARDANT une base
existante. On y voit la colonne, on la renomme, la migration passe. Elle ne
passera plus jamais ailleurs si la colonne n'existe qu'à cet endroit-là.

C'est ce qui s'est produit sur food-service : `20260814000000_
RepriseImagesVersMedia` renommait `restaurants.LogoUrl` en `LegacyLogoUrl`
alors que `20260812121955_ImagesVersMedia` l'avait DÉJÀ fait deux jours plus
tôt. Sur la base de développement de l'époque, la première n'avait pas encore
tourné. Sur une base neuve, la seconde tombe sur

    42703: column "LogoUrl" does not exist

et le service ne démarre pas — les migrations sont appliquées AVANT l'ouverture
du port, délibérément.

CE QUE LE COMPILATEUR NE VOIT PAS.

Une migration est du C# valide quoi qu'elle raconte : `RenameColumn("Inexistant")`
compile. L'erreur n'apparaît qu'à l'exécution, sur une base dans le bon état —
c'est-à-dire tard, et sur la machine de quelqu'un d'autre.

Ce script rejoue donc les migrations À SEC, dans l'ordre, en tenant la liste
des colonnes existantes. Il ne remplace pas un vrai départ à froid ; il attrape
la classe d'erreurs qui ne se manifeste QUE là.

CE QU'IL NE VOIT PAS NON PLUS.

Le `migrationBuilder.Sql(...)` brut n'est pas ANALYSÉ — comprendre du SQL
arbitraire dépasse ce que ce script prétend faire. Les migrations de reprise de
données passent souvent par là et restent à vérifier à la main. Il en lit
cependant les identifiants entre guillemets, pour la seule question de la CASSE
(voir `check_sql_identifier_case`).

TROIS CHOSES ONT ÉTÉ AJOUTÉES AU LOT 9.4, ET LA PREMIÈRE EST UN AVEU.

  • `check_sql_identifier_case` ne voyait que 19 blocs SQL sur 232 : il ne
    connaissait que la forme à triple guillemet, alors que le dépôt écrit son
    SQL à 146 exemplaires en littéral VERBATIM. Il passait donc à côté de 94 %
    de ce qu'il prétendait lire — un contrôle qui rassure sans rien vérifier,
    exactement ce que son propre encadré dénonce plus bas.

  • `check_snapshot_entities` compare le ModelSnapshot au CODE. C'est ce qui
    manquait quand `DeliveriesDbContextModelSnapshot` a gardé trois agrégats
    dont le namespace n'existait plus : le prochain `migrations add` aurait
    généré, tout seul, une migration supprimant quatre tables.

  • Le rejeu à froid, lui, ne change pas : il compare les migrations ENTRE
    ELLES. Ni l'un ni l'autre ne lit la configuration EF — pour cela il
    faudrait démarrer l'application.

Un dossier `Migrations` = un DbContext = un historique. Les services à plusieurs
contextes (financial, engagement, communication) sont donc traités séparément,
ce qui évite de confondre trois tables `outbox_messages` distinctes.

Usage :
    python3 scripts/check-migrations.py          # tous les services
    python3 scripts/check-migrations.py food-service

Sort 1 s'il trouve quelque chose : utilisable en CI.
═══════════════════════════════════════════════════════════════════════════════
"""

import collections
import os
import re
import sys

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'services')
ROOT = os.path.normpath(ROOT)


# Voir l'encadré de `check-di.py` : « src/services » n'existe plus, et les
# services sont rangés par univers. Sans cette énumération à deux niveaux, ce
# script levait un FileNotFoundError au lieu de vérifier quoi que ce soit.
def services_relatifs():
    """Chemins relatifs des services, sous la forme « univers/nom-du-service »."""
    trouves = []
    for univers in sorted(os.listdir(ROOT)):
        dossier = os.path.join(ROOT, univers)
        if not os.path.isdir(dossier):
            continue
        for nom in sorted(os.listdir(dossier)):
            if os.path.isdir(os.path.join(dossier, nom)):
                trouves.append(os.path.join(univers, nom))
    return trouves


def demande(service, wanted):
    """L'argument de ligne de commande reste le nom court."""
    return not wanted or service in wanted or os.path.basename(service) in wanted

CALL = re.compile(
    r'migrationBuilder\.(CreateTable|AddColumn|RenameColumn|DropColumn'
    r'|AlterColumn|DropTable|RenameTable)\b')


def call_args(src, start):
    """Texte entre les parenthèses de l'appel commençant à `start`.

    Un simple regex ne suffit pas : `CreateTable` contient des parenthèses
    imbriquées sur plusieurs dizaines de lignes.
    """
    i = src.index('(', start)
    depth = 0
    for j in range(i, len(src)):
        if src[j] == '(':
            depth += 1
        elif src[j] == ')':
            depth -= 1
            if depth == 0:
                return src[i + 1:j], j
    return src[i + 1:], len(src)


def named(text, key):
    m = re.search(r'\b%s:\s*"([^"]*)"' % key, text)
    return m.group(1) if m else None


def check_context(folder, files):
    """Rejoue un historique de migrations. Rend la liste des problèmes."""
    problems = []
    columns = collections.defaultdict(set)
    tables = set()

    # Le tri lexicographique EST l'ordre d'application : le préfixe des
    # migrations EF est un horodatage à largeur fixe.
    for name in sorted(files):
        with open(os.path.join(folder, name), encoding='utf-8') as handle:
            src = handle.read()

        # Seul `Up()` nous intéresse. `Down()` s'exécute dans un état différent
        # et n'est de toute façon jamais joué au démarrage.
        start = src.find('void Up(')
        stop = src.find('void Down(')
        if start < 0:
            continue
        up = src[start:stop] if stop > start else src[start:]

        pos = 0
        while True:
            match = CALL.search(up, pos)
            if not match:
                break
            kind = match.group(1)
            text, pos = call_args(up, match.start())

            if kind == 'CreateTable':
                table = named(text, 'name')
                if table:
                    tables.add(table)
                    for column in re.findall(r'(\w+)\s*=\s*table\.Column<', text):
                        columns[table].add(column)

            elif kind == 'AddColumn':
                table, column = named(text, 'table'), named(text, 'name')
                if table and column:
                    if table in tables and column in columns[table]:
                        problems.append(
                            (name, 'AddColumn %s."%s" — colonne déjà présente'
                             % (table, column)))
                    columns[table].add(column)

            elif kind == 'RenameColumn':
                table = named(text, 'table')
                column, new = named(text, 'name'), named(text, 'newName')
                if table and column:
                    # On ne signale que les tables créées par CES migrations.
                    #
                    # Une table héritée d'un module extrait n'a pas son
                    # `CreateTable` ici : la juger produirait un faux positif à
                    # chaque colonne, et le bruit tuerait le contrôle.
                    if table in tables and column not in columns[table]:
                        problems.append(
                            (name, 'RenameColumn %s."%s" — colonne absente à ce stade'
                             % (table, column)))
                    columns[table].discard(column)
                    if new:
                        columns[table].add(new)

            elif kind in ('DropColumn', 'AlterColumn'):
                table, column = named(text, 'table'), named(text, 'name')
                if table and column:
                    if table in tables and column not in columns[table]:
                        problems.append(
                            (name, '%s %s."%s" — colonne absente à ce stade'
                             % (kind, table, column)))
                    if kind == 'DropColumn':
                        columns[table].discard(column)

            elif kind == 'RenameTable':
                table, new = named(text, 'name'), named(text, 'newName')
                if table and new and table in tables:
                    tables.discard(table)
                    tables.add(new)
                    columns[new] = columns.pop(table, set())

            elif kind == 'DropTable':
                table = named(text, 'name')
                if table:
                    tables.discard(table)
                    columns.pop(table, None)

    return problems


# UNE CONFIGURATION PARTAGÉE CRÉE UNE TABLE TOUT AUSSI RÉELLE.
#
# `ConsumerInboxConfiguration` et `IdempotencyConfiguration` vivent dans
# `shared/common/HBA.Shared.Infrastructure`, et quatre DbContext les appliquent —
# identity, user, notifications, payments. Chacun a donc besoin des tables
# `consumer_inbox` et `idempotency_keys` dans SON schéma.
#
# Ce contrôle ne regardait que `services/`. Les huit tables manquantes étaient
# donc invisibles, alors que leur absence est exactement la panne qu'il existe
# pour attraper : le service démarre, le premier événement consommé lève
# « relation "consumer_inbox" does not exist », et le message part en boucle de
# rejeu.
#
# On n'impute une configuration partagée qu'aux services qui l'appliquent
# vraiment : `ApplyConfiguration(new XxxConfiguration())` dans leur DbContext.
SHARED_ROOT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'shared'))
APPLY_CONFIG = re.compile(r'ApplyConfiguration\(\s*new\s+(\w+Configuration)\s*\(')


def tables_des_configurations_partagees():
    """`{ NomDeClasseConfiguration: (table, fichier) }` pour tout `shared/`."""
    trouvees = {}
    for folder, _, names in os.walk(SHARED_ROOT):
        if '/obj/' in folder or '/bin/' in folder:
            continue
        for name in names:
            if not name.endswith('Configuration.cs'):
                continue
            with open(os.path.join(folder, name), encoding='utf-8', errors='ignore') as handle:
                content = handle.read()
            tables = re.findall(r'\.ToTable\(\s*"([a-z_0-9]+)"', content)
            if tables:
                trouvees[name[:-3]] = (tables[0], name)
    return trouvees


PARTAGEES = tables_des_configurations_partagees()


def configured_tables(service):
    """Tables déclarées par une configuration EF : `ToTable("nom")`."""
    tables = {}
    for folder, _, names in os.walk(os.path.join(ROOT, service)):
        if '/obj/' in folder or '/bin/' in folder:
            continue
        for name in names:
            if not name.endswith('.cs') or 'Migrations' in folder:
                continue
            with open(os.path.join(folder, name), encoding='utf-8', errors='ignore') as handle:
                content = handle.read()
            for table in re.findall(r'\.ToTable\(\s*"([a-z_0-9]+)"', content):
                tables.setdefault(table, name)

            # Les configurations que ce service applique sans les héberger.
            for classe in APPLY_CONFIG.findall(content):
                if classe in PARTAGEES:
                    table, fichier = PARTAGEES[classe]
                    tables.setdefault(table, '%s (partagée, appliquée par %s)' % (fichier, name))
    return tables


def created_tables(service):
    """Tables créées par une migration : `CreateTable(name: "nom"`."""
    tables = set()
    for folder, _, names in os.walk(os.path.join(ROOT, service)):
        if not folder.endswith('Migrations'):
            continue
        for name in names:
            if not name.endswith('.cs') or 'Designer' in name or 'Snapshot' in name:
                continue
            with open(os.path.join(folder, name), encoding='utf-8', errors='ignore') as handle:
                content = handle.read()
            tables.update(re.findall(r'CreateTable\(\s*name:\s*"([a-z_0-9]+)"', content))
            # Une table peut aussi apparaître par renommage.
            tables.update(re.findall(r'RenameTable\([^)]*newName:\s*"([a-z_0-9]+)"', content))
    return tables


def declared_columns(service):
    """Toutes les colonnes créées par les migrations d'un service."""
    columns = set()
    for folder, _, names in os.walk(os.path.join(ROOT, service)):
        if not folder.endswith('Migrations'):
            continue
        for name in names:
            if not name.endswith('.cs') or 'Designer' in name or 'Snapshot' in name:
                continue
            with open(os.path.join(folder, name), encoding='utf-8', errors='ignore') as handle:
                content = handle.read()
            columns.update(re.findall(r'(\w+)\s*=\s*table\.Column<', content))
            for call in re.finditer(r'(AddColumn<[^>]*>|RenameColumn)\(([^;]*?)\)\s*;', content, re.S):
                for key in ('name', 'newName'):
                    found = re.search(r'\b%s:\s*"([^"]+)"' % key, call.group(2))
                    if found:
                        columns.add(found.group(1))
    return columns


def blocs_sql(contenu):
    """
    Le corps de chaque `migrationBuilder.Sql(...)`, quelle que soit la forme du
    litteral.

    ATTENTION : LE CONTROLE NE VOYAIT QUE 19 BLOCS SUR 232.

    Il cherchait uniquement la forme brute a triple guillemet. Or le depot ecrit
    son SQL a 146 exemplaires en litteral VERBATIM (arobase) et a 11 en litteral
    ordinaire. Le controle passait donc a cote de 94 % de ce qu'il pretend lire —
    et l'audit le decrivait, a peine trop durement, comme « totalement mort ».

    ATTENTION : LES TROIS FORMES N'ECHAPPENT PAS LES GUILLEMETS PAREIL, et c'est
    precisement ce qui compte ici puisqu'on cherche des IDENTIFIANTS ENTRE
    GUILLEMETS :

      - brut      : rien a desechapper, un identifiant s'y ecrit tel quel ;
      - verbatim  : un guillemet s'y DOUBLE ;
      - ordinaire : un guillemet s'y ECHAPPE par une contre-oblique.

    Rendre les trois sous la meme forme est la seule facon d'appliquer ensuite un
    seul motif de recherche.
    """
    resultats = []
    i = 0
    marqueur = "migrationBuilder.Sql("
    TRIPLE = chr(34) * 3
    GUILLEMET = chr(34)
    OBLIQUE = chr(92)

    while True:
        i = contenu.find(marqueur, i)
        if i == -1:
            return resultats

        j = i + len(marqueur)
        while j < len(contenu) and contenu[j] in " \t\r\n":
            j += 1

        if contenu.startswith(TRIPLE, j):
            fin = contenu.find(TRIPLE, j + 3)
            if fin == -1:
                i = j
                continue
            resultats.append(contenu[j + 3:fin])
            i = fin + 3

        elif contenu.startswith("@" + GUILLEMET, j):
            k = j + 2
            while k < len(contenu):
                if contenu[k] == GUILLEMET:
                    if contenu.startswith(GUILLEMET * 2, k):
                        k += 2
                        continue
                    break
                k += 1
            resultats.append(contenu[j + 2:k].replace(GUILLEMET * 2, GUILLEMET))
            i = k + 1

        elif contenu.startswith(GUILLEMET, j):
            k = j + 1
            while k < len(contenu):
                if contenu[k] == OBLIQUE:
                    k += 2
                    continue
                if contenu[k] == GUILLEMET:
                    break
                k += 1
            resultats.append(contenu[j + 1:k].replace(OBLIQUE + GUILLEMET, GUILLEMET))
            i = k + 1

        else:
            i = j


def check_sql_identifier_case(service):
    """
    POSTGRESQL DISTINGUE LA CASSE DÈS QU'ON MET DES GUILLEMETS.

    `se."Metadata"` et `se."metadata"` sont deux colonnes différentes. Une
    migration de reprise écrite à la main a désigné la première alors que la
    configuration EF mappe la seconde (`HasColumnName("metadata")`) — et
    PostgreSQL l'a dit lui-même, à l'exécution, sur une base neuve :

        42703: column se.Metadata does not exist
        Hint: Perhaps you meant to reference the column "se.metadata".

    On ne prétend pas analyser du SQL. On cherche UNIQUEMENT les identifiants
    entre guillemets qui ne correspondent à aucune colonne connue mais qui en
    égalent une À LA CASSE PRÈS. Un identifiant totalement inconnu est ignoré :
    il vient d'un autre schéma, d'un alias ou d'une expression, et le signaler
    noierait le vrai constat.
    """
    columns = declared_columns(service)
    lowered = {column.lower(): column for column in columns}
    problems = []

    for folder, _, names in os.walk(os.path.join(ROOT, service)):
        if not folder.endswith('Migrations'):
            continue
        for name in sorted(names):
            if not name.endswith('.cs') or 'Designer' in name or 'Snapshot' in name:
                continue
            with open(os.path.join(folder, name), encoding='utf-8', errors='ignore') as handle:
                content = handle.read()

            for block in blocs_sql(content):
                for identifier in set(re.findall(r'"([A-Za-z_][A-Za-z_0-9]*)"', block)):
                    if identifier in columns:
                        continue
                    expected = lowered.get(identifier.lower())
                    if expected:
                        problems.append(
                            (name, '"%s" dans du SQL brut — la colonne s\'appelle "%s"'
                             % (identifier, expected)))

    return problems


_TYPES_DU_DEPOT = None


def types_declares():
    """
    Tous les types du depot, sous la forme « Namespace.Type ».

    Indexe une seule fois : le snapshot de chaque contexte est compare a ce
    meme jeu.
    """
    global _TYPES_DU_DEPOT
    if _TYPES_DU_DEPOT is not None:
        return _TYPES_DU_DEPOT

    espace = re.compile(r'^\s*namespace\s+([\w\.]+)', re.M)
    type_ = re.compile(
        r'^\s*(?:public|internal|private|protected)?\s*'
        r'(?:sealed\s+|abstract\s+|static\s+|partial\s+|readonly\s+|record\s+)*'
        r'(?:class|record|struct|interface)\s+(\w+)', re.M)

    trouves = set()
    racine = os.path.dirname(ROOT)
    for dossier, sous, noms in os.walk(racine):
        sous[:] = [d for d in sous
                   if d not in ('obj', 'bin', 'node_modules', '_to_delete', '.git')]
        for nom in noms:
            # ATTENTION : ON ECARTE LES SNAPSHOTS ET DESIGNERS PAR LEUR SUFFIXE,
            # PAS PAR « Snapshot in nom ».
            #
            # Le premier jet ecartait tout fichier dont le NOM contenait
            # « Snapshot » — donc `PolicySnapshot.cs`, un value object parfaitement
            # vivant. Le controle a immediatement annonce que le snapshot de
            # return-refund declarait un type introuvable : un faux positif
            # fabrique par son propre filtre, sur le premier depot venu.
            if not nom.endswith('.cs'):
                continue
            if nom.endswith('ModelSnapshot.cs') or nom.endswith('.Designer.cs'):
                continue
            chemin = os.path.join(dossier, nom)
            with open(chemin, encoding='utf-8', errors='ignore') as flux:
                contenu = flux.read()
            espaces = espace.findall(contenu)
            if not espaces:
                continue
            for t in type_.findall(contenu):
                for e in espaces:
                    trouves.add('%s.%s' % (e, t))

    _TYPES_DU_DEPOT = trouves
    return trouves


def check_snapshot_entities(service):
    """
    ATTENTION : LE SNAPSHOT PEUT DECRIRE DES TYPES QUI N'EXISTENT PLUS.

    `DeliveriesDbContextModelSnapshot` declarait trois agregats — DeliveryQuote,
    DeliveryZone et PricingRule — sous un namespace INTROUVABLE dans tout le
    depot. Le domaine de tarification avait ete deplace vers
    delivery-pricing-service ; le code etait parti, le snapshot etait reste.

    CE QUE CET ECART COUTE : le prochain `dotnet ef migrations add` sur ce
    contexte genere, tout seul, une migration qui SUPPRIME les tables
    correspondantes — au milieu d'un diff portant sur autre chose, sans que
    personne l'ait demande. C'est une suppression de donnees produite par un
    outil, pas par une decision.

    Aucun autre controle ne le voit : le rejeu a froid compare les migrations
    ENTRE ELLES, jamais au modele.

    CE QU'IL NE VERIFIE PAS : que le snapshot decrive toutes les COLONNES du
    modele, ni leurs types. Il ne compare que l'existence des TYPES. Verifier
    les colonnes demanderait de rejouer la configuration EF — c'est-a-dire de
    demarrer l'application.
    """
    connus = types_declares()
    reference = re.compile(r'(?:modelBuilder\.Entity|b\d*\.OwnsOne|b\d*\.OwnsMany)'
                           r'\(\s*"([\w\.]+)"')
    problemes = []

    for dossier, _, noms in os.walk(os.path.join(ROOT, service)):
        for nom in sorted(noms):
            if not nom.endswith('ModelSnapshot.cs'):
                continue
            with open(os.path.join(dossier, nom), encoding='utf-8', errors='ignore') as flux:
                contenu = flux.read()

            for plein in sorted(set(reference.findall(contenu))):
                # Un type d'entite sans point est un sac de proprietes EF, sans
                # classe CLR : il n'y a rien a chercher dans le code.
                if '.' not in plein or plein in connus:
                    continue
                problemes.append(
                    (nom, 'le snapshot declare « %s », introuvable dans le depot' % plein))

    return problemes


def check_tables(service):
    """
    UNE CONFIGURATION SANS MIGRATION NE SE VOIT QU'EN PRODUCTION.

    `Store`, sa configuration, son dépôt et ses commandes existaient ; aucune
    migration ne créait `sellers.stores`, et l'instantané l'ignorait. Preuve que
    `dotnet ef migrations add` n'avait pas été relancé après la configuration.

    Le code compile, les tests de domaine passent, le service démarre — jusqu'à
    la première requête, ou, ici, jusqu'à une migration de reprise qui
    interrogeait la table absente et désignait le mauvais coupable.
    """
    configured = configured_tables(service)
    created = created_tables(service)
    return [(table, source) for table, source in sorted(configured.items())
            if table not in created]


def services_avec_tables_manquantes():
    """
    ═══════════════════════════════════════════════════════════════════════════
    LA LISTE QUE `db/add-missing-migrations.sh` TENAIT À LA MAIN.

    ET QUI EST DEVENUE FAUSSE AU SERVICE SUIVANT.

    Le script énumérait cinq services en dur. Quand catalog-service a gagné trois
    tables sans migration, il ne les a pas générées — il a simplement REJOUÉ le
    contrôle à la fin, affiché les trois erreurs et rendu 1. On lisait donc
    « ❌ aucune migration ne la crée » juste après avoir lancé la commande censée
    les créer, sans que rien n'explique le paradoxe.

    C'est le défaut que ce dépôt combat partout : une liste écrite à la main à
    côté d'une source de vérité qui, elle, sait déjà.

    Ne remonte QUE la classe « table configurée sans migration ». Les autres
    incohérences — rejeu de contexte, casse des identifiants SQL — ne se
    corrigent pas en générant une migration, et lancer `dotnet ef` dessus
    n'écrirait qu'un fichier vide de plus.
    ═══════════════════════════════════════════════════════════════════════════
    """
    rendu = []
    for service in services_relatifs():
        manquantes = sorted({table for table, _ in check_tables(service)})
        if manquantes:
            rendu.append((service, manquantes))
    return rendu


def main():
    if '--services-en-defaut' in sys.argv:
        for service, tables in services_avec_tables_manquantes():
            print('%s\t%s' % (service, ','.join(tables)))
        return 0

    wanted = [a for a in sys.argv[1:] if not a.startswith('--')]
    total = 0
    contexts = 0

    for service in services_relatifs():
        if not demande(service, wanted):
            continue
        service_root = os.path.join(ROOT, service)

        for folder, _, names in os.walk(service_root):
            if not folder.endswith('Migrations'):
                continue
            files = [n for n in names
                     if n.endswith('.cs')
                     and 'Designer' not in n
                     and 'Snapshot' not in n]
            if not files:
                continue
            contexts += 1

            for name, message in check_context(folder, files):
                total += 1
                print('❌ %s' % service)
                print('     %s' % name)
                print('     %s' % message)

        for table, source in check_tables(service):
            total += 1
            print('❌ %s' % service)
            print('     table « %s » configurée (%s)' % (table, source))
            print('     aucune migration ne la crée')

        for name, message in check_sql_identifier_case(service):
            total += 1
            print('❌ %s' % service)
            print('     %s' % name)
            print('     %s' % message)

        for name, message in check_snapshot_entities(service):
            total += 1
            print('❌ %s' % service)
            print('     %s' % name)
            print('     %s' % message)
            print('     le prochain `migrations add` generera une SUPPRESSION de table')

    print()
    print('%d contexte(s) rejoué(s), %d incohérence(s) de départ à froid.'
          % (contexts, total))
    return 1 if total else 0


if __name__ == '__main__':
    sys.exit(main())
