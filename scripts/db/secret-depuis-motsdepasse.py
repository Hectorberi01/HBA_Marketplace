#!/usr/bin/env python3
# Construit le Secret Kubernetes `hba-platform` a partir du fichier de mots de
# passe produit par creer-bases.sh.
#
# Ce qui etait casse : la seule facon documentee de fabriquer ce Secret etait de
# recopier quatorze mots de passe a la main dans treize chaines de connexion.
# Une recopie manuelle se trompe, et surtout elle fait transiter les mots de
# passe par le presse-papier, l'historique du shell, et — c'est arrive le
# 28 aout 2026 — par la sortie d'un terminal.
#
# Ce qui est choisi : le script lit le fichier, construit les chaines, et ecrit
# directement le Secret. Aucune valeur n'est affichee : la sortie ne contient
# que des noms de cles et des longueurs.
#
# Ce que ce choix NE couvre PAS :
#   - le fichier de sortie contient les secrets EN CLAIR. Il est en 0600 et hors
#     du depot, mais il doit etre supprime apres `kubectl apply`.
#   - le script ne verifie aucune connexion. Que le mot de passe soit le bon,
#     seul Postgres le dira.
#   - les roles hba_delivery et hba_food n'ont pas de cle : leurs lots ne sont
#     pas deployes. Quand ils le seront, il faudra les ajouter a CLES ET dans
#     k8s/base/common/secret.yaml.

import base64
import json
import os
import re
import secrets
import shutil
import stat
import subprocess
import sys

HOTE = os.environ.get("PGHOST_CLUSTER", "10.20.0.2")
PORT = os.environ.get("PGPORT_CLUSTER", "5432")

# LE NAMESPACE N'EST PAS `hba`.
#
# `k8s/base/namespaces/namespace.yaml` declare bien `hba`, mais chaque overlay le
# renomme : `hba-prod`, `hba-staging`, `hba-dev`. La premiere version de ce script
# ecrivait `namespace: hba` en dur, et `kubectl apply` repondait
# « namespaces "hba" not found » — un message clair. La variante silencieuse est
# pire : si un namespace `hba` existait, le Secret y serait pose sans erreur, et
# les pods de `hba-prod` demarreraient en CreateContainerConfigError sur un Secret
# introuvable, loin de la cause.
NAMESPACE = os.environ.get("HBA_NAMESPACE", "hba-prod")

NOM_SECRET = "hba-platform"

RACINE = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
GABARIT = os.path.join(RACINE, "k8s", "base", "common", "secret.yaml")

# cle du Secret -> base de donnees. Le role porte toujours le nom de sa base :
# c'est ce que creer-bases.sh garantit, et ce sur quoi repose le cloisonnement.
# Mettre Username=hector ici rendrait le cloisonnement decoratif : le
# superutilisateur passe partout, et le REVOKE CONNECT ne protegerait plus rien.
CLES = [
    ("CONNECTIONSTRINGS__IDENTITY", "hba_identity"),
    ("CONNECTIONSTRINGS__USER", "hba_user"),
    ("CONNECTIONSTRINGS__MEDIA", "hba_media"),
    ("CONNECTIONSTRINGS__NOTIFICATION", "hba_communication"),
    ("CONNECTIONSTRINGS__PAYMENT", "hba_financial"),
    ("CONNECTIONSTRINGS__PROMOTION", "hba_promotion"),
    ("CONNECTIONSTRINGS__REVIEW", "hba_engagement"),
    ("CONNECTIONSTRINGS__CATALOG", "hba_catalog"),
    ("CONNECTIONSTRINGS__CART", "hba_commerce"),
    ("CONNECTIONSTRINGS__INVENTORY", "hba_inventory"),
    ("CONNECTIONSTRINGS__ORDER", "hba_order"),
    ("CONNECTIONSTRINGS__SELLER", "hba_merchant"),
    # returnrefund partage la base de cart : memes identifiants, schemas distincts.
    ("CONNECTIONSTRINGS__RETURNREFUND", "hba_commerce"),
]

# ═══════════════════════════════════════════════════════════════════════════════
# LES CLES QUI NE VIENNENT PAS DE POSTGRES — ET POURQUOI ELLES SONT ICI.
#
# `kubectl apply -f` REMPLACE la carte `data` en entier. Un fichier qui ne porte
# que les treize chaines de connexion n'ajoute pas quatorze cles a un Secret
# existant : il en fait un Secret de quatorze cles, et EFFACE les autres.
#
# Ce que ca aurait donne : `AUTHENTICATION__SIGNINGKEY` disparue, donc toutes les
# sessions en cours invalidees d'un coup ; `INTERNAL__APIKEY` disparue, donc tous
# les appels entre services refuses ; et surtout
# `SECURITY__SECRETPROTECTION__KEY` disparue — celle-la protege des donnees au
# repos, et une donnee chiffree avec une cle qu'on a perdue ne se rechiffre pas,
# elle se perd.
#
# Le script porte donc TOUTES les cles vivantes de `secret.yaml`, et refuse
# d'ecrire s'il en manque une. L'ordre de resolution, pour chacune :
#   1. la valeur deja posee dans le cluster, si le Secret existe — on la reprend
#      telle quelle. C'est ce qui rend le script rejouable sans rien casser.
#   2. la variable d'environnement du meme nom, si elle est posee.
#   3. une valeur engendree — annoncee comme telle, en toutes lettres.
# ═══════════════════════════════════════════════════════════════════════════════

def engendrer_base64_48():
    return base64.b64encode(secrets.token_bytes(48)).decode("ascii")


def engendrer_hex_32():
    return secrets.token_hex(32)


AUTRES_CLES = {
    # Redis tourne dans le cluster : le nom du Service suffit, ce n'est pas un secret.
    "REDIS__CONNECTIONSTRING": lambda: "redis:6379",
    "AUTHENTICATION__SIGNINGKEY": engendrer_base64_48,
    "INTERNAL__APIKEY": engendrer_hex_32,
    "SECURITY__SECRETPROTECTION__KEY": engendrer_hex_32,
}

# Perdre celle-ci rend illisible ce qu'elle a chiffre. Elle merite un mot a part.
CLES_IRREMPLACABLES = {"SECURITY__SECRETPROTECTION__KEY"}


def lire_motsdepasse(chemin):
    """Retourne {role: motdepasse}. Format : deux colonnes separees par des espaces."""
    mdp = {}
    with open(chemin, encoding="utf-8") as f:
        for ligne in f:
            ligne = ligne.rstrip("\n")
            if not ligne.strip() or ligne.lstrip().startswith("#"):
                continue
            champs = ligne.split(None, 1)
            if len(champs) != 2:
                print("ligne ignoree (pas deux colonnes) : %d caractere(s)" % len(ligne),
                      file=sys.stderr)
                continue
            mdp[champs[0].strip()] = champs[1].strip()
    return mdp


def lire_cles_declarees():
    """Les cles vivantes de secret.yaml — la liste de reference.

    Elle est lue, pas recopiee : une cle ajoutee au gabarit sans l'etre ici doit
    faire echouer le script, pas passer inapercue.
    """
    if not os.path.exists(GABARIT):
        return None
    declarees = []
    with open(GABARIT, encoding="utf-8") as f:
        for ligne in f:
            if ligne.lstrip().startswith("#"):
                continue
            m = re.match(r"^  ([A-Z][A-Z0-9_]*)\s*:", ligne)
            if m:
                declarees.append(m.group(1))
    return declarees


def valeurs_du_cluster():
    """Les valeurs deja posees, pour les reprendre telles quelles.

    Sans kubectl, ou si le Secret n'existe pas encore, on rend une carte vide :
    ce n'est pas une erreur, c'est le premier deploiement.
    """
    if not shutil.which("kubectl"):
        return {}, "kubectl absent"
    try:
        r = subprocess.run(
            ["kubectl", "-n", NAMESPACE, "get", "secret", NOM_SECRET, "-o", "json"],
            capture_output=True, text=True, timeout=30)
    except (OSError, subprocess.SubprocessError) as e:
        return {}, "kubectl a echoue (%s)" % type(e).__name__
    if r.returncode != 0:
        return {}, "aucun Secret %s dans %s" % (NOM_SECRET, NAMESPACE)
    try:
        data = json.loads(r.stdout).get("data") or {}
    except ValueError:
        return {}, "reponse kubectl illisible"
    valeurs = {}
    for cle, encodee in data.items():
        try:
            valeurs[cle] = base64.b64decode(encodee).decode("utf-8")
        except (ValueError, UnicodeDecodeError):
            # Une valeur binaire ne se reconstruit pas ici ; on la reprend telle
            # quelle, encodee, pour ne pas la perdre.
            valeurs[cle] = None
    return valeurs, "Secret %s lu dans %s" % (NOM_SECRET, NAMESPACE)


def main():
    if len(sys.argv) < 2:
        print("usage: secret-depuis-motsdepasse.py <fichier-motsdepasse> [sortie.yaml]",
              file=sys.stderr)
        return 2

    source = sys.argv[1]
    sortie = sys.argv[2] if len(sys.argv) > 2 else os.path.join(
        os.path.expanduser("~"), "secrets-hba-prod", "secret-hba-platform.yaml")

    if not os.path.exists(source):
        print("introuvable : %s" % source, file=sys.stderr)
        return 1

    # Le fichier source doit etre en 0600. S'il ne l'est pas, c'est que quelqu'un
    # d'autre a pu le lire : on refuse plutot que de propager le probleme.
    mode = stat.S_IMODE(os.stat(source).st_mode)
    if mode & 0o077:
        print("REFUS : %s est en %o, attendu 0600." % (source, mode), file=sys.stderr)
        return 1

    declarees = lire_cles_declarees()
    if declarees is None:
        print("introuvable : %s — impossible de savoir quelles cles le Secret porte"
              % GABARIT, file=sys.stderr)
        return 1

    mdp = lire_motsdepasse(source)
    print("%d role(s) lu(s) dans %s" % (len(mdp), os.path.basename(source)))
    print("%d cle(s) declaree(s) par k8s/base/common/secret.yaml" % len(declarees))

    existantes, comment = valeurs_du_cluster()
    print("Etat du cluster : %s" % comment)

    connexions = dict(CLES)
    anomalies = []
    valeurs = {}
    origine = {}

    for cle in declarees:
        if cle in connexions:
            base = connexions[cle]
            secret = mdp.get(base)
            if secret is None:
                anomalies.append("%s : aucun mot de passe pour le role %s" % (cle, base))
                continue
            # Un point-virgule ou un guillemet couperait la chaine de connexion en
            # deux : Npgsql lirait une cle tronquee et un parametre inconnu.
            if ";" in secret or "'" in secret or '"' in secret:
                anomalies.append("%s : le mot de passe de %s contient ; ou un guillemet, "
                                 "il casserait la chaine de connexion" % (cle, base))
                continue
            valeurs[cle] = "Host=%s;Port=%s;Database=%s;Username=%s;Password=%s" % (
                HOTE, PORT, base, base, secret)
            origine[cle] = "mot de passe (%s)" % base
        elif cle == "CONNECTIONSTRINGS__DEFAULT":
            # Vide a dessein : aucun service ne doit retomber sur une base par defaut.
            valeurs[cle] = ""
            origine[cle] = "vide, volontairement"
        elif os.environ.get(cle):
            # L'ENVIRONNEMENT PASSE AVANT LE CLUSTER, ET C'EST DELIBERE.
            #
            # La premiere version reprenait d'abord la valeur du cluster. Poser
            # `export AUTHENTICATION__SIGNINGKEY=...` pour faire tourner la cle
            # n'aurait alors rien fait : le script aurait garde l'ancienne, en
            # annoncant « REPRISE », et on aurait cru la rotation faite.
            # Une intention explicite l'emporte sur un etat herite.
            valeurs[cle] = os.environ[cle]
            if existantes.get(cle) and existantes[cle] != os.environ[cle]:
                origine[cle] = "environnement — REMPLACE la valeur du cluster"
            else:
                origine[cle] = "variable d'environnement"
        elif cle in existantes and existantes[cle]:
            valeurs[cle] = existantes[cle]
            origine[cle] = "REPRISE du cluster"
        elif cle in AUTRES_CLES:
            valeurs[cle] = AUTRES_CLES[cle]()
            origine[cle] = "ENGENDREE MAINTENANT"
        else:
            anomalies.append(
                "%s : declaree dans secret.yaml, mais ce script ne sait pas d'ou "
                "elle vient — l'ajouter a AUTRES_CLES ou la poser en variable "
                "d'environnement" % cle)

    if anomalies:
        for a in anomalies:
            print("  ANOMALIE " + a, file=sys.stderr)
        print("%d anomalie(s) : rien n'a ete ecrit." % len(anomalies), file=sys.stderr)
        return 1

    lignes = [
        "# Genere par scripts/db/secret-depuis-motsdepasse.py — NE PAS COMMITER.",
        "# Applique avec :  kubectl -n %s apply -f %s" % (NAMESPACE, os.path.basename(sortie)),
        "# Puis SUPPRIMER ce fichier.",
        "apiVersion: v1",
        "kind: Secret",
        "metadata:",
        "  name: %s" % NOM_SECRET,
        "  namespace: %s" % NAMESPACE,
        "type: Opaque",
        "data:",
    ]
    for cle in declarees:
        encodee = base64.b64encode(valeurs[cle].encode("utf-8")).decode("ascii")
        lignes.append("  %s: %s" % (cle, encodee))

    os.makedirs(os.path.dirname(sortie), exist_ok=True)
    # Ouverture en 0600 des la creation : pas de fenetre ou le fichier est lisible.
    fd = os.open(sortie, os.O_WRONLY | os.O_CREAT | os.O_TRUNC, 0o600)
    with os.fdopen(fd, "w", encoding="utf-8") as f:
        f.write("\n".join(lignes) + "\n")

    print("ecrit : %s (0600), %d cle(s)" % (sortie, len(declarees)))
    for cle in declarees:
        print("    %-34s %4d car.  %s" % (cle, len(valeurs[cle]), origine[cle]))

    engendrees = [c for c in declarees if origine[c] == "ENGENDREE MAINTENANT"]
    if engendrees:
        print()
        print("%d cle(s) engendree(s) a l'instant — elles n'existaient pas avant :"
              % len(engendrees))
        for c in engendrees:
            print("    " + c)
        perdues = [c for c in engendrees if c in CLES_IRREMPLACABLES]
        if perdues and existantes:
            print()
            print("ARRET DE LECTURE. Le Secret existe deja dans le cluster et ne portait")
            print("pas ces cles-ci :")
            for c in perdues:
                print("    " + c)
            print("Si des donnees ont ete chiffrees avec une valeur precedente, une")
            print("nouvelle cle ne les dechiffrera pas. Verifier avant d'appliquer.")


    remplacees = [c for c in declarees
                  if origine[c] == "environnement — REMPLACE la valeur du cluster"]
    irremplacables = [c for c in remplacees if c in CLES_IRREMPLACABLES]
    if irremplacables:
        print()
        print("ARRET DE LECTURE. Ces cles avaient une AUTRE valeur dans le cluster :")
        for c in irremplacables:
            print("    " + c)
        print("Ce qu'elles ont chiffre ne se dechiffrera pas avec la nouvelle.")
        print("Si ce n'est pas une rotation voulue, retirer la variable et relancer.")

    inutilises = sorted(set(mdp) - {b for _, b in CLES})
    if inutilises:
        print("%d role(s) sans cle (lots non deployes) : %s"
              % (len(inutilises), ", ".join(inutilises)))
    print()
    print("Aucune valeur n'a ete affichee. Le fichier de sortie, lui, est en clair :")
    print("  1. kubectl -n %s apply -f %s" % (NAMESPACE, sortie))
    print("  2. supprimer %s ET %s" % (sortie, source))
    return 0


if __name__ == "__main__":
    sys.exit(main())
