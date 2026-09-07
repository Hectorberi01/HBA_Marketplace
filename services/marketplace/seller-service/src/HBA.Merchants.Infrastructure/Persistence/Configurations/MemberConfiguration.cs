using HBA.Merchants.Domain.Members;
using HBA.Shared.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HBA.Merchants.Infrastructure.Persistence.Configurations;

/// <summary>LES RÔLES — SYSTÈME ET PERSONNALISÉS DANS LA MÊME TABLE.</summary>
internal sealed class SellerRoleConfiguration : IEntityTypeConfiguration<SellerRole>
{
    public void Configure(EntityTypeBuilder<SellerRole> builder)
    {
        builder.ToTable("seller_roles");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id)
            .HasConversion(id => id.Value, value => new SellerRoleId(value))
            .ValueGeneratedNever();

        builder.Property(r => r.SellerId);
        builder.Property(r => r.Name).HasMaxLength(100).IsRequired();
        builder.Property(r => r.Description).HasMaxLength(255);

        // EN ENTIER, PAS EN CHAÎNE — même raison que `StaffRole` côté food : une
        // comparaison ou un filtre sur la portée doit tenir en SQL.
        builder.Property(r => r.Scope).HasConversion<int>().IsRequired();

        builder.Property(r => r.IsSystemRole).IsRequired();
        builder.Property(r => r.CreatedOnUtc).IsRequired();
        builder.Property(r => r.UpdatedOnUtc);

        builder.HasIndex(r => r.SellerId).HasDatabaseName("IX_seller_roles_SellerId");

        // Un nom par vendeur, et un nom système unique globalement.
        builder.HasIndex(r => new { r.SellerId, r.Name })
            .IsUnique()
            .HasFilter("\"SellerId\" IS NOT NULL")
            .HasDatabaseName("UX_seller_roles_SellerId_Name");

        builder.HasIndex(r => r.Name)
            .IsUnique()
            .HasFilter("\"SellerId\" IS NULL")
            .HasDatabaseName("UX_seller_roles_SystemName");

        builder.UsePostgresRowVersion();

        builder.OwnsMany<SellerRolePermission>("_permissions", permissions =>
        {
            permissions.ToTable("role_permissions");
            permissions.WithOwner().HasForeignKey("SellerRoleId");

            permissions.Property(p => p.Permission).HasConversion<int>().IsRequired();

            // La clé est la paire : une permission ne figure qu'une fois par rôle.
            permissions.HasKey("SellerRoleId", nameof(SellerRolePermission.Permission));
        });

        builder.Ignore(r => r.DomainEvents);
        builder.Ignore(r => r.Permissions);
        builder.Ignore(r => r.IsOwnerRole);
    }
}

/// <summary>LES MEMBRES, LEURS RÔLES, ET LEURS AFFECTATIONS — TROIS TABLES IMBRIQUÉES.</summary>
internal sealed class SellerMemberConfiguration : IEntityTypeConfiguration<SellerMember>
{
    public void Configure(EntityTypeBuilder<SellerMember> builder)
    {
        builder.ToTable("seller_members");

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id)
            .HasConversion(id => id.Value, value => new SellerMemberId(value))
            .ValueGeneratedNever();

        builder.Property(m => m.SellerId).IsRequired();
        builder.Property(m => m.UserId).IsRequired();
        builder.Property(m => m.Status).HasConversion<int>().IsRequired();
        builder.Property(m => m.DisplayName).HasMaxLength(150);
        builder.Property(m => m.JobTitle).HasMaxLength(120);
        builder.Property(m => m.InvitedByUserId);
        builder.Property(m => m.JoinedOnUtc);
        builder.Property(m => m.CreatedOnUtc).IsRequired();
        builder.Property(m => m.UpdatedOnUtc);

        builder.HasIndex(m => new { m.SellerId, m.UserId })
            .IsUnique()
            .HasDatabaseName("UX_seller_members_SellerId_UserId");

        builder.HasIndex(m => m.UserId).HasDatabaseName("IX_seller_members_UserId");

        // VERROU OPTIMISTE — CONTRE L'ÉCRITURE CONCURRENTE SUR LA MÊME LIGNE.
        builder.UsePostgresRowVersion();

        builder.OwnsMany<SellerMemberRole>("_sellerRoles", roles =>
        {
            roles.ToTable("seller_member_roles");
            roles.WithOwner().HasForeignKey("SellerMemberId");

            roles.Property(r => r.RoleId)
                .HasConversion(id => id.Value, value => new SellerRoleId(value))
                .HasColumnName("SellerRoleId")
                .IsRequired();

            roles.HasKey("SellerMemberId", nameof(SellerMemberRole.RoleId));
        });

        // UNE RELATION, PAS UNE POSSESSION — parce que `StoreMembership` a son
        // propre identifiant et possède elle-même ses rôles.
        builder.HasMany<StoreMembership>("_storeMemberships")
            .WithOne()
            .HasForeignKey("SellerMemberId")
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation("_storeMemberships").UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(m => m.DomainEvents);
        builder.Ignore(m => m.SellerRoleIds);
        builder.Ignore(m => m.StoreMemberships);
        builder.Ignore(m => m.ReferencedRoleIds);
        builder.Ignore(m => m.CanAct);
        builder.Ignore(m => m.IsOwner);
    }
}

/// <summary>L'affectation d'un membre à une boutique.</summary>
internal sealed class StoreMembershipConfiguration : IEntityTypeConfiguration<StoreMembership>
{
    public void Configure(EntityTypeBuilder<StoreMembership> builder)
    {
        builder.ToTable("store_memberships");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();

        builder.Property(a => a.StoreId).IsRequired();
        builder.Property(a => a.Status).HasConversion<int>().IsRequired();
        builder.Property(a => a.Enforcement).HasConversion<int>().IsRequired();
        builder.Property(a => a.CreatedOnUtc).IsRequired();
        builder.Property(a => a.UpdatedOnUtc);

        // UN MEMBRE N'EST AFFECTÉ QU'UNE FOIS À UNE MÊME BOUTIQUE. Deux lignes
        // donneraient deux jeux de rôles pour le même couple, et la résolution en
        // choisirait un selon l'ordre de la table.
        builder.HasIndex("SellerMemberId", nameof(StoreMembership.StoreId))
            .IsUnique()
            .HasDatabaseName("UX_store_memberships_SellerMemberId_StoreId");

        builder.HasIndex(a => a.StoreId).HasDatabaseName("IX_store_memberships_StoreId");

        builder.OwnsMany<StoreMembershipRole>("_roles", roles =>
        {
            roles.ToTable("store_membership_roles");
            roles.WithOwner().HasForeignKey("StoreMembershipId");

            roles.Property(r => r.RoleId)
                .HasConversion(id => id.Value, value => new SellerRoleId(value))
                .HasColumnName("SellerRoleId")
                .IsRequired();

            roles.HasKey("StoreMembershipId", nameof(StoreMembershipRole.RoleId));
        });

        builder.Ignore(a => a.RoleIds);
    }
}

/// <summary>LES INVITATIONS.</summary>
internal sealed class SellerInvitationConfiguration : IEntityTypeConfiguration<SellerInvitation>
{
    public void Configure(EntityTypeBuilder<SellerInvitation> builder)
    {
        builder.ToTable("seller_invitations");

        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id)
            .HasConversion(id => id.Value, value => new SellerInvitationId(value))
            .ValueGeneratedNever();

        builder.Property(i => i.SellerId).IsRequired();
        builder.Property(i => i.Email).HasMaxLength(200).IsRequired();
        builder.Property(i => i.DisplayName).HasMaxLength(150);
        builder.Property(i => i.JobTitle).HasMaxLength(120);
        builder.Property(i => i.Status).HasConversion<int>().IsRequired();

        // 64 caractères hexadécimaux : un SHA-256, jamais le jeton.
        builder.Property(i => i.TokenHash).HasMaxLength(64).IsRequired();

        builder.Property(i => i.ExpiresOnUtc).IsRequired();
        builder.Property(i => i.InvitedByUserId).IsRequired();
        builder.Property(i => i.AcceptedByUserId);
        builder.Property(i => i.CreatedOnUtc).IsRequired();
        builder.Property(i => i.ResolvedOnUtc);

        builder.HasIndex(i => i.TokenHash)
            .IsUnique()
            .HasDatabaseName("UX_seller_invitations_TokenHash");

        builder.HasIndex(i => i.SellerId).HasDatabaseName("IX_seller_invitations_SellerId");

        // 0 = Pending. La valeur littérale est ici parce qu'un filtre d'index est
        // du SQL : il ne connaît pas l'énumération.
        builder.HasIndex(i => new { i.SellerId, i.Email })
            .IsUnique()
            .HasFilter("\"Status\" = 0")
            .HasDatabaseName("UX_seller_invitations_Pending");

        builder.UsePostgresRowVersion();

        builder.OwnsMany<InvitationAssignment>("_assignments", affectations =>
        {
            affectations.ToTable("seller_invitation_assignments");
            affectations.WithOwner().HasForeignKey("SellerInvitationId");

            affectations.HasKey(a => a.Id);
            affectations.Property(a => a.Id).ValueGeneratedOnAdd();

            // Nul = rôle de niveau vendeur.
            affectations.Property(a => a.StoreId);

            affectations.Property(a => a.RoleId)
                .HasConversion(id => id.Value, value => new SellerRoleId(value))
                .HasColumnName("SellerRoleId")
                .IsRequired();
        });

        builder.Ignore(i => i.DomainEvents);
        builder.Ignore(i => i.Assignments);
        builder.Ignore(i => i.SellerRoleIds);
        builder.Ignore(i => i.StoreAssignments);
        builder.Ignore(i => i.ReferencedRoleIds);
    }
}
