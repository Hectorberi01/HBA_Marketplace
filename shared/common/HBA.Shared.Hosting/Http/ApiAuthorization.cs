// `ProducesResponseTypeAttribute` vit dans `Microsoft.AspNetCore.Mvc`, qui NE
// FIGURE PAS parmi les `using` implicites que le csproj de ce projet reproduit à la
// main — il reproduit ceux du SDK Web, et celui-là n'en fait pas partie.
using Microsoft.AspNetCore.Mvc;

namespace HBA.Shared.Hosting.Http;

/// <summary>Rôles et garde-fous d'autorisation de l'API monolithe.</summary>
public static class ApiAuthorization
{
    public const string AdminRole = "Admin";
    public const string ModeratorRole = "Moderator";
    public const string SellerRole = "Seller";

    // ── Rôles du cahier, semés mais PAS ENCORE EXIGÉS ───────────────────────
    public const string DriverRole = "Driver";
    public const string DispatcherRole = "Dispatcher";
    public const string FoodPartnerRole = "FoodPartner";

    /// <summary>Groupe réservé au BACK-OFFICE (Admin / Modérateur).</summary>
    public static RouteGroupBuilder MapAdminGroup(this IEndpointRouteBuilder app, string prefix)
        => app.MapGroup(prefix)
            .RequireAuthorization(policy => policy.RequireRole(AdminRole, ModeratorRole))
            .DocumenterLesRefus(avecRole: true);

    /// <summary>RESSERRE UNE ROUTE À L'ADMINISTRATEUR SEUL, DANS UN GROUPE PLUS LARGE.</summary>
    public static RouteHandlerBuilder RequireAdmin(this RouteHandlerBuilder route)
        => route.RequireAuthorization(policy => policy.RequireRole(AdminRole));

    /// <summary>GROUPE D'EXPLOITATION LOGISTIQUE (Admin / Dispatcher).</summary>
    public static RouteGroupBuilder MapOperationsGroup(this IEndpointRouteBuilder app, string prefix)
        => app.MapGroup(prefix)
            .RequireAuthorization(policy => policy.RequireRole(AdminRole, DispatcherRole))
            .DocumenterLesRefus(avecRole: true);

    /// <summary>
    /// GROUPE PARTENAIRE VENDEUR (Seller / Admin / Modérateur).
    ///
    /// POURQUOI ADMIN ET MODERATOR Y SONT, ALORS QUE LE GROUPE S'APPELLE
    ///    « SELLER ».
    ///
    /// Parce que les gardes d'appartenance qui protègent ces routes les laissent
    /// DÉJÀ passer : `DenyUnlessProductOwnerAsync` commence par
    /// `if (IsAdmin(user)) return null`. C'est délibéré — un modérateur doit
    /// pouvoir corriger la fiche d'un vendeur injoignable, ou retirer une image
    /// signalée sans attendre.
    ///
    /// Poser `RequireRole(Seller)` seul aurait donc fermé, au niveau du groupe, un
    /// chemin que le handler ouvre explicitement trois lignes plus bas. La route
    /// aurait rendu 403 avant même d'atteindre la garde, et le bandeau de cette
    /// garde serait devenu un mensonge que personne n'aurait relu.
    ///
    /// CE QUE CE GROUPE APPORTE PAR RAPPORT À `MapAuthenticatedGroup`.
    ///
    /// Il ferme la porte à l'ACHETEUR. Avant, tout compte authentifié entrait dans
    /// la surface vendeur ; seule la garde d'appartenance l'arrêtait, route par
    /// route, en rendant 404. Cela tenait tant que chaque route portait sa garde —
    /// c'est-à-dire tant que personne n'en ajoutait une en l'oubliant. Le rôle
    /// déplace la protection du cas particulier vers le groupe.
    ///
    /// CE QU'IL FAUT SAVOIR AVANT DE LE POSER SUR UN SERVICE.
    ///
    /// Le rôle `Seller` est greffé par `GrantSellerRoleHandler` à l'inscription
    /// vendeur, par événement. Les comptes semés AVANT que cette chaîne ne
    /// fonctionne ne l'ont pas : leurs événements ont été détruits et rien ne les
    /// rejouera. C'est `scripts/grant-partner-roles.sql` qui les rattrape, et il
    /// ne concerne qu'un jeu de développement.
    /// </summary>
    public static RouteGroupBuilder MapSellerGroup(this IEndpointRouteBuilder app, string prefix)
        => app.MapGroup(prefix)
            .RequireAuthorization(policy => policy.RequireRole(SellerRole, AdminRole, ModeratorRole))
            .DocumenterLesRefus(avecRole: true);

    /// <summary>
    /// Groupe exigeant simplement un utilisateur AUTHENTIFIÉ (sans rôle
    /// particulier).
    /// </summary>
    public static RouteGroupBuilder MapAuthenticatedGroup(this IEndpointRouteBuilder app, string prefix)
        => app.MapGroup(prefix)
            .RequireAuthorization()
            .DocumenterLesRefus(avecRole: false);

    /// <summary>
    /// Documente les réponses que TOUT groupe protégé peut rendre, quel que soit
    /// son handler.
    /// </summary>
    /// <param name="groupe">Le groupe à annoter.</param>
    /// <param name="avecRole">
    /// Vrai si le groupe exige un RÔLE en plus de l'authentification : il peut
    /// alors rendre 403.
    /// </param>
    private static RouteGroupBuilder DocumenterLesRefus(
        this RouteGroupBuilder groupe, bool avecRole)
    {
        // `WithMetadata` ET NON `Produces<T>()`, ET CE N'EST PAS UN DÉTAIL.
        groupe.WithMetadata(new ProducesResponseTypeAttribute(
            typeof(ApiEnvelope<object>), StatusCodes.Status401Unauthorized));

        if (avecRole)
        {
            groupe.WithMetadata(new ProducesResponseTypeAttribute(
                typeof(ApiEnvelope<object>), StatusCodes.Status403Forbidden));
        }

        return groupe;
    }
}
