using HBA.Gateway.Application.Abstractions.Services;
using HBA.Gateway.Application.Bff.Shared;

namespace HBA.Gateway.Application.Bff.Driver;

/// <summary>Écran « Revenus » du livreur (§15).</summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// CRITICITÉ (§23)
///
///   Delivery · compte  CRITIQUE   — sans driverId, aucun portefeuille à lire.
///   Financial · solde  CRITIQUE   — un écran « revenus » sans solde n'est rien.
///   Financial · lignes IMPORTANTE — le solde s'affiche sans le détail.
///
/// LE SOLDE EST CRITIQUE ICI, IMPORTANT SUR L'ACCUEIL.
///
/// Le même service, deux niveaux. C'est la démonstration du §23 : la criticité
/// est une décision d'ÉCRAN, pas une propriété du service. L'accueil reste utile
/// sans les gains ; l'écran « revenus », lui, n'a plus de raison d'exister.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed class GetDriverEarningsHandler
{
    public const string ScreenId = "driver.earnings";

    /// <summary>
    /// Mouvements demandés au service.
    /// </summary>
    /// <remarks>
    /// La route exige un <c>take</c> et n'offre pas de page. On demande donc un
    /// lot fixe et l'on pagine en mémoire — le service ne sait pas faire mieux
    /// aujourd'hui.
    /// </remarks>
    private const int TransactionsToFetch = 100;

    private readonly IDeliveryClient _delivery;
    private readonly IFinancialClient _financial;

    public GetDriverEarningsHandler(IDeliveryClient delivery, IFinancialClient financial)
    {
        _delivery = delivery;
        _financial = financial;
    }

    public async Task<BffEnvelope<DriverEarningsDto>> HandleAsync(
        PageRequest page, CancellationToken cancellationToken)
    {
        using var context = AggregationContext.Start(ScreenId);

        var account = context.Resolve(
            DependencyCriticality.Critical,
            "Delivery",
            await context.CallAsync("Delivery", () => _delivery.GetMyDriverAccountAsync(cancellationToken)))!;

        // Solde et mouvements ne dépendent que du driverId : ils partent ensemble.
        var walletTask = context.CallAsync(
            "Financial", () => _financial.GetDriverWalletAsync(account.DriverId, cancellationToken));

        var movementsTask = context.CallAsync(
            "Financial",
            () => _financial.ListDriverTransactionsAsync(
                account.DriverId, TransactionsToFetch, cancellationToken));

        await Task.WhenAll(walletTask, movementsTask);

        var wallet = context.Resolve(
            DependencyCriticality.Critical, "Financial", await walletTask)!;

        var movements = context.Resolve(
            DependencyCriticality.Important, "Financial", await movementsTask);

        var lines = (movements ?? [])
            .OrderByDescending(movement => movement.CreatedAtUtc)
            .Select(movement => new DriverMovementDto(
                movement.Id,
                movement.Direction,
                movement.Amount,
                movement.Currency,
                movement.Reason,
                movement.ReferenceType,
                movement.ReferenceId,
                movement.CreatedAtUtc))
            .ToList();

        var dto = new DriverEarningsDto(
            wallet.AvailableBalance,
            wallet.LifetimeEarned,
            wallet.Currency,
            PagedResult<DriverMovementDto>.Of(page.Apply(lines), page));

        return context.Complete(dto);
    }
}
