using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace HBA.Merchants.IntegrationTests;

/// <summary>
/// Retient les journaux d'ERREUR de l'hôte de test, pour qu'un 500 puisse dire
/// ce qui l'a produit.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// CE QUI ÉTAIT CASSÉ, ET CE QUE ÇA A COÛTÉ.
///
/// Le 2 septembre, vingt-trois cas de cette suite sont tombés en intégration
/// continue sur des 500 identiques. La réponse HTTP ne porte qu'un
/// `correlationId` — c'est délibéré, une trace interne n'a rien à faire chez le
/// client. L'EXCEPTION, elle, est écrite par le gestionnaire global dans les
/// journaux de l'hôte de test, et ces journaux ne vivaient que dans la sortie du
/// job : lisible des seuls administrateurs du dépôt, et perdue dès l'expiration.
///
/// Résultat : la suite passait 38/38 sur le poste et tombait 23 fois sur le
/// runner, sans que la cause soit accessible à qui que ce soit d'autre.
/// `Parcours.ReussirAsync` disait déjà, en toutes lettres, qu'elle « ne remplace
/// pas les journaux du service ». C'est ce trou-là qu'on ferme.
///
/// CE QUE ÇA NE COUVRE PAS.
///
///   • Les tests qui vérifient un code de statut à l'assertion — `PieceKybTests`
///     par exemple — n'appellent pas `ReussirAsync` et ne verront donc rien de
///     plus. Leur message dit « attendu 400, reçu 500 », et la cause reste dans
///     la sortie du pas.
///   • Rien n'est retenu au-dessous de `Error` : une requête qui rend 500 SANS
///     que rien ne soit journalisé ne laissera aucune trace ici. Ce serait alors
///     le gestionnaire global qu'il faudrait regarder, pas ce fichier.
///   • Ce n'est pas un contrôle. Aucun test ne devient plus tolérant : on ajoute
///     de la cause à un échec, on n'en retire aucun.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
internal static class JournalHote
{
    /// <summary>
    /// Une borne, parce qu'un hôte de test bavard remplirait la mémoire sur une
    /// suite longue. Les entrées les plus anciennes sont abandonnées.
    /// </summary>
    private const int Capacite = 200;

    private static readonly ConcurrentQueue<Entree> Entrees = new();

    /// <param name="Correlation">
    /// L'identifiant de trace ACTIF au moment de l'écriture. C'est lui que la
    /// réponse rend au client sous le nom `correlationId` : il permet de relier
    /// une réponse 500 précise à l'exception qui l'a causée, plutôt que de
    /// montrer « la dernière erreur vue », qui peut venir d'une autre requête.
    /// </param>
    internal sealed record Entree(
        DateTime Instant, string Categorie, string Message, string? Exception, string? Correlation);

    internal static void Ajouter(Entree entree)
    {
        Entrees.Enqueue(entree);

        while (Entrees.Count > Capacite && Entrees.TryDequeue(out _))
        {
            // On ne garde que les dernières : un échec se diagnostique avec ce qui
            // vient de se passer, pas avec le début de la suite.
        }
    }

    /// <summary>
    /// Les erreurs liées à une réponse donnée, ou à défaut les plus récentes.
    /// </summary>
    /// <param name="correlation">
    /// Le `correlationId` lu dans le corps de la réponse. Null ou inconnu, on
    /// rend les dernières entrées — MOINS SÛR, et le texte rendu le dit.
    /// </param>
    internal static string Pour(string? correlation, int maximum = 3)
    {
        var toutes = Entrees.ToArray();

        var ciblees = correlation is { Length: > 0 }
            ? toutes.Where(e => e.Correlation == correlation).ToArray()
            : [];

        var exact = ciblees.Length > 0;
        var retenues = exact
            ? ciblees
            : toutes.TakeLast(maximum).ToArray();

        if (retenues.Length == 0)
        {
            return "\n  journal de l'hôte : aucune erreur journalisée "
                   + "(le 500 ne vient donc pas d'une exception journalisée)";
        }

        var entete = exact
            ? $"\n  journal de l'hôte (corrélation {correlation}) :"
            : "\n  journal de l'hôte (DERNIÈRES erreurs, corrélation non retrouvée — "
              + "elles peuvent venir d'une autre requête) :";

        var lignes = retenues.Select(e =>
        {
            var texte = $"\n    [{e.Categorie}] {e.Message}";
            return e.Exception is { Length: > 0 }
                ? texte + "\n      " + e.Exception.Replace("\n", "\n      ")
                : texte;
        });

        return entete + string.Concat(lignes);
    }
}

/// <summary>Le fournisseur à brancher sur l'hôte de test.</summary>
internal sealed class JournalHoteProvider : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new JournalHoteLogger(categoryName);

    public void Dispose()
    {
        // Rien à libérer : la file est statique et vit le temps du processus.
    }

    private sealed class JournalHoteLogger(string categorie) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        // AU-DESSUS DE `Error` SEULEMENT. Retenir `Information` ferait remonter,
        // dans le message d'un échec, des dizaines de lignes de requêtes EF sans
        // rapport — et l'on aurait remplacé un message muet par un message
        // illisible.
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            JournalHote.Ajouter(new JournalHote.Entree(
                DateTime.UtcNow,
                categorie,
                formatter(state, exception),
                exception?.ToString(),
                Activity.Current?.TraceId.ToHexString()));
        }
    }
}
