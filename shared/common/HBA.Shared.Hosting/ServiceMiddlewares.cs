using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace HBA.Shared.Hosting;

/// <summary>Reprend l'identifiant de corrélation posé par la passerelle.</summary>
public sealed class ServiceCorrelationMiddleware
{
    public const string HeaderName = "X-Correlation-ID";

    private readonly RequestDelegate _next;

    public ServiceCorrelationMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        // ON REPREND CELUI DE LA PASSERELLE, ON N'EN REFABRIQUE PAS.
        //
        // En générer un nouveau casserait le lien entre la requête du client et
        // son traitement ici : les journaux du service et ceux de la passerelle
        // porteraient deux identifiants différents pour la MÊME requête, et le
        // rapprochement deviendrait impossible — exactement ce que la corrélation
        // sert à éviter. On n'en crée un que pour les appels qui n'en portent pas :
        // tâches de fond, sondes, appel direct entre services.
        var correlationId = context.Request.Headers[HeaderName].ToString();

        if (string.IsNullOrWhiteSpace(correlationId))
        {
            correlationId = Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("n");
        }

        context.Items[HeaderName] = correlationId;

        context.Response.OnStarting(state =>
        {
            ((HttpContext)state).Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        }, context);

        Activity.Current?.SetTag("hba.correlation_id", correlationId);

        await _next(context);
    }
}

/// <summary>Erreur uniforme en <c>application/problem+json</c>, sans fuite.</summary>
public sealed class ServiceExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ServiceExceptionMiddleware> _logger;

    public ServiceExceptionMiddleware(RequestDelegate next, ILogger<ServiceExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // Le client — ici, souvent la passerelle — a raccroché. Ce n'est pas
            // une panne du service, et le compter en 500 noierait les vraies.
            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode = 499;
            }
        }
        catch (DbUpdateConcurrencyException exception) when (!context.Response.HasStarted)
        {
            // ═════════════════════════════════════════════════════════════════
            // `ConcurrencyExceptionHandler` N'A JAMAIS EXISTÉ — LOT 5.1.
            //
            // Quatre configurations du dépôt — `OrderConfiguration`,
            // `InventoryItemConfiguration`, `PaymentConfiguration`,
            // `WalletConfigurations` — et l'encadré de `UsePostgresRowVersion`
            // annonçaient depuis des mois que le conflit optimiste était
            // « traduit en 409 par ConcurrencyExceptionHandler ». Ce type
            // n'existe NULLE PART dans le dépôt. Rien ne traduisait rien.
            //
            // CE QUE ÇA PROVOQUAIT : `DbUpdateConcurrencyException` dérive de
            // `DbUpdateException`, mais son exception interne n'est pas une
            // `PostgresException` — l'UPDATE n'a pas échoué, il a touché ZÉRO
            // ligne. `Doublon` rendait donc `null`, le filtre du bloc suivant ne
            // mordait pas, et le verrou optimiste de TOUT le dépôt ressortait en
            // 500 « Une erreur inattendue est survenue ».
            //
            // Le client — souvent la passerelle, souvent une application qui
            // réessaie sur 5xx — relançait alors une écriture perdante, sur une
            // ressource que quelqu'un d'autre venait de modifier. Et
            // l'exploitation comptait en panne serveur une garde qui avait fait
            // exactement son travail.
            //
            // CE BLOC PASSE AVANT CELUI DES DOUBLONS, ET CE N'EST PAS UN
            // DÉTAIL DE STYLE : `DbUpdateConcurrencyException` EST une
            // `DbUpdateException`. Placé après, il ne serait jamais atteint dès
            // que le filtre du bloc précédent accepterait le cas.
            //
            // AUCUN REJEU AUTOMATIQUE ICI, et c'est la règle posée par
            // `UsePostgresRowVersion` : `ModuleDbContext` dispatche les
            // événements de domaine et draine l'outbox AVANT `SaveChangesAsync`.
            // Rejouer dans le même scope re-publierait ces messages. Le rejeu
            // doit venir d'une requête neuve — donc du client, à qui l'on rend
            // 409 pour le lui dire.
            // ═════════════════════════════════════════════════════════════════
            var correlationConcurrence = context.Items[ServiceCorrelationMiddleware.HeaderName]?.ToString();

            _logger.LogWarning(
                exception,
                "Conflit de concurrence optimiste sur {Method} {Path}. "
                + "C'est une garde qui a fonctionné, pas une panne : une autre transaction "
                + "a modifié la ligne entre la lecture et l'écriture. [CorrelationId={CorrelationId}]",
                context.Request.Method, context.Request.Path, correlationConcurrence);

            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status409Conflict;
            context.Response.ContentType = "application/problem+json";

            var concurrence = new ProblemDetails
            {
                Title = "Conflict",
                Status = StatusCodes.Status409Conflict,

                // Le message dit quoi FAIRE. « Erreur de concurrence » ne se
                // traduit en aucun geste pour la personne qui le lit sur son
                // téléphone ; « recommencez » si.
                Detail = "Cette ressource vient d'être modifiée par quelqu'un d'autre. "
                    + "Rechargez et recommencez.",
                Instance = context.Request.Path
            };

            concurrence.Extensions["traceId"] = Activity.Current?.Id ?? context.TraceIdentifier;
            concurrence.Extensions["correlationId"] = correlationConcurrence;

            await context.Response.WriteAsJsonAsync(concurrence, context.RequestAborted);
        }
        catch (DbUpdateException exception) when (Doublon(exception) is { } contrainte && !context.Response.HasStarted)
        {
            // ═════════════════════════════════════════════════════════════════
            // UNE CONTRAINTE D'UNICITÉ QUI MORD N'EST PAS UNE PANNE DU SERVICE.
            //
            // Le lot 3.1 a posé des index uniques sur les objets financiers :
            // référence PSP d'un paiement, identifiant de remboursement externe,
            // clé d'idempotence d'un remboursement de retour et d'un versement
            // client. Ils existent pour qu'un rejeu ÉCHOUE au lieu d'encaisser ou
            // de verser deux fois.
            //
            // Sans ces lignes, cet échec-là remontait en 500 « Une erreur inattendue
            // est survenue » : la protection fonctionnait, et le client — souvent la
            // passerelle, souvent une application qui réessaie — lisait une panne
            // serveur. Il réessayait donc, indéfiniment, sur une opération qui ne
            // passera jamais. Et l'exploitation voyait un taux d'erreur 5xx là où
            // une garde avait fait exactement son travail.
            //
            // 409, ET LE NOM DE LA CONTRAINTE RESTE DANS LE JOURNAL.
            //
            // Il nomme une table et une colonne : c'est ce dont le support a besoin,
            // et ce qu'un client n'a pas à connaître. Le corps ne porte que le
            // `correlationId`, qui suffit à retrouver la ligne de journal — c'est la
            // même discipline que le message fixe du 500 juste en dessous.
            // ═════════════════════════════════════════════════════════════════
            var correlationId = context.Items[ServiceCorrelationMiddleware.HeaderName]?.ToString();

            _logger.LogWarning(
                exception,
                "Doublon refusé par la contrainte {Contrainte} sur {Method} {Path}. "
                + "C'est une garde qui a fonctionné, pas une panne. [CorrelationId={CorrelationId}]",
                contrainte, context.Request.Method, context.Request.Path, correlationId);

            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status409Conflict;
            context.Response.ContentType = "application/problem+json";

            var conflit = new ProblemDetails
            {
                Title = "Conflict",
                Status = StatusCodes.Status409Conflict,
                Detail = "Cette opération a déjà été enregistrée.",
                Instance = context.Request.Path
            };

            conflit.Extensions["traceId"] = Activity.Current?.Id ?? context.TraceIdentifier;
            conflit.Extensions["correlationId"] = correlationId;

            await context.Response.WriteAsJsonAsync(conflit, context.RequestAborted);
        }
        catch (Exception exception)
        {
            var correlationId = context.Items[ServiceCorrelationMiddleware.HeaderName]?.ToString();

            _logger.LogError(
                exception,
                "Exception non gérée sur {Method} {Path}. [CorrelationId={CorrelationId}]",
                context.Request.Method, context.Request.Path, correlationId);

            if (context.Response.HasStarted)
            {
                throw;
            }

            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/problem+json";

            var problem = new ProblemDetails
            {
                Title = "Internal Server Error",
                Status = StatusCodes.Status500InternalServerError,

                // MESSAGE FIXE. NE JAMAIS Y INTERPOLER `exception.Message`.
                //
                // Un service porte la chaîne de connexion à SA base. Une
                // `NpgsqlException` la contient dans son message. Interpoler ici
                // ferait sortir mot de passe PostgreSQL compris — vers la
                // passerelle, qui la relaierait au client.
                Detail = "Une erreur inattendue est survenue lors du traitement de la requête.",
                Instance = context.Request.Path
            };

            problem.Extensions["traceId"] = Activity.Current?.Id ?? context.TraceIdentifier;
            problem.Extensions["correlationId"] = correlationId;

            await context.Response.WriteAsJsonAsync(problem, context.RequestAborted);
        }
    }

    /// <summary>
    /// Le nom de la contrainte d'unicité violée, ou <c>null</c> si ce n'en est pas une.
    /// </summary>
    /// <remarks>
    /// ON DESCEND DANS L'EXCEPTION INTERNE, ET C'EST OBLIGATOIRE.
    ///
    /// EF Core enveloppe toujours l'erreur du pilote dans une `DbUpdateException`.
    /// Le `SqlState` — `23505` pour une violation d'unicité — n'est porté que par la
    /// `PostgresException` en dessous. Tester l'enveloppe ne rendrait jamais rien.
    /// </remarks>
    private static string? Doublon(DbUpdateException exception)
        => exception.InnerException is PostgresException { SqlState: "23505" } postgres
            ? postgres.ConstraintName ?? "(sans nom)"
            : null;
}
