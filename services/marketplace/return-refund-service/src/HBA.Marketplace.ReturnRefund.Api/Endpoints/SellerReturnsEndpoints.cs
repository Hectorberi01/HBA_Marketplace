using System.Security.Claims;
using HBA.Marketplace.ReturnRefund.Application.Commands;
using HBA.Marketplace.ReturnRefund.Application.DTOs;
using HBA.Marketplace.ReturnRefund.Application.Queries;
using HBA.Merchants.Contracts;
using HBA.Shared.Domain.Results;
using HBA.Shared.Hosting.Http;
using MediatR;

namespace HBA.Marketplace.ReturnRefund.Api.Endpoints;

/// <summary>LES RETOURS, CÔTÉ VENDEUR.</summary>
public static class SellerReturnsEndpoints
{
    // Le catalogue de permissions du vendeur porte six `RETURN_*`. Elles étaient
    // attribuées aux rôles et ne gardaient AUCUNE route — le commentaire de
    // `MerchantPermission` le disait lui-même : « RETURN_* : le service est un
    // squelette ». Elles gardent enfin quelque chose.
    //
    // CES ALIAS POINTENT VERS `MerchantCapabilities`, ILS NE RECOPIENT PLUS LES
    // CODES. La première écriture déclarait ici cinq `private const string` valant
    // "RETURN_VIEW", "RETURN_APPROVE"… — les mêmes chaînes que le contrat, écrites
    // une deuxième fois. Deux conséquences, et la seconde est la pire :
    //
    //   • une faute de frappe dans l'une d'elles compilait, et la garde n'était
    //     alors JAMAIS satisfaite (`Can("RETURN_VEIW")` est faux pour tout le
    //     monde) — ou, si le code renommait la permission côté catalogue, la garde
    //     restait muette sur l'ancien code et laissait passer ;
    //   • `check-permissions.py` cherche les usages sous la forme
    //     `MerchantCapabilities.X` : une chaîne littérale lui est invisible. Le
    //     contrôle a donc déclaré ces cinq permissions « sans garde » alors
    //     qu'elles gardaient dix routes. Il a été corrigé le même jour pour voir
    //     aussi les littéraux ET pour les REFUSER — ici, la cause était le code.
    //
    // Les noms français restent : ils disent le geste métier à l'endroit où on lit
    // la route. Ce sont des alias, plus des copies.
    private const string VoirRetour = MerchantCapabilities.ReturnView;
    private const string ApprouverRetour = MerchantCapabilities.ReturnApprove;
    private const string RejeterRetour = MerchantCapabilities.ReturnReject;
    private const string InspecterRetour = MerchantCapabilities.ReturnInspect;
    private const string ConfirmerReception = MerchantCapabilities.ReturnConfirmReceived;

    public static IEndpointRouteBuilder MapSellerReturnsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapSellerGroup("/api/v1/seller/returns").WithTags("Seller Returns");

        group.MapGet("/", ListAsync);
        group.MapGet("/{id:guid}", GetAsync);
        group.MapPost("/{id:guid}/approve", ApproveAsync);
        group.MapPost("/{id:guid}/reject", RejectAsync);
        group.MapPost("/{id:guid}/inspection", InspectAsync);
        group.MapPost("/{id:guid}/refund-decision", DecideRefundAsync);
        group.MapPost("/{id:guid}/shipment", RegisterShipmentAsync);
        group.MapPost("/{id:guid}/receive", ReceiveAsync);

        return app;
    }

    /// <summary>
    /// `sellerId` NE VIENT PLUS DE LA REQUÊTE. Il est résolu depuis le jeton par
    /// seller-service, qui seul sait à quelle équipe appartient ce compte — et un
    /// compte peut appartenir à plusieurs vendeurs.
    /// </summary>
    private static async Task<IResult> ListAsync(
        int page,
        int pageSize,
        ClaimsPrincipal user,
        IMerchantAccessApi access,
        ISender sender,
        CancellationToken ct)
    {
        if (CurrentUserId(user) is not { } userId)
        {
            return Results.Unauthorized();
        }

        var acces = await access.GetAccessAsync(userId, ct);

        // `null` ne veut pas dire « interdit » mais « ce compte n'a aucun dossier
        // vendeur » — le contrat le dit.
        if (acces is null)
        {
            return Refus(VoirRetour);
        }

        if (!acces.Can(VoirRetour))
        {
            return Refus(VoirRetour);
        }

        return (await sender.Send(new GetSellerReturnsQuery(acces.SellerId, page, pageSize), ct))
            .Match(ApiResults.Page);
    }

    private static async Task<IResult> GetAsync(
        Guid id, ClaimsPrincipal user, IMerchantAccessApi access, ISender sender, CancellationToken ct)
    {
        var dossier = await sender.Send(new GetReturnQuery(id), ct);
        if (dossier.IsFailure)
        {
            return dossier.Match(ApiResults.Ok);
        }

        var garde = await VerifierAsync(dossier.Value, VoirRetour, user, access, ct);
        return garde ?? ApiResults.Ok(dossier.Value);
    }

    private static async Task<IResult> ApproveAsync(
        Guid id, ClaimsPrincipal user, IMerchantAccessApi access, ISender sender, CancellationToken ct)
        => await ExecuterAsync(
            id, ApprouverRetour, user, access, sender, ct,
            (utilisateur, envoyer) => envoyer.Send(new ApproveReturnCommand(id, utilisateur), ct));

    private static async Task<IResult> RejectAsync(
        Guid id, ReasonDto reason, ClaimsPrincipal user, IMerchantAccessApi access, ISender sender, CancellationToken ct)
        => await ExecuterAsync(
            id, RejeterRetour, user, access, sender, ct,
            (utilisateur, envoyer) => envoyer.Send(new RejectReturnCommand(id, reason.Reason, utilisateur), ct));

    private static async Task<IResult> InspectAsync(
        Guid id, InspectReturnDto request, ClaimsPrincipal user, IMerchantAccessApi access, ISender sender, CancellationToken ct)
        => await ExecuterAsync(
            id, InspecterRetour, user, access, sender, ct,
            (utilisateur, envoyer) => envoyer.Send(
                new InspectReturnCommand(id, request.Condition, request.Disposition, request.Notes, utilisateur), ct));

    /// <summary>LA DÉCISION DE REMBOURSEMENT EXIGE `RETURN_APPROVE`, PAS `RETURN_VIEW`.</summary>
    private static async Task<IResult> DecideRefundAsync(
        Guid id, DecideRefundDto request, ClaimsPrincipal user, IMerchantAccessApi access, ISender sender, CancellationToken ct)
        => await ExecuterAsync(
            id, ApprouverRetour, user, access, sender, ct,
            (utilisateur, envoyer) => envoyer.Send(
                new DecideRefundCommand(id, request.Amount, request.Currency, utilisateur), ct));

    /// <summary>`RETURN_CONFIRM_RECEIVED` pour l'expédition comme pour la réception.</summary>
    private static async Task<IResult> RegisterShipmentAsync(
        Guid id, RegisterShipmentDto request, ClaimsPrincipal user, IMerchantAccessApi access, ISender sender, CancellationToken ct)
        => await ExecuterAsync(
            id, ConfirmerReception, user, access, sender, ct,
            (utilisateur, envoyer) => envoyer.Send(
                new RegisterReturnShipmentCommand(id, request.DeliveryId, request.Mode, request.TrackingNumber, utilisateur), ct));

    private static async Task<IResult> ReceiveAsync(
        Guid id, ClaimsPrincipal user, IMerchantAccessApi access, ISender sender, CancellationToken ct)
        => await ExecuterAsync(
            id, ConfirmerReception, user, access, sender, ct,
            (utilisateur, envoyer) => envoyer.Send(new ReceiveReturnCommand(id, utilisateur), ct));

    /// <summary>
    /// Le chemin commun des six routes d'écriture : lire le dossier, vérifier
    /// l'appartenance ET la capacité, puis seulement exécuter.
    /// </summary>
    private static async Task<IResult> ExecuterAsync(
        Guid id,
        string permission,
        ClaimsPrincipal user,
        IMerchantAccessApi access,
        ISender sender,
        CancellationToken ct,
        Func<Guid?, ISender, Task<Result>> action)
    {
        var dossier = await sender.Send(new GetReturnQuery(id), ct);
        if (dossier.IsFailure)
        {
            return dossier.Match(ApiResults.Ok);
        }

        var garde = await VerifierAsync(dossier.Value, permission, user, access, ct);
        if (garde is not null)
        {
            return garde;
        }

        return (await action(CurrentUserId(user), sender)).Match(() => Results.NoContent());
    }

    /// <summary>
    /// Rend <c> null</c> quand l'appelant a le droit, ou le refus à renvoyer sinon.
    /// </summary>
    private static async Task<IResult?> VerifierAsync(
        ReturnRequestDto dossier,
        string permission,
        ClaimsPrincipal user,
        IMerchantAccessApi access,
        CancellationToken ct)
    {
        if (CurrentUserId(user) is not { } userId)
        {
            return Results.Unauthorized();
        }

        // Administrateurs et modérateurs entrent par ce groupe (voir
        // MapSellerGroup) et arbitrent les litiges : ils ne sont rattachés à aucun
        // vendeur, donc `HasCapabilityAsync` les refuserait tous.
        if (user.IsInRole(ApiAuthorization.AdminRole) || user.IsInRole(ApiAuthorization.ModeratorRole))
        {
            return null;
        }

        var autorise = await access.HasCapabilityAsync(
            userId, dossier.SellerId, dossier.StoreId, permission, ct);

        return autorise ? null : Refus(permission);
    }

    private static IResult Refus(string permission)
        => ApiResults.MissingCapability(
            permission,
            "Ce dossier de retour n'appartient pas à votre équipe, ou votre rôle ne porte pas cette capacité.");

    private static Guid? CurrentUserId(ClaimsPrincipal user)
    {
        var value = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
        return Guid.TryParse(value, out var id) ? id : null;
    }
}
