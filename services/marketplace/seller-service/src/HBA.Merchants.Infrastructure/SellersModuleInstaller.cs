using System.Reflection;
using HBA.Merchants.Infrastructure.Messaging.Kafka.Configuration;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using HBA.Shared.Infrastructure.Configuration;
using HBA.Shared.Application.Abstractions;
using HBA.Shared.Domain.Events;
using HBA.Shared.Infrastructure.Idempotency;
using HBA.Shared.Infrastructure.Modularity;
using HBA.Shared.IntegrationEvents;
using HBA.Identity.Contracts.IntegrationEvents;
using HBA.Engagement.Reviews.Contracts.IntegrationEvents;
using HBA.Orders.Contracts.IntegrationEvents;
using HBA.Merchants.Application.Abstractions;
using HBA.Merchants.Application.Sellers.Commands.RegisterSeller;
using HBA.Merchants.Application.Sellers.EventHandlers;
using HBA.Merchants.Application.Members;
using HBA.Merchants.Contracts;
using HBA.Merchants.Domain.Members;
using HBA.Merchants.Domain.Members.Events;
using HBA.Merchants.Domain.Sellers;
using HBA.Merchants.Infrastructure.Security;
using HBA.Merchants.Application.Stores;
using HBA.Merchants.Domain.Stores;
using HBA.Merchants.Domain.Stores.Events;
using HBA.Merchants.Domain.Sellers.Events;
using HBA.Merchants.Infrastructure.Persistence;
using HBA.Merchants.Infrastructure.Public;

using HBA.Merchants.Infrastructure.Caching.Redis;
using HBA.Merchants.Infrastructure.Observability;
using HBA.Merchants.Infrastructure.Persistence.Outbox;
using HBA.Merchants.Infrastructure.Persistence.Inbox;
using HBA.Merchants.Infrastructure.Idempotency;
namespace HBA.Merchants.Infrastructure;

/// <summary>
/// Enregistre tout le module Sellers : DbContext (schéma propre), repository, API
/// publique, handlers d'events, validators, processeur d'outbox.
/// </summary>
public sealed class SellersModuleInstaller : IModuleInstaller
{
    public string ModuleName => "Sellers";

    public Assembly ApplicationAssembly => typeof(RegisterSellerCommand).Assembly;

    public void Install(IServiceCollection services, IConfiguration configuration)
    {
        // LE CACHE DE CE SERVICE (Caching/Redis/).
        services.AjouterCacheMerchants(configuration);

        // LES SONDES DE CE SERVICE (Observability/).
        services.AjouterObservabiliteMerchants(configuration);

        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Chaîne de connexion « Default » absente.");

        // L'outbox et l'inbox sont descendues dans `Messaging/Kafka/`, donc hors de
        // cet installeur : elles sont desormais enregistrees par
        // `AjouterMessagerieMerchants()`, que le composition root peut oublier.
        services.AddHostedService<GardeDeCablage>();

        services.AddDbContext<SellersDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", SellersDbContext.SchemaName)));

        services.AddScoped<ISellerUnitOfWork>(sp => sp.GetRequiredService<SellersDbContext>());

        // CONSTRUIT ICI, PAS RÉSOLU PLUS TARD : la validation du barème vit dans le
        // constructeur, et doit faire échouer le DÉMARRAGE.
        services.AddSingleton<IPlatformPricing>(new PlatformPricing(configuration));

        services.AddScoped<ISellerRepository, SellerRepository>();
        services.AddScoped<IStoreRepository, StoreRepository>();
        services.AddScoped<ISellerModuleApi, SellerModuleApi>();

        // L'IMPLÉMENTATION LOCALE, POUR LE SERVICE QUI LA SERT.
        services.AddScoped<IMerchantAccessApi, MerchantAccessApi>();

        // L'ÉQUIPE D'UN VENDEUR.
        //
        // `MemberAccessResolver` EST LA GARDE DE TOUTES CES ROUTES.
        //
        // Il n'y a pas de `DenyUnlessOwnSellerAsync` en amont : la résolution de
        // l'appartenance EST le contrôle. L'enregistrer comme un service ordinaire
        // plutôt que le recopier dans chaque handler est ce qui garantit qu'il n'y
        // en a qu'un — et donc qu'aucune route ne peut en avoir une variante plus
        // permissive.
        services.AddScoped<ISellerMemberRepository, SellerMemberRepository>();
        services.AddScoped<ISellerRoleRepository, SellerRoleRepository>();
        services.AddScoped<ISellerInvitationRepository, SellerInvitationRepository>();
        services.AddScoped<MemberAccessResolver>();

        // LECTURE SEULE, ET AUCUN DÉPÔT EN FACE.
        services.AddScoped<IAuditTrailReader, AuditTrailReader>();

        // Aléa et SHA-256 : sans état, donc singleton.
        services.AddSingleton<IInvitationTokens, InvitationTokens>();

        // LES SEPT TRADUCTEURS « ÉVÉNEMENT DE DOMAINE → ÉVÉNEMENT D'INTÉGRATION ».
        services.AddScoped<IDomainEventHandler<SellerMemberJoinedDomainEvent>,
            SellerMemberJoinedDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<SellerMemberRolesChangedDomainEvent>,
            SellerMemberRolesChangedDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<SellerMemberStoreAssignedDomainEvent>,
            SellerMemberStoreAssignedDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<SellerMemberStoreUnassignedDomainEvent>,
            SellerMemberStoreUnassignedDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<SellerMemberSuspendedDomainEvent>,
            SellerMemberSuspendedDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<SellerMemberActivatedDomainEvent>,
            SellerMemberActivatedDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<SellerMemberRevokedDomainEvent>,
            SellerMemberRevokedDomainEventHandler>();

        // LE TRANSFERT DE PROPRIÉTÉ (lot 7.2, ISSUE-040).
        services.AddScoped<IDomainEventHandler<SellerOwnershipTransferredDomainEvent>,
            SellerOwnershipTransferredDomainEventHandler>();

        // PLUS AUCUN STOCKAGE ICI. LES PIÈCES KYB VIVENT DANS HBA MEDIA.

        services.AddScoped<IDomainEventHandler<SellerRegisteredDomainEvent>, SellerRegisteredDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<SellerActivatedDomainEvent>, SellerActivatedDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<SellerClosedDomainEvent>, SellerClosedDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<SellerReactivatedDomainEvent>, SellerReactivatedDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<SellerDeletedDomainEvent>, SellerDeletedDomainEventHandler>();

        // SANS CET ENREGISTREMENT, LE FICHIER D'UNE PIÈCE KYB RETIRÉE RESTE DANS LE
        // BUCKET PRIVÉ.
        services.AddScoped<IDomainEventHandler<KybDocumentRemovedDomainEvent>, KybDocumentRemovedDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<SellerKybRejectedDomainEvent>, SellerKybRejectedDomainEventHandler>();

        // LES DEUX MOITIÉS MANQUANTES DU PARCOURS KYB (§10.3).
        services.AddScoped<IDomainEventHandler<SellerKybSubmittedDomainEvent>, SellerKybSubmittedDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<SellerKybVerifiedDomainEvent>, SellerKybVerifiedDomainEventHandler>();

        // Le cycle de vie d'une BOUTIQUE, distinct de celui du vendeur.
        services.AddScoped<IDomainEventHandler<StoreClosedDomainEvent>, StoreClosedDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<StoreOpenedDomainEvent>, StoreOpenedDomainEventHandler>();

        // UNE SANCTION N'EST PAS DES CONGÉS. `Store.Suspend` émettait pourtant
        // `StoreClosedDomainEvent`, exactement comme une fermeture volontaire.
        services.AddScoped<IDomainEventHandler<StoreSuspendedDomainEvent>, StoreSuspendedDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<StoreSuspensionLiftedDomainEvent>, StoreSuspensionLiftedDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<SellerSuspendedDomainEvent>, SellerSuspendedDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<SellerSuspensionLiftedDomainEvent>, SellerSuspensionLiftedDomainEventHandler>();

        // INBOX DE CONSOMMATION (§19.5) ET IDEMPOTENCE DES ÉCRITURES (§25).
        services.AjouterIdempotenceMerchants();

        // Les gestionnaires d'evenements sont enregistres par le module de
        // messagerie du service : `Messaging/Kafka/DependencyInjection.cs`.

        services.AddValidatorsFromAssembly(ApplicationAssembly, includeInternalTypes: true);

    }

}
