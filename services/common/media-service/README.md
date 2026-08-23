# Media Service

Dépôt et téléchargement, stockage objet, traitement d'images, URL signées, métadonnées.

**Modules actuels :** `../../../../src/Modules/Media/`

## À savoir avant d'extraire

**LE MEILLEUR PREMIER CANDIDAT, et il a été écrit pour cela.**

Il ne connaît aucun autre module, ne fait aucune jointure, et son stockage est déjà derrière un port (`IObjectStorage`) avec deux implémentations. Il a d'ailleurs consolidé cinq implémentations S3 dispersées dans le monolithe — trois dans Catalog, deux dans Sellers.

**La visibilité est une règle du service, pas de l'appelant.** Une image produit est publique et servie par URL permanente ; une pièce d'identité, un justificatif de livraison ou une preuve de litige sont privés et ne se lisent que par URL signée de courte durée, demandée nommément.

**Le droit métier reste chez l'appelant.** Le service sait qu'un fichier est privé ; il ne sait pas si CE demandeur a le droit de le voir. C'est la règle qui a fermé trois failles réelles — et l'oublier les rouvrirait toutes.

## Squelette attendu

```
media-service/
├── src/
│   ├── api/             Points d'entrée HTTP/gRPC, consommateurs d'événements
│   ├── domain/          Agrégats, invariants, événements de domaine
│   ├── application/     Commandes, requêtes, ports
│   └── infrastructure/  Persistance, adaptateurs, publication d'événements
├── tests/
├── Dockerfile
└── README.md
```
