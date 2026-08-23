#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Équilibre des accolades, parenthèses et crochets dans les fichiers C#.

═══════════════════════════════════════════════════════════════════════════════
POURQUOI CE CONTRÔLE EXISTE ALORS QUE LE COMPILATEUR LE FAIT MIEUX.

Il ne remplace pas `dotnet build` : il le PRÉCÈDE. Une accolade manquante coûte
un aller-retour complet — restauration, compilation des projets en amont, échec
sur `CS1513 } attendue` — pour une faute qu'une lecture de trois secondes
attrape. Sur ce dépôt, où l'on édite souvent plusieurs dizaines de fichiers avant
de compiler, cet aller-retour est le poste de temps perdu le plus régulier.

CE N'EST PAS UN COMPTAGE NAÏF, ET IL NE PEUT PAS L'ÊTRE.

Compter les caractères sur le texte brut donne des résultats faux dans les deux
sens sur ce dépôt, pour trois raisons :

  • les commentaires sont en français et pleins d'apostrophes — « l'hôte »,
    « d'un » — que tout traitement des littéraux de caractère apparie deux à
    deux, avalant le code situé entre les deux ;
  • les commentaires contiennent des parenthèses dépareillées, parce qu'ils
    citent du code : « (voir `realGateways.Count == 0` plus haut) » ;
  • les chaînes interpolées imbriquent des chaînes :
    `$"... {string.Join(", ", noms)} ..."` ;
  • les migrations portent du SQL en littéral BRUT — délimité par trois
    guillemets — dont le contenu déborde d'accolades, de parenthèses et de `$$`
    PostgreSQL.

Le lecteur ci-dessous est donc un vrai automate : commentaires de ligne et de
bloc, chaînes normales, verbatim (`@"…"`), interpolées (`$"…"`) avec leurs trous
`{…}` — dont le contenu redevient du code, y compris s'il contient une chaîne.

ET L'ÉQUILIBRE NE SUFFIT PAS. C'EST LA LEÇON DU 21 AOÛT 2026.

Ce jour-là, une accolade fermante de méthode a disparu et une accolade orpheline
est apparue en fin de fichier. Le compte était donc JUSTE, et un contrôle
d'équilibre seul serait resté muet. Ce que le fichier était devenu, en revanche,
se voyait d'un coup d'œil : la méthode privée suivante s'était retrouvée à
l'intérieur du corps de la précédente — une fonction locale, à laquelle C#
refuse le modificateur `private`. D'où `CS1513 } attendue`, à quinze lignes de la
vraie faute.

D'où le second contrôle : dans ce dépôt, l'indentation est de quatre espaces par
niveau et une déclaration de membre commence toujours par son modificateur
d'accès. Si le nombre d'espaces ne correspond pas à la profondeur d'accolades
réelle, c'est qu'une accolade manque ou est en trop — même quand le compte
tombe juste.
═══════════════════════════════════════════════════════════════════════════════
"""


import os
import re
import sys

ROOT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..'))

PAIRES = {'{': '}', '(': ')', '[': ']'}
FERMANTS = {v: k for k, v in PAIRES.items()}


def code_seul(source, signaler=None):
    """
    Rend le source privé de ses commentaires et de ses littéraux.

    Les caractères retirés sont remplacés par des espaces afin que les numéros de
    ligne restent EXACTS : c'est ce qui permet de désigner la ligne fautive.
    """
    sortie = []
    i = 0
    n = len(source)

    # Pile des chaînes interpolées ouvertes : pour chacune, la profondeur
    # d'accolades du code au moment où son trou `{` s'est ouvert.
    interpolees = []

    def pousser(caractere):
        sortie.append(caractere if caractere == '\n' else ' ')

    while i < n:
        c = source[i]
        suivant = source[i + 1] if i + 1 < n else ''

        # ── Commentaires ────────────────────────────────────────────────────
        if c == '/' and suivant == '/':
            while i < n and source[i] != '\n':
                pousser(source[i])
                i += 1
            continue

        if c == '/' and suivant == '*':
            while i < n and not (source[i] == '*' and i + 1 < n and source[i + 1] == '/'):
                pousser(source[i])
                i += 1
            for _ in range(min(2, n - i)):
                pousser(source[i])
                i += 1
            continue

        # ── Littéral de caractère ───────────────────────────────────────────
        if c == "'":
            pousser(c)
            i += 1
            while i < n and source[i] != "'":
                if source[i] == '\\':
                    pousser(source[i])
                    i += 1
                    if i < n:
                        pousser(source[i])
                        i += 1
                    continue
                pousser(source[i])
                i += 1
            if i < n:
                pousser(source[i])
                i += 1
            continue

        # ── Chaînes ─────────────────────────────────────────────────────────
        prefixe = ''
        j = i
        while j < n and source[j] in '@$':
            prefixe += source[j]
            j += 1

        # ── Littéral BRUT (`"""…"""`) ───────────────────────────────────────
        #
        # TRAITÉ COMME OPAQUE, TROUS D'INTERPOLATION COMPRIS.
        #
        # Un `$$"""…"""` peut contenir du code dans ses trous ; ne pas le lire
        # revient à ignorer ce code. C'est un choix : manquer une anomalie coûte un
        # aller-retour de compilation, en inventer une fait perdre confiance dans le
        # contrôle — et un contrôle en qui on ne croit plus, personne ne le lance.
        # Le dépôt n'utilise les littéraux bruts que pour du SQL, sans trou.
        if j < n and source[j:j + 3] == '"""':
            longueur = 0
            while j + longueur < n and source[j + longueur] == '"':
                longueur += 1

            for _ in range(j - i + longueur):
                pousser(source[i])
                i += 1

            while i < n:
                if source[i] == '"':
                    fin = 0
                    while i + fin < n and source[i + fin] == '"':
                        fin += 1
                    if fin >= longueur:
                        for _ in range(fin):
                            pousser(source[i])
                            i += 1
                        break
                    for _ in range(fin):
                        pousser(source[i])
                        i += 1
                    continue
                pousser(source[i])
                i += 1
            continue

        if prefixe and j < n and source[j] == '"':
            verbatim = '@' in prefixe
            interpole = '$' in prefixe
            for _ in range(j - i + 1):
                pousser(source[i])
                i += 1
            i = _lire_chaine(source, i, n, verbatim, interpole, pousser, interpolees, sortie, signaler)
            continue

        if c == '"':
            pousser(c)
            i += 1
            i = _lire_chaine(source, i, n, False, False, pousser, interpolees, sortie, signaler)
            continue

        # ── Sortie d'un trou d'interpolation ────────────────────────────────
        if c == '}' and interpolees and interpolees[-1]['profondeur'] == 0:
            contexte = interpolees.pop()
            pousser(c)
            i += 1
            i = _lire_chaine(
                source, i, n, contexte['verbatim'], True, pousser, interpolees, sortie, signaler)
            continue

        if interpolees:
            if c == '{':
                interpolees[-1]['profondeur'] += 1
            elif c == '}':
                interpolees[-1]['profondeur'] -= 1

        sortie.append(c)
        i += 1

    return ''.join(sortie)


def _lire_chaine(source, i, n, verbatim, interpole, pousser, interpolees, sortie, signaler=None):
    """Consomme le corps d'une chaîne. Rend l'indice APRÈS le guillemet fermant."""
    while i < n:
        c = source[i]

        if not verbatim and c == '\\':
            pousser(c)
            i += 1
            if i < n:
                pousser(source[i])
                i += 1
            continue

        if c == '"':
            # En verbatim, `""` est un guillemet littéral, pas la fin.
            if verbatim and i + 1 < n and source[i + 1] == '"':
                pousser(c)
                pousser(source[i + 1])
                i += 2
                continue
            pousser(c)
            return i + 1

        if interpole and c == '{':
            # `{{` est une accolade littérale.
            if i + 1 < n and source[i + 1] == '{':
                pousser(c)
                pousser(source[i + 1])
                i += 2
                continue

            # Ouverture d'un trou : ce qui suit est du CODE.
            pousser(c)
            interpolees.append({'profondeur': 0, 'verbatim': verbatim})
            return i + 1

        if not verbatim and c == '\n':
            # ═════════════════════════════════════════════════════════════════
            # UN SAUT DE LIGNE DANS UNE CHAÎNE NON VERBATIM — C'EST CS1010.
            #
            # Ce cas était DÉTECTÉ et TU. On rendait la main pour ne pas avaler
            # le reste du fichier, et on ne disait rien : les accolades restaient
            # équilibrées, le contrôle rendait « 0 anomalie », et le compilateur
            # crachait dix-neuf erreurs sur le même fichier.
            #
            # C'est arrivé pour de vrai (lot 4.1, `FoodCartModuleInstaller`) : un
            # `\n` écrit comme un vrai retour à la ligne au lieu de l'échappement.
            # Le contrôle qui existait pour attraper exactement ce genre de faute
            # a répondu vert.
            #
            # ET C'EST PRÉCISÉMENT LE PIRE MODE DE DÉFAILLANCE D'UN GARDE-FOU :
            # il ne manque pas l'anomalie par ignorance, il la VOIT et se tait.
            # ═════════════════════════════════════════════════════════════════
            if signaler is not None:
                signaler(i)

            return i

        pousser(c)
        i += 1

    return i


DECLARATION = re.compile(r'^(\s*)(public|private|protected|internal)\s')


def profondeurs_par_ligne(nu):
    """Profondeur d'accolades AU DÉBUT de chaque ligne du source dépouillé."""
    profondeurs = []
    courante = 0

    for ligne in nu.split('\n'):
        profondeurs.append(courante)
        courante += ligne.count('{') - ligne.count('}')

    return profondeurs


def membres_mal_places(nu):
    """
    Déclarations de membre dont l'indentation contredit la profondeur réelle.

    ON NE REGARDE QUE LES LIGNES COMMENÇANT PAR UN MODIFICATEUR D'ACCÈS.

    Elles sont, dans ce dépôt, toujours des déclarations de membre — jamais des
    continuations d'expression, jamais des `case`, jamais des initialiseurs. C'est
    ce qui rend la comparaison indentation/profondeur fiable ici alors qu'elle
    serait bruyante sur n'importe quelle ligne.

    Les lignes indentées avec autre chose que des multiples de quatre espaces sont
    ignorées : mieux vaut ne rien dire que dire n'importe quoi.
    """
    profondeurs = profondeurs_par_ligne(nu)
    anomalies = []

    for index, ligne in enumerate(nu.split('\n')):
        correspondance = DECLARATION.match(ligne)
        if not correspondance:
            continue

        indentation = correspondance.group(1)
        if '\t' in indentation or len(indentation) % 4 != 0:
            continue

        attendue = len(indentation) // 4
        reelle = profondeurs[index]

        if attendue != reelle:
            anomalies.append((
                index + 1,
                'déclaration indentée pour la profondeur %d, mais la profondeur '
                'réelle est %d — une accolade manque ou est en trop plus haut'
                % (attendue, reelle)))

    return anomalies


def analyser(chemin):
    """Rend la liste des anomalies : (ligne, message)."""
    with open(chemin, encoding='utf-8') as handle:
        source = handle.read()

    # COLLECTÉES PENDANT LE DÉCOUPAGE, RAPPORTÉES EN PREMIER.
    #
    # Une chaîne coupée par un saut de ligne n'a AUCUN effet sur l'équilibre des
    # accolades — le contenu est retiré dans les deux cas. Elle ne peut donc pas
    # être trouvée par le comptage : il faut la signaler au moment où on la voit.
    chaines_coupees = []

    nu = code_seul(source, lambda position: chaines_coupees.append(
        (source.count(chr(10), 0, position) + 1,
         source[max(0, position - 60):position].rsplit(chr(34), 1)[-1])))

    pile = []
    anomalies = []
    ligne = 1

    for c in nu:
        if c == '\n':
            ligne += 1
        elif c in PAIRES:
            pile.append((c, ligne))
        elif c in FERMANTS:
            if not pile:
                anomalies.append((ligne, '« %s » sans ouvrant' % c))
            elif pile[-1][0] != FERMANTS[c]:
                ouvrant, ouverte = pile.pop()
                anomalies.append((
                    ligne,
                    '« %s » ferme un « %s » ouvert ligne %d' % (c, ouvrant, ouverte)))
            else:
                pile.pop()

    for ouvrant, ouverte in pile:
        anomalies.append((ouverte, '« %s » jamais fermé' % ouvrant))

    # SEULEMENT SI L'ÉQUILIBRE EST BON. Sur un fichier déjà déséquilibré, la
    # profondeur dérive après la première anomalie et CHAQUE déclaration suivante
    # serait signalée : cinquante lignes de bruit pour une seule faute.
    if not anomalies:
        anomalies.extend(membres_mal_places(nu))

    # En tête : c'est la cause, et le reste en découle souvent.
    anomalies = [
        (ligne_coupee,
         'saut de ligne dans une chaîne (CS1010) — « %s… » ; écrire un échappement, ou passer la chaîne en verbatim' % extrait.strip()[:40])
        for ligne_coupee, extrait in chaines_coupees
    ] + anomalies

    return anomalies


IGNORES = ('/obj/', '/bin/', '/_to_delete/')


def fichiers_demandes(arguments):
    """
    Les `.cs` passés en argument, sinon TOUT le dépôt.

    PAS « CE QUE GIT VOIT COMME MODIFIÉ », ET C'EST DÉLIBÉRÉ.

    `git status --porcelain` liste un dossier entier — pas ses fichiers — quand ce
    dossier n'est pas suivi. Un lot de fichiers NEUFS, exactement ceux qu'on vient
    d'écrire et sur lesquels une faute de structure est la plus probable, passerait
    donc au travers. Le dépôt entier tient en moins d'une seconde : le filtrage ne
    ferait gagner que du risque.
    """
    demandes = [a for a in arguments if a.endswith('.cs')]
    if demandes:
        return demandes

    trouves = []
    for dossier, sous_dossiers, fichiers in os.walk(ROOT):
        sous_dossiers[:] = [d for d in sous_dossiers if d not in ('obj', 'bin', '.git', 'node_modules')]
        for nom in fichiers:
            if not nom.endswith('.cs'):
                continue
            chemin = os.path.relpath(os.path.join(dossier, nom), ROOT)
            if any(('/' + chemin.replace(os.sep, '/') + '/').find(ignore) >= 0 for ignore in IGNORES):
                continue
            trouves.append(chemin)

    return sorted(trouves)


def main():
    fichiers = fichiers_demandes(sys.argv[1:])
    total = 0

    for relatif in fichiers:
        chemin = os.path.join(ROOT, relatif)
        try:
            anomalies = analyser(chemin)
        except (OSError, UnicodeDecodeError):
            continue

        if not anomalies:
            continue

        print('❌ %s' % relatif)
        for ligne, message in anomalies:
            total += 1
            print('     ligne %d : %s' % (ligne, message))

    print()
    print('%d fichier(s) C# analysé(s), %d anomalie(s) de structure.' % (len(fichiers), total))

    return 1 if total else 0


if __name__ == '__main__':
    sys.exit(main())
