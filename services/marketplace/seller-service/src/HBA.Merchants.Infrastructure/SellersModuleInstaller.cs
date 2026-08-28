using System.Reflection;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using HBA.Shared.Infrastructure.Configuration;
using HBA.Shared.Application.Abstractions;
using HBA.Shared.Domain.Events;
using HBA.Shared.Infrastructure.Idempotency;
using HBA.Shared.Infrastructure.Inbox;
using HBA.Shared.Infrastructure.Modularity;
using HBA.Shared.Infrastructure.Outbox;
using HBA.Shared.IntegrationEvents;
using HBA.Identity.Contracts.IntegrationEvents;
using HBA.Engagement.Reviews.Contracts.IntegrationEvents;
using HBA.Orders.Contracts.IntegrationEvents;
using HBA.Merchants.Infrastructure.Integration;
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
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Chaîne de connexion « Default » absente.");

        services.AddDbContext<SellersDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", SellersDbContext.SchemaName)));

        services.AddScoped<ISellerUnitOfWork>(sp => sp.GetRequiredService<SellersDbContext>());

        // CONSTRUIT ICI, PAS RÉSOLU PLUS TARD : la validation du barème vit
        // dans le constructeur, et doit faire échouer le DÉMARRAGE.
        //
        // Sellers n'applique aucune commission — il l'AFFICHE. C'est justement
        // pourquoi il doit lire la même source que ceux qui l'appliquent : servir
        // au vendeur un taux différent de celui qu'on lui prélève est le défaut
        // qu'on corrige ici.
        services.AddSingleton<IPlatformPricing>(new PlatformPricing(configuration));

        services.AddScoped<ISellerRepository, SellerRepository>();
        services.AddScoped<IStoreRepository, StoreRepository>();
        services.AddScoped<ISellerModuleApi, SellerModuleApi>();

        // L'IMPLÉMENTATION LOCALE, POUR LE SERVICE QUI LA SERT.
        //
        // Les quatre autres services obtiennent `IMerchantAccessApi` par le client
        // gRPC ; ici c'est la lecture directe, avec son cache. Sans cet
        // enregistrement, `MerchantsGrpcService` ne se construit pas — et le
        // service refuse de démarrer, ce qui est la bonne façon de découvrir un
        // oubli de composition.
        services.AddScoped<IMerchantAccessApi, MerchantAccessApi>();

        // ═════════════════════════════════════════════════════════════════════
        // L'ÉQUIPE D'UN VENDEUR.
        //
        // `MemberAccessResolver` EST LA GARDE DE TOUTES CES ROUTES.
        //
        // Il n'y a pas de `DenyUnlessOwnSellerAsync` en amont : la résolution de
        // l'appartenance EST le contrôle. L'enregistrer comme un service ordinaire
        // plutôt que le recopier dans chaque handler est ce qui garantit qu'il n'y
        // en a qu'un — et donc qu'aucune route ne peut en avoir une variante plus
        // permissive.
        // ═════════════════════════════════════════════════════════════════════
        services.AddScoped<ISellerMemberRepository, SellerMemberRepository>();
        services.AddScoped<ISellerRoleRepository, SellerRoleRepository>();
        services.AddScoped<ISellerInvitationRepository, SellerInvitationRepository>();
        services.AddScoped<MemberAccessResolver>();

        // LECTURE SEULE, ET AUCUN DÉPÔT EN FACE.
        //
        // Le journal n'est jamais écrit depuis le métier : `ModuleDbContext` s'en
        // charge à partir du `ChangeTracker`. Enregistrer un « dépôt » symétrique
        // avec un `AddAsync` inviterait quelqu'un à l'appeler à la main, et cette
        // ligne-là échapperait à la transaction qui fait toute la valeur du journal.
        services.AddScoped<IAuditTrailReader, AuditTrailReader>();

        // Aléa et SHA-256 : sans état, donc singleton.
        services.AddSingleton<IInvitationTokens, InvitationTokens>();

        // ═════════════════════════════════════════════════════════════════════
        // LES SEPT TRADUCTEURS « ÉVÉNEMENT DE DOMAINE → ÉVÉNEMENT D'INTÉGRATION ».
        //
        // UN OUBLI ICI NE CASSE RIEN ET NE DIT RIEN.
        //
        // Le répartiteur résout paresseusement : un événement de domaine sans
        // gestionnaire enregistré est ignoré en silence. Le membre serait
        // correctement écrit en base, la commande rendrait un succès, et
        // l'événement d'intégration ne partirait jamais — donc le rôle `Seller` ne
        // serait jamais greffé (lot B′), et le cache d'autorisation ne serait
        // jamais invalidé. Tout aurait l'air de fonctionner.
        //
        // IL N'Y EN A PAS HUIT. L'INVITATION EST PUBLIÉE PAR SON HANDLER DE
        // COMMANDE, parce que son événement porte le jeton et que l'agrégat ne
        // connaît que l'empreinte.
        // ═════════════════════════════════════════════════════════════════════
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

        // LE TRANSFERT DE PROPRIÉTÉ (lot 7.2, ISSUE-040). Sans cet
        // enregistrement, l'événement serait levé par l'agrégat et ne sortirait
        // nulle part : ni le cédant ni le bénéficiaire n'apprendraient le geste le
        // plus irréversible du module.
        services.AddScoped<IDomainEventHandler<SellerOwnershipTransferredDomainEvent>,
            SellerOwnershipTransferredDomainEventHandler>();

        // ═════════════════════════════════════════════════════════════════════
        // PLUS AUCUN STOCKAGE ICI. LES PIÈCES KYB VIVENT DANS HBA MEDIA.
        //
        // Ce module portait sa PROPRE implémentation S3 — signature AWS V4
        // comprise — pendant que le catalogue en portait une seconde, dans un
        // module qui l'ignorait. Deux copies d'un algorithme cryptographique :
        // une correction de l'une n'aurait jamais atteint l'autre, et la
        // divergence ne se serait vue que le jour où une signature aurait été
        // refusée en production.
        //
        // `CloudflareR2KybStorage`, `SimulatedKybStorage` et `IKybDocumentStorage`
        // sont supprimés. Le service média les remplace, avec en prime une
        // politique de rétention que ce module n'avait pas : une pièce d'identité
        // supprimée y survit un an, comme l'exige la conservation légale.
        // ═════════════════════════════════════════════════════════════════════

        services.AddScoped<IDomainEventHandler<SellerRegisteredDomainEvent>, SellerRegisteredDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<SellerActivatedDomainEvent>, SellerActivatedDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<SellerClosedDomainEvent>, SellerClosedDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<SellerReactivatedDomainEvent>, SellerReactivatedDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<SellerDeletedDomainEvent>, SellerDeletedDomainEventHandler>();

        // SANS CET ENREGISTREMENT, LE FICHIER D'UNE PIÈCE KYB RETIRÉE RESTE DANS
        // LE BUCKET PRIVÉ. Rien ne casse, rien n'alerte : la donnée personnelle
        // reste, simplement.
        services.AddScoped<IDomainEventHandler<KybDocumentRemovedDomainEvent>, KybDocumentRemovedDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<SellerKybRejectedDomainEvent>, SellerKybRejectedDomainEventHandler>();

        // ═════════════════════════════════════════════════════════════════════
        // LES DEUX MOITIÉS MANQUANTES DU PARCOURS KYB (§10.3).
        //
        // Seul le REFUS était publié. Le vendeur n'était donc prévenu que lorsqu'on
        // lui refusait son dossier — jamais de sa réception, jamais de sa
        // validation. Et l'exploitation n'avait aucun signal pour alimenter sa file
        // de validation : elle la découvrait en la rafraîchissant.
        //
        // `SellerKybVerifiedDomainEvent` était levé depuis l'origine SANS AUCUN
        // GESTIONNAIRE. Un événement de domaine sans destinataire ne lève pas, ne
        // journalise pas, et disparaît à la fin de l'unité de travail : rien ne
        // pouvait le signaler.
        // ═════════════════════════════════════════════════════════════════════
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

        // ═════════════════════════════════════════════════════════════════════
        // INBOX DE CONSOMMATION (§19.5) ET IDEMPOTENCE DES ÉCRITURES (§25).
        //
        // LES DEUX TABLES EXISTENT SANS CES DEUX LIGNES, ET NE SERVENT À RIEN.
        //
        // `IConsumerInbox` non enregistré, un gestionnaire qui l'injecte ne se
        // construit pas — le message part en erreur à la consommation, pas au
        // démarrage. Et `IIdempotencyStore` non enregistré, le filtre LAISSE
        // PASSER : il journalise en Erreur puis exécute la requête SANS protection
        // contre le rejeu. C'est le pire des cas — la route a l'air protégée.
        // ═════════════════════════════════════════════════════════════════════
        services.AddScoped<IConsumerInbox, EfConsumerInbox<SellersDbContext>>();
        // LE MAGASIN ET SON PURGEUR, EN UN SEUL GESTE.
        //
        // `ExpiresAtUtc` existait depuis le début, avec son index de purge, et
        // aucune ligne de code ne la lisait : une réservation inachevée bloquait
        // sa clé pour toujours (audit 1.8). Les deux enregistrements sont
        // désormais indissociables — voir `IdempotencyRegistration` pour la
        // raison, qui tient en une phrase : un huitième service qui ne copierait
        // que la première ligne n'aurait jamais de purge, sans rien signaler.
        services.AddIdempotence<SellersDbContext>();

        // ═════════════════════════════════════════════════════════════════════
        // LE DROIT À L'EFFACEMENT S'ARRÊTAIT À IDENTITY.
        //
        // `UserAnonymizedIntegrationEvent` n'était consommé que par user-service.
        // seller-service détient pourtant ce que la plateforme a de plus sensible :
        // cartes d'identité, registres de commerce, documents fiscaux. Sans ce
        // consommateur, ils survivaient à l'effacement du compte — sans plus rien
        // pour les relier à une personne, donc sans moyen de les retrouver.
        // ═════════════════════════════════════════════════════════════════════
        services.AddScoped<IIntegrationEventHandler<UserAnonymizedIntegrationEvent>,
            UserAnonymizedSellerPurgeHandler>();

        // ═════════════════════════════════════════════════════════════════════
        // LES DEUX COMPTEURS DE LA VITRINE, QUI VALAIENT ZÉRO POUR TOUT LE MONDE.
        //
        // `Rating` et `SalesCount` étaient persistés, projetés, affichés — et
        // n'avaient AUCUN alimenteur : `Seller.UpdateRating` n'avait pas un seul
        // appelant dans le dépôt, et rien n'incrémentait `SalesCount`. Un vendeur
        // ayant écoulé trois cents commandes était présenté comme n'ayant jamais
        // rien vendu.
        //
        // Les deux gestionnaires POSENT une valeur recalculée depuis la source, ils
        // n'accumulent pas : c'est ce qui les rend idempotents face à un rejeu, et
        // c'est la règle que `Seller.SetSalesCount` écrit lui-même.
        // ═════════════════════════════════════════════════════════════════════
        services.AddScoped<IIntegrationEventHandler<SellerRatingRecomputedIntegrationEvent>,
            SellerRatingHandler>();

        services.AddScoped<IIntegrationEventHandler<OrderConfirmedIntegrationEvent>,
            SellerSalesCountHandler>();

        services.AddValidatorsFromAssembly(ApplicationAssembly, includeInternalTypes: true);

        services.AddOutboxProcessor<SellersDbContext>();
    }

}
