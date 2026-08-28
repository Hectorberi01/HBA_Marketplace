-- ═══════════════════════════════════════════════════════════════════════════════
-- CRÉATION DES BASES ET DES RÔLES — PRODUCTION, POSTGRES HORS CLUSTER
--
-- Une base et un rôle par service. À exécuter UNE FOIS sur l'instance de
-- production, avec un compte capable de créer des bases et des rôles :
--
--     psql -h <hôte> -U postgres -f creer-bases.sql
--
-- Rejouable sans dommage : rien n'est supprimé, et un rôle déjà présent GARDE son
-- mot de passe (voir l'encadré du bloc 2).
--
-- ═══ CE QUE CE FICHIER NE FAIT PAS ═══
--
--   • Aucun schéma, aucune table : ce sont les migrations EF, à l'étape de
--     release. Une base créée ici est VIDE, et c'est normal.
--   • Aucune configuration de pg_hba.conf, listen_addresses, pare-feu ou tunnel.
--   • AUCUNE SAUVEGARDE. Une base de production sans PITR est une perte de
--     données en attente.
-- ═══════════════════════════════════════════════════════════════════════════════

\set ON_ERROR_STOP on
\timing off

-- ═══════════════════════════════════════════════════════════════════════════════
-- CONTRÔLE PRÉALABLE — LE CHIFFREMENT SE DÉCIDE À LA CRÉATION DU RÔLE.
--
-- Un rôle créé pendant que `password_encryption` vaut `md5` garde un mot de passe
-- md5 même après bascule du paramètre, et un `pg_hba.conf` en `scram-sha-256` le
-- refusera. L'erreur — « password authentication failed » — ressemble à un mot de
-- passe faux : on le regénère, et l'échec se répète.
--
-- On s'arrête ici plutôt que de créer quatorze rôles inutilisables.
-- ═══════════════════════════════════════════════════════════════════════════════
DO $$
BEGIN
  IF current_setting('password_encryption') <> 'scram-sha-256' THEN
    RAISE EXCEPTION
      'password_encryption vaut « % » et non scram-sha-256. Corriger postgresql.conf, recharger, puis relancer.',
      current_setting('password_encryption');
  END IF;
END $$;

-- ═══════════════════════════════════════════════════════════════════════════════
-- BLOC 1 — LA LISTE, EN UN SEUL ENDROIT
--
-- LES NOMS SUIVENT LE CODE, PAS LES NOMS DE CONTENEUR.
--
-- `payment-service` écrit dans `financial`, `review-service` dans `engagement`,
-- `notification-service` dans `communication`, `seller-service` dans `merchant`.
-- Ces noms viennent du découpage d'origine, que les migrations EF ciblent encore ;
-- les aligner sur les noms de service actuels imposerait de toutes les réécrire.
--
-- `commerce` PORTE DEUX SERVICES — cart et return-refund — dans deux schémas
-- distincts. C'est la seule paire dans ce cas.
--
-- `delivery` et `food` sont créées bien que leurs services soient au lot suivant :
-- une base vide ne coûte rien, et l'oubli se paierait plus tard.
-- ═══════════════════════════════════════════════════════════════════════════════
CREATE TEMP TABLE hba_cibles(nom text PRIMARY KEY);
INSERT INTO hba_cibles(nom) VALUES
  ('identity'), ('user'), ('media'), ('communication'), ('financial'),
  ('promotion'), ('engagement'), ('catalog'), ('commerce'), ('inventory'),
  ('order'), ('merchant'), ('delivery'), ('food');

CREATE TEMP TABLE hba_nouveaux(role text, mot_de_passe text);

-- ═══════════════════════════════════════════════════════════════════════════════
-- BLOC 2 — LES RÔLES
--
-- UN RÔLE DÉJÀ PRÉSENT GARDE SON MOT DE PASSE. C'EST LA PROPRIÉTÉ QUI REND CE
--    FICHIER REJOUABLE SANS COUPER LA PRODUCTION.
--
-- Un `ALTER ROLE ... PASSWORD` inconditionnel donnerait un fichier qui PARAÎT
-- idempotent — mêmes bases, mêmes rôles, aucun DROP — et qui l'est sur la
-- structure, pas sur les identifiants. Le rejouer pour ajouter une seule base
-- régénérerait les quatorze mots de passe, et les services en cours se verraient
-- refuser l'authentification à leur connexion suivante. Une panne totale, causée
-- par un script réputé sans effet.
--
-- Pour régénérer délibérément, voir le bloc 6, commenté.
--
-- `gen_random_uuid()` PLUTÔT QUE `md5(random())` : le second est prévisible —
-- `random()` n'est pas cryptographique et sa graine se devine. Le premier tire du
-- générateur fort de PostgreSQL. Intégré depuis la version 13, aucune extension.
-- ═══════════════════════════════════════════════════════════════════════════════
DO $$
DECLARE
  cible   text;
  secret  text;
BEGIN
  FOR cible IN SELECT nom FROM hba_cibles ORDER BY nom LOOP
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'hba_' || cible) THEN
      secret := replace(gen_random_uuid()::text, '-', '')
             || replace(gen_random_uuid()::text, '-', '');

      -- `format` avec %I et %L, jamais une concaténation : %I cite l'identifiant,
      -- %L cite le littéral. Sans eux, un caractère inattendu dans une valeur
      -- ferait deux requêtes au lieu d'une.
      EXECUTE format('CREATE ROLE %I LOGIN PASSWORD %L', 'hba_' || cible, secret);
      INSERT INTO hba_nouveaux VALUES ('hba_' || cible, secret);
    END IF;
  END LOOP;
END $$;

-- ═══════════════════════════════════════════════════════════════════════════════
-- BLOC 3 — LES BASES
--
-- `CREATE DATABASE` REFUSE D'ÊTRE DANS UNE TRANSACTION, ET IL N'A PAS DE
--    `IF NOT EXISTS`.
--
-- Impossible donc de le mettre dans le bloc DO ci-dessus : PostgreSQL rendrait
-- « CREATE DATABASE cannot run inside a transaction block ». Et un `CREATE
-- DATABASE` nu échouerait au second passage sur « already exists », interrompant
-- le fichier à cause de ON_ERROR_STOP.
--
-- `\gexec` résout les deux : la requête ci-dessous ne CRÉE rien, elle FABRIQUE le
-- texte des commandes manquantes ; `\gexec` exécute ensuite chaque ligne rendue,
-- hors transaction. Zéro ligne rendue = rien à faire, et c'est le cas normal d'un
-- rejeu.
-- ═══════════════════════════════════════════════════════════════════════════════
SELECT format('CREATE DATABASE %I OWNER %I', 'hba_' || nom, 'hba_' || nom)
FROM hba_cibles
WHERE NOT EXISTS (SELECT 1 FROM pg_database WHERE datname = 'hba_' || hba_cibles.nom)
ORDER BY nom;
\gexec

-- Une base déjà présente mais mal attribuée est réalignée — cas d'une base créée
-- à la main avant ce fichier.
SELECT format('ALTER DATABASE %I OWNER TO %I', d.datname, 'hba_' || c.nom)
FROM hba_cibles c
JOIN pg_database d ON d.datname = 'hba_' || c.nom
JOIN pg_roles r ON r.oid = d.datdba
WHERE r.rolname <> 'hba_' || c.nom
ORDER BY c.nom;
\gexec

-- ═══════════════════════════════════════════════════════════════════════════════
-- BLOC 4 — LE CLOISONNEMENT
--
-- SANS CE REVOKE, UN RÔLE PAR SERVICE NE SERT À RIEN.
--
-- PostgreSQL accorde CONNECT à PUBLIC sur toute base nouvellement créée. Les
-- quatorze rôles pourraient donc se connecter aux quatorze bases : un
-- payment-service compromis lirait les jetons d'identity. Créer un rôle par
-- service sans révoquer PUBLIC donne l'apparence du cloisonnement et aucune de
-- ses propriétés.
--
-- `template1` N'EST PAS TOUCHÉE : la révocation porte base par base. La modifier
-- changerait le comportement de TOUTE base créée ensuite sur cette instance, y
-- compris par quelqu'un d'autre et pour un autre usage.
-- ═══════════════════════════════════════════════════════════════════════════════
SELECT format('REVOKE CONNECT ON DATABASE %I FROM PUBLIC', 'hba_' || nom)
FROM hba_cibles ORDER BY nom;
\gexec

SELECT format('GRANT CONNECT ON DATABASE %I TO %I', 'hba_' || nom, 'hba_' || nom)
FROM hba_cibles ORDER BY nom;
\gexec

-- ═══════════════════════════════════════════════════════════════════════════════
-- BLOC 5 — CE QUI A ÉTÉ FAIT, ET CE QUI RESTE À FAIRE DE CES MOTS DE PASSE
--
-- Ils ne s'affichent QU'UNE FOIS, ici. Ils ne sont stockés nulle part en clair —
-- PostgreSQL n'en garde qu'une empreinte SCRAM, irréversible. Perdus, ils se
-- régénèrent (bloc 6) ; ils ne se relisent pas.
-- ═══════════════════════════════════════════════════════════════════════════════
\echo ''
\echo '═══ Rôles créés à ce passage — à recopier MAINTENANT dans le gestionnaire ═══'
SELECT role AS "rôle", mot_de_passe AS "mot de passe" FROM hba_nouveaux ORDER BY role;

\echo ''
\echo '═══ État des quatorze bases ═══'
SELECT c.nom AS service,
       'hba_' || c.nom AS base,
       CASE WHEN d.datname IS NULL THEN 'ABSENTE' ELSE 'présente' END AS etat,
       coalesce(r.rolname, '—') AS proprietaire,
       CASE WHEN has_database_privilege('public', 'hba_' || c.nom, 'CONNECT')
            THEN 'OUVERTE À TOUS' ELSE 'cloisonnée' END AS acces
FROM hba_cibles c
LEFT JOIN pg_database d ON d.datname = 'hba_' || c.nom
LEFT JOIN pg_roles r ON r.oid = d.datdba
ORDER BY c.nom;

\echo ''
\echo 'Aucun schéma ni aucune table n''a été créé : ce sont les migrations.'
\echo 'Aucune sauvegarde n''a été mise en place.'
\echo ''

-- ═══════════════════════════════════════════════════════════════════════════════
-- BLOC 6 — RÉGÉNÉRER LES MOTS DE PASSE (VOLONTAIREMENT COMMENTÉ)
--
-- À DÉCOMMENTER SEULEMENT SI L'ON ACCEPTE DE RECONSTRUIRE LE SECRET KUBERNETES
-- DANS LA FOULÉE. Entre l'exécution de ce bloc et la mise à jour du Secret, les
-- services en cours échouent à s'authentifier.
-- ═══════════════════════════════════════════════════════════════════════════════
-- DO $$
-- DECLARE cible text; secret text;
-- BEGIN
--   FOR cible IN SELECT nom FROM hba_cibles ORDER BY nom LOOP
--     secret := replace(gen_random_uuid()::text, '-', '')
--            || replace(gen_random_uuid()::text, '-', '');
--     EXECUTE format('ALTER ROLE %I PASSWORD %L', 'hba_' || cible, secret);
--     INSERT INTO hba_nouveaux VALUES ('hba_' || cible, secret);
--   END LOOP;
-- END $$;
-- SELECT role, mot_de_passe FROM hba_nouveaux ORDER BY role;
