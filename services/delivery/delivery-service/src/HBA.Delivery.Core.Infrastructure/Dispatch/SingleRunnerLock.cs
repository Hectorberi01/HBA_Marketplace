using System.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace HBA.Deliveries.Infrastructure.Dispatch;

/// <summary>UN SEUL PROCESSUS À LA FOIS DANS UNE BOUCLE DE FOND.</summary>
internal sealed class SingleRunnerLock : IAsyncDisposable
{
    /// <summary>Clés d'application des verrous.</summary>
    public const long DispatchKey = 771_001;

    public const long WebhookKey = 771_002;

    private readonly NpgsqlConnection? _connection;
    private readonly long _key;
    private readonly bool _openedHere;

    private SingleRunnerLock(NpgsqlConnection? connection, long key, bool openedHere)
    {
        _connection = connection;
        _key = key;
        _openedHere = openedHere;
    }

    /// <summary>Le verrou a-t-il été obtenu ? Faux = un autre processus travaille déjà.</summary>
    public bool Acquired => _connection is not null;

    /// <summary>Tente de prendre le verrou.</summary>
    public static async Task<SingleRunnerLock> TryAcquireAsync(
        DbContext dbContext, long key, CancellationToken cancellationToken)
    {
        if (dbContext.Database.GetDbConnection() is not NpgsqlConnection connection)
        {
            // Pas PostgreSQL : on n'a pas de verrou à offrir.
            return new SingleRunnerLock(null, key, openedHere: false);
        }

        var openedHere = connection.State is not ConnectionState.Open;
        if (openedHere)
        {
            await connection.OpenAsync(cancellationToken);
        }

        // pg_try_advisory_lock et NON pg_advisory_xact_lock : ce dernier est lié à
        // une transaction, et ces boucles n'en ouvrent pas d'explicite.
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT pg_try_advisory_lock($1)";
        command.Parameters.Add(new NpgsqlParameter { Value = key });

        var acquired = await command.ExecuteScalarAsync(cancellationToken) is true;

        if (acquired)
        {
            return new SingleRunnerLock(connection, key, openedHere);
        }

        if (openedHere)
        {
            await connection.CloseAsync();
        }

        return new SingleRunnerLock(null, key, openedHere: false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is null)
        {
            return;
        }

        try
        {
            await using var command = _connection.CreateCommand();
            command.CommandText = "SELECT pg_advisory_unlock($1)";
            command.Parameters.Add(new NpgsqlParameter { Value = _key });
            await command.ExecuteScalarAsync();
        }
        catch
        {
            // Libération best-effort : si la connexion est déjà tombée, PostgreSQL
            // a relâché le verrou avec la session.
        }

        if (_openedHere)
        {
            await _connection.CloseAsync();
        }
    }
}
