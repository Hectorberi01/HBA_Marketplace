using System.Reflection;
using HBA.Engagement.Reviews.Infrastructure.Messaging.Kafka.Configuration;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using HBA.Shared.Application.Abstractions;
using HBA.Shared.Domain.Events;
using HBA.Shared.Infrastructure.Modularity;
using HBA.Engagement.Reviews.Application.Abstractions;
using HBA.Engagement.Reviews.Application.Reviews.Commands.SubmitReview;
using HBA.Engagement.Reviews.Application.Reviews.EventHandlers;
using HBA.Engagement.Reviews.Contracts;
using HBA.Engagement.Reviews.Domain.Reviews;
using HBA.Engagement.Reviews.Domain.Reviews.Events;
using HBA.Engagement.Reviews.Infrastructure.Persistence;
using HBA.Engagement.Reviews.Infrastructure.Public;

using HBA.Engagement.Reviews.Infrastructure.Caching.Redis;
using HBA.Engagement.Reviews.Infrastructure.Observability;
namespace HBA.Engagement.Reviews.Infrastructure;

/// <summary>
/// Enregistre le module Reviews : DbContext, repository, API publique, handlers,
/// validators, outbox.
/// </summary>
public sealed class ReviewsModuleInstaller : IModuleInstaller
{
    public string ModuleName => "Reviews";

    public Assembly ApplicationAssembly => typeof(SubmitReviewCommand).Assembly;

    public void Install(IServiceCollection services, IConfiguration configuration)
    {
        // LE CACHE DE CE SERVICE (Caching/Redis/).
        services.AjouterCacheEngagementReviews(configuration);

        // LES SONDES DE CE SERVICE (Observability/).
        services.AjouterObservabiliteEngagementReviews(configuration);

        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Chaîne de connexion « Default » absente.");

        // L'outbox et l'inbox sont descendues dans `Messaging/Kafka/`, donc hors de
        // cet installeur : elles sont desormais enregistrees par
        // `AjouterMessagerieEngagementReviews()`, que le composition root peut
        // oublier.
        services.AddHostedService<GardeDeCablage>();

        services.AddDbContext<ReviewsDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", ReviewsDbContext.SchemaName)));

        services.AddScoped<IReviewsUnitOfWork>(sp => sp.GetRequiredService<ReviewsDbContext>());

        services.AddScoped<IReviewRepository, ReviewRepository>();
        services.AddScoped<IReviewsModuleApi, ReviewsModuleApi>();

        services.AddScoped<IDomainEventHandler<ReviewPublishedDomainEvent>, ReviewPublishedDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<ReviewRejectedDomainEvent>, ReviewRejectedDomainEventHandler>();

        services.AddValidatorsFromAssembly(ApplicationAssembly, includeInternalTypes: true);

    }
}
