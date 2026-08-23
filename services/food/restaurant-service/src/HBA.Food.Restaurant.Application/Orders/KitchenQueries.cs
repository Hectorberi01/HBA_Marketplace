using HBA.Food.Contracts;
using HBA.Food.Domain.Orders;
using HBA.Food.Domain.Stations;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;

namespace HBA.Food.Application.Orders;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// L'ÉCRAN DE CUISINE (cahier des charges §13).
///
/// <paramref name="StationId"/> découpe l'écran : le grillardin ne voit que ses
/// grillades, le barman que ses boissons. Nul = tout le restaurant, ce qu'affiche
/// le passe.
///
/// UN TICKET FILTRÉ RESTE UN TICKET ENTIER.
///
/// Filtrer par poste retire les LIGNES des autres postes, jamais la commande.
/// Le grillardin doit voir qu'il fait partie d'un ensemble : sans cela, il pose
/// ses deux burgers sur le passe, considère son travail fini, et personne ne
/// s'étonne que le sac attende ses boissons.
///
/// Chaque ticket porte donc <c>OtherStationsPending</c> — combien de lignes
/// travaillent ailleurs.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed record GetKitchenBoardQuery(Guid RestaurantId, Guid? StationId) : IQuery<KitchenBoardView>;

/// <summary>
/// La file des commandes en attente de décision (§18 : <c>GET /restaurants/{id}/orders</c>).
///
/// SANS ELLE, L'ACCEPTATION SERAIT UN BOUTON SANS LISTE — le défaut exact qui
/// avait bloqué les dossiers de livreurs pendant des semaines, et celui que la
/// file de validation des restaurants a déjà corrigé.
/// </summary>
public sealed record ListPendingFoodOrdersQuery(Guid RestaurantId) : IQuery<IReadOnlyList<FoodOrderView>>;

public sealed record GetFoodOrderQuery(Guid RestaurantId, Guid FoodOrderId) : IQuery<FoodOrderView>;

internal sealed class KitchenQueryHandler
    : IQueryHandler<GetKitchenBoardQuery, KitchenBoardView>,
      IQueryHandler<ListPendingFoodOrdersQuery, IReadOnlyList<FoodOrderView>>,
      IQueryHandler<GetFoodOrderQuery, FoodOrderView>
{
    private readonly IFoodOrderRepository _orders;
    private readonly IPreparationStationRepository _stations;

    public KitchenQueryHandler(IFoodOrderRepository orders, IPreparationStationRepository stations)
    {
        _orders = orders;
        _stations = stations;
    }

    public async Task<Result<KitchenBoardView>> Handle(
        GetKitchenBoardQuery query, CancellationToken cancellationToken)
    {
        var commandes = await _orders.ListActiveAsync(query.RestaurantId, cancellationToken);
        var postes = await _stations.ListByRestaurantAsync(query.RestaurantId, cancellationToken);

        var tickets = new List<KitchenTicketView>();

        foreach (var commande in commandes)
        {
            // Le ticket n'existe qu'après acceptation : une commande en attente de
            // décision n'a rien à faire sur l'écran de cuisine, et l'y afficher
            // ferait commencer des plats que le restaurant n'a pas encore acceptés.
            if (commande.Status is FoodOrderStatus.PendingRestaurantAcceptance
                or FoodOrderStatus.Rejected or FoodOrderStatus.Cancelled)
            {
                continue;
            }

            var lignes = commande.Items.AsEnumerable();

            if (query.StationId is { } poste)
            {
                lignes = lignes.Where(i => i.PreparationStationId == poste);
            }

            var retenues = lignes.ToList();

            // Un poste qui n'a rien sur cette commande ne l'affiche pas du tout.
            if (retenues.Count == 0)
            {
                continue;
            }

            tickets.Add(new KitchenTicketView(
                commande.Id.Value,
                commande.OrderId,
                commande.KitchenStatus.ToString(),
                commande.Priority,
                commande.EstimatedPreparationMinutes,
                commande.ReceivedAtUtc,
                commande.AcceptedAtUtc,
                commande.StartedAtUtc,
                commande.ReadyAtUtc,
                commande.CustomerNote,

                // CE QUI TRAVAILLE AILLEURS. Sans ce nombre, le grillardin croit
                // avoir fini la commande alors que le bar n'a pas commencé.
                commande.Items.Count(i => !retenues.Contains(i) && i.Status != KitchenItemStatus.Ready),

                retenues
                    .Select(i => new KitchenTicketItemView(
                        i.Id,
                        i.NameSnapshot,
                        i.Quantity,
                        i.Notes,
                        i.Status.ToString(),
                        i.PreparationStationId,
                        i.PreparationMinutes,
                        i.Options.Select(o => $"{o.GroupName} : {o.OptionName}").ToList()))
                    .ToList()));
        }

        IReadOnlyList<KitchenTicketView> ordonnes = tickets
            // PRIORITÉ D'ABORD, ANCIENNETÉ ENSUITE. L'inverse rendrait la
            // priorité décorative : une commande remontée à la main resterait
            // derrière les vingt autres arrivées avant elle.
            .OrderByDescending(t => t.Priority)
            .ThenBy(t => t.ReceivedAtUtc)
            .ToList();

        IReadOnlyList<PreparationStationView> vuesPostes = postes
            .OrderBy(s => s.DisplayOrder)
            .Select(s => new PreparationStationView(s.Id.Value, s.Name, s.Code, s.IsActive, s.DisplayOrder))
            .ToList();

        return Result.Success(new KitchenBoardView(query.RestaurantId, query.StationId, vuesPostes, ordonnes));
    }

    public async Task<Result<IReadOnlyList<FoodOrderView>>> Handle(
        ListPendingFoodOrdersQuery query, CancellationToken cancellationToken)
    {
        var commandes = await _orders.ListByStatusAsync(
            query.RestaurantId, FoodOrderStatus.PendingRestaurantAcceptance, 100, cancellationToken);

        // Les plus anciennes d'abord : c'est le temps d'acceptation que le cahier
        // mesure (§21), et une commande doublée indéfiniment est un client perdu.
        IReadOnlyList<FoodOrderView> vues = commandes
            .OrderBy(o => o.ReceivedAtUtc)
            .Select(Project)
            .ToList();

        return Result.Success(vues);
    }

    public async Task<Result<FoodOrderView>> Handle(GetFoodOrderQuery query, CancellationToken cancellationToken)
    {
        var commande = await _orders.GetByIdAsync(new FoodOrderId(query.FoodOrderId), cancellationToken);

        // Même réponse pour « inconnue » et « pas la vôtre » : distinguer les deux
        // dirait à qui teste des identifiants lesquels existent.
        if (commande is null || commande.RestaurantId != query.RestaurantId)
        {
            return Result.Failure<FoodOrderView>(
                Error.NotFound("food.order.not_found", "Commande introuvable."));
        }

        return Result.Success(Project(commande));
    }

    private static FoodOrderView Project(FoodOrder commande)
        => new(
            commande.Id.Value,
            commande.OrderId,
            commande.RestaurantId,
            commande.Status.ToString(),
            commande.KitchenStatus.ToString(),
            commande.Total,
            commande.Items.FirstOrDefault()?.Currency ?? "XOF",
            commande.CustomerNote,
            commande.EstimatedPreparationMinutes,
            commande.Priority,
            commande.ReceivedAtUtc,
            commande.AcceptedAtUtc,
            commande.StartedAtUtc,
            commande.ReadyAtUtc,
            commande.PickedUpAtUtc,
            commande.Rejection is null
                ? null
                : new FoodOrderRejectionView(
                    commande.Rejection.Reason.ToString(),
                    commande.Rejection.Comment,
                    commande.Rejection.RejectedAtUtc),
            commande.Items
                .Select(i => new FoodOrderItemView(
                    i.Id,
                    i.MenuItemId,
                    i.NameSnapshot,
                    i.UnitPrice,
                    i.Quantity,
                    i.LineTotal,
                    i.Notes,
                    i.Status.ToString(),
                    i.PreparationStationId,
                    i.Options
                        .Select(o => new FoodOrderItemOptionView(o.GroupName, o.OptionName, o.PriceDelta))
                        .ToList()))
                .ToList());
}
