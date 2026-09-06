#!/usr/bin/env python3
"""
LOT D, CORRECTION — LA RESOLUTION PAR NAMESPACE ENGLOBANT, DEUX FOIS.

CE QUI ETAIT CASSE, ET POURQUOI JE NE L'AI PAS VU AVANT DE COPIER.

Le code des adaptateurs vivait dans `HBA.<X>.Contracts.Grpc`. A cet endroit, C#
resolvait DEUX choses gratuitement, par namespace englobant :

  1. `Contracts.IDeliveryModuleApi` — `Contracts` etait trouve comme membre du
     namespace parent `HBA.Deliveries`. Copie dans
     `HBA.FoodOrders.Infrastructure.Grpc.Clients`, le meme mot designe desormais
     `HBA.FoodOrders.Contracts` : UN AUTRE NAMESPACE, DU MEME NOM COURT. D'ou
     CS0234, sur des types qui existent — mais ailleurs.

  2. `MediaView` tout court — present a la fois dans `HBA.Media.Contracts` et
     dans `HBA.Media.Grpc.V1`. Dans le fichier d'origine, le namespace englobant
     `HBA.Media.Contracts` l'emportait sur le `using` du stub. Dans le fichier
     copie, les deux arrivent par `using` : CS0104, ambigu.

C'EST LE MEME PIEGE QUE PENDANT LA MIGRATION KAFKA, et je l'avais deja traite au
lot B — mais seulement pour les types ABSENTS (CS0246), pas pour les types
AMBIGUS. Ajouter le `using` du parent resout le premier cas et CREE le second.

LA CORRECTION, ET POURQUOI ELLE EST DETERMINISTE.

Dans le fichier d'origine, un nom nu present des deux cotes designait TOUJOURS le
contrat — c'est ce que fait la regle du namespace englobant. On peut donc lever
l'ambiguite sans lire le code : tout nom present a la fois dans
`HBA.<X>.Contracts` et dans le `.proto` est prefixe par un alias qui ne peut pas
etre masque, et le qualificateur `Contracts.` est remplace par ce meme alias.

L'alias porte un nom qui n'existe nulle part ailleurs (`ContratsDeliveries`) :
un alias nomme `Contracts` ne servirait a rien, puisque le membre de namespace
est examine AVANT les alias du fichier — c'est precisement ce qui a casse.
"""
import os, re, io, sys, collections

RACINE = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SKIP = {"obj", "bin", ".git", "build", "node_modules"}
SEC = "--ecrire" in sys.argv


def fichiers_cs(base):
    for d, dirs, fs in os.walk(base):
        dirs[:] = [x for x in dirs if x not in SKIP]
        for f in fs:
            if f.endswith(".cs"):
                yield os.path.join(d, f)


def types_des_contrats():
    """namespace de contrats -> noms de types publics."""
    res = collections.defaultdict(set)
    for base in ("shared", "services"):
        for f in fichiers_cs(os.path.join(RACINE, base)):
            s = io.open(f, encoding="utf-8", errors="replace").read()
            m = re.search(r'^namespace\s+([\w\.]+)', s, re.M)
            if not m or not m.group(1).endswith(".Contracts"):
                continue
            s2 = re.sub(r'//.*', '', s)
            for t in re.finditer(
                    r'\b(?:public|internal)\s+(?:sealed\s+|abstract\s+|partial\s+|static\s+|readonly\s+)*'
                    r'(?:class|record|struct|interface|enum)\s+(\w+)', s2):
                res[m.group(1)].add(t.group(1))
    return res


def types_des_protos():
    """csharp_namespace -> noms generes (messages, enums, services + clients)."""
    res = {}
    for d, _, fs in os.walk(os.path.join(RACINE, "shared", "proto")):
        for f in fs:
            if not f.endswith(".proto"):
                continue
            s = io.open(os.path.join(d, f), encoding="utf-8", errors="replace").read()
            s = re.sub(r'//.*', '', s)
            m = re.search(r'option\s+csharp_namespace\s*=\s*"([^"]+)"', s)
            if not m:
                continue
            noms = set(re.findall(r'^\s*message\s+(\w+)', s, re.M))
            noms |= set(re.findall(r'^\s*enum\s+(\w+)', s, re.M))
            noms |= set(re.findall(r'^\s*service\s+(\w+)', s, re.M))
            res[m.group(1)] = noms
    return res


def proto_de(domaine, protos):
    """`HBA.Deliveries` -> le csharp_namespace `HBA.Deliveries.Grpc.V1`."""
    attendu = domaine + ".Grpc.V1"
    return attendu if attendu in protos else None


def main():
    contrats = types_des_contrats()
    protos = types_des_protos()

    cibles = []
    for base in ("services", "apps"):
        for f in fichiers_cs(os.path.join(RACINE, base)):
            if os.sep + "Grpc" + os.sep not in f:
                continue
            if not any(x in f for x in (os.sep + "Clients" + os.sep,
                                        os.sep + "Mappers" + os.sep,
                                        os.sep + "Services" + os.sep)):
                continue
            cibles.append(f)

    total_amb, total_qual, touches = 0, 0, 0
    for f in cibles:
        s = io.open(f, encoding="utf-8").read()
        m = re.search(r'(?:COPIE|DEPLACE) DEPUIS `([\w\.]+)\.Contracts\.Grpc`', s)
        if not m:
            continue
        domaine = m.group(1)                      # HBA.Deliveries
        ns_contrats = domaine + ".Contracts"
        ns_proto = proto_de(domaine, protos)
        if ns_contrats not in contrats:
            print(f"  contrats introuvables pour {ns_contrats} ({os.path.basename(f)})")
            continue

        alias = "Contrats" + domaine.split(".")[-1]
        ambigus = sorted(contrats[ns_contrats] & protos.get(ns_proto, set()))

        avant = s

        # 1. le qualificateur `Contracts.` designait le domaine d'origine
        n_qual = len(re.findall(r'(?<![\w\.])Contracts\.', s))
        if n_qual:
            s = re.sub(r'(?<![\w\.])Contracts\.', alias + ".", s)

        # 2. les noms nus presents des deux cotes : le contrat gagnait
        n_amb = 0
        for nom in ambigus:
            s, k = re.subn(r'(?<![\w\.])' + nom + r'(?![\w])', f"{alias}.{nom}", s)
            n_amb += k

        if s == avant:
            continue

        # l'alias, une seule fois, apres le dernier using
        if f"using {alias} =" not in s:
            usings = list(re.finditer(r'^using\s+[^\n]+;\s*$', s, re.M))
            ligne = (f"using {alias} = {ns_contrats};"
                     "  // alias non masquable : voir tools/migration-grpc/lot_d_resolution.py")
            if usings:
                s = s[:usings[-1].end()] + "\n" + ligne + s[usings[-1].end():]
            else:
                s = ligne + "\n" + s

        # l'alias ne doit pas se retrouver dans sa propre declaration
        s = s.replace(f"using {alias} = {alias}.", f"using {alias} = ")

        touches += 1
        total_qual += n_qual
        total_amb += n_amb
        print(f"  {os.path.relpath(f, RACINE):100} {n_qual:3} qualif. {n_amb:3} ambigus ({len(ambigus)} noms)")
        if SEC:
            io.open(f, "w", encoding="utf-8").write(s)

    print(f"\n{touches} fichiers, {total_qual} qualificateurs `Contracts.`, {total_amb} noms ambigus"
          + ("" if SEC else " — SIMULATION"))


if __name__ == "__main__":
    main()
