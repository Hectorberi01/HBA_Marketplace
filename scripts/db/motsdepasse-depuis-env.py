#!/usr/bin/env python3
# ==============================================================================
# CONVERTIT LE `.env` DU COMPOSE EN FICHIER DE MOTS DE PASSE POUR KUBERNETES.
#
# CE PONT MANQUAIT, ET IL MANQUAIT AU PIRE ENDROIT.
#
# `docker-compose.prod.yml` lit les mots de passe sous la forme
# `HBA_<ROLE>_PASSWORD` ; `secret-depuis-motsdepasse.py` les attend en deux
# colonnes, `hba_<role> <motdepasse>`. Entre les deux, il n'y avait rien — donc
# une recopie a la main de quatorze valeurs, au moment precis de la bascule en
# production.
#
# UNE RECOPIE MANUELLE SE TROMPE, ET SURTOUT ELLE EXPOSE. Elle fait transiter
# chaque mot de passe par le presse-papier, l'historique du shell et la sortie
# d'un terminal — c'est deja arrive le 28 aout 2026. Ce script lit, convertit,
# ecrit, et n'affiche que des NOMS DE ROLES.
#
# LA LISTE DES ROLES ATTENDUS EST DERIVEE, PAS RECOPIEE.
#
# Elle vient de la table `CLES` de `secret-depuis-motsdepasse.py`, qui est
# elle-meme le miroir de `k8s/base/common/secret.yaml`. Un service ajoute au
# catalogue fait donc apparaitre son role ici sans qu'on touche a ce fichier —
# et un role absent du `.env` est SIGNALE, jamais silencieusement omis.
#
# CE QUE CE SCRIPT NE COUVRE PAS :
#   - il ne verifie AUCUNE connexion. Qu'un mot de passe soit le bon, seul
#     Postgres le dira ;
#   - il ne cree ni base ni role. C'est `creer-bases.sh` qui le fait ;
#   - le fichier de sortie contient les secrets EN CLAIR. Il est en 0600 et hors
#     du depot, mais il doit etre supprime des que le Secret est applique.
# ==============================================================================

import argparse
import os
import re
import stat
import sys

RACINE = os.path.normpath(
    os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
SOURCE_CLES = os.path.join(RACINE, "scripts", "db", "secret-depuis-motsdepasse.py")

# `HBA_IDENTITY_PASSWORD=xxx` — avec ou sans `export`, avec ou sans guillemets.
LIGNE_ENV = re.compile(
    r'^\s*(?:export\s+)?HBA_([A-Z0-9]+)_PASSWORD\s*=\s*(.*?)\s*$')


def roles_attendus():
    """Les roles distincts que `secret-depuis-motsdepasse.py` reclame.

    Lus dans sa table `CLES`, pas recopies : une base ajoutee la-bas doit
    apparaitre ici toute seule.
    """
    if not os.path.exists(SOURCE_CLES):
        return None
    source = open(SOURCE_CLES, encoding="utf-8").read()
    bloc = re.search(r"^CLES\s*=\s*\[(.*?)^\]", source, re.S | re.M)
    if not bloc:
        return None
    return sorted(set(re.findall(r'"\s*,\s*"(hba_[a-z]+)"', bloc.group(1))))


def valeur_nettoyee(brute):
    """Retire les guillemets englobants, s'il y en a."""
    if len(brute) >= 2 and brute[0] == brute[-1] and brute[0] in ("'", '"'):
        return brute[1:-1]
    return brute


def main():
    p = argparse.ArgumentParser(
        description="Convertit un .env de compose en fichier de mots de passe. "
                    "Aucune valeur n'est affichee.")
    p.add_argument("env", help="le .env du compose de production")
    p.add_argument("sortie", help="le fichier a deux colonnes a ecrire")
    args = p.parse_args()

    if not os.path.exists(args.env):
        print("introuvable : %s" % args.env, file=sys.stderr)
        return 1

    # LE FICHIER SOURCE DOIT ETRE EN 0600, comme dans l'autre script.
    #
    # S'il ne l'est pas, c'est que quelqu'un d'autre a PU le lire. On refuse
    # plutot que de propager le probleme dans un second fichier — et on ne
    # corrige pas le mode a la place de l'operateur : un `chmod` silencieux
    # ferait croire que le secret n'a jamais ete expose.
    mode = stat.S_IMODE(os.stat(args.env).st_mode)
    if mode & 0o077:
        print("REFUS : %s est en %o, attendu 0600." % (args.env, mode), file=sys.stderr)
        print("        chmod 600 %s puis relancer." % args.env, file=sys.stderr)
        return 1

    attendus = roles_attendus()
    if not attendus:
        print("impossible de lire la table CLES de %s"
              % os.path.relpath(SOURCE_CLES, RACINE), file=sys.stderr)
        return 1

    trouves = {}
    with open(args.env, encoding="utf-8") as f:
        for ligne in f:
            if ligne.lstrip().startswith("#"):
                continue
            m = LIGNE_ENV.match(ligne.rstrip("\n"))
            if m:
                trouves["hba_" + m.group(1).lower()] = valeur_nettoyee(m.group(2))

    print("%d role(s) attendu(s) par le Secret, %d trouve(s) dans %s"
          % (len(attendus), len(trouves), os.path.basename(args.env)))

    anomalies = []
    for role in attendus:
        if role not in trouves:
            anomalies.append("%s : aucune variable HBA_%s_PASSWORD dans le .env"
                             % (role, role[4:].upper()))
        elif not trouves[role]:
            anomalies.append("%s : la variable existe mais sa valeur est vide" % role)

    # Un role du .env que le Secret ne reclame pas n'est pas une erreur — il
    # appartient a un service hors du lot deploye. On le nomme, sans le retenir.
    en_trop = sorted(set(trouves) - set(attendus))
    for role in en_trop:
        print("  ignore (aucune cle du Secret ne le reclame) : %s" % role)

    if anomalies:
        for a in anomalies:
            print("  ANOMALIE " + a, file=sys.stderr)
        print("%d anomalie(s) : rien n'a ete ecrit." % len(anomalies), file=sys.stderr)
        return 1

    # Un point-virgule ou un guillemet casserait la chaine de connexion plus
    # loin. Le signaler ICI plutot qu'a l'etape suivante : la cause est dans le
    # .env, pas dans le script qui la lit.
    for role in attendus:
        v = trouves[role]
        if ";" in v or "'" in v or '"' in v:
            print("  ANOMALIE %s : le mot de passe contient ; ou un guillemet, "
                  "il casserait la chaine de connexion Npgsql" % role, file=sys.stderr)
            return 1

    dossier = os.path.dirname(os.path.abspath(args.sortie))
    if dossier:
        os.makedirs(dossier, exist_ok=True)
    # Ouverture en 0600 des la creation : pas de fenetre ou le fichier est lisible.
    fd = os.open(args.sortie, os.O_WRONLY | os.O_CREAT | os.O_TRUNC, 0o600)
    with os.fdopen(fd, "w", encoding="utf-8") as f:
        f.write("# Engendre par scripts/db/motsdepasse-depuis-env.py — NE PAS COMMITER.\n")
        f.write("# Source : %s\n" % os.path.basename(args.env))
        for role in attendus:
            f.write("%s %s\n" % (role, trouves[role]))

    print()
    for role in attendus:
        print("  %-20s %2d car." % (role, len(trouves[role])))
    print()
    print("Ecrit : %s (0600), %d role(s)." % (args.sortie, len(attendus)))
    print("Aucune valeur n'a ete affichee. Le fichier, lui, est en clair :")
    print("  1. python3 scripts/db/secret-depuis-motsdepasse.py --env prod %s /tmp/secret.yaml"
          % args.sortie)
    print("  2. supprimer %s quand le Secret est applique." % args.sortie)
    return 0


if __name__ == "__main__":
    sys.exit(main())
