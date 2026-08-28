#!/usr/bin/env bash
# ═══════════════════════════════════════════════════════════════════════════════
# CRÉATION DES BASES ET DES RÔLES — POSTGRES INSTALLÉ À LA MAIN, HORS CLUSTER.
#
# POURQUOI CE SCRIPT EXISTE, ALORS QUE DEUX MÉCANISMES ÉTAIENT DÉJÀ CENSÉS LE FAIRE.
#
# Les deux échouent, chacun à sa façon, et aucun ne le dit :
#
#   1. `infra/postgres/init/001-create-databases.sql` ne concerne QUE la pile de
#      développement, et il y était inopérant : le compose montait le dossier
#      PARENT sur /docker-entrypoint-initdb.d ; le point d'entrée de l'image
#      postgres ne parcourt que les fichiers du premier niveau et ignore les
#      sous-dossiers. Il lui manquait aussi `hba_promotion` — treize bases pour
#      quatorze. Les deux sont corrigés, et le fichier a quitté `infra/docker/`,
#      retiré du dépôt. CELA NE REMPLACE TOUJOURS PAS CE SCRIPT : en production,
#      Postgres est installé à la main, aucune image ne tourne, et ce fichier-là
#      ne crée ni rôle, ni droit, ni REVOKE.
#
#   2. En développement, ça marche quand même — et c'est ce qui masque tout.
#      `Database.Migrate()` d'EF Core CRÉE la base absente avant d'appliquer les
#      migrations. Le défaut ci-dessus n'a donc jamais eu de symptôme.
#
#      EN PRODUCTION, `MigrateOnStartup` VAUT FAUX (§15). Plus personne ne crée
#      rien, et les treize services échouent au démarrage sur
#      « database does not exist ». C'est le seul environnement où le défaut se
#      voit, et c'est le plus mauvais endroit pour le découvrir.
#
# Le Job Kubernetes `postgres-databases` faisait ce travail, mais il est parti
# avec CloudNativePG le jour où la base a quitté le cluster.
#
# ═══ CE QUE CE SCRIPT FAIT ═══
#
#   • quatorze bases, quatorze rôles, un mot de passe aléatoire par rôle ;
#   • REVOKE CONNECT ... FROM PUBLIC sur chaque base, puis GRANT au seul
#     propriétaire — sans quoi tout rôle de l'instance peut se connecter à toutes
#     les bases, et l'isolation par rôle est décorative ;
#   • une VÉRIFICATION réelle : il se connecte avec chaque rôle, et prouve qu'un
#     rôle étranger est refusé. Un script qui annonce « créé » sans l'éprouver
#     est précisément ce dont ce dépôt se méfie.
#
# ═══ CE QU'IL NE FAIT PAS, ET IL FAUT LE SAVOIR ═══
#
#   • IL NE SUPPRIME RIEN, jamais. Aucun DROP. Rejouable autant qu'on veut.
#   • Il ne crée AUCUN SCHÉMA ni AUCUNE TABLE. Ce sont les migrations EF qui les
#     posent, à l'étape de release. Une base créée ici est vide, et c'est normal.
#   • Il ne configure ni `pg_hba.conf`, ni `listen_addresses`, ni le pare-feu, ni
#     le tunnel. Voir docs/DEPLOIEMENT.md §3.4.
#   • IL NE MET EN PLACE AUCUNE SAUVEGARDE. Une base de production sans PITR est
#     une perte de données en attente — voir §3.10.
#
# ═══ USAGE ═══
#
#   Sur le VPS de base, en tant que postgres :
#       sudo -u postgres ./creer-bases.sh
#
#   À distance, par le tunnel :
#       PGHOST=10.0.0.1 PGUSER=hector PGPASSWORD=... ./creer-bases.sh
#
#   Répétition à blanc, qui n'écrit rien :
#       ./creer-bases.sh --simulation
#
#   Un autre préfixe, si staging et production partagent une instance (§2 exige
#   des bases distinctes par environnement — le préfixe est ce qui les distingue) :
#       HBA_PREFIXE=hba_staging_ ./creer-bases.sh
# ═══════════════════════════════════════════════════════════════════════════════
set -euo pipefail

PREFIXE="${HBA_PREFIXE:-hba_}"
SIMULATION=0
ROTATION=0
for arg in "$@"; do
  case "$arg" in
    --simulation) SIMULATION=1 ;;
    --rotation)   ROTATION=1 ;;
    *) echo "Option inconnue : $arg (attendu : --simulation, --rotation)" >&2; exit 2 ;;
  esac
done

# ── LES QUATORZE BASES ─────────────────────────────────────────────────────────
#
# LES NOMS SUIVENT LE CODE, PAS LES NOMS DE CONTENEUR, ET C'EST DÉLIBÉRÉ.
#
# `payment-service` écrit dans `financial`, `review-service` dans `engagement`,
# `notification-service` dans `communication`, `seller-service` dans `merchant`.
# Ces noms viennent du découpage d'origine, que les migrations EF ciblent encore.
# Les aligner sur les noms de service actuels imposerait de réécrire toutes les
# migrations, pour un gain cosmétique.
#
# `commerce` PORTE DEUX SERVICES : cart-service et return-refund-service, dans
# deux schémas distincts. C'est la seule paire dans ce cas — le §10 dit « une base
# par service », la réalité est « une base par famille de modules ».
#
# `delivery` et `food` sont créées bien que leurs services soient au lot suivant :
# une base vide ne coûte rien, et l'oubli se paierait à un moment où plus personne
# ne pense à cette page.
BASES=(identity user media communication financial promotion engagement
       catalog commerce inventory order merchant delivery food)

# ── Le fichier des mots de passe ───────────────────────────────────────────────
#
# CRÉÉ EN 0600 AVANT D'ÊTRE ÉCRIT, ET NON APRÈS.
#
# `touch` puis `chmod` laisse une fenêtre — courte, mais réelle — pendant laquelle
# le fichier est lisible par tous. `umask` avant la création ferme cette fenêtre.
SORTIE="${HBA_SORTIE:-./motsdepasse-$(date +%Y%m%d-%H%M%S).txt}"

psql_admin() { psql -v ON_ERROR_STOP=1 -qtAX "$@"; }

echo "═══ Bases HBA — préfixe « ${PREFIXE} » ═══"
[ "$SIMULATION" = 1 ] && echo "    SIMULATION : rien ne sera écrit."

# ── GARDE-FOU : SANS DROITS DE CRÉATION, LE SCRIPT ÉCHOUERAIT À MI-CHEMIN ──────
#
# Le vérifier d'abord évite de laisser six bases créées et huit manquantes, état
# dans lequel un rejeu est correct mais où le diagnostic part de la mauvaise
# question — « pourquoi ces six-là seulement ? ».
if ! psql_admin -c "SELECT 1" >/dev/null 2>&1; then
  echo "ÉCHEC : impossible de se connecter. Vérifier PGHOST/PGUSER/PGPASSWORD," >&2
  echo "        ou lancer le script en tant que postgres." >&2
  exit 1
fi

SUPER=$(psql_admin -c "SELECT rolsuper OR rolcreatedb AND rolcreaterole FROM pg_roles WHERE rolname = current_user")
if [ "$SUPER" != "t" ]; then
  echo "ÉCHEC : « $(psql_admin -c 'SELECT current_user')  » ne peut ni créer de base ni créer de rôle." >&2
  exit 1
fi

VERSION=$(psql_admin -c "SHOW server_version_num")
CHIFFREMENT=$(psql_admin -c "SHOW password_encryption")
echo "    serveur $(psql_admin -c 'SHOW server_version'), password_encryption=${CHIFFREMENT}"

# LE CHIFFREMENT DES MOTS DE PASSE SE DÉCIDE À LA CRÉATION, PAS APRÈS.
#
# Un rôle créé pendant que `password_encryption` vaut `md5` garde un mot de passe
# md5 même après bascule du paramètre — et `pg_hba.conf` en `scram-sha-256` le
# refusera. L'erreur, « password authentication failed », ressemble à un mot de
# passe faux : on le regénère, et ça échoue encore.
if [ "$CHIFFREMENT" != "scram-sha-256" ]; then
  echo "ÉCHEC : password_encryption vaut « ${CHIFFREMENT} » et non scram-sha-256." >&2
  echo "        Corriger postgresql.conf, recharger, PUIS relancer ce script —" >&2
  echo "        les rôles créés maintenant porteraient un mot de passe inutilisable." >&2
  exit 1
fi

CREEES=0; EXISTANTES=0; INCHANGES=0; ROTES=0
[ "$SIMULATION" = 0 ] && { umask 077; : > "$SORTIE"; printf '# Bases HBA — %s\n# À recopier dans le gestionnaire de mots de passe, puis SUPPRIMER ce fichier.\n\n' "$(date -Iseconds)" >> "$SORTIE"; }

for nom in "${BASES[@]}"; do
  base="${PREFIXE}${nom}"
  role="${PREFIXE}${nom}"

  existe=$(psql_admin -c "SELECT 1 FROM pg_database WHERE datname = '${base}'")

  if [ "$SIMULATION" = 1 ]; then
    [ -n "$existe" ] && echo "    = ${base} (déjà là)" || echo "    + ${base} + rôle ${role}"
    continue
  fi

  # Le rôle d'abord : une base ne peut appartenir qu'à un rôle existant.
  mdp=$(openssl rand -base64 24 | tr -d '\n')

  # ═══════════════════════════════════════════════════════════════════════════
  # LE MOT DE PASSE PASSE PAR UNE VARIABLE psql, ET PAR L'ENTRÉE STANDARD.
  #
  # Deux pièges, et le second ne se voit qu'à l'exécution :
  #
  #   • Une concaténation shell casserait sur un « ' » sorti d'openssl, et un
  #     « ; » ferait deux requêtes. `:'mdp'` laisse psql citer la valeur.
  #
  #   • `psql -c` N'INTERPOLE PAS LES VARIABLES. La première version de ce script
  #     employait `-c "CREATE ROLE :\"role\" ..."` et échouait sur
  #     « syntax error at or near ":" », dès le premier rôle. L'interpolation
  #     n'a lieu que sur l'entrée standard ou un fichier — d'où ce document
  #     en-ligne, dont les guillemets simples empêchent bash de toucher aux
  #     `:'mdp'` avant que psql ne les voie.
  # ═══════════════════════════════════════════════════════════════════════════
  # ═══════════════════════════════════════════════════════════════════════════
  # UN RÔLE QUI EXISTE DÉJÀ GARDE SON MOT DE PASSE. C'EST LA CORRECTION D'UN
  #    DÉFAUT QUI AURAIT COUPÉ LA PRODUCTION.
  #
  # La première version faisait `ALTER ROLE ... PASSWORD` à chaque passage. Le
  # script paraissait idempotent — mêmes bases, mêmes rôles, aucun DROP — et il
  # l'était sur la structure. Il ne l'était PAS sur les identifiants : le rejouer
  # pour ajouter une seule base régénérait les quatorze mots de passe, et les
  # treize services en cours d'exécution se voyaient refuser l'authentification
  # à leur prochaine connexion. Une panne totale, causée par un script réputé
  # sans effet.
  #
  # Le mot de passe n'est donc posé qu'à la CRÉATION. Le régénérer se demande,
  # explicitement, avec `--rotation` — et le Secret Kubernetes est alors à
  # reconstruire dans la foulée.
  # ═══════════════════════════════════════════════════════════════════════════
  role_existe=$(psql_admin -c "SELECT 1 FROM pg_roles WHERE rolname = '${role}'")

  if [ -z "$role_existe" ]; then
    psql -v ON_ERROR_STOP=1 -qX -v role="${role}" -v mdp="${mdp}" >/dev/null <<'SQL'
CREATE ROLE :"role" LOGIN PASSWORD :'mdp';
SQL
    mdp_connu=1
  elif [ "$ROTATION" = 1 ]; then
    psql -v ON_ERROR_STOP=1 -qX -v role="${role}" -v mdp="${mdp}" >/dev/null <<'SQL'
ALTER ROLE :"role" LOGIN PASSWORD :'mdp';
SQL
    mdp_connu=1
    ROTES=$((ROTES + 1))
  else
    # Le rôle est là et on n'y touche pas : son mot de passe reste celui que le
    # Secret porte déjà. Il n'entre donc pas dans le fichier de sortie.
    mdp_connu=0
    INCHANGES=$((INCHANGES + 1))
  fi

  if [ -z "$existe" ]; then
    # `CREATE DATABASE` refuse d'être dans une transaction : d'où un appel séparé.
    psql -v ON_ERROR_STOP=1 -qX -c "CREATE DATABASE \"${base}\" OWNER \"${role}\"" >/dev/null
    CREEES=$((CREEES + 1)); etat="créée"
  else
    psql -v ON_ERROR_STOP=1 -qX -c "ALTER DATABASE \"${base}\" OWNER TO \"${role}\"" >/dev/null
    EXISTANTES=$((EXISTANTES + 1)); etat="déjà là"
  fi

  # ═══════════════════════════════════════════════════════════════════════════
  # SANS CE REVOKE, L'ISOLATION PAR RÔLE NE VAUT RIEN.
  #
  # PostgreSQL accorde CONNECT à PUBLIC sur toute base nouvellement créée. Les
  # quatorze rôles pourraient donc se connecter aux quatorze bases — un
  # payment-service compromis lirait les jetons d'identity. Créer un rôle par
  # service sans révoquer PUBLIC donne l'apparence du cloisonnement et aucune de
  # ses propriétés.
  #
  # `template1` N'EST PAS TOUCHÉE : la révocation porte sur chaque base, une par
  # une. La modifier changerait le comportement de toute base créée ensuite sur
  # cette instance, y compris par quelqu'un d'autre.
  # ═══════════════════════════════════════════════════════════════════════════
  psql -v ON_ERROR_STOP=1 -qX <<SQL >/dev/null
REVOKE CONNECT ON DATABASE "${base}" FROM PUBLIC;
GRANT  CONNECT ON DATABASE "${base}" TO "${role}";
SQL

  # SUR POSTGRES < 15, `public` EST OUVERT EN ÉCRITURE À TOUS.
  #
  # Le comportement a changé en 15 : PUBLIC n'a plus CREATE sur le schéma public.
  # En dessous, tout rôle pouvant se connecter peut y créer des tables. Comme le
  # CONNECT vient d'être fermé, l'exposition est faible — on la ferme quand même,
  # parce que « faible » se transforme en « nulle » pour une ligne.
  if [ "$VERSION" -lt 150000 ]; then
    psql -v ON_ERROR_STOP=1 -qX -d "${base}" -c "REVOKE CREATE ON SCHEMA public FROM PUBLIC" >/dev/null
  fi

  if [ "$mdp_connu" = 1 ]; then
    printf '%-28s %s\n' "${role}" "${mdp}" >> "$SORTIE"
  else
    etat="${etat}, mot de passe INCHANGÉ"
  fi
  printf "    %-28s %s\n" "${base}" "${etat}"
done

if [ "$SIMULATION" = 1 ]; then
  echo "═══ Simulation terminée — rien n'a été écrit. ═══"
  exit 0
fi

# ═══════════════════════════════════════════════════════════════════════════════
# VÉRIFICATION — ON ÉPROUVE, ON N'ANNONCE PAS.
#
# Deux questions, et la seconde compte autant que la première :
#   1. chaque rôle se connecte-t-il à SA base ?
#   2. est-il REFUSÉ sur celle du voisin ?
#
# Un script qui ne pose que la première laisserait passer un REVOKE oublié, et
# l'on croirait les bases cloisonnées jusqu'au jour d'un audit.
# ═══════════════════════════════════════════════════════════════════════════════
echo "═══ Vérification ═══"
ECHECS=0; EPROUVES=0; NON_EPROUVES=0
TEMOIN=""

for nom in "${BASES[@]}"; do
  base="${PREFIXE}${nom}"; role="${PREFIXE}${nom}"
  mdp=$(awk -v r="${role}" '$1 == r {print $2}' "$SORTIE")

  # ON NE PEUT PAS ÉPROUVER UN MOT DE PASSE QU'ON NE CONNAÎT PAS.
  #
  # Sur un rejeu, les rôles existants gardent le leur : il n'est pas dans le
  # fichier de sortie, donc pas testable ici. Les COMPTER et le DIRE, plutôt que
  # de les sauter en silence — « 14 vérifiés » alors que deux l'ont été serait
  # exactement le genre d'affirmation que ce dépôt traque.
  if [ -z "$mdp" ]; then
    NON_EPROUVES=$((NON_EPROUVES + 1))
    continue
  fi

  if PGPASSWORD="${mdp}" psql -qtAX -h "${PGHOST:-localhost}" -p "${PGPORT:-5432}" \
       -U "${role}" -d "${base}" -c 'SELECT 1' >/dev/null 2>&1; then
    EPROUVES=$((EPROUVES + 1))
    [ -z "$TEMOIN" ] && TEMOIN="${role}"
  else
    echo "    ÉCHEC : ${role} ne peut pas se connecter à ${base}" >&2
    ECHECS=$((ECHECS + 1))
  fi
done

# LE CLOISONNEMENT SE PROUVE PAR UN REFUS, PAS PAR UNE ABSENCE DE TEST.
#
# On prend un rôle dont on connaît le mot de passe et on lui fait viser la base
# d'un AUTRE. Cette connexion doit être refusée ; si elle s'ouvre, le
# REVOKE CONNECT n'a pas pris et les quatorze bases sont ouvertes entre elles.
if [ -n "$TEMOIN" ]; then
  autre=""
  for nom in "${BASES[@]}"; do
    [ "${PREFIXE}${nom}" != "$TEMOIN" ] && { autre="${PREFIXE}${nom}"; break; }
  done
  mdp_t=$(awk -v r="${TEMOIN}" '$1 == r {print $2}' "$SORTIE")
  if PGPASSWORD="${mdp_t}" psql -qtAX -h "${PGHOST:-localhost}" -p "${PGPORT:-5432}" \
       -U "${TEMOIN}" -d "${autre}" -c 'SELECT 1' >/dev/null 2>&1; then
    echo "    ÉCHEC : ${TEMOIN} atteint ${autre} — le REVOKE CONNECT n'a pas pris." >&2
    ECHECS=$((ECHECS + 1))
  else
    echo "    cloisonnement vérifié : ${TEMOIN} est refusé sur ${autre}"
  fi
else
  echo "    cloisonnement NON éprouvé : aucun mot de passe connu à ce passage."
fi

echo "    ${EPROUVES} connexion(s) éprouvée(s), ${NON_EPROUVES} rôle(s) non éprouvé(s) (mot de passe inchangé)."

echo
echo "${CREEES} base(s) créée(s), ${EXISTANTES} déjà présente(s), ${ECHECS} anomalie(s)."
echo "${INCHANGES} rôle(s) au mot de passe inchangé, ${ROTES} régénéré(s)."
if [ "$INCHANGES" -gt 0 ] && [ "$ROTATION" = 0 ]; then
  echo
  echo "Les rôles déjà présents ont GARDÉ leur mot de passe : le Secret Kubernetes"
  echo "en place reste valide. Pour les régénérer — et devoir le reconstruire —"
  echo "relancer avec --rotation."
fi
echo "Mots de passe : ${SORTIE}  (0600)"
echo
echo "À FAIRE MAINTENANT, PENDANT QUE LE FICHIER EXISTE :"
echo "  1. recopier ces mots de passe dans le gestionnaire ;"
echo "  2. construire le Secret Kubernetes (docs/DEPLOIEMENT.md §3.7) ;"
echo "  3. SUPPRIMER ${SORTIE}."
echo
echo "Ce script n'a créé AUCUN schéma ni AUCUNE table : ce sont les migrations."
echo "Il n'a mis en place AUCUNE sauvegarde."

exit $(( ECHECS > 0 ? 1 : 0 ))
