using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HBA.Shared.Hosting;

/// <summary>Réglages de la base d'un service.</summary>
public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    /// <summary>Le service applique-t-il ses migrations avant d'écouter ?</summary>
    public bool? MigrateOnStartup { get; set; }

    /// <summary>
    /// Le processus applique-t-il ses migrations PUIS s'arrête-t-il, sans ouvrir de
    /// port ?
    /// </summary>
    public bool MigrateOnly { get; set; }
}

public static class DatabaseMigrationExtensions
{
    /// <summary>Applique les migrations en attente, si la configuration l'autorise.</summary>
    public static async Task<WebApplication> MigrateHbaDatabaseAsync<TDbContext>(
        this WebApplication app, CancellationToken cancellationToken = default)
        where TDbContext : DbContext
    {
        var options = app.Configuration
            .GetSection(DatabaseOptions.SectionName)
            .Get<DatabaseOptions>() ?? new DatabaseOptions();

        // `MigrateOnly` l'emporte : un Job de migration doit migrer, quel que soit
        // le réglage destiné au démarrage ordinaire du serveur.
        var enabled = options.MigrateOnly
                      || (options.MigrateOnStartup ?? app.Environment.IsDevelopment());

        var logger = app.Services
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("HBA.Database");

        if (!enabled)
        {
            // Journalisé, et non passé sous silence : « les tables n'existent pas »
            // est un symptôme qu'on met longtemps à relier à un réglage dont on
            // ignorait l'existence.
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
        await dbContext.Database.MigrateAsync(cancellationToken);

        logger.LogInformation("Migrations appliquées ({Context}).", typeof(TDbContext).Name);

        return app;
    }

    /// <summary>
    /// Vrai si le processus doit s'arrêter maintenant : les migrations sont faites,
    /// et rien d'autre n'était demandé.
    /// </summary>
    public static bool SortirApresMigrations(this WebApplication app)
    {
        var options = app.Configuration
            .GetSection(DatabaseOptions.SectionName)
            .Get<DatabaseOptions>() ?? new DatabaseOptions();

        if (!options.MigrateOnly)
        {
            return false;
        }

        app.Services
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("HBA.Database")
            .LogInformation(
                "Database:MigrateOnly — migrations terminées, le processus s'arrête "
                + "sans ouvrir de port.");

        return true;
    }
}
