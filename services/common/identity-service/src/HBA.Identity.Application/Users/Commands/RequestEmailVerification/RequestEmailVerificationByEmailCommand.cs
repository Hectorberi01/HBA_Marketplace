using HBA.Identity.Application.Abstractions;
using HBA.Identity.Contracts.IntegrationEvents;
using HBA.Identity.Domain.Users;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;
using HBA.Shared.IntegrationEvents;
using HBA.Shared.Application.Abstractions;

namespace HBA.Identity.Application.Users.Commands.RequestEmailVerification;

/// <summary>
/// Renvoie un code de vérification à partir de l'ADRESSE, pour un compte qui n'est
/// pas connecté.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// POURQUOI PAR E-MAIL, ALORS QUE `RequestEmailVerificationCommand` EXISTE DÉJÀ.
///
/// La commande sœur prend un `UserId`, et suppose donc un appelant authentifié.
/// Or l'inscription NE CONNECTE PAS : le compte naît en attente de vérification,
/// sans jeton. L'écran « saisissez le code reçu » est le premier après
/// l'inscription, et son bouton « renvoyer le code » n'a aucune session à
/// présenter.
///
/// J'avais d'abord exposé la route authentifiée. Elle est correcte — un compte
/// connecté mais non vérifié peut s'en servir — et elle ne couvre PAS le parcours
/// pour lequel le bouton existe.
///
/// SUCCÈS SILENCIEUX SUR UNE ADRESSE INCONNUE, COMME LA RÉINITIALISATION.
///
/// Une route anonyme qui distingue « compte inconnu » de « code renvoyé » dit à
/// qui la sonde quelles adresses sont inscrites. La règle est celle de
/// `RequestPasswordResetCommand`, et pour la même raison.
///
/// CETTE COMMANDE NE RENVOIE RIEN — NI CODE, NI IDENTIFIANT.
///
/// Le BFF du monolithe renvoyait le `userId` dans sa réponse pour que l'écran
/// suivant l'utilise. C'est un oracle : l'obtenir prouve que le compte existe.
/// Le code part par e-mail, et l'identifiant reste chez qui l'a déjà — il est
/// rendu par l'inscription.
///
/// CORPS DUPLIQUÉ AVEC LA COMMANDE SŒUR, DÉLIBÉRÉMENT.
///
/// Les quinze lignes de génération sont recopiées plutôt que réémises par
/// `ISender` depuis ce gestionnaire. Un gestionnaire qui en appelle un autre
/// rend le chemin d'exécution illisible dans les traces, et fait dépendre une
/// règle de sécurité d'un dispatch dynamique. La duplication se voit ; le
/// couplage caché, non.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed record RequestEmailVerificationByEmailCommand(string Email) : ICommand;

internal sealed class RequestEmailVerificationByEmailCommandHandler
    : ICommandHandler<RequestEmailVerificationByEmailCommand>
{
    private readonly IUserRepository _userRepository;
    private readonly ISecureTokenGenerator _tokenGenerator;
    private readonly IAuthTokenSettings _tokenSettings;
    private readonly IIntegrationEventPublisher _publisher;
    private readonly IIdentityUnitOfWork _unitOfWork;

    /// <summary>
    /// LE CODE NE TRAVERSE PLUS LE BUS EN CLAIR.
    ///
    /// Il partait tel quel dans l'événement, donc dans
    /// `identity.outbox_messages.Content` — table jamais purgée — puis sur un topic
    /// Kafka retenu sept jours. Une lecture de l'un ou l'autre valait prise de
    /// compte. Il est désormais chiffré ici, et déchiffré par le seul service qui
    /// doit l'envoyer.
    /// </summary>
    private readonly ISecretProtector _protecteur;

    public RequestEmailVerificationByEmailCommandHandler(
        IUserRepository userRepository,
        ISecureTokenGenerator tokenGenerator,
        IAuthTokenSettings tokenSettings,
        IIntegrationEventPublisher publisher,
        IIdentityUnitOfWork unitOfWork,
        ISecretProtector protecteur)
    {
        _userRepository = userRepository;
        _tokenGenerator = tokenGenerator;
        _tokenSettings = tokenSettings;
        _publisher = publisher;
        _unitOfWork = unitOfWork;
        _protecteur = protecteur;
    }

    public async Task<Result> Handle(
        RequestEmailVerificationByEmailCommand command, CancellationToken cancellationToken)
    {
        var email = command.Email.Trim().ToLowerInvariant();
        var user = await _userRepository.GetByEmailAsync(email, cancellationToken);

        if (user is null)
        {
            // Anti-énumération : exactement la même réponse qu'un envoi réussi.
            //
            // Ne pas ajouter de délai « pour égaliser les temps ». Le chemin
            // inconnu est plus court, et c'est le limiteur du groupe /auth qui
            // rend la mesure impraticable — pas une temporisation.
            return Result.Success();
        }

        // Un compte déjà vérifié n'a pas de code à recevoir. Silencieux là encore :
        // « cette adresse est déjà vérifiée » renseignerait tout autant.
        if (user.EmailVerified)
        {
            return Result.Success();
        }

        var (code, codeHash) = _tokenGenerator.GenerateNumericCode();
        var expiresOnUtc = DateTime.UtcNow.Add(_tokenSettings.EmailVerificationLifetime);

        var begin = user.BeginEmailVerification(codeHash, expiresOnUtc);

        if (begin.IsFailure)
        {
            // Refus du domaine — par exemple une demande trop rapprochée. Il ne
            // remonte pas jusqu'au client : la route répond 204 dans tous les cas.
            return Result.Success();
        }

        await _publisher.PublishAsync(
            new EmailVerificationRequestedIntegrationEvent
            {
                UserId = user.Id.Value,
                Email = user.Email.Value,
                FirstName = user.FirstName,
                ProtectedVerificationToken = _protecteur.Protect(code)
            },
            cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
