#!/usr/bin/env python3
"""
═══════════════════════════════════════════════════════════════════════════════
UN CLIENT gRPC DONT L'ADRESSE N'EST DÉCLARÉE NULLE PART.

CE DÉFAUT NE SE TRADUIT PAS PAR UNE PANNE D'APPEL — LE SERVICE NE DÉMARRE PAS.

Chaque `Add<X>GrpcClient` LÈVE à la construction de l'hôte quand sa clé
`Services:<Y>` est absente. C'est le bon sens de l'erreur : un client sans
adresse rendrait sinon une panne au premier appel, sur un chemin d'autorisation,
des heures après le déploiement.

Mais l'échec arrive tard dans la boucle — après une construction d'image de
plusieurs minutes — et sous une forme qui ne désigne PAS le fichier fautif :

    Unhandled exception. System.InvalidOperationException: Services:Merchant est absent.
       at HBA.Merchants.Contracts.Grpc.MerchantsGrpcRegistration.AddMerchantsGrpcClient(…)
       at Program.<Main>$(String[] args) in /src/services/common/review-service/…

Le fichier à corriger n'est ni celui de la pile, ni un `.cs` : c'est
`docker-compose.dev.yml`. C'est arrivé à engagement-service, le jour où répondre
à un avis a cessé d'être ouvert à tout compte inscrit — la garde exigeait de
savoir quel dossier vendeur porte le compte, donc un client vers
merchant-service, dont personne n'a pensé à ajouter l'adresse.

POURQUOI CE CONTRÔLE N'APPARTIENT PAS À `check-config-and-guards.py`.

Celui-là vérifie le sens INVERSE — une clé d'environnement dont aucune section de
configuration ne correspond (`OBJECTSTORAGE__*` qui ne liait rien, et media-service
tournant en mémoire tout un développement). Ici c'est une section RÉCLAMÉE dont
aucune variable ne correspond. Deux moitiés d'une même symétrie, et aucune ne voit
l'autre.

ET IL Y A UN TROISIÈME ENDROIT : LA FABRIQUE DES TESTS D'AUTORISATION.

`tests/Shared/AuthorizationTestFactory.cs` démarre les vrais `Program.cs` par
`WebApplicationFactory`. Les mêmes `Add<X>GrpcClient` y lèvent donc de la même
façon — sauf que l'échec ne ressemble à rien de connu : cinquante-neuf tests
d'autorisation tombent d'un coup, sur une exception levée AVANT la première
décision d'autorisation, et la pile désigne un fichier de contrats partagés.

C'est arrivé au lot 6.1, le jour où payment-service a gagné un client vers
food-order-service. Sa liste de clés était tenue à la main, au rythme des
besoins, et RIEN ne la reliait à celle des clients qui la réclament. Ce contrôle
vérifiait le compose et le configmap ; il ne regardait pas ce fichier-là.

Il exige désormais que la fabrique porte TOUTE clé réclamée par une extension
d'enregistrement — y compris celles qu'aucun test n'emploie encore. Une clé
posée d'avance ne coûte rien ; son absence coûte une suite entière et une demi-
heure à comprendre pourquoi.

ET IL Y AVAIT QUATRE AUTRES FABRIQUES. LE CONTRÔLE N'EN VOYAIT QU'UNE.

Corriger la fabrique d'autorisation après les cinquante-neuf échecs n'a fermé
qu'un cinquième du trou. `OrderIntegrationFixture`, `CatalogIntegrationFixture`,
`MerchantsIntegrationFixture` et `GatewayFactory` démarrent elles aussi de vrais
`Program.cs`, chacune avec sa propre liste d'adresses tenue à la main.

Le lot 7.4 a branché `AddProductsGrpcClient` dans `HBA.Order.Api/Program.cs`.
Trois tests d'intégration sont tombés au démarrage sur « Services:Catalog est
absent » — et le contrôle, vert, regardait ailleurs. C'était la CINQUIÈME
occurrence du même motif dans ce dépôt, et la deuxième fois que la correction
d'une occurrence laissait les autres intactes.

CES QUATRE-LÀ NE SE VÉRIFIENT PAS COMME LA PREMIÈRE. La fabrique
d'autorisation démarre n'importe quel `Program.cs` : on lui demande TOUT le
catalogue. Celles-ci en démarrent UN, désigné par la `ProjectReference` vers un
`*.Api.csproj` de leur `.csproj`. Leur exiger tout le catalogue serait du bruit —
on leur demande exactement ce que LEUR hôte réclame, ni plus ni moins.

CE QUE CE CONTRÔLE NE PEUT PAS DIRE : que l'adresse posée mène quelque part.
Ces fabriques visent délibérément des ports fermés pour les clients qu'aucun
chemin n'emprunte. Un client que le code se met à APPELER a donc besoin, en plus
de son adresse, d'un double — sans quoi l'erreur de construction devient une
erreur d'appel dans chaque test concerné. C'est exactement ce que le catalogue a
demandé au lot 7.4, et aucun contrôle statique ne l'aurait vu.

KUBERNETES N'EST PAS EXPOSÉ DE LA MÊME FAÇON.

Là-bas, `k8s/base/common/configmap.yaml` distribue TOUTES les adresses à TOUS les
pods : un client oublié y trouve la sienne par accident. Le compose, lui, énumère
les variables service par service — plus sûr en production, plus facile à oublier
en développement. On vérifie donc le compose service par service, et le configmap
seulement sur les clés réellement employées quelque part.
═══════════════════════════════════════════════════════════════════════════════
"""
import os
import re
import sys

RACINE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
COMPOSE = os.path.join(RACINE, 'docker-compose.dev.yml')
CONFIGMAP = os.path.join(RACINE, 'k8s', 'base', 'common', 'configmap.yaml')
FABRIQUE_TESTS = os.path.join(RACINE, 'tests', 'Shared', 'AuthorizationTestFactory.cs')

# Les racines où vivent les suites de tests. `apps/api-gateway/tests` n'est pas
# sous `tests/` — l'oublier laisserait `GatewayFactory` hors contrôle, ce qui est
# précisément la façon dont ce défaut se reproduit.
RACINES_TESTS = (
    os.path.join(RACINE, 'tests'),
    os.path.join(RACINE, 'apps'),
)
IGNORES = ('obj', 'bin', '_to_delete', 'node_modules', '.git')

ENREGISTREMENT = re.compile(r'Add(\w+)GrpcClient\s*\([^)]*\)\s*\{(.*?)\n    \}', re.S)
CLE = re.compile(r'configuration\["Services:(\w+)"\]')
APPEL = re.compile(r'(Add\w+GrpcClient)\s*\(')
POSEE = re.compile(r'"Services(?:__|:)(\w+)"')
REFERENCE = re.compile(r'ProjectReference\s+Include="([^"]+)"')


def clients_connus():
    """Chaque extension d'enregistrement, et la clé de configuration qu'elle exige."""
    table = {}
    for dossier, _, fichiers in os.walk(os.path.join(RACINE, 'shared', 'contracts')):
        for fichier in fichiers:
            if not fichier.endswith('.cs'):
                continue
            chemin = os.path.join(dossier, fichier)
            with open(chemin, encoding='utf-8', errors='ignore') as flux:
                contenu = flux.read()
            for methode in ENREGISTREMENT.finditer(contenu):
                cle = CLE.search(methode.group(2))
                if cle:
                    table['Add' + methode.group(1) + 'GrpcClient'] = cle.group(1)
    return table


def programmes(dossier):
    """Tous les `Program.cs` d'un service — deux groupes sont co-hébergés."""
    trouves = []
    for chemin, _, fichiers in os.walk(dossier):
        if 'Program.cs' in fichiers:
            trouves.append(os.path.join(chemin, 'Program.cs'))
    return trouves


def projets_de_test():
    """
    Les projets de test qui posent des adresses de service, et l'hôte qu'ils démarrent.

    Rend une liste de (chemin du .csproj, ensemble des clés posées, [Program.cs visés]).

    L'HÔTE EST DÉDUIT DE LA `ProjectReference` VERS UN `*.Api.csproj`, pas d'un
    nom de fichier ni d'une convention. Un projet de test ne peut démarrer par
    `WebApplicationFactory<Program>` qu'un assemblage qu'il référence : c'est la
    seule liaison que le compilateur garantit, donc la seule sur laquelle un
    contrôle peut s'appuyer sans partager une hypothèse avec le code.
    """
    projets = []

    for racine in RACINES_TESTS:
        for dossier, sous, fichiers in os.walk(racine):
            sous[:] = [d for d in sous if d not in IGNORES and not d.startswith('.')]

            csproj = [f for f in fichiers if f.endswith('.csproj')]
            if not csproj:
                continue

            sources = [f for f in fichiers if f.endswith('.cs')]
            posees = set()
            for fichier in sources:
                chemin = os.path.join(dossier, fichier)
                if os.path.abspath(chemin) == os.path.abspath(FABRIQUE_TESTS):
                    continue
                with open(chemin, encoding='utf-8', errors='ignore') as flux:
                    posees |= set(POSEE.findall(flux.read()))

            if not posees:
                continue

            chemin_csproj = os.path.join(dossier, csproj[0])
            with open(chemin_csproj, encoding='utf-8', errors='ignore') as flux:
                declaration = flux.read()

            hotes = []
            for reference in REFERENCE.findall(declaration):
                if not reference.endswith('.Api.csproj'):
                    continue
                vise = os.path.normpath(os.path.join(dossier, reference.replace('\\', os.sep)))
                hotes.extend(programmes(os.path.dirname(vise)))

            projets.append((chemin_csproj, posees, hotes))

    return projets


def main():
    try:
        import yaml
    except ImportError:
        # Même parti pris que check-k8s.py : un outil absent se signale, il ne
        # fait pas échouer la chaîne.
        print('  PyYAML absent — contrôle ignoré (pip install pyyaml).')
        return 0

    table = clients_connus()
    if not table:
        print("  Aucune extension d'enregistrement trouvée — contrôle sans objet.")
        return 0

    with open(COMPOSE, encoding='utf-8') as flux:
        compose = yaml.safe_load(flux)

    manques = []
    employees = set()
    examines = 0

    for nom, service in (compose.get('services') or {}).items():
        build = service.get('build')
        if not isinstance(build, dict) or not build.get('dockerfile'):
            continue

        dossier = os.path.join(RACINE, os.path.dirname(build['dockerfile']))
        if not os.path.isdir(dossier):
            continue

        fichiers = programmes(dossier)
        if not fichiers:
            continue

        examines += 1
        environnement = {str(k).upper() for k in (service.get('environment') or {})}

        exigees = set()
        for fichier in fichiers:
            with open(fichier, encoding='utf-8', errors='ignore') as flux:
                for appel in APPEL.findall(flux.read()):
                    if appel in table:
                        exigees.add(table[appel])

        employees |= exigees

        for clef in sorted(exigees):
            variable = 'SERVICES__' + clef.upper()
            if variable not in environnement:
                manques.append((nom, variable, build['dockerfile']))

    for nom, variable, dockerfile in manques:
        print(f'❌ {nom}')
        print(f'     {variable} absent de docker-compose.dev.yml')
        print(f'     le service l\'exige au démarrage — {dockerfile}')

    # Le configmap Kubernetes, sur les seules clés réellement employées.
    absentes_k8s = []
    if os.path.isfile(CONFIGMAP):
        with open(CONFIGMAP, encoding='utf-8') as flux:
            texte = flux.read()
        for clef in sorted(employees):
            variable = 'SERVICES__' + clef.upper()
            if variable + ':' not in texte:
                absentes_k8s.append(variable)

    for variable in absentes_k8s:
        print('❌ k8s/base/common/configmap.yaml')
        print(f'     {variable} absent — un pod qui emploie ce client ne démarrera pas')

    # ═════════════════════════════════════════════════════════════════════════
    # La fabrique des tests d'autorisation — le troisième endroit.
    #
    # ON EXIGE TOUTES LES CLÉS DU CATALOGUE, PAS SEULEMENT CELLES EMPLOYÉES.
    #
    # Le compose est vérifié service par service : chacun n'a besoin que de ce
    # qu'il appelle. Ici, non — la fabrique démarre N'IMPORTE LEQUEL des
    # `Program.cs` du dépôt, et la prochaine référence de projet peut faire
    # entrer une clé jusque-là inutile dans un hôte que la suite construit. Une
    # clé posée d'avance ne coûte rien ; son absence coûte une suite entière.
    # ═════════════════════════════════════════════════════════════════════════
    absentes_tests = []
    if os.path.isfile(FABRIQUE_TESTS):
        with open(FABRIQUE_TESTS, encoding='utf-8') as flux:
            fabrique = flux.read()
        for clef in sorted(set(table.values())):
            if f'"Services__{clef}"' not in fabrique:
                absentes_tests.append(clef)

    for clef in absentes_tests:
        print('❌ tests/Shared/AuthorizationTestFactory.cs')
        print(f'     Services__{clef} absent — l\'hôte lèvera À LA CONSTRUCTION dès qu\'un')
        print('     service sous test d\'autorisation emploiera ce client')

    # ═════════════════════════════════════════════════════════════════════════
    # Les QUATRE autres fabriques — chacune comparée à SON hôte.
    #
    # EXIGENCE EXACTE, PAS LE CATALOGUE ENTIER. Ces fabriques démarrent un
    # `Program.cs` désigné, pas n'importe lequel : leur demander toutes les clés
    # produirait du bruit que personne ne corrigerait, et le bruit finit toujours
    # par masquer le vrai manque.
    # ═════════════════════════════════════════════════════════════════════════
    absentes_fabriques = []
    fabriques_examinees = 0

    for chemin_csproj, posees, hotes in projets_de_test():
        if not hotes:
            # Une suite qui pose des adresses sans démarrer d'API : rien à comparer.
            # Ce n'est pas une anomalie — c'est une suite qui configure autre chose.
            continue

        fabriques_examinees += 1

        exigees = set()
        for hote in hotes:
            with open(hote, encoding='utf-8', errors='ignore') as flux:
                for appel in APPEL.findall(flux.read()):
                    if appel in table:
                        exigees.add(table[appel])

        for clef in sorted(exigees - posees):
            absentes_fabriques.append(
                (os.path.relpath(chemin_csproj, RACINE), clef,
                 os.path.relpath(hotes[0], RACINE)))

    for projet, clef, hote in absentes_fabriques:
        print(f'❌ {projet}')
        print(f'     Services__{clef} absent — l\'hôte LÈVE À LA CONSTRUCTION,')
        print(f'     donc toute la suite tombe avant la première assertion')
        print(f'     réclamé par {hote}')

    total = len(manques) + len(absentes_k8s) + len(absentes_tests) + len(absentes_fabriques)
    print()
    print(f'{examines} service(s) et {fabriques_examinees} fabrique(s) de test examinés, '
          f'{total} adresse(s) manquante(s).')
    return 1 if total else 0


if __name__ == '__main__':
    sys.exit(main())
