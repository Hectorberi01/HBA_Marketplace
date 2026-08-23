using HBA.Food.Application.Abstractions;
using HBA.Food.Contracts;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;

namespace HBA.Food.Application.Restaurants;

/// <summary>
/// La vitrine publique : ce qu'un client voit avant d'avoir choisi un restaurant.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// CE QUI MANQUAIT POUR QU'UNE APPLICATION CLIENTE PUISSE EXISTER.
///
/// Le service ne savait rendre qu'une carte — <c>/restaurants/{id}/menu</c> — ou
/// la file de validation. Il fallait donc DÉJÀ connaître l'identifiant d'un
/// établissement pour en apprendre quoi que ce soit. Aucune première page n'était
/// constructible : le BFF client HBA Food n'avait littéralement aucun amont.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed record ListStorefrontQuery(int Page = 1, int PageSize = 20)
    : IQuery<IReadOnlyList<RestaurantCardView>>;

/// <summary>
/// La fiche d'un établissement, telle qu'un client la voit.
/// </summary>
/// <remarks>
/// DISTINCTE DE LA LECTURE INTERNE, ET LE FILTRE EST TOUT L'ÉCART.
///
/// <c>IFoodModuleApi.GetRestaurantAsync</c> rend N'IMPORTE QUEL établissement, y
/// compris un dossier en brouillon ou suspendu : c'est ce qu'il faut à l'espace
/// du restaurateur et à la file de validation. L'exposer tel quel sur une route
/// anonyme laisserait consulter un établissement écarté de la plateforme — et
/// commander chez lui si un autre défaut s'y ajoutait.
///
/// Ici, un établissement non visible publiquement est traité comme INEXISTANT.
/// Pas « interdit » : « introuvable ». Un 403 confirmerait à qui essaie que
/// l'identifiant correspond à quelque chose.
/// </remarks>
public sealed record GetPublicRestaurantQuery(Guid RestaurantId) : IQuery<RestaurantSummary>;

internal sealed class StorefrontQueryHandler
    : IQueryHandler<ListStorefrontQuery, IReadOnlyList<RestaurantCardView>>,
      IQueryHandler<GetPublicRestaurantQuery, RestaurantSummary>
{
    private const int MaxPageSize = 50;
    private const int DefaultPageSize = 20;

    private readonly IStorefrontReader _storefront;

    /// <remarks>
    /// DÉPEND DE <c>IStorefrontReader</c>, PAS DE <c>IFoodModuleApi</c>.
    ///
    /// Ma première version passait par le contrat inter-modules : elle a cassé la
    /// compilation de <c>FoodGrpcClient</c>, qui l'implémente aussi. Une vitrine
    /// n'a rien à faire dans un contrat destiné à traverser le réseau entre
    /// modules — cf. <c>IStorefrontReader</c>.
    /// </remarks>
    public StorefrontQueryHandler(IStorefrontReader storefront) => _storefront = storefront;

    public async Task<Result<IReadOnlyList<RestaurantCardView>>> Handle(
        ListStorefrontQuery query, CancellationToken cancellationToken)
    {
        // Bornes appliquées ici ET dans le module : une limite qui n'existe qu'à
        // un seul niveau se contourne en entrant par une autre porte.
        var pageSize = query.PageSize is < 1 or > MaxPageSize ? DefaultPageSize : query.PageSize;
        var page = query.Page < 1 ? 1 : query.Page;

        var cartes = await _storefront.ListAsync(
            (page - 1) * pageSize, pageSize, cancellationToken);

        return Result.Success(cartes);
    }

    public async Task<Result<RestaurantSummary>> Handle(
        GetPublicRestaurantQuery query, CancellationToken cancellationToken)
    {
        // Le lecteur rend déjà `null` dans les DEUX cas — inexistant et hors
        // vitrine. Les distinguer permettrait d'énumérer les établissements
        // suspendus en comparant deux codes de réponse.
        var restaurant = await _storefront.GetPublicAsync(query.RestaurantId, cancellationToken);

        if (restaurant is null)
        {
            return Result.Failure<RestaurantSummary>(
                Error.NotFound("Food.Restaurant.NotFound", "Établissement introuvable."));
        }

        return Result.Success(restaurant);
    }
}
