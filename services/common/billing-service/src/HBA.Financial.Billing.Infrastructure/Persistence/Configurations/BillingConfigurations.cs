using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using HBA.Financial.Billing.Domain.Commissions;
using HBA.Financial.Billing.Domain.Invoices;
using HBA.Shared.Infrastructure.Persistence;

namespace HBA.Financial.Billing.Infrastructure.Persistence.Configurations;

internal sealed class CommissionRuleConfiguration : IEntityTypeConfiguration<CommissionRule>
{
    public void Configure(EntityTypeBuilder<CommissionRule> builder)
    {
        builder.ToTable("commission_rules");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id)
            .HasConversion(id => id.Value, value => new CommissionRuleId(value))
            .ValueGeneratedNever();

        builder.Property(r => r.Scope).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(r => r.TargetId);
        builder.Property(r => r.Rate).HasColumnType("numeric(6,4)").IsRequired();
        builder.Property(r => r.FixedFee).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(r => r.Currency).HasMaxLength(3).IsRequired();
        builder.Property(r => r.MinFee).HasColumnType("numeric(18,2)");
        builder.Property(r => r.MaxFee).HasColumnType("numeric(18,2)");
        builder.Property(r => r.EffectiveFromUtc).IsRequired();
        builder.Property(r => r.IsActive).IsRequired();

        builder.HasIndex(r => new { r.Scope, r.TargetId });
        builder.HasIndex(r => r.IsActive);

        builder.Ignore(r => r.DomainEvents);
    }
}

internal sealed class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> builder)
    {
        builder.ToTable("invoices");
        builder.HorodateLesModifications();

        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id)
            .HasConversion(id => id.Value, value => new InvoiceId(value))
            .ValueGeneratedNever();

        builder.Property(i => i.SellerId).IsRequired();
        builder.Property(i => i.PeriodStartUtc).IsRequired();
        builder.Property(i => i.PeriodEndUtc).IsRequired();
        builder.Property(i => i.Currency).HasMaxLength(3).IsRequired();
        builder.Property(i => i.TotalAmount).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(i => i.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(i => i.CreatedAtUtc).IsRequired();
        builder.Property(i => i.IssuedAtUtc);

        // IsRequired() — LA CONTRAINTE DOIT VIVRE DANS LA BASE, PAS DANS UN RÉGLAGE EF.
        //
        // CORRECTION D'UNE AFFIRMATION ANTÉRIEURE (voir doc 22). Ce commentaire disait
        // que, sans IsRequired(), EF « sèvrerait » les enfants retirés d'une collection
        // (UPDATE enfant SET FK = NULL) et produirait des orphelins. C'est FAUX ici : le
        // `OnDelete(DeleteBehavior.Cascade)` ci-dessous gouverne aussi le sort des orphelins,
        // et avec Cascade, un enfant retiré est SUPPRIMÉ. Les données de production l'ont
        // confirmé (10 commandes confirmées, zéro réservation orpheline).
        //
        // Ce qui restait vrai, en revanche : EF s'apprêtait à rendre cette colonne NULLABLE
        // pour aligner la base sur un modèle qui la déclarait facultative — alors qu'elle ne
        // l'est pas. On aurait perdu la seule garantie qui ne dépende d'aucun réglage.
        //
        // IsRequired() sert donc à : (1) dire la vérité — un enfant sans parent n'a aucun
        // sens métier ; (2) maintenir le NOT NULL en base, qui refuse un orphelin quelle que
        // soit sa provenance (une ligne orpheline a été trouvée en production, et on ignore
        // ce qui l'a créée) ; (3) rendre le comportement indépendant du OnDelete — dont le
        // retrait, geste anodin en apparence, ferait RÉELLEMENT basculer en sévérance.
        //
        // ET C'EST DÉSORMAIS `Restrict`, PAS `Cascade` (§8).
        //
        // Supprimer une facture effaçait son DÉTAIL — les lignes qui disent ce qui
        // a été facturé et à quel titre. Le total survivrait dans l'en-tête ; ce qui
        // le compose, non. Une facture dont on ne peut plus expliquer le montant
        // n'est plus une facture.
        //
        // Le point (3) ci-dessus est devenu le point principal : il annonçait que
        // retirer `Cascade` ferait basculer la relation en SÉVÉRANCE. Avec
        // `IsRequired()` et le NOT NULL en base, EF lève au lieu de sévrer.
        // Vérifié avant de toucher : `_lines` n'est jamais muté par retrait.
        builder.HasMany(i => i.Lines)
            .WithOne()
            .HasForeignKey("InvoiceId")
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);

        builder.Navigation(i => i.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(i => i.SellerId);

        // ═════════════════════════════════════════════════════════════════════
        // JETON DE CONCURRENCE (§6).
        //
        // Le chemin exposé : deux `Issue()` ou deux `MarkPaid()` simultanés sur la
        // même facture — une reprise automatique qui croise une action humaine.
        // Sans jeton, le second écrase le premier et la facture change d'état deux
        // fois sans que rien ne le dise.
        //
        // Les deux transitions écrivent `Status` sur la ligne : un `UPDATE` est
        // bien émis, donc le jeton n'est pas inerte. C'est la vérification qu'exige
        // l'encadré d'`UsePostgresRowVersion` — un jeton posé sur un agrégat dont
        // le chemin n'écrit que des lignes ENFANTS ne protège rien.
        //
        // `AddLine` N'EST PAS COUVERT DE LA MÊME FAÇON. S'il n'écrit qu'une
        // ligne fille sans salir l'en-tête, deux ajouts concurrents ne se voient
        // pas. Ce n'est pas le risque que ce jeton adresse : une facture se
        // construit en brouillon, puis s'émet — et c'est l'émission qui est
        // sensible.
        //
        // AUCUNE COLONNE N'EST CRÉÉE : `xmin` est une colonne système.
        // ═════════════════════════════════════════════════════════════════════
        builder.UsePostgresRowVersion();

        builder.Ignore(i => i.DomainEvents);
    }
}

internal sealed class InvoiceLineConfiguration : IEntityTypeConfiguration<InvoiceLine>
{
    public void Configure(EntityTypeBuilder<InvoiceLine> builder)
    {
        builder.ToTable("invoice_lines");

        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).ValueGeneratedNever();

        builder.Property(l => l.Description).HasMaxLength(300).IsRequired();
        builder.Property(l => l.Amount).HasColumnType("numeric(18,2)").IsRequired();

        builder.HasIndex("InvoiceId");
    }
}
