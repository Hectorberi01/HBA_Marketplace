using HBA.Merchants.Domain.Members;
using HBA.Shared.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HBA.Merchants.Infrastructure.Persistence.Configurations;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LES RÔLES — SYSTÈME ET PERSONNALISÉS DANS LA MÊME TABLE.
///
/// `SellerId` EST NULLABLE, ET L'INDEX D'UNICITÉ EST FILTRÉ EN CONSÉQUENCE.
///
/// Un rôle système a `SellerId` nul et un nom unique globalement ; un rôle
/// personnalisé a un nom unique DANS SON VENDEUR. Deux vendeurs peuvent donc
/// nommer tous les deux un rôle « Préparateur ». Un index unique nu sur
/// (SellerId, Name) laisserait passer plusieurs rôles système homonymes, parce
/// que PostgreSQL considère deux NULL comme distincts — d'où les deux index.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
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

        // EN ENTIER, PAS EN CHAÎNE — même raison que `StaffRole` côté food :
        // une comparaison ou un filtre sur la portée doit tenir en SQL.
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

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LES MEMBRES, LEURS RÔLES, ET LEURS AFFECTATIONS — TROIS TABLES IMBRIQUÉES.
///
/// `UX_seller_members_SellerId_UserId` : UN COMPTE NE FIGURE QU'UNE FOIS.
///
/// Sans lui, une invitation acceptée deux fois créerait deux appartenances au même
/// vendeur, avec des rôles différents — et la résolution d'autorisation en
/// choisirait une au hasard, selon l'ordre de la table.
///
/// `IX_seller_members_UserId` : C'EST L'INDEX DU CHEMIN CHAUD.
///
/// Chaque requête vendeur, sur cinq services, part d'un identifiant
/// d'UTILISATEUR pour trouver son appartenance. Sans cet index, c'est un balayage
/// complet à chaque appel autorisé de la plateforme.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
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

        // ═════════════════════════════════════════════════════════════════════
        // VERROU OPTIMISTE — CONTRE L'ÉCRITURE CONCURRENTE SUR LA MÊME LIGNE.
        //
        // Deux administrateurs qui modifient les rôles du MÊME membre en même temps
        // : le second échoue et rejoue. C'est ce que `xmin` sait faire, et c'est
        // utile.
        //
        // CE COMMENTAIRE AFFIRMAIT QU'IL GARDAIT LE DERNIER PROPRIÉTAIRE. IL NE
        // LE PEUT PAS.
        //
        // Le texte disait : « deux propriétaires qui se retirent simultanément
        // liraient chacun "il en reste deux" ; `xmin` fait échouer la seconde
        // écriture ». C'est faux. `xmin` est un jeton PAR LIGNE, et révoquer O1 puis
        // O2 écrit DEUX LIGNES DIFFÉRENTES : il n'y a aucun conflit à détecter, les
        // deux écritures réussissent, et le vendeur tombe à zéro propriétaire.
        //
        // La forme du défaut est « lire puis décider sur une autre ligne » —
        // qu'aucun verrou optimiste n'attrape, faute de quoi que ce soit à comparer.
        // La garde réelle est le verrou consultatif tenu par
        // `ISellerUnitOfWork.ExecuteUnderSellerLockAsync` autour du décompte ET de
        // la décision.
        //
        // ET CETTE PHRASE-LÀ A ÉTÉ FAUSSE PENDANT UN TEMPS, ELLE AUSSI. Elle
        // nommait `LockSellerAsync`, qui prenait le verrou au fil d'un handler,
        // hors de toute transaction — donc le relâchait aussitôt. Le texte décrivait
        // une garde exacte dans son principe, portée par un appel qui ne la tenait
        // pas. C'est le même défaut que celui qu'il corrigeait, d'un cran plus loin.
        //
        // Le commentaire est corrigé plutôt que supprimé : c'est lui qui avait fait
        // passer la relecture.
        // ═════════════════════════════════════════════════════════════════════
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
        // propre identifiant et possède elle-même ses rôles. Deux niveaux de
        // possession imbriqués obligeraient à nommer à la main des clés étrangères
        // composées, ce que rien d'autre dans ce dépôt ne fait.
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

/// <summary>
/// L'affectation d'un membre à une boutique.
/// </summary>
/// <remarks>
/// LA COLONNE `Enforcement` DIT LA VÉRITÉ SUR LA PHASE 1.
///
/// `Prepared` signifie « l'affectation est écrite, la règle ne s'applique pas
/// encore » : les permissions qu'elle porte valent aujourd'hui pour le VENDEUR
/// ENTIER, parce qu'aucune commande et aucun article de stock ne connaît la
/// boutique. Au lot G, elle passera à `Enforced` boutique par boutique — et ce qui
/// change se lira en base plutôt que dans un journal de déploiement.
/// </remarks>
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

        // UN MEMBRE N'EST AFFECTÉ QU'UNE FOIS À UNE MÊME BOUTIQUE.
        // Deux lignes donneraient deux jeux de rôles pour le même couple, et la
        // résolution en choisirait un selon l'ordre de la table.
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

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LES INVITATIONS.
///
/// L'INDEX UNIQUE SUR `TokenHash` FAIT DEUX CHOSES, PAS UNE.
///
/// Il rend la recherche à l'acceptation immédiate — c'est le seul chemin de
/// lecture de cette table sur le parcours de l'invité — et il interdit que deux
/// invitations partagent une empreinte, ce qui rendrait l'une d'elles
/// inatteignable et l'autre ambiguë.
///
/// ET L'INDEX PARTIEL SUR (SellerId, Email) INTERDIT LES DOUBLONS EN ATTENTE.
///
/// Deux invitations vivantes pour la même personne, ce sont deux jetons valides et
/// deux jeux de rôles concurrents : celui qui gagne dépend du lien ouvert en
/// premier. Le contrôle existe aussi dans le handler ; le filtrer ici le rend vrai
/// même en cas de double soumission simultanée, que le handler ne verrait pas.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
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

            // Nul = rôle de niveau vendeur. Une colonne nullable plutôt qu'une
            // seconde table : c'est le seul champ qui distingue les deux cas.
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
