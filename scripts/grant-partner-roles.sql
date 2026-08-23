-- ═══════════════════════════════════════════════════════════════════════════
-- RATTRAPAGE DES RÔLES PARTENAIRES — À N'EMPLOYER QUE SUR UN JEU DE
-- DÉVELOPPEMENT, ET SEULEMENT SUR DES COMPTES DÉJÀ SEMÉS.
--
-- CECI NE CORRIGE PAS LA CAUSE, ET NE DOIT PAS EN TENIR LIEU.
--
-- Les rôles Seller, FoodPartner et Driver ne s'attribuent pas à la main dans
-- la vie normale de la plateforme : merchant-service, food-service et
-- delivery-service publient un événement, identity-service le consomme et
-- greffe le rôle. Cette chaîne était rompue par DEUX maillons qui rendaient
-- succès sur échec :
--
--   • le publieur Kafka rendait la main en succès quand le producteur était
--     nul — l'outbox marquait aussitôt le message « traité », donc supprimé,
--     sans lettre morte ni métrique ;
--   • le consommateur committait l'offset des événements dont aucun
--     gestionnaire n'était enregistré, en journalisant en Debug.
--
-- Les deux sont corrigés. Ce script ne sert qu'aux comptes semés AVANT la
-- correction : leurs événements ont été détruits, et rien ne les rejouera —
-- `Verify()` et `Approve()` sont idempotents et ne relèvent pas l'événement
-- sur un état déjà atteint.
--
-- SUR UNE BASE NEUVE, N'EXÉCUTEZ PAS CE SCRIPT. Reconstruisez et relancez
-- `scripts/seed-accounts.sh` : il vérifie désormais les rôles lui-même, et
-- son succès prouve que la chaîne d'événements fonctionne. Employer ce script
-- à la place masquerait précisément ce qu'on cherche à éprouver.
--
-- Usage :
--   docker compose -f docker-compose.dev.yml exec -T postgres \
--     psql -U hba -d hba_identity -f - < scripts/grant-partner-roles.sql
--
-- Ou, depuis un client graphique, sur la base `hba_identity`.
-- ═══════════════════════════════════════════════════════════════════════════

BEGIN;

-- LES RÔLES SONT DÉSIGNÉS PAR LEUR NOM, JAMAIS PAR LEUR IDENTIFIANT.
--
-- `IdentityDataSeeder` crée les sept rôles au démarrage avec des GUID neufs :
-- une base recréée porte d'autres identifiants. Un script qui les codait en
-- dur fonctionnerait une fois, puis attribuerait silencieusement le mauvais
-- rôle — ou aucun, la clé étrangère échouant sur un identifiant absent.

-- ── Qui reçoit quoi ────────────────────────────────────────────────────────
--
-- CETTE LISTE DOIT REFLÉTER CE QUI EXISTE VRAIMENT DANS LES AUTRES BASES.
--
-- Les treize services ont chacun la leur : aucune jointure n'est possible
-- depuis ici vers `hba_merchant.sellers` ou `hba_food.restaurants`. La liste
-- ci-dessous reprend la topologie que `seed-accounts.sh` produit ; si votre
-- amorçage s'est interrompu, vérifiez d'abord ce qui a réellement été créé :
--
--   \c hba_merchant
--   SELECT "Id", "ShopName", "Status", "KybStatus" FROM sellers.sellers;
--   \c hba_food
--   SELECT "Id", "Name", "Status", "PayoutSellerId" FROM food.restaurants;
--
-- Un compte qui n'a pas de dossier vendeur validé ne DOIT pas recevoir
-- `Seller` : le rôle ouvrirait des écrans que le service refusera ensuite,
-- et l'on chercherait la panne dans l'application.
WITH attributions (email, role_name) AS (
    VALUES
        -- Vendeur marchandise : une boutique.
        ('vendeur.market@hba.local',      'Seller'),

        -- Restaurateur. Il porte AUSSI `Seller` : un établissement ne peut
        -- entrer en service sans dossier de reversement, et ce dossier EST un
        -- vendeur — c'est lui qui reçoit les recettes.
        ('vendeur.food@hba.local',        'Seller'),
        ('vendeur.food@hba.local',        'FoodPartner'),

        -- Les deux activités sous un même compte.
        ('vendeur.mixte@hba.local',       'Seller'),
        ('vendeur.mixte@hba.local',       'FoodPartner'),

        -- Deux boutiques, un seul dossier vendeur.
        ('vendeur.2boutiques@hba.local',  'Seller'),

        -- Deux établissements — dont un seul existe réellement : food-service
        -- pose un index UNIQUE sur le compte propriétaire. Le rôle reste juste.
        ('vendeur.2restos@hba.local',     'Seller'),
        ('vendeur.2restos@hba.local',     'FoodPartner')

        -- Livreurs : décommentez APRÈS avoir vérifié qu'ils existent et sont
        -- vérifiés dans `hba_delivery.deliveries.drivers`. Un rôle Driver sur
        -- un livreur non vérifié ouvre l'application à un compte que
        -- delivery-service refusera.
        -- ,('livreur1@hba.local', 'Driver')
        -- ,('livreur2@hba.local', 'Driver')
        -- ,('livreur3@hba.local', 'Driver')
        -- ,('livreur4@hba.local', 'Driver')
        -- ,('livreur5@hba.local', 'Driver')
)
INSERT INTO identity.user_roles ("Id", "UserId", "RoleId")
SELECT gen_random_uuid(), u."Id", r."Id"
  FROM attributions a
  JOIN identity.users u ON lower(u.email)  = lower(a.email)
  JOIN identity.roles r ON r."Name"        = a.role_name
 -- Idempotent : relancer le script ne crée pas de doublon, et ne touche pas
 -- aux rôles déjà présents (Buyer, notamment, qui reste légitime — un vendeur
 -- est aussi un acheteur).
 WHERE NOT EXISTS (
        SELECT 1
          FROM identity.user_roles ur
         WHERE ur."UserId" = u."Id"
           AND ur."RoleId" = r."Id"
       );

COMMIT;

-- ── Conformité : ce que le script a produit ────────────────────────────────
--
-- UN SCRIPT QUI NE RELIT PAS SON TRAVAIL NE VAUT PAS MIEUX QU'UN SILENCE.
--
-- Un e-mail mal orthographié dans la liste ci-dessus ne produit AUCUNE erreur :
-- la jointure ne ramène simplement rien. C'est cette relecture qui le révèle.
SELECT u.email,
       string_agg(r."Name", ', ' ORDER BY r."Name") AS roles
  FROM identity.users u
  LEFT JOIN identity.user_roles ur ON ur."UserId" = u."Id"
  LEFT JOIN identity.roles      r  ON r."Id"      = ur."RoleId"
 GROUP BY u.email
 ORDER BY u.email;
