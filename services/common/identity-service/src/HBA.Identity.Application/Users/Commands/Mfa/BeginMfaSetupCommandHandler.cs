using HBA.Shared.Application.Abstractions;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;
using HBA.Identity.Application.Abstractions;
using HBA.Identity.Application.Models;
using HBA.Identity.Domain.Users;

namespace HBA.Identity.Application.Users.Commands.Mfa;

/// <summary>Génère le secret TOTP, le stocke (non confirmé) et renvoie l'URI otpauth.</summary>
internal sealed class BeginMfaSetupCommandHandler : ICommandHandler<BeginMfaSetupCommand, MfaSetupResponse>
{
    private readonly IUserRepository _userRepository;
    private readonly ITotpService _totpService;
    private readonly IIdentityUnitOfWork _unitOfWork;

    public BeginMfaSetupCommandHandler(
        IUserRepository userRepository,
        ITotpService totpService,
        IIdentityUnitOfWork unitOfWork)
    {
        _userRepository = userRepository;
        _totpService = totpService;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<MfaSetupResponse>> Handle(BeginMfaSetupCommand command, CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByIdAsync(new UserId(command.UserId), cancellationToken);
        if (user is null)
        {
            return Result.Failure<MfaSetupResponse>(Error.NotFound("identity.user.not_found", $"Compte {command.UserId} introuvable."));
        }

        var secret = _totpService.GenerateSecret();

        var result = user.BeginMfaSetup(secret);
        if (result.IsFailure)
        {
            return Result.Failure<MfaSetupResponse>(result.Error);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var uri = _totpService.BuildOtpAuthUri(secret, user.Email.Value);
        return new MfaSetupResponse(secret, uri);
    }
}
