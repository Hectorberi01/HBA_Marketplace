#!/usr/bin/env python3
"""
═══════════════════════════════════════════════════════════════════════════════
CE QUE LE FICHIER D'ENVIRONNEMENT DOIT PORTER, VÉRIFIÉ AVANT DE PARTIR.

    ./scripts/verifier-env-compose.py <compose.yml> <fichier.env>

CE QUI ÉTAIT CASSÉ : COMPOSE NE NOMME QU'UNE VARIABLE À LA FOIS.

`${VAR:?...}` arrête l'interpolation à la PREMIÈRE variable manquante. Sur un
fichier neuf auquel il manque neuf clés, cela fait neuf allers-retours vers un
VPS, chacun précédé d'un envoi de sources. Le message est juste, il est
simplement servi au compte-gouttes.

Ce contrôle les liste toutes, en une passe, sans rien envoyer nulle part.

IL NE FAUT JAMAIS IMPRIMER UNE VALEUR.

Ce script lit un fichier qui porte tous les mots de passe de production. Il
n'affiche donc que des NOMS de variables, et une longueur quand elle est nulle.
Aucun chemin de code ne doit rendre autre chose — c'est la seule règle qui
compte ici, et elle vaut pour toute modification future.

CE QU'IL NE VÉRIFIE PAS :

  • que les valeurs soient les BONNES. Un mot de passe présent mais faux passe
    ce contrôle et échoue à la connexion. Personne ici ne peut joindre la base.
  • que `AUTHENTICATION__SIGNINGKEY` et `JWT__SIGNINGKEY` coïncident — voir
    plus bas, c'est le seul couple dont l'égalité est vérifiée.
  • les variables à valeur par défaut (`${VAR:-...}`) : leur absence est prévue.
═══════════════════════════════════════════════════════════════════════════════
"""
import io
import os
import re
import sys

# Posées par `deployer.sh` dans l'environnement du shell, qui l'emporte sur le
# fichier. Les réclamer dans le fichier serait une fausse alerte.
FOURNIES_PAR_LE_SCRIPT = {"HBA_TAG"}

# Deux noms pour une seule clé : identity-service signe avec l'une, les dix-huit
# autres vérifient avec l'autre. Une divergence rend 401 partout, sans erreur.
COUPLES_IDENTIQUES = [("AUTHENTICATION__SIGNINGKEY", "JWT__SIGNINGKEY")]

OBLIGATOIRE = re.compile(r"\$\{([A-Za-z_][A-Za-z0-9_]*):\?")


def variables_du_compose(chemin):
    lignes = [l for l in io.open(chemin, encoding="utf-8").read().splitlines()
              if not l.lstrip().startswith("#")]
    return set(OBLIGATOIRE.findall("\n".join(lignes)))


def lire_env(chemin):
    """nom -> valeur. Aucune valeur ne sort de cette fonction."""
    valeurs = {}
    for ligne in io.open(chemin, encoding="utf-8"):
        ligne = ligne.strip()
        if not ligne or ligne.startswith("#") or "=" not in ligne:
            continue
        nom, _, valeur = ligne.partition("=")
        valeurs[nom.strip()] = valeur
    return valeurs


def main():
    if len(sys.argv) not in (2, 3):
        print("usage: verifier-env-compose.py <compose.yml> [fichier.env]",
              file=sys.stderr)
        return 2

    # ═══════════════════════════════════════════════════════════════════════
    # UN SEUL ARGUMENT : LA LISTE, SANS RIEN COMPARER.
    #
    # Sous Coolify, les valeurs ne vivent plus dans un fichier mais dans son
    # interface, et rien ici ne peut les lire. Ce qui reste utile, c'est la
    # LISTE de ce qu'il faut y saisir — sinon elle se recopie a la main depuis
    # le compose, et une variable oubliee ne se voit qu'au demarrage.
    # ═══════════════════════════════════════════════════════════════════════
    if len(sys.argv) == 2:
        requises = sorted(variables_du_compose(sys.argv[1]) - FOURNIES_PAR_LE_SCRIPT)
        print("%d variable(s) obligatoire(s) dans %s :" % (len(requises), sys.argv[1]))
        for nom in requises:
            print("    %s" % nom)
        for nom_a, nom_b in COUPLES_IDENTIQUES:
            if nom_a in requises and nom_b in requises:
                print("  %s et %s doivent porter la MEME valeur." % (nom_a, nom_b))
        return 0

    compose, env = sys.argv[1], sys.argv[2]
    if not os.path.exists(env):
        print("fichier d'environnement introuvable : %s" % env, file=sys.stderr)
        return 1

    requises = variables_du_compose(compose) - FOURNIES_PAR_LE_SCRIPT
    presentes = lire_env(env)

    absentes = sorted(v for v in requises if v not in presentes)
    vides = sorted(v for v in requises
                   if v in presentes and not presentes[v].strip())

    # ═══════════════════════════════════════════════════════════════════════
    # UN `$` DANS UNE VALEUR EST LU PAR COMPOSE COMME UNE RÉFÉRENCE.
    #
    # Compose interpole AUSSI le fichier d'environnement. Un mot de passe
    # contenant `$abc` devient une variable nommée `abc`, absente, donc vide :
    #
    #     WARN The "abc" variable is not set. Defaulting to a blank string.
    #
    # Le service part alors avec un mot de passe TRONQUÉ, et échoue à la
    # connexion sur une erreur qui parle d'authentification, pas de `$`.
    # L'échappement est `$$`.
    # ═══════════════════════════════════════════════════════════════════════
    dollars = sorted(nom for nom, valeur in presentes.items()
                     if re.search(r"(?<!\$)\$(?!\$)", valeur))

    for nom_a, nom_b in COUPLES_IDENTIQUES:
        if nom_a in presentes and nom_b in presentes:
            if presentes[nom_a] != presentes[nom_b]:
                absentes.append("%s != %s (elles doivent être IDENTIQUES)"
                                % (nom_a, nom_b))

    if dollars:
        print("ATTENTION : %d valeur(s) contiennent un `$` non échappé — Compose "
              "les lira comme des références de variable :" % len(dollars),
              file=sys.stderr)
        for nom in dollars:
            print("    %s" % nom, file=sys.stderr)
        print("    Doubler le dollar : `$` devient `$$` dans le fichier.",
              file=sys.stderr)
        print(file=sys.stderr)

    if absentes or vides:
        if absentes:
            print("%d variable(s) absente(s) de %s :" % (len(absentes), env),
                  file=sys.stderr)
            for nom in absentes:
                print("    %s" % nom, file=sys.stderr)
        if vides:
            print("%d variable(s) présente(s) mais vide(s) :" % len(vides),
                  file=sys.stderr)
            for nom in vides:
                print("    %s" % nom, file=sys.stderr)
        return 1

    if dollars:
        return 1

    print("%d variable(s) obligatoire(s), toutes renseignées." % len(requises))
    return 0


if __name__ == "__main__":
    sys.exit(main())
