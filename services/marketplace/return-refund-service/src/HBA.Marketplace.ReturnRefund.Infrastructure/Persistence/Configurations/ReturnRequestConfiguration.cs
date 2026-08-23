using HBA.Marketplace.ReturnRefund.Domain.Aggregates.ReturnRequest;
using HBA.Shared.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HBA.Marketplace.ReturnRefund.Infrastructure.Persistence.Configurations;

internal sealed class ReturnRequestConfiguration : IEntityTypeConfiguration<ReturnRequest>
{
    public void Configure(EntityTypeBuilder<ReturnRequest> builder)
    {
        builder.ToTable("return_requests");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.ReturnNumber).HasMaxLength(32).IsRequired();
        builder.HasIndex(r => r.ReturnNumber).IsUnique();
        builder.HasIndex(r => r.OrderId);
        builder.HasIndex(r => new { r.CustomerId, r.CreatedAtUtc });
        builder.HasIndex(r => new { r.SellerId, r.Status });

        // L'INDEX DU BALAYAGE D'EXPIRATION.
        //
        // `ExpireReturnsWorker` demande toutes les dix minutes « quels dossiers
        // `AwaitingApproval` ou `AwaitingReturn` ont dépassé leur date ? ». Sans
        // cet index, chaque tour relit l'intégralité de `return_requests` — une
        // table qui ne fait que grandir — pour n'en retenir presque jamais rien.
        //
        // `Status` d'abord : c'est lui qui élimine la quasi-totalité des lignes.
        builder.HasIndex(r => new { r.Status, r.ExpiresAtUtc });

        builder.Property(r => r.Currency).HasMaxLength(3).IsRequired();
        builder.Property(r => r.EstimatedRefundAmount).HasPrecision(18, 2);
        builder.Property(r => r.ApprovedRefundAmount).HasPrecision(18, 2);
        builder.Property(r => r.ReturnShippingPayer).HasMaxLength(24);
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(40);
        builder.Property(r => r.ResolutionRequested).HasConversion<string>().HasMaxLength(40);
        builder.Property(r => r.ReasonCode).HasConversion<string>().HasMaxLength(64);
        builder.Property(r => r.Version).IsConcurrencyToken();

        builder.OwnsOne(r => r.PolicySnapshot, policy =>
        {
            policy.Property(p => p.PolicyId).HasColumnName("policy_id").HasMaxLength(80);
            policy.Property(p => p.Version).HasColumnName("policy_version").HasMaxLength(32);
            policy.Property(p => p.ReturnWindowDays).HasColumnName("policy_return_window_days");
            policy.Property(p => p.AllowReturn).HasColumnName("policy_allow_return");
            policy.Property(p => p.AllowRefundOnly).HasColumnName("policy_allow_refund_only");
            policy.Property(p => p.RequireEvidence).HasColumnName("policy_require_evidence");
            policy.Property(p => p.RequireInspection).HasColumnName("policy_require_inspection");
            policy.Property(p => p.RestockingFeePercent).HasColumnName("policy_restocking_fee_percent").HasPrecision(5, 2);
            policy.Ignore(p => p.CustomerPaysReturnShippingFor);
            policy.Ignore(p => p.AutoApproveReasons);
        });

        // ═════════════════════════════════════════════════════════════════════
        // `Restrict` SUR LES SIX — UN DOSSIER DE LITIGE EST UNE PREUVE.
        //
        // Les six relations étaient en `Cascade`. Un
        // `DELETE FROM returns.return_requests WHERE …` mal ciblé emportait donc,
        // sans une erreur ni une trace : les remboursements et leurs tentatives
        // PSP, les photos versées par le client, le rapport d'inspection, les
        // expéditions, et l'historique des transitions — c'est-à-dire l'intégralité
        // de ce qu'on relit quand quelqu'un conteste.
        //
        // POURQUOI LES SIX, ALORS QUE L'AUDIT N'EN NOMMAIT QUE DEUX.
        //
        // Ne protéger que `refunds` et `return_status_history` produirait le pire
        // des trois états possibles : un `DELETE` mal ciblé ÉCHOUERAIT sur ces
        // deux tables après avoir déjà effacé les photos, l'inspection et les
        // expéditions — la transaction serait annulée, certes, mais la protection
        // par moitié est un raisonnement fragile qui ne tient que tant que
        // l'effacement est transactionnel. Un dossier de litige se protège
        // entier ou pas du tout.
        //
        // CE QUE CELA COÛTE : un dossier de retour ne peut plus être supprimé.
        // C'est le but. Aucun code du dépôt n'en supprime — ni `Remove`, ni
        // `RemoveRange`, ni `ExecuteDeleteAsync` — donc rien ne casse ; ce qui
        // change, c'est ce qu'une main humaine peut faire par inadvertance.
        //
        // CE QUE CELA NE COUVRE PAS : un `DELETE` visant directement une table
        // fille. Rien ne protège une table de sa propre suppression. Ce qui est
        // fermé ici, c'est l'effacement INVISIBLE — celui qu'on déclenche en
        // croyant n'agir que sur le parent.
        // ═════════════════════════════════════════════════════════════════════
        builder.HasMany(r => r.Items).WithOne().HasForeignKey(i => i.ReturnId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(r => r.Evidence).WithOne().HasForeignKey(e => e.ReturnId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(r => r.Shipments).WithOne().HasForeignKey(s => s.ReturnId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(r => r.Inspections).WithOne().HasForeignKey(i => i.ReturnId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(r => r.Refunds).WithOne().HasForeignKey(r => r.ReturnId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(r => r.History).WithOne().HasForeignKey(h => h.ReturnId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ReturnItemConfiguration : IEntityTypeConfiguration<ReturnItem>
{
    public void Configure(EntityTypeBuilder<ReturnItem> builder)
    {
        builder.ToTable("return_items");
        builder.HasKey(i => i.Id);
        builder.HasIndex(i => i.OrderItemId);
        builder.Property(i => i.SkuSnapshot).HasMaxLength(128);
        builder.Property(i => i.NameSnapshot).HasMaxLength(512);
        builder.Property(i => i.UnitPaidAmount).HasPrecision(18, 2);
        builder.Property(i => i.Currency).HasMaxLength(3);
        builder.Property(i => i.ReasonCode).HasConversion<string>().HasMaxLength(64);
        builder.Property(i => i.ConditionDeclared).HasConversion<string>().HasMaxLength(64);
        builder.Property(i => i.ConditionInspected).HasConversion<string>().HasMaxLength(64);
    }
}

internal sealed class ReturnEvidenceConfiguration : IEntityTypeConfiguration<ReturnEvidence>
{
    public void Configure(EntityTypeBuilder<ReturnEvidence> builder)
    {
        builder.ToTable("return_evidence");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.MediaId).HasMaxLength(128);
        builder.Property(e => e.Kind).HasMaxLength(64);
        builder.Property(e => e.Caption).HasMaxLength(512);
    }
}

internal sealed class ReturnShipmentConfiguration : IEntityTypeConfiguration<ReturnShipment>
{
    public void Configure(EntityTypeBuilder<ReturnShipment> builder)
    {
        builder.ToTable("return_shipments");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.DeliveryId).HasMaxLength(128);
        builder.Property(s => s.Mode).HasMaxLength(64);
        builder.Property(s => s.TrackingNumber).HasMaxLength(128);
    }
}

internal sealed class ReturnInspectionConfiguration : IEntityTypeConfiguration<ReturnInspection>
{
    public void Configure(EntityTypeBuilder<ReturnInspection> builder)
    {
        builder.ToTable("return_inspections");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Condition).HasConversion<string>().HasMaxLength(64);
        builder.Property(i => i.Disposition).HasConversion<string>().HasMaxLength(64);
        builder.Property(i => i.Notes).HasMaxLength(2000);
    }
}

internal sealed class RefundConfiguration : IEntityTypeConfiguration<Refund>
{
    public void Configure(EntityTypeBuilder<Refund> builder)
    {
        builder.ToTable("refunds");
        builder.HasKey(r => r.Id);
        builder.HasIndex(r => r.ReturnId);
        builder.Property(r => r.Amount).HasPrecision(18, 2);
        builder.Property(r => r.Currency).HasMaxLength(3);
        builder.Property(r => r.IdempotencyKey).HasMaxLength(160);

        // ═════════════════════════════════════════════════════════════════════
        // UN REMBOURSEMENT PAR CLÉ — LA COURSE DE `DecideRefund` SE FERME ICI.
        //
        // La colonne existait sans unicité : deux appels SIMULTANÉS à
        // `POST /{id}/refund-decision` chargent tous deux l'agrégat, lisent tous
        // deux `_refunds.Count == 0`, produisent tous deux la clé
        // `return:{ReturnId}:refund:1` et écrivent DEUX remboursements sur le même
        // dossier. `RefundCalculationPolicy` ne les voit pas : elle contrôle un
        // total déjà remboursé que ni l'une ni l'autre n'a encore écrit.
        //
        // PORTÉE : LA SEULE COLONNE, PAS `(ReturnId, IdempotencyKey)`.
        //
        // Contrairement à `payment_refunds`, la clé n'est jamais fournie par
        // l'appelant : `ReturnRequest.DecideRefund` est son unique producteur et la
        // fabrique en `return:{Id}:refund:{n}`. Le Guid du dossier y est DÉJÀ, donc
        // deux dossiers ne peuvent pas se croiser et `ReturnId` dans l'index ne
        // discriminerait rien de plus.
        //
        // Il ferait même moins : un couple `(ReturnId, Key)` laisserait passer une
        // clé qui se répète d'un dossier à l'autre — impossible aujourd'hui, mais
        // ce serait exactement le symptôme d'un générateur de clé cassé, et c'est
        // la chose qu'on veut voir échouer plutôt que constater six mois plus tard.
        //
        // Pas de filtre : la colonne est NOT NULL (voir le snapshot et
        // `InitialReturnRefund`), il n'y a rien à exclure de l'index.
        // ═════════════════════════════════════════════════════════════════════
        builder.HasIndex(r => r.IdempotencyKey).IsUnique();

        // L'INDEX DU BALAYAGE D'EXÉCUTION.
        //
        // `RefundRetryWorker` demande toutes les vingt secondes les remboursements
        // `Pending`, `Processing` ou `Failed`, du plus ancien au plus récent. C'est
        // la requête la plus fréquente du module, et sans index elle parcourt toute
        // la table des remboursements — dont l'écrasante majorité est `Succeeded`,
        // c'est-à-dire exactement ce qu'on ne veut pas lire.
        builder.HasIndex(r => new { r.Status, r.CreatedAtUtc });

        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(64);
        builder.Property(r => r.ProviderRefundId).HasMaxLength(128);
        builder.OwnsOne(r => r.Breakdown, b =>
        {
            b.OwnsOne(x => x.Items, m => MapMoney(m, "items"));
            b.OwnsOne(x => x.Tax, m => MapMoney(m, "tax"));
            b.OwnsOne(x => x.OriginalShipping, m => MapMoney(m, "original_shipping"));
            b.OwnsOne(x => x.DiscountAllocation, m => MapMoney(m, "discount_allocation"));
            b.OwnsOne(x => x.RestockingFee, m => MapMoney(m, "restocking_fee"));
            b.OwnsOne(x => x.ReturnShippingCharge, m => MapMoney(m, "return_shipping_charge"));
            b.OwnsOne(x => x.PreviousRefunds, m => MapMoney(m, "previous_refunds"));
        });
        // `Restrict` — LE SECOND NIVEAU DE LA CHAÎNE.
        //
        // `return_requests → refunds → refund_attempts` cascadait sur DEUX niveaux :
        // supprimer un dossier effaçait les remboursements ET chaque tentative
        // adressée au prestataire de paiement. Ce sont ces tentatives qui portent
        // la référence PSP — le seul point de rapprochement possible avec le relevé
        // de l'opérateur. Sans elles, un remboursement parti et non abouti devient
        // invérifiable des deux côtés.
        builder.HasMany(r => r.Attempts).WithOne().HasForeignKey(a => a.RefundId).OnDelete(DeleteBehavior.Restrict);
    }

    private static void MapMoney(
        OwnedNavigationBuilder<Domain.ValueObjects.RefundBreakdown, Domain.ValueObjects.Money> builder,
        string prefix)
    {
        builder.Property(m => m.Amount).HasColumnName($"{prefix}_amount").HasPrecision(18, 2);
        builder.Property(m => m.Currency).HasColumnName($"{prefix}_currency").HasMaxLength(3);
    }
}

internal sealed class RefundAttemptConfiguration : IEntityTypeConfiguration<RefundAttempt>
{
    public void Configure(EntityTypeBuilder<RefundAttempt> builder)
    {
        builder.ToTable("refund_attempts");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Provider).HasMaxLength(64);
        builder.Property(a => a.Status).HasConversion<string>().HasMaxLength(64);
        builder.Property(a => a.ProviderReference).HasMaxLength(256);
    }
}

internal sealed class ReturnStatusHistoryConfiguration : IEntityTypeConfiguration<ReturnStatusHistory>
{
    public void Configure(EntityTypeBuilder<ReturnStatusHistory> builder)
    {
        builder.ToTable("return_status_history");
        builder.HasKey(h => h.Id);
        builder.Property(h => h.Status).HasConversion<string>().HasMaxLength(64);
        builder.Property(h => h.Reason).HasMaxLength(2000);
        builder.HasIndex(h => new { h.ReturnId, h.OccurredAtUtc });
    }
}

internal sealed class ReturnIdempotencyKeyConfiguration : IEntityTypeConfiguration<ReturnIdempotencyKey>
{
    public void Configure(EntityTypeBuilder<ReturnIdempotencyKey> builder)
    {
        builder.ToTable("idempotency_keys");
        builder.HasKey(k => k.Key);
        builder.Property(k => k.Key).HasMaxLength(180);
        builder.HasIndex(k => k.ReturnRequestId).IsUnique();
    }
}
