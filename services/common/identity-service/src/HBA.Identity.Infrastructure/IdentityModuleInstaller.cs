using HBA.Shared.Infrastructure.Inbox;
using HBA.Shared.Infrastructure.Idempotency;
using HBA.Identity.Domain.Mfa;
using FluentValidation;
using HBA.Drivers.Contracts.IntegrationEvents;
using HBA.Food.Contracts.IntegrationEvents;
using HBA.Identity.Application.Abstractions;
using HBA.Identity.Application.Users.Commands.RegisterUser;
using HBA.Identity.Application.Users.EventHandlers;
using HBA.Identity.Application.Users;
using HBA.Identity.Contracts;
using HBA.Identity.Domain.Roles;
using HBA.Identity.Domain.Users.Events;
using HBA.Identity.Domain.Users;
using HBA.Identity.Infrastructure.Persistence;
using HBA.Identity.Infrastructure.Public;
using HBA.Identity.Infrastructure.Security;
using HBA.Merchants.Contracts.IntegrationEvents;
using HBA.Shared.Application.Abstractions;
using HBA.Shared.Domain.Events;
using HBA.Shared.Infrastructure.Modularity;
using HBA.Shared.Infrastructure.Outbox;
using HBA.Shared.IntegrationEvents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace HBA.Identity.Infrastructure;

/// <summary>
/// Enregistre tout le module Identity : DbContext (schéma propre), repositories,
/// API publique, services d'auth (BCrypt, JWT, TOTP), handlers d'events,
/// validators, processeur d'outbox.
/// </summary>
public sealed class IdentityModuleInstaller : IModuleInstaller
{
    public string ModuleName => "Identity";

    public Assembly ApplicationAssembly => typeof(RegisterUserCommand).Assembly;

    public void Install(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Chaîne de connexion « Default » absente.");

        services.AddDbContext<IdentityDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", IdentityDbContext.SchemaName)));

        services.AddScoped<IIdentityUnitOfWork>(sp => sp.GetRequiredService<IdentityDbContext>());

        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IMfaChallengeRepository, MfaChallengeRepository>();

        // Socle du §5 et du §19.5.
        services.AddScoped<IConsumerInbox, EfConsumerInbox<IdentityDbContext>>();
        services.AddScoped<IIdempotencyStore, EfIdempotencyStore<IdentityDbContext>>();
        services.AddScoped<IRoleRepository, RoleRepository>();
        services.AddScoped<IIdentityModuleApi, IdentityModuleApi>();
        services.AddScoped<AuthTokenIssuer>();

        // Options + services de sécurité.
        services.AddSingleton(BuildJwtOptions(configuration));
        services.AddSingleton<IPasswordHasher, BcryptPasswordHasher>();
        services.AddSingleton<ISecureTokenGenerator, Sha256TokenGenerator>();
        services.AddSingleton<IJwtTokenGenerator, JwtTokenGenerator>();
        services.AddSingleton<ITotpService, TotpService>();
        services.AddSingleton<IAuthTokenSettings, AuthTokenSettings>();

        // Politique d'activation des comptes (section « Identity:Registration »).
        // Section absente = valeurs par défaut = les plus strictes.
        services.AddSingleton(BuildRegistrationOptions(configuration));
        services.AddSingleton<IRegistrationPolicy, RegistrationPolicy>();

        // Handlers de domain events.
        services.AddScoped<IDomainEventHandler<UserRegisteredDomainEvent>, UserRegisteredDomainEventHandler>();

        // ═════════════════════════════════════════════════════════════════
        // RÔLES MÉTIER — TROIS ÉVÉNEMENTS VENUS D'AILLEURS.
        //
        // Sans ces trois lignes, les rôles Seller, FoodPartner et Driver ne sont
        // attribués par personne : ils existent en base, semés au démarrage, et
        // aucun compte ne les porte. Les BFF partenaire et livreur répondent
        // alors 403 à tout le monde, sans qu'aucun journal ne relie le refus à
        // l'inscription qui aurait dû donner le droit.
        // ═════════════════════════════════════════════════════════════════
        services.AddScoped<BusinessRoleGrant>();

        services.AddScoped<IIntegrationEventHandler<SellerRegisteredIntegrationEvent>, GrantSellerRoleHandler>();
        services.AddScoped<IIntegrationEventHandler<RestaurantApprovedIntegrationEvent>, GrantFoodPartnerRoleHandler>();
        // `DriverVerifiedIntegrationEvent` DE `HBA.Drivers.Contracts`, PAS DE
        //    `HBA.Deliveries.Contracts` — les deux le déclaraient, aux champs
        //    identiques, et rendaient le même « driver.verified ». Le consommateur
        //    ne voyait que le type retenu par ordre alphabétique : enregistré sur
        //    l'autre, ce handler n'aurait JAMAIS été appelé, sans erreur, et le rôle
        //    `Driver` ne serait attribué à personne. La déclaration côté Deliveries
        //    a été retirée : l'agrégat décrit est le livreur, pas la course.
        services.AddScoped<IIntegrationEventHandler<DriverVerifiedIntegrationEvent>, GrantDriverRoleHandler>();

        // ═════════════════════════════════════════════════════════════════════
        // LES DEUX LIGNES QUI RENDENT LE MODULE DES MEMBRES UTILISABLE.
        //
        // `MapSellerGroup` ne regarde que la claim de rôle du jeton. Sans le
        // premier consommateur, un membre correctement écrit en base est refoulé
        // par le ROUTAGE, avant tout handler et avant toute permission — et rien,
        // ni côté merchant ni ici, ne le signale.
        //
        // Le second est asymétrique par nature : il ne retire le rôle que si
        // seller-service a établi qu'il ne reste AUCUNE autre appartenance. Voir
        // l'encadré du handler.
        // ═════════════════════════════════════════════════════════════════════
        services.AddScoped<IIntegrationEventHandler<SellerMemberJoinedIntegrationEvent>,
            GrantSellerRoleToMemberHandler>();
        services.AddScoped<IIntegrationEventHandler<SellerMemberRevokedIntegrationEvent>,
            RevokeSellerRoleOnMemberRemovedHandler>();
        services.AddScoped<IDomainEventHandler<UserEmailConfirmedDomainEvent>, UserEmailConfirmedDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<UserProfileUpdatedDomainEvent>, UserProfileUpdatedDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<UserAnonymizedDomainEvent>, UserAnonymizedDomainEventHandler>();

        services.AddValidatorsFromAssembly(ApplicationAssembly, includeInternalTypes: true);

        services.AddOutboxProcessor<IdentityDbContext>();
    }

    private static JwtOptions BuildJwtOptions(IConfiguration configuration)
    {
        var section = configuration.GetSection(JwtOptions.SectionName);
        return new JwtOptions
        {
            Issuer = section["Issuer"] ?? "marketplace",
            Audience = section["Audience"] ?? "marketplace",
            SigningKey = section["SigningKey"]
                ?? throw new InvalidOperationException("Clé de signature JWT absente (Jwt:SigningKey)."),
            AccessTokenMinutes = ParseInt(section["AccessTokenMinutes"], 15),
            RefreshTokenDays = ParseInt(section["RefreshTokenDays"], 30),
            EmailVerificationHours = ParseInt(section["EmailVerificationHours"], 48)
        };
    }

    /// <summary>
    /// Lit « Identity:Registration » à la main, comme <see cref="BuildJwtOptions"/>.
    ///
    /// Pas de <c>Bind()</c> : ce projet ne référence que
    /// <c>Microsoft.Extensions.Configuration.Abstractions</c>, où l'extension
    /// n'existe pas — elle vit dans le paquet <c>…Configuration.Binder</c>. Ajouter
    /// une dépendance pour lire deux booléens serait disproportionné, et créerait
    /// deux façons de lire la configuration dans le même fichier.
    /// </summary>
    private static RegistrationOptions BuildRegistrationOptions(IConfiguration configuration)
    {
        var section = configuration.GetSection(RegistrationOptions.SectionName);
        return new RegistrationOptions
        {
            // Défauts STRICTS : une clé absente ou illisible ferme la porte.
            RequireApprovalForBuyers = ParseBool(section["RequireApprovalForBuyers"], fallback: true),
            RequireApprovalForAdminCreated = ParseBool(section["RequireApprovalForAdminCreated"], fallback: false)
        };
    }

    private static int ParseInt(string? value, int fallback)
        => int.TryParse(value, out var parsed) ? parsed : fallback;

    /// <summary>
    /// Tolère « 1 » et « 0 » en plus de « true »/« false ».
    ///
    /// Ces valeurs arrivent d'un fichier .env, pas d'un JSON typé : quelqu'un
    /// écrira <c>REQUIRE_APPROVAL_BUYERS=1</c> un jour, et un <c>bool.TryParse</c>
    /// seul répondrait « false » en silence — c'est-à-dire l'exact contraire de
    /// l'intention, sur un réglage de sécurité.
    /// </summary>
    private static bool ParseBool(string? value, bool fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var normalized = value.Trim();
        if (bool.TryParse(normalized, out var parsed))
        {
            return parsed;
        }

        // Minuscules : « YES » et « ON » sont des réponses aussi valables que « yes ».
        return normalized.ToLowerInvariant() switch
        {
            "1" or "yes" or "on" => true,
            "0" or "no" or "off" => false,
            _ => fallback
        };
    }
}
