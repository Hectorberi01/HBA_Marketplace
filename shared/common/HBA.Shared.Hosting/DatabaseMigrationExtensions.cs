using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HBA.Shared.Hosting;

/// <summary>Réglages de la base d'un service. Section de configuration : « Database ».</summary>
public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    /// <summary>
    /// Le service applique-t-il ses migrations avant d'écouter ?
    /// </summary>
    /// <remarks>
    /// `null` signifie « non renseigné », et c'est délibérément un booléen
    /// NULLABLE : la valeur par défaut dépend de l'environnement — vrai en
    /// Development, faux ailleurs — et un `bool` ordinaire ne permet pas de
    /// distinguer « faux parce qu'on l'a demandé » de « faux parce qu'absent ».
    /// </remarks>
    public bool? MigrateOnStartup { get; set; }
}

public static class DatabaseMigrationExtensions
{
    /// <summary>
    /// Applique les migrations en attente, si la configuration l'autorise.
    /// </summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// APPEL EXPLICITE, ET NON UN `IHostedService` ENREGISTRÉ EN COULISSE.
    ///
    /// Un service hébergé aurait évité de toucher aux treize `Program.cs`, mais
    /// il démarre en parallèle de Kestrel : le serveur peut accepter une requête
    /// pendant que les migrations tournent encore, et la première requête tombe
    /// alors sur des tables absentes — de façon intermittente, donc difficile à
    /// reproduire. Ici l'appel est `await`é avant `app.Run()` : quand le port
    /// s'ouvre, le schéma est à jour.
    ///
    /// Le coût assumé est une ligne visible dans chaque `Program.cs`. C'est aussi
    /// un avantage : le comportement se lit là où il se produit.
    ///
    /// CE N'EST PAS UN OUTIL DE DÉPLOIEMENT.
    ///
    /// Deux instances qui démarrent ensemble migreront en concurrence. EF pose un
    /// verrou consultatif côté PostgreSQL, ce qui évite la corruption, mais rien
    /// ne garantit l'ordre ni le délai. D'où le défaut à FAUX hors Development :
    /// en production, les migrations restent une étape que l'on déclenche et que
    /// l'on regarde. `Database__MigrateOnStartup=true` permet de l'activer
    /// sciemment, par exemple sur un environnement de recette à instance unique.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
    public static async Task<WebApplication> MigrateHbaDatabaseAsync<TDbContext>(
        this WebApplication app, CancellationToken cancellationToken = default)
        where TDbContext : DbContext
    {
        var options = app.Configuration
            .GetSection(DatabaseOptions.SectionName)
            .Get<DatabaseOptions>() ?? new DatabaseOptions();

        var enabled = options.MigrateOnStartup ?? app.Environment.IsDevelopment();

        var logger = app.Services
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("HBA.Database");

        if (!enabled)
        {
            // Journalisé, et non passé sous silence : « les tables n'existent
            // pas » est un symptôme qu'on met longtemps à relier à un réglage
            // dont on ignorait l'existence.
            logger.LogInformation(
                "Migrations non appliquées au démarrage ({Context}). "
                + "Database:MigrateOnStartup vaut false (défaut hors Development).",
                typeof(TDbContext).Name);

            return app;
        }

        await using var scope = app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TDbContext>();

        var pending = (await dbContext.Database
            .GetPendingMigrationsAsync(cancellationToken)).ToArray();

        if (pending.Length == 0)
        {
            logger.LogInformation("Schéma à jour ({Context}).", typeof(TDbContext).Name);
            return app;
        }

        logger.LogInformation(
            "Application de {Count} migration(s) sur {Context} : {Migrations}",
            pending.Length, typeof(TDbContext).Name, string.Join(", ", pending));

        // ON NE RATTRAPE PAS L'EXCEPTION.
        //
        // Un service dont le schéma n'a pas pu être mis à jour ne peut rien
        // servir de juste. Le laisser démarrer produirait des erreurs SQL
        // dispersées sur les premières requêtes, chacune désignant une colonne
        // manquante plutôt que la migration qui a échoué. Échouer ici nomme la
        // cause une fois, au bon endroit.
        await dbContext.Database.MigrateAsync(cancellationToken);

        logger.LogInformation("Migrations appliquées ({Context}).", typeof(TDbContext).Name);

        return app;
    }
}
