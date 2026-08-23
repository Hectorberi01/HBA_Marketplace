using Microsoft.Extensions.Logging;
using HBA.Marketplace.ReturnRefund.Application.Abstractions;
using HBA.Marketplace.ReturnRefund.Domain.Policies;
using HBA.Marketplace.ReturnRefund.Domain.Repositories;
using HBA.Marketplace.ReturnRefund.Domain.ValueObjects;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;
using ReturnAggregate = HBA.Marketplace.ReturnRefund.Domain.Aggregates.ReturnRequest.ReturnRequest;

namespace HBA.Marketplace.ReturnRefund.Application.Commands;

public sealed record CancelReturnCommand(Guid ReturnId, Guid? ActorId) : ICommand;
public sealed record AddEvidenceCommand(Guid ReturnId, string MediaId, string Kind, string? Caption, Guid? ActorId) : ICommand;
public sealed record ApproveReturnCommand(Guid ReturnId, Guid? ActorId) : ICommand;
public sealed record RejectReturnCommand(Guid ReturnId, string Reason, Guid? ActorId) : ICommand;
public sealed record RegisterReturnShipmentCommand(Guid ReturnId, string DeliveryId, string Mode, string? TrackingNumber, Guid? ActorId) : ICommand;
public sealed record ReceiveReturnCommand(Guid ReturnId, Guid? ActorId) : ICommand;
public sealed record InspectReturnCommand(Guid ReturnId, Domain.Enums.InspectionCondition Condition, Domain.Enums.StockDisposition Disposition, string Notes, Guid? ActorId) : ICommand;
public sealed record DecideRefundCommand(Guid ReturnId, decimal Amount, string Currency, Guid? ActorId) : ICommand;
public sealed record ExecuteRefundCommand(Guid ReturnId, Guid RefundId) : ICommand;
public sealed record CloseReturnCommand(Guid ReturnId, Guid? ActorId) : ICommand;

internal abstract class ReturnCommandHandlerBase
{
    protected ReturnCommandHandlerBase(IReturnRequestRepository returns, IReturnRefundUnitOfWork unitOfWork, IClock clock)
    {
        Returns = returns;
        UnitOfWork = unitOfWork;
        Clock = clock;
    }

    protected IReturnRequestRepository Returns { get; }
    protected IReturnRefundUnitOfWork UnitOfWork { get; }
    protected IClock Clock { get; }
}

internal sealed class CancelReturnCommandHandler : ReturnCommandHandlerBase, ICommandHandler<CancelReturnCommand>
{
    public CancelReturnCommandHandler(IReturnRequestRepository returns, IReturnRefundUnitOfWork unitOfWork, IClock clock)
        : base(returns, unitOfWork, clock) { }

    public async Task<Result> Handle(CancelReturnCommand command, CancellationToken cancellationToken)
    {
        var request = await Returns.GetAsync(command.ReturnId, cancellationToken);
        if (request is null) return Result.Failure(Error.NotFound("return.not_found", "Retour introuvable."));
        var result = request.Cancel(Clock.UtcNow, command.ActorId);
        if (result.IsFailure) return result;
        await UnitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

internal sealed class AddEvidenceCommandHandler : ReturnCommandHandlerBase, ICommandHandler<AddEvidenceCommand>
{
    private readonly IMediaGrpcClient _media;

    public AddEvidenceCommandHandler(IReturnRequestRepository returns, IReturnRefundUnitOfWork unitOfWork, IClock clock, IMediaGrpcClient media)
        : base(returns, unitOfWork, clock) => _media = media;

    public async Task<Result> Handle(AddEvidenceCommand command, CancellationToken cancellationToken)
    {
        var request = await Returns.GetAsync(command.ReturnId, cancellationToken);
        if (request is null) return Result.Failure(Error.NotFound("return.not_found", "Retour introuvable."));
        var media = await _media.ValidateMediaAsync(command.MediaId, request.CustomerId, cancellationToken);
        if (media.IsFailure) return media;
        var result = request.AddEvidence(command.MediaId, command.Kind, command.Caption, Clock.UtcNow);
        if (result.IsFailure) return result;
        await UnitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

internal sealed class ApproveReturnCommandHandler : ReturnCommandHandlerBase, ICommandHandler<ApproveReturnCommand>
{
    public ApproveReturnCommandHandler(IReturnRequestRepository returns, IReturnRefundUnitOfWork unitOfWork, IClock clock)
        : base(returns, unitOfWork, clock) { }

    public async Task<Result> Handle(ApproveReturnCommand command, CancellationToken cancellationToken)
    {
        var request = await Returns.GetAsync(command.ReturnId, cancellationToken);
        if (request is null) return Result.Failure(Error.NotFound("return.not_found", "Retour introuvable."));
        var result = request.Approve(Clock.UtcNow, command.ActorId);
        if (result.IsFailure) return result;
        await UnitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

internal sealed class RejectReturnCommandHandler : ReturnCommandHandlerBase, ICommandHandler<RejectReturnCommand>
{
    public RejectReturnCommandHandler(IReturnRequestRepository returns, IReturnRefundUnitOfWork unitOfWork, IClock clock)
        : base(returns, unitOfWork, clock) { }

    public async Task<Result> Handle(RejectReturnCommand command, CancellationToken cancellationToken)
    {
        var request = await Returns.GetAsync(command.ReturnId, cancellationToken);
        if (request is null) return Result.Failure(Error.NotFound("return.not_found", "Retour introuvable."));
        var result = request.Reject(command.Reason, Clock.UtcNow, command.ActorId);
        if (result.IsFailure) return result;
        await UnitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

/// <summary>
/// Enregistre l'expédition de retour, en créant la course d'enlèvement si
/// l'appelant n'en fournit pas.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// ICI L'APPEL EXTERNE NE PEUT PAS SUIVRE LA PERSISTANCE (ISSUE-032).
///
/// Le motif « appel externe avant `SaveChangesAsync` » a été inversé partout où
/// c'était possible — annulation de commande, inspection de retour. Pas ici :
/// c'est la course qui PRODUIT l'identifiant qu'on veut écrire. Persister d'abord
/// supposerait un état « expédition demandée, identifiant inconnu » dans
/// l'agrégat, donc une transition et une migration.
///
/// Ce qui est fait à la place : l'échec devient VISIBLE. Si l'écriture échoue
/// après la création de la course, l'identifiant de la course orpheline part en
/// `Critical` — c'est la seule prise pour l'annuler ou la rattacher à la main.
/// Sans cela, un coursier serait dépêché pour un retour dont le dossier ignore
/// tout, et personne ne saurait lequel.
///
/// CE QU'IL FAUDRAIT POUR FERMER VRAIMENT : que la création de course soit
/// idempotente sur l'identifiant du retour, de sorte qu'un rejeu rende la même
/// course au lieu d'en créer une seconde. Cela se décide côté delivery-service,
/// dont l'adaptateur est aujourd'hui un bouchon — voir `DeliveryGrpcClient`.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
internal sealed class RegisterReturnShipmentCommandHandler : ReturnCommandHandlerBase, ICommandHandler<RegisterReturnShipmentCommand>
{
    private readonly IDeliveryGrpcClient _delivery;
    private readonly ILogger<RegisterReturnShipmentCommandHandler> _logger;

    public RegisterReturnShipmentCommandHandler(
        IReturnRequestRepository returns,
        IReturnRefundUnitOfWork unitOfWork,
        IClock clock,
        IDeliveryGrpcClient delivery,
        ILogger<RegisterReturnShipmentCommandHandler> logger)
        : base(returns, unitOfWork, clock)
    {
        _delivery = delivery;
        _logger = logger;
    }

    public async Task<Result> Handle(RegisterReturnShipmentCommand command, CancellationToken cancellationToken)
    {
        var request = await Returns.GetAsync(command.ReturnId, cancellationToken);
        if (request is null) return Result.Failure(Error.NotFound("return.not_found", "Retour introuvable."));

        // RÉÉCRIT : le filtrage par motif portait sur une expression dont le
        // type unifié était TOUJOURS `Result<string>`, si bien que `deliveryId is
        // Result<string>` était vrai dans les deux branches et que la branche
        // « identifiant fourni » repassait par `ok.Value`. Cela fonctionnait par
        // coïncidence, pas par construction.
        string course;
        var creee = false;

        if (string.IsNullOrWhiteSpace(command.DeliveryId))
        {
            var creation = await _delivery.CreateReturnDeliveryAsync(
                request.Id, request.OrderId, request.SellerId, request.CustomerId, cancellationToken);

            if (creation.IsFailure)
            {
                return Result.Failure(creation.Error);
            }

            course = creation.Value;
            creee = true;
        }
        else
        {
            course = command.DeliveryId;
        }

        var registered = request.RegisterShipment(course, command.Mode, command.TrackingNumber, Clock.UtcNow, command.ActorId);
        if (registered.IsFailure) return registered;

        try
        {
            await UnitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (creee)
        {
            _logger.LogCritical(
                exception,
                "Retour {ReturnId} : la course d'enlèvement {DeliveryId} a été CRÉÉE et l'enregistrement "
                + "a échoué. Le dossier ignore cette course : un coursier peut être dépêché sans que rien "
                + "ne le relie au retour. Annulation ou rattachement manuel requis.",
                request.Id, course);

            throw;
        }

        return Result.Success();
    }
}

internal sealed class ReceiveReturnCommandHandler : ReturnCommandHandlerBase, ICommandHandler<ReceiveReturnCommand>
{
    public ReceiveReturnCommandHandler(IReturnRequestRepository returns, IReturnRefundUnitOfWork unitOfWork, IClock clock)
        : base(returns, unitOfWork, clock) { }

    public async Task<Result> Handle(ReceiveReturnCommand command, CancellationToken cancellationToken)
    {
        var request = await Returns.GetAsync(command.ReturnId, cancellationToken);
        if (request is null) return Result.Failure(Error.NotFound("return.not_found", "Retour introuvable."));
        var result = request.Receive(Clock.UtcNow, command.ActorId);
        if (result.IsFailure) return result;
        await UnitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

/// <summary>
/// Inspecte la marchandise revenue et décide de son sort (remise en rayon, mise au
/// rebut…).
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// L'INSPECTION EST ÉCRITE AVANT QUE LE STOCK NE BOUGE (ISSUE-032).
///
/// L'ordre était l'inverse : on remettait la marchandise en rayon chez Inventory
/// pour chaque ligne, PUIS on enregistrait l'inspection. Un `SaveChangesAsync` qui
/// lève laissait alors le stock remis et l'inspection nulle part. Le dossier
/// restait « à inspecter », et le geste suivant — le rejeu du message, ou
/// l'opérateur qui recommence — REMETTAIT LA MÊME MARCHANDISE EN RAYON une
/// seconde fois. Du stock fantôme, vendable, qui n'existe pas dans l'entrepôt.
///
/// CE QUE L'ORDRE INVERSE COÛTE, ET POURQUOI C'EST MOINS CHER.
///
/// Si la remise en stock échoue APRÈS l'écriture, l'inspection est enregistrée et
/// la marchandise n'est pas rentrée : elle est physiquement là, invisible du
/// système. C'est un manque à gagner, réparable par une reprise manuelle, et le
/// journal `Critical` ci-dessous porte le retour et la ligne concernée. Le stock
/// fantôme, lui, se paie en commande vendue qu'on ne peut pas honorer.
///
/// UN ÉCHEC DE STOCK NE FAIT PLUS ÉCHOUER LA COMMANDE, ET C'EST FORCÉ.
///
/// Il rendait `Result.Failure` ; il ne le peut plus, l'inspection étant déjà
/// committée. Rendre une erreur pour un geste qui a bel et bien eu lieu
/// inviterait l'opérateur à recommencer une inspection déjà faite.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
internal sealed class InspectReturnCommandHandler : ReturnCommandHandlerBase, ICommandHandler<InspectReturnCommand>
{
    private readonly IInventoryGrpcClient _inventory;
    private readonly ILogger<InspectReturnCommandHandler> _logger;

    public InspectReturnCommandHandler(
        IReturnRequestRepository returns,
        IReturnRefundUnitOfWork unitOfWork,
        IClock clock,
        IInventoryGrpcClient inventory,
        ILogger<InspectReturnCommandHandler> logger)
        : base(returns, unitOfWork, clock)
    {
        _inventory = inventory;
        _logger = logger;
    }

    public async Task<Result> Handle(InspectReturnCommand command, CancellationToken cancellationToken)
    {
        var request = await Returns.GetAsync(command.ReturnId, cancellationToken);
        if (request is null) return Result.Failure(Error.NotFound("return.not_found", "Retour introuvable."));

        var result = request.Inspect(command.Condition, command.Disposition, command.Notes, Clock.UtcNow, command.ActorId);
        if (result.IsFailure) return result;

        await UnitOfWork.SaveChangesAsync(cancellationToken);

        foreach (var item in request.Items)
        {
            var stock = await _inventory.ProcessReturnedStockAsync(request.Id, item.OrderItemId, command.Disposition, cancellationToken);

            if (stock.IsFailure)
            {
                _logger.LogCritical(
                    "Retour {ReturnId} inspecté ({Disposition}), mais la ligne {OrderItemId} n'a PAS été "
                    + "traitée par l'inventaire — {Code} : {Message}. La marchandise est revenue et "
                    + "n'existe pas en stock : reprise manuelle requise.",
                    request.Id, command.Disposition, item.OrderItemId, stock.Error.Code, stock.Error.Message);
            }
        }

        return Result.Success();
    }
}

/// <summary>
/// Fixe le montant rendu au client.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// LE PLAFOND COMPARAIT UNE VALEUR À ELLE-MÊME (ISSUE-049).
///
/// Ce gestionnaire fabriquait le détail de remboursement AVEC LE MONTANT SAISI :
///
///     var breakdown = new RefundBreakdown(amount.Value, zero, zero, zero, zero, zero, zero);
///     var result = request.DecideRefund(amount.Value, breakdown, …);
///
/// `RefundBreakdown.Total()` valant alors exactement `Items`, c'est-à-dire le
/// montant demandé, le contrôle « le montant décidé dépasse-t-il le montant
/// calculé côté serveur ? » comparait le demandé au demandé. Il ne pouvait pas
/// échouer. Un vendeur pouvait rembourser 500 000 sur une commande de 12 000, et
/// le service validait — le garde-fou existait, s'exécutait, et n'arrêtait rien.
///
/// Le plafond est désormais construit DEPUIS LA COMMANDE. Les champs consommés
/// dans `OrderReturnContext` sont exactement ceux-ci :
///
///   • `Currency`                     — devise de contrôle ;
///   • `CapturedAmount`               — ce qui a été réellement encaissé ;
///   • `AlreadyRefundedAmount`        — ce qui en a déjà été rendu ;
///   • `Lines[].OrderItemId`          — rapprochement avec les lignes du retour ;
///   • `Lines[].UnitPaidAmount`       — prix unitaire PAYÉ, base du calcul ;
///   • `Lines[].DeliveredQuantity`    — borne haute de ce qui peut revenir ;
///   • `Lines[].AlreadyReturnedQuantity` — ce qui est déjà revenu.
///
/// CES DEUX CHAMPS DISENT DÉSORMAIS LA VÉRITÉ (ISSUE-014, corrigé).
///
/// `OrderingModuleApi.GetOrderReturnContextAsync` codait `AlreadyReturnedQuantity: 0`
/// et `AlreadyRefundedAmount: 0m` EN DUR : le plafond ignorait purement et
/// simplement les retours et remboursements antérieurs sur la même commande.
/// Order-service les apprend maintenant par `ReturnRefundedIntegrationEvent` et
/// les inscrit dans l'agrégat commande.
///
/// ET C'EST POURQUOI LE PLAFOND NE COMPTE PLUS `TotalRefunded()`.
///
/// `RefundCalculationPolicy.Validate` vérifie `demandé + engagé > plafond`. Tant
/// qu'`AlreadyRefundedAmount` valait zéro, y passer `TotalRefunded()` — tout ce
/// que CE dossier a engagé, `Succeeded` compris — était la seule protection.
/// Maintenant qu'order-service compte ces mêmes versements aboutis, les passer
/// à nouveau les compterait DEUX FOIS : le plafond se fermerait à la moitié du
/// montant réellement disponible, et un client légitimement remboursable se
/// verrait refuser.
///
/// On ne passe donc plus que l'engagement NON ABOUTI du dossier — `Pending` et
/// `Processing` —, exactement ce qu'order-service ne voit pas encore. La somme
/// des deux couvre tout, sans recouvrement.
///
/// Les versements déjà aboutis de ce dossier ne sont pas perdus pour autant :
/// ils entrent dans `RefundBreakdown.PreviousRefunds` (via `Compute`), donc dans
/// le PREMIER contrôle, celui du montant calculé côté serveur.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
internal sealed class DecideRefundCommandHandler : ReturnCommandHandlerBase, ICommandHandler<DecideRefundCommand>
{
    private readonly IOrderGrpcClient _orders;

    public DecideRefundCommandHandler(
        IReturnRequestRepository returns,
        IReturnRefundUnitOfWork unitOfWork,
        IClock clock,
        IOrderGrpcClient orders)
        : base(returns, unitOfWork, clock) => _orders = orders;

    public async Task<Result> Handle(DecideRefundCommand command, CancellationToken cancellationToken)
    {
        var request = await Returns.GetAsync(command.ReturnId, cancellationToken);
        if (request is null) return Result.Failure(Error.NotFound("return.not_found", "Retour introuvable."));

        var amount = Money.Create(command.Amount, command.Currency);
        if (amount.IsFailure) return Result.Failure(amount.Error);

        var order = await _orders.GetOrderReturnContextAsync(request.OrderId, cancellationToken);
        if (order.IsFailure) return Result.Failure(order.Error);

        // Comparer des montants de devises différentes n'a aucun sens, et le faire
        // silencieusement donnerait un plafond en francs pour une saisie en euros.
        if (!string.Equals(order.Value.Currency, amount.Value.Currency, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure(Error.Validation(
                "refund.currency_mismatch",
                "La devise du remboursement ne correspond pas a celle de la commande."));
        }

        // Ce que la COMMANDE peut encore rendre, tous dossiers de retour confondus.
        var plafondCommande = order.Value.CapturedAmount - order.Value.AlreadyRefundedAmount;

        // Les AUTRES dossiers ouverts sur cette commande — ceux qu'order-service
        // ne voit pas encore. Le nôtre est exclu : ses propres engagements sont
        // déjà comptés par l'agrégat.
        var autresDossiers = await Returns.ListOpenQuantitiesByOrderAsync(
            request.OrderId, exceptReturnId: request.Id, cancellationToken);

        var result = request.DecideRefund(
            amount.Value,
            LignesRemboursables(request, order.Value, autresDossiers),
            plafondCommande,
            Clock.UtcNow,
            command.ActorId);

        if (result.IsFailure) return result;
        await UnitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    /// <summary>
    /// Croise les lignes du RETOUR avec celles de la COMMANDE pour obtenir, ligne à
    /// ligne, la quantité qu'on accepte de reprendre et son prix unitaire payé.
    /// </summary>
    /// <remarks>
    /// LA QUANTITÉ RETENUE EST LA REÇUE QUAND ELLE EXISTE, PAS LA DEMANDÉE.
    ///
    /// Un client peut demander trois articles et n'en renvoyer que deux :
    /// `ReturnItem.ReceivedQuantity` est renseignée à la réception physique. Se
    /// fonder sur la demandée rembourserait un article que le vendeur n'a jamais
    /// revu. Avant réception, `ReceivedQuantity` vaut 0 et la demandée fait foi —
    /// c'est le cas du remboursement sans retour (`RefundOnly`).
    ///
    /// ET ELLE EST BORNÉE PAR LA COMMANDE. `DeliveredQuantity −
    /// AlreadyReturnedQuantity` est le nombre d'exemplaires qu'il reste à reprendre.
    /// Une ligne du retour qui ne correspond à aucune ligne de commande est ignorée :
    /// elle ne peut pas contribuer à un plafond fondé sur ce qui a été payé.
    /// </remarks>
    private static IReadOnlyCollection<RefundableLine> LignesRemboursables(
        ReturnAggregate request,
        OrderReturnContext order,
        IReadOnlyDictionary<Guid, int> autresDossiersOuverts)
    {
        var lignes = new List<RefundableLine>();

        // La devise du dossier, déjà normalisée à l'ouverture. Reprendre celle du
        // contrat brut ferait entrer un « xof » minuscule dans le détail, que
        // `Validate` refuserait ensuite au motif d'une incohérence de devise —
        // pour un remboursement parfaitement légitime.
        var devise = request.Currency;

        foreach (var item in request.Items)
        {
            var ligne = order.Lines.FirstOrDefault(l => l.OrderItemId == item.OrderItemId);
            if (ligne is null)
            {
                continue;
            }

            var reprise = item.ReceivedQuantity > 0 ? item.ReceivedQuantity : item.RequestedQuantity;

            // `AlreadyReturnedQuantity` compte les retours ABOUTIS ; les autres
            // dossiers encore ouverts sur la même ligne, order-service ne les voit
            // pas. Sans eux, deux dossiers menés en parallèle rembourseraient
            // chacun la totalité de la ligne.
            var ouvertAilleurs = autresDossiersOuverts.TryGetValue(item.OrderItemId, out var q) ? q : 0;
            var disponible = Math.Max(0, ligne.DeliveredQuantity - ligne.AlreadyReturnedQuantity - ouvertAilleurs);
            var quantite = Math.Clamp(reprise, 0, disponible);

            if (quantite == 0)
            {
                continue;
            }

            lignes.Add(new RefundableLine(quantite, new Money(ligne.UnitPaidAmount, devise)));
        }

        return lignes;
    }
}

internal sealed class CloseReturnCommandHandler : ReturnCommandHandlerBase, ICommandHandler<CloseReturnCommand>
{
    public CloseReturnCommandHandler(IReturnRequestRepository returns, IReturnRefundUnitOfWork unitOfWork, IClock clock)
        : base(returns, unitOfWork, clock) { }

    public async Task<Result> Handle(CloseReturnCommand command, CancellationToken cancellationToken)
    {
        var request = await Returns.GetAsync(command.ReturnId, cancellationToken);
        if (request is null) return Result.Failure(Error.NotFound("return.not_found", "Retour introuvable."));
        var result = request.Close(Clock.UtcNow, command.ActorId);
        if (result.IsFailure) return result;
        await UnitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
