-- ═══════════════════════════════════════════════════════════════════════════════
-- CRÉATION DES BASES ET DES RÔLES — VERSION pgAdmin
--
-- POURQUOI UNE SECONDE VERSION.
--
-- `creer-bases.sql` emploie `\set`, `\gexec` et `\echo` : ce sont des commandes
-- DU CLIENT psql, pas du langage SQL. pgAdmin envoie le texte tel quel au
-- serveur, qui ne les connaît pas et répond « syntax error at or near "\" ».
-- Ce fichier n'en contient aucune.
--
-- ═══ IL SE JOUE EN TROIS ENVOIS, ET LA PARTIE 2 EST PARTICULIÈRE ═══
--
-- `CREATE DATABASE` REFUSE D'ÊTRE DANS UNE TRANSACTION. Or pgAdmin envoie tout
-- l'éditeur en UNE requête, et une requête à plusieurs instructions est une
-- transaction implicite. Deux CREATE DATABASE dans le même envoi donnent donc :
--
--     ERROR: CREATE DATABASE cannot run inside a transaction block
--
-- La partie 2 se joue instruction par instruction : sélectionner UNE ligne dans
-- l'éditeur, puis F5. pgAdmin n'exécute alors que la sélection. Quatorze fois.
--
-- SI CELA VOUS PARAÎT LONG — et c'est le cas — `psql` fait le tout en une
-- commande, sur le VPS où PostgreSQL est déjà installé :
--
--     psql -U postgres -f creer-bases.sql
-- ═══════════════════════════════════════════════════════════════════════════════


-- ═══════════════════════════════════════════════════════════════════════════════
-- PARTIE 1 — LES RÔLES.  Tout sélectionner jusqu'à la fin de la partie, puis F5.
--
-- Un rôle déjà présent GARDE son mot de passe : rejouer cette partie pour ajouter
-- un rôle ne périme pas les identifiants des autres. Un `ALTER ROLE ... PASSWORD`
-- inconditionnel donnerait un script qui paraît sans effet et qui couperait
-- l'authentification de tous les services en cours.
--
-- Les mots de passe s'affichent À LA FIN DE CETTE PARTIE, une seule fois.
-- PostgreSQL n'en garde qu'une empreinte SCRAM : ils se régénèrent, ils ne se
-- relisent pas. Les recopier avant de passer à la suite.
-- ═══════════════════════════════════════════════════════════════════════════════

CREATE TEMP TABLE hba_nouveaux(role text, mot_de_passe text);

DO $$
DECLARE
  cible  text;
  secret text;
  liste  text[] := ARRAY['identity','user','media','communication','financial',
                         'promotion','engagement','catalog','commerce','inventory',
                         'order','merchant','delivery','food'];
BEGIN
  IF current_setting('password_encryption') <> 'scram-sha-256' THEN
    RAISE EXCEPTION
      'password_encryption vaut « % » et non scram-sha-256. Un rôle créé maintenant garderait un mot de passe md5 que pg_hba.conf refusera. Corriger postgresql.conf, recharger, puis relancer.',
      current_setting('password_encryption');
  END IF;

  FOREACH cible IN ARRAY liste LOOP
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'hba_' || cible) THEN
      secret := replace(gen_random_uuid()::text, '-', '')
             || replace(gen_random_uuid()::text, '-', '');
      EXECUTE format('CREATE ROLE %I LOGIN PASSWORD %L', 'hba_' || cible, secret);
      INSERT INTO hba_nouveaux VALUES ('hba_' || cible, secret);
    END IF;
  END LOOP;
END $$;

SELECT role AS "rôle", mot_de_passe AS "mot de passe — À RECOPIER MAINTENANT"
FROM hba_nouveaux ORDER BY role;


-- ═══════════════════════════════════════════════════════════════════════════════
-- PARTIE 2 — LES BASES.  UNE LIGNE À LA FOIS : sélectionner la ligne, puis F5.
--
-- Les exécuter ensemble échoue — voir l'encadré d'en-tête. Une ligne rejouée sur
-- une base existante rend « database already exists » : c'est sans dommage, on
-- passe à la suivante.
-- ═══════════════════════════════════════════════════════════════════════════════

CREATE DATABASE hba_identity      OWNER hba_identity;
CREATE DATABASE hba_user          OWNER hba_user;
CREATE DATABASE hba_media         OWNER hba_media;
CREATE DATABASE hba_communication OWNER hba_communication;
CREATE DATABASE hba_financial     OWNER hba_financial;
CREATE DATABASE hba_promotion     OWNER hba_promotion;
CREATE DATABASE hba_engagement    OWNER hba_engagement;
CREATE DATABASE hba_catalog       OWNER hba_catalog;
CREATE DATABASE hba_commerce      OWNER hba_commerce;
CREATE DATABASE hba_inventory     OWNER hba_inventory;
CREATE DATABASE hba_order         OWNER hba_order;
CREATE DATABASE hba_merchant      OWNER hba_merchant;
CREATE DATABASE hba_delivery      OWNER hba_delivery;
CREATE DATABASE hba_food          OWNER hba_food;


-- ═══════════════════════════════════════════════════════════════════════════════
-- PARTIE 3 — LE CLOISONNEMENT ET LE RAPPORT.  Tout sélectionner, puis F5.
--
-- SANS CE REVOKE, UN RÔLE PAR SERVICE NE SERT À RIEN. PostgreSQL accorde CONNECT
-- à PUBLIC sur toute base nouvellement créée : les quatorze rôles pourraient donc
-- se connecter aux quatorze bases, et un service compromis lirait les tables des
-- autres. Créer un rôle par service sans révoquer PUBLIC donne l'apparence du
-- cloisonnement et aucune de ses propriétés.
--
-- `template1` n'est pas touchée : la révocation porte base par base. La modifier
-- changerait le comportement de toute base créée ensuite sur cette instance.
-- ═══════════════════════════════════════════════════════════════════════════════

REVOKE CONNECT ON DATABASE hba_identity      FROM PUBLIC;
REVOKE CONNECT ON DATABASE hba_user          FROM PUBLIC;
REVOKE CONNECT ON DATABASE hba_media         FROM PUBLIC;
REVOKE CONNECT ON DATABASE hba_communication FROM PUBLIC;
REVOKE CONNECT ON DATABASE hba_financial     FROM PUBLIC;
REVOKE CONNECT ON DATABASE hba_promotion     FROM PUBLIC;
REVOKE CONNECT ON DATABASE hba_engagement    FROM PUBLIC;
REVOKE CONNECT ON DATABASE hba_catalog       FROM PUBLIC;
REVOKE CONNECT ON DATABASE hba_commerce      FROM PUBLIC;
REVOKE CONNECT ON DATABASE hba_inventory     FROM PUBLIC;
REVOKE CONNECT ON DATABASE hba_order         FROM PUBLIC;
REVOKE CONNECT ON DATABASE hba_merchant      FROM PUBLIC;
REVOKE CONNECT ON DATABASE hba_delivery      FROM PUBLIC;
REVOKE CONNECT ON DATABASE hba_food          FROM PUBLIC;

GRANT CONNECT ON DATABASE hba_identity      TO hba_identity;
GRANT CONNECT ON DATABASE hba_user          TO hba_user;
GRANT CONNECT ON DATABASE hba_media         TO hba_media;
GRANT CONNECT ON DATABASE hba_communication TO hba_communication;
GRANT CONNECT ON DATABASE hba_financial     TO hba_financial;
GRANT CONNECT ON DATABASE hba_promotion     TO hba_promotion;
GRANT CONNECT ON DATABASE hba_engagement    TO hba_engagement;
GRANT CONNECT ON DATABASE hba_catalog       TO hba_catalog;
GRANT CONNECT ON DATABASE hba_commerce      TO hba_commerce;
GRANT CONNECT ON DATABASE hba_inventory     TO hba_inventory;
GRANT CONNECT ON DATABASE hba_order         TO hba_order;
GRANT CONNECT ON DATABASE hba_merchant      TO hba_merchant;
GRANT CONNECT ON DATABASE hba_delivery      TO hba_delivery;
GRANT CONNECT ON DATABASE hba_food          TO hba_food;

-- Le rapport : ce qui existe, à qui, et si le cloisonnement a bien pris.
-- Une ligne « OUVERTE À TOUS » signifie que le REVOKE de cette base a été sauté.
SELECT c.nom                                   AS service,
       'hba_' || c.nom                         AS base,
       CASE WHEN d.datname IS NULL THEN 'ABSENTE' ELSE 'présente' END AS etat,
       coalesce(r.rolname, '—')                AS proprietaire,
       CASE WHEN d.datname IS NULL THEN '—'
            WHEN has_database_privilege('public', 'hba_' || c.nom, 'CONNECT')
            THEN 'OUVERTE À TOUS' ELSE 'cloisonnée' END AS acces
FROM (VALUES ('identity'),('user'),('media'),('communication'),('financial'),
             ('promotion'),('engagement'),('catalog'),('commerce'),('inventory'),
             ('order'),('merchant'),('delivery'),('food')) AS c(nom)
LEFT JOIN pg_database d ON d.datname = 'hba_' || c.nom
LEFT JOIN pg_roles    r ON r.oid = d.datdba
ORDER BY c.nom;
