using HBA.Identity.Application.Abstractions;
using HBA.Identity.Infrastructure.Persistence;

namespace HBA.Identity.Api;

/// <summary>
/// Compte administrateur créé au premier démarrage. Section : « Admin ».
/// </summary>
/// <remarks>
/// `Password` NE DOIT JAMAIS ÊTRE VERSIONNÉ.
///
/// Il arrive par `ADMIN__PASSWORD`. La valeur par défaut de développement vit
/// dans `docker-compose.dev.yml`, dont l'en-tête assume que ses secrets sont
/// publics et strictement locaux.
/// </remarks>
public sealed class AdminSeedOptions
{
    public const string SectionName = "Admin";

    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;

    public string FirstName { get; set; } = "Admin";
    public string LastName { get; set; } = "Plateforme";

    /// <summary>Le domaine attend 8 à 15 chiffres. Bénin : indicatif +229.</summary>
    public string Phone { get; set; } = "+22900000000";
}

public static class IdentitySeedExtensions
{
    /// <summary>Valeurs de repli, actives UNIQUEMENT en Development.</summary>
    private const string DevelopmentEmail = "admin@hba.local";
    private const string DevelopmentPassword = "Admin123!";

    /// <summary>
    /// Sème les rôles système, puis le compte administrateur.
    /// </summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// POURQUOI CE CROCHET EXISTE : LA BOUCLE DE LA POULE ET DE L'ŒUF.
    ///
    /// Créer un administrateur exige d'être administrateur. Sur une base neuve,
    /// personne ne l'est. Sans amorçage, la console d'administration reste
    /// définitivement fermée, et `POST /api/auth/register` ne donne que « Buyer ».
    ///
    /// LES RÔLES SONT SEMÉS INCONDITIONNELLEMENT, L'ADMIN NON.
    ///
    /// « Buyer » est attribué par `RegisterUserCommandHandler` : sans lui en base,
    /// c'est l'INSCRIPTION ENTIÈRE qui échoue, pas seulement l'administration. Le
    /// semis des rôles ne dépend donc d'aucune configuration.
    ///
    /// IDEMPOTENT — LE MOT DE PASSE N'EST JAMAIS RÉINITIALISÉ.
    ///
    /// Un redémarrage sur une base peuplée ne touche à rien. Réappliquer le mot de
    /// passe de la configuration à chaque démarrage rendrait impossible d'en
    /// changer depuis l'application, et ferait d'une variable d'environnement
    /// oubliée une porte ouverte permanente.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
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
            // ON LÈVE, ET C'EST UN CHOIX ASSUMÉ — LE MONOLITHE SE CONTENTAIT
            //    D'UN AVERTISSEMENT.
            //
            // Dans le monolithe, un unique flux de journal recevait l'avertissement
            // et quelqu'un finissait par le lire. Ici quatorze conteneurs écrivent
            // en même temps : un WARNING au démarrage d'identity-service se perd
            // dans la minute. Le symptôme, lui, n'apparaît qu'au premier écran de
            // connexion — parfois des semaines plus tard, sans rien pour le relier
            // à une variable jamais renseignée.
            //
            // Un démarrage refusé nomme la cause tout de suite. Le coût est réel :
            // un service par ailleurs sain ne démarre pas. Il est préférable à une
            // plateforme qu'on croit installée et dans laquelle personne ne peut
            // entrer.
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
                // utilisateurs existants. Mais le message doit être impossible à
                // manquer.
                app.Logger.LogError(
                    "AMORÇAGE ADMINISTRATEUR ÉCHOUÉ ({Raison}) : aucun compte « {Email} » n'a été créé. "
                    + "Sur une base neuve, PERSONNE ne pourra ouvrir la console d'administration.",
                    outcome, options.Email);
                break;
        }

        // ── Vérification effective, par relecture ────────────────────────────
        //
        // `AlreadyPresent` ne dit rien de l'ÉTAT du compte. Un administrateur
        // suspendu, ou dont le rôle a été retiré, franchit l'amorçage sans erreur
        // tout en étant incapable d'entrer — c'est le cas qu'on ne découvrait
        // qu'à l'écran de connexion.
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
