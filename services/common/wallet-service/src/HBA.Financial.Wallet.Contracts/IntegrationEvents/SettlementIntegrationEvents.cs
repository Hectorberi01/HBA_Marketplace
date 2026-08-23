using HBA.Shared.IntegrationEvents;

namespace HBA.Financial.Wallet.Contracts.IntegrationEvents;

/// <summary>Un reversement a été versé à un vendeur. Consommé par Notifications / comptabilité.</summary>
[HbaEvent("payout.completed", Version = 1, AggregateType = "Payout")]
public sealed record PayoutPaidIntegrationEvent : IntegrationEvent
{
    public required Guid BatchId { get; init; }
    public required Guid PayoutId { get; init; }
    public required Guid SellerId { get; init; }
    public required decimal NetAmount { get; init; }
    public required string Currency { get; init; }
}

/// <summary>
/// Le gain d'une course est ARRIVÉ au portefeuille du livreur.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// POURQUOI UN ÉVÉNEMENT DE PLUS, PLUTÔT QUE D'ÉCOUTER LA FIN DE COURSE.
///
/// `DeliveryCompletedIntegrationEvent` porte déjà le montant : on aurait pu
/// notifier le livreur depuis lui. Mais ce montant n'est qu'un CALCUL — rien n'y
/// dit qu'il a été écrit. Une course sans prix, un livreur inconnu, une devise
/// incompatible : le crédit est refusé et le message aurait quand même annoncé
/// un gain. Un solde annoncé et absent est pire qu'un solde silencieux, parce
/// qu'il envoie le livreur au support avec raison.
///
/// Ce fait-ci n'est publié que par la ligne qui écrit au grand livre, et dans la
/// même transaction : il ne peut pas mentir.
///
/// PAS DE `UserId` — c'est le `DriverId` du référentiel logistique.
///
/// Le portefeuille est indexé sur le livreur, pas sur le compte. La traduction se
/// fait chez le consommateur, par `IDeliveryModuleApi.GetDriverAccountAsync`,
/// comme pour la proposition de course.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
/// <remarks>
/// NOM DE CLASSE ET NOM MÉTIER DIVERGENT, ET C'EST VOULU.
///
/// Le §10.13 nomme `wallet.credited` tout crédit de portefeuille, sans distinguer
/// le bénéficiaire. La classe, elle, dit ce qu'elle transporte : le gain d'un
/// livreur. Renommer la classe perdrait cette précision dans le code ; renommer
/// l'événement casserait le contrat que notification et comptabilité attendent.
/// </remarks>
[HbaEvent("wallet.credited", Version = 1, AggregateType = "Wallet")]
public sealed record DriverEarningCreditedIntegrationEvent : IntegrationEvent
{
    public required Guid DriverId { get; init; }
    public required Guid DeliveryId { get; init; }
    public required decimal Amount { get; init; }
    public required string Currency { get; init; }
}
