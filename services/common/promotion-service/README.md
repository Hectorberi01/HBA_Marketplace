# promotion-service

**Promotion Service (Coupons, Campagnes)**

## CE README DÉCRIVAIT UN SQUELETTE. IL N'EN EST PLUS UN DEPUIS LE LOT 4.1.

Il annonçait encore, en août : « les quatre projets sont créés et compilent, mais
ils sont **vides** : aucune entité, aucun cas d'usage, aucun endpoint métier »,
et « ce service n'est **pas** déclaré dans `docker-compose.dev.yml` ni dans
`HBA.sln` ».

Les quatre affirmations étaient fausses. Le service porte aujourd'hui l'agrégat
`Promotion`, l'agrégat `Coupon`, ses règles, son évaluation, son contrat gRPC
(`PromotionApi`), ses migrations et son schéma `promotions` — il est dans la
solution et dans `docker-compose.dev.yml`.

**UN README QUI DÉCRIT UN SERVICE AU PASSÉ EST DU MÊME ORDRE QUE LE BANDEAU
« SQUELETTE » POSÉ AU LOT 0.5 : dans les deux cas, le document et le code
racontent deux systèmes différents, et c'est le document qu'on croit.** Le lot 0.5
avait posé les bandeaux parce qu'« un audit les a d'abord comptés comme faits » ;
celui-ci est le cas symétrique — un service fait, compté comme à faire.

## Ce que le service rend aujourd'hui

* **Campagnes** (`Promotion`) — périmètre FOOD / MARKETPLACE / GLOBAL, type et
  valeur de remise, budget et budget consommé, financeur (D28 : part vendeur en
  points de base), fenêtre de validité, statuts.
* **Coupons** (`Coupon`) — code, plafond global, plafond par compte, réservation
  au checkout et libération à l'annulation.
* **Évaluation** — `PromotionApi.EvaluatePromotion` / `ReserveCoupon` /
  `ReleaseCoupon`, appelés par cart-service.

**Ce qui reste ouvert** est décrit dans les encadrés du code, pas ici — un
README qui recopie l'état du code finit toujours par le contredire. Voir
notamment `PromotionStatus.Expired`, état inatteignable faute de balayeur
(lot 9.2), et `PromotionConfigurations` pour le choix de l'entier monétaire (D39).

## Structure

```
promotion-service/
├── src/
│   ├── HBA.Promotions.Domain/           entites metier (agregats, value objects)
│   ├── HBA.Promotions.Application/      cas d'usage, services, DTO, interfaces
│   ├── HBA.Promotions.Infrastructure/   persistance, cache, fournisseurs externes
│   └── HBA.Promotions.Api/              controleurs HTTP, gRPC, consumers
├── Dockerfile
└── README.md
```

**LES « ÉTAPES POUR L'ACTIVER » ONT ÉTÉ RETIRÉES : LES CINQ SONT FAITES.**

Elles disaient encore « déplacer le code métier depuis le service d'origine »,
« ajouter les quatre projets à `HBA.sln` », « créer la base et les migrations,
puis déclarer le service dans `docker-compose.dev.yml` ». Tout cela a eu lieu au
lot 4.1. Une liste de tâches accomplies laissée telle quelle finit par être
relue comme un reste à faire — et par être refaite.

