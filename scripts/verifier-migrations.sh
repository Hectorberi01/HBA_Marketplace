#!/usr/bin/env bash
# ═══════════════════════════════════════════════════════════════════════════════
# LE PASSAGE `dotnet ef migrations add`, ET SA SEULE RAISON D'ETRE : PROUVER QUE
# LE DIFF EST VIDE.
#
# CE QUI S'EST PASSE. Quatre entites techniques — journal d'audit, outbox, inbox,
# idempotence — sont descendues de `HBA.Shared.Infrastructure` dans chaque
# service. Les instantanes EF nomment leurs entites PAR CHAINE :
#
#     modelBuilder.Entity("HBA.Shared.Infrastructure.Outbox.OutboxMessage", …)
#
# Ces chaines ont ete realignees a la main sur les nouveaux espaces de noms. Rien
# ne le prouve. Un instantane faux ne casse RIEN : le code compile, les
# migrations deja ecrites s'appliquent par identifiant, le service demarre. Le
# defaut n'apparait qu'au prochain `migrations add`, qui ne retrouve pas les
# anciens types et genere un `DROP TABLE` pour chacun.
#
# ═══════════════════════════════════════════════════════════════════════════════
# CE SCRIPT NE GENERE AUCUNE MIGRATION. IL EN FABRIQUE UNE, LA LIT, LA JETTE.
#
# Pour chaque contexte :
#   1. `migrations add` une migration jetable ;
#   2. lecture de `Up()` et `Down()` — ils DOIVENT etre vides ;
#   3. `migrations remove`, toujours, quel que soit le verdict.
#
# Un `Up()` vide veut dire : le modele et l'instantane disent la meme chose. Un
# `Up()` NON vide est le rapport que l'on cherche — il nomme exactement ce que le
# realignement a manque, et son contenu est affiche tel quel.
#
# `--garder` conserve les migrations non vides pour les relire a froid. Sans ce
# drapeau, RIEN ne subsiste : ce script constate, il ne modifie pas le depot.
#
# CE QU'IL NE FAIT JAMAIS, ET C'EST DELIBERE :
#   · aucun `database update` — il ne touche a AUCUNE base ;
#   · aucun `git commit` ;
#   · il refuse de tourner sur un arbre de travail sale, pour qu'un `git
#     checkout` suffise toujours a revenir en arriere.
#
# CE QU'IL NE COUVRE PAS : que les migrations DEJA ecrites soient justes. Il
# compare le modele a l'instantane, pas l'instantane a une base reelle. Le
# controle `migrations` de `tools/HBA.Controls` fait l'autre moitie.
# ═══════════════════════════════════════════════════════════════════════════════
set -uo pipefail

RACINE="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
NOM="ZZVerificationRealignement"
GARDER=0
FILTRE=""

for arg in "$@"; do
  case "$arg" in
    --garder) GARDER=1 ;;
    --contexte=*) FILTRE="${arg#*=}" ;;
    -h|--help)
      echo "usage: $0 [--garder] [--contexte=<XDbContext>]"
      echo "  --garder     conserve les migrations dont le diff n'est PAS vide"
      echo "  --contexte=  ne verifie qu'un contexte"
      exit 0 ;;
    *) echo "argument inconnu : $arg (voir --help)"; exit 2 ;;
  esac
done

# ── `dotnet` et `dotnet-ef` ────────────────────────────────────────────────────
if ! command -v dotnet >/dev/null 2>&1; then
  echo "❌ dotnet introuvable. Ce script exige le SDK .NET 9."
  exit 1
fi

if ! dotnet ef --version >/dev/null 2>&1; then
  echo "❌ l'outil dotnet-ef n'est pas installe."
  echo "   dotnet tool install --global dotnet-ef --version 9.*"
  exit 1
fi

# ── UN ARBRE SALE REND CE SCRIPT IRREVERSIBLE ─────────────────────────────────
#
# Il ecrit puis retire des fichiers de migration, et `migrations remove` reecrit
# l'instantane. Si d'autres modifications trainent, plus rien ne distingue ce que
# le script a fait de ce qui etait deja la, et `git checkout` devient dangereux.
if [[ -n "$(cd "$RACINE" && git status --porcelain -- '*.cs' 2>/dev/null)" ]]; then
  echo "❌ des fichiers .cs sont modifies et non commites."
  echo "   Ce script ecrit puis retire des migrations : sur un arbre sale, on ne"
  echo "   peut plus revenir en arriere sans risquer d'emporter votre travail."
  echo "   Committez ou remisez d'abord."
  exit 1
fi

# ── L'INVENTAIRE SE DECOUVRE, IL NE SE TIENT PAS A LA MAIN ────────────────────
#
# Un tableau ecrit en dur est exactement ce qui a casse `check-all.sh` : un
# contexte ajoute et non inscrit ne serait jamais verifie, et le script rendrait
# vert sans l'avoir vu. Chaque `*ModelSnapshot.cs` designe son contexte, son
# projet et son dossier de sortie.
INVENTAIRE="$(cd "$RACINE" && python3 - <<'PYEOF'
import io, os, re
RAC = os.getcwd()
for b, d, fs in os.walk(RAC):
    if any(x in b for x in ("/bin/", "/obj/", "/node_modules/", "/clients/", "/.git")):
        d[:] = []
        continue
    for f in fs:
        if not f.endswith("ModelSnapshot.cs"):
            continue
        s = io.open(os.path.join(b, f), encoding="utf-8", errors="ignore").read()
        m = re.search(r"\[DbContext\(typeof\((\w+)\)\)\]", s)
        if not m:
            continue
        projet = b
        while projet != RAC and not any(x.endswith(".csproj") for x in os.listdir(projet)):
            projet = os.path.dirname(projet)
        csproj = next(x for x in os.listdir(projet) if x.endswith(".csproj"))

        # une fabrique design-time evite de demarrer l'hote — sinon il faut un
        # projet de demarrage, et `EnvironnementDeploiement` exige alors un
        # environnement explicite (voir plus bas).
        fabrique = any(
            ("IDesignTimeDbContextFactory<%s>" % m.group(1))
            in io.open(os.path.join(r, x), encoding="utf-8", errors="ignore").read()
            for r, _, xs in os.walk(projet)
            if "/bin/" not in r and "/obj/" not in r
            for x in xs if x.endswith(".cs"))

        print("\t".join([
            m.group(1),
            os.path.relpath(os.path.join(projet, csproj), RAC),
            os.path.relpath(b, projet).replace(os.sep, "/"),
            "1" if fabrique else "0",
        ]))
PYEOF
)"

if [[ -z "$INVENTAIRE" ]]; then
  echo "❌ aucun *ModelSnapshot.cs trouve — ce script n'a RIEN pu verifier."
  echo "   Rendre 0 ici serait un vert silencieux : voir l'encadre de Depot.cs."
  exit 1
fi

# ── LES TROIS CONTEXTES SANS FABRIQUE DESIGN-TIME ─────────────────────────────
#
# Sans fabrique, `dotnet ef` DEMARRE l'hote pour obtenir le contexte. Il traverse
# alors `EnvironnementDeploiement`, qui lit la CLE DE CONFIGURATION
# `ASPNETCORE_ENVIRONMENT` et considere son absence comme la PRODUCTION — a
# dessein, fail-closed. En production, la verification des secrets refuse le
# demarrage, et `migrations add` echoue sur « Security:SecretProtection:Key est
# absente », un message qui n'a rien a voir avec les migrations.
#
# C'est exactement ce qui avait fait tomber les 41 tests de la passerelle. On
# pose donc l'environnement, et une chaine de connexion factice : `migrations
# add` construit le modele, il ne contacte AUCUNE base.
export ASPNETCORE_ENVIRONMENT=Development
export ConnectionStrings__Default="Host=localhost;Port=5432;Database=hba_migrations_verification;Username=postgres;Password=postgres"

# LE NETTOYAGE, PAR DIFFERENCE ET PAR git.
#
# `dotnet ef migrations remove` a echoue a chaque execution — il reconstruit le
# projet, et un projet que la migration vient de casser ne se reconstruit pas.
# Demander a l'outil de defaire ce qu'il a fait suppose qu'il en soit encore
# capable. On ne le suppose plus : on retire les fichiers APPARUS, et on rend
# l'instantane a git.
nettoyer() {
  local apres
  apres="$(ls "$PROJET_DIR/$SORTIE" 2>/dev/null | sort)"
  comm -13 <(echo "$AVANT") <(echo "$apres") | while read -r nouveau; do
    [[ -n "$nouveau" ]] && rm -f "$PROJET_DIR/$SORTIE/$nouveau"
  done
  (cd "$RACINE" && git checkout -- "$(dirname "$CSPROJ")/$SORTIE" 2>/dev/null) || true
}

VUS=0; VIDES=0; PLEINS=0; ECHECS=0
declare -a A_REVOIR=()

while IFS=$'\t' read -r CONTEXTE CSPROJ SORTIE FABRIQUE; do
  [[ -z "$CONTEXTE" ]] && continue
  if [[ -n "$FILTRE" && "$CONTEXTE" != "$FILTRE" ]]; then continue; fi

  VUS=$((VUS + 1))
  PROJET_DIR="$RACINE/$(dirname "$CSPROJ")"
  printf '── %-26s ' "$CONTEXTE"

  # DEUX TABLEAUX, ET C'EST LA CORRECTION D'UN DEFAUT REEL.
  #
  # `migrations remove` N'ACCEPTE PAS `--output-dir` : reutiliser le tableau de
  # `add` faisait echouer TOUS les `remove`, et ce script laissait trente
  # fichiers derriere lui apres avoir promis que rien ne subsisterait. Un
  # verificateur qui modifie le depot est pire que pas de verificateur.
  COMMUN=(--project "$RACINE/$CSPROJ" --context "$CONTEXTE")

  # Sans fabrique, il faut un projet de demarrage : l'hote qui monte ce contexte.
  if [[ "$FABRIQUE" == "0" ]]; then
    API="$(cd "$RACINE" && ls -d "$(dirname "$CSPROJ")"/../*.Api 2>/dev/null | head -1)"
    if [[ -n "$API" ]]; then
      COMMUN+=(--startup-project "$RACINE/$API")
    fi
  fi

  # `--namespace` EXPLICITE. Sans lui, EF derive l'espace de noms du NOM DU
  # PROJET et du dossier de sortie : `HBA.Order.Infrastructure.Migrations` la ou
  # le code vit dans `HBA.Orders.*`. Ce seul fichier cree un espace de noms
  # `HBA.Order` qui masque le TYPE `Order` dans tout le service — CS0118 en
  # cascade. On lit l'espace de noms sur l'instantane, qui est la reference.
  NS="$(grep -m1 "^namespace" "$PROJET_DIR/$SORTIE/${CONTEXTE}ModelSnapshot.cs" 2>/dev/null | awk '{print $2}' | tr -d ';')"
  ARGS=("${COMMUN[@]}" --output-dir "$SORTIE")
  [[ -n "$NS" ]] && ARGS+=(--namespace "$NS")

  # CE QUI EXISTE AVANT. Le nettoyage se fera par difference, pas en demandant
  # a `dotnet ef` de defaire son propre travail.
  AVANT="$(ls "$PROJET_DIR/$SORTIE" 2>/dev/null | sort)"

  JOURNAL="$(mktemp)"
  if ! dotnet ef migrations add "$NOM" "${ARGS[@]}" >"$JOURNAL" 2>&1; then
    echo "ECHEC de migrations add"
    sed 's/^/      /' "$JOURNAL" | tail -12
    rm -f "$JOURNAL"
    # `add` peut avoir ecrit AVANT d'echouer : on nettoie meme sur ce chemin.
    nettoyer
    ECHECS=$((ECHECS + 1))
    A_REVOIR+=("$CONTEXTE (la commande a echoue)")
    continue
  fi
  rm -f "$JOURNAL"

  FICHIER="$(ls -t "$PROJET_DIR/$SORTIE"/*_"$NOM".cs 2>/dev/null | head -1)"
  if [[ -z "$FICHIER" ]]; then
    echo "migration introuvable apres add — RIEN n'a pu etre lu"
    ECHECS=$((ECHECS + 1))
    A_REVOIR+=("$CONTEXTE (migration introuvable)")
    continue
  fi

  # Le corps de `Up()` et `Down()`, commentaires et blancs retires. Une migration
  # a diff vide ne porte que les signatures.
  CORPS="$(python3 - "$FICHIER" <<'PYEOF'
import io, re, sys
s = io.open(sys.argv[1], encoding="utf-8").read()
s = re.sub(r"/\*.*?\*/", "", s, flags=re.S)
s = re.sub(r"//[^\n]*", "", s)
corps = []
for methode in ("Up", "Down"):
    m = re.search(r"protected override void %s\(MigrationBuilder \w+\)\s*\{" % methode, s)
    if not m:
        continue
    p, i = 1, m.end()
    while i < len(s) and p:
        if s[i] == "{": p += 1
        elif s[i] == "}": p -= 1
        i += 1
    corps.append(s[m.end():i - 1].strip())
print("\n".join(x for x in corps if x))
PYEOF
)"

  if [[ -z "$CORPS" ]]; then
    echo "diff VIDE — modele et instantane concordent"
    VIDES=$((VIDES + 1))
    nettoyer
  else
    echo "diff NON VIDE — le realignement a manque quelque chose"
    echo "$CORPS" | sed 's/^/      /' | head -30
    PLEINS=$((PLEINS + 1))
    A_REVOIR+=("$CONTEXTE")
    if [[ "$GARDER" == "1" ]]; then
      echo "      conservee : ${FICHIER#$RACINE/}"
    else
      nettoyer
    fi
  fi
done <<< "$INVENTAIRE"

# ── DERNIERE GARDE : LE SCRIPT NE LAISSE RIEN DERRIERE LUI ────────────────────
#
# `migrations remove` peut echouer pour d'autres raisons que la mienne. On
# verifie ce qui reste sur le disque plutot que de croire les codes de retour :
# c'est la difference entre « j'ai demande le nettoyage » et « le depot est
# propre ».
RESTES="$(cd "$RACINE" && find . -name "*_$NOM*" -not -path "*/bin/*" -not -path "*/obj/*" 2>/dev/null)"
if [[ -n "$RESTES" && "$GARDER" != "1" ]]; then
  echo
  echo "❌ DES MIGRATIONS DE VERIFICATION SONT RESTEES SUR LE DISQUE :"
  echo "$RESTES" | sed 's/^/   /'
  echo
  echo "   git checkout -- . && git clean -f -- \"*_$NOM*\""
  echo "   ramene le depot a son etat d'avant. Ne poussez rien avant."
  exit 1
fi

echo
echo "═══════════════════════════════════════════════════════════════════════════"
echo "$VUS contexte(s) : $VIDES a diff vide, $PLEINS a diff non vide, $ECHECS en echec."

if ((PLEINS == 0 && ECHECS == 0)); then
  echo
  echo "LES INSTANTANES SONT JUSTES. Un migrations add ne generera aucun"
  echo "DROP TABLE : le realignement des espaces de noms est prouve, plus"
  echo "seulement plausible."
  exit 0
fi

echo
echo "A REVOIR :"
for c in "${A_REVOIR[@]}"; do echo "   · $c"; done
echo
echo "NE POUSSEZ PAS ET NE DEPLOYEZ PAS TANT QUE CETTE LISTE N'EST PAS VIDE."
echo "Un diff non vide sur ces entites signifie que l'instantane ne decrit plus"
echo "le modele : la premiere migration generee apres le deploiement emporterait"
echo "l'outbox, l'inbox, le journal d'audit ou l'idempotence."
exit 1
