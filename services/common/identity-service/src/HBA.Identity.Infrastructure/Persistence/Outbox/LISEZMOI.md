# `Persistence/Outbox/`

Vide dans **HBA.Identity.Infrastructure**.

**Ce qui va ici :** le CABLAGE de l'outbox de ce service, pas une copie de l'entite.

**Ou ca vit aujourd'hui :** `HBA.Shared.Infrastructure.Outbox` — `OutboxMessage` est une entite EF dont les colonnes sont creees par les migrations de 18 services sur la MEME table, et `OutboxProcessor<TDbContext>` est deja generique sur le DbContext de ce service. Le service possede donc deja son outbox a l'execution ; une copie du code ne lui donnerait que le droit de diverger de la table.

---

Ce fichier existe parce que git ne versionne pas les dossiers vides : sans lui,
ce dossier n'existerait que sur la machine ou il a ete cree. Le supprimer quand
le dossier recoit du vrai contenu.

La regle de partage du depot, la meme depuis la migration Kafka : **ce dossier
porte la politique de ce service, le socle partage porte le type et le protocole.**
