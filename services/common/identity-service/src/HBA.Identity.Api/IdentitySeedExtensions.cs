using HBA.Identity.Application.Abstractions;
using HBA.Identity.Infrastructure.Persistence;

namespace HBA.Identity.Api;

/// <summary>Compte administrateur créé au premier démarrage.</summary>
public sealed class AdminSeedOptions
{
    public const string SectionName = "Admin";

    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;

    public string FirstName { get; set; } = "Admin";
    public string LastName { get; set; } = "Plateforme";

    /// <summary>Le domaine attend 8 à 15 chiffres.</summary>
    public string Phone { get; set; } = "+22900000000";
}

public static class IdentitySeedExtensions
{
    /// <summary>Valeurs de repli, actives UNIQUEMENT en Development.</summary>
    private const string DevelopmentEmail = "admin@hba.local";
    private const string DevelopmentPassword = "Admin123!";

    /// <summary>Sème les rôles système, puis le compte administrateur.</summary>
    public static async Task<WebApplication> SeedIdentityAsync(
        this WebApplication app, CancellationToken cancellationToken = default)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        await IdentityDataSeeder.SeedDefaultRolesAsync(dbContext, cancellationToken);
        app.Logger.LogInformation("Rôles système vérifiés.");

        var options = app.Configuration
            .GetSection(AdminSeedOptions.SectionName)
            .Get<AdminSeedOptions>() ?? new AdminSeedOptions();

        var isDevelopment = app.Environment.IsDevelopment();

        if (string.IsNullOrWhiteSpace(options.Email) && isDevelopment)
        {
            options.Email = DevelopmentEmail;
        }

        if (string.IsNullOrWhiteSpace(options.Password) && isDevelopment)
        {
            options.Password = DevelopmentPassword;
        }

        if (string.IsNullOrWhiteSpace(options.Email) || string.IsNullOrWhiteSpace(options.Password))
        {
            // ON LÈVE, ET C'EST UN CHOIX ASSUMÉ — LE MONOLITHE SE CONTENTAIT D'UN
            // AVERTISSEMENT.
            throw new InvalidOperationException(
                "Amorçage administrateur : ADMIN__EMAIL et ADMIN__PASSWORD sont obligatoires "
                + $"hors Development (environnement actuel : {app.Environment.EnvironmentName}). "
                + "Sans eux, aucun compte ne peut ouvrir la console d'administration sur une base neuve.");
        }

        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var outcome = await IdentityDataSeeder.SeedAdminUserAsync(
            dbContext, hasher,
            options.Email, options.Password,
            options.FirstName, options.LastName, options.Phone,
            cancellationToken);

        switch (outcome)
        {
            case IdentityDataSeeder.AdminSeedOutcome.Created:
                app.Logger.LogInformation(
                    "Amorçage administrateur : « {Email} » CRÉÉ (actif, rôle Admin).", options.Email);
                break;

            case IdentityDataSeeder.AdminSeedOutcome.AlreadyPresent:
                app.Logger.LogInformation(
                    "Amorçage administrateur : « {Email} » existe déjà — inchangé "
                    + "(le mot de passe n'est jamais réinitialisé).", options.Email);
                break;

            default:
                // Ici on ne lève PAS : le compte est peut-être déjà là sous une
                // autre forme, et refuser de démarrer priverait de service des
                // utilisateurs existants.
                app.Logger.LogError(
                    "AMORÇAGE ADMINISTRATEUR ÉCHOUÉ ({Raison}) : aucun compte « {Email} » n'a été créé. "
                    + "Sur une base neuve, PERSONNE ne pourra ouvrir la console d'administration.",
                    outcome, options.Email);
                break;
        }

        // ── Vérification effective, par relecture ────────────────────────────
        var blocker = await IdentityDataSeeder.VerifyAdminCanSignInAsync(
            dbContext, options.Email, cancellationToken);

        if (blocker is null)
        {
            app.Logger.LogInformation(
                "Première connexion possible : « {Email} » est actif et porte le rôle Admin.",
                options.Email);
        }
        else
        {
            app.Logger.LogError(
                "CONNEXION ADMINISTRATEUR IMPOSSIBLE pour « {Email} » : {Raison}.",
                options.Email, blocker);
        }

        return app;
    }
}
