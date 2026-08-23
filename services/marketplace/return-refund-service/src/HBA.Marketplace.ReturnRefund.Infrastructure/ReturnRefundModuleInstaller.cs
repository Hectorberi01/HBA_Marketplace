using System.Reflection;
using FluentValidation;
using HBA.Marketplace.ReturnRefund.Application.Abstractions;
using HBA.Marketplace.ReturnRefund.Application.Commands.CreateReturn;
using HBA.Marketplace.ReturnRefund.Application.Events;
using HBA.Marketplace.ReturnRefund.Domain.Events;
using HBA.Marketplace.ReturnRefund.Domain.Repositories;
using HBA.Marketplace.ReturnRefund.Infrastructure.BackgroundJobs;
using HBA.Marketplace.ReturnRefund.Infrastructure.Grpc.DeliveryClient;
using HBA.Marketplace.ReturnRefund.Infrastructure.Grpc.InventoryClient;
using HBA.Marketplace.ReturnRefund.Infrastructure.Grpc.MediaClient;
using HBA.Marketplace.ReturnRefund.Infrastructure.Grpc.OrderClient;
using HBA.Marketplace.ReturnRefund.Infrastructure.Grpc.PaymentClient;
using HBA.Marketplace.ReturnRefund.Infrastructure.Persistence;
using HBA.Marketplace.ReturnRefund.Infrastructure.Persistence.Repositories;
using HBA.Marketplace.ReturnRefund.Infrastructure.Redis;
using HBA.Financial.Contracts.Grpc;
using HBA.Ordering.Contracts.Grpc;
using HBA.Shared.Application.Abstractions;
using HBA.Shared.Infrastructure.Modularity;
using HBA.Shared.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HBA.Marketplace.ReturnRefund.Infrastructure;

public sealed class ReturnRefundModuleInstaller : IModuleInstaller
{
    public string ModuleName => "ReturnRefund";

    public Assembly ApplicationAssembly => typeof(CreateReturnCommand).Assembly;

    public void Install(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Chaine de connexion Default absente.");

        services.AddDbContext<ReturnRefundDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", ReturnRefundDbContext.SchemaName)));

        services.AddScoped<IReturnRefundUnitOfWork>(sp => sp.GetRequiredService<ReturnRefundDbContext>());
        services.AddScoped<IReturnRequestRepository, ReturnRequestRepository>();
        services.AddScoped<IReturnPolicyRepository, ReturnPolicyRepository>();

        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<ReturnPolicyCache>();

        services.AddOrderingGrpcClient(configuration);
        services.AddFinancialGrpcClient(configuration);
        services.AddScoped<IOrderGrpcClient, OrderGrpcClient>();
        services.AddScoped<IPaymentGrpcClient, PaymentGrpcClient>();
        services.AddScoped<IInventoryGrpcClient, InventoryGrpcClient>();
        services.AddScoped<IDeliveryGrpcClient, DeliveryGrpcClient>();
        services.AddScoped<IMediaGrpcClient, MediaGrpcClient>();

        // ═════════════════════════════════════════════════════════════════════
        // TROIS DE CES CINQ ADAPTATEURS NE PARLENT À PERSONNE.
        //
        // `OrderGrpcClient` et `PaymentGrpcClient` appellent réellement leur
        // interlocuteur — ils ont un champ injecté et un constructeur. Les trois
        // autres sont des BOUCHONS INTÉGRAUX : aucun champ, aucun constructeur,
        // une expression-corps qui fabrique un succès sur place et le rend.
        //
        //   • InventoryGrpcClient.ProcessReturnedStockAsync rend
        //     `Task.FromResult(Result.Success())`. `InspectReturnCommandHandler` le
        //     croit, boucle sur les articles, et enchaîne. LA MARCHANDISE RETOURNÉE
        //     N'ENTRE JAMAIS À L'INVENTAIRE : elle est inspectée, déclarée remettable
        //     en rayon, physiquement présente à l'entrepôt — et invisible au
        //     catalogue. Elle ne sera jamais revendue, et rien n'indiquera pourquoi.
        //
        //   • DeliveryGrpcClient.CreateReturnDeliveryAsync fabrique la chaîne
        //     `RET-DELIVERY-{guid}`. `RegisterReturnShipmentCommandHandler` l'inscrit
        //     sur le retour et la rend au client. AUCUNE COURSE D'ENLÈVEMENT N'EST
        //     CRÉÉE : le client attend un livreur que personne n'a commandé, avec un
        //     numéro de suivi qu'aucun système ne connaît.
        //
        //   • MediaGrpcClient.ValidateMediaAsync vérifie que la chaîne n'est pas
        //     vide, et rien d'autre — ni l'existence du média, ni son propriétaire.
        //     `AddEvidenceCommandHandler` accepte donc n'importe quel identifiant
        //     comme preuve photo, y compris celui du média d'un autre client.
        //
        // CE QUE CE GARDE-FOU FAIT, ET CE QU'IL NE FAIT PAS.
        //
        // Il N'IMPLÉMENTE PAS les vrais appels gRPC : ceux-là demandent des `.proto`,
        // des serveurs en face et des décisions de contrat qui ne sont pas dans ce
        // lot. Il applique la règle que ce dépôt s'est déjà donnée aux vagues 0.3 et
        // 3.2, pour `SimulatedPayoutGateway` et pour les passerelles sans
        // remboursement : UN ADAPTATEUR QUI SIMULE REFUSE DE DÉMARRER EN PRODUCTION.
        //
        // Le périmètre honnête est donc : la limite est DÉCLARÉE, elle refuse la
        // production, et elle s'ANNONCE bruyamment partout ailleurs.
        //
        // POURQUOI PAS UN DRAPEAU DE CONFIGURATION POUR PASSER OUTRE.
        //
        // C'est exactement la variable qu'on recopie d'un fichier d'environnement de
        // recette vers celui de production. Et il n'y aurait rien à assumer : ces
        // trois méthodes n'ont pas de version dégradée acceptable, elles ont une
        // version FAUSSE. Un service qui ne démarre pas se répare en une journée ;
        // un stock jamais réapprovisionné se découvre à l'inventaire annuel, et
        // plus rien ne permet alors de savoir quels retours auraient dû l'alimenter.
        //
        // CE QUI L'AVAIT LAISSÉ PASSER.
        //
        // `scripts/check-grpc-stubs.py` balayait `<dépôt>/src`, dossier hérité du
        // monolithe et inexistant ici : il rendait « 0 bouchon » depuis toujours.
        // Réparé, il désigne ces trois classes — et sait désormais reconnaître le
        // cas « classe *GrpcClient sans aucun champ client », qui est précisément
        // celui qui passait entre les mailles.
        // ═════════════════════════════════════════════════════════════════════
        GuardSimulatedGrpcAdapters(configuration);

        // ═════════════════════════════════════════════════════════════════════
        // LES DEUX TRAVAILLEURS SONT CE QUI FAIT AVANCER LE MODULE.
        //
        // Ils étaient enregistrés — et vides. `RefundRetryWorker` est le SEUL
        // émetteur d'`ExecuteRefundCommand` : sans lui, une décision de
        // remboursement reste une ligne en base et l'argent ne part jamais.
        // ═════════════════════════════════════════════════════════════════════
        services.AddHostedService<ExpireReturnsWorker>();
        services.AddHostedService<RefundRetryWorker>();

        // ═════════════════════════════════════════════════════════════════════
        // `OutboxPublisherWorker` A ÉTÉ SUPPRIMÉ, PAS IMPLÉMENTÉ.
        //
        // `AddOutboxProcessor<ReturnRefundDbContext>()` — deux lignes plus bas —
        // enregistre DÉJÀ `OutboxProcessor<ReturnRefundDbContext>` et son purgeur.
        // Écrire un second drain aurait donné deux processus lisant la même table
        // sans `SELECT … FOR UPDATE SKIP LOCKED` : chaque message publié DEUX FOIS,
        // donc chaque gain vendeur contre-passé deux fois par wallet-service.
        //
        // La coquille disait ce qu'elle aurait dû faire ; ce qu'elle aurait dû
        // faire existe déjà ailleurs. Le bon geste est de la retirer.
        // ═════════════════════════════════════════════════════════════════════

        // ═════════════════════════════════════════════════════════════════════
        // SANS CES DEUX LIGNES, LES ÉVÉNEMENTS DU MODULE NE SORTENT PAS.
        //
        // `DomainEventDispatcher` résout ses gestionnaires par le conteneur :
        // un gestionnaire non enregistré n'est pas une erreur de démarrage, c'est
        // un silence. `ReturnRefundApprovedIntegrationEvent` et
        // `ReturnRefundedIntegrationEvent` — dont wallet-service et
        // notification-service ont les consommateurs prêts depuis toujours — ne
        // seraient jamais publiés, et le vendeur garderait son gain sur une vente
        // remboursée.
        // ═════════════════════════════════════════════════════════════════════
        services.AddScoped<IDomainEventHandler<RefundRequestedDomainEvent>, RefundRequestedDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<RefundSucceededDomainEvent>, RefundSucceededDomainEventHandler>();

        services.AddValidatorsFromAssembly(ApplicationAssembly, includeInternalTypes: true);
        services.AddOutboxProcessor<ReturnRefundDbContext>();
    }

    /// <summary>
    /// Refuse le démarrage en production tant qu'un adaptateur gRPC de ce module
    /// simule sa réponse ; l'annonce bruyamment partout ailleurs.
    /// </summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// MÊME RÈGLE QUE `PaymentsModuleInstaller`, MÊME RAISON.
    ///
    /// Un adaptateur silencieux ne fait pas « tourner la plateforme en mode
    /// dégradé » : il la fait tourner dans un ÉTAT FAUX, que rien ne distingue
    /// après coup d'un état vrai. Il n'existe aucun moyen de retrouver, dans six
    /// mois, quels retours auraient dû réapprovisionner le stock ni quels clients
    /// ont reçu un numéro d'enlèvement imaginaire : ces appels n'ont laissé
    /// aucune trace, puisqu'ils n'ont jamais eu lieu.
    ///
    /// LA LISTE EST ÉCRITE À LA MAIN, ET C'EST SA FAIBLESSE.
    ///
    /// Elle ne se met pas à jour toute seule. Implémenter réellement l'un de ces
    /// trois adaptateurs SANS retirer sa ligne d'ici bloquerait la production
    /// pour rien — l'inverse du défaut qu'on corrige, mais un défaut quand même.
    /// `scripts/check-grpc-stubs.py` est le filet : il liste les bouchons réels à
    /// chaque exécution de `scripts/check-all.sh`, et l'écart entre sa sortie et
    /// cette liste se voit en une lecture.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
    private static void GuardSimulatedGrpcAdapters(IConfiguration configuration)
    {
        // (adaptateur, conséquence métier) — la conséquence, pas le symptôme
        // technique : c'est elle qui permet à qui lit le message de décider.
        var simulated = new (string Adapter, string Consequence)[]
        {
            ("InventoryGrpcClient.ProcessReturnedStockAsync",
             "la marchandise retournée n'est JAMAIS remise en stock, alors que le retour est clos « remettable en rayon »"),
            ("DeliveryGrpcClient.CreateReturnDeliveryAsync",
             "AUCUNE course d'enlèvement n'est créée ; le numéro rendu au client ne correspond à rien"),
            ("MediaGrpcClient.ValidateMediaAsync",
             "aucune preuve photo n'est vérifiée — ni son existence, ni son propriétaire")
        };

        var details = string.Join(
            Environment.NewLine,
            simulated.Select(adapter => $"  • {adapter.Adapter} — {adapter.Consequence}."));

        if (IsProduction(configuration))
        {
            throw new InvalidOperationException(
                "PRODUCTION AVEC DES ADAPTATEURS gRPC SIMULÉS — DÉMARRAGE REFUSÉ." + Environment.NewLine
                + Environment.NewLine
                + "Les adaptateurs suivants du module ReturnRefund ne contactent AUCUN serveur : ils"
                + " fabriquent leur réponse et rendent un succès." + Environment.NewLine
                + details + Environment.NewLine
                + Environment.NewLine
                + "Le refus est DÉLIBÉRÉ. Sans lui, le service démarrerait normalement, traiterait des"
                + " retours, et produirait des effets métier qui n'existent pas — sans une seule erreur,"
                + " sans une seule ligne de journal, et sans aucun moyen de reconstituer après coup ce"
                + " qui aurait dû se passer." + Environment.NewLine
                + Environment.NewLine
                + "Pour lever ce refus : implémenter réellement ces appels (clients gRPC générés à"
                + " partir des .proto d'inventory-service, delivery-service et media-service), puis"
                + " retirer leur ligne de GuardSimulatedGrpcAdapters.");
        }

        // Bruyant, et volontairement. En production ce cas est impossible (voir
        // ci-dessus) ; ailleurs, il faut qu'un développeur qui déroule un retour de
        // bout en bout sache qu'une partie du parcours est une FICTION — sans quoi
        // il conclura que « ça marche ».
        Console.WriteLine(
            "[ReturnRefund]  ADAPTATEURS gRPC SIMULÉS ACTIFS :" + Environment.NewLine
            + details + Environment.NewLine
            + "Le parcours de retour se déroule intégralement en développement, mais ces trois effets"
            + " n'ont AUCUNE contrepartie réelle. Le démarrage est refusé en production.");
    }

    /// <summary>
    /// Sommes-nous en production ?
    /// </summary>
    /// <remarks>
    /// Copie assumée de `PaymentsModuleInstaller.IsProduction` : l'installeur ne
    /// reçoit qu'un <see cref="IConfiguration"/> — les modules s'installent avant
    /// que l'hôte ne soit construit, donc pas d'IHostEnvironment.
    ///
    /// FAIL-SAFE À L'ENVERS DE CE QU'ON VOUDRAIT, ET DÉLIBÉRÉMENT. Un
    /// environnement inconnu est traité comme « pas la production », sinon un nom
    /// mal orthographié empêcherait de travailler. Le risque assumé est donc
    /// qu'une VRAIE prod dont ASPNETCORE_ENVIRONMENT serait mal renseigné passe au
    /// travers du refus — c'est pourquoi l'avertissement ci-dessus est aussi
    /// bruyant : il doit se voir dans les journaux de démarrage même quand
    /// personne ne les cherche.
    /// </remarks>
    private static bool IsProduction(IConfiguration configuration)
    {
        var environment = configuration["ASPNETCORE_ENVIRONMENT"]
            ?? configuration["DOTNET_ENVIRONMENT"]
            ?? string.Empty;

        return string.Equals(environment, "Production", StringComparison.OrdinalIgnoreCase);
    }
}

internal sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}
