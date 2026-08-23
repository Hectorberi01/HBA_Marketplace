using HBA.Shared.Application.Abstractions;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;
using HBA.Identity.Application.Abstractions;
using HBA.Identity.Domain.Users;

namespace HBA.Identity.Application.Users.Commands.Mfa;

/// <summary>Vérifie le code TOTP courant puis désactive la MFA.</summary>
internal sealed class DisableMfaCommandHandler : ICommandHandler<DisableMfaCommand>
{
    private readonly IUserRepository _userRepository;
    private readonly ITotpService _totpService;
    private readonly IIdentityUnitOfWork _unitOfWork;

    public DisableMfaCommandHandler(
        IUserRepository userRepository,
        ITotpService totpService,
        IIdentityUnitOfWork unitOfWork)
    {
        _userRepository = userRepository;
        _totpService = totpService;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(DisableMfaCommand command, CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByIdAsync(new UserId(command.UserId), cancellationToken);
        if (user is null)
        {
            return Result.Failure(Error.NotFound("identity.user.not_found", $"Compte {command.UserId} introuvable."));
        }

        if (!user.MfaEnabled)
        {
            return Result.Success();
        }

        if (user.MfaSecret is null || !_totpService.VerifyCode(user.MfaSecret, command.Code))
        {
            return Result.Failure(Error.Unauthorized("identity.auth.mfa_invalid", "Code de double authentification invalide."));
        }

        user.DisableMfa();
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
