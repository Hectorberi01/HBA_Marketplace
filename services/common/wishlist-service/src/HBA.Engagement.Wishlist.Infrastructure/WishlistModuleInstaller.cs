using System.Reflection;
using HBA.Engagement.Wishlist.Infrastructure.Messaging.Kafka.Configuration;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using HBA.Shared.Application.Abstractions;
using HBA.Shared.Infrastructure.Modularity;
using HBA.Engagement.Wishlist.Application.Abstractions;
using HBA.Engagement.Wishlist.Application.Wishlists;
using HBA.Engagement.Wishlist.Domain.Wishlists;
using HBA.Engagement.Wishlist.Infrastructure.Persistence;

using HBA.Engagement.Wishlist.Infrastructure.Caching.Redis;
using HBA.Engagement.Wishlist.Infrastructure.Observability;
namespace HBA.Engagement.Wishlist.Infrastructure;

/// <summary>Enregistre le module Wishlist : DbContext, repository, validators, outbox.</summary>
public sealed class WishlistModuleInstaller : IModuleInstaller
{
    public string ModuleName => "Wishlist";

    public Assembly ApplicationAssembly => typeof(AddToWishlistCommand).Assembly;

    public void Install(IServiceCollection services, IConfiguration configuration)
    {
        // LE CACHE DE CE SERVICE (Caching/Redis/).
        services.AjouterCacheEngagementWishlist(configuration);

        // LES SONDES DE CE SERVICE (Observability/).
        services.AjouterObservabiliteEngagementWishlist(configuration);

        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Chaîne de connexion « Default » absente.");

        // L'outbox et l'inbox sont descendues dans `Messaging/Kafka/`, donc hors de
        // cet installeur : elles sont desormais enregistrees par
        // `AjouterMessagerieEngagementWishlist()`, que le composition root peut
        // oublier.
        services.AddHostedService<GardeDeCablage>();

        services.AddDbContext<WishlistDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", WishlistDbContext.SchemaName)));

        services.AddScoped<IWishlistUnitOfWork>(sp => sp.GetRequiredService<WishlistDbContext>());

        services.AddScoped<IWishlistRepository, WishlistRepository>();

        services.AddValidatorsFromAssembly(ApplicationAssembly, includeInternalTypes: true);

    }
}
