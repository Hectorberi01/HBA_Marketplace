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

        // ON ENREGISTRE MÊME EN CAS D'ÉCHEC. C'EST LA MOITIÉ DU CORRECTIF.
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return result;
    }
}
