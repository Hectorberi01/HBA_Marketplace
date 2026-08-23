using HBA.Identity.Application.Abstractions;
using HBA.Identity.Domain.Users;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;

namespace HBA.Identity.Application.Users.Commands.ConfirmEmail;

/// <summary>
/// Vérifie une adresse à partir de l'ADRESSE et du code à six chiffres.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// DEUX CHEMINS DE VÉRIFICATION, ET ILS NE SONT PAS REDONDANTS.
///
/// `ConfirmEmailCommand` prend un `UserId`. C'est le bon contrat pour le LIEN
/// cliquable reçu par e-mail : le lien porte l'identifiant, personne ne le
/// saisit.
///
/// Cette commande-ci sert le SECOND parcours, celui de l'application : l'écran
/// « saisissez le code reçu ». L'utilisateur y arrive après une inscription, ou
/// après avoir tenté de se connecter avec un compte non vérifié — et dans ce
/// dernier cas il n'a PAS d'identifiant, seulement l'adresse qu'il vient de
/// taper.
///
/// Le BFF du monolithe contournait le problème en renvoyant l'`userId` dans la
/// réponse de « renvoyer le code ». C'était un oracle sur une route anonyme :
/// obtenir un identifiant prouvait l'existence du compte. En le supprimant, il
/// fallait bien que l'autre bout accepte une adresse.
///
/// ICI, L'ÉCHEC EST DIT — CONTRAIREMENT AUX ROUTES ANONYMES VOISINES.
///
/// `password/forgot` et `email/resend` taisent tout, parce qu'ils n'exigent rien
/// que l'attaquant ne connaisse déjà : une adresse. Celle-ci exige un CODE à six
/// chiffres, valable quelques minutes, envoyé sur la boîte. Un échec ne
/// renseigne donc pas sur l'existence du compte — il dit seulement que ce
/// couple-là ne va pas. Le taire empêcherait l'utilisateur légitime de
/// distinguer un code périmé d'une faute de frappe.
///
/// Le nombre d'essais est déjà borné par le domaine, et le groupe `/auth` par
/// son limiteur.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed record ConfirmEmailByEmailCommand(string Email, string Code) : ICommand;

internal sealed class ConfirmEmailByEmailCommandHandler : ICommandHandler<ConfirmEmailByEmailCommand>
{
    private readonly IUserRepository _userRepository;
    private readonly ISecureTokenGenerator _tokenGenerator;
    private readonly IIdentityUnitOfWork _unitOfWork;

    public ConfirmEmailByEmailCommandHandler(
        IUserRepository userRepository,
        ISecureTokenGenerator tokenGenerator,
        IIdentityUnitOfWork unitOfWork)
    {
        _userRepository = userRepository;
        _tokenGenerator = tokenGenerator;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(ConfirmEmailByEmailCommand command, CancellationToken cancellationToken)
    {
        var email = command.Email.Trim().ToLowerInvariant();
        var user = await _userRepository.GetByEmailAsync(email, cancellationToken);

        // MÊME ERREUR POUR « COMPTE INCONNU » ET « CODE FAUX ».
        //
        // Deux messages distincts rendraient à nouveau la route bavarde : il
        // suffirait d'un code au hasard pour savoir si l'adresse est inscrite.
        var invalid = Error.Validation(
            "identity.email.invalid_code",
            "Ce code n'est pas valide, ou il a expiré.");

        if (user is null)
        {
            return Result.Failure(invalid);
        }

        var codeHash = _tokenGenerator.Hash(command.Code);
        // `nowUtc` est un PARAMÈTRE du domaine, pas un `DateTime.UtcNow` lu à
        // l'intérieur : c'est ce qui rend l'expiration testable.
        var result = user.ConfirmEmail(codeHash, DateTime.UtcNow);

        if (result.IsFailure)
        {
            // On enregistre quand même : le domaine compte les tentatives, et ce
            // compteur est ce qui borne la force brute. Ne pas sauvegarder le
            // remettrait à zéro à chaque essai.
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Result.Failure(invalid);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
