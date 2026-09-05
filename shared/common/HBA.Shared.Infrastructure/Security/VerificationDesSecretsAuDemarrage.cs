using HBA.Shared.Application.Abstractions;
using Microsoft.Extensions.Hosting;

namespace HBA.Shared.Infrastructure.Security;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LA CLE DE PROTECTION DES SECRETS EST CONSTRUITE AU DEMARRAGE, PAS A LA
///     PREMIERE INSCRIPTION.
///
/// CE QUI ETAIT CASSE — ET LE COMMENTAIRE DE `DependencyInjection` LE DISAIT
///     DEJA, SANS QUE PERSONNE N'EN TIRE LA CONSEQUENCE.
///
/// `ISecretProtector` est un singleton enregistre par lambda : le conteneur ne
/// le construit qu'a la PREMIERE resolution. Or les seuls consommateurs sont
/// l'inscription, la demande de code de verification et la reinitialisation de
/// mot de passe. Un service deploye avec une cle absente, mal encodee ou de
/// mauvaise taille demarrait donc NORMALEMENT, passait `/health/ready`, servait
/// son trafic — et rendait un 500 opaque au premier utilisateur qui creait un
/// compte, des semaines apres la mise en production.
///
/// C'est exactement ce qui s'est produit : `POST /api/v1/auth/register` en 500,
/// message volontairement muet, pendant que `/login` fonctionnait — parce que
/// la connexion ne resout jamais le protecteur.
///
/// CE QUE CE CONTROLE FAIT. Il resout le protecteur une fois, au demarrage. Une
/// cle invalide arrete l'hote : le conteneur ne passe pas ses sondes, le
/// deploiement echoue, et le message dit lequel des trois defauts c'est.
///
/// CE QU'IL NE COUVRE PAS.
///
///   . Une cle VALIDE MAIS DIFFERENTE de celle de notification-service. Les
///     deux cotes demarrent, les codes partent chiffres, et le destinataire ne
///     sait pas les relire. Ce cas se voit en lettre morte a l'autre bout.
///   . Les autres secrets. Cette classe ne regarde que celui-la ; les chaines de
///     connexion, la cle de signature JWT et la cle d'API interne ont leurs
///     propres controles.
///
/// AUCUNE VALEUR N'EST LUE NI JOURNALISEE ICI. On construit, ou on echoue.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
/// <remarks>
/// `IHostedService` et NON `BackgroundService` : une exception levee dans
/// `StartAsync` remonte hors de `RunAsync` et empeche l'hote de demarrer. Un
/// `BackgroundService` la porterait dans une tache, ou son sort dependrait de
/// `BackgroundServiceExceptionBehavior` — donc d'un reglage, alors qu'on veut un
/// refus inconditionnel.
///
/// Enregistre EN PREMIER par `AddHbaInfrastructure` : les services hebergés
/// demarrent dans l'ordre d'enregistrement, et echouer avant que les
/// consommateurs Kafka ne se connectent evite un demarrage a moitie fait.
/// </remarks>
internal sealed class VerificationDesSecretsAuDemarrage : IHostedService
{
    private readonly ISecretProtector _protecteur;

    /// <summary>
    /// L'INJECTION EST LE CONTROLE. Le conteneur construit `ISecretProtector`
    /// pour fabriquer cette classe : si la fabrique leve, elle leve ici, au
    /// demarrage, et non a la premiere requete.
    /// </summary>
    public VerificationDesSecretsAuDemarrage(ISecretProtector protecteur)
        => _protecteur = protecteur;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Le protecteur est deja construit — c'est le constructeur ci-dessus qui
        // l'a exige. On garde une reference pour que le compilateur ne considere
        // pas le champ comme inutilise, sans rien executer de plus.
        _ = _protecteur;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
