using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using HBA.Financial.Payments.Domain.Payments;
using HBA.Shared.Infrastructure.Persistence;

namespace HBA.Financial.Payments.Infrastructure.Persistence.Configurations;

internal sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {

        // ═════════════════════════════════════════════════════════════════════
        // VERROU OPTIMISTE — DOUBLE CAPTURE = DOUBLE CRÉDIT AU VENDEUR.
        //
        // `Capture()` vérifie que le statut est Pending/Authorized. Mais deux webhooks
        // FedaPay concurrents (le PSP RÉESSAIE en cas de timeout) lisent tous deux
        // « Pending », passent tous deux, et émettent DEUX FOIS PaymentCaptured — donc
        // deux crédits pour un seul paiement.
        //
        // (`UsePostgresRowVersion` — l'API Npgsql `UseXminAsConcurrencyToken` est dépréciée
        //  et casse la build en « warnings = errors » ; notre extension fait exactement
        //  ce qu'elle faisait. Voir ConcurrencyTokenExtensions.)
        //
        // `xmin` est une colonne SYSTÈME de PostgreSQL : elle existe déjà sur chaque
        // ligne et porte le numéro de la transaction qui l'a écrite en dernier. On ne
        // l'ajoute pas, on la LIT. Rien à changer dans le modèle de domaine.
        //
        // EF l'inclut désormais dans la clause WHERE de chaque UPDATE. Si une autre
        // transaction a modifié la ligne entre-temps, l'UPDATE touche 0 ligne et EF
        // lève `DbUpdateConcurrencyException` — traduite en 409 (voir
        // ServiceExceptionMiddleware).
        //
        // AUCUN RETRY AUTOMATIQUE, ET C'EST DÉLIBÉRÉ.
        //
        // ModuleDbContext dispatche les événements de domaine AVANT
        // base.SaveChangesAsync, et draine les événements d'intégration vers l'outbox.
        // Rejouer la commande dans le MÊME scope re-dispatcherait ces événements et
        // dupliquerait les messages d'outbox. On échoue donc franchement en 409 ; le
        // client rejoue avec une requête neuve (les PSP le font d'eux-mêmes sur leurs
        // webhooks).
        // ═════════════════════════════════════════════════════════════════════
        builder.UsePostgresRowVersion();
        builder.ToTable("payments");
        builder.HorodateLesModifications();

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Id)
            .HasConversion(id => id.Value, value => new PaymentId(value))
            .ValueGeneratedNever();

        builder.Property(p => p.OrderId).IsRequired();

        // VALEUR PAR DÉFAUT « Marketplace » POUR LES LIGNES DÉJÀ EN BASE.
        //
        // Stockée en chaîne comme les autres énumérations du fichier — un entier
        // rendrait la table illisible et ferait dépendre les données de l'ordre de
        // déclaration en C#. Le défaut n'est pas une commodité de migration : tous
        // les paiements existants SONT des commandes Marketplace, le Food n'ayant
        // pas encore de chemin de paiement.
        builder.Property(p => p.OrderType)
            .HasConversion<string>()
            .HasMaxLength(20)
            .HasDefaultValue(PaymentOrderType.Marketplace)
            .IsRequired();

        // Un paiement se retrouve par sa commande, et l'univers fait partie de la
        // clé de recherche : deux commandes d'univers différents peuvent porter le
        // même identifiant sans que ce soit une anomalie.
        builder.HasIndex(p => new { p.OrderType, p.OrderId })
            .HasDatabaseName("ix_payments_order");
        builder.Property(p => p.BuyerId).IsRequired();
        builder.Property(p => p.Method).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(p => p.Provider).HasMaxLength(100).IsRequired();
        builder.Property(p => p.Flow).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(p => p.ProviderReference).HasMaxLength(200);
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(p => p.FailureReason).HasMaxLength(500);
        builder.Property(p => p.CreatedAtUtc).IsRequired();
        builder.Property(p => p.CapturedAtUtc);
        builder.Property(p => p.EscrowReleasedAt);
        builder.Ignore(p => p.IsEscrowHeld);

        builder.OwnsOne(p => p.Amount, money =>
        {
            money.Property(m => m.Amount).HasColumnName("amount").HasColumnType("numeric(18,2)").IsRequired();
            money.Property(m => m.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        });
        builder.Navigation(p => p.Amount).IsRequired();

        // ═════════════════════════════════════════════════════════════════════
        // `Restrict`, ET NON `Cascade` — UN PAIEMENT SUPPRIMÉ EMPORTAIT LA
        //     PREUVE QUE LE CLIENT AVAIT ÉTÉ REMBOURSÉ.
        //
        // `ON DELETE CASCADE` ne se voit pas dans le code : il vit dans la base.
        // Un `DELETE FROM payments.payments WHERE …` mal ciblé — un nettoyage de
        // données de test, une reprise, une main qui glisse en psql — effaçait
        // silencieusement les `payment_refunds` correspondants. Le client garde
        // son relevé bancaire ; la plateforme, elle, n'a plus rien à opposer.
        //
        // CE QUE CE CHANGEMENT COÛTE, ET C'EST ASSUMÉ : un paiement portant
        // des remboursements ne peut PLUS être supprimé du tout. La base refuse,
        // bruyamment. C'est exactement l'effet recherché sur une donnée
        // comptable — et si une purge légitime devient un jour nécessaire
        // (rétention, RGPD), elle demandera une procédure délibérée, écrite, qui
        // dira ce qu'elle efface. C'est le contraire d'un effacement par effet de
        // bord.
        //
        // AUCUNE SUPPRESSION LOGIQUE N'EST AJOUTÉE, contrairement à ce que
        // suggérait l'audit. Rien dans le dépôt ne supprime un `Payment` : ni
        // `Remove`, ni `RemoveRange`, ni `ExecuteDeleteAsync`. Des colonnes
        // `IsDeleted`/`DeletedAtUtc` sans un seul appelant seraient du code mort
        // à maintenir, et une colonne de plus à oublier dans chaque requête.
        // ═════════════════════════════════════════════════════════════════════
        builder.HasMany(p => p.Refunds)
            .WithOne()
            .HasForeignKey(r => r.PaymentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(p => p.OrderId);
        builder.HasIndex(p => p.Status);

        // ═════════════════════════════════════════════════════════════════════
        // UNE RÉFÉRENCE PSP NE DÉSIGNE QU'UN SEUL PAIEMENT.
        //
        // L'index existait, mais SANS `IsUnique` — deux paiements pouvaient donc
        // porter la même référence. Le webhook du prestataire retrouve le paiement
        // PAR ELLE : il en encaisse un au hasard, et l'autre reste `Pending` POUR
        // TOUJOURS. Rien ne le signale — ni erreur, ni journal : du point de vue du
        // service, un paiement encore en attente est un cas parfaitement normal.
        //
        // Index PARTIEL parce que la colonne est nullable par construction : un
        // paiement n'a pas de référence tant que le PSP n'a pas répondu.
        //
        // Le filtre n'est PAS ce qui évite un conflit entre paiements en attente :
        // PostgreSQL tient déjà deux NULL pour distincts, un index unique nu les
        // aurait tous acceptés. Il sert à écrire l'intention en clair et à ne pas
        // indexer une file d'attente qui peut être longue. La garantie utile est
        // ailleurs : DÈS QU'une référence est écrite, elle est unique.
        // ═════════════════════════════════════════════════════════════════════
        builder.HasIndex(p => p.ProviderReference)
            .IsUnique()
            .HasFilter("\"ProviderReference\" IS NOT NULL");

        builder.Ignore(p => p.DomainEvents);
    }
}

internal sealed class PaymentRefundConfiguration : IEntityTypeConfiguration<PaymentRefund>
{
    public void Configure(EntityTypeBuilder<PaymentRefund> builder)
    {
        builder.ToTable("payment_refunds");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        builder.Property(r => r.PaymentId)
            .HasConversion(id => id.Value, value => new PaymentId(value))
            .IsRequired();

        builder.Property(r => r.ReturnId);
        builder.Property(r => r.ExternalRefundId);
        builder.Property(r => r.Reason).HasMaxLength(500).IsRequired();
        builder.Property(r => r.IdempotencyKey).HasMaxLength(180).IsRequired();
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(r => r.ProviderRefundId).HasMaxLength(200);
        builder.Property(r => r.FailureReason).HasMaxLength(500);
        builder.Property(r => r.RequestedAtUtc).IsRequired();
        builder.Property(r => r.CompletedAtUtc);
        builder.Property(r => r.LastAttemptAtUtc);
        builder.Property(r => r.AttemptCount).IsRequired();

        builder.OwnsOne(r => r.Amount, money =>
        {
            money.Property(m => m.Amount).HasColumnName("amount").HasColumnType("numeric(18,2)").IsRequired();
            money.Property(m => m.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        });
        builder.Navigation(r => r.Amount).IsRequired();

        builder.HasIndex(r => new { r.PaymentId, r.IdempotencyKey }).IsUnique();

        // Même panne que sur `payments.ProviderReference`, un cran plus bas : cet
        // identifiant est celui que le service de retours donne au remboursement
        // qu'il demande. Sans unicité, deux lignes peuvent le porter — l'issue du
        // PSP est alors reportée sur une seule, et le retour resté en suspens
        // n'est jamais soldé côté client.
        //
        // Nullable (un remboursement peut naître d'un webhook sans dossier de
        // retour en face), donc même index partiel qu'au-dessus.
        builder.HasIndex(r => r.ExternalRefundId)
            .IsUnique()
            .HasFilter("\"ExternalRefundId\" IS NOT NULL");
        builder.HasIndex(r => r.ReturnId);
        builder.HasIndex(r => r.Status);
    }
}
