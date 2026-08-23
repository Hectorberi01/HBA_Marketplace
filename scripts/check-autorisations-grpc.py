#!/usr/bin/env python3
"""
═══════════════════════════════════════════════════════════════════════════════
UNE TABLE D'AUTORISATIONS QUI NE SUIT PAS LE CODE REDEVIENT « TOUT LE MONDE PEUT
TOUT » — SANS QUE PERSONNE NE S'EN APERÇOIVE.

LES DEUX DÉRIVES SONT AUSSI GRAVES L'UNE QUE L'AUTRE, ET N'ONT PAS LE MÊME
   SYMPTÔME.

  • UN APPEL SANS AUTORISATION casse en production, au premier appel réel :
    `PermissionDenied`. Bruyant, immédiat, réparable. Mais il casse un parcours
    utilisateur, et personne n'aura relié la panne à un fichier de sécurité.

  • UNE AUTORISATION SANS APPEL ne casse RIEN, jamais. Elle reste, elle
    s'accumule, et au bout de quelques lots la table autorise tout le monde à
    tout — exactement l'état qu'elle avait été écrite pour quitter. C'est la
    dérive silencieuse, donc la dangereuse.

D'où un contrôle qui refuse les deux : la table de `AutorisationsGrpc.cs` doit
être EXACTEMENT le graphe d'appel du dépôt, ni plus ni moins.

CE QU'IL VÉRIFIE

  1. Pour chaque hôte (projet portant un `Program`), l'ensemble des méthodes
     gRPC atteignables par ses références de projet transitives, comparé à
     l'entrée correspondante de la table.
  2. Que tout `Internal__ServiceName` écrit dans un compose est une clé de la
     table — un nom mal orthographié fermerait un service entier.

CE QU'IL NE VÉRIFIE PAS, ET POURQUOI IL LE DIT

  • LA GRANULARITÉ RESTE CELLE DU PAQUET DE CONTRATS. Une enveloppe
    `*.Contracts.Grpc` est une seule classe qui appelle tous les RPC de son
    service ; référencer le paquet donne donc droit à tous. Ce contrôle
    reproduit fidèlement cette approximation — il ne la corrige pas. La
    resserrer demande de découper les enveloppes par interface, ce qui est un
    lot en soi.

  • LES APPELS PAR RÉFLEXION OU PAR CANAL CONSTRUIT À LA MAIN. Seule la forme
    `_client.X(` est reconnue, qui est celle de tout le dépôt.

  • QUE L'HÔTE SOIT RÉELLEMENT DÉPLOYÉ. Un projet `*.Api` sans conteneur figure
    quand même dans la table : l'y laisser ne coûte rien, l'en retirer ferait
    échouer le jour où il est déployé.

RÉGÉNÉRER LA TABLE : ce script avec `--ecrire` réécrit le bloc engendré de
`AutorisationsGrpc.cs` en place, en conservant tout ce qui l'entoure.

Sort 1 en cas de divergence.
═══════════════════════════════════════════════════════════════════════════════
"""
import io
import os
import re
import sys

RACINE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
IGNORES = ("obj", "bin", "_to_delete", "node_modules", ".git", "clients")

TABLE_CS = os.path.join(
    RACINE, "shared", "common", "HBA.Shared.Hosting", "Grpc", "AutorisationsGrpc.cs")

PAQUET = re.compile(r'^\s*package\s+([\w\.]+)\s*;', re.M)
SERVICE = re.compile(r'^\s*service\s+(\w+)\s*\{')
RPC = re.compile(r'^\s*rpc\s+(\w+)\s*\(')

# `IdentityApi.IdentityApiClient` — le service proto se lit dans le nom du client
# généré. On exige que les deux moitiés coïncident pour ne pas confondre avec un
# type quelconque nommé `…Client`.
DECLARATION = re.compile(r'\b(\w+)\.(\w+)Client\b')

# `_client.NomAsync(` ET `_client.Nom(` : protoc engendre les deux formes.
APPEL = re.compile(r'\b_client\.(\w+?)(?:Async)?\s*\(')

REFERENCE = re.compile(r'ProjectReference\s+Include="([^"]+)"')


def fichiers(extension, depuis="."):
    for dossier, sous, noms in os.walk(os.path.join(RACINE, depuis)):
        sous[:] = [d for d in sous if d not in IGNORES]
        for nom in noms:
            if nom.endswith(extension):
                yield os.path.join(dossier, nom)


def lire(chemin):
    return io.open(chemin, encoding="utf-8", errors="replace").read()


def protos():
    """service proto -> (paquet, {rpc})"""
    connus = {}
    for chemin in fichiers(".proto", os.path.join("shared", "proto")):
        texte = lire(chemin)
        trouve = PAQUET.search(texte)
        paquet = trouve.group(1) if trouve else None
        if paquet is None:
            continue
        courant = None
        for ligne in texte.splitlines():
            debut = SERVICE.match(ligne)
            if debut:
                courant = debut.group(1)
                connus.setdefault(courant, (paquet, set()))
            methode = RPC.match(ligne)
            if methode and courant:
                connus[courant][1].add(methode.group(1))
    return connus


def projet_de(fichier):
    dossier = os.path.dirname(fichier)
    while len(dossier) > len(RACINE):
        for nom in os.listdir(dossier):
            if nom.endswith(".csproj"):
                return nom[:-7], os.path.join(dossier, nom)
        dossier = os.path.dirname(dossier)
    return None, None


def graphe():
    """Recalcule la table depuis le dépôt."""
    connus = protos()

    # projet -> {methodes appelees}
    appels = {}
    for chemin in fichiers(".cs"):
        texte = lire(chemin)
        invoques = set(APPEL.findall(texte))
        if not invoques:
            continue
        services = {a for a, b in DECLARATION.findall(texte) if a == b and a in connus}
        nom, _ = projet_de(chemin)
        if nom is None:
            continue
        for service in services:
            paquet, disponibles = connus[service]
            for invoque in invoques:
                if invoque in disponibles:
                    appels.setdefault(nom, set()).add(f"/{paquet}.{service}/{invoque}")

    projets = {os.path.basename(c)[:-7]: c for c in fichiers(".csproj")}

    def transitives(csproj, vus):
        try:
            texte = lire(csproj)
        except OSError:
            return vus
        for reference in REFERENCE.findall(texte):
            chemin = os.path.normpath(os.path.join(
                os.path.dirname(csproj), reference.replace("\\", "/")))
            nom = os.path.basename(chemin)[:-7]
            if nom in vus:
                continue
            vus.add(nom)
            transitives(chemin, vus)
        return vus

    # UN HÔTE EST UN PROJET QUI PORTE UN `Program`, PAS UN NOM EN `.Api`.
    #
    # La convention tient aujourd'hui, mais elle n'est écrite nulle part : un
    # futur hôte nommé autrement disparaîtrait silencieusement du contrôle, donc
    # de la table, donc de toute autorisation. La présence d'un point d'entrée,
    # elle, est une propriété du code.
    table = {}
    for nom, csproj in sorted(projets.items()):
        dossier = os.path.dirname(csproj)
        if not os.path.exists(os.path.join(dossier, "Program.cs")):
            continue
        methodes = set()
        for reference in transitives(csproj, {nom}):
            methodes |= appels.get(reference, set())
        table[nom] = sorted(methodes)
    return table


def table_ecrite():
    """Relit la table telle qu'elle est engendrée dans le fichier C#."""
    texte = lire(TABLE_CS)
    table = {}
    for bloc in re.finditer(
            r'\["([\w\.]+)"\]\s*=\s*(FrozenSet<string>\.Empty|new\[\]\s*\{(.*?)\})',
            texte, re.S):
        table[bloc.group(1)] = sorted(re.findall(r'"(/[^"]+)"', bloc.group(3) or ""))
    return table


def noms_des_compose():
    """Les `Internal__ServiceName` posés dans les fichiers compose."""
    poses = {}
    for dossier, sous, noms in os.walk(RACINE):
        sous[:] = [d for d in sous if d not in IGNORES]
        for nom in noms:
            if not (nom.endswith(".yml") or nom.endswith(".yaml")):
                continue
            chemin = os.path.join(dossier, nom)
            for valeur in re.findall(
                    r'^\s*Internal__ServiceName:\s*(\S+)\s*$', lire(chemin), re.M):
                poses.setdefault(valeur, []).append(os.path.relpath(chemin, RACINE))
    return poses


def ecrire(table):
    texte = lire(TABLE_CS)
    debut = texte.index("        {\n", texte.index("_table ="))
    fin = texte.index("        }\n        .ToFrozenDictionary(")
    corps = []
    for appelant in sorted(table):
        methodes = table[appelant]
        if not methodes:
            corps.append(f'            // {appelant} : aucun appel gRPC sortant.\n'
                         f'            ["{appelant}"] = FrozenSet<string>.Empty,\n')
            continue
        lignes = "".join(f'                "{m}",\n' for m in methodes)
        corps.append(f'            ["{appelant}"] =\n            new[]\n            {{\n'
                     f'{lignes}            }}\n            .ToFrozenSet(StringComparer.Ordinal),\n')
    io.open(TABLE_CS, "w", encoding="utf-8").write(
        texte[:debut] + "        {\n" + "\n".join(corps) + texte[fin:])


def principal():
    attendue = graphe()

    if "--ecrire" in sys.argv:
        ecrire(attendue)
        print(f"Table réécrite : {len(attendue)} appelants, "
              f"{sum(len(v) for v in attendue.values())} autorisations.")
        return 0

    ecrite = table_ecrite()
    anomalies = []

    for appelant in sorted(set(attendue) | set(ecrite)):
        if appelant not in ecrite:
            anomalies.append(
                f"  ❌ {appelant} : hôte absent de la table. "
                f"Aucun de ses {len(attendue[appelant])} appels ne passerait.")
            continue
        if appelant not in attendue:
            anomalies.append(
                f"  ❌ {appelant} : dans la table, mais ce n'est plus un hôte. "
                f"Entrée à retirer.")
            continue
        manquantes = sorted(set(attendue[appelant]) - set(ecrite[appelant]))
        surnumeraires = sorted(set(ecrite[appelant]) - set(attendue[appelant]))
        for methode in manquantes:
            anomalies.append(f"  ❌ {appelant} appelle {methode} sans y être autorisé.")
        for methode in surnumeraires:
            anomalies.append(
                f"  ❌ {appelant} est autorisé à {methode} sans jamais l'appeler.")

    for nom, ou in sorted(noms_des_compose().items()):
        if nom not in attendue:
            anomalies.append(
                f"  ❌ Internal__ServiceName={nom} ({', '.join(ou)}) "
                f"ne correspond à aucun hôte connu.")

    if anomalies:
        print("\n".join(anomalies))
        print(f"\n{len(anomalies)} divergence(s). "
              f"`scripts/check-autorisations-grpc.py --ecrire` régénère la table.")
        return 1

    print(f"{len(attendue)} appelant(s), "
          f"{sum(len(v) for v in attendue.values())} autorisation(s), 0 divergence.")
    return 0


if __name__ == "__main__":
    sys.exit(principal())
