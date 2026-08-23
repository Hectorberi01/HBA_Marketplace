using System.Reflection;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using HBA.Shared.Application.Abstractions;
using HBA.Shared.Infrastructure.Modularity;
using HBA.Shared.Infrastructure.Outbox;
using HBA.Engagement.Wishlist.Application.Abstractions;
using HBA.Engagement.Wishlist.Application.Wishlists;
using HBA.Engagement.Wishlist.Domain.Wishlists;
using HBA.Engagement.Wishlist.Infrastructure.Persistence;

namespace HBA.Engagement.Wishlist.Infrastructure;

/// <summary>Enregistre le module Wishlist : DbContext, repository, validators, outbox.</summary>
public sealed class WishlistModuleInstaller : IModuleInstaller
{
    public string ModuleName => "Wishlist";

    public Assembly ApplicationAssembly => typeof(AddToWishlistCommand).Assembly;

    public void Install(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Chaîne de connexion « Default » absente.");

        services.AddDbContext<WishlistDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", WishlistDbContext.SchemaName)));

        services.AddScoped<IWishlistUnitOfWork>(sp => sp.GetRequiredService<WishlistDbContext>());

        services.AddScoped<IWishlistRepository, WishlistRepository>();

        services.AddValidatorsFromAssembly(ApplicationAssembly, includeInternalTypes: true);

        services.AddOutboxProcessor<WishlistDbContext>();
    }
}
