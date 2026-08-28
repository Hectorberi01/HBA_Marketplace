-- ═══════════════════════════════════════════════════════════════════════════
-- UNE BASE PAR SERVICE.
--
-- CE FICHIER ÉTAIT PRÉSENT MAIS VIDE DE TOUTE INSTRUCTION `CREATE DATABASE`.
--
-- compose.services.yml injecte pourtant `Database=hba_identity`,
-- `Database=hba_catalog`… : au premier démarrage, chaque service aurait échoué
-- sur « database does not exist », treize fois, sans que rien ne désigne ce
-- fichier comme responsable.
--
-- CE SCRIPT NE S'EXÉCUTE QU'AU PREMIER DÉMARRAGE DU VOLUME.
--
-- `docker-entrypoint-initdb.d` est ignoré si `postgres_data` contient déjà des
-- données. Après modification, il faut supprimer le volume — ou créer la base
-- à la main. C'est la cause la plus fréquente de « j'ai ajouté la base et rien
-- ne se passe ».
--
-- Le schéma interne de chaque base reste celui du monolithe (`media`,
-- `identity`, `users`…) : les migrations EF copiées le ciblent, et le changer
-- aurait imposé de toutes les réécrire.
-- ═══════════════════════════════════════════════════════════════════════════

CREATE DATABASE hba_identity;
CREATE DATABASE hba_user;
CREATE DATABASE hba_merchant;
CREATE DATABASE hba_catalog;
CREATE DATABASE hba_inventory;
-- commerce-service N'A AUCUNE CHAÎNE DE CONNEXION DANS compose.services.yml.
--
-- Il n'y reçoit que Redis et Kafka. Or trois de ses cinq modules d'origine —
-- Pricing, Loyalty, Marketing — persistent en PostgreSQL (schémas `pricing`,
-- `loyalty`, `marketing`). Seuls Cart et Wishlist sont réellement en cache.
--
-- La base est donc créée ici, mais compose devra recevoir
-- `ConnectionStrings__Default` pour commerce-service le jour de son extraction —
-- sans quoi son installateur échouera au démarrage sur une chaîne absente.
CREATE DATABASE hba_commerce;
CREATE DATABASE hba_order;
CREATE DATABASE hba_food;
CREATE DATABASE hba_delivery;
CREATE DATABASE hba_financial;
CREATE DATABASE hba_engagement;
CREATE DATABASE hba_communication;
CREATE DATABASE hba_media;

-- hba_promotion MANQUAIT ICI.
--
-- docker-compose.dev.yml injecte `Database=hba_promotion` a
-- promotion-service. Ce fichier listait treize bases et pas celle-la :
-- sur un volume neuf, promotion-service etait le seul a echouer.
-- Il ne le montrait pas en pratique, parce que Database.Migrate() cree
-- la base absente en dev — ce qui masquait l'oubli au lieu de le dire.
CREATE DATABASE hba_promotion;
