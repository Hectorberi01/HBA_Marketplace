using HBA.Food.Contracts;
using HBA.Food.Domain.Restaurants;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;

namespace HBA.Food.Application.Restaurants;

/// <summary>
/// La file des dossiers en attente de validation.
///
/// SANS ELLE, LA VALIDATION SERAIT UN BOUTON SANS LISTE.
///
/// Le seul à connaître l'identifiant d'un établissement soumis est le
/// restaurateur qui l'a soumis. L'exploitation n'aurait aucun moyen de savoir qui
/// attend — et les dossiers dormiraient jusqu'à ce que quelqu'un se plaigne.
/// C'est le défaut exact qui avait bloqué les livreurs pendant des semaines.
/// </summary>
public sealed record ListPendingRestaurantsQuery(int Take = 100) : IQuery<IReadOnlyList<RestaurantSummary>>;

internal sealed class RestaurantQueryHandler
    : IQueryHandler<ListPendingRestaurantsQuery, IReadOnlyList<RestaurantSummary>>
{
    private const int MaxTake = 200;

    private readonly IRestaurantRepository _restaurants;

    public RestaurantQueryHandler(IRestaurantRepository restaurants) => _restaurants = restaurants;

    public async Task<Result<IReadOnlyList<RestaurantSummary>>> Handle(
        ListPendingRestaurantsQuery query, CancellationToken cancellationToken)
    {
        var dossiers = await _restaurants.ListByStatusAsync(
            RestaurantStatus.PendingApproval, Math.Clamp(query.Take, 1, MaxTake), cancellationToken);

        // L'heure est lue UNE fois pour toute la projection : la relire par
        // établissement ferait qu'un même écran répondrait à deux instants.
        var maintenant = DateTime.UtcNow;

        IReadOnlyList<RestaurantSummary> vues = dossiers.Select(r => Project(r, maintenant)).ToList();
        return Result.Success(vues);
    }

    private static RestaurantSummary Project(Restaurant r, DateTime nowUtc)
    {
        // SURCHARGE À UN SEUL PARAMÈTRE, DÉLIBÉRÉMENT.
        //
        // Cette file ne contient que des dossiers EN ATTENTE DE VALIDATION :
        // CanAcceptOrders y répond « NotInService » quoi qu'il arrive, et
        // interroger la carte ne changerait pas la réponse — ce serait une requête
        // par dossier pour rien.
        //
        // Si cette projection servait un jour à lister des établissements ACTIFS,
        // il faudrait la surcharge à deux paramètres, sous peine d'annoncer
        // « ouvert » un restaurant dont tout est épuisé. Voir FoodModuleApi.
        var blocage = r.CanAcceptOrders(nowUtc);

        return new RestaurantSummary(
            r.Id.Value,
            r.OwnerUserId,
            r.Name,
            r.Description,
            r.LogoMediaId,
            r.CoverMediaId,
            r.LegacyLogoUrl,
            r.Phone,
            r.Status.ToString(),
            blocage == OrderingBlockedReason.None,
            blocage.ToString(),
            r.PreparationMinutes,
            r.AcceptanceMode.ToString(),
            r.MinimumOrderAmount,

            // CHARGE NON CALCULÉE, ET C'EST CORRECT ICI.
            //
            // Cette file ne contient que des dossiers EN ATTENTE DE VALIDATION :
            // ils n'ont aucune commande en cours, et interroger le nombre de
            // commandes actives serait une requête par dossier pour un zéro connu
            // d'avance. Si cette projection servait un jour des établissements
            // ACTIFS, il faudrait la calculer — comme le fait FoodModuleApi.
            nameof(KitchenLoadLevel.Normal),
            0,

            // Un dossier en attente n'a pas de vitrine : le motif d'une fermeture
            // exceptionnelle n'y sert à rien.
            null,

            r.FulfillmentLocationId,
            r.PayoutSellerId,
            r.ServiceHours
                // Lundi en tête : DayOfWeek vaut Sunday = 0.
                .OrderBy(h => ((int)h.Day + 6) % 7)
                .ThenBy(h => h.OpensAt)
                .Select(h => new ServiceHoursSummary(
                    h.Day.ToString(),
                    h.OpensAt.ToString("HH\\:mm", System.Globalization.CultureInfo.InvariantCulture),
                    h.ClosesAt.ToString("HH\\:mm", System.Globalization.CultureInfo.InvariantCulture)))
                .ToList(),
            r.IsPubliclyVisible);
    }
}
