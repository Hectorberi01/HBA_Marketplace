using HBA.Shared.Application.Abstractions;
using Microsoft.Extensions.Hosting;

namespace HBA.Shared.Infrastructure.Security;

/// <summary>
/// LA CLE DE PROTECTION DES SECRETS EST CONSTRUITE AU DEMARRAGE, PAS A LA PREMIERE
/// INSCRIPTION.
/// </summary>
internal sealed class VerificationDesSecretsAuDemarrage : IHostedService
{
    private readonly ISecretProtector _protecteur;

    /// <summary>
    /// L'INJECTION EST LE CONTROLE. Le conteneur construit `ISecretProtector` pour
    /// fabriquer cette classe : si la fabrique leve, elle leve ici, au demarrage,
    /// et non a la premiere requete.
    /// </summary>
    public VerificationDesSecretsAuDemarrage(ISecretProtector protecteur)
        => _protecteur = protecteur;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Le protecteur est deja construit — c'est le constructeur ci-dessus qui
        // l'a exige.
        _ = _protecteur;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
