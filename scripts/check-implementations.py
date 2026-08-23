#!/usr/bin/env python3
"""
═══════════════════════════════════════════════════════════════════════════════
UNE INTERFACE QUI CHANGE LAISSE SES DOUBLES DE TEST DERRIÈRE ELLE.

TROIS ALLERS-RETOURS DE BUILD POUR LE MÊME DÉFAUT, EN UNE SEULE SÉANCE.

Ajouter un paramètre à une méthode de dépôt — une BORNE, au lot 8.4 — casse
toutes ses implémentations. Celles du code de production se voient : on vient de
les écrire. Celles des TESTS, non : ce sont des classes qu'on ne relit jamais,
souvent enfouies au bas d'un fichier de test, et dont les méthodes lèvent
`NotSupportedException` parce qu'aucun test ne les appelle.

Le compilateur les attrape — mais seulement au build, c'est-à-dire après avoir
rendu la main. Chaque oubli coûte un cycle complet.

CE QU'IL VÉRIFIE

Pour chaque interface déclarée dans le dépôt, il relève ses méthodes (nom +
nombre de paramètres). Pour chaque classe qui déclare implémenter cette
interface, il vérifie qu'une méthode de même NOM et de même ARITÉ existe.

NOM ET ARITÉ, PAS SIGNATURE COMPLÈTE. Comparer les types demanderait de
résoudre les alias, les génériques et les `using` — c'est-à-dire d'écrire un
compilateur. L'arité suffit à attraper le défaut visé : un paramètre AJOUTÉ ou
RETIRÉ. Elle ne verra pas un type changé à arité constante, et c'est assumé.

CE QU'IL NE VÉRIFIE PAS, ET POURQUOI IL LE DIT AU LIEU DE SE TAIRE

  • Une classe qui hérite d'une base peut tenir le contrat par héritage. Elle est
    signalée en ⓘ, jamais en ❌ : le contrôle ne sait pas lire la base.
  • Les membres d'interface à CORPS (méthodes par défaut, C# 8) ne sont pas
    exigés — ils sont donc écartés du relevé.
  • Une méthode déclarée `abstract` COMPTE comme implémentée : elle satisfait le
    compilateur et reporte l'écriture sur les dérivées. Le contrôle ne va pas
    vérifier que les dérivées, elles, l'écrivent — le compilateur le fait.
  • Les propriétés, indexeurs et événements ne sont pas relevés : le défaut visé
    est le paramètre ajouté, qui n'existe que sur les méthodes.
  • Une classe partielle dont les méthodes vivent dans un autre fichier serait
    un faux positif. Le dépôt n'en a pas ; s'il en gagne une, ce texte est
    l'endroit où le dire.

CE CONTRÔLE NE REMPLACE PAS LE COMPILATEUR. Il l'ANTICIPE, sur la seule
famille d'erreurs qui se répète — et il tourne en deux secondes là où un build
en prend deux cents.
═══════════════════════════════════════════════════════════════════════════════
"""
import os
import re
import sys

from _lecture_csharp import sans_commentaires

RACINE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
IGNORES = ("obj", "bin", "_to_delete", "node_modules", ".git")

DECLARATION_INTERFACE = re.compile(
    r'^\s*(?:public|internal|private|protected)?\s*(?:partial\s+)?interface\s+(I\w+)', re.M)

DECLARATION_CLASSE = re.compile(
    r'^\s*(?:public|internal|private|protected)?\s*(?:sealed\s+|abstract\s+|static\s+|partial\s+)*'
    r'class\s+(\w+)\s*(?:<[^>]*>)?\s*:\s*([^\{\r\n]+)', re.M)

# Une déclaration de méthode d'interface : se termine par « ; », pas de corps.
MEMBRE = re.compile(r'^\s*(?!//|/\*|\*)([\w<>\[\],\?\. ]+?)\s+(\w+)\s*\(([^;{]*)\)\s*;', re.M)

# Une méthode de classe : corps en accolade OU en expression.
METHODE_CLASSE = re.compile(r'\b(\w+)\s*(?:<[^>()]*>)?\s*\(([^)]*)\)\s*(?:=>|\{)')

# UNE MÉTHODE PEUT TENIR LE CONTRAT SANS AVOIR DE CORPS.
#
# `public abstract Task<X> FaireAsync(Y y, CancellationToken ct);` implémente
# l'interface : elle reporte l'écriture sur les classes dérivées, mais elle
# SATISFAIT le compilateur. Reconnue par sa seule terminaison en « ; », donc
# invisible pour METHODE_CLASSE, qui exige `=>` ou `{`.
#
# C'est ce qui a valu trois faux positifs à HttpPaymentGatewayBase, une base
# abstraite dont les quatre méthodes de passerelle sont déclarées abstraites.
#
# On EXIGE le modificateur (abstract / extern / partial) plutôt que d'accepter
# toute ligne finissant par « ; » : sans lui, un simple appel de méthode dans un
# corps — `await Publier(evenement);` — passerait pour une déclaration et
# ferait taire le contrôle sur une vraie absence.
MEMBRE_SANS_CORPS = re.compile(
    r'^\s*(?:public|protected|internal|private)?\s*(?:public|protected|internal|private)?\s*'
    r'(?:abstract|extern|partial)\s+[^;{()]*?\b(\w+)\s*(?:<[^>()]*>)?\s*\(([^;{]*)\)\s*;', re.M)


def arite(parametres: str) -> int:
    """Compte les paramètres, en ignorant les virgules imbriquées."""
    parametres = parametres.strip()
    if not parametres:
        return 0

    profondeur = 0
    compte = 1
    for caractere in parametres:
        if caractere in "<([":
            profondeur += 1
        elif caractere in ">)]":
            profondeur -= 1
        elif caractere == "," and profondeur == 0:
            compte += 1
    return compte


def corps(source: str, depart: int) -> str:
    """Le bloc { … } qui suit `depart`, accolades équilibrées."""
    ouverture = source.find("{", depart)
    if ouverture == -1:
        return ""

    profondeur = 0
    for i in range(ouverture, len(source)):
        if source[i] == "{":
            profondeur += 1
        elif source[i] == "}":
            profondeur -= 1
            if profondeur == 0:
                return source[ouverture:i]
    return source[ouverture:]


def main():
    fichiers = []
    for dossier, sous, noms in os.walk(RACINE):
        sous[:] = [d for d in sous if d not in IGNORES and not d.startswith(".")]
        fichiers.extend(
            os.path.join(dossier, n) for n in noms
            if n.endswith(".cs") and not n.endswith(".Designer.cs"))

    # LES COMMENTAIRES SONT RETIRÉS AVANT TOUTE LECTURE.
    #
    # Sans cela, une méthode dont la signature est séparée de son corps par un
    # commentaire — forme courante dans ce dépôt, où les encadrés expliquent la
    # requête juste avant le `=>` — passe pour non implémentée. C'est ce qui a valu
    # dix-neuf faux positifs à la première exécution de ce contrôle, sur du code
    # qui compilait. Un contrôle qui crie au loup dix-neuf fois est pire que pas de
    # contrôle du tout : on cesse de le lire.
    sources = {}
    for chemin in fichiers:
        with open(chemin, encoding="utf-8", errors="replace") as flux:
            sources[chemin] = sans_commentaires(flux.read())

    # ── Le contrat de chaque interface : {nom: {(methode, arite)}}
    contrats = {}
    for chemin, source in sources.items():
        for declaration in DECLARATION_INTERFACE.finditer(source):
            nom = declaration.group(1)
            bloc = corps(source, declaration.end())
            membres = {
                (m.group(2), arite(m.group(3)))
                for m in MEMBRE.finditer(bloc)
                # Un `get;`/`set;` de propriété n'est pas une méthode.
                if m.group(2) not in ("get", "set", "init")
            }
            if membres:
                contrats.setdefault(nom, set()).update(membres)

    anomalies = []
    incertains = []
    classes_examinees = 0

    for chemin, source in sources.items():
        for declaration in DECLARATION_CLASSE.finditer(source):
            classe = declaration.group(1)
            heritages = [h.strip().split("<")[0] for h in declaration.group(2).split(",")]

            attendus = set()
            interfaces = []
            for h in heritages:
                if h in contrats:
                    attendus |= contrats[h]
                    interfaces.append(h)

            if not attendus:
                continue

            classes_examinees += 1

            bloc = corps(source, declaration.end())
            presentes = {
                (m.group(1), arite(m.group(2)))
                for m in METHODE_CLASSE.finditer(bloc)
            }
            presentes |= {
                (m.group(1), arite(m.group(2)))
                for m in MEMBRE_SANS_CORPS.finditer(bloc)
            }

            manquants = sorted(attendus - presentes)
            if not manquants:
                continue

            # Une base non-interface peut tenir le contrat : on ne tranche pas.
            avec_base = any(h not in contrats and not h.startswith("I") for h in heritages)
            relatif = os.path.relpath(chemin, RACINE)

            for methode, n in manquants:
                if avec_base:
                    incertains.append((relatif, classe, methode, n))
                else:
                    anomalies.append((relatif, classe, methode, n, ", ".join(interfaces)))

    print()
    print("  %d classe(s) implémentant une interface du dépôt." % classes_examinees)

    if incertains:
        print()
        print("  ── Peut-être tenu par une classe de base — non tranché")
        for chemin, classe, methode, n in incertains[:20]:
            print("       ⓘ %s : %s.%s/%d" % (chemin, classe, methode, n))

    print()
    for chemin, classe, methode, n, interfaces in anomalies:
        print("  ❌ %s" % chemin)
        print("       « %s » n'implémente pas %s/%d, exigée par %s."
              % (classe, methode, n, interfaces))
        print("       Un paramètre a probablement été ajouté ou retiré côté interface.")

    print("%d implémentation(s) manquante(s)." % len(anomalies))
    return 1 if anomalies else 0


if __name__ == "__main__":
    sys.exit(main())
