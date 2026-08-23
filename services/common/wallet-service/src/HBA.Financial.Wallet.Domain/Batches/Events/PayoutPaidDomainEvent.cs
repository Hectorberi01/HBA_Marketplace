using HBA.Shared.Domain.Events;

namespace HBA.Financial.Wallet.Domain.Batches.Events;

/// <summary>Un reversement a été versé à un vendeur (consommé par Notifications / compta).</summary>
public sealed record PayoutPaidDomainEvent(Guid BatchId, Guid PayoutId, Guid SellerId, decimal NetAmount, string Currency) : DomainEvent;
