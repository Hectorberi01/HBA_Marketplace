namespace HBA.Deliveries.Domain.Partners;

/// <summary>Accès aux partenaires. L'implémentation vit en Infrastructure.</summary>
public interface IPartnerRepository
{
    Task<Partner?> GetByIdAsync(PartnerId id, CancellationToken cancellationToken = default);

    /// <summary>Retrouve le partenaire portant une clé active dont le préfixe correspond.</summary>
    Task<Partner?> FindByApiKeyPrefixAsync(string prefix, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Partner>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Nombre de livraisons créées aujourd'hui par ce partenaire.</summary>
    Task<int> CountDeliveriesTodayAsync(PartnerId id, CancellationToken cancellationToken = default);

    Task AddAsync(Partner partner, CancellationToken cancellationToken = default);
}
