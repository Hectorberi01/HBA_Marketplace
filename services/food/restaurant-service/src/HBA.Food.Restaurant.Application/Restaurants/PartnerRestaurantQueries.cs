using HBA.Food.Contracts;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;

namespace HBA.Food.Application.Restaurants;

/// <summary>
/// L'établissement du compte connecté, avec son rôle.
/// </summary>
/// <remarks>
/// PREND UN <c>UserId</c>, JAMAIS UN <c>RestaurantId</c>.
///
/// Même règle que partout dans cet espace : le compte vient du jeton signé.
/// Accepter un identifiant d'établissement en paramètre laisserait un caissier
/// lire le rôle et les permissions du personnel d'un autre restaurant.
/// </remarks>
public sealed record GetMyRestaurantQuery(Guid UserId) : IQuery<PartnerRestaurantView>;

internal sealed class PartnerRestaurantQueryHandler
    : IQueryHandler<GetMyRestaurantQuery, PartnerRestaurantView>
{
    private readonly IFoodModuleApi _food;

    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// DÉPEND DE <c>IFoodModuleApi</c>, ET CETTE FOIS C'EST LÉGITIME.
    ///
    /// J'avais dû retirer <c>ListStorefrontAsync</c> de cette interface : toute
    /// méthode AJOUTÉE oblige <c>FoodGrpcClient</c> à l'implémenter, donc à
    /// définir une RPC dans le proto.
    ///
    /// Ici, rien n'est ajouté : on CONSOMME <c>GetStaffMembershipAsync</c> et
    /// <c>GetRestaurantAsync</c>, qui existent déjà et sont déjà portées par les
    /// deux implémentations. Consommer ne coûte rien ; étendre coûtait un contrat
    /// réseau.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
    public PartnerRestaurantQueryHandler(IFoodModuleApi food) => _food = food;

    public async Task<Result<PartnerRestaurantView>> Handle(
        GetMyRestaurantQuery query, CancellationToken cancellationToken)
    {
        var membre = await _food.GetStaffMembershipAsync(query.UserId, cancellationToken);

        if (membre is null)
        {
            // Compte valide, mais qui ne travaille dans aucun établissement. Ce
            // n'est pas une erreur : c'est le cas d'un vendeur qui n'a que des
            // boutiques. L'appelant l'interprète comme « aucune activité Food ».
            return Result.Failure<PartnerRestaurantView>(
                Error.NotFound("Food.Membership.NotFound", "Aucun établissement pour ce compte."));
        }

        // LECTURE NON FILTRÉE SUR LA VISIBILITÉ, DÉLIBÉRÉMENT.
        //
        // La fiche PUBLIQUE refuse un établissement en brouillon ou suspendu.
        // C'est correct pour un client, et faux ici : un restaurateur dont le
        // dossier attend validation doit voir son établissement dans son
        // sélecteur d'activité — sinon l'application lui paraît vide et il
        // n'a aucun moyen de suivre sa demande.
        //
        // L'appartenance au personnel, vérifiée juste au-dessus, est ce qui
        // autorise cette lecture.
        var restaurant = await _food.GetRestaurantAsync(membre.RestaurantId, cancellationToken);

        if (restaurant is null)
        {
            // Appartenance orpheline : l'établissement a été supprimé sans que la
            // ligne de personnel le soit. Traité comme une absence plutôt que
            // comme une panne — l'application affichera « aucune activité Food ».
            return Result.Failure<PartnerRestaurantView>(
                Error.NotFound("Food.Restaurant.NotFound", "Établissement introuvable."));
        }

        return Result.Success(new PartnerRestaurantView(
            restaurant.Id,
            restaurant.Name,
            restaurant.Status,
            membre.Role,
            membre.IsFounder,
            membre.IsActive,
            membre.Permissions,
            restaurant.PayoutSellerId,
            restaurant.AcceptsOrdersNow,
            restaurant.BlockedReason));
    }
}
