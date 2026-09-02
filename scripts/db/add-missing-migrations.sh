#!/usr/bin/env bash
set -uo pipefail

# ═══════════════════════════════════════════════════════════════════════════════
# LES TABLES CONFIGURÉES QU'AUCUNE MIGRATION NE CRÉE.
#
# le contrôle `migrations` les liste. Ce script les rattrape, en laissant
# `dotnet ef` faire le travail.
#
# IL N'A PLUS DE LISTE DE SERVICES, ET C'EST LA CORRECTION D'UN DÉFAUT RÉEL.
#
# Il en tenait une, écrite à la main : cinq services, cinq noms de migration. Le
# jour où catalog-service a gagné trois tables sans migration, le script ne les a
# PAS générées. Il a parcouru ses cinq entrées, annoncé « déjà à jour » cinq fois,
# puis rejoué le contrôle final — qui a affiché les trois tables manquantes et
# rendu 1.
#
# On lisait donc « ❌ aucune migration ne la crée » juste après avoir lancé la
# commande censée les créer. Rien, dans cette sortie, ne disait que le service
# n'était pas dans la liste : le script paraissait avoir travaillé.
#
# La liste vient maintenant de `check-migrations.py --services-en-defaut`. Le
# contrôle qui SAIT quels services sont en défaut est celui qui les nomme.
#
# POURQUOI CE SCRIPT PLUTÔT QU'UNE MIGRATION ÉCRITE À LA MAIN.
#
# Le `Up`/`Down` n'est pas la partie difficile : ce sont des `CreateTable`
# mécaniques. Le piège est le `ModelSnapshot`.
#
# EF ne compare pas le modèle à la BASE, il le compare au SNAPSHOT. Une migration
# posée sans mettre le snapshot à jour EXACTEMENT comme EF l'aurait écrit ne casse
# rien tout de suite : c'est le PROCHAIN `migrations add`, des semaines plus tard,
# qui régénère les mêmes tables ou produit un diff fantôme. Le coût de l'erreur
# est différé et atterrit sur quelqu'un qui n'a pas le contexte.
#
# Les projets Infrastructure ont une `IDesignTimeDbContextFactory` autonome —
# chaîne de connexion par défaut, répartiteur d'événements inerte. AUCUNE BASE
# N'EST CONTACTÉE : `migrations add` ne lit que le modèle. Le script tourne donc
# hors ligne, sans docker, sans postgres.
#
# DEUX PASSES, ET LA SECONDE A ÉTÉ AJOUTÉE APRÈS UN DÉFAUT QUI SERAIT PASSÉ.
#
# La passe 1 traite les TABLES manquantes, listées par `check-migrations.py`.
# Elle ne voit que les tables — c'est sa nature : elle compare des `CreateTable`
# à des `ToTable`.
#
# Le lot 7 a ajouté une COLONNE (`outbox_messages.trace_parent`) sur les quatorze
# services. Aucune table ne manquait : le contrôle a donc affiché
#
#     ✓ Aucun service n'a de table configurée sans migration.
#
# et n'a rien généré. Au premier démarrage, chaque service serait tombé sur
# `42703: column o.trace_parent does not exist` — quatorze services d'un coup, sur
# une commande qui venait d'annoncer que tout allait bien.
#
# La passe 2 ne devine rien : elle demande à EF, qui SAIT.
# `dotnet ef migrations has-pending-model-changes` compare le modèle au snapshot
# et rend 1 s'ils diffèrent, quelle que soit la nature de l'écart — colonne,
# index, contrainte, type. C'est le seul contrôle qui ne peut pas se laisser
# distancer par une évolution du modèle qu'on n'avait pas prévue.
#
# CE QU'IL NE FAIT PAS : LE DÉPLACEMENT DE DONNÉES.
#
# `dotnet ef` produit le schéma. Une colonne qui change de table — nom et
# description de produit passant vers `product_revisions` — demande un INSERT …
# SELECT qu'aucun outil ne devine. Voir
# `services/marketplace/catalog-service/MIGRATION-REVISIONS.md`.
#
# Usage :
#     ./scripts/db/add-missing-migrations.sh
#     ./scripts/db/add-missing-migrations.sh --dry-run
# ═══════════════════════════════════════════════════════════════════════════════

ROOT_DIR="$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)"
DRY_RUN=0
[ "${1:-}" = "--dry-run" ] && DRY_RUN=1

if ! dotnet ef --version >/dev/null 2>&1; then
  echo "❌ L'outil « dotnet ef » est absent."
  echo "   dotnet tool install --global dotnet-ef"
  exit 1
fi

FAILED=0
TRAITES=0

# LE NOM DE LA MIGRATION EST DÉRIVÉ DES TABLES, PAS CHOISI.
#
# Un nom écrit à la main était plus lisible — et c'est ce qui obligeait à tenir la
# liste. `product_revisions,product_conditions` donne
# `AddProductRevisionsProductConditions` : moins élégant, mais exact, et il ne
# peut pas mentir sur ce que la migration contient.
nom_de_migration() {
  local tables="$1"
  local nom="Add"
  local compte=0

  IFS=',' read -ra liste <<< "$tables"
  for table in "${liste[@]}"; do
    if [ "$compte" -ge 4 ]; then
      nom="${nom}EtAutres"
      break
    fi
    # snake_case -> PascalCase
    local pascal
    pascal="$(echo "$table" | awk -F_ '{for (i=1;i<=NF;i++) printf toupper(substr($i,1,1)) substr($i,2)}')"
    nom="${nom}${pascal}"
    compte=$((compte + 1))
  done

  echo "$nom"
}

# ═══════════════════════════════════════════════════════════════════════════════
# L'ESPACE DE NOMS SE LIT SUR L'INSTANTANÉ, IL NE SE DEVINE PAS.
#
# `dotnet ef migrations add` place le fichier généré dans l'espace de noms PAR
# DÉFAUT du projet, c'est-à-dire son nom d'assembly. Cela suppose que le nom du
# projet et l'espace de noms du code coïncident.
#
# CE N'EST PAS VRAI PARTOUT, ET ÇA A CASSÉ LE DÉPÔT.
#
# `HBA.Order.Infrastructure.csproj` contient du code déclaré dans
# `HBA.Orders.Infrastructure` — avec un « s ». L'écart vient de la réorganisation
# `a726cb0`, qui a renommé les projets sans renommer les espaces de noms. Il était
# resté sans conséquence tant que personne ne générait de fichier dans ce projet.
#
# La première migration générée y a déclaré `namespace HBA.Order.Infrastructure.
# Migrations`, créant ainsi un espace de noms `HBA.Order` là où il n'existait que
# `HBA.Orders`. À partir de là, dans tout `HBA.Orders.Infrastructure.*`,
# l'identifiant `Order` ne désignait plus le TYPE `Order` mais ce nouvel espace de
# noms — la résolution parcourt les espaces englobants AVANT les `using` du
# fichier. Quinze erreurs `CS0118: 'Order' est un espace de noms mais est utilisé
# comme un type`, dans des fichiers que personne n'avait touchés.
#
# Un fichier ajouté, aucune ligne modifiée, et un service entier qui ne compile
# plus. On lit donc l'espace de noms sur l'instantané existant, qui est la seule
# source qui dise la vérité sur ce projet.
#
# Absent (tout premier schéma d'un service) : on laisse EF choisir son défaut,
# puisqu'il n'y a rien à contredire.
# ═══════════════════════════════════════════════════════════════════════════════
espace_de_noms() {
  local projet="$1"
  local instantane
  instantane="$(ls "$projet"/Migrations/*ModelSnapshot.cs 2>/dev/null | head -1)"

  [ -n "$instantane" ] || return 1

  sed -n 's/^namespace \([A-Za-z0-9_.]*\).*/\1/p' "$instantane" | head -1
}

# LE PROJET INFRASTRUCTURE EST DÉDUIT, PAS DÉCLARÉ.
#
# Un seul `*.Infrastructure` par service dans ce dépôt. En trouver zéro ou deux
# est une anomalie de structure qu'il vaut mieux signaler que contourner : un
# `head -1` silencieux aurait généré la migration dans le mauvais projet.
projet_infrastructure() {
  local service="$1"
  local trouves
  trouves="$(find "$ROOT_DIR/services/$service/src" -maxdepth 1 -type d -name '*.Infrastructure' 2>/dev/null)"

  local compte
  compte="$(echo "$trouves" | grep -c . || true)"

  if [ "$compte" -ne 1 ]; then
    return 1
  fi

  echo "$trouves"
}

# ═══════════════════════════════════════════════════════════════════════════════
# ON CAPTURE AVANT DE BOUCLER, ET ON VÉRIFIE LE CODE DE SORTIE.
#
# La première version lisait le contrôle par substitution de processus
# (`done < <(python3 …)`). Bash y PERD le code de sortie du producteur : quand la
# commande échouait — chemin faux, python absent, script cassé — la boucle
# recevait zéro ligne, et ce script affichait
#
#     ✓ Aucun service n'a de table configurée sans migration.
#
# c'est-à-dire un succès franc pour « je n'ai rien pu lire ». Exactement le faux
# vert que ce script existe pour empêcher, reproduit dans le script lui-même.
# Trouvé en le simulant depuis un autre dossier.
# ═══════════════════════════════════════════════════════════════════════════════
if ! EN_DEFAUT="$(python3 "$ROOT_DIR/le contrôle `migrations`" --services-en-defaut)"; then
  echo "❌ Le contrôle des migrations n'a pas pu s'exécuter — rien n'a été généré."
  echo "   python3 $ROOT_DIR/le contrôle `migrations` --services-en-defaut"
  exit 1
fi

while IFS=$'\t' read -r service tables; do
  [ -z "$service" ] && continue
  TRAITES=$((TRAITES + 1))

  echo
  echo "── $service"
  echo "   tables sans migration : ${tables//,/, }"

  if ! projet="$(projet_infrastructure "$service")"; then
    echo "   ❌ impossible de désigner un projet *.Infrastructure unique sous services/$service/src"
    FAILED=$((FAILED + 1))
    continue
  fi

  if [ ! -d "$projet/Migrations" ]; then
    echo "   ⓘ premier schéma de ce service : le dossier Migrations sera créé."
  fi

  name="$(nom_de_migration "$tables")"

  # Le projet Infrastructure est SON PROPRE projet de démarrage.
  #
  # Passer l'hôte `*.Api` obligerait à nommer le contexte : `HBA.Financial.Api`
  # en embarque trois (payments, billing, wallet) et `dotnet ef` refuse de
  # choisir. La factory design-time rend l'hôte inutile.
  cmd=(dotnet ef migrations add "$name"
       --project "$projet"
       --startup-project "$projet"
       --output-dir Migrations)

  # Voir l'encadré de `espace_de_noms` : le défaut d'EF est le nom du projet, et
  # il ne coïncide pas partout avec l'espace de noms du code.
  if ns="$(espace_de_noms "$projet")" && [ -n "$ns" ]; then
    cmd+=(--namespace "$ns")
  fi

  echo "   ${cmd[*]}"
  [ "$DRY_RUN" -eq 1 ] && continue

  if "${cmd[@]}"; then
    echo "   ✓ $name"
  else
    echo "   ❌ échec sur $name"
    FAILED=$((FAILED + 1))
  fi
done <<< "$EN_DEFAUT"

echo
if [ "$TRAITES" -eq 0 ]; then
  echo "✓ Aucun service n'a de table configurée sans migration."
else
  if [ "$FAILED" -ne 0 ]; then
    echo "✗ $FAILED migration(s) en échec."
    exit 1
  fi
fi

# ═══════════════════════════════════════════════════════════════════════════════
# PASSE 2 — LES ÉCARTS DE MODÈLE QUE LA PASSE 1 NE PEUT PAS VOIR.
#
# Voir l'encadré d'en-tête. On interroge TOUS les projets Infrastructure, pas
# seulement ceux que la passe 1 a traités : un service dont aucune table ne manque
# peut très bien avoir une colonne, un index ou un type qui a changé.
# ═══════════════════════════════════════════════════════════════════════════════
echo
echo "── Écarts de modèle (colonnes, index, contraintes)"

IGNORES=0

for projet in "$ROOT_DIR"/services/*/*/src/*.Infrastructure; do
  [ -d "$projet" ] || continue

  service="$(echo "$projet" | sed -E "s|^$ROOT_DIR/services/||; s|/src/.*$||")"

  # ═══════════════════════════════════════════════════════════════════════
  # ON N'INTERROGE QUE LES PROJETS QUE LA SOLUTION CONSTRUIT.
  #
  # Le dépôt contient douze projets `*.Infrastructure` qui ne sont PAS dans
  # `HBA.sln` : des coquilles issues de la réorganisation, avec un `.csproj` et
  # ZÉRO fichier `.cs`. Ni DbContext, ni migration, ni schéma.
  #
  # `make build` ne les construit donc jamais, leur `obj/project.assets.json`
  # n'existe pas, et `dotnet ef` rend `NETSDK1004: exécutez une restauration de
  # package`. Le discriminateur les classait — correctement — en « contrôle
  # impossible », et douze échecs faisaient rendre 1 à `make migrations` alors
  # que les dix-neuf VRAIS services venaient d'être traités sans une erreur.
  #
  # POURQUOI L'APPARTENANCE À LA SOLUTION, ET NON « aucun fichier .cs ».
  #
  # Les deux marchent aujourd'hui. Mais un projet peut légitimement n'avoir que
  # des fichiers générés, et surtout : ce qui compte est « ce projet
  # participe-t-il au build ». S'il n'y participe pas, il n'a pas de schéma à
  # migrer, et il n'a pas non plus de raison d'être restauré.
  #
  # Un projet PRÉSENT dans la solution mais non restauré reste, lui, un échec
  # franc — c'est le cas où l'on a vraiment omis de construire avant de migrer,
  # et le taire ferait sauter une migration nécessaire.
  # ═══════════════════════════════════════════════════════════════════════
  if ! grep -q "$(basename "$projet").csproj" "$ROOT_DIR/HBA.sln"; then
    IGNORES=$((IGNORES + 1))
    continue
  fi

  # ═══════════════════════════════════════════════════════════════════════
  # ON DISTINGUE « PAS D'ÉCART » DE « JE N'AI PAS PU REGARDER ».
  #
  # `has-pending-model-changes` rend 0 s'il n'y a rien à faire et 1 s'il y a un
  # écart — mais AUSSI un code non nul si le projet ne compile pas ou si le
  # contexte est introuvable. Le code seul ne suffit donc pas.
  #
  # ON RECONNAÎT L'ÉCHEC D'OUTILLAGE, PAS L'ÉCART. LE DÉFAUT PENCHE DU BON CÔTÉ.
  #
  # La première version faisait l'inverse : elle cherchait « pending » dans la
  # sortie pour reconnaître un écart. EF n'écrit pas ce mot — son message est
  # « Changes have been made to the model since the last migration. » L'injection
  # de faute l'a montré aussitôt : les quinze services étaient déclarés « contrôle
  # impossible » alors qu'ils avaient tous un vrai écart à migrer.
  #
  # Le SENS du test compte autant que le test. Conclure « écart » à tort sur un
  # projet cassé fait échouer le `migrations add` qui suit, bruyamment, et le
  # compteur d'échecs le relève. Se tromper dans l'autre sens fait SAUTER une
  # migration nécessaire en n'affichant qu'un avertissement — et l'on redécouvre
  # la colonne manquante au démarrage, en production.
  # ═══════════════════════════════════════════════════════════════════════
  sortie="$(dotnet ef migrations has-pending-model-changes \
              --project "$projet" --startup-project "$projet" 2>&1)"
  code=$?

  if [ "$code" -eq 0 ]; then
    continue
  fi

  if echo "$sortie" | grep -qiE "unable to|build failed|error [A-Z]+[0-9]+|MSBUILD"; then
    echo "   ❌ $service : contrôle impossible"
    echo "$sortie" | tail -3 | sed 's/^/      /'
    FAILED=$((FAILED + 1))
    continue
  fi

  echo
  echo "── $service : écart de modèle sans migration"

  if [ "$DRY_RUN" -eq 1 ]; then
    echo "   dotnet ef migrations add SyncModel --project $projet"
    TRAITES=$((TRAITES + 1))
    continue
  fi

  # NOM HORODATÉ, PARCE QU'IL N'Y A RIEN À DÉRIVER.
  #
  # La passe 1 nomme d'après les tables manquantes. Ici on ne sait pas CE QUI a
  # changé — seulement qu'il y a un écart. Un nom fixe (`SyncModel`) entrerait en
  # collision au deuxième usage : EF refuse deux migrations de même nom, et le
  # message parle d'un fichier existant sans dire qu'il faut changer le nom.
  name="SyncModel$(date -u +%Y%m%d%H%M%S)"

  ajout=(dotnet ef migrations add "$name"
         --project "$projet" --startup-project "$projet" --output-dir Migrations)

  if ns="$(espace_de_noms "$projet")" && [ -n "$ns" ]; then
    ajout+=(--namespace "$ns")
  fi

  if "${ajout[@]}"; then
    echo "   ✓ $name"
    TRAITES=$((TRAITES + 1))
  else
    echo "   ❌ échec sur $name"
    FAILED=$((FAILED + 1))
  fi
done

echo

# CE QUI EST ÉCARTÉ SE DIT. Un contrôle qui saute des projets en silence
# finit par en sauter un qui comptait, et personne ne s'en aperçoit.
if [ "$IGNORES" -ne 0 ]; then
  echo "ⓘ $IGNORES projet(s) *.Infrastructure hors solution ignoré(s) — coquilles sans schéma."
fi

if [ "$FAILED" -ne 0 ]; then
  echo "✗ $FAILED migration(s) en échec."
  exit 1
fi

if [ "$DRY_RUN" -eq 1 ]; then
  echo "Rien exécuté (--dry-run) — $TRAITES service(s) concerné(s)."
  exit 0
fi

if [ "$TRAITES" -eq 0 ]; then
  echo "✓ Aucun écart de modèle."
  exit 0
fi

# LE CONTRÔLE EST LA SEULE PREUVE QUI COMPTE ICI.
#
# « La commande a rendu 0 » dit que le fichier a été écrit, pas que la table
# manquante y est. On rejoue donc le contrôle qui a motivé le script.
echo "── Vérification"
python3 "$ROOT_DIR/le contrôle `migrations`"
