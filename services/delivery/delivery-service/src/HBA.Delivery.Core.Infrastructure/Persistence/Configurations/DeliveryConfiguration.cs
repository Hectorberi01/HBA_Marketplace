using HBA.Deliveries.Domain.Deliveries;
using HBA.Shared.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HBA.Deliveries.Infrastructure.Persistence.Configurations;

internal sealed class DeliveryConfiguration : IEntityTypeConfiguration<Domain.Deliveries.Delivery>
{
    public void Configure(EntityTypeBuilder<Domain.Deliveries.Delivery> builder)
    {
        // ══════════════════════════════════════════════════════════════════════
        // VERROU OPTIMISTE — ISSUE-028. DEUX LIVREURS NE PEUVENT PLUS ACCEPTER LA
        // MÊME COURSE.
        //
        // CE QUI ÉTAIT CASSÉ.
        //
        // `Delivery` était le seul agrégat vivant de ce contexte sans jeton de
        // concurrence. Deux requêtes d'acceptation concurrentes lisaient toutes
        // deux `Status = DriverAssigned`, passaient toutes deux la garde de
        // `AcceptByDriver`, et la SECONDE écriture écrasait la première en
        // silence : `AssignedDriverId` finissait sur le dernier arrivé, alors que
        // `DeliveryAcceptedDomainEvent` avait été levé DEUX FOIS. Deux livreurs
        // partaient chercher la même commande, tous deux rémunérés.
        //
        // ET ICI LE JETON EST RÉELLEMENT ÉVALUÉ — CE N'EST PAS LE CAS DE
        //    `InventoryItem`.
        //
        // L'encadré de `InventoryItem.StockVersion` décrit le piège : une mutation
        // qui n'écrit que des lignes ENFANTS n'émet aucun `UPDATE` sur le parent,
        // le jeton n'entre dans la clause `WHERE` d'aucune requête, et le verrou est
        // TOTALEMENT INERTE tout en restant visible à la relecture.
        //
        // `AcceptByDriver` n'est pas dans ce cas, et pour une raison vérifiable :
        // outre `offer.Accept()` qui touche `delivery_assignments`, elle écrit
        // TROIS colonnes de la ligne parente — `Status`, `AssignedDriverId`,
        // `AcceptedAtUtc`. EF émet donc bien
        // `UPDATE deliveries SET … WHERE "Id" = … AND xmin = …`, le second
        // écrivain touche 0 ligne et lève `DbUpdateConcurrencyException` → 409.
        // Il en va de même de `AssignTo`, `RejectByDriver`, `RevokeAssignment` et
        // de toutes les transitions d'exécution : aucune ne mute uniquement un
        // enfant. AUCUN compteur à la `StockVersion` n'est donc nécessaire.
        //
        // RÈGLE À TENIR : le jour où quelqu'un ajoute une opération qui ne mute
        // qu'une `DeliveryAssignment` — marquer une proposition « vue », noter un
        // délai de réponse — sans toucher la course, ce verrou redeviendra
        // décoratif SUR CE CHEMIN-LÀ, et il faudra le compteur.
        // ══════════════════════════════════════════════════════════════════════
        builder.UsePostgresRowVersion();
        builder.HorodateLesModifications();

        // ═════════════════════════════════════════════════════════════════════
        // CE QUE L'AUDIT DEMANDAIT ICI AURAIT REJETÉ DES COURSES LÉGITIMES.
        //
        // La demande était : « une course livrée a nécessairement un prix et un
        // gain livreur, donc CHECK (Status <> 'Delivered' OR Price IS NOT NULL
        // AND DriverEarning IS NOT NULL) ». C'est FAUX, et le domaine dit
        // pourquoi, en toutes lettres, dans `Delivery.MarkDelivered` :
        //
        //     « PAS DE PRIX, PAS DE GAIN — ET C'EST UN SIGNAL, PAS UN DÉTAIL.
        //       Mettre zéro serait exact arithmétiquement et faux dans les
        //       faits : le livreur a bien roulé. On laisse donc NUL. »
        //
        // Une course peut être livrée sans devis rattaché — course interne,
        // reprise manuelle, partenaire hors tarification. La contrainte demandée
        // aurait fait échouer la remise du colis pour faire respecter une règle
        // que le code refuse délibérément d'appliquer.
        //
        // CE QUI EST VRAI, EN REVANCHE, C'EST LA COHÉRENCE ENTRE CES COLONNES.
        //
        //   • `AttachQuote` pose `Price` ET `Currency` ensemble : un montant sans
        //     devise n'est ni facturable ni versable — personne ne sait en quoi
        //     il est libellé.
        //   • `MarkDelivered` ne calcule `DriverEarning` QUE dans
        //     `if (Price is { } price)`, et pose `DriverShareRate` dans le même
        //     bloc : un gain existe donc toujours AVEC le prix et le taux dont il
        //     est dérivé. Un gain orphelin serait un montant que personne ne peut
        //     recalculer ni contester — et c'est de l'argent dû à quelqu'un.
        //
        // Ces deux contraintes-là portent une règle que le code tient déjà ; la
        // troisième aurait porté une règle que le code refuse.
        // ═════════════════════════════════════════════════════════════════════
        builder.ToTable("deliveries", t =>
        {
            t.HasCheckConstraint(
                "ck_deliveries_price_has_currency",
                "\"Price\" IS NULL OR \"Currency\" IS NOT NULL");

            t.HasCheckConstraint(
                "ck_deliveries_earning_has_basis",
                "\"DriverEarning\" IS NULL "
                + "OR (\"Price\" IS NOT NULL AND \"DriverShareRate\" IS NOT NULL)");
        });

        builder.HasKey(d => d.Id);

        builder.Property(d => d.Id)
            .HasConversion(id => id.Value, value => new DeliveryId(value))
            .ValueGeneratedNever();

        builder.Property(d => d.Reference).HasMaxLength(120).IsRequired();

        // Les énumérations sont stockées en TEXTE, pas en entier.
        //
        // Un entier oblige à ouvrir le code pour lire une ligne, et surtout il se
        // décale silencieusement le jour où quelqu'un insère une valeur au milieu
        // de l'énumération. « Delivered » reste « Delivered ».
        builder.Property(d => d.Source).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(d => d.Type).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(d => d.Status).HasConversion<string>().HasMaxLength(24).IsRequired();
        builder.Property(d => d.RequiredProof).HasConversion<string>().HasMaxLength(16).IsRequired();

        builder.Property(d => d.AssignedDriverId)
            .HasConversion(
                id => id!.Value.Value,
                value => new DriverId(value));

        // Renseigné pour les seules courses de source externe (invariant tenu par
        // l'agrégat). Indexé : la facturation et le quota interrogent par partenaire.
        builder.Property(d => d.PartnerId);

        builder.HasIndex(d => new { d.PartnerId, d.CreatedAtUtc })
            .HasDatabaseName("ix_deliveries_partner")
            .HasFilter("\"PartnerId\" IS NOT NULL");

        // Devis d'origine et montant figé. Le prix est RECOPIÉ ici plutôt que lu
        // à travers le devis : celui-ci sera purgé, la course doit garder trace
        // de ce qui a été facturé.
        builder.Property(d => d.QuoteId);

        // ═════════════════════════════════════════════════════════════════════
        // UN DEVIS NE PAIE QU'UNE SEULE COURSE (§5) — ET CE N'EST QUE LE FILET.
        //
        // La cause était dans `EfDeliveryPricingStore.ConsumeQuoteAsync` : lire,
        // tester `ACTIVE`, écrire `CONSUMED`, sans rien qui tienne la ligne entre
        // les deux. Deux courses concurrentes passaient toutes deux, et la
        // plateforme payait deux livraisons pour un devis. Elle est fermée là-bas,
        // par un `UPDATE … WHERE Status = 'ACTIVE'` atomique.
        //
        // Cet index garde l'AUTRE bout : il refuse que deux courses citent le même
        // devis, quel que soit le chemin qui les écrit — y compris un chemin futur
        // qui ne passerait pas par le magasin de tarification.
        //
        // FILTRÉ SUR `QuoteId IS NOT NULL` : PostgreSQL tolère déjà plusieurs
        // `NULL` dans un index unique, donc le filtre ne change pas la sémantique —
        // il évite d'indexer la majorité des courses, qui n'ont pas de devis.
        // ═════════════════════════════════════════════════════════════════════
        builder.HasIndex(d => d.QuoteId)
            .IsUnique()
            .HasDatabaseName("ux_deliveries_quote")
            .HasFilter("\"QuoteId\" IS NOT NULL");
        builder.Property(d => d.Price).HasPrecision(12, 2);
        builder.Property(d => d.Currency).HasMaxLength(3);

        // Part du livreur, figée à la remise. Le TAUX est conservé à côté du
        // montant : sans lui, un gain contesté six mois plus tard ne peut plus
        // être refait — voir l'encadré sur Delivery.DriverEarning.
        builder.Property(d => d.DriverEarning).HasPrecision(12, 2);
        builder.Property(d => d.DriverShareRate).HasPrecision(5, 4);

        // Le code remis au destinataire. Il n'est JAMAIS projeté vers
        // l'application livreur — voir l'encadré sur Delivery.IssuedPin.
        builder.Property(d => d.IssuedPin).HasMaxLength(8);

        // Le compteur de tentatives infructueuses. Il DOIT être persisté : en
        // mémoire seulement, il serait remis à zéro à chaque requête et le livreur
        // disposerait de tentatives infinies — le compteur existerait, les tests
        // passeraient, et la faille resterait entière.
        builder.Property(d => d.FailedProofAttempts).IsRequired();

        builder.Property(d => d.ScheduledForUtc);

        // Index partiel : la boucle de dispatch cherche les courses programmées
        // dont la fenêtre s'ouvre. Sans filtre, l'index porterait sur toutes les
        // courses, dont l'écrasante majorité n'est pas programmée.
        builder.HasIndex(d => d.ScheduledForUtc)
            .HasFilter("\"ScheduledForUtc\" IS NOT NULL")
            .HasDatabaseName("ix_deliveries_scheduled_for");

        builder.OwnsOne(d => d.Proof, proof =>
        {
            proof.Property(p => p.Kind).HasColumnName("proof_kind").HasConversion<string>().HasMaxLength(20);
            proof.Property(p => p.Value).HasColumnName("proof_value").HasMaxLength(500);
            proof.Property(p => p.CapturedAtUtc).HasColumnName("proof_captured_at_utc");
        });

        builder.Property(d => d.CreatedAtUtc).IsRequired();
        builder.Property(d => d.AcceptedAtUtc);
        builder.Property(d => d.PickedUpAtUtc);
        builder.Property(d => d.DeliveredAtUtc);
        builder.Property(d => d.CancelledAtUtc);
        builder.Property(d => d.CancellationReason).HasMaxLength(300);

        // Points de collecte et de remise : objets-valeurs, donc COLONNES de la
        // table des courses. Ils n'ont pas d'existence propre et ne sont jamais
        // interrogés seuls — en faire des tables ajouterait deux jointures à
        // chaque lecture, pour aucun bénéfice.
        builder.OwnsOne(d => d.Pickup, ConfigureStop("pickup"));
        builder.OwnsOne(d => d.Dropoff, ConfigureStop("dropoff"));

        builder.OwnsOne(d => d.Package, package =>
        {
            package.Property(p => p.Description).HasColumnName("package_description").HasMaxLength(300).IsRequired();
            package.Property(p => p.WeightKg).HasColumnName("package_weight_kg").HasPrecision(9, 3);
            package.Property(p => p.IsFragile).HasColumnName("package_is_fragile").IsRequired();
            package.Property(p => p.IsPerishable).HasColumnName("package_is_perishable").IsRequired();
        });

        // L'historique des propositions appartient à la course : il naît et meurt
        // avec elle, et personne ne l'interroge hors de son contexte.
        builder.OwnsMany(d => d.Assignments, assignment =>
        {
            assignment.ToTable("delivery_assignments");
            assignment.WithOwner().HasForeignKey("delivery_id");
            assignment.HasKey(a => a.Id);
            assignment.Property(a => a.Id).ValueGeneratedNever();

            assignment.Property(a => a.DriverId)
                .HasConversion(id => id.Value, value => new DriverId(value))
                .IsRequired();

            assignment.Property(a => a.AttemptNumber).IsRequired();
            assignment.Property(a => a.Outcome).HasConversion<string>().HasMaxLength(16).IsRequired();
            assignment.Property(a => a.OfferedAtUtc).IsRequired();
            assignment.Property(a => a.RespondedAtUtc);
            assignment.Property(a => a.Reason).HasMaxLength(300);

            // Retrouver les propositions faites à un livreur : c'est la requête du
            // tableau de bord « pourquoi ce livreur refuse-t-il autant ? ».
            assignment.HasIndex(a => a.DriverId);
        });

        // ─────────────────────────────────────────────────────────────────────
        // INDEX PARTIEL SUR LES COURSES À POURVOIR.
        //
        // La boucle de dispatch pose sans arrêt la même question : « quelles
        // courses cherchent un livreur ? ». Sur une table qui grossit
        // indéfiniment, un index complet sur `Status` deviendrait surtout un
        // index sur « Delivered » — la valeur la plus fréquente, et la seule dont
        // on ne fait jamais rien.
        //
        // Le filtre restreint l'index aux deux états vivants. Il reste petit quel
        // que soit l'historique.
        // ─────────────────────────────────────────────────────────────────────
        builder.HasIndex(d => new { d.Status, d.CreatedAtUtc })
            .HasDatabaseName("ix_deliveries_awaiting_driver")
            .HasFilter("\"Status\" IN ('SearchingDriver', 'NoDriverAvailable')");

        // Idempotence de la création : une référence par source. Voir
        // CreateDeliveryCommandHandler — c'est ce qui empêche un partenaire de
        // créer deux courses en rejouant sa requête après un délai dépassé.
        builder.HasIndex(d => new { d.Reference, d.Source })
            .IsUnique()
            .HasDatabaseName("ux_deliveries_reference_source");

        // ══════════════════════════════════════════════════════════════════════
        // UN LIVREUR N'A QU'UNE COURSE EN COURS — INDEX UNIQUE **PARTIEL**.
        //
        // L'INDEX UNIQUE SEC QUE RÉCLAMAIT L'AUDIT AURAIT ÉTÉ FAUX. On ne le
        // pose PAS.
        //
        // `CREATE UNIQUE INDEX … ON deliveries ("AssignedDriverId")` interdirait à
        // un livreur d'avoir DEUX courses de toute son histoire : la deuxième
        // course de sa carrière serait refusée par la base. C'est absurde
        // métier, et le défaut d'origine n'était pas là.
        //
        // CE QUE LA CONTRAINTE GARANTIT, ET CE QU'ELLE NE GARANTIT PAS.
        //
        // Elle garantit qu'À UN INSTANT DONNÉ, un livreur ne détient qu'UNE course
        // ENGAGÉE — des cinq états qui vont de l'acceptation à l'arrivée chez le
        // destinataire. C'est la règle métier réelle : une moto, un colis, un
        // trajet. Une seconde acceptation par un livreur déjà engagé est refusée
        // PAR LA BASE, même si deux processus concurrents ont lu l'agrégat au même
        // instant — c'est ce que le verrou optimiste ne sait PAS faire, puisque
        // `xmin` est un jeton PAR LIGNE et que ces deux acceptations portent sur
        // DEUX courses différentes (voir `MemberConfiguration` et
        // `ISellerUnitOfWork`, où la même confusion est détaillée).
        //
        // Elle NE garantit PAS :
        //
        //   • qu'une course n'ait qu'un affecté — c'est acquis structurellement,
        //     `AssignedDriverId` est UNE colonne d'UNE ligne ; deux acceptations
        //     sur la MÊME course sont arbitrées par `xmin`, pas par cet index ;
        //   • l'unicité des PROPOSITIONS : `DriverAssigned` est volontairement hors
        //     du filtre. Le dispatch propose à plusieurs candidats —
        //     `DispatchStore.BuildCandidates` en rend deux — et l'un doit pouvoir
        //     recevoir une offre pendant qu'il termine sa course précédente. La
        //     course d'avant se ferme, la nouvelle s'ouvre ;
        //   • le groupage : le jour où HBAExpress laissera un livreur porter deux
        //     colis du même quartier, CET INDEX DEVRA TOMBER. Il encode une
        //     décision d'exploitation, pas une loi de la nature. C'est le prix
        //     assumé : sans lui, rien en base n'empêche la double affectation.
        //
        // LE FILTRE ÉNUMÈRE LES ÉTATS EN TOUTES LETTRES, comme
        // `ix_deliveries_awaiting_driver`. `Status` est stocké en TEXTE : ajouter
        // un état engagé à `DeliveryStatus` sans l'ajouter ICI le laisserait hors
        // contrainte, en silence. C'est le défaut connu de la forme, et il est
        // préféré à un index total qui grossirait indéfiniment.
        // ══════════════════════════════════════════════════════════════════════
        builder.HasIndex(d => d.AssignedDriverId)
            .IsUnique()
            .HasDatabaseName("ux_deliveries_engaged_driver")
            .HasFilter(
                "\"AssignedDriverId\" IS NOT NULL AND \"Status\" IN "
                + "('DriverAccepted', 'ArrivedAtPickup', 'PickedUp', 'InTransit', 'ArrivedAtDropoff')");

        // L'index NON unique d'origine reste : il sert les lectures « les courses
        // de ce livreur », historique compris, que l'index partiel ci-dessus ne
        // peut pas servir puisqu'il ne contient que les courses vivantes.
        builder.HasIndex(d => new { d.AssignedDriverId, d.CreatedAtUtc })
            .HasDatabaseName("ix_deliveries_driver");
    }

    /// <summary>Colonnes d'un point de la course, préfixées par son rôle.</summary>
    private static Action<OwnedNavigationBuilder<Domain.Deliveries.Delivery, DeliveryStop>> ConfigureStop(string prefix)
        => stop =>
        {
            stop.Property(s => s.ContactName).HasColumnName($"{prefix}_contact_name").HasMaxLength(120).IsRequired();
            stop.Property(s => s.Phone).HasColumnName($"{prefix}_phone").HasMaxLength(20).IsRequired();
            stop.Property(s => s.CommuneCode).HasColumnName($"{prefix}_commune_code").HasMaxLength(60).IsRequired();
            stop.Property(s => s.Quartier).HasColumnName($"{prefix}_quartier").HasMaxLength(120);
            stop.Property(s => s.Landmark).HasColumnName($"{prefix}_landmark").HasMaxLength(250).IsRequired();
            stop.Property(s => s.Instructions).HasColumnName($"{prefix}_instructions").HasMaxLength(500);

            // La position est désormais OBLIGATOIRE : la tarification à la
            // distance ne peut pas s'en passer. Les colonnes sont donc NOT NULL,
            // et `Navigation(...).IsRequired()` le dit à EF — sans cette ligne,
            // EF génère des colonnes nullables même quand la propriété C# ne
            // l'est pas, et la base cesserait de garantir ce que le domaine exige.
            stop.OwnsOne(s => s.Position, position =>
            {
                position.Property(p => p.Latitude).HasColumnName($"{prefix}_latitude").IsRequired();
                position.Property(p => p.Longitude).HasColumnName($"{prefix}_longitude").IsRequired();
            });

            stop.Navigation(s => s.Position).IsRequired();
        };
}
