#!/usr/bin/env bash
set -uo pipefail

# ═══════════════════════════════════════════════════════════════════════════
# LES CONTRÔLES QUI SE PAIENT EN SECONDES PLUTÔT QU'EN CONSTRUCTIONS.
#
# Chacun est né d'une panne réelle, et chacun attrape une classe d'erreurs que
# le compilateur ne voit pas :
#
#   • check-refs.py        une `ProjectReference` dont la cible n'existe plus.
#                          MSBuild n'en fait qu'un AVERTISSEMENT, puis échoue
#                          plus loin sur des `using` — le message d'erreur
#                          désigne alors des espaces de noms, jamais la ligne
#                          fautive. Les projets de test ne sont dans aucune
#                          solution : `check-solution.py` ne peut pas les voir.
#   • check-di.py          une interface injectée que personne ne fournit.
#   • check-usings.py      un type référencé sans `using` accessible — un namespace
#                          FRÈRE ne compte pas, et c'est le piège qui a coûté trois
#                          allers-retours de compilation.
#                          Le service compile, le conteneur refuse de démarrer,
#                          et l'exception arrive par paquets tronqués.
#
#   • check-braces.py      une accolade manquante ou en trop. Le compilateur le
#                          dit aussi — mais après avoir restauré et compilé tout
#                          l'amont, et en désignant une ligne qui n'est pas la
#                          bonne. Il vérifie AUSSI que l'indentation d'une
#                          déclaration correspond à sa profondeur réelle : c'est
#                          ce qui attrape le cas où le compte tombe juste et où la
#                          méthode suivante s'est retrouvée DANS la précédente.
#
#   • check-dockerfiles.py un projet atteint transitivement mais non copié dans
#                          l'image. La restauration réussit, la compilation
#                          tombe sur un namespace introuvable.
#
#   • check-migrations.py  une migration écrite en regardant une base existante,
#                          rejouée sur une base neuve. « column does not exist ».
#
#   • chaînes de connexion un installeur hérité du monolithe qui réclame encore
#                          « Marketplace » là où le compose fournit « Default ».
#                          Le service démarre, puis lève.
#
#   • check-config-and-guards.py
#                          TROIS défauts qui se ressemblent : du code qui paraît
#                          correct et ne fait pas ce qu'il dit.
#                          — une clé d'environnement dont aucune section ne
#                            correspond : media-service a tourné en MÉMOIRE tout
#                            un développement, `OBJECTSTORAGE__*` ne liant rien ;
#                          — une garde nommée dans un commentaire et absente du
#                            code : trois lectures financières restaient ouvertes
#                            derrière une note qui certifiait le contraire ;
#                          — un corps inféré sur GET/DELETE, qu'ASP.NET refuse.
#
#   • check-service-addresses.py
#                          un client gRPC dont l'adresse n'est déclarée nulle
#                          part. Le service ne DÉMARRE pas — et la pile désigne
#                          le contrat partagé, jamais le compose qu'il faut
#                          corriger. engagement-service en a fait les frais.
#
#   • check-kafka-topics.py
#                          trois nommages de sujets Kafka qui ne se parlaient pas.
#                          Un événement publié sur un sujet auquel personne ne
#                          s'abonne ne lève rien : il est acquitté, et il disparaît.
#                          Le contrôle rapproche `HbaTopics`, les `KAFKA__PRODUCER`
#                          du compose et les manifestes d'overlay.
#
#   • check-infra.py       l'infrastructure est le seul code que personne
#                          n'exécute en boucle : un module Terraform mal câblé se
#                          découvre le jour où l'on provisionne. Faute
#                          d'identifiants OVH, ce contrôle remplace le
#                          `terraform plan` qu'on ne peut pas lancer.
#                          Il couvre aussi l'export OTLP de chaque service du
#                          compose : sans adresse, la télémétrie est coupée
#                          SANS erreur, et treize services sur quatorze l'ont
#                          été pendant des semaines sans que rien ne le dise.
#
#   • check-grpc-stubs.py / check-event-consumers.py
#                          CES DEUX-LÀ N'ONT RIEN REGARDÉ PENDANT TOUTE LEUR
#                          VIE. Ils balayaient `<dépôt>/src`, chemin hérité du
#                          monolithe et inexistant ici. `os.walk` sur un dossier
#                          absent ne lève pas : il n'itère pas. Les deux
#                          affichaient donc un compteur à ZÉRO, lu comme « rien à
#                          signaler », alors qu'il voulait dire « rien regardé ».
#                          Derrière ce zéro dormaient trois clients gRPC
#                          entièrement bouchonnés dans return-refund-service.
#                          Les racines viennent désormais de
#                          `scripts/racines_source.py`, qui LÈVE sur un dossier
#                          déclaré absent — un contrôle qui ne trouve pas ses
#                          fichiers sort en code 2 au lieu de rassurer.
#
# À lancer AVANT `./scripts/dev-up.sh`, qui dure une demi-heure.
# ═══════════════════════════════════════════════════════════════════════════

ROOT_DIR="$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)"

# ═══════════════════════════════════════════════════════════════════════════════
# L'INTERPRÉTEUR PYTHON DES CONTRÔLES.
#
# `./scripts/preparer-outils.sh` pose un environnement virtuel dans `.venv/`,
# parce que le Python de Homebrew refuse les installations globales (PEP 668).
# S'il existe, on l'emploie ; sinon on retombe sur le `python3` du PATH.
#
# POURQUOI CE CHOIX EST FAIT ICI, ET NON LAISSÉ À CELUI QUI LANCE.
#
# Sans cette ligne, il faudrait penser à `source .venv/bin/activate` avant chaque
# exécution. Personne n'y pense à tous les coups — et l'oubli ne casse rien : les
# contrôles qui ont besoin de PyYAML s'annoncent simplement « ignorés ». Un
# contrôle silencieusement sauté est exactement ce que ce fichier existe pour
# empêcher.
# ═══════════════════════════════════════════════════════════════════════════════
if [ -x "$ROOT_DIR/.venv/bin/python3" ]; then
  PYTHON="$ROOT_DIR/.venv/bin/python3"
else
  PYTHON="python3"
  if ! python3 -c "import yaml" 2>/dev/null; then
    echo "  PyYAML absent et aucun .venv — plusieurs contrôles seront ignorés." >&2
    echo "  Poser l'environnement une fois : ./scripts/preparer-outils.sh" >&2
    echo >&2
  fi
fi
FAILED=0

# LE NOMBRE DE CONTRÔLES SE COMPTE, IL NE S'ÉCRIT PAS.
#
# La ligne finale annonçait « les dix contrôles » alors qu'il y en avait onze :
# un contrôle ajouté sans toucher au texte, et le texte a menti sans que rien ne
# le dise. C'est le même défaut, en miniature, que celui qu'attrapent les scripts
# appelés ici — une seconde source de vérité qui cesse de correspondre.
TOTAL=0

run() {
  local label="$1"; shift
  TOTAL=$((TOTAL + 1))
  echo
  echo "── $label"
  if "$@"; then
    return 0
  fi
  FAILED=$((FAILED + 1))
}

# ── Quatrième contrôle, tenu ici parce qu'il tient en trois lignes ─────────
#
# UN SEUL NOM DE CHAÎNE DE CONNEXION : « Default ».
#
# Le compose ne renseigne que `CONNECTIONSTRINGS__DEFAULT`. Un module hérité du
# monolithe demandait encore « Marketplace » — nom qui avait survécu au
# déménagement. Le service compilait, démarrait, puis levait sur une clé que
# personne n'avait eu l'intention de fournir. C'était le dernier des dix-huit.
check_connection_strings() {
  local offenders
  offenders=$(grep -rn 'GetConnectionString("' --include='*.cs' \
                "$ROOT_DIR/services" 2>/dev/null \
              | grep -v '/obj/\|/bin/' \
              | grep -v 'GetConnectionString("Default")' || true)

  if [ -z "$offenders" ]; then
    echo "  Tous les installeurs lisent « Default »."
    return 0
  fi

  echo "$offenders" | sed "s|$ROOT_DIR/||" | sed 's/^/  ❌ /'
  return 1
}

# EN PREMIER, PARCE QU'UNE SOLUTION INCOHÉRENTE NE COMPILE RIEN.
#
# `HBA.sln` a cassé le build avec MSB5023 — vingt lignes d'imbrication laissées
# derrière des projets retirés — pendant que les quinze autres contrôles
# passaient. Zéro fichier C# en cause. Voir l'en-tête de check-solution.py.
run "Cohérence de la solution"     "$PYTHON" "$ROOT_DIR/scripts/check-solution.py"

# ═══════════════════════════════════════════════════════════════════════════════
# JUSTE APRÈS LA SOLUTION, ET C'EST VOLONTAIRE : LES DEUX SE COMPLÈTENT.
#
# `check-solution.py` vérifie `HBA.sln` — que chaque projet listé existe, qu'aucun
# GUID n'est orphelin. Il ne voit QUE ce que la solution liste, et les projets de
# TEST n'y sont pas.
#
# C'est exactement l'espace par lequel le défaut du 28 août est passé : le retrait
# de dispatch, tracking et proof (D42, D43) a laissé trois `ProjectReference`
# mortes dans `HBA.Delivery.UnitTests`, que rien ne regardait. MSBuild rend alors
# un simple AVERTISSEMENT MSB9008 puis échoue sur les `using` en CS0234 — cinq
# erreurs qui parlent d'espaces de noms, et la vraie cause en warning au milieu.
#
# Celui-ci part des `.csproj` du DISQUE, sans passer par aucune solution.
# ═══════════════════════════════════════════════════════════════════════════════
run "Références de projet"         "$PYTHON" "$ROOT_DIR/scripts/check-refs.py"
run "Structure des fichiers C#"    "$PYTHON" "$ROOT_DIR/scripts/check-braces.py"
run "Dépendances non résolues"     "$PYTHON" "$ROOT_DIR/scripts/check-di.py"          "$@"
run "Types hors portée"            "$PYTHON" "$ROOT_DIR/scripts/check-usings.py"      "$@"
run "Fermeture des Dockerfiles"    "$PYTHON" "$ROOT_DIR/scripts/check-dockerfiles.py" "$@"
run "Migrations à froid"           "$PYTHON" "$ROOT_DIR/scripts/check-migrations.py"  "$@"

# APRÈS LES MIGRATIONS, PARCE QU'IL EN DÉPEND. Un contexte qui déclare
# `KeepsAuditTrail => true` sans que la table existe ne casse ni la compilation ni
# le démarrage : il casse le PREMIER GESTE MÉTIER. Voir l'en-tête du script.
run "Journal d'audit"              "$PYTHON" "$ROOT_DIR/scripts/check-audit-trail.py"
run "Chaînes de connexion"         check_connection_strings
run "Configuration et gardes"      "$PYTHON" "$ROOT_DIR/scripts/check-config-and-guards.py" "$@"
run "Adresses de service"          "$PYTHON" "$ROOT_DIR/scripts/check-service-addresses.py"

# CINQ ENDROITS À TENIR D'ACCORD pour qu'un service soit joignable. Le manque
# de l'un donne 503 ou 404 sur une configuration qui a l'air complète — c'est
# arrivé quatre fois. Voir l'en-tête du script.
run "Cohérence de la passerelle"   "$PYTHON" "$ROOT_DIR/scripts/check-gateway.py"

# UNE PERMISSION QUE PERSONNE N'INTERROGE EST UN DROIT SANS EFFET.
#
# Sept des cinquante-sept permissions du catalogue vendeur n'étaient exigées par
# aucune route : attribuées aux rôles, affichées au vendeur, cochables — et sans
# le moindre effet. Rien ne casse, personne ne se plaint, et le vendeur croit
# avoir restreint son équipe.
#
# Le contrôle refuse aussi un code de permission recopié en chaîne littérale :
# une faute de frappe y compile, et la garde refuse alors TOUT LE MONDE. Voir
# l'en-tête du script — il s'est lui-même trompé sur ce point à sa première
# exécution, et c'est écrit là.
run "Permissions vendeur"          "$PYTHON" "$ROOT_DIR/scripts/check-permissions.py"

# UNE INTERFACE QUI CHANGE LAISSE SES DOUBLES DE TEST DERRIÈRE ELLE.
#
# Ajouter une BORNE à une méthode de dépôt casse toutes ses implémentations.
# Celles du code de production se voient — on vient de les écrire. Celles des
# TESTS, non : classes enfouies au bas d'un fichier de test, dont les méthodes
# lèvent `NotSupportedException` parce qu'aucun test ne les appelle.
#
# Le compilateur les attrape, mais seulement au build : trois allers-retours
# pour le même défaut en une seule séance. Ce contrôle l'ANTICIPE en deux
# secondes. Il compare le NOM et l'ARITÉ, pas la signature complète — un type
# changé à arité constante lui échappe, et c'est assumé : voir son en-tête.
run "Implémentations d'interface" "$PYTHON" "$ROOT_DIR/scripts/check-implementations.py"

# TROIS ENDROITS NOMMAIENT LES SUJETS KAFKA, ET AUCUN NE SE PARLAIT (ISSUE-001).
#
# Le producteur dérivait son sujet de `SERVICE_NAME`, le consommateur s'abonnait à
# une liste écrite en dur, et `k8s/overlays/*/kafka-topics.yaml` provisionnait un
# TROISIÈME schéma que pas une ligne de code ne connaissait. Rien ne casse : le
# message part, le courtier l'acquitte, et il n'arrive nulle part.
#
# `HbaTopics` a fermé les deux premières. Ce contrôle empêche la troisième de
# re-diverger — et signale un service qui publie sans être au catalogue, ce qui
# revient au même défaut par une autre porte.
run "Sujets Kafka"                 "$PYTHON" "$ROOT_DIR/scripts/check-kafka-topics.py"

# LA RÈGLE ADDITIVE DES CONTRATS D'ÉVÉNEMENTS (D32).
#
# Renommer un champ d'événement compile. Le supprimer compile. Et rien ne casse
# à l'exécution : `JsonSerializer` lit ce qu'il reconnaît, ignore le reste, et
# rend un objet aux champs manquants à `null`. Le gestionnaire s'exécute sur une
# charge amputée, écrit un effet faux, et la seule trace est un span vert.
#
# Ce contrôle compare les contrats à un instantané versionné : il ne rend pas la
# rupture impossible, il la rend VISIBLE en revue.
run "Contrats d'événements"         "$PYTHON" "$ROOT_DIR/scripts/check-event-contracts.py"

# CELUI-CI CONSTRUIT VRAIMENT LES OVERLAYS KUSTOMIZE.
#
# Lire `k8s/base/` ne prouve rien : c'est l'overlay qui décide, et un patch peut
# défaire en silence ce que la base garantissait. Le contrôle vérifie sur le
# RÉSULTAT : non-root, les trois sondes, requests/limits, pas de `latest` en
# production, deny-by-default présent, aucun secret en clair.
#
# Non bloquant si `kustomize` est absent : ce n'est pas une dépendance de
# compilation, et faire échouer le lot d'un développeur qui ne déploie pas serait
# le meilleur moyen de faire ignorer les six autres.
run "Manifests Kubernetes"         "$PYTHON" "$ROOT_DIR/scripts/check-k8s.py" "$@"

# UN WORKFLOW MAL FORME NE SE PLAINT PAS — IL NE TOURNE PAS.
#
# GitHub n'execute pas un workflow dont le YAML est invalide : aucune execution,
# aucune notification, aucun statut sur la PR. On croit la CI verte alors qu'elle
# n'a jamais demarre. Le defaut rencontre en ecrivant `ci.yml` : un `- name:`
# contenant « : » sans guillemets.
run "Workflows GitHub"             "$PYTHON" "$ROOT_DIR/scripts/check-workflows.py"

# L'INFRASTRUCTURE EST LE SEUL CODE QUE PERSONNE N'EXÉCUTE EN BOUCLE.
#
# Un service cassé se voit au premier `dotnet build`. Un module Terraform cassé
# se voit le jour où l'on provisionne — sous pression, souvent par quelqu'un qui
# ne l'a pas écrit. Faute d'identifiants OVH, ce dépôt ne peut lancer ni
# `terraform plan` ni `ansible-playbook` : ce contrôle en est le substitut, et il
# vérifie ce qui se vérifie sans fournisseur — le câblage des modules, les
# variables non fournies, l'état distant, les handlers Ansible jamais appelés.
#
# Non bloquant si `python-hcl2` ou PyYAML manquent : voir check-k8s.py, même
# raison.
run "Infrastructure (Terraform, Ansible, Compose)" "$PYTHON" "$ROOT_DIR/scripts/check-infra.py"

# INFORMATIF, ET NON BLOQUANT — d'où l'absence de `--strict`.
#
# Les consommateurs manquants ne cassent aucun démarrage : ils rendent des
# événements silencieusement inertes. La liste demande un tri humain (certains
# modules ne sont pas encore extraits), pas un échec de CI.
#
# « Informatif » ne veut PAS dire « ne peut pas échouer » : une racine de code
# introuvable sort en code 2 et fait rougir ce lot. C'est la seule chose que ce
# contrôle refuse désormais de laisser passer en silence.
run "Consommateurs d'événements"   "$PYTHON" "$ROOT_DIR/scripts/check-event-consumers.py"

# INFORMATIF AUSSI — un bouchon peut être délibéré tant que personne ne
# l'appelle depuis un autre service. C'est la LISTE qui compte : elle dit ce que
# la couche synchrone promet sans le tenir.
#
# Le garde-fou qui BLOQUE, lui, n'est pas ici : il est dans l'installeur du
# module concerné, qui refuse de démarrer en production avec un adaptateur
# simulé (voir `ReturnRefundModuleInstaller` et `PaymentsModuleInstaller`). Un
# script d'inventaire ne sait pas qui appelle quoi ; l'installeur, si.
#
# Comme ci-dessus : une racine introuvable sort en code 2, et là ce lot rougit.
run "Bouchons gRPC"                "$PYTHON" "$ROOT_DIR/scripts/check-grpc-stubs.py"

# UN RPC APPELÉ SANS CORPS DE SERVEUR REND `UNIMPLEMENTED`, ET RIEN NE LE DIT.
#
# Deux fois en une journée, dont une qui tuait tout le parcours repas :
# `DeliveryApi.LookupQuote`, appelé par les deux checkouts, et
# `OrderApi.ListOrdersBySeller`, appelé à chaque commande confirmée — laissant
# `SalesCount` à zéro pour tous les vendeurs, c'est-à-dire le défaut même que son
# handler avait été écrit pour fermer.
#
# Les deux compilent. Les deux passaient les vingt autres contrôles. `protoc`
# génère une base serveur dont les membres non surchargés lèvent À L'EXÉCUTION :
# il n'existait aucun moment, entre l'éditeur et la production, où quelque chose
# s'en apercevait.
run "RPC gRPC sans serveur"        "$PYTHON" "$ROOT_DIR/scripts/check-grpc-rpc.py"

# UNE TABLE D'AUTORISATIONS QUI NE SUIT PAS LE CODE REDEVIENT « TOUT LE MONDE
# PEUT TOUT ».
#
# `AutorisationsGrpc` restreint chaque hôte aux RPC qu'il appelle réellement —
# `RefundPayment` passe ainsi de vingt-quatre appelants possibles à un. Les deux
# dérives possibles n'ont pas le même symptôme, et c'est la seconde qui est
# dangereuse :
#
#   • un appel non autorisé casse en production, bruyamment, au premier appel ;
#   • une autorisation sans appel ne casse RIEN, jamais — elle s'accumule
#     jusqu'à ce que la table autorise tout, c'est-à-dire jusqu'à ce qu'elle ne
#     serve plus à rien.
#
# Ce contrôle refuse les deux, et vérifie en prime que chaque
# `Internal__ServiceName` posé dans un compose désigne un hôte connu — un nom
# mal orthographié fermerait un service entier.
run "Autorisations gRPC"           "$PYTHON" "$ROOT_DIR/scripts/check-autorisations-grpc.py"

echo
if [ "$FAILED" -eq 0 ]; then
  echo "✓ Les $TOTAL contrôles passent."
else
  echo "✗ $FAILED contrôle(s) en échec — voir ci-dessus."
fi

exit "$FAILED"
