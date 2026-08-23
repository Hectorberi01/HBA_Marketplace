#!/usr/bin/env python3
"""
═══════════════════════════════════════════════════════════════════════════════
TROIS CONTRÔLES NÉS DE LA MÊME SESSION, ET DE LA MÊME CAUSE : DU CODE QUI PARAÎT
CORRECT ET NE FAIT PAS CE QU'IL DIT.

Aucun des trois n'est visible du compilateur. Aucun ne fait échouer un démarrage.
Les trois ont coûté des heures de recherche au mauvais endroit.

───────────────────────────────────────────────────────────────────────────────
1. LES CLÉS D'ENVIRONNEMENT QUI NE LIENT RIEN

    # docker-compose.dev.yml
    OBJECTSTORAGE__ENDPOINT: http://minio:9000
    OBJECTSTORAGE__ACCESSKEY: hba-minio

    // le code
    public const string SectionName = "Media:Storage";
    public string? AccessKeyId { get; set; }

Quatre clés, aucune correspondance : le préfixe attendu était `MEDIA__STORAGE__`
et la propriété s'appelle `AccessKeyId`. `IsConfigured` rendait donc faux, et
media-service basculait sur un stockage EN MÉMOIRE. Toutes les photos produit
vivaient dans un dictionnaire, perdues à chaque redémarrage.

Le service AVERTISSAIT au démarrage. Personne ne lisait la ligne — parce qu'un
avertissement au démarrage se noie dans quarante lignes d'initialisation.

CE CONTRÔLE NE VÉRIFIE PAS LES NOMS DE PROPRIÉTÉS, seulement les RACINES de
sections. Vérifier les propriétés demanderait de résoudre les classes d'options
et leur hiérarchie ; la racine suffit à attraper le cas réel, et ne produit aucun
faux positif.

───────────────────────────────────────────────────────────────────────────────
2. LES GARDES CITÉES DANS UN COMMENTAIRE ET ABSENTES DU CODE

    "_lire": "Les trois routes existaient sous MapAuthenticatedGroup, AVEC
              EnsureSellerAsync QUI VÉRIFIE LA PROPRIÉTÉ"

`EnsureSellerAsync` n'existait NULLE PART. Les trois routes rendaient le chiffre
d'affaires brut, les commissions et le net de n'importe quel vendeur à n'importe
quel compte authentifié.

C'EST PIRE QU'UN SILENCE. Un commentaire qui certifie une garde absente fait
PASSER la relecture : on lit « c'est gardé », on passe à la suite. Deux cas dans
la même session — celui-là, et le routeur Flutter qui affirmait qu'un écran était
« conservé hors routeur » alors que sa route était déclarée trente lignes plus bas.

───────────────────────────────────────────────────────────────────────────────
3. LE CORPS INFÉRÉ SUR UNE MÉTHODE QUI N'EN ACCEPTE PAS

    seller.MapDelete("/me", DeleteAccountAsync);
    ...
    static Task<IResult> DeleteAccountAsync(DeleteAccountRequest request, ...)

ASP.NET refuse d'INFÉRER un corps sur GET, DELETE, HEAD et OPTIONS. La route
compile, et le service refuse de démarrer :

    Body was inferred but the method does not allow inferred body parameters

Deux services l'ont violé — dont un depuis longtemps, sans que personne ne s'en
aperçoive parce qu'il ne démarrait pas pour une AUTRE raison.

RFC 9110 autorise un corps sur DELETE ; ASP.NET refuse de le DEVINER. La
nuance est exactement ce qui rend ce défaut si facile à écrire : le raisonnement
sur le protocole est juste, et l'implémentation ne suit pas. `[FromBody]` le lève.
═══════════════════════════════════════════════════════════════════════════════
"""

from __future__ import annotations

import re
import sys
from pathlib import Path

RACINE = Path(__file__).resolve().parent.parent
STRICT = "--strict" in sys.argv

# Racines fournies par le framework ou l'hôte, jamais déclarées dans le code.
# Les omettre produirait cinq faux positifs à chaque exécution — et un contrôle
# qui crie pour rien finit ignoré, ce qui est le seul échec qui compte.
RACINES_CONNUES = {
    "ASPNETCORE", "DOTNET", "LOGGING", "CONNECTIONSTRINGS",
    "ALLOWEDHOSTS", "URLS", "KESTREL",
}


# CES TROIS RACINES, ET NON « src » — LE CONTRÔLE LISAIT ZÉRO FICHIER.
#
# Ce script vient du monolithe, où tout le C# tenait sous `src/`. Après la
# réorganisation en monorepo, ce dossier n'existe plus : le code vit sous
# `services/`, `shared/` et `apps/`.
#
# Le contrôle ne tombait pas en erreur pour autant — il concluait qu'AUCUNE
# section de configuration n'était déclarée, donc que TOUTES les clés
# d'environnement du compose étaient orphelines. Plus de cent « ❌ » à chaque
# exécution, tous faux, et les deux autres règles (une garde nommée dans un
# commentaire mais absente du code, un corps inféré sur GET/DELETE) ne
# vérifiaient plus rien en silence.
#
# C'est le même défaut que `check-usings.py` avait, et il se voit à la même
# ligne : « 0 fichiers C# analysés » imprimé juste au-dessus du verdict.
RACINES_SOURCES = ("services", "shared", "apps")


def sources_cs() -> list[Path]:
    fichiers: list[Path] = []
    for racine in RACINES_SOURCES:
        dossier = RACINE.joinpath(racine)
        if not dossier.is_dir():
            continue
        fichiers.extend(
            f for f in dossier.rglob("*.cs")
            if "/obj/" not in str(f) and "/bin/" not in str(f)
        )
    return fichiers


# ═════════════════════════════════════════════════════════ 1. clés d'environnement
def racines_declarees(fichiers: list[Path]) -> set[str]:
    """Toutes les racines de section que le code lit, sous les trois formes."""
    racines: set[str] = set()
    motifs = [
        r'SectionName\s*=\s*"([^"]+)"',      # const string SectionName = "A:B"
        r'GetSection\("([^"]+)"\)',           # configuration.GetSection("A:B")
        r'[Cc]onfiguration\["([^"]+)"\]',     # configuration["A:B"]
    ]
    for f in fichiers:
        t = f.read_text(errors="ignore")
        for m in motifs:
            for valeur in re.findall(m, t):
                racines.add(valeur.split(":")[0].upper())
    return racines


def controle_env(fichiers: list[Path]) -> list[str]:
    try:
        import yaml
    except ImportError:
        print("  PyYAML absent — contrôle des clés d'environnement sauté.")
        return []

    compose = RACINE / "docker-compose.dev.yml"
    if not compose.exists():
        return []

    declarees = racines_declarees(fichiers) | RACINES_CONNUES
    d = yaml.safe_load(compose.read_text())

    fautes = []
    for nom, service in (d.get("services") or {}).items():
        for cle in (service.get("environment") or {}):
            if "__" not in cle:
                continue
            racine = cle.split("__")[0].upper()
            if racine not in declarees:
                fautes.append(
                    f"{compose.name} · {nom} · {cle}\n"
                    f"      → aucune section « {racine} » n'est lue par le code. "
                    f"La valeur est ignorée EN SILENCE."
                )
    return fautes


# ═══════════════════════════════════════════════════════════════ 2. gardes citées
def controle_gardes(fichiers: list[Path]) -> list[str]:
    """Une garde nommée dans un commentaire ou une métadonnée doit exister."""
    # Les noms réellement définis, quelle que soit leur visibilité.
    definis: set[str] = set()
    for f in fichiers:
        t = f.read_text(errors="ignore")
        definis |= set(re.findall(r'\b(Ensure\w+Async|Deny\w+Async)\s*\(', t))

    # ON NE CHERCHE QUE DANS LES COMMENTAIRES ET LES CHAÎNES DE MÉTADONNÉES.
    # Une mention dans du code exécutable est déjà vérifiée par le compilateur ;
    # c'est l'affirmation NON COMPILÉE qui peut mentir.
    cites: dict[str, set[str]] = {}
    a_scruter = list(fichiers) + [
        RACINE / "apps/api-gateway/src/HBA.Gateway.Api/appsettings.json"
    ]
    for f in a_scruter:
        if not f.exists():
            continue
        for i, ligne in enumerate(f.read_text(errors="ignore").split("\n"), 1):
            nu = ligne.strip()
            commentaire = nu.startswith("//") or nu.startswith("///") or nu.startswith("*")
            metadonnee = f.suffix == ".json"
            if not (commentaire or metadonnee):
                continue
            for nom in re.findall(r'\b(Ensure\w+Async|Deny\w+Async)\b', ligne):
                if nom not in definis:
                    cites.setdefault(nom, set()).add(
                        f"{f.relative_to(RACINE)}:{i}"
                    )

    return [
        f"« {nom} » est annoncée comme garde mais n'existe nulle part :\n"
        + "\n".join(f"      → {lieu}" for lieu in sorted(lieux))
        for nom, lieux in sorted(cites.items())
    ]


# ═══════════════════════════════════════════════════════════ 3. corps inféré
SANS_CORPS = ("Get", "Delete", "Head", "Options")


def controle_corps_infere(fichiers: list[Path]) -> list[str]:
    fautes = []
    for f in fichiers:
        if "Endpoints" not in f.name:
            continue
        t = f.read_text(errors="ignore")

        for verbe in SANS_CORPS:
            for m in re.finditer(rf'\.Map{verbe}\("([^"]*)",\s*(\w+)\)', t):
                chemin, handler = m.group(1), m.group(2)

                sig = re.search(
                    rf'Task<IResult>\s+{re.escape(handler)}\s*\(([^)]*)\)', t, re.S
                )
                if not sig:
                    continue
                params = sig.group(1)

                # ON CHERCHE UN TYPE « …Request », pas n'importe quel type
                # complexe : `ISender`, `ClaimsPrincipal` et les autres services
                # sont résolus par injection, jamais depuis le corps. Élargir
                # produirait un faux positif sur chaque route.
                for p in re.findall(r'(\w+Request)\s+\w+', params):
                    if "[FromBody]" not in params:
                        fautes.append(
                            f"{f.relative_to(RACINE)} · Map{verbe}(\"{chemin}\") → {handler}\n"
                            f"      → paramètre « {p} » sans [FromBody]. ASP.NET refuse "
                            f"d'inférer un corps sur {verbe.upper()} : le service ne "
                            f"démarrera pas."
                        )
                    break
    return fautes


# ═══════════════════════════════════════════════════════════════════════ sortie
def rapporter(titre: str, fautes: list[str], bloquant: bool) -> int:
    print(f"\n  ── {titre}")
    if not fautes:
        print("     rien à signaler.")
        return 0
    for faute in fautes:
        print(f"     ❌ {faute}")
    return 1 if bloquant else 0


def main() -> int:
    fichiers = sources_cs()
    print(f"  {len(fichiers)} fichiers C# analysés.")

    echecs = 0
    echecs += rapporter(
        "Clés d'environnement sans section correspondante",
        controle_env(fichiers), bloquant=True)
    echecs += rapporter(
        "Gardes annoncées mais inexistantes",
        controle_gardes(fichiers), bloquant=True)
    echecs += rapporter(
        "Corps inféré sur GET / DELETE / HEAD / OPTIONS",
        controle_corps_infere(fichiers), bloquant=True)

    if echecs and STRICT:
        return 1
    if echecs:
        # SANS `--strict`, ON SIGNALE SANS FAIRE ÉCHOUER. C'est la convention
        # des autres vérificateurs du dossier : on veut pouvoir lancer la suite
        # localement sans être arrêté au premier écart.
        print("\n  (relancer avec --strict pour faire échouer la CI)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
