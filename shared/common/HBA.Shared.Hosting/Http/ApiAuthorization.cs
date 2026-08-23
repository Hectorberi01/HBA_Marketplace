namespace HBA.Shared.Hosting.Http;

/// <summary>
/// Rôles et garde-fous d'autorisation de l'API monolithe.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// POURQUOI CE FICHIER EXISTE
///
/// Les 5 hôtes (API + 4 BFF) partagent la MÊME clé de signature JWT. Un jeton obtenu
/// par n'importe quel acheteur sur /mobile/auth/login est donc parfaitement valide
/// sur cette API.
///
/// La FallbackPolicy (voir Program.cs) ferme la porte aux ANONYMES. Elle ne dit rien
/// des utilisateurs authentifiés : sans rôle exigé, un acheteur lambda pouvait
/// changer le prix d'un vendeur, manipuler le stock, valider un dossier KYB ou
/// toucher au règlement.
///
/// D'où cette seconde couche : les groupes sensibles exigent un RÔLE.
///
/// RÈGLE À TENIR : tout nouveau groupe de l'API part de `MapAdminGroup` ou
/// `MapAuthenticatedGroup`. Jamais de `MapGroup` nu. Le jour où l'on en écrira un par
/// distraction, la FallbackPolicy le rendra au moins authentifié — mais elle ne le
/// rendra pas administrateur, et c'est bien là le piège qu'on vient de refermer.
/// ─────────────────────────────────────────────────────────────────────────────
/// </summary>
public static class ApiAuthorization
{
    public const string AdminRole = "Admin";
    public const string ModeratorRole = "Moderator";
    public const string SellerRole = "Seller";

    // ── Rôles du cahier, semés mais PAS ENCORE EXIGÉS ───────────────────────
    //
    // Ils existent en base (voir IdentityDataSeeder) et peuvent être attribués à
    // la main. Aucune route ne les réclame :
    //
    //   • DriverRole — l'inscription livreur ne l'attribue pas encore. Poser un
    //     RequireRole(DriverRole) sur /api/deliveries/mine verrouillerait tous les
    //     livreurs inscrits, y compris ceux qui roulent.
    //   • DispatcherRole — l'exploitation passe aujourd'hui par AdminRole.
    //   • FoodPartnerRole — HBA Food n'a pas encore de surface propre.
    //
    // Les déclarer ici plutôt qu'à l'usage évite qu'ils soient réécrits en chaîne
    // littérale dans le premier endpoint qui en aura besoin — c'est exactement
    // ainsi qu'un rôle finit orthographié de deux façons.
    public const string DriverRole = "Driver";
    public const string DispatcherRole = "Dispatcher";
    public const string FoodPartnerRole = "FoodPartner";

    /// <summary>
    /// Groupe réservé au BACK-OFFICE (Admin / Modérateur).
    ///
    /// À utiliser pour tout ce qui gouverne la plateforme : catalogue de référence,
    /// utilisateurs, rôles, KYB, règlement, fraude, campagnes, analytique.
    ///
    /// Ces opérations sont accessibles aux vendeurs et aux acheteurs par leurs BFF
    /// respectifs, avec les contrôles de propriété qui vont avec. L'API monolithe,
    /// elle, n'a aucune raison de leur être ouverte.
    /// </summary>
    public static RouteGroupBuilder MapAdminGroup(this IEndpointRouteBuilder app, string prefix)
        => app.MapGroup(prefix)
            .RequireAuthorization(policy => policy.RequireRole(AdminRole, ModeratorRole));

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// RESSERRE UNE ROUTE À L'ADMINISTRATEUR SEUL, DANS UN GROUPE PLUS LARGE.
    ///
    /// LES POLITIQUES S'ADDITIONNENT, ELLES NE SE REMPLACENT PAS.
    ///
    /// ASP.NET Core combine TOUTES les métadonnées d'autorisation d'un point de
    /// terminaison — celles du groupe et celles de la route — en une seule
    /// politique dont les exigences s'accumulent. Poser <c>RequireRole(Admin)</c>
    /// sur une route d'un groupe Admin/Moderator produit donc « Admin ET
    /// (Admin OU Moderator) », c'est-à-dire Admin.
    ///
    /// Corollaire à retenir : on ne peut pas ÉLARGIR une route depuis le groupe.
    /// Un <c>.RequireAuthorization()</c> nu posé sur une route d'un groupe
    /// administrateur n'ouvre rien aux non-administrateurs — il ajoute seulement
    /// une exigence d'authentification déjà satisfaite. C'est un appel sans effet,
    /// et c'est ainsi qu'on trouve des routes « /me » inaccessibles à leur
    /// utilisateur légitime : elles sont dans un groupe admin et personne ne l'a
    /// remarqué, parce que l'appel avait l'air de les ouvrir.
    ///
    /// Cette extension existe pour que le resserrement ait un nom qui le dit.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    public static RouteHandlerBuilder RequireAdmin(this RouteHandlerBuilder route)
        => route.RequireAuthorization(policy => policy.RequireRole(AdminRole));

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// GROUPE D'EXPLOITATION LOGISTIQUE (Admin / Dispatcher).
    ///
    /// CRÉÉ POUR REFERMER UNE FUITE DE DONNÉES PERSONNELLES.
    ///
    /// Les routes de courses de l'API — lecture, suivi, annulation, création —
    /// n'exigeaient qu'un compte authentifié. Le contrôle d'appartenance côté
    /// application ne s'applique qu'aux PARTENAIRES : il retourne « autorisé »
    /// dès que l'identifiant de partenaire est absent, ce qui est le cas ici.
    ///
    /// Avec un identifiant de course glané dans un ticket de support ou une
    /// capture d'écran, n'importe quel acheteur obtenait le nom et le téléphone
    /// du destinataire, les repères de son domicile, ceux du livreur, et sa
    /// POSITION GPS EN DIRECT. Il pouvait aussi annuler la course d'un partenaire
    /// payant : le colis restait chez le vendeur.
    ///
    /// Ce groupe n'inclut PAS Moderator, contrairement à MapAdminGroup. La
    /// modération arbitre des contenus et des litiges ; elle n'a aucune raison de
    /// suivre des livreurs à la trace.
    ///
    /// Dispatcher est semé mais pas encore attribué. Admin reste donc le seul
    /// accès effectif aujourd'hui — c'est voulu : ajouter le rôle ici avant qu'il
    /// existe évite qu'on écrive la chaîne « Dispatcher » à la main le jour où
    /// l'exploitation sera dotée.
    /// </summary>
    public static RouteGroupBuilder MapOperationsGroup(this IEndpointRouteBuilder app, string prefix)
        => app.MapGroup(prefix)
            .RequireAuthorization(policy => policy.RequireRole(AdminRole, DispatcherRole));

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
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
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    public static RouteGroupBuilder MapSellerGroup(this IEndpointRouteBuilder app, string prefix)
        => app.MapGroup(prefix)
            .RequireAuthorization(policy => policy.RequireRole(SellerRole, AdminRole, ModeratorRole));

    /// <summary>
    /// Groupe exigeant simplement un utilisateur AUTHENTIFIÉ (sans rôle particulier).
    ///
    /// Pour les ressources dont le propriétaire est vérifié DANS le handler (une
    /// commande n'est lisible que par son acheteur, un avis n'est modifiable que par
    /// son auteur…). L'authentification ne suffit jamais seule : elle dit QUI parle,
    /// pas ce qu'il a le droit de toucher.
    /// </summary>
    public static RouteGroupBuilder MapAuthenticatedGroup(this IEndpointRouteBuilder app, string prefix)
        => app.MapGroup(prefix).RequireAuthorization();
}
