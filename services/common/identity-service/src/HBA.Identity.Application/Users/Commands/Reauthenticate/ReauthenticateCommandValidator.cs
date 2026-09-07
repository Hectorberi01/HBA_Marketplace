using FluentValidation;

namespace HBA.Identity.Application.Users.Commands.Reauthenticate;

/// <summary>Validation d'entrée de la réauthentification.</summary>
public sealed class ReauthenticateCommandValidator : AbstractValidator<ReauthenticateCommand>
{
    public ReauthenticateCommandValidator()
    {
        RuleFor(c => c.UserId).NotEmpty();

        // AUCUNE RÈGLE DE COMPLEXITÉ ICI, ET C'EST VOLONTAIRE.
        RuleFor(c => c.Password).NotEmpty().MaximumLength(128);
    }
}
