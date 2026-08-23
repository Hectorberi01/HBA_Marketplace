using HBA.Shared.IntegrationEvents;

namespace HBA.Returns.Contracts.IntegrationEvents;

/// <summary>
/// Un remboursement a été VALIDÉ, mais l'argent n'est pas encore parti.
///
/// Consommé par Notifications : on rassure l'acheteur (« votre remboursement est
/// accepté, le versement est en cours »). C'est ce message qui évite qu'il ouvre un
/// litige pendant les quelques heures où l'opération attend d'être exécutée.
///
/// N'engage AUCUNE écriture comptable.
/// </summary>
public sealed record ReturnRefundApprovedIntegrationEvent : IntegrationEvent
{
    public required Guid ReturnRequestId { get; init; }
    public required Guid OrderId { get; init; }
    public required Guid BuyerId { get; init; }
    public required Guid SellerId { get; init; }
    public required decimal RefundAmount { get; init; }
    public required string Currency { get; init; }
}

/// <summary>
/// L'argent a RÉELLEMENT été versé à l'acheteur (référence FedaPay à l'appui).
///
/// Consommé par Settlement (contre-passation du gain vendeur + restitution de la
/// commission) et Notifications (acheteur et vendeur).
///
/// Cet événement était déjà publié — et PERSONNE ne l'écoutait. C'est ce silence
/// qui faisait qu'un remboursement ne remboursait rien, ne débitait personne, et
/// n'informait qui que ce soit.
/// </summary>
public sealed record ReturnRefundedIntegrationEvent : IntegrationEvent
{
    public required Guid ReturnRequestId { get; init; }
    public required Guid OrderId { get; init; }
    public required Guid BuyerId { get; init; }
    public required Guid SellerId { get; init; }
    public required decimal RefundAmount { get; init; }
    public required string Currency { get; init; }

    /// <summary>Référence du versement chez FedaPay : la preuve que l'argent est parti.</summary>
    public required string RefundReference { get; init; }

    /// <summary>
    /// Les lignes de commande que ce dossier a effectivement reprises, avec la
    /// quantité retenue — la reçue quand elle existe, la demandée sinon.
    ///
    /// ═════════════════════════════════════════════════════════════════════════
    /// C'EST CE QUI MANQUAIT POUR QU'ORDER-SERVICE PUISSE DIRE LA VÉRITÉ.
    ///
    /// `OrderingModuleApi.GetOrderReturnContextAsync` codait `AlreadyReturnedQuantity: 0`
    /// en dur (ISSUE-014) : rien ne lui apprenait jamais qu'un article était déjà
    /// revenu. Un même exemplaire pouvait donc être retourné et remboursé autant
    /// de fois qu'on ouvrait de dossiers. Order-service ne peut pas le savoir seul
    /// — il ne possède pas les retours — et personne ne le lui disait.
    ///
    /// LA QUANTITÉ EST CUMULÉE POUR LE DOSSIER, PAS INCRÉMENTALE.
    ///
    /// Le consommateur RAPPROCHE (il ne cumule pas) : il retient, par dossier et
    /// par ligne, la quantité la plus élevée qu'il ait vue. Deux versements
    /// partiels sur le même dossier ne comptent donc pas la marchandise deux fois,
    /// et un message rejoué ne compte rien du tout.
    ///
    /// FACULTATIF PAR CONSTRUCTION (décision D32). Un producteur antérieur qui
    /// ne le remplit pas laisse la collection vide : le consommateur n'impute
    /// alors aucune quantité — l'ancien comportement, pas une exception.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    public IReadOnlyCollection<ReturnedOrderLine> Lines { get; init; } = [];

    /// <summary>
    /// Ce que CE dossier a remboursé au total, versements cumulés, à l'instant où
    /// le message part.
    ///
    /// <para>
    /// Cumulé, pour la même raison que <see cref="Lines"/> : order-service pose
    /// la valeur du dossier au lieu de l'additionner, et somme ensuite les
    /// dossiers. Un rejeu ne double alors aucun montant. Zéro — la valeur d'un
    /// producteur antérieur — signifie « inconnu » : le consommateur retombe sur
    /// <see cref="RefundAmount"/>, qui est le montant de CE versement.
    /// </para>
    /// </summary>
    public decimal ReturnTotalRefundedAmount { get; init; }
}

/// <summary>
/// Une ligne de commande reprise par un dossier de retour.
///
/// <para>
/// `OrderItemId` est l'identifiant de la ligne CHEZ ORDER-SERVICE — celui que le
/// dossier a recopié à son ouverture depuis `OrderReturnLineContext`. C'est la
/// seule clé qui permette le rapprochement ; le produit ne suffit pas, une même
/// référence pouvant figurer sur deux lignes.
/// </para>
/// </summary>
public sealed record ReturnedOrderLine
{
    public required Guid OrderItemId { get; init; }

    /// <summary>Quantité reprise, cumulée pour le dossier.</summary>
    public required int Quantity { get; init; }
}
