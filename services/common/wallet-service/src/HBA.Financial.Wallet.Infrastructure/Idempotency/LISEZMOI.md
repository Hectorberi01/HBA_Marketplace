# `Idempotency/`

Vide dans **HBA.Financial.Wallet.Infrastructure** : ce service ne tient pas de table d'idempotence.

**Ce qui va ici :** `IdempotencyRecord`, sa configuration EF, le depot, la purge
et l'enregistrement — pour un service qui expose des routes annotees
`AllowIdempotency()` ou qui doit dedupliquer des commandes rejouees.

**Ou ca vit aujourd'hui :** dans les SEPT services qui en tiennent une —
identity, users, merchants, catalog, promotions, payments, notifications — chacun
dans son propre `Idempotency/`. Seul le port `IIdempotencyStore` reste au socle,
parce que `IdempotencyEndpointFilter` (couche HTTP partagee) le resout sur chaque
route annotee.

---

Ce fichier existe parce que git ne versionne pas les dossiers vides : sans lui,
ce dossier n'existerait que sur la machine ou il a ete cree. Le supprimer quand
le dossier recoit du vrai contenu.

La regle de partage du depot, la meme depuis la migration Kafka : **ce dossier
porte la politique de ce service, le socle partage porte le type et le protocole.**
