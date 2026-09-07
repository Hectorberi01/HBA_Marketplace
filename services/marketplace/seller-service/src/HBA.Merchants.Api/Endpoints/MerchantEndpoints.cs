using System.Security.Claims;
using HBA.Merchants.Application.Sellers.Commands.ActivateSeller;
using HBA.Merchants.Application.Sellers.Commands.AddKybDocument;
using HBA.Merchants.Application.Sellers.Commands.ApproveKyb;
using HBA.Merchants.Application.Sellers.Commands.ApproveSellerReactivation;
using HBA.Merchants.Application.Sellers.Commands.DeleteSeller;
using HBA.Merchants.Application.Sellers.Commands.RegisterSeller;
using HBA.Merchants.Application.Sellers.Commands.RejectKyb;
using HBA.Merchants.Application.Sellers.Commands.RemoveKybDocument;
using HBA.Merchants.Application.Sellers.Commands.SubmitKyb;
using HBA.Merchants.Application.Sellers.Commands.RequestSellerClosure;
using HBA.Merchants.Application.Sellers.Commands.RequestSellerReactivation;
using HBA.Merchants.Application.Sellers.Commands.SetPayoutAccount;
using HBA.Merchants.Application.Sellers.Commands.SuspendSeller;
using HBA.Merchants.Application.Sellers.Commands.UpdateSellerMetadata;
using HBA.Merchants.Application.Sellers.Commands.UpdateSellerProfile;
using HBA.Merchants.Application.Sellers.Queries.GetSeller;
using HBA.Merchants.Application.Sellers.Queries.GetSellerByUser;
using HBA.Merchants.Application.Members;
using HBA.Merchants.Application.Sellers.Queries.ListSellers;
using HBA.Merchants.Application.Stores;
using HBA.Merchants.Contracts;
using HBA.Merchants.Domain.Members;
using HBA.Merchants.Domain.Sellers;
using HBA.Shared.Domain.Results;
using HBA.Shared.Hosting.Http;
using MediatR;

namespace HBA.Merchants.Api.Endpoints;

/// <summary>Surface HTTP initiale du service Merchant.</summary>
public static class MerchantEndpoints
{
    public static IEndpointRouteBuilder MapMerchantEndpoints(this IEndpointRouteBuilder app)
    {
        // TOUT CE QUI SUIT TENAIT DANS UN SEUL GROUPE « AUTHENTIFIÉ ».
        //
        // `MapAuthenticatedGroup` n'exige aucun rôle, et aucun handler n'ouvrait
        // le jeton. Un acheteur, avec le jeton que lui rend sa propre application
        // mobile — même clé de signature sur les cinq hôtes — et un sellerId
        // ramassé dans une fiche produit, obtenait :
        //
        //   POST /{id}/kyb/approve  → il validait SON PROPRE dossier KYB et
        //                             devenait vendeur sans qu'aucun humain n'ait
        //                             regardé une pièce ;
        //   POST /{id}/suspend      → il coupait la vente d'un concurrent, qui ne
        //                             pouvait pas se relever lui-même ;
        //   DELETE /{id}            → il effaçait un vendeur, ses boutiques et ses
        //                             pièces d'identité.
        //
        // La réparation est en deux temps : la gouvernance descend dans un groupe
        // administrateur, et ce qui reste au vendeur prouve la propriété dans le
        // handler — voir `DenyUnlessOwnSellerAsync`.

        // LA RACINE PASSE DE `/api/merchants` À `/api/v1/merchants` (D15).

        // L'INSCRIPTION RESTE OUVERTE À TOUT COMPTE AUTHENTIFIÉ.
        var inscription = app.MapAuthenticatedGroup("/api/v1/merchants").WithTags("Merchant · Sellers");
        inscription.MapGet("/me", GetMySellerAsync);
        inscription.MapPost("/", RegisterSellerAsync).AllowIdempotency();

        // LE RESTE DE LA SURFACE VENDEUR EXIGE LE RÔLE (§22).
        //
        // Avant, un jeton suffisait : n'importe quel ACHETEUR entrait, et seule la
        // garde d'appartenance — route par route — l'arrêtait. Cela tenait tant que
        // CHAQUE route portait sa garde, c'est-à-dire tant que personne n'en
        // ajoutait une en l'oubliant. La protection était une discipline, pas une
        // barrière.
        //
        // `MapSellerGroup` admet `Seller`, `Admin` et `Moderator` — les deux
        // derniers parce que `DenyUnlessOwnSellerAsync` les laisse déjà passer
        // délibérément, pour qu'un modérateur puisse corriger le dossier d'un
        // vendeur injoignable.
        var sellers = app.MapSellerGroup("/api/v1/merchants").WithTags("Merchant · Sellers");

        sellers.MapGet("/{sellerId:guid}", GetSellerAsync);
        sellers.MapPut("/{sellerId:guid}/profile", UpdateProfileAsync);
        sellers.MapPut("/{sellerId:guid}/metadata", UpdateMetadataAsync);
        sellers.MapPut("/{sellerId:guid}/payout-account", SetPayoutAsync);
        sellers.MapPost("/{sellerId:guid}/kyb/documents", AddKybDocumentAsync).AllowIdempotency();
        sellers.MapDelete("/{sellerId:guid}/kyb/documents/{documentId:guid}", RemoveKybDocumentAsync);

        // LA SOUMISSION DU DOSSIER (§10.3 : POST /kyc/submit) — ELLE MANQUAIT.
        sellers.MapPost("/{sellerId:guid}/kyb/submit", SubmitKybAsync);

        // Demander n'est pas décider.
        sellers.MapPost("/{sellerId:guid}/close", RequestClosureAsync);
        sellers.MapPost("/{sellerId:guid}/reactivation", RequestReactivationAsync);

        // ─────────────────────────── Gouvernance ─────────────────────────────
        var governance = app.MapAdminGroup("/api/v1/merchants").WithTags("Merchant · Gouvernance");
        governance.MapGet("/", ListSellersAsync);

        // L'ONBOARDING PAR UN ADMINISTRATEUR — ANNONCÉ PAR LE CONTRAT, JAMAIS
        // MONTÉ.
        governance.MapPost("/inscriptions", RegisterSellerForUserAsync).AllowIdempotency();
        governance.MapPost("/{sellerId:guid}/kyb/approve", ApproveKybAsync);
        governance.MapPost("/{sellerId:guid}/kyb/reject", RejectKybAsync);
        governance.MapPost("/{sellerId:guid}/activate", ActivateSellerAsync);
        governance.MapPost("/{sellerId:guid}/suspend", SuspendSellerAsync);
        governance.MapPost("/{sellerId:guid}/lift-suspension", LiftSuspensionAsync);
        governance.MapPost("/{sellerId:guid}/reactivation/approve", ApproveReactivationAsync);
        governance.MapDelete("/{sellerId:guid}", DeleteSellerAsync);

        var stores = app.MapSellerGroup("/api/v1/merchants/{sellerId:guid}/stores").WithTags("Merchant · Stores");
        stores.MapGet("/", ListStoresAsync);
        stores.MapPost("/", CreateStoreAsync).AllowIdempotency();
        stores.MapGet("/{storeId:guid}", GetStoreAsync);
        stores.MapPut("/{storeId:guid}/profile", UpdateStoreProfileAsync);
        stores.MapPut("/{storeId:guid}/contact", UpdateStoreContactAsync);
        stores.MapPut("/{storeId:guid}/location", AttachStoreLocationAsync);
        stores.MapPut("/{storeId:guid}/opening-hours", SetOpeningHoursAsync);
        stores.MapPost("/{storeId:guid}/open", OpenStoreAsync);
        stores.MapPost("/{storeId:guid}/close", CloseStoreAsync);

        // SUSPENDRE UNE BOUTIQUE EST UNE SANCTION, PAS UNE FERMETURE.
        var storeGovernance = app.MapAdminGroup("/api/v1/merchants/{sellerId:guid}/stores")
            .WithTags("Merchant · Stores · Gouvernance");
        storeGovernance.MapPost("/{storeId:guid}/suspend", SuspendStoreAsync);
        storeGovernance.MapPost("/{storeId:guid}/lift-suspension", LiftStoreSuspensionAsync);

        // L'ÉQUIPE.
        //
        // PAS DE `DenyUnlessOwnSellerAsync` SUR CES ROUTES, ET CE N'EST PAS UN
        //    OUBLI.
        //
        // Leur garde est la RÉSOLUTION D'APPARTENANCE, dans la couche Application :
        // « faites-vous partie de cette équipe, et avec quels droits ». Poser en
        // plus la garde de propriété donnerait deux sources de vérité pour la même
        // décision — et le jour où l'une serait oubliée sur une route, c'est la
        // plus permissive qui l'emporterait.
        //
        // Conséquence assumée : un ADMINISTRATEUR de la plateforme n'a pas
        // d'appartenance, donc ne compose pas l'équipe d'un commerçant. Ce n'est
        // pas un acte de gouvernance, et les routes qui le sont — suspension,
        // clôture — existent déjà.
        //
        // `MapSellerGroup` reste en première barrière : un acheteur est refusé
        // sans qu'on touche la base.
        var members = app.MapSellerGroup("/api/v1/merchants/{sellerId:guid}/members")
            .WithTags("Merchant · Équipe");

        members.MapGet("/", ListMembersAsync);
        members.MapGet("/invitations", ListInvitationsAsync);
        members.MapPost("/invitations", InviteMemberAsync).AllowIdempotency();
        members.MapPost("/invitations/{invitationId:guid}/resend", ResendInvitationAsync);
        members.MapDelete("/invitations/{invitationId:guid}", RevokeInvitationAsync);
        members.MapGet("/{memberId:guid}", GetMemberAsync);
        members.MapPut("/{memberId:guid}/roles", SetMemberRolesAsync);
        members.MapPut("/{memberId:guid}/stores/{storeId:guid}", AssignMemberStoreAsync);
        members.MapDelete("/{memberId:guid}/stores/{storeId:guid}", UnassignMemberStoreAsync);
        members.MapPost("/{memberId:guid}/suspend", SuspendMemberAsync);
        members.MapPost("/{memberId:guid}/activate", ReactivateMemberAsync);
        members.MapDelete("/{memberId:guid}", RevokeMemberAsync);

        // LE TRANSFERT DE PROPRIÉTÉ — `OWNERSHIP_TRANSFER` NE GARDAIT RIEN
        //    (ISSUE-040).
        //
        // La permission était déclarée, critique, réservée au propriétaire, et
        // aucune route ne l'exigeait. Trois gardes du domaine renvoyaient pourtant
        // l'utilisateur vers ce geste : « le rôle de propriétaire se transfère »,
        // « il ne s'attribue que par un transfert de propriété », « transférez la
        // propriété d'abord ». Aucune des trois ne désignait quoi que ce soit.
        //
        // `POST` ET NON `PUT`, PARCE QUE CE N'EST PAS UNE PROPRIÉTÉ QU'ON ÉCRIT.
        //
        // C'est une opération, avec ses propres refus et son propre événement. Un
        // `PUT /{memberId}/roles` étendu aurait été plus court, et aurait rouvert
        // exactement ce que `EnsureCanAssign` referme : le rôle OWNER attribuable
        // comme un autre.
        //
        // ET LE STEP-UP EST PORTÉ ICI, PAS HÉRITÉ.
        //
        // Le groupe `members` ne passe DÉLIBÉRÉMENT pas par
        // `DenyUnlessOwnSellerAsync` — la garde d'appartenance y est dans le
        // domaine. Or c'est cette méthode qui applique la réauthentification pour
        // les permissions critiques. Sans la ligne ci-dessous, le geste le plus
        // irréversible du module serait le SEUL geste critique sans step-up.
        members.MapPost("/{memberId:guid}/ownership", TransferOwnershipAsync);

        // LE DÉPART VOLONTAIRE — AUCUN `memberId`, ET C'EST LE POINT.
        members.MapDelete("/me", LeaveSellerAsync);

        var memberRoles = app.MapSellerGroup("/api/v1/merchants/{sellerId:guid}/roles")
            .WithTags("Merchant · Rôles");

        memberRoles.MapGet("/", ListSellerRolesAsync);

        // LES RÔLES TAILLÉS PAR LE VENDEUR (§18, lot A3).
        memberRoles.MapPost("/", CreateSellerRoleAsync).AllowIdempotency();
        memberRoles.MapPatch("/{roleId:guid}", UpdateSellerRoleAsync);
        memberRoles.MapDelete("/{roleId:guid}", DeleteSellerRoleAsync);

        // LE JOURNAL D'ÉQUIPE — LA SEULE ROUTE QUE `AUDIT_VIEW` GARDE.
        members.MapGet("/audit", ListAuditEntriesAsync);

        // Le catalogue des permissions ne dépend d'aucun vendeur.
        var permissions = app.MapSellerGroup("/api/v1/merchants/permissions")
            .WithTags("Merchant · Rôles");

        permissions.MapGet("/", ListPermissionsAsync);

        // L'ACCEPTATION — LA SEULE ROUTE D'ÉQUIPE HORS DU GROUPE VENDEUR.
        var acceptation = app.MapAuthenticatedGroup("/api/v1/merchants/invitations")
            .WithTags("Merchant · Équipe");

        acceptation.MapPost("/accept", AcceptInvitationAsync);

        return app;
    }

    // LE JETON DÉSIGNE UN UTILISATEUR, L'URL DÉSIGNE UN VENDEUR.
    /// <param name="storeId">La boutique visée, quand la route en nomme une.</param>
    private static async Task<IResult?> DenyUnlessOwnSellerAsync(
        ClaimsPrincipal user, Guid sellerId, MemberAccessResolver acces,
        MerchantPermission capacite, CancellationToken ct, Guid? storeId = null)
    {
        if (CurrentUserId(user) is not { } userId)
        {
            return ApiResults.Unauthorized();
        }

        if (user.IsInRole(ApiAuthorization.AdminRole))
        {
            return null;
        }

        var acteur = await acces.ResolveAsync(sellerId, userId, ct);

        // `Results.Forbid()` RENDAIT UN 403 AU CORPS VIDE.
        if (acteur.IsFailure)
        {
            return ApiResults.Failure(
                ErrorCodes.Forbidden,
                "Ce dossier vendeur n'est pas le vôtre.",
                StatusCodes.Status403Forbidden,
                [new ApiErrorDetail { Field = "reason", Message = acteur.Error.Code }]);
        }

        // `HasInStore` QUAND LA ROUTE NOMME UNE BOUTIQUE, `Has` SINON (lot F).
        var autorise = storeId is { } boutique
            ? acteur.Value.HasInStore(boutique, capacite)
            : acteur.Value.Has(capacite);

        if (!autorise)
        {
            return ApiResults.MissingCapability(capacite.ToCode());
        }

        // LE STEP-UP DU §37 — DEUX ROUTES DE CE GROUPE SONT CONCERNÉES.
        //
        // `PUT /payout-account` (PAYOUT_CONFIGURE) et `POST /close` (SELLER_CLOSE).
        // Repointer un compte de versement détourne les virements à venir ; fermer
        // le dossier coupe la boutique. Les deux sont exactement ce qu'on fait d'un
        // poste laissé ouvert au marché — la permission dit que le rôle a le droit,
        // pas que le titulaire est devant l'écran.
        //
        // ICI LE NIVEAU DE RISQUE EST LU DANS LE DOMAINE, PAS DANS LE CONTRAT.
        //
        // Les quatre autres services passent par `MerchantCapabilities.Critical`,
        // une recopie tenue par un test — ils n'ont pas accès au catalogue. Ce
        // service-ci L'A : s'en servir retire une recopie de plus, et fait de la
        // table du domaine la seule source du niveau de risque là où c'est possible.
        //
        // APRÈS LA CAPACITÉ, JAMAIS AVANT.
        //
        // Un membre qui n'a pas `PAYOUT_CONFIGURE` doit lire « votre rôle ne
        // l'autorise pas ». L'ordre inverse l'enverrait ressaisir son mot de passe
        // pour se voir refuser ensuite — deux écrans pour une seule mauvaise
        // nouvelle.
        //
        // ET L'ADMINISTRATION EST DÉJÀ SORTIE PLUS HAUT, DÉLIBÉRÉMENT.
        //
        // Un modérateur n'a pas d'appartenance ; le soumettre au step-up d'un rôle
        // vendeur n'aurait aucun sens. Sa propre traçabilité est ailleurs.
        if (MerchantPermissions.Critical.Contains(capacite) && !user.HasRecentAuthentication())
        {
            return ApiResults.ReauthenticationRequired(capacite.ToCode());
        }

        return null;
    }

    /// <summary>LA FILE D'ADMINISTRATION — PAGINÉE, FILTRABLE, ET ALLÉGÉE.</summary>
    private static async Task<IResult> ListSellersAsync(
        int? page,
        int? pageSize,
        string? search,
        string? kybStatus,
        string? status,
        ISender sender,
        CancellationToken ct)
        => (await sender.Send(
                new ListSellersQuery(page ?? 1, pageSize ?? 20, search, kybStatus, status), ct))
            .Match(resultat => ApiResults.Page(resultat));

    private static async Task<IResult> GetMySellerAsync(ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => CurrentUserId(user) is not { } userId
            ? ApiResults.Unauthorized()
            : (await sender.Send(new GetSellerByUserQuery(userId), ct)).Match(seller => ApiResults.Ok(seller));

    private static async Task<IResult> RegisterSellerAsync(
        ClaimsPrincipal user, RegisterSellerRequest request, ISender sender, CancellationToken ct)
        => CurrentUserId(user) is not { } userId
            ? ApiResults.Unauthorized()
            : (await sender.Send(new RegisterSellerCommand(
                userId, request.ShopName, request.CommissionRate ?? 0.10m, request.Metadata), ct))
                .Match(id => ApiResults.Created(new { id }, $"/api/v1/merchants/{id}"));

    /// <summary>Inscrit un vendeur POUR UN AUTRE COMPTE (Admin).</summary>
    private static async Task<IResult> RegisterSellerForUserAsync(
        RegisterSellerForUserRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new RegisterSellerCommand(
            request.UserId, request.ShopName, request.CommissionRate ?? 0.10m, request.Metadata), ct))
            .Match(id => ApiResults.Created(new { id }, $"/api/v1/merchants/{id}"));

    private static async Task<IResult> GetSellerAsync(
        Guid sellerId, ClaimsPrincipal user, MemberAccessResolver acces, ISender sender, CancellationToken ct)
        => await DenyUnlessOwnSellerAsync(user, sellerId, acces, MerchantPermission.SellerProfileView, ct)
            ?? (await sender.Send(new GetSellerDetailQuery(sellerId), ct)).Match(seller => ApiResults.Ok(seller));

    private static async Task<IResult> UpdateProfileAsync(
        Guid sellerId, UpdateProfileRequest request,
        ClaimsPrincipal user, MemberAccessResolver acces, ISender sender, CancellationToken ct)
        => await DenyUnlessOwnSellerAsync(user, sellerId, acces, MerchantPermission.SellerProfileUpdate, ct)
            ?? (await sender.Send(new UpdateSellerProfileCommand(
                sellerId, request.ShopName, request.LogoUrl, request.Description), ct)).Match(() => Results.NoContent());

    private static async Task<IResult> UpdateMetadataAsync(
        Guid sellerId, UpdateMetadataRequest request,
        ClaimsPrincipal user, MemberAccessResolver acces, ISender sender, CancellationToken ct)
        => await DenyUnlessOwnSellerAsync(user, sellerId, acces, MerchantPermission.SellerProfileUpdate, ct)
            ?? (await sender.Send(new UpdateSellerMetadataCommand(sellerId, request.Metadata), ct))
                .Match(() => Results.NoContent());

    private static async Task<IResult> SetPayoutAsync(
        Guid sellerId, SetPayoutRequest request,
        ClaimsPrincipal user, MemberAccessResolver acces, ISender sender, CancellationToken ct)
        => await DenyUnlessOwnSellerAsync(user, sellerId, acces, MerchantPermission.PayoutConfigure, ct)
            ?? (await sender.Send(new SetPayoutAccountCommand(
                sellerId, request.Provider, request.AccountNumber, request.AccountName), ct))
                .Match(() => Results.NoContent());

    private static async Task<IResult> AddKybDocumentAsync(
        Guid sellerId, AddKybDocumentRequest request,
        ClaimsPrincipal user, MemberAccessResolver acces, ISender sender, CancellationToken ct)
        => await DenyUnlessOwnSellerAsync(user, sellerId, acces, MerchantPermission.KybManage, ct)
            ?? (await sender.Send(new AddKybDocumentCommand(
                sellerId, request.Type, request.MediaId,
                // Le déposant de la pièce doit appartenir à CE dossier vendeur —
                // voir le gestionnaire.
                RequestedByUserId: CurrentUserId(user) ?? Guid.Empty), ct))
                .Match(id => ApiResults.Created(new { id }, $"/api/v1/merchants/{sellerId}/kyb/documents/{id}"));

    private static async Task<IResult> RemoveKybDocumentAsync(
        Guid sellerId, Guid documentId,
        ClaimsPrincipal user, MemberAccessResolver acces, ISender sender, CancellationToken ct)
        => await DenyUnlessOwnSellerAsync(user, sellerId, acces, MerchantPermission.KybManage, ct)
            ?? (await sender.Send(new RemoveKybDocumentCommand(sellerId, documentId), ct))
                .Match(() => Results.NoContent());

    private static async Task<IResult> SubmitKybAsync(
        Guid sellerId, ClaimsPrincipal user, MemberAccessResolver acces,
        ISender sender, CancellationToken ct)
        => await DenyUnlessOwnSellerAsync(user, sellerId, acces, MerchantPermission.KybManage, ct)
            ?? (await sender.Send(new SubmitKybCommand(sellerId), ct))
                .Match(() => Results.NoContent());

    private static async Task<IResult> ApproveKybAsync(Guid sellerId, ISender sender, CancellationToken ct)
        => (await sender.Send(new ApproveKybCommand(sellerId), ct)).Match(() => Results.NoContent());

    private static async Task<IResult> RejectKybAsync(
        Guid sellerId, ReasonRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new RejectKybCommand(sellerId, request.Reason), ct)).Match(() => Results.NoContent());

    private static async Task<IResult> ActivateSellerAsync(Guid sellerId, ISender sender, CancellationToken ct)
        => (await sender.Send(new ActivateSellerCommand(sellerId), ct)).Match(() => Results.NoContent());

    private static async Task<IResult> SuspendSellerAsync(
        Guid sellerId, ReasonRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new SuspendSellerCommand(sellerId, request.Reason), ct)).Match(() => Results.NoContent());

    private static async Task<IResult> LiftSuspensionAsync(Guid sellerId, ISender sender, CancellationToken ct)
        => (await sender.Send(new LiftSellerSuspensionCommand(sellerId), ct)).Match(() => Results.NoContent());

    private static async Task<IResult> RequestClosureAsync(
        Guid sellerId, ClaimsPrincipal user, MemberAccessResolver acces, ISender sender, CancellationToken ct)
        => await DenyUnlessOwnSellerAsync(user, sellerId, acces, MerchantPermission.SellerClose, ct)
            ?? (await sender.Send(new RequestSellerClosureCommand(sellerId), ct)).Match(() => Results.NoContent());

    private static async Task<IResult> RequestReactivationAsync(
        Guid sellerId, ClaimsPrincipal user, MemberAccessResolver acces, ISender sender, CancellationToken ct)
        => await DenyUnlessOwnSellerAsync(user, sellerId, acces, MerchantPermission.SellerReactivate, ct)
            ?? (await sender.Send(new RequestSellerReactivationCommand(sellerId), ct)).Match(() => Results.NoContent());

    private static async Task<IResult> ApproveReactivationAsync(Guid sellerId, ISender sender, CancellationToken ct)
        => (await sender.Send(new ApproveSellerReactivationCommand(sellerId), ct)).Match(() => Results.NoContent());

    private static async Task<IResult> DeleteSellerAsync(Guid sellerId, ISender sender, CancellationToken ct)
        => (await sender.Send(new DeleteSellerCommand(sellerId), ct)).Match(() => Results.NoContent());

    // POURQUOI CHAQUE ROUTE BOUTIQUE REFAIT LE MÊME CONTRÔLE.

    private static async Task<IResult> ListStoresAsync(
        Guid sellerId, ClaimsPrincipal user, MemberAccessResolver acces, ISender sender, CancellationToken ct)
        => await DenyUnlessOwnSellerAsync(user, sellerId, acces, MerchantPermission.StoreView, ct)
            ?? (await sender.Send(new ListSellerStoresQuery(sellerId), ct)).Match(stores => ApiResults.Ok(stores));

    private static async Task<IResult> CreateStoreAsync(
        Guid sellerId, CreateStoreRequest request,
        ClaimsPrincipal user, MemberAccessResolver acces, ISender sender, CancellationToken ct)
        => await DenyUnlessOwnSellerAsync(user, sellerId, acces, MerchantPermission.StoreCreate, ct)
            ?? (await sender.Send(new CreateStoreCommand(
                sellerId, request.Name, request.ContactPhone, request.ContactEmail), ct))
                .Match(id => ApiResults.Created(new { id }, $"/api/v1/merchants/{sellerId}/stores/{id}"));

    private static async Task<IResult> GetStoreAsync(
        Guid sellerId, Guid storeId,
        ClaimsPrincipal user, MemberAccessResolver acces, ISellerModuleApi sellers,
        ISender sender, CancellationToken ct)
    {
        if (await DenyUnlessOwnSellerAsync(user, sellerId, acces, MerchantPermission.StoreView, ct, storeId) is { } denied)
        {
            return denied;
        }

        var store = await sellers.GetStoreAsync(storeId, ct);

        if (store is null || store.SellerId != sellerId)
        {
            return ApiResults.NotFound(ServiceCodes.Seller);
        }

        return (await sender.Send(new GetStoreQuery(storeId), ct)).Match(found => ApiResults.Ok(found));
    }

    private static async Task<IResult> UpdateStoreProfileAsync(
        Guid sellerId, Guid storeId, StoreProfileRequest request,
        ClaimsPrincipal user, MemberAccessResolver acces, ISender sender, CancellationToken ct)
        => await DenyUnlessOwnSellerAsync(user, sellerId, acces, MerchantPermission.StoreUpdate, ct, storeId)
            ?? (await sender.Send(new UpdateStoreProfileCommand(
                storeId, sellerId, request.Name, request.LogoUrl, request.Description), ct))
                .Match(() => Results.NoContent());

    private static async Task<IResult> UpdateStoreContactAsync(
        Guid sellerId, Guid storeId, StoreContactRequest request,
        ClaimsPrincipal user, MemberAccessResolver acces, ISender sender, CancellationToken ct)
        => await DenyUnlessOwnSellerAsync(user, sellerId, acces, MerchantPermission.StoreUpdate, ct, storeId)
            ?? (await sender.Send(new UpdateStoreContactCommand(
                storeId, sellerId, request.ContactPhone, request.ContactEmail), ct))
                .Match(() => Results.NoContent());

    private static async Task<IResult> AttachStoreLocationAsync(
        Guid sellerId, Guid storeId, AttachLocationRequest request,
        ClaimsPrincipal user, MemberAccessResolver acces, ISender sender, CancellationToken ct)
        => await DenyUnlessOwnSellerAsync(user, sellerId, acces, MerchantPermission.StoreUpdate, ct, storeId)
            ?? (await sender.Send(new AttachStoreLocationCommand(
                storeId, sellerId, request.FulfillmentLocationId), ct)).Match(() => Results.NoContent());

    private static async Task<IResult> SetOpeningHoursAsync(
        Guid sellerId, Guid storeId, SetOpeningHoursRequest request,
        ClaimsPrincipal user, MemberAccessResolver acces, ISender sender, CancellationToken ct)
        => await DenyUnlessOwnSellerAsync(user, sellerId, acces, MerchantPermission.StoreUpdate, ct, storeId)
            ?? (await sender.Send(new SetStoreOpeningHoursCommand(storeId, sellerId, request.Hours), ct))
                .Match(() => Results.NoContent());

    private static async Task<IResult> OpenStoreAsync(
        Guid sellerId, Guid storeId, ClaimsPrincipal user, MemberAccessResolver acces, ISender sender, CancellationToken ct)
        => await DenyUnlessOwnSellerAsync(user, sellerId, acces, MerchantPermission.StoreOpenClose, ct, storeId)
            ?? (await sender.Send(new OpenStoreCommand(storeId, sellerId), ct)).Match(() => Results.NoContent());

    private static async Task<IResult> CloseStoreAsync(
        Guid sellerId, Guid storeId, ReasonRequest request,
        ClaimsPrincipal user, MemberAccessResolver acces, ISender sender, CancellationToken ct)
        => await DenyUnlessOwnSellerAsync(user, sellerId, acces, MerchantPermission.StoreOpenClose, ct, storeId)
            ?? (await sender.Send(new CloseStoreCommand(storeId, sellerId, request.Reason), ct))
                .Match(() => Results.NoContent());

    private static async Task<IResult> SuspendStoreAsync(
        Guid storeId, ReasonRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new SuspendStoreCommand(storeId, request.Reason), ct)).Match(() => Results.NoContent());

    private static async Task<IResult> LiftStoreSuspensionAsync(Guid storeId, ISender sender, CancellationToken ct)
        => (await sender.Send(new LiftStoreSuspensionCommand(storeId), ct)).Match(() => Results.NoContent());

    // L'ÉQUIPE — TOUTES CES ROUTES PASSENT L'IDENTIFIANT DE L'APPELANT.

    private static async Task<IResult> ListMembersAsync(
        Guid sellerId, ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => CurrentUserId(user) is not { } userId
            ? ApiResults.Unauthorized()
            : (await sender.Send(new ListMembersQuery(sellerId, userId), ct)).Match(ApiResults.Ok);

    private static async Task<IResult> GetMemberAsync(
        Guid sellerId, Guid memberId, ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => CurrentUserId(user) is not { } userId
            ? ApiResults.Unauthorized()
            : (await sender.Send(new GetMemberQuery(sellerId, userId, memberId), ct)).Match(ApiResults.Ok);

    private static async Task<IResult> ListInvitationsAsync(
        Guid sellerId, ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => CurrentUserId(user) is not { } userId
            ? ApiResults.Unauthorized()
            : (await sender.Send(new ListInvitationsQuery(sellerId, userId), ct)).Match(ApiResults.Ok);

    private static async Task<IResult> ListSellerRolesAsync(
        Guid sellerId, ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => CurrentUserId(user) is not { } userId
            ? ApiResults.Unauthorized()
            : (await sender.Send(new ListSellerRolesQuery(sellerId, userId), ct)).Match(ApiResults.Ok);

    private static async Task<IResult> CreateSellerRoleAsync(
        Guid sellerId, CreateSellerRoleRequest request,
        ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => CurrentUserId(user) is not { } userId
            ? ApiResults.Unauthorized()
            : (await sender.Send(new CreateSellerRoleCommand(
                sellerId, userId, request.Name, request.Description,
                request.Scope, request.Permissions ?? []), ct))
                .Match(id => ApiResults.Created(new { id }, $"/api/v1/merchants/{sellerId}/roles/{id}"));

    private static async Task<IResult> UpdateSellerRoleAsync(
        Guid sellerId, Guid roleId, UpdateSellerRoleRequest request,
        ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => CurrentUserId(user) is not { } userId
            ? ApiResults.Unauthorized()
            : (await sender.Send(new UpdateSellerRoleCommand(
                sellerId, userId, roleId, request.Name, request.Description,
                request.Permissions ?? []), ct))
                .Match(() => Results.NoContent());

    private static async Task<IResult> DeleteSellerRoleAsync(
        Guid sellerId, Guid roleId, ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => CurrentUserId(user) is not { } userId
            ? ApiResults.Unauthorized()
            : (await sender.Send(new DeleteSellerRoleCommand(sellerId, userId, roleId), ct))
                .Match(() => Results.NoContent());

    /// <summary>Le journal des gestes de l'équipe.</summary>
    private static async Task<IResult> ListAuditEntriesAsync(
        Guid sellerId,
        Guid? memberUserId,
        DateTime? fromUtc,
        DateTime? toUtc,
        int? page,
        int? pageSize,
        ClaimsPrincipal user,
        ISender sender,
        CancellationToken ct)
        => CurrentUserId(user) is not { } userId
            ? ApiResults.Unauthorized()
            : (await sender.Send(new ListAuditEntriesQuery(
                sellerId, userId, memberUserId, fromUtc, toUtc, page ?? 1, pageSize ?? 20), ct))
                .Match(resultat => ApiResults.Page(resultat));

    /// <summary>Quitter volontairement une équipe.</summary>
    private static async Task<IResult> LeaveSellerAsync(
        Guid sellerId, ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => CurrentUserId(user) is not { } userId
            ? ApiResults.Unauthorized()
            : (await sender.Send(new LeaveSellerCommand(sellerId, userId), ct))
                .Match(() => Results.NoContent());

    private static async Task<IResult> ListPermissionsAsync(ISender sender, CancellationToken ct)
        => (await sender.Send(new ListPermissionsQuery(), ct)).Match(ApiResults.Ok);

    /// <summary>LA RÉPONSE CONTIENT LE JETON, ET C'EST LE SEUL MOMENT OÙ IL EXISTE.</summary>
    private static async Task<IResult> InviteMemberAsync(
        Guid sellerId, InviteMemberRequest request,
        ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => CurrentUserId(user) is not { } userId
            ? ApiResults.Unauthorized()
            : (await sender.Send(new InviteMemberCommand(
                sellerId, userId, request.Email, request.DisplayName, request.JobTitle,
                request.RoleIds ?? [], request.Stores ?? []), ct))
                .Match(ApiResults.Ok);

    private static async Task<IResult> ResendInvitationAsync(
        Guid sellerId, Guid invitationId, ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => CurrentUserId(user) is not { } userId
            ? ApiResults.Unauthorized()
            : (await sender.Send(new ResendInvitationCommand(sellerId, userId, invitationId), ct))
                .Match(ApiResults.Ok);

    private static async Task<IResult> RevokeInvitationAsync(
        Guid sellerId, Guid invitationId, ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => CurrentUserId(user) is not { } userId
            ? ApiResults.Unauthorized()
            : (await sender.Send(new RevokeInvitationCommand(sellerId, userId, invitationId), ct))
                .Match(() => Results.NoContent());

    /// <summary>AUCUN `sellerId` DANS CETTE ROUTE. LE JETON DÉSIGNE TOUT.</summary>
    private static async Task<IResult> AcceptInvitationAsync(
        AcceptInvitationRequest request, ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => CurrentUserId(user) is not { } userId
            ? ApiResults.Unauthorized()
            : (await sender.Send(new AcceptInvitationCommand(request.Token, userId), ct))
                .Match(id => ApiResults.Ok(new { memberId = id }));

    private static async Task<IResult> SetMemberRolesAsync(
        Guid sellerId, Guid memberId, MemberRolesRequest request,
        ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => CurrentUserId(user) is not { } userId
            ? ApiResults.Unauthorized()
            : (await sender.Send(new SetMemberRolesCommand(
                sellerId, userId, memberId, request.RoleIds ?? []), ct))
                .Match(() => Results.NoContent());

    private static async Task<IResult> AssignMemberStoreAsync(
        Guid sellerId, Guid memberId, Guid storeId, MemberRolesRequest request,
        ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => CurrentUserId(user) is not { } userId
            ? ApiResults.Unauthorized()
            : (await sender.Send(new AssignMemberStoreCommand(
                sellerId, userId, memberId, storeId, request.RoleIds ?? []), ct))
                .Match(() => Results.NoContent());

    private static async Task<IResult> UnassignMemberStoreAsync(
        Guid sellerId, Guid memberId, Guid storeId,
        ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => CurrentUserId(user) is not { } userId
            ? ApiResults.Unauthorized()
            : (await sender.Send(new UnassignMemberStoreCommand(sellerId, userId, memberId, storeId), ct))
                .Match(() => Results.NoContent());

    private static async Task<IResult> SuspendMemberAsync(
        Guid sellerId, Guid memberId, ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => CurrentUserId(user) is not { } userId
            ? ApiResults.Unauthorized()
            : (await sender.Send(new SuspendMemberCommand(sellerId, userId, memberId), ct))
                .Match(() => Results.NoContent());

    private static async Task<IResult> ReactivateMemberAsync(
        Guid sellerId, Guid memberId, ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => CurrentUserId(user) is not { } userId
            ? ApiResults.Unauthorized()
            : (await sender.Send(new ReactivateMemberCommand(sellerId, userId, memberId), ct))
                .Match(() => Results.NoContent());

    private static async Task<IResult> RevokeMemberAsync(
        Guid sellerId, Guid memberId, ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => CurrentUserId(user) is not { } userId
            ? ApiResults.Unauthorized()
            : (await sender.Send(new RevokeMemberCommand(sellerId, userId, memberId), ct))
                .Match(() => Results.NoContent());

    /// <summary>
    /// Transfère la propriété du dossier au membre désigné.
    /// </summary>
    /// <remarks>
    /// LE STEP-UP AVANT L'ENVOI, LA PERMISSION APRÈS — L'ORDRE INVERSE DE
    /// `DenyUnlessOwnSellerAsync`, ET C'EST ASSUMÉ.
    ///
    /// Là-bas, la permission est vérifiée d'abord pour qu'un membre non autorisé
    /// lise « votre rôle ne l'autorise pas » plutôt que d'aller ressaisir son mot
    /// de passe pour rien. Ici, la permission ne se lit qu'après avoir résolu
    /// l'acteur en base — c'est-à-dire dans le handler. Exiger la réauthentification
    /// avant coûte donc un écran de trop à un membre non propriétaire ; l'accepter
    /// laisserait le geste le plus irréversible du module se faire depuis une
    /// session ouverte le matin et laissée sans surveillance.
    ///
    /// Entre les deux, on protège le dossier.
    /// </remarks>
    private static async Task<IResult> TransferOwnershipAsync(
        Guid sellerId, Guid memberId, ClaimsPrincipal user, ISender sender, CancellationToken ct)
    {
        if (CurrentUserId(user) is not { } userId)
        {
            return ApiResults.Unauthorized();
        }

        if (!user.HasRecentAuthentication())
        {
            return ApiResults.ReauthenticationRequired(
                MerchantPermission.OwnershipTransfer.ToCode());
        }

        var resultat = await sender.Send(
            new TransferSellerOwnershipCommand(sellerId, userId, memberId), ct);

        return resultat.Match(() => Results.NoContent());
    }

    private static Guid? CurrentUserId(ClaimsPrincipal principal)
    {
        var raw = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue("sub");
        return Guid.TryParse(raw, out var id) ? id : null;
    }

    public sealed record RegisterSellerRequest(string ShopName, decimal? CommissionRate, SellerCompanyInfo? Metadata);

    /// <summary>Corps de `POST /api/v1/merchants/inscriptions` (Admin).</summary>
    public sealed record RegisterSellerForUserRequest(
        Guid UserId, string ShopName, decimal? CommissionRate, SellerCompanyInfo? Metadata);

    public sealed record UpdateProfileRequest(string ShopName, string? LogoUrl, string? Description);

    public sealed record UpdateMetadataRequest(SellerCompanyInfo? Metadata);

    public sealed record SetPayoutRequest(string Provider, string AccountNumber, string AccountName);

    public sealed record AddKybDocumentRequest(string Type, Guid MediaId);

    public sealed record ReasonRequest(string? Reason);

    public sealed record CreateStoreRequest(string Name, string ContactPhone, string? ContactEmail);

    public sealed record StoreProfileRequest(string Name, string? LogoUrl, string? Description);

    public sealed record StoreContactRequest(string ContactPhone, string? ContactEmail);

    public sealed record AttachLocationRequest(Guid FulfillmentLocationId);

    public sealed record SetOpeningHoursRequest(IReadOnlyList<OpeningHourInput> Hours);

    /// <summary>Corps de `POST /merchants/{sellerId}/roles`.</summary>
    /// <param name="Scope">
    /// `Seller` ou `Store`. Absent, c'est `Seller` — voir `LirePortee` : en phase 1
    /// un rôle de vocation boutique s'applique de toute façon au vendeur entier, et
    /// choisir `Store` par défaut ferait croire à un cadrage qui n'existe pas.
    /// </param>
    /// <param name="Permissions">
    /// Les CODES publics (`ORDER_CONFIRM`, `INVENTORY_ADJUST`…), tels que `GET
    /// /merchants/permissions` les rend.
    /// </param>
    public sealed record CreateSellerRoleRequest(
        string Name,
        string? Description,
        string? Scope,
        IReadOnlyList<string>? Permissions);

    /// <summary>Corps de `PATCH /merchants/{sellerId}/roles/{roleId}`.</summary>
    public sealed record UpdateSellerRoleRequest(
        string Name,
        string? Description,
        IReadOnlyList<string>? Permissions);

    public sealed record InviteMemberRequest(
        string Email,
        string? DisplayName,
        string? JobTitle,
        IReadOnlyList<Guid>? RoleIds,
        IReadOnlyList<StoreAssignmentInput>? Stores);

    public sealed record MemberRolesRequest(IReadOnlyList<Guid>? RoleIds);

    public sealed record AcceptInvitationRequest(string Token);
}
