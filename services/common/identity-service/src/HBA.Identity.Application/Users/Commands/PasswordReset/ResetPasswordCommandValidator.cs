using FluentValidation;

namespace HBA.Identity.Application.Users.Commands.PasswordReset;

/// <summary>VALIDATION D'ENTRÉE DE LA RÉINITIALISATION.</summary>
public sealed class ResetPasswordCommandValidator : AbstractValidator<ResetPasswordCommand>
{
    public ResetPasswordCommandValidator()
    {
        RuleFor(c => c.Email).NotEmpty().EmailAddress();
        RuleFor(c => c.Token).NotEmpty();
        RuleFor(c => c.NewPassword).NotEmpty().MinimumLength(8).MaximumLength(128);
    }
}
