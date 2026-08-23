"""
Detecte les types utilises sans `using` accessible — la classe d'erreur CS0246 qui
m'a coute trois allers-retours (FindFirstValue, NotificationChannel...).

Heuristique, pas un compilateur : on indexe « type -> namespace » sur tout le
depot, puis pour chaque fichier on verifie que chaque type reference est soit dans
le namespace du fichier, soit dans un namespace ENGLOBANT, soit dans un `using`.
Un namespace FRERE ne compte pas — c'est precisement le piege.
"""
import re, os, sys, collections

# RACINE DEDUITE DE L'EMPLACEMENT DU SCRIPT, JAMAIS D'UN CHEMIN EN DUR.
#
# La premiere version portait le chemin de la machine ou elle a ete ecrite. Elle
# indexait donc 0 fichier partout ailleurs — et ANNONCAIT « aucun type
# inaccessible detecte ». Un controle qui ne trouve rien parce qu'il ne regarde
# rien est pire qu'un controle absent : il rassure.
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SKIP = {'bin','obj','node_modules','.git','.idea','Migrations','_to_delete'}

def cs_files():
    for d, dirs, fs in os.walk(ROOT):
        dirs[:] = [x for x in dirs if x not in SKIP]
        for f in fs:
            if f.endswith('.cs'):
                yield os.path.join(d, f)

# 1. index type -> {namespaces}
index = collections.defaultdict(set)
DECL = re.compile(r'\b(?:public|internal)\s+(?:sealed\s+|static\s+|abstract\s+|partial\s+|readonly\s+)*'
                  r'(?:class|record|struct|interface|enum)\s+(\w+)')
# LES CLASSES STATIQUES SONT INDEXEES A PART, ET C'EST CE QUI REND LE
# CINQUIEME MOTIF UTILISABLE.
#
# Une classe statique ne peut apparaitre QUE sous la forme `Nom.Membre` : jamais
# en type de variable, de parametre, de propriete ni d'argument generique. Son
# nom suivi d'un point est donc forcement un acces de type — la ou n'importe quel
# autre identifiant capitalise suivi d'un point est le plus souvent une propriete
# (`resultat.Error.Code`, `promotion.Status`).
#
# Sans cette restriction, chercher les acces de membre statique produisait des
# centaines de faux positifs. Avec elle : un seul sur tout le depot, et c'etait un
# homonyme reel — voir la garde dans la boucle de detection.
STATIC_DECL = re.compile(r'\b(?:public|internal)\s+static\s+(?:partial\s+)*class\s+(\w+)')
static_index = collections.defaultdict(set)

NS = re.compile(r'^\s*namespace\s+([\w.]+)', re.M)
files = list(cs_files())
for p in files:
    t = open(p, encoding='utf-8', errors='ignore').read()
    m = NS.search(t)
    if not m: continue
    for name in DECL.findall(t):
        index[name].add(m.group(1))
    for name in STATIC_DECL.findall(t):
        static_index[name].add(m.group(1))

# =============================================================================
# LES IDENTIFIANTS QUE CETTE HEURISTIQUE NE PEUT PAS VOIR CORRECTEMENT.
#
# `Program` : chaque API en declare un, sous la forme
# `public partial class Program` — mais dans le namespace GLOBAL, sans
# instruction `namespace`. Or l'indexation ci-dessus saute tout fichier ou
# `NS.search` echoue. Les vingt `Program` du depot sont donc invisibles a
# l'index, et le seul qui y figure est celui de `HBA.Admin.Desktop`, qui a un
# namespace parce que c'est une application de bureau et non une API.
#
# Consequence observee : les treize `WebApplicationFactory<Program>` des projets
# de test etaient signales comme visant le `Program` de la console admin — un
# projet qu'aucun d'eux ne reference. Treize signalements faux sur vingt-sept :
# la moitie de la sortie du controle, et de quoi cesser de la lire.
#
# CE QUE CETTE EXCLUSION NE COUVRE PAS : un vrai `using` manquant sur un type
# nomme `Program`. Le cas n'existe pas — `Program` n'est jamais importe, il est
# resolu dans le namespace global du projet de l'API.
# =============================================================================
INVISIBLES = {'Program'}

def accessible(ns_fichier, usings, ns_type):
    if ns_type in usings: return True
    parts = ns_fichier.split('.')
    return any('.'.join(parts[:i]) == ns_type for i in range(len(parts), 0, -1))

problemes = []
cibles = sys.argv[1:] or []
for p in files:
    rel = os.path.relpath(p, ROOT)
    if cibles and not any(rel.startswith(c) for c in cibles): continue
    t = open(p, encoding='utf-8', errors='ignore').read()
    m = NS.search(t)
    if not m: continue
    ns = m.group(1)
    usings = set(re.findall(r'^\s*using\s+(?:static\s+)?([\w.]+);', t, re.M))
    corps = re.sub(r'//[^\n]*', '', re.sub(r'@?\$?"(?:\\.|[^"\\])*"', '""', t))
    declares = set(DECL.findall(t))
    # ─────────────────────────────────────────────────────────────────────
    # ON NE REGARDE QUE LES POSITIONS DE TYPE.
    #
    # Chercher tout identifiant capitalise produisait 205 signalements sur un
    # depot qui compile : `resultat.Error.Code`, une propriete nommee `Message`,
    # un parametre `email`... Tous des mots courants qui sont AUSSI des types
    # ailleurs. Un outil qui crie 205 fois a tort n'est pas consulte.
    #
    # Les quatre formes ci-dessous sont celles ou un identifiant DOIT designer un
    # type : declaration de parametre, de propriete, `new X(`, argument generique.
    # ─────────────────────────────────────────────────────────────────────
    positions = set()
    positions |= set(re.findall(r'\bnew\s+([A-Z]\w+)\s*[({]', corps))
    positions |= set(re.findall(r'[<,]\s*([A-Z]\w+)\s*[,>]', corps))
    positions |= set(re.findall(r'[(,]\s*([A-Z]\w+\??)\s+[a-z]\w*\s*[,)=]', corps))
    positions |= set(re.findall(r'\b(?:public|private|internal|protected)\s+(?:static\s+|readonly\s+)*([A-Z]\w+\??)\s+\w+\s*\{\s*get', corps))

    # =====================================================================
    # LE `?` EST RETIRE ICI, ET C'EST LA CORRECTION D'UN TROU REEL.
    #
    # Deux des quatre motifs capturent `([A-Z]\w+\??)` — le nom AVEC son point
    # d'interrogation. La boucle, elle, itere sur les noms NORMALISES
    # (`{i.rstrip('?') for i in positions}`) et teste ensuite
    # `if ident not in positions`. Le nom nu n'etant jamais dans l'ensemble
    # brut, ce test etait vrai pour tout type employe UNIQUEMENT en position
    # nullable — et le `continue` qui suit l'ecartait en silence.
    #
    # CE QUE CE TROU A COUTE : `ReturnStatus? status` dans
    # `IReturnRequestRepository`, un CS0246 decouvert par `docker compose build`
    # apres quatre-vingt-deux secondes de compilation — exactement l'aller-retour
    # que ce script existe pour eviter. Le controle avait tourne et annonce
    # « aucun type inaccessible detecte ».
    #
    # Normaliser une fois, ici, aligne les deux ensembles. Mesure sur le depot
    # entier : ZERO nouveau signalement sur du code qui compile, et le CS0246
    # ci-dessus retrouve.
    # =====================================================================
    positions = {i.rstrip('?') for i in positions}

    # ─────────────────────────────────────────────────────────────────────
    # CINQUIEME MOTIF : L'ACCES DE MEMBRE STATIQUE, `Nom.Membre`.
    #
    # Ajoute apres un CS0103 que les quatre motifs precedents ne pouvaient pas
    # voir : `PromotionConstantes.Convertir(...)` n'est ni une declaration, ni un
    # `new`, ni un argument generique — c'est un acces de membre, et le fichier
    # n'avait pas le `using` du namespace FRERE. Exactement la classe d'erreur que
    # ce script existe pour attraper, dans la seule position qu'il ignorait.
    #
    # Restreint aux classes STATIQUES : voir l'encadre de `STATIC_DECL`.
    statiques = set(re.findall(r'(?<![.\w])([A-Z]\w+)\s*\.', corps))

    for ident in {i.rstrip('?') for i in positions} | statiques:
        if ident in declares or ident not in index: continue
        if ident in INVISIBLES: continue

        # POUR UN ACCES DE MEMBRE, IL FAUT QUE LE NOM SOIT UNE CLASSE STATIQUE.
        #
        # Sinon `resultat.Error.Code` ferait remonter le type `Error`, et le
        # controle crierait sur du code parfaitement valide.
        if ident not in positions and ident.rstrip('?') not in static_index: continue

        # ET IL FAUT REGARDER TOUS LES HOMONYMES, PAS SEULEMENT LE STATIQUE.
        #
        # `PageRequest` existe en DEUX exemplaires : une classe statique dans
        # `HBA.Shared.Application.Pagination` et un record dans
        # `HBA.Gateway.Application.Bff.Shared`. Un test de la passerelle importe le
        # second et compile ; ne consulter que l'index statique le signalait a tort.
        # C'etait le seul faux positif du motif, et il suffisait a le disqualifier.
        ns_possibles = index[ident]
        if not any(accessible(ns, usings, n) for n in ns_possibles):
            problemes.append((rel, ident, tuple(sorted(ns_possibles)[:2])))

print(f"{len(files)} fichiers indexes, {len(index)} types connus")
if not problemes:
    print("aucun type inaccessible detecte")
for rel, ident, ns in sorted(set(problemes)):
    print(f"  !! {rel}\n       {ident}  declare dans {ns}")
