using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;
using HBA.Identity.Application.Abstractions;
using HBA.Identity.Domain.Users;

namespace HBA.Identity.Application.Users.Commands.PasswordReset;

internal sealed class ResetPasswordCommandHandler : ICommandHandler<ResetPasswordCommand>
{
    private readonly IUserRepository _userRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ISecureTokenGenerator _tokenGenerator;
    private readonly IIdentityUnitOfWork _unitOfWork;

    public ResetPasswordCommandHandler(
        IUserRepository userRepository,
        IPasswordHasher passwordHasher,
        ISecureTokenGenerator tokenGenerator,
        IIdentityUnitOfWork unitOfWork)
    {
        _userRepository = userRepository;
        _passwordHasher = passwordHasher;
        _tokenGenerator = tokenGenerator;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(ResetPasswordCommand command, CancellationToken cancellationToken)
    {
        var email = command.Email.Trim().ToLowerInvariant();
        var user = await _userRepository.GetByEmailAsync(email, cancellationToken);
        if (user is null)
        {
            // Message générique (pas d'énumération de comptes).
            return Result.Failure(Error.Validation("identity.user.reset_invalid", "Lien de réinitialisation invalide."));
        }

        var providedHash = _tokenGenerator.Hash(command.Token);
        var result = user.ResetPassword(providedHash, _passwordHasher.Hash(command.NewPassword), DateTime.UtcNow);

        // ═════════════════════════════════════════════════════════════════════
        // ON ENREGISTRE MÊME EN CAS D'ÉCHEC. C'EST LA MOITIÉ DU CORRECTIF.
        //
        // Un essai raté incrémente le compteur sur l'agrégat. La version
        // précédente sortait ici avec `return result` avant tout SaveChanges :
        // le compteur montait en mémoire, l'entité était jetée avec le scope de
        // la requête, et l'essai suivant repartait de zéro.
        //
        // Le plafond de cinq essais n'aurait alors JAMAIS été atteint. Le code
        // aurait eu l'air correct — la règle est dans le domaine, elle est
        // testée — et l'attaque serait restée exactement aussi praticable
        // qu'avant. Un compteur qu'on n'enregistre pas est un compteur décoratif.
        // ═════════════════════════════════════════════════════════════════════
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return result;
    }
}
