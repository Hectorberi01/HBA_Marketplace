using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using HBA.Communication.Domain.Conversations;

namespace HBA.Communication.Infrastructure.Persistence.Configurations;

internal sealed class ConversationConfiguration : IEntityTypeConfiguration<Conversation>
{
    public void Configure(EntityTypeBuilder<Conversation> builder)
    {
        builder.ToTable("conversations");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id)
            .HasConversion(id => id.Value, value => new ConversationId(value))
            .ValueGeneratedNever();

        builder.Property(c => c.ContextType).HasMaxLength(50);
        builder.Property(c => c.ContextId);
        builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(c => c.LastMessageAtUtc).IsRequired();

        // IsRequired() — LA CONTRAINTE DOIT VIVRE DANS LA BASE, PAS DANS UN RÉGLAGE
        // EF.
        builder.HasMany(c => c.Participants)
            .WithOne()
            .HasForeignKey("ConversationId")
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(c => c.Participants).UsePropertyAccessMode(PropertyAccessMode.Field);

        // IsRequired() — LA CONTRAINTE DOIT VIVRE DANS LA BASE, PAS DANS UN RÉGLAGE
        // EF.
        builder.HasMany(c => c.Messages)
            .WithOne()
            .HasForeignKey("ConversationId")
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(c => c.Messages).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(c => c.ParticipantIds);
        builder.Ignore(c => c.DomainEvents);
    }
}

internal sealed class ConversationParticipantConfiguration : IEntityTypeConfiguration<ConversationParticipant>
{
    public void Configure(EntityTypeBuilder<ConversationParticipant> builder)
    {
        builder.ToTable("conversation_participants");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property(p => p.UserId).IsRequired();

        // Index sur le participant pour la requête « mes conversations ».
        builder.HasIndex(p => p.UserId);
    }
}

internal sealed class MessageConfiguration : IEntityTypeConfiguration<Message>
{
    public void Configure(EntityTypeBuilder<Message> builder)
    {
        builder.ToTable("conversation_messages");

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();

        builder.Property(m => m.SenderId).IsRequired();
        builder.Property(m => m.Body).HasMaxLength(4000).IsRequired();
        builder.Property(m => m.ReadAtUtc);
        builder.Property(m => m.CreatedAtUtc).IsRequired();

        // Suppression « pour tout le monde » : le corps N'EST PAS effacé
        // (preuve/support), seule cette date est posée.
        builder.Property(m => m.DeletedAtUtc);

        // Pièces jointes : collection ENFANT (table `message_attachments`),
        // exactement le même pattern que les réactions ci-dessous.
        builder.HasMany(m => m.Attachments)
            .WithOne()
            .HasForeignKey("MessageId")
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(m => m.Attachments).UsePropertyAccessMode(PropertyAccessMode.Field);

        // IsRequired() — LA CONTRAINTE DOIT VIVRE DANS LA BASE, PAS DANS UN RÉGLAGE
        // EF.
        builder.HasMany(m => m.Reactions)
            .WithOne()
            .HasForeignKey("MessageId")
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(m => m.Reactions).UsePropertyAccessMode(PropertyAccessMode.Field);

        // IsRequired() — LA CONTRAINTE DOIT VIVRE DANS LA BASE, PAS DANS UN RÉGLAGE
        // EF.
        builder.HasMany(m => m.HiddenFor)
            .WithOne()
            .HasForeignKey("MessageId")
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(m => m.HiddenFor).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(m => m.IsDeleted);
    }
}

internal sealed class MessageReactionConfiguration : IEntityTypeConfiguration<MessageReaction>
{
    public void Configure(EntityTypeBuilder<MessageReaction> builder)
    {
        builder.ToTable("message_reactions");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        builder.Property(r => r.UserId).IsRequired();
        // 16 caractères : un emoji peut être composé (séquences ZWJ, sélecteurs de
        // variante).
        builder.Property(r => r.Emoji).HasMaxLength(16).IsRequired();
        builder.Property(r => r.CreatedAtUtc).IsRequired();

        // Invariant DB : une seule réaction par personne et par message.
        builder.HasIndex("MessageId", "UserId").IsUnique();
    }
}

internal sealed class MessageAttachmentConfiguration : IEntityTypeConfiguration<MessageAttachment>
{
    public void Configure(EntityTypeBuilder<MessageAttachment> builder)
    {
        builder.ToTable("message_attachments");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();

        // LA VÉRITÉ. Zéro pour une pièce d'avant la bascule.
        builder.Property(a => a.MediaId).IsRequired();

        // TRANSITOIRE : l'URL publique d'avant la bascule, NULLE désormais.
        builder.Property(a => a.LegacyUrl).HasMaxLength(1000);

        // Enum stocké en int (valeurs figées : Image=1 … Other=6).
        builder.Property(a => a.Type).HasConversion<int>().IsRequired();

        // Propriété CALCULÉE : sans cet Ignore, EF réclamerait une colonne.
        builder.Ignore(a => a.IsLegacy);

        builder.HasIndex("MessageId");

        // INDEX SUR LE MÉDIA, ET IL SERT À UN CONTRÔLE DE SÉCURITÉ.
        builder.HasIndex(a => a.MediaId);
    }
}

internal sealed class MessageHiddenForConfiguration : IEntityTypeConfiguration<MessageHiddenFor>
{
    public void Configure(EntityTypeBuilder<MessageHiddenFor> builder)
    {
        builder.ToTable("message_hidden_for");

        builder.HasKey(h => h.Id);
        builder.Property(h => h.Id).ValueGeneratedNever();

        builder.Property(h => h.UserId).IsRequired();
        builder.Property(h => h.HiddenAtUtc).IsRequired();

        // Un message ne peut être masqué qu'une fois par utilisateur.
        builder.HasIndex("MessageId", "UserId").IsUnique();
    }
}
