# `Security/Encryption/`

Vide dans **HBA.Engagement.Reviews.Infrastructure**.

**Ce qui va ici :** le chiffrement propre a ce service.

**Ou ca vit aujourd'hui :** `AesGcmSecretProtector` — meme cle des deux cotes, sans quoi rien n'est dechiffrable

---

Ce fichier existe parce que git ne versionne pas les dossiers vides : sans lui,
ce dossier n'existerait que sur la machine ou il a ete cree. Le supprimer quand
le dossier recoit du vrai contenu.

La regle de partage du depot, la meme depuis la migration Kafka : **ce dossier
porte la politique de ce service, le socle partage porte le type et le protocole.**
