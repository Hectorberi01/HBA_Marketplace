using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace HBA.Shared.Hosting.Grpc;

/// <summary>Côté SERVEUR : traduit une exception non gérée en statut gRPC parlant.</summary>
public sealed class TraductionDesErreursServerInterceptor : Interceptor
{
    private readonly ILogger<TraductionDesErreursServerInterceptor> _journal;

    public TraductionDesErreursServerInterceptor(
        ILogger<TraductionDesErreursServerInterceptor> journal)
        => _journal = journal;

    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        try
        {
            return await continuation(request, context).ConfigureAwait(false);
        }
        catch (RpcException)
        {
            // Déjà tranché en amont : on ne retraduit pas.
            throw;
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            // L'appelant a raccroché — souvent son échéance de 5 s.
            throw new RpcException(new Status(StatusCode.Cancelled, "Call cancelled by caller."));
        }
        catch (DbUpdateConcurrencyException exception)
        {
            // `Aborted` EST LE CODE PRÉVU POUR UN CONFLIT DE CONCURRENCE — c'est
            // l'équivalent gRPC du 409 rendu sur le chemin HTTP. Il dit à
            // l'appelant que rejouer la SÉQUENCE ENTIÈRE peut aboutir, ce qui est
            // exactement vrai d'un verrou optimiste.
            Consigner(exception, context, "Conflit de concurrence optimiste");

            throw new RpcException(new Status(
                StatusCode.Aborted,
                "Resource was modified concurrently. Retry the whole operation."));
        }
        catch (DbUpdateException exception) when (Doublon(exception) is { } contrainte)
        {
            // `AlreadyExists` : une contrainte d'unicité a mordu.
            Consigner(exception, context, $"Doublon refusé par la contrainte {contrainte}");

            throw new RpcException(new Status(
                StatusCode.AlreadyExists, "Duplicate rejected by a uniqueness constraint."));
        }
        catch (Exception exception)
        {
            // `Internal` PLUTÔT QUE `Unknown`, ET LA NUANCE EST UTILE.
            Consigner(exception, context, "Exception non gérée");

            throw new RpcException(new Status(StatusCode.Internal, "Internal error."));
        }
    }

    private void Consigner(Exception exception, ServerCallContext context, string quoi)
        => _journal.LogError(
            exception,
            "{Quoi} sur {Methode}. [CorrelationId={CorrelationId}]",
            quoi,
            context.Method,
            context.RequestHeaders.GetValue("x-correlation-id"));

    /// <summary>Le nom de la contrainte d'unicité violée, ou nul.</summary>
    private static string? Doublon(DbUpdateException exception)
        => exception.InnerException is PostgresException { SqlState: "23505" } postgres
            ? postgres.ConstraintName ?? "(sans nom)"
            : null;
}
