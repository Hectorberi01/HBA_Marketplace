using HBA.Users.Domain.Addresses;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HBA.Users.Infrastructure.Persistence.Configurations;

internal sealed class AddressConfiguration : IEntityTypeConfiguration<Address>
{
    public void Configure(EntityTypeBuilder<Address> builder)
    {
        // Le schéma vient du DbContext (« users ») : ne PAS le nommer ici, sinon la
        // table échapperait à HasDefaultSchema et le module écrirait hors de chez lui.
        builder.ToTable("addresses");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Id)
            .HasConversion(id => id.Value, value => new AddressId(value))
            .ValueGeneratedNever();

        // ─────────────────────────────────────────────────────────────────────────────
        // UserId EST UNE RÉFÉRENCE, PAS UNE CLÉ ÉTRANGÈRE.
        //
        // Le compte vit dans le schéma « identity », l'adresse dans « users ». Aucune
        // contrainte ne relie les deux, et c'est la règle du monolithe modulaire : pas
        // de FK cross-schéma, sinon l'extraction d'un module devient impossible sans
        // migration de données.
        //
        // Conséquence assumée : la suppression d'un compte ne fait pas disparaître ses
        // adresses en cascade. C'est au module User de réagir à l'événement de
        // suppression — un travail à part, pas un effet de bord de la base.
        // ─────────────────────────────────────────────────────────────────────────────
        builder.Property(a => a.UserId).IsRequired();
        builder.Property(a => a.Label).HasMaxLength(Address.MaxLabel).IsRequired();
        builder.Property(a => a.Recipient).HasMaxLength(Address.MaxRecipient).IsRequired();
        builder.Property(a => a.Phone).HasMaxLength(Address.MaxPhone).IsRequired();

        // ─────────────────────────────────────────────────────────────────────────────
        // COMMUNE, QUARTIER, REPÈRE ET RUE SONT NULLABLES EN BASE, ET OBLIGATOIRES DANS
        // LE DOMAINE (sauf quartier et rue). Ce n'est pas une incohérence.
        //
        // Les adresses saisies avant la refonte n'ont ni commune normalisée ni repère.
        // Les déclarer NOT NULL exigerait d'inventer une valeur pour chacune — donc
        // d'envoyer des coursiers à des adresses fabriquées. On les laisse incomplètes,
        // `Address.IsComplete` les signale, et le checkout les refuse. Elles se réparent
        // à la première modification, qui applique les règles complètes.
        // ─────────────────────────────────────────────────────────────────────────────
        builder.Property(a => a.CommuneCode).HasMaxLength(Address.MaxCommuneCode);
        builder.Property(a => a.Quartier).HasMaxLength(Address.MaxQuartier);
        builder.Property(a => a.Landmark).HasMaxLength(Address.MaxLandmark);
        builder.Property(a => a.Line1).HasMaxLength(Address.MaxLine);

        builder.Property(a => a.CountryCode).HasMaxLength(2).IsRequired();

        // Position facultative : `double precision`, nullable. Aucun index — rien ne
        // requête par coordonnées aujourd'hui, et un index spatial supposerait PostGIS.
        builder.Property(a => a.Latitude);
        builder.Property(a => a.Longitude);
        builder.Property(a => a.IsDefault).IsRequired();
        builder.Property(a => a.CreatedOnUtc).IsRequired();

        // Propriétés calculées : elles se résolvent depuis BeninGeography, rien à stocker.
        builder.Ignore(a => a.CommuneName);
        builder.Ignore(a => a.IsComplete);
        builder.Ignore(a => a.HasCoordinates);

        builder.HasIndex(a => a.UserId);

        // Sert au futur calcul de frais par zone, et déjà aux statistiques : « combien de
        // livraisons hors du Grand Nokoué ? » ne doit pas balayer toute la table.
        builder.HasIndex(a => a.CommuneCode);

        builder.Ignore(a => a.DomainEvents);
    }
}
