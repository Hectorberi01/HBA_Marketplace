# `Auditing/`

Vide dans **HBA.Promotions.Infrastructure**.

**Ce qui va ici :** la lecture du journal d'audit de ce service.

**Ou ca vit aujourd'hui :** `AuditEntry` et `AuditConfiguration` partages ; la table, elle, est celle de ce service. MANQUENT : l'intercepteur d'audit, l'accesseur de contexte et les options.

---

Ce fichier existe parce que git ne versionne pas les dossiers vides : sans lui,
ce dossier n'existerait que sur la machine ou il a ete cree. Le supprimer quand
le dossier recoit du vrai contenu.

La regle de partage du depot, la meme depuis la migration Kafka : **ce dossier
porte la politique de ce service, le socle partage porte le type et le protocole.**
