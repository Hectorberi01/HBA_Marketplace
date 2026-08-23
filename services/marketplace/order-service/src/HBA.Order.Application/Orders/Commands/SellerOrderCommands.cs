using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;
using HBA.Orders.Application.Abstractions;
using HBA.Orders.Domain.Orders.SellerOrders;

namespace HBA.Orders.Application.Orders.Commands;

// ═════════════════════════════════════════════════════════════════════════════
// LES CINQ GESTES QUE `ORDER_MANAGER` ATTENDAIT (ISSUE-026).
//
// CINQ PERMISSIONS EXISTAIENT, ÉTAIENT ATTRIBUÉES, ET NE GARDAIENT AUCUNE
// ROUTE.
//
// `ORDER_CONFIRM`, `ORDER_REJECT`, `ORDER_MARK_PREPARING`, `ORDER_MARK_READY` et
// `ORDER_CANCEL` étaient déclarées dans `MerchantPermissions`, distribuées au
// rôle `ORDER_MANAGER`, affichées dans la console d'équipe — et il n'existait
// nulle part une seule route qui les exige. Le rôle promettait une autorité
// qu'il n'exerçait pas, et le parcours vendeur s'arrêtait à la RÉCEPTION de la
// commande.
//
// Ce n'était pas un oubli de câblage : il n'y avait rien à faire changer d'état.
// C'est `SellerOrder` qui donne l'objet, et ces cinq commandes qui donnent les
// gestes.
//
// CHAQUE COMMANDE PORTE LE `SellerId`, ET CE N'EST PAS REDONDANT.
//
// La part est désignée par le COUPLE (commande, vendeur), jamais par un
// `SellerOrderId` seul. Deux raisons : le vendeur ne connaît pas cet
// identifiant avant d'avoir lu son carnet, et surtout le couple rend la commande
// intrinsèquement cadrée — même appelée depuis un chemin qui aurait oublié sa
// garde d'appartenance, elle ne peut toucher que la part de CE vendeur-là. La
// garde HTTP reste la première ligne (voir `OrderEndpoints`), celle-ci est ce
// qui reste vrai si on l'oublie.
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>Le vendeur s'engage à honorer sa part. Permission `ORDER_CONFIRM`.</summary>
public sealed record ConfirmSellerOrderCommand(Guid OrderId, Guid SellerId) : ICommand;

/// <summary>
/// Le vendeur refuse sa part avant de s'être engagé. Permission `ORDER_REJECT`.
///
/// LE MOTIF EST OBLIGATOIRE : c'est la seule trace de pourquoi une commande
/// PAYÉE ne sera pas honorée. L'agrégat le refuse vide, la route ne peut donc pas
/// l'oublier.
/// </summary>
public sealed record RejectSellerOrderCommand(Guid OrderId, Guid SellerId, string Reason) : ICommand;

/// <summary>Le colis se monte. Permission `ORDER_MARK_PREPARING`.</summary>
public sealed record MarkSellerOrderPreparingCommand(Guid OrderId, Guid SellerId) : ICommand;

/// <summary>Le colis attend le livreur. Permission `ORDER_MARK_READY`.</summary>
public sealed record MarkSellerOrderReadyCommand(Guid OrderId, Guid SellerId) : ICommand;

/// <summary>
/// Le vendeur se dédit après s'être engagé. Permission `ORDER_CANCEL`, la seule
/// des cinq classée SENSIBLE — se dédire après avoir fait attendre le client
/// n'est pas le même geste que refuser tout de suite.
/// </summary>
public sealed record CancelSellerOrderCommand(Guid OrderId, Guid SellerId, string Reason) : ICommand;

/// <summary>
/// Les cinq transitions de la part vendeur.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// AUCUN APPEL À INVENTORY NI À PAYMENTS DANS CE FICHIER, ET C'EST DÉLIBÉRÉ.
///
/// Un refus ou une annulation de vendeur DEVRAIT libérer du stock et rembourser
/// une part. Ni l'un ni l'autre n'appartient à order-service : le stock a été
/// SOLDÉ à la confirmation (il n'y a plus de réservation à rendre, il y a de la
/// marchandise à remettre en rayon), et le remboursement appartient à
/// financial-service.
///
/// L'agrégat lève donc `SellerOrderRefusedDomainEvent`, qui devient
/// `SellerOrderRefusedIntegrationEvent`. Cet événement N'A AUCUN CONSOMMATEUR
/// aujourd'hui : un refus vendeur ne rembourse encore personne. C'est écrit ici,
/// sur l'agrégat et sur l'événement, plutôt que laissé à découvrir — une lacune
/// nommée vaut mieux qu'un silence.
///
/// UN SEUL GESTIONNAIRE POUR LES CINQ, COMME `OrderReviewCommandHandler`.
///
/// Les cinq font littéralement la même chose : lire la part par (commande,
/// vendeur), appliquer UNE transition, persister. Cinq classes n'auraient
/// multiplié que le préambule — et c'est dans le préambule recopié quatre fois
/// qu'on oublie une ligne.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
internal sealed class SellerOrderCommandHandler
    : ICommandHandler<ConfirmSellerOrderCommand>,
      ICommandHandler<RejectSellerOrderCommand>,
      ICommandHandler<MarkSellerOrderPreparingCommand>,
      ICommandHandler<MarkSellerOrderReadyCommand>,
      ICommandHandler<CancelSellerOrderCommand>
{
    private readonly ISellerOrderRepository _sellerOrders;
    private readonly IOrderingUnitOfWork _unitOfWork;

    public SellerOrderCommandHandler(ISellerOrderRepository sellerOrders, IOrderingUnitOfWork unitOfWork)
    {
        _sellerOrders = sellerOrders;
        _unitOfWork = unitOfWork;
    }

    public Task<Result> Handle(ConfirmSellerOrderCommand command, CancellationToken cancellationToken)
        => MuterAsync(
            command.OrderId, command.SellerId,
            (part, maintenant) => part.Confirm(maintenant),
            cancellationToken);

    public Task<Result> Handle(RejectSellerOrderCommand command, CancellationToken cancellationToken)
        => MuterAsync(
            command.OrderId, command.SellerId,
            // AUCUN MOTIF PAR DÉFAUT, CONTRAIREMENT À L'ARBITRAGE.
            //
            // `OrderReviewCommandHandler` substitue « Commande devenue
            // inexécutable. » à un motif vide, parce que la décision y est prise
            // par l'exploitation et que le contexte est déjà dans la file. Ici la
            // décision est prise par un VENDEUR contre un client qui a payé :
            // inventer un motif à sa place fabriquerait une justification qu'il
            // n'a pas donnée. L'agrégat refuse le vide, et la route rend un 400.
            (part, maintenant) => part.Reject(command.Reason, maintenant),
            cancellationToken);

    public Task<Result> Handle(MarkSellerOrderPreparingCommand command, CancellationToken cancellationToken)
        => MuterAsync(
            command.OrderId, command.SellerId,
            (part, maintenant) => part.MarkPreparing(maintenant),
            cancellationToken);

    public Task<Result> Handle(MarkSellerOrderReadyCommand command, CancellationToken cancellationToken)
        => MuterAsync(
            command.OrderId, command.SellerId,
            (part, maintenant) => part.MarkReadyForPickup(maintenant),
            cancellationToken);

    public Task<Result> Handle(CancelSellerOrderCommand command, CancellationToken cancellationToken)
        => MuterAsync(
            command.OrderId, command.SellerId,
            (part, maintenant) => part.Cancel(command.Reason, maintenant),
            cancellationToken);

    private async Task<Result> MuterAsync(
        Guid orderId,
        Guid sellerId,
        Func<SellerOrder, DateTime, Result> transition,
        CancellationToken cancellationToken)
    {
        var part = await _sellerOrders.FindAsync(orderId, sellerId, cancellationToken);

        // « INTROUVABLE » COUVRE TROIS CAS BIEN DIFFÉRENTS, ET C'EST VOULU.
        //
        // La commande n'existe pas ; elle existe mais ce vendeur n'y vend rien ;
        // elle existe et il y vend, mais elle a été CONFIRMÉE avant l'arrivée de
        // cet agrégat et n'a donc pas de part (voir la migration
        // `CommandeParVendeur`). Les distinguer dirait à un vendeur si telle
        // commande existe chez un concurrent — et le troisième cas n'est pas une
        // erreur du vendeur, c'est une limite du rattrapage, qu'un message
        // technique n'aiderait pas à franchir.
        if (part is null)
        {
            return Result.Failure(Error.NotFound(
                "ordering.seller_order.not_found", "Commande vendeur introuvable."));
        }

        var resultat = transition(part, DateTime.UtcNow);
        if (resultat.IsFailure)
        {
            return resultat;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
