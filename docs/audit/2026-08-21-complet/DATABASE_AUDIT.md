# Audit de la persistance — HBAExpress

Périmètre : `/root/audit-src` (lecture seule), analyse statique.
Méthode : lecture des `DbContext`, des `IEntityTypeConfiguration`, des migrations et des
`*ModelSnapshot.cs` ; rejeu à sec des migrations (script maison) ; recoupement snapshot ↔ migrations
↔ configuration, colonne par colonne et index par index.

## Chiffres

| Élément | Nombre |
|---|---|
| `DbContext` de module (hors `ModuleDbContext` abstrait) | **23** |
| `DbContext` disposant d'un dossier `Migrations` + snapshot | **21** |
| `DbContext` sans **aucune** migration ni snapshot | **2** (`DeliveryPricingDbContext`, `ReturnRefundDbContext`) |
| Fichiers de migration (hors `.Designer.cs` / snapshot) | **169** |
| Snapshots | **21** |
| Migrations **inertes** (jamais appliquées par EF) | **2** |
| Schémas PostgreSQL déclarés | 23 |

---

## 1. Le script `scripts/check-migrations.py` — ce qu'il dit, ce qu'il ne dit pas

### Exécution

```
$ python3 scripts/check-migrations.py
❌ delivery/delivery-pricing-service   tables delivery_quotes, delivery_zones, pricing_rules
❌ marketplace/return-refund-service   tables idempotency_keys, refund_attempts, refunds,
                                       return_evidence, return_inspections, return_items,
                                       return_requests, return_shipments, return_status_history
21 contexte(s) rejoué(s), 12 incohérence(s) de départ à froid.
```

### Vérification indépendante de ses conclusions

Les 12 constats sont **exacts** : ces deux services ont un `DbContext`, des configurations EF
complètes, des dépôts et des commandes, et **aucun dossier `Migrations`**
(`find services/*/*/ -type d -name Migrations` ne rend rien pour eux). Voir §2.1.

### Limites confirmées du script (vérifiées, pas supposées)

| # | Limite | Preuve |
|---|---|---|
| L1 | **Il vérifie les TABLES, jamais les COLONNES.** Une colonne du modèle qu'aucune migration ne crée passe inaperçue. | C'est exactement le défaut §3.1 : la colonne `ordering.orders."PaymentId"` échappe au script. |
| L2 | **Il ne vérifie pas que la migration est *découvrable* par EF.** Il lit les fichiers `.cs` du dossier ; EF, lui, ne charge que les classes portant `[Migration]` + `[DbContext]`. Deux migrations du dépôt en sont dépourvues et sont donc **mortes**. | `20260824000000_AddOrderPaymentId.cs`, `20260824010000_AddPaymentRefunds.cs` — §3.1 et §3.2. |
| L3 | **`check_sql_identifier_case` est totalement mort.** Il ne cherche des identifiants que dans des littéraux `"""…"""` (raw string). **Aucune** migration du dépôt n'en utilise : les 45 fichiers à SQL brut emploient `@"…"`. Ce contrôle n'a jamais pu se déclencher. | `grep -rln 'migrationBuilder\.Sql(\s*@"'` → 45 fichiers ; `grep -rln 'Sql(\s*"""'` → 0. J'ai rejoué le contrôle à la main sur les `@"…"` : **aucune** faute de casse trouvée. |
| L4 | **Il ignore le SQL brut pour le DDL.** Il annonce cette limite, mais elle est plus grave qu'elle n'en a l'air : la colonne `TraceParent` des 19 tables `outbox_messages` n'est créée QUE par `migrationBuilder.Sql`. Le script ne la « voit » pas — et un futur contrôle colonne-par-colonne naïf produirait 19 faux positifs. | `20260823000000_AjoutTraceParentOutbox.cs` (×19). |
| L5 | **Il ne compare jamais au snapshot.** Une table créée par migration mais absente du snapshot n'est pas signalée — or le prochain `dotnet ef migrations add` la recréerait. | `payments.payment_refunds` — §3.2. |
| L6 | **Il agrège les contextes d'un même service pour `check_tables`.** `configured_tables()` / `created_tables()` marchent au niveau du *service*, pas du *contexte*. Sur notification-service (2 contextes, 2 schémas), une table créée dans `messaging` satisferait un besoin déclaré dans `notifications`. | `check_tables(service)`, lignes `for folder, _, names in os.walk(os.path.join(ROOT, service))`. Aucun faux négatif constaté aujourd'hui, mais la faille est ouverte. |

Verdict : le script attrape une classe de défauts réelle (table configurée sans migration) et rien
d'autre. Les deux défauts les plus dangereux du dépôt (§3.1, §3.2) lui échappent tous les deux.

---

## 2. Inventaire par service

Convention : « Snapshot cohérent ? » = les tables et colonnes du `*ModelSnapshot.cs` correspondent-elles
à ce que produisent réellement les migrations rejouées dans l'ordre.

### 2.1 Les deux contextes sans aucune migration

#### DeliveryPricingDbContext
```
Schéma: delivery_pricing
DbSet: DeliveryQuotes, PricingRules, DeliveryZones (+ OutboxMessages hérité de ModuleDbContext)
Migrations: AUCUNE — le dossier Migrations n'existe pas
Snapshot cohérent avec les migrations ? Sans objet : aucun snapshot
Défauts:
  - CRITICAL — les 4 tables du schéma (delivery_quotes, pricing_rules, delivery_zones,
    outbox_messages) ne sont créées par rien. Le service ne peut pas démarrer utilement :
    première requête → 42P01 relation "delivery_pricing.delivery_quotes" does not exist.
    Preuve : services/delivery/delivery-pricing-service/src/HBA.Delivery.Pricing.Infrastructure/
    Persistence/DeliveryPricingDbContext.cs:30-63 (OnModelCreating déclare les 3 ToTable).
  - CRITICAL — outbox absente : aucun événement d'intégration de la tarification ne sortira.
  - HIGH — DeliveryQuote.ConsumedByDeliveryId (DeliveryQuote.cs:22) : aucune contrainte
    d'unicité, donc un même devis peut être consommé par deux courses → deux courses au prix
    d'une. Aucun jeton de concurrence non plus.
  - MEDIUM — mapping intégralement inline dans OnModelCreating (pas d'IEntityTypeConfiguration),
    aucun index déclaré à part ce que le snapshot absent aurait porté ; les OwnsOne
    (Pickup/Dropoff/Components) n'ont ni HasColumnName ni précision.
```

#### ReturnRefundDbContext
```
Schéma: return_refund
DbSet: ReturnRequests, IdempotencyKeys (+ OutboxMessages ; + AuditEntry car KeepsAuditTrail => true)
Migrations: AUCUNE — le dossier Migrations n'existe pas
Snapshot cohérent avec les migrations ? Sans objet : aucun snapshot
Défauts:
  - CRITICAL — 11 tables jamais créées : return_requests, return_items, return_evidence,
    return_shipments, return_inspections, refunds, refund_attempts, return_status_history,
    idempotency_keys, outbox_messages, audit_entries.
    Preuve : .../Persistence/Configurations/ReturnRequestConfiguration.cs (9 ToTable),
    ReturnRefundDbContext.cs:28 (KeepsAuditTrail => true → audit_entries entre au modèle),
    ModuleDbContext.cs:70-78.
  - CRITICAL — KeepsAuditTrail=true SANS migration : c'est précisément ce que l'encadré de
    ModuleDbContext.KeepsAuditTrail interdit (« L'activation se fait module par module, DANS LE
    MÊME COMMIT que sa migration »). La règle est écrite dans le socle et violée ici.
  - HIGH — refunds.IdempotencyKey (ReturnRequestConfiguration.cs:113) : mappé, longueur 160,
    AUCUN index unique. Deux exécutions concurrentes de ExecuteRefundCommand créent deux
    remboursements. Comparer avec payment-service qui, lui, pose (PaymentId, IdempotencyKey) unique.
  - HIGH — refunds.ProviderRefundId (ligne 114) : pas d'unicité → un même remboursement PSP peut
    être enregistré deux fois.
  - MEDIUM — aucun index sur les clés étrangères return_items.ReturnId, return_evidence.ReturnId,
    return_shipments.ReturnId, return_inspections.ReturnId, refund_attempts.RefundId
    (lignes 42-47 et 126 : les HasMany posent les FK, aucun HasIndex ne les couvre côté enfant ;
    EF crée un index de FK par défaut, mais ici les enfants n'ont pas de configuration d'index et
    aucune migration ne viendra le matérialiser de toute façon).
  - MEDIUM — 6 DeleteBehavior.Cascade sur une chaîne financière (§7).
```

### 2.2 Univers `marketplace`

#### OrderingDbContext
```
Schéma: ordering
DbSet: Orders (+ OutboxMessages)
Migrations (ordre d'application) :
  20250608000000_InitialOrdering
  20250629030000_AddOrderShippingAddress
  20250701000000_AddOrderShippingFee
  20260714135446_AddConcurrencyTokens
  20260714143436_AddOrderPromotionCode
  20260714152702_AddOutboxRetryTracking
  20260714194425_MakeChildForeignKeysRequired
  20260805090500_BeninOrderShippingAddress
  20260805100500_OrderShipToCoordinates
  20260818183422_SyncModel20260818183420
  20260819000000_LigneDeCommandeTypee
  20260820000100_DevisDeCourseFige
  20260821000000_CommandeEnArbitrage
  20260823000000_AjoutTraceParentOutbox
  20260823000100_UnicitePanierParCommande
  20260824000000_AddOrderPaymentId        ← INERTE (ni [Migration] ni .Designer.cs)
Snapshot cohérent avec les migrations ? NON.
  Le snapshot porte orders."PaymentId" (OrderingDbContextModelSnapshot.cs:56) et l'index
  IX_orders_PaymentId (ligne 140) ; la seule migration qui les crée n'est jamais exécutée.
Défauts:
  - CRITICAL — colonne PaymentId sans migration effective (§3.1).
  - MEDIUM — l'index IX_orders_PaymentId figure au snapshot mais n'est PAS déclaré par
    OrderConfiguration.cs (qui n'a qu'un builder.Property(o => o.PaymentId), ligne 63). Le
    snapshot ment donc aussi dans l'autre sens : le prochain scaffolding générera un DropIndex.
  - MEDIUM — orders sans UpdatedAtUtc alors que l'agrégat est muté par toute la saga
    (MarkPaid, Cancel, UnderReview…). Voir §9.
  - INFO (bon point) — 20260823000100_UnicitePanierParCommande pose UX sur CartId avec un garde-fou
    qui refuse la migration si des doublons existent (lignes 60-80) : c'est le bon geste.
```

#### PaymentsDbContext
```
Schéma: payments
DbSet: Payments, SavedPaymentMethods, ConsumerInbox, IdempotencyKeys (+ OutboxMessages)
Migrations (ordre d'application) :
  20250609000000_InitialPayments
  20250626000000_AddPaymentGatewayFlow
  20250627000000_AddPaymentEscrow
  20260714135435_AddConcurrencyTokens
  20260714152704_AddOutboxRetryTracking
  20260811152329_AdoptPaymentMethods
  20260818075555_AddInboxAndIdempotency
  20260818183320_SyncModel20260818183318
  20260823000000_AjoutTraceParentOutbox
  20260824010000_AddPaymentRefunds        ← INERTE (ni [Migration] ni .Designer.cs)
Snapshot cohérent avec les migrations ? NON.
  payment_refunds est créée par une migration (inerte) et ABSENTE du snapshot
  (grep "payment_refunds" PaymentsDbContextModelSnapshot.cs → 0 occurrence), alors que
  PaymentRefundConfiguration est bien chargée (ApplyConfigurationsFromAssembly,
  PaymentsDbContext.cs:54).
Défauts:
  - CRITICAL — table payment_refunds jamais créée (§3.2).
  - CRITICAL — payments.ProviderReference indexé mais NON unique
    (PaymentConfiguration.cs:98) alors que GatewayConfirmationCommands.cs:73 →
    PaymentRepository.cs:30 fait FirstOrDefaultAsync dessus. Deux paiements portant la même
    référence PSP → le webhook encaisse un paiement au hasard. Voir §5.
  - HIGH — payment_refunds.ExternalRefundId indexé non-unique
    (PaymentConfiguration.cs:137) : le service return-refund peut demander deux fois le même
    remboursement avec deux clés d'idempotence différentes → double sortie d'argent.
  - MEDIUM — DeleteBehavior.Cascade payments → payment_refunds (ligne 94) : effacer un paiement
    efface la preuve de ses remboursements.
  - MEDIUM — payments sans UpdatedAtUtc (statut, capture, escrow mutent).
  - INFO (bon point) — jeton xmin présent (ligne 43) et (PaymentId, IdempotencyKey) unique (ligne 136).
```

#### CatalogDbContext
```
Schéma: catalog
DbSet: Products, ProductRevisions, ProductReviews, Brands, BrandRequests, Categories,
       AttributeDefinitions, CategoryAttributes, Offers, ConsumerInbox, IdempotencyKeys
Migrations : 20250601000000_InitialCatalog, 20260714152640_AddOutboxRetryTracking,
  20260802101804_CategoryUniqueByPath, 20260816000000_RepriseImagesProduitVersMedia,
  20260816210421_AjoutOffresProduit, 20260817133913_AjoutVarianteActive,
  20260818141400_AddProductConditionDefectsProductConditionsProductRevisions,
  20260818154141_AddProductReviewReasonsProductReviews,
  20260818160327_AddAttributeDefinitionsBrandRequestsCategoryAttributes,
  20260818163923_AddProductSpecificationGroupsProductSpecifications,
  20260818172643_AddConsumerInboxIdempotencyKeys, 20260818183413_SyncModel20260818183411,
  20260823000000_AjoutTraceParentOutbox
Snapshot cohérent avec les migrations ? OUI (13 tables + colonnes vérifiées une à une).
Défauts:
  - LOW — IX_outbox_messages_ProcessedOnUtc, créé par InitialCatalog, n'existe plus au modèle
    (OutboxConfiguration ne déclare que les deux index partiels) : index orphelin qui coûte à
    chaque écriture. Idem sur 14 autres contextes (§4).
  - LOW — 11 des 21 tables n'ont pas de CreatedAtUtc (product_variants, product_media,
    brands, categories…) : voir §9.
```

#### InventoryDbContext
```
Schéma: inventory
DbSet: InventoryItems, FulfillmentLocations (+ OutboxMessages)
Migrations : 20250605000000_InitialInventory, 20260714135348_AddConcurrencyTokens,
  20260714152649_AddOutboxRetryTracking, 20260714194404_MakeChildForeignKeysRequired,
  20260805091000_BeninLocationAddress, 20260811121428_AddLocationContactPhone,
  20260818183417_SyncModel20260818183415, 20260823000000_AjoutTraceParentOutbox
Snapshot cohérent avec les migrations ? OUI.
Défauts:
  - HIGH — stock_reservations : index sur OrderId (StockReservationConfiguration.cs:21) mais
    PAS d'unicité (InventoryItemId, OrderId). InventoryItem.Reserve (InventoryItem.cs:132-152)
    ne vérifie pas qu'une réservation existe déjà pour cette commande : un rejeu Kafka de
    ReserveStockCommand réserve deux fois → stock immobilisé pour rien, puis survente
    apparente à la libération.
  - HIGH — InventoryItemRepository.cs:73-75 : ListLowStockAsync charge la table ENTIÈRE avec
    toutes ses réservations puis filtre en mémoire. Voir §12.
  - MEDIUM — inventory_items sans CreatedAtUtc ni UpdatedAtUtc alors que StockVersion est
    incrémenté à chaque mouvement : impossible de dater un mouvement de stock.
  - MEDIUM — stock_reservations.ExpiresAtUtc non indexé alors que c'est le critère du balayage
    des réservations expirées.
  - INFO (bon point) — xmin (InventoryItemConfiguration.cs:56) + Touch() explicite dans Reserve
    pour forcer l'UPDATE parent : le raisonnement est juste et documenté.
```

#### CartDbContext
```
Schéma: cart
DbSet: Carts (+ OutboxMessages)
Migrations : 20250607000000_InitialCart, 20260714143416_AddCartPromotionCode,
  20260714152638_AddOutboxRetryTracking, 20260818000000_LigneDePanierTypee,
  20260818183408_SyncModel20260818183405, 20260823000000_AjoutTraceParentOutbox
Snapshot cohérent avec les migrations ? OUI.
Défauts:
  - HIGH — carts : index (BuyerId, Status) NON unique (CartConfiguration.cs:52) alors que
    CartRepository.cs:31-35 fait FirstOrDefaultAsync(BuyerId && Status==Active). Deux « ajouter
    au panier » simultanés sur un compte sans panier créent DEUX paniers actifs ; l'un des deux
    devient invisible et ses articles sont perdus. Un index unique partiel
    (filter "Status = 'Active'") ferme la course.
  - MEDIUM — pas de jeton de concurrence sur carts.
  - LOW — carts / cart_items sans CreatedAtUtc : impossible de purger les paniers abandonnés
    par ancienneté.
```

#### SellersDbContext
```
Schéma: sellers
DbSet: Sellers, Stores, SellerMembers, SellerRoles, SellerInvitations, ConsumerInbox,
       IdempotencyKeys (+ OutboxMessages + AuditEntry : KeepsAuditTrail => true)
Migrations : 20250603000000_InitialSellers, 20260714152717_AddOutboxRetryTracking,
  20260718101830_AddSellerMetadata, 20260805091500_BeninSellerCommune,
  20260811225036_AddKybRejectionReason, 20260812000000_TableDesBoutiques,
  20260813000000_RepriseStoresFromSellers, 20260815000000_RepriseKybVersMedia,
  20260818183427_SyncModel20260818183425, 20260818192153_SyncModel20260818192151,
  20260818204451_AddConsumerInboxIdempotencyKeys, 20260819120000_TableDesMembresVendeur,
  20260819130000_TableDesInvitations, 20260819160000_JournalDAudit,
  20260823000000_AjoutTraceParentOutbox
Snapshot cohérent avec les migrations ? OUI (audit_entries bien créée par 20260819160000,
  KeepsAuditTrail activée dans le même lot — la règle du socle est respectée ici).
Défauts:
  - LOW — stores : index sur SellerId seul (StoreConfiguration.cs:64), pas d'unicité sur
    (SellerId, nom/slug) : deux boutiques homonymes chez un même vendeur.
  - INFO — c'est le service le mieux contraint du dépôt : xmin sur seller_roles/seller_members/
    seller_invitations, uniques partiels (SellerId,Name)/(Name où SellerId IS NULL),
    (SellerId,UserId), (SellerId,Email où Status=0), TokenHash unique.
```

### 2.3 Univers `delivery`

#### DeliveriesDbContext
```
Schéma: deliveries
DbSet: Deliveries, Drivers, Partners, WebhookDeliveries (+ OutboxMessages)
Migrations : 20260811060336_InitialDeliveries, 20260811084626_AddPartners,
  20260811091221_AddPricingAndZones, 20260811092201_AddPartnersPricingAndZones,
  20260811122505_AddDriverEarning, 20260811123856_AddProofAndSchedule,
  20260811131618_AddWebhookDeliveries, 20260811134314_AddFailedProofAttempts,
  20260818183351_SyncModel20260818183348, 20260823000000_AjoutTraceParentOutbox
Snapshot cohérent avec les migrations ? OUI.
Défauts:
  - CRITICAL — AUCUN jeton de concurrence sur `deliveries` (DeliveryConfiguration.cs entier :
    pas de UsePostgresRowVersion, contrairement à orders/payments/inventory_items). Or
    Delivery.AcceptByDriver (Delivery.cs:476-496) est un lire-puis-écrire sur Status. La boucle
    de dispatch (DeliveryDispatchService) expire une offre et en propose une autre pendant que
    le premier livreur accepte : deux livreurs peuvent se voir attribuer la même course. Le
    commentaire du dispatch reconnaît le risque et le traite par « une seule instance à la
    fois » — ce qui ne protège pas de la concurrence entre le dispatch et une requête livreur.
  - HIGH — deliveries.QuoteId (DeliveryConfiguration.cs:47) : ni index ni unicité. Le devis
    figé est le montant facturé ; rien n'empêche deux courses de porter le même QuoteId.
  - MEDIUM — drivers sans jeton de concurrence alors que AccountStatus/Availability sont mutés
    par le dispatch et par le livreur.
  - MEDIUM — deliveries et drivers sans UpdatedAtUtc (drivers n'a même pas de CreatedAtUtc).
  - INFO (bon point) — index partiels bien pensés : ix_deliveries_awaiting_driver
    (filter Status IN (…)), ix_deliveries_scheduled_for, ix_deliveries_partner ;
    ux_deliveries_reference_source ferme l'idempotence de création partenaire.
```

### 2.4 Univers `food`

#### FoodDbContext (restaurant-service)
```
Schéma: food
DbSet: Restaurants, Menus, MenuCategories, MenuItems, Staff, PreparationStations, FoodOrders
Migrations : 20260812081143_InitialFood,
  20260812110630_PersonnelStationsCommandesSaturationEtExceptions,
  20260812121955_ImagesVersMedia, 20260812235900_SeedRestaurantFounders,
  20260813000100_RepriseCartesADeuxNiveaux, 20260818183403_SyncModel20260818183400,
  20260820000000_DossierDeReversementDuRestaurant, 20260823000000_AjoutTraceParentOutbox
Snapshot cohérent avec les migrations ? OUI, avec une réserve :
  le fichier 20260818183403_SyncModel….Designer.cs (daté du 18/08) porte déjà
  restaurants."PayoutSellerId", colonne créée par la migration du 20/08. Les Designer ont donc
  été réécrits après coup. Sans effet à l'exécution, mais l'historique ne décrit plus l'état
  du modèle à chaque étape — la valeur principale d'un Designer.
Défauts:
  - LOW — food_orders sans CreatedAtUtc.
  - INFO — l'incident historique cité par l'en-tête de check-migrations.py (double renommage
    de restaurants.LogoUrl) N'EST PLUS présent : 20260814000000_RepriseImagesVersMedia n'existe
    pas dans cet arbre. Le rejeu à sec ne signale rien sur ce contexte.
  - INFO (bon point) — xmin sur restaurants, restaurant_staff et food_orders.
```

#### MealOrderingDbContext (food-order-service)
```
Schéma: food_ordering
DbSet: Orders (+ OutboxMessages + AuditEntry : KeepsAuditTrail => true)
Migrations : 20260819190000_InitialFoodOrdering
Snapshot cohérent avec les migrations ? OUI (audit_entries et outbox créées dans la migration
  initiale, colonnes complètes TraceParent comprise).
Défauts:
  - MEDIUM — meal_orders sans jeton de concurrence alors que la commande de repas subit les
    mêmes transitions concurrentes qu'une commande marketplace (paiement, cuisine, livraison).
  - LOW — meal_order_lines / meal_order_line_options sans CreatedAtUtc.
  - INFO (bon point) — U:CartId présent dès la migration initiale, ix_meal_orders_under_review
    partiel.
```

#### FoodCartDbContext
```
Schéma: food_cart
DbSet: Carts (+ OutboxMessages)
Migrations : 20260819180000_InitialFoodCart
Snapshot cohérent avec les migrations ? OUI.
Défauts:
  - HIGH — food_carts : index (BuyerId, Status) non unique, même défaut que cart-service ; en
    outre rien ne borne « un panier actif par (acheteur, restaurant) ».
  - MEDIUM — pas de jeton de concurrence.
  - LOW — aucune colonne d'horodatage sur les 3 tables.
```

### 2.5 Univers `common`

#### WalletDbContext — fichier `SettlementDbContext.cs`
```
Schéma: settlement
DbSet: Earnings, Batches, SellerWallets, PlatformWallets, DriverWallets, Withdrawals,
       CustomerRefunds, WalletTransactions (+ OutboxMessages)
Migrations : 20250615000000_InitialSettlement, 20250628000000_AddEarningReleased,
  20250701010000_AddWalletTables, 20260704171817_AddProviderFee,
  20260711170457_AddWithdrawalProcessingState, 20260711172903_AddWithdrawalProcessingState_1,
  20260714095610_AddRefundReversalIdempotencyIndex,
  20260714105157_AddRefundReversalIdempotencyIndex_v1, 20260714135358_AddConcurrencyTokens,
  20260714152719_AddOutboxRetryTracking, 20260714194434_MakeChildForeignKeysRequired,
  20260727153736_AddCustomerRefunds, 20260811211830_AddDriverWallet,
  20260811223820_AddWithdrawalDestination, 20260816120000_AddWithdrawalEarningImputation,
  20260818183341_SyncModel20260818183338, 20260823000000_AjoutTraceParentOutbox
Snapshot cohérent avec les migrations ? OUI.
Défauts:
  - CRITICAL — customer_refunds : aucune clé d'idempotence, aucun index unique sur OrderId ni
    ProviderRef (CustomerRefundConfiguration, WalletConfigurations.cs:151-176). Deux appels
    concurrents à InitiateCustomerRefundCommand lisent tous deux
    SumActiveForOrderAsync avant l'écriture de l'autre, passent tous deux le plafond, et
    envoient DEUX versements Mobile Money. Argent réellement sorti deux fois.
    (CustomerRefundCommands.cs:90-116)
  - CRITICAL — dans le même handler, l'appel PSP `SendMobileMoneyPayoutAsync`
    (CustomerRefundCommands.cs:108-116) est fait AVANT tout SaveChangesAsync : si le processus
    meurt entre l'envoi et la ligne 140, l'argent est parti et AUCUNE ligne customer_refunds
    n'existe. La réconciliation (ListProcessingAsync) ne le retrouvera jamais. Voir §11.
  - MEDIUM — withdrawals : index sur SellerId seul. WalletRepositories.cs:72
    (ListByStatusAsync) filtre sur Status seul → balayage complet de la file des retraits, la
    requête de la console d'administration.
  - MEDIUM — settlement_batches : AUCUN index. SettlementRepositories.cs:112 et :120 listent
    tous les lots triés par CreatedAtUtc → scan + tri sur une table qui grossit d'un lot par
    période et par devise.
  - MEDIUM — N+1 dans SettlementCommands.cs:144-147 : un GetBySellerAsync par groupe de
    vendeur, dans la boucle de reversement. Voir §11.
  - LOW — 20260714105157_AddRefundReversalIdempotencyIndex_v1 : Up() et Down() VIDES. Migration
    fantôme, doublon avorté de la précédente ; elle ne fait qu'occuper une ligne de
    __EFMigrationsHistory.
  - LOW — la classe s'appelle WalletDbContext mais vit dans SettlementDbContext.cs, dans un
    schéma « settlement », derrière un dossier wallet-service. Trois noms pour une chose.
  - INFO (bon point) — xmin sur seller_wallets, platform_wallet, driver_wallets ; unique partiel
    ux_wallet_transactions_refund_reversal.
```

#### BillingDbContext
```
Schéma: billing
DbSet: CommissionRules, Invoices (+ OutboxMessages)
Migrations : 20250614000000_InitialBilling, 20260714152635_AddOutboxRetryTracking,
  20260818183257_SyncModel20260818183255, 20260823000000_AjoutTraceParentOutbox
Snapshot cohérent avec les migrations ? OUI.
Défauts:
  - MEDIUM — BillingRepositories.cs:50 : ListBySellerAsync charge TOUTES les factures d'un
    vendeur avec leurs lignes, sans pagination. Une facture par période × des années.
  - MEDIUM — commission_rules sans CreatedAtUtc ; BillingRepositories.cs:30 trie sur
    EffectiveFromUtc qui n'est pas indexé (index existants : IsActive, Scope+TargetId).
  - MEDIUM — aucun jeton de concurrence sur invoices ni commission_rules ; une règle de
    commission modifiée par deux administrateurs s'écrase silencieusement.
  - LOW — DeleteBehavior.Cascade invoices → invoice_lines (BillingConfigurations.cs:78) :
    supprimer une facture efface son détail (§7).
```

#### IdentityDbContext
```
Schéma: identity
DbSet: Users, Roles, MfaChallenges, ConsumerInbox, IdempotencyKeys (+ OutboxMessages)
Migrations : 19 fichiers, de 20250602000000_InitialIdentity à 20260823000000_AjoutTraceParentOutbox
  (dont 20260811150100_MoveAddressesToUsers et 20260811180000_DropIdentityPaymentMethods qui
  suppriment addresses et payment_methods — d'où leur présence dans les migrations et leur
  absence du snapshot : c'est correct).
Snapshot cohérent avec les migrations ? OUI.
Défauts:
  - MEDIUM — refresh_tokens.TokenHash indexé NON unique (RefreshTokenConfiguration.cs:59) :
    rien n'interdit deux jetons de rafraîchissement au même hash, et la révocation se fait par
    FirstOrDefault.
  - MEDIUM — RoleRepository.cs:38 charge TOUS les rôles puis filtre en mémoire, en assumant
    « table de petite taille ». Voir §14.
  - LOW — roles / user_roles sans horodatage : impossible de dater l'octroi d'un rôle
    (aucun journal d'audit sur ce contexte non plus, §13).
```

#### UsersDbContext
```
Schéma: users
DbSet: Addresses, UserProfiles, Preferences, Devices, ConsumerInbox, IdempotencyKeys
Migrations : 20260811150000_InitialUsers, 20260811150127_AddUserProfiles,
  20260811181000_PurgeAddressesOfDeletedAccounts,
  20260818075551_AddPreferencesDevicesInboxAndIdempotency,
  20260818183336_SyncModel20260818183334, 20260823000000_AjoutTraceParentOutbox
Snapshot cohérent avec les migrations ? OUI.
Défauts: LOW — user_profiles et preferences sans index (acceptable : PK = identifiant
  utilisateur) ; devices sans CreatedAtUtc.
```

#### MessagingDbContext / NotificationsDbContext (notification-service, 2 contextes)
```
### MessagingDbContext
Schéma: messaging
DbSet: Conversations (+ OutboxMessages)
Migrations : 20250618000000_InitialMessaging, 20260711155329_AddMessageReactionsAndDeletion,
  20260714152655_AddOutboxRetryTracking, 20260714194415_MakeChildForeignKeysRequired,
  20260723030000_MessageAttachmentsAsJson, 20260723040000_MessageAttachmentsChildTable,
  20260817000000_PiecesJointesPrivees, 20260818183310_SyncModel20260818183308,
  20260823000000_AjoutTraceParentOutbox
Snapshot cohérent avec les migrations ? OUI.
Défauts:
  - MEDIUM — la table `conversations` n'a AUCUN index. ConversationRepository.cs:44 trie par
    LastMessageAtUtc sur toutes les conversations d'un utilisateur : tri sans index.
  - MEDIUM — conversation_messages indexée sur ConversationId seul, sans CreatedAtUtc : la
    pagination d'un fil trie en mémoire.
  - LOW — conversations, conversation_participants, message_attachments, message_hidden_for
    sans CreatedAtUtc.

### NotificationsDbContext
Schéma: notifications
DbSet: Notifications, DeviceTokens, NotificationPreferences, NotificationTemplates,
       ConsumerInbox, IdempotencyKeys (+ OutboxMessages)
Migrations : 20250613000000_InitialNotifications, 20260708000000_AddDeviceTokens,
  20260714152658_AddOutboxRetryTracking, 20260722000000_AddNotificationPreferences,
  20260818075553_AddNotificationTemplatesInboxAndIdempotency,
  20260818183315_SyncModel20260818183313, 20260823000000_AjoutTraceParentOutbox
Snapshot cohérent avec les migrations ? OUI.
Défauts: LOW — notification_preferences sans index (PK = utilisateur, acceptable) ;
  notifications sans CreatedAtUtc explicite au snapshot.
```

#### PromotionsDbContext
```
Schéma: promotions
DbSet: Promotions, PromotionRules, Coupons, CouponUsages, ConsumerInbox, IdempotencyKeys
Migrations : 20260818093539_InitialPromotions, 20260818183324_SyncModel20260818183322,
  20260823000000_AjoutTraceParentOutbox
Snapshot cohérent avec les migrations ? OUI.
Défauts:
  - MEDIUM — le plafond « n usages par compte » (Coupon.PerUserLimit) n'a pour garde qu'un
    index NON unique (CouponId, UserId) (PromotionConfigurations.cs:181). Quand PerUserLimit
    vaut 1, deux paniers simultanés du même client passent tous les deux. Un index unique
    partiel sur (CouponId, UserId) filtré sur Status <> 'Released' fermerait le cas le plus
    fréquent.
  - INFO (bon point) — ux_coupons_code, ux_coupon_usages_live_hold (unique partiel sur
    Status='Held'), ix_coupon_usages_expiring : le raisonnement sur les index partiels est
    exemplaire.
```

#### ReviewsDbContext (common/review-service)
```
Schéma: reviews
DbSet: Reviews (+ OutboxMessages)
Migrations : 20250611000000_InitialReviews, 20250629000000_AddReviewSellerReply,
  20260714152713_AddOutboxRetryTracking, 20260818183332_SyncModel20260818183330,
  20260823000000_AjoutTraceParentOutbox
Snapshot cohérent avec les migrations ? OUI.
Défauts:
  - HIGH — AUCUN index sur reviews.SellerId. ReviewRepository.cs:29 (ListBySellerAsync) et :61
    (GetSellerRatingAsync) balaient toute la table. La note vendeur est recalculée à chaque
    affichage de fiche vendeur.
  - HIGH — GetSellerRatingAsync / GetProductRatingAsync (lignes 43 et 61) chargent TOUTES les
    notes en mémoire pour faire une moyenne. Un vendeur à 200 000 avis fait 200 000 lignes
    remontées par affichage de sa page. Voir §12.
  - INFO (bon point) — unique (BuyerId, ProductId, OrderId) : un avis par achat.
```

#### MediaDbContext, WishlistDbContext, RecommendationsDbContext
```
### MediaDbContext — schéma media — DbSet: Assets
Migrations : 20260812121940_InitialMedia, 20260818183306_SyncModel20260818183304,
  20260823000000_AjoutTraceParentOutbox — snapshot cohérent : OUI.
Défauts: LOW. Seul contexte à porter une trace d'acteur (CreatedByUserId, MediaDbContext.cs:79).
  Index bien choisis (U:ObjectKey, OwnerType+OwnerId, DeletedOnUtc).

### WishlistDbContext — schéma wishlist — DbSet: Wishlists
Migrations : 20250619000000_InitialWishlist, 20260714152728_AddOutboxRetryTracking,
  20260818183345_SyncModel20260818183343, 20260823000000_AjoutTraceParentOutbox — cohérent : OUI.
Défauts: LOW — aucun horodatage sur wishlists / wishlist_items.

### RecommendationsDbContext — schéma recommendations — DbSet: Recommendations
Migrations : 20250623000000_InitialRecommendations, 20260714152709_AddOutboxRetryTracking,
  20260818183328_SyncModel20260818183326, 20260823000000_AjoutTraceParentOutbox — cohérent : OUI.
Défauts: LOW — recommendations sans horodatage : impossible d'expirer une recommandation.
```

---

## 3. Colonnes/tables du modèle qu'AUCUNE migration ne crée réellement

C'est le point 1 de la mission. Le résultat est plus étroit et plus grave que ce que le script
laisse croire : les colonnes `outbox_messages` sont **toutes présentes** partout ; en revanche
deux migrations récentes sont **structurellement invisibles pour EF**.

### 3.0 Les tables `outbox_messages` : vérification exhaustive — RAS

J'ai rejoué, pour chacun des 21 contextes, les 10 propriétés de `OutboxMessage`
(`shared/common/HBA.Shared.Infrastructure/Outbox/OutboxMessage.cs`) contre le DDL réellement émis
(`CreateTable` + `AddColumn` + `ALTER TABLE … ADD COLUMN` en SQL brut) :

| Colonne | Couverture |
|---|---|
| Id, Type, Content, OccurredOnUtc, ProcessedOnUtc, Error | créées par la migration initiale des 21 contextes |
| AttemptCount, NextAttemptAtUtc, DeadLetteredOnUtc | créées, soit par `AddOutboxRetryTracking` (15 contextes), soit directement par la migration initiale (media, promotions, users, deliveries, food, food_ordering, food_cart) |
| TraceParent | créée par `20260823000000_AjoutTraceParentOutbox` (19 contextes, en `migrationBuilder.Sql` avec `ADD COLUMN IF NOT EXISTS`) et par la migration initiale pour food_cart et food_ordering |

Le nom de schéma du SQL brut a été comparé un à un au `SchemaName` du `DbContext` : **les 19
correspondent** (`billing`, `identity`, `media`, `messaging`, `notifications`, `payments`,
`promotions`, `recommendations`, `reviews`, `users`, `settlement`, `wishlist`, `deliveries`,
`food`, `cart`, `catalog`, `inventory`, `ordering`, `sellers`).

**Les deux seules tables `outbox_messages` manquantes sont celles de `delivery_pricing` et
`return_refund`** — parce que ces deux schémas n'ont aucune migration du tout (§2.1). Conséquence :
CRITICAL, aucun événement d'intégration ne sortira jamais de ces deux modules.

Note : `20260818183…_SyncModel…` et `20260823000000_AjoutTraceParentOutbox` émettent le MÊME
`ALTER TABLE … ADD COLUMN IF NOT EXISTS "TraceParent"`. Le `IF NOT EXISTS` rend le doublon
inoffensif, mais c'est deux fois la même vérité dans deux fichiers — exactement ce que le dépôt
combat par ailleurs.

### 3.1 CRITICAL — `ordering.orders."PaymentId"` : déclarée partout, créée par personne

- **Modèle** : `services/marketplace/order-service/src/HBA.Order.Infrastructure/Persistence/Configurations/OrderConfiguration.cs:63` → `builder.Property(o => o.PaymentId);`
- **Snapshot** : `…/Migrations/OrderingDbContextModelSnapshot.cs:56` → `b.Property<Guid?>("PaymentId")`, et l'index ligne 140.
- **Migration** : `…/Migrations/20260824000000_AddOrderPaymentId.cs` — elle contient bien l'`AddColumn` et le `CreateIndex`.
- **Mais** : ce fichier n'a **ni `[Migration("…")]` ni `[DbContext(typeof(OrderingDbContext))]`**, et **aucun `.Designer.cs` compagnon** ne les porte (les 15 autres migrations de ce contexte en ont un, ou portent les attributs en propre).

EF Core découvre les migrations en scannant les types dérivant de `Migration` **qui portent
`MigrationAttribute`** (et dont le `DbContextAttribute` désigne le contexte). Sans ces deux
attributs, la classe est ignorée : elle ne figure pas dans `IMigrationsAssembly.Migrations`,
`Database.Migrate()` ne l'applique jamais, et rien ne le signale à l'exécution.

Conséquence en production, sur base neuve **comme** sur base existante : la colonne n'existe pas,
mais EF l'ajoute à chaque `SELECT` sur `orders`. Le premier `GET /api/v1/orders/{id}` rend

```
42703: column o.PaymentId does not exist
```

Le service démarre (les migrations « passent », il n'y en a qu'une de moins), sert `/health`, et
échoue sur **toutes** les lectures de commande. C'est le même mécanisme, à la colonne près, que
l'incident décrit dans l'en-tête de `check-migrations.py` — et le script ne l'attrape pas
davantage cette fois.

### 3.2 CRITICAL — `payments.payment_refunds` : table entière, même cause

- **Modèle** : `PaymentRefundConfiguration` (`…/Persistence/Configurations/PaymentConfiguration.cs:104-141`), chargée par `ApplyConfigurationsFromAssembly` (`PaymentsDbContext.cs:54`), plus la relation `Payment.Refunds` (`PaymentConfiguration.cs:91-94`).
- **Migration** : `…/Migrations/20260824010000_AddPaymentRefunds.cs` — `CreateTable` de 14 colonnes + 4 index.
- **Mais** : ni `[Migration]`, ni `[DbContext]`, ni `.Designer.cs`. Migration inerte.
- **Et** : `payment_refunds` est **absente du snapshot** (`grep payment_refunds PaymentsDbContextModelSnapshot.cs` → 0). Le snapshot ment donc dans les deux sens : il ignore une table du modèle.

Conséquences :
1. `42P01: relation "payments.payment_refunds" does not exist` au premier `RefundPaymentCommand`, et déjà au chargement d'un paiement si le dépôt inclut `Refunds`.
2. Le snapshot n'ayant jamais vu la table, un `dotnet ef migrations add` futur **régénérerait** un `CreateTable payment_refunds` : sur une base où quelqu'un aurait appliqué le SQL à la main, la nouvelle migration échouerait en `42P07 relation already exists`.

### 3.3 Contrôle exhaustif du reste — RAS

Le rejeu colonne-par-colonne snapshot ↔ migrations sur les 21 contextes ne rend, en dehors de
§3.1/§3.2, que des **faux positifs identifiés et écartés** :

- clés étrangères fantômes des types possédés (`PaymentId` dans le bloc `OwnsOne(Money)` de
  `payments`, `StoreId` dans `OwnsOne(BusinessContact)`, `DeliveryStopDeliveryId`,
  `ProductOfferId`, `MenuItemId`…) : ce sont des propriétés d'ombre, pas des colonnes ;
- `xmin` sur seller_invitations / seller_members / seller_roles : colonne **système** PostgreSQL,
  lue et non créée (`ConcurrencyTokenExtensions.cs:57-65`) ;
- collections primitives jsonb (`roles.permissions`, `notification_preferences.muted_categories`,
  `recommendations.recommended_product_ids`, `attribute_definitions.options`,
  `product_conditions.refurbishment_operations`, `product_revisions.tags`) : déclarées en
  `PrimitiveCollection` / champ de sauvegarde, bien créées par leurs migrations ;
- `identity.addresses` et `identity.payment_methods` créées puis supprimées
  (`MoveAddressesToUsers`, `DropIdentityPaymentMethods`) : absence normale du snapshot.

Fautes de casse d'identifiants en SQL brut (le défaut `se."Metadata"` vs `"metadata"` que le
script prétend chercher) : **aucune**, contrôle refait à la main sur les 45 fichiers en `@"…"`.

---

## 4. Index manquants sur des colonnes de filtrage fréquent

| Sév. | Table.colonne | Requête qui souffre | Fichier |
|---|---|---|---|
| HIGH | `reviews.SellerId` | `Where(r => r.SellerId == sellerId)` + tri `CreatedAtUtc` — page vendeur et calcul de note | `common/review-service/.../ReviewRepository.cs:26-29`, `:58-61` |
| HIGH | `deliveries.QuoteId` | rapprochement course ↔ devis facturé ; aucun index, aucune unicité | `delivery/delivery-service/.../DeliveryConfiguration.cs:47` |
| MEDIUM | `withdrawals.Status` | `ListByStatusAsync` — file d'attente des retraits en console d'admin, exécutée en boucle | `common/wallet-service/.../WalletRepositories.cs:70-72` |
| MEDIUM | `settlement_batches` (aucun index) | `ListAsync` / `ListWithPayoutsAsync` triés par `CreatedAtUtc` | `common/wallet-service/.../SettlementRepositories.cs:112`, `:120` |
| MEDIUM | `conversations` (aucun index) | `Where(c => c.Participants.Any(p => p.UserId == userId)).OrderByDescending(c => c.LastMessageAtUtc)` | `common/notification-service/.../ConversationRepository.cs:38-44` |
| MEDIUM | `conversation_messages.CreatedAtUtc` | pagination d'un fil : index sur `ConversationId` seul, tri en mémoire | `MessagingDbContextModelSnapshot.cs` (index : `ConversationId`) |
| MEDIUM | `commission_rules.EffectiveFromUtc` | `OrderByDescending(r => r.EffectiveFromUtc)` sur toutes les règles | `common/billing-service/.../BillingRepositories.cs:30` |
| MEDIUM | `stock_reservations.ExpiresAtUtc` | balayage des réservations expirées | `marketplace/inventory-service/.../StockReservationConfiguration.cs` (index : `OrderId` seul) |
| MEDIUM | `return_*.ReturnId`, `refund_attempts.RefundId` | toutes les lectures d'un dossier de retour | `marketplace/return-refund-service/.../ReturnRequestConfiguration.cs:42-47`, `:126` |
| LOW | `outbox_messages.ProcessedOnUtc` (index de trop) | `IX_outbox_messages_ProcessedOnUtc` est créé par 15 migrations initiales mais n'existe **plus** au modèle (`OutboxConfiguration` ne déclare que les deux index partiels). Index mort qui coûte une écriture à chaque message. | `shared/common/HBA.Shared.Infrastructure/Outbox/OutboxConfiguration.cs:44-62` vs `*/Migrations/*Initial*.cs` |

---

## 5. Contraintes d'unicité manquantes — et le doublon que chacune laisse passer

| Sév. | Ce qui devrait être unique | Doublon possible | Fichier |
|---|---|---|---|
| CRITICAL | `payments.ProviderReference` | Deux paiements portant la même référence PSP. `GetByProviderReferenceAsync` fait `FirstOrDefaultAsync` : le webhook FedaPay encaisse **l'un des deux au hasard**, l'autre reste Pending pour toujours. | `common/payment-service/.../PaymentConfiguration.cs:98` ; lecture : `PaymentRepository.cs:27-30`, appelée par `GatewayConfirmationCommands.cs:73` |
| CRITICAL | `customer_refunds` — clé d'idempotence (inexistante) | **Deux versements Mobile Money** pour un même remboursement client. Ni clé, ni unique sur `OrderId`, ni sur `ProviderRef`. | `common/wallet-service/.../WalletConfigurations.cs:151-176` ; handler : `CustomerRefundCommands.cs:63-116` |
| HIGH | `payment_refunds.ExternalRefundId` | Deux remboursements pour un même retour, si return-refund rejoue avec une autre clé. | `PaymentConfiguration.cs:137` |
| HIGH | `refunds.IdempotencyKey` (return_refund) | Deux remboursements pour un même dossier de retour. La colonne existe, l'index unique non. | `marketplace/return-refund-service/.../ReturnRequestConfiguration.cs:113` |
| HIGH | `stock_reservations` (InventoryItemId, OrderId) | Deux réservations pour une même commande sur un même article — stock immobilisé, puis libéré une seule fois. | `marketplace/inventory-service/.../StockReservationConfiguration.cs:21` ; domaine : `InventoryItem.cs:132-152` |
| HIGH | `carts` (BuyerId) où Status='Active' | Deux paniers actifs pour un acheteur ; le second masque le premier. | `marketplace/cart-service/.../CartConfiguration.cs:52` ; lecture : `CartRepository.cs:31-35` |
| HIGH | `food_carts` (BuyerId, RestaurantId) où Status='Active' | Idem côté food. | `food/food-cart-service/.../FoodCartConfiguration.cs:45` |
| HIGH | `delivery_quotes.ConsumedByDeliveryId` | Un devis consommé par deux courses. | `delivery/delivery-pricing-service/.../DeliveryQuote.cs:22` (aucune configuration d'index) |
| HIGH | `deliveries.QuoteId` | Deux courses facturées sur le même devis. | `.../DeliveryConfiguration.cs:47` |
| MEDIUM | `coupon_usages` (CouponId, UserId) quand `PerUserLimit = 1` | Un client utilise deux fois un coupon « une fois par compte » via deux paniers simultanés. | `common/promotion-service/.../PromotionConfigurations.cs:181` |
| MEDIUM | `refresh_tokens.TokenHash` | Deux jetons au même hash ; la révocation par `FirstOrDefault` n'en révoque qu'un. | `common/identity-service/.../RefreshTokenConfiguration.cs:59` |
| LOW | `stores` (SellerId, nom) | Deux boutiques homonymes chez un vendeur. | `marketplace/seller-service/.../StoreConfiguration.cs:64` |

Déjà correctement fermées (à mettre au crédit du dépôt) : `orders.CartId` UNIQUE
(`20260823000100_UnicitePanierParCommande` — avec garde-fou anti-doublon existant),
`meal_orders.CartId` UNIQUE, `payment_refunds (PaymentId, IdempotencyKey)` UNIQUE,
`ux_deliveries_reference_source`, `ux_coupons_code`, `ux_coupon_usages_live_hold`,
`ux_wallet_transactions_refund_reversal`, `reviews (BuyerId, ProductId, OrderId)`,
`inventory_items (Sku, LocationId)`, `seller_members (SellerId, UserId)`.

---

## 6. Jeton de concurrence

Extension maison `UsePostgresRowVersion()` (`shared/common/HBA.Shared.Infrastructure/Persistence/ConcurrencyTokenExtensions.cs:57-65`) : mappe `xmin` en jeton de concurrence.

**Présent** (10 agrégats) : `payments` (PaymentConfiguration.cs:43), `orders` (OrderConfiguration.cs:39),
`inventory_items` (InventoryItemConfiguration.cs:56), `seller_wallets` / `platform_wallet` /
`driver_wallets` (WalletConfigurations.cs:43, 78, 133), `seller_roles` / `seller_members` /
`seller_invitations` (MemberConfiguration.cs:57, 144, 290), `restaurants` / `restaurant_staff` /
`food_orders` (FoodConfigurations.cs:45, 264 ; StaffConfiguration.cs:59 ; OrderConfigurations.cs:94).
Cas particulier correct : `return_requests` utilise `Version` + `IsConcurrencyToken()` — mais la
table n'existe pas (§2.1).

**Absent là où il faut** :

| Sév. | Agrégat | Écriture concurrente réelle | Fichier |
|---|---|---|---|
| CRITICAL | `deliveries` (mission de livraison) | `AcceptByDriver` (lire Status → écrire) contre la boucle de dispatch qui expire et re-propose | `delivery/delivery-service/.../DeliveryConfiguration.cs` (aucun `UsePostgresRowVersion`) ; `Delivery.cs:476-496` |
| HIGH | `customer_refunds` | double versement (§5) | `common/wallet-service/.../WalletConfigurations.cs:151` |
| HIGH | `withdrawals` | approbation/réconciliation concurrentes d'un même retrait | `common/wallet-service/.../WalletConfigurations.cs:182-215` (`WithdrawalConfiguration`, aucun `UsePostgresRowVersion`) |
| MEDIUM | `meal_orders` | mêmes transitions de saga qu'`orders`, qui, lui, est protégé | `food/food-order-service/.../MealOrderConfiguration.cs` |
| MEDIUM | `drivers` | AccountStatus / Availability mutés par le dispatch et par le livreur | `delivery/delivery-service/.../DriverConfiguration.cs` |
| MEDIUM | `carts` / `food_carts` | deux onglets, deux ajouts | `CartConfiguration.cs`, `FoodCartConfiguration.cs` |
| MEDIUM | `invoices`, `commission_rules` | édition concurrente par deux administrateurs | `common/billing-service/.../BillingConfigurations.cs` |
| MEDIUM | `promotions` (`BudgetConsumed`) | deux réservations de coupon simultanées incrémentent le même compteur d'enveloppe → budget dépassé | `common/promotion-service/.../PromotionConfigurations.cs:34` (aucun `UsePostgresRowVersion` dans le fichier) |

---

## 7. Types monétaires

**Résultat du balayage : aucun montant en `double`, `float` ou `real`, et aucun `decimal` sans
précision.**

- Les 80 propriétés `decimal` des 21 snapshots portent toutes une précision explicite
  (`numeric(18,2)` × 51, `numeric(12,2)` × 14 pour les courses, `numeric(5,4)` pour un taux,
  `numeric(9,3)` pour un poids, `numeric(3,2)` pour une note, `numeric(5,2)` pour un pourcentage).
- Les 26 colonnes `double precision` du dépôt sont toutes des **coordonnées** (`*_latitude`,
  `*_longitude`), un **score** de recommandation, une **distance** (`DistanceKm`) ou un
  **facteur routier** (`RoadFactor`) — jamais de l'argent.
- Deux modules stockent la monnaie en entier signé assumé : `promotions` (`Value`, `Budget`,
  `BudgetConsumed` en `bigint`, justifié en commentaire — le franc CFA n'a pas de sous-unité,
  `PromotionConfigurations.cs:30-34`) et `delivery_pricing` (`Subtotal`, `Discount`, `Total` en
  `long`, `DeliveryQuote.cs:15-19`). C'est cohérent, mais **la cohabitation de deux
  représentations de l'argent dans un même système** (`numeric(18,2)` ailleurs) est un risque de
  conversion : `MEDIUM`, à documenter, pas à corriger à l'aveugle.

---

## 8. Suppression en cascade sur des données financières ou historiques

51 `DeleteBehavior.Cascade` dans le dépôt. La majorité relie un agrégat à ses enfants possédés
(légitime). Les cas à reprendre :

| Sév. | Relation | Ce qui disparaît | Fichier |
|---|---|---|---|
| HIGH | `payments` → `payment_refunds` | l'historique des remboursements d'un paiement — la preuve qu'un client a été remboursé | `common/payment-service/.../PaymentConfiguration.cs:94` |
| HIGH | `return_requests` → `refunds` → `refund_attempts` | toute la trace des tentatives de remboursement d'un retour | `marketplace/return-refund-service/.../ReturnRequestConfiguration.cs:46`, `:126` |
| MEDIUM | `settlement_batches` → `payouts` | le détail de ce qui a été versé à chaque vendeur lors d'un lot | `common/wallet-service/.../SettlementConfigurations.cs:97` |
| MEDIUM | `invoices` → `invoice_lines` | le détail d'une facture | `common/billing-service/.../BillingConfigurations.cs:78` |
| MEDIUM | `orders` → `order_lines` → `order_line_options` | ce qui a été vendu | `marketplace/order-service/.../OrderConfiguration.cs:125`, `:198` |
| MEDIUM | `return_requests` → `return_status_history` | l'historique des transitions, précisément ce qu'on relit en litige | `ReturnRequestConfiguration.cs:47` |

Pour les tables financières, `DeleteBehavior.Restrict` + suppression logique est le geste attendu :
un `DELETE FROM payments WHERE …` mal ciblé efface aujourd'hui la comptabilité des remboursements
sans qu'aucune contrainte ne s'y oppose.

---

## 9. Colonnes nullable incorrectes & horodatage

### 9.1 Nullable

- **HIGH — `restaurants.PayoutSellerId` nullable** (`20260820000000_DossierDeReversementDuRestaurant.cs:37-42`) : le choix est argumenté (on ne veut pas désigner un compte Mobile Money au hasard) et **correct** ; mais rien en base n'empêche un restaurant de passer en `Submitted` sans dossier. `Restaurant.cs:582` le vérifie en mémoire seulement. Une contrainte `CHECK (Status <> 'Submitted' OR "PayoutSellerId" IS NOT NULL)` porterait la règle là où elle tient.
- **MEDIUM — `orders.PaymentId` nullable** : correct (une commande non payée n'en a pas), mais aucune contrainte ne lie `Status='Paid'` à sa présence.
- **MEDIUM — `deliveries.QuoteId`, `Price`, `Currency`, `DriverEarning`, `DriverShareRate` nullables** (`DeliveryConfiguration.cs:47-55`) alors qu'une course livrée en a nécessairement. Aucun `CHECK` d'état.
- **MEDIUM (inverse) — `order_lines.RestaurantId` / `MenuItemId` `IsRequired()`** (`OrderConfiguration.cs:190-191`) et remplis de `'00000000-…'` pour une ligne de marchandise ; `Sku` `IsRequired()` mais « possiblement vide » (ligne 186). La colonne garde une contrainte qui ne dit plus rien : une valeur sentinelle traverse un `NOT NULL`. Le commentaire l'assume ; le prix est un index partiel de plus (`HasFilter("\"Kind\" = 'Food'")`, ligne 219) et une lecture impossible sans connaître la convention.

### 9.2 Horodatage

- **Type : conforme partout.** Les 286 colonnes datées des migrations et les 275 des snapshots sont **toutes** en `timestamp with time zone`. Aucun `timestamp without time zone`, aucun `date` détourné.
- **Couverture : très inégale.** Sur les 131 tables décrites par les 21 snapshots, **73 n'ont aucune** colonne `Created*Utc`, dont des agrégats muables : `inventory_items`, `carts`, `food_carts`, `drivers`, `wishlists`, `recommendations`, `conversations`, `commission_rules`, `food_orders`, `product_variants`, `brands`, `categories`, `roles`, `user_roles`, `devices`.
- **`Updated*Utc` : quasi absent.** 41 tables ont un `Created*Utc` sans jamais de `Updated*Utc` (17 seulement portent les deux), dont `ordering.orders`, `payments.payments`, `deliveries.deliveries`, `settlement.withdrawals`, `settlement.seller_earnings`, `settlement.customer_refunds`, `promotions.coupons`, `billing.invoices`, `reviews.reviews`. Ce sont précisément les agrégats dont l'état change plusieurs fois. Sur `payments`, on peut dire *quand un paiement a été créé* et *quand il a été capturé* (colonne dédiée), mais pas *quand la ligne a été touchée la dernière fois* — la question que l'on pose en incident.
- Seuls 10 fichiers de configuration déclarent un `Updated*Utc` (catalog produits/offres, sellers stores/membres, food staff/orders/restaurants, notifications preferences, users preferences, wallet).

---

## 10. Transaction absente là où plusieurs agrégats changent

**Aucun `BeginTransactionAsync` dans tout le dépôt** (`grep -rn "BeginTransactionAsync" services shared` → 0 résultat). Le modèle retenu est « une transaction = un `SaveChangesAsync` », ce qui est cohérent avec l'outbox et suffisant tant qu'un handler n'écrit qu'une fois.

18 méthodes appellent `SaveChangesAsync` **plusieurs fois**. Les cas problématiques :

| Sév. | Handler | Ce qui casse | Fichier |
|---|---|---|---|
| CRITICAL | `InitiateCustomerRefundCommandHandler` | L'appel PSP Mobile Money (`SendMobileMoneyPayoutAsync`) est fait **avant tout `SaveChangesAsync`**. Crash entre les deux → argent parti, aucune ligne `customer_refunds`, réconciliation aveugle (`ListProcessingAsync` ne voit rien). | `common/wallet-service/.../Wallets/CustomerRefundCommands.cs:100-140` |
| HIGH | `RefundPaymentCommandHandler` | 3 `SaveChangesAsync` autour d'un appel PSP. Ici c'est **volontaire et correct** (on persiste `Processing` avant d'appeler le prestataire) — mais l'échec entre le `SaveChanges` de `Processing` et celui de `Succeeded` laisse un remboursement en `Processing` que rien ne réconcilie côté payments (contrairement à wallet qui a un `ReconcileCustomerRefunds`). | `common/payment-service/.../PaymentLifecycleCommands.cs:158-270` |
| MEDIUM | `ExecuteRefundCommandHandler` (return-refund) | 2 `SaveChangesAsync` autour d'un appel externe, sans table (§2.1). | `marketplace/return-refund-service/.../ExecuteRefund/ExecuteRefundCommandHandler.cs:32+` |
| MEDIUM | `MarkDeliveredCommandHandler` | 2 `SaveChangesAsync` : la course et le gain livreur ne changent pas dans la même transaction. | `delivery/delivery-service/.../MarkPickedUp/DeliveryProgressCommands.cs:143+` |
| MEDIUM | `ApproveWithdrawalCommandHandler` | 2 `SaveChangesAsync` autour du versement. | `common/wallet-service/.../Wallets/WalletCommands.cs:208+` |

À noter au crédit : `CreateSettlementBatchCommandHandler` mute N portefeuilles + N gains + 1 lot et
n'appelle `SaveChangesAsync` qu'**une** fois (`SettlementCommands.cs:260-261`) — l'atomicité est
là où elle compte le plus.

---

## 11. N+1

| Sév. | Emplacement | Ce qui se passe |
|---|---|---|
| MEDIUM | `common/wallet-service/.../Batches/SettlementCommands.cs:144-147` | `foreach (var group in settleable.GroupBy(e => e.SellerId))` → `await _walletRepository.GetBySellerAsync(...)` : un aller-retour par vendeur. Un lot mensuel à 5 000 vendeurs = 5 000 requêtes dans une transaction unique, donc un verrou tenu d'autant plus longtemps. |
| MEDIUM | `common/wallet-service/.../SettlementCommands.cs:341-343` | `foreach (var payout in batch.Payouts)` → `GetBySellerAsync` par payout, à l'annulation d'un lot. |
| MEDIUM | `marketplace/catalog-service/.../CreateProduct/CreateProductCommandHandler.cs:98-106` et `CreateProductWithImages/CreateProductWithImagesCommand.cs:145-153` | `for (var n = 2; n <= 100; n++) { if (!await _productRepository.SlugExistsAsync(...)) }` : jusqu'à **99 requêtes** pour trouver un slug libre, à chaque création de produit portant un nom fréquent. Une seule requête `LIKE 'slug-%'` suffirait. |
| LOW | `common/identity-service/.../IdentityDataSeeder.cs:65-67` | une requête `AnyAsync` par rôle par défaut ; démarrage seulement. |
| LOW | `marketplace/seller-service/.../MerchantsDataSeeder.cs:45-49` | idem. |

Chargement paresseux : **désactivé partout** (`grep UseLazyLoadingProxies` → 0). Les dépôts font
des `Include` explicites, et les configurations le commentent (`PromotionRepositories.cs:6-22`).
C'est le bon choix ; le risque résiduel est l'`Include` **oublié** (collection vide silencieuse),
signalé par le dépôt lui-même mais non vérifiable statiquement au-delà de la lecture.

---

## 12. Requêtes non paginées

94 `ToListAsync()` sans `Take`/`Skip`. Les plus dangereuses, par ordre de dégât :

| Sév. | Requête | Ce qui explose en production |
|---|---|---|
| HIGH | `InventoryItemRepository.cs:73-75` — `ListLowStockAsync` : `_dbContext.InventoryItems.Include(i => i.Reservations).ToListAsync()` puis `.Where(i => i.IsLowStock)` en mémoire | **Toute** la table de stock + **toutes** les réservations remontées à chaque consultation du tableau de bord « stock bas ». Sur un catalogue à 500 000 SKU × N localisations, c'est un OOM du service et une saturation du pool de connexions. |
| HIGH | `ReviewRepository.cs:39-46` et `:57-64` — `GetProductRatingAsync` / `GetSellerRatingAsync` | Toutes les notes d'un produit / d'un vendeur chargées pour calculer une moyenne. Un `AVG()` SQL fait le même travail en une ligne. Sur la fiche d'un vendeur à succès, la page devient injouable. |
| HIGH | `OrderRepository.cs:44-49` et `:72-77` — historique acheteur et vendeur | `Include` + `AsSplitQuery` sur **toutes** les commandes d'un acheteur ou d'un vendeur. Le vendeur à 100 000 commandes fait tomber la requête. |
| HIGH | `SettlementRepositories.cs:20-22`, `:32-34`, `:73-75`, `:91-93` | Tous les gains d'une période / d'un vendeur. C'est la source du lot de reversement : il grossit linéairement avec le chiffre d'affaires. |
| MEDIUM | `BillingRepositories.cs:47-50` | Toutes les factures d'un vendeur, avec leurs lignes. |
| MEDIUM | `WalletRepositories.cs:58-61`, `:70-72`, `:109-111` | Tous les retraits d'un vendeur ; tous les retraits d'un statut ; tous les remboursements en cours. |
| MEDIUM | `ConversationRepository.cs:38-44` | Toutes les conversations d'un utilisateur, avec messages, réactions et masquages (`AsSplitQuery`), sans index sur `conversations`. |
| MEDIUM | `MealOrderRepository.cs:31-36`, `:40-45` | Tout l'historique de commandes d'un client / d'un restaurant, avec lignes et options. |
| MEDIUM | `FoodRepositories.cs:225-232` | Toutes les commandes non terminales d'un restaurant (l'écran cuisine) — borné en pratique, non borné en droit. |
| MEDIUM | `CategoryRepository.cs:24-27` et `:50-53`, `BrandRepository.cs:36-39`, `AttributeRepositories.cs:30-33` | Arbre de catégories, marques, définitions d'attributs : petites tables aujourd'hui, aucune borne demain. |
| MEDIUM | `PartnerRepository.cs:39-42`, `SellerModuleApi.cs:53-57`, `MemberRepositories.cs:52-55` | Listes d'administration sans pagination. |
| LOW | `RoleRepository.cs:38`, `DeviceTokenRepository.cs:22` | Tables réellement bornées ; à surveiller. |

---

## 13. Audit — trace de l'acteur

`ModuleDbContext.RecordAuditTrail()` (`shared/common/HBA.Shared.Infrastructure/Persistence/ModuleDbContext.cs:100-250`) est un mécanisme complet et bien conçu : acteur, type d'acteur, corrélation, opération, instant unique par transaction, remontée des types possédés vers leur propriétaire, refus d'inventer un acteur pour un traitement automatique.

**Il est activé sur 3 contextes sur 23**, et l'un des trois n'a pas de table :

| Contexte | `KeepsAuditTrail` | Table `audit_entries` créée ? |
|---|---|---|
| `SellersDbContext` | true (`SellersDbContext.cs:84`) | OUI (`20260819160000_JournalDAudit.cs:49`) |
| `MealOrderingDbContext` | true (`MealOrderingDbContext.cs:45`) | OUI (`20260819190000_InitialFoodOrdering.cs:138`) |
| `ReturnRefundDbContext` | true (`ReturnRefundDbContext.cs:28`) | **NON** — voir §2.1 |
| 20 autres | false (défaut) | sans objet |

Conséquence : **aucune trace d'acteur sur les gestes les plus sensibles du système** —

- qui a approuvé un retrait vendeur (`ApproveWithdrawalCommand`, wallet) ;
- qui a déclenché un remboursement client Mobile Money (`InitiateCustomerRefundCommand`) ;
- qui a remboursé un paiement (`RefundPaymentCommand`, payments) ;
- qui a annulé une commande ou l'a mise en arbitrage (`OrderLifecycleCommands`, ordering) ;
- qui a modifié une règle de commission (billing) ;
- qui a suspendu un compte ou changé un rôle (identity).

Aucun mécanisme de substitution : le seul champ d'acteur du dépôt, hors `audit_entries`, est
`media_assets.CreatedByUserId` (`MediaDbContext.cs:79`). Sévérité **HIGH** sur les trois contextes
financiers (payments, settlement, billing) et sur ordering.

À noter : la prudence de l'encadré `KeepsAuditTrail` (activer « module par module, DANS LE MÊME
COMMIT que sa migration ») est saine — mais elle sert ici de justification permanente à ne pas le
faire. Trois modules en dix-huit mois, aucun financier.

---

## 14. Requêtes non traduisibles côté serveur

| Sév. | Emplacement | Ce qui se passe |
|---|---|---|
| MEDIUM | `common/identity-service/.../RoleRepository.cs:34-40` | `var roles = await _dbContext.Roles.ToListAsync(ct); return roles.Where(r => guidSet.Contains(r.Id.Value)).ToList();` — la table entière est ramenée puis filtrée en C#, **explicitement** pour contourner la traduction d'un `Contains` sur une clé à value converter. Le commentaire l'assume (« table de petite taille ») ; le contournement est ce que le point 13 vise. Une projection sur `Guid` avant le `Contains` se traduit sans difficulté. |
| MEDIUM | `common/review-service/.../ReviewRepository.cs:39-46`, `:57-64` | `.Select(r => r.Rating).ToListAsync()` puis `ratings.Average(r => r.Value)` : la moyenne est faite en mémoire, pour ne pas traduire le VO `Rating`. Même contournement, même conséquence (§12). |
| MEDIUM | `marketplace/inventory-service/.../InventoryItemRepository.cs:73-75` | `.ToListAsync()` puis `.Where(i => i.IsLowStock)` : `IsLowStock` est une propriété C# calculée (`Ignore`), donc intraduisible. Le filtre part côté client sur la table entière. |
| LOW | `common/identity-service/.../IdentityDataSeeder.cs:234`, `:285` | `FirstOrDefaultAsync(u => u.Email == emailResult.Value)` : `emailResult.Value` est évalué **avant** la requête (c'est un `Result<T>`, pas une navigation), donc traduisible. Faux positif du motif `.Value ==` — signalé pour mémoire. |
| LOW | `food/restaurant-service/.../Orders/KitchenQueries.cs:74` | `commande.Items.AsEnumerable()` sur une collection **déjà chargée** : bascule volontaire et sans effet base. |

Aucun usage de `.Value ==` sur un identifiant fortement typé **à l'intérieur** d'un `Where` traduit
n'a été trouvé : les identifiants passent par `HasConversion`, et les dépôts comparent des types
forts (`p.Id == id`), ce qui se traduit correctement.

---

## 15. Ce qu'il faut corriger en premier

1. Ajouter `[DbContext]` + `[Migration]` à `20260824000000_AddOrderPaymentId.cs` et
   `20260824010000_AddPaymentRefunds.cs`, et réaligner `PaymentsDbContextModelSnapshot` sur
   `payment_refunds`. Sans cela, order-service et payment-service sont hors service dès la
   première lecture (§3.1, §3.2).
2. Écrire les migrations initiales de `delivery_pricing` et `return_refund`, outbox et
   `audit_entries` comprises (§2.1).
3. Clé d'idempotence + unique sur `customer_refunds`, et déplacer l'appel PSP **après** le
   premier `SaveChangesAsync` (§5, §10).
4. `payments.ProviderReference` en UNIQUE (§5).
5. `UsePostgresRowVersion()` sur `deliveries` (§6).
6. Ajouter au CI un contrôle **colonne par colonne** et un contrôle « toute classe dérivant de
   `Migration` porte `[Migration]` et `[DbContext]` » — les deux angles morts qui ont produit
   §3.1 et §3.2 ; et retirer ou réécrire `check_sql_identifier_case`, qui ne peut pas se
   déclencher (§1, L3).
