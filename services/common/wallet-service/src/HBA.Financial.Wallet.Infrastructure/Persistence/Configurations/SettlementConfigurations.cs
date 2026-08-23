using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using HBA.Financial.Wallet.Domain.Batches;
using HBA.Financial.Wallet.Domain.Earnings;
using HBA.Shared.Infrastructure.Persistence;

namespace HBA.Financial.Wallet.Infrastructure.Persistence.Configurations;

internal sealed class SellerEarningConfiguration : IEntityTypeConfiguration<SellerEarning>
{
    public void Configure(EntityTypeBuilder<SellerEarning> builder)
    {
        builder.ToTable("seller_earnings");
        builder.HorodateLesModifications();

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id)
            .HasConversion(id => id.Value, value => new SellerEarningId(value))
            .ValueGeneratedNever();

        builder.Property(e => e.OrderId).IsRequired();
        builder.Property(e => e.OfferId).IsRequired();
        builder.Property(e => e.SellerId).IsRequired();
        builder.Property(e => e.ProductId).IsRequired();
        builder.Property(e => e.GrossAmount).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(e => e.CommissionAmount).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(e => e.ProviderFeeAmount).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(e => e.NetAmount).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(e => e.Currency).HasMaxLength(3).IsRequired();
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(e => e.CreatedAtUtc).IsRequired();
        builder.Property(e => e.ReleasedAtUtc);
        builder.Property(e => e.SettlementBatchId);
        builder.Property(e => e.SettledByWithdrawalId);

        // ═════════════════════════════════════════════════════════════════════
        // LES CUMULS DE REPRISE (voir `SellerEarning.ReversedGrossAmount`).
        //
        // VALEUR PAR DÉFAUT 0 CÔTÉ BASE, POSÉE PAR LA MIGRATION.
        //
        // Les lignes ANTÉRIEURES n'ont rien de repris, mais un `ADD COLUMN` sans
        // défaut les remplirait de NULL — et `decimal` non nullable refuserait de
        // les matérialiser au premier chargement. Le défaut vit dans
        // `20260830000100_RepriseDesGains`, pas ici : `HasDefaultValue` inscrirait
        // aussi le défaut dans le MODÈLE, et EF cesserait alors d'envoyer les zéros
        // explicites qu'un gain neuf doit écrire.
        // ═════════════════════════════════════════════════════════════════════
        builder.Property(e => e.ReversedGrossAmount).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(e => e.ReversedCommissionAmount).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(e => e.ReversedProviderFeeAmount).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(e => e.ReversedNetAmount).HasColumnType("numeric(18,2)").IsRequired();

        // LES QUATRE « RESTANT » SONT CALCULÉS — IGNORER EST OBLIGATOIRE.
        //
        // Ce sont des propriétés en lecture seule sans champ de stockage. Sans cet
        // `Ignore`, EF les prend pour des colonnes, ne trouve ni setter ni backing
        // field, et le MODÈLE ÉCHOUE À SE CONSTRUIRE — c'est-à-dire au démarrage du
        // service, pas à la première requête.
        builder.Ignore(e => e.RemainingGrossAmount);
        builder.Ignore(e => e.RemainingCommissionAmount);
        builder.Ignore(e => e.RemainingProviderFeeAmount);
        builder.Ignore(e => e.RemainingNetAmount);

        builder.HasIndex(e => e.OrderId);
        builder.HasIndex(e => e.SellerId);
        builder.HasIndex(e => new { e.Status, e.CreatedAtUtc });

        // La règle d'imputation lit les gains payables d'UN vendeur, du plus ancien
        // au plus récent. Sans cet index, chaque demande de retrait déclenche un tri
        // sur toute la table — et le tri est ici une règle métier, pas un confort :
        // il décide quels gains le retrait consomme.
        builder.HasIndex(e => new { e.SellerId, e.Status, e.ReleasedAtUtc });

        // Remonter les gains d'un retrait à rembourser (refus, échec PSP). Filtré :
        // l'écrasante majorité des gains n'a jamais été imputée à un retrait.
        builder.HasIndex(e => e.SettledByWithdrawalId)
            .HasFilter("\"SettledByWithdrawalId\" IS NOT NULL")
            .HasDatabaseName("ix_seller_earnings_withdrawal");

        builder.Ignore(e => e.DomainEvents);
    }
}

internal sealed class SettlementBatchConfiguration : IEntityTypeConfiguration<SettlementBatch>
{
    public void Configure(EntityTypeBuilder<SettlementBatch> builder)
    {
        builder.ToTable("settlement_batches");

        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id)
            .HasConversion(id => id.Value, value => new SettlementBatchId(value))
            .ValueGeneratedNever();

        builder.Property(b => b.PeriodStartUtc).IsRequired();
        builder.Property(b => b.PeriodEndUtc).IsRequired();
        builder.Property(b => b.Currency).HasMaxLength(3).IsRequired();
        builder.Property(b => b.TotalNet).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(b => b.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(b => b.CreatedAtUtc).IsRequired();

        // IsRequired() — LA CONTRAINTE DOIT VIVRE DANS LA BASE, PAS DANS UN RÉGLAGE EF.
        //
        // Correction d'une affirmation antérieure (voir doc 22) : SANS IsRequired(), EF
        // ne sévrait PAS pour autant. Le `OnDelete(DeleteBehavior.Cascade)` ci-dessous
        // gouverne aussi le sort des ORPHELINS — avec Cascade, un enfant retiré de la
        // collection est SUPPRIMÉ, pas mis à NULL. Les données de production l'ont confirmé.
        //
        // Alors pourquoi IsRequired() ? Pour trois raisons plus modestes et plus sûres :
        //
        //   1. Cette clé étrangère est RÉELLEMENT obligatoire — un enfant sans parent n'a
        //      aucun sens métier. Le modèle le déclarait facultatif. Un modèle qui ment
        //      finit toujours par produire du code qui se trompe.
        //
        //   2. La colonne était NULL-able en base, donc RIEN ne l'interdisait. Une ligne
        //      orpheline a d'ailleurs été trouvée en production (message_reactions) : on
        //      ignore ce qui l'a créée, et c'est précisément le problème. NOT NULL l'aurait
        //      refusée, quelle que soit sa provenance.
        //
        //   3. Sans ça, le comportement dépend d'un réglage FRAGILE : retirer le
        //      `OnDelete(Cascade)` — geste anodin en apparence — ferait réellement basculer
        //      cette relation en sévérance. Avec IsRequired() ET NOT NULL, c'est impossible.
        //
        // ET C'EST DÉSORMAIS `Restrict`, PAS `Cascade` (§8).
        //
        // Supprimer un lot de reversement effaçait le DÉTAIL de ce qui a été versé
        // à chaque vendeur. Le lot porte le total ; les `payouts` portent qui a reçu
        // combien. Sans eux, un vendeur qui conteste son versement n'a plus rien en
        // face de son relevé.
        //
        // LE POINT 3 CI-DESSUS EST DEVENU LE POINT PRINCIPAL. Il annonçait que
        // retirer `Cascade` ferait basculer la relation en SÉVÉRANCE — c'est-à-dire
        // qu'un `payout` retiré de la collection se verrait mis à `NULL` au lieu
        // d'être supprimé. C'est bien ce qui arriverait SANS `IsRequired()` ; avec
        // lui, EF lève au lieu de sévrer, et la base refuserait de toute façon le
        // `NULL`. Vérifié avant de toucher : `_payouts` n'est jamais muté par
        // retrait — seulement lu et alimenté. Le basculement est donc sans effet
        // sur le code existant.
        builder.HasMany(b => b.Payouts)
            .WithOne()
            .HasForeignKey("SettlementBatchId")
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);

        // LES DEUX SEULES LECTURES DE LISTE TRIENT SUR `CreatedAtUtc`, SUR TOUTE
        // LA TABLE ET SANS BORNE (`SettlementRepositories.cs:135` et `:143`).
        //
        // Sans index, chaque affichage de l'historique des lots est un tri complet.
        // La table ne décroît jamais — un lot par période, indéfiniment. L'index ne
        // borne pas la requête (c'est le lot 8.4), il rend le tri gratuit.
        builder.HasIndex(b => b.CreatedAtUtc);

        builder.Navigation(b => b.Payouts).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(b => b.DomainEvents);
    }
}

internal sealed class PayoutConfiguration : IEntityTypeConfiguration<Payout>
{
    public void Configure(EntityTypeBuilder<Payout> builder)
    {
        builder.ToTable("payouts");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();

        builder.Property(p => p.SellerId).IsRequired();
        builder.Property(p => p.GrossAmount).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(p => p.CommissionAmount).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(p => p.NetAmount).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(p => p.Currency).HasMaxLength(3).IsRequired();
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(p => p.ProviderRef).HasMaxLength(200);
        builder.Property(p => p.PaidAtUtc);

        // L'index sur la FK « SettlementBatchId » est créé automatiquement par la
        // relation (HasMany dans SettlementBatchConfiguration) ; on ne le déclare
        // pas ici pour ne pas dépendre de l'ordre d'application des configurations.
        builder.HasIndex(p => p.SellerId);
    }
}
