using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace HBA.Shared.Hosting.Grpc;

/// <summary>
/// Côté SERVEUR : traduit une exception non gérée en statut gRPC parlant.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// LE PENDANT gRPC DE `ServiceExceptionMiddleware`, QUI N'EXISTAIT PAS.
///
/// Sur le chemin HTTP, une exception non gérée est triée : conflit optimiste en
/// 409, violation d'unicité en 409, client qui raccroche en 499, le reste en 500
/// avec un message fixe. Sur le chemin gRPC, aucun serveur ne posait le moindre
/// filtre. <c>Grpc.AspNetCore</c> convertissait donc TOUT en
/// <c>StatusCode.Unknown</c> avec « Exception was thrown by handler » —
/// <c>EnableDetailedErrors</c> étant à faux, choix délibéré et correct pour la
/// confidentialité.
///
/// CONSÉQUENCE : UNE PANNE DE BASE DE DONNÉES ET UN BUG DE SÉRIALISATION
/// ÉTAIENT LE MÊME `Unknown` SANS DÉTAIL, côté appelant. Impossible de décider
/// s'il fallait réessayer, alerter, ou corriger du code.
///
/// ET DEPUIS QUE LES CLIENTS ONT UN DISJONCTEUR, LE TRI COMPTE DOUBLE.
///
/// Un conflit de concurrence optimiste est une GARDE QUI A FONCTIONNÉ. Laissé en
/// <c>Unknown</c>, il est compté comme une panne : dix écritures concurrentes
/// sur une même commande — un cas ordinaire un jour de soldes — ouvriraient le
/// disjoncteur de tous les appelants. Traduit en <c>Aborted</c>, il ne compte
/// pas. Le même raisonnement vaut pour la violation d'unicité, qui existe
/// précisément pour qu'un rejeu échoue.
/// ═════════════════════════════════════════════════════════════════════════════
///
/// <para>
/// <b>LE MESSAGE RENDU RESTE FIXE POUR LE CAS GÉNÉRAL.</b> Même discipline
/// que le 500 du chemin HTTP : le détail part dans le JOURNAL du service appelé,
/// pas dans la réponse. Le « client » est ici un autre service, qui journalise ce
/// qu'il reçoit — une chaîne de connexion PostgreSQL partie d'ici finirait dans
/// les journaux d'un service voisin.
/// </para>
///
/// <para>
/// <b>UNE `RpcException` PASSE INTACTE.</b> Elle a déjà été choisie par
/// quelqu'un — <c>InternalCallServerInterceptor</c>, ou un service qui refuse un
/// argument. La retraduire écraserait une décision prise en connaissance de
/// cause.
/// </para>
///
/// <para>
/// <b>CE QU'IL NE FAIT PAS : il ne traduit pas les `Result` d'échec.</b> Un
/// refus métier voyage « en bande » dans la réponse (<c>succeeded = false</c> +
/// <c>reason_code</c>), c'est le parti pris du dépôt et il est cohérent. Cet
/// intercepteur ne voit que ce qui est LEVÉ.
/// </para>
/// </remarks>
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
            // L'appelant a raccroché — souvent son échéance de 5 s. Ce n'est pas
            // une panne d'ici, et le compter comme telle noierait les vraies.
            throw new RpcException(new Status(StatusCode.Cancelled, "Call cancelled by caller."));
        }
        catch (DbUpdateConcurrencyException exception)
        {
            // `Aborted` EST LE CODE PRÉVU POUR UN CONFLIT DE CONCURRENCE — c'est
            // l'équivalent gRPC du 409 rendu sur le chemin HTTP. Il dit à
            // l'appelant que rejouer la SÉQUENCE ENTIÈRE peut aboutir, ce qui est
            // exactement vrai d'un verrou optimiste.
            //
            // AUCUN REJEU ICI, et c'est la règle de `UsePostgresRowVersion` :
            // `ModuleDbContext` dispatche les événements de domaine et draine
            // l'outbox AVANT `SaveChangesAsync`. Rejouer dans le même scope
            // republierait ces messages.
            Consigner(exception, context, "Conflit de concurrence optimiste");

            throw new RpcException(new Status(
                StatusCode.Aborted,
                "Resource was modified concurrently. Retry the whole operation."));
        }
        catch (DbUpdateException exception) when (Doublon(exception) is { } contrainte)
        {
            // `AlreadyExists` : une contrainte d'unicité a mordu. Elle existe
            // pour qu'un rejeu ÉCHOUE au lieu d'encaisser ou de verser deux fois.
            // Le NOM de la contrainte reste dans le journal — il nomme une table
            // et une colonne, ce dont le support a besoin et qu'un appelant n'a
            // pas à connaître.
            Consigner(exception, context, $"Doublon refusé par la contrainte {contrainte}");

            throw new RpcException(new Status(
                StatusCode.AlreadyExists, "Duplicate rejected by a uniqueness constraint."));
        }
        catch (Exception exception)
        {
            // `Internal` PLUTÔT QUE `Unknown`, ET LA NUANCE EST UTILE.
            //
            // `Unknown` veut dire « on ne sait pas ce qui s'est passé » ; c'était
            // littéralement vrai avant ce fichier, et c'est ce qui rendait le
            // statut inexploitable. `Internal` dit « le serveur a un défaut » —
            // les deux comptent comme une panne pour le disjoncteur, mais seul le
            // second oriente vers la bonne enquête.
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

    /// <summary>
    /// Le nom de la contrainte d'unicité violée, ou nul.
    /// </summary>
    /// <remarks>
    /// EF Core enveloppe toujours l'erreur du pilote dans une
    /// <see cref="DbUpdateException"/>. Le <c>SqlState</c> — <c>23505</c> pour une
    /// violation d'unicité — n'est porté que par la <c>PostgresException</c> en
    /// dessous. Tester l'enveloppe ne rendrait jamais rien.
    ///
    /// Copie assumée de <c>ServiceExceptionMiddleware.Doublon</c> : les deux
    /// vivent dans le même assemblage, mais l'un est un middleware HTTP et
    /// l'autre un intercepteur gRPC. Si un troisième apparaît, c'est le moment de
    /// l'extraire — pas avant.
    /// </remarks>
    private static string? Doublon(DbUpdateException exception)
        => exception.InnerException is PostgresException { SqlState: "23505" } postgres
            ? postgres.ConstraintName ?? "(sans nom)"
            : null;
}
