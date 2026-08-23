using FluentValidation;

namespace HBA.Identity.Application.Users.Commands.Reauthenticate;

/// <summary>Validation d'entrée de la réauthentification.</summary>
public sealed class ReauthenticateCommandValidator : AbstractValidator<ReauthenticateCommand>
{
    public ReauthenticateCommandValidator()
    {
        RuleFor(c => c.UserId).NotEmpty();

        // AUCUNE RÈGLE DE COMPLEXITÉ ICI, ET C'EST VOLONTAIRE.
        //
        // On VÉRIFIE un mot de passe existant, on n'en crée pas. Imposer une
        // longueur minimale refuserait, avec un message de validation, les comptes
        // dont le mot de passe est antérieur à la règle actuelle — et le message
        // dirait à qui tâtonne que la valeur essayée était trop courte pour être
        // la bonne. La borne haute reste, elle : elle protège le hachage d'une
        // entrée démesurée.
        RuleFor(c => c.Password).NotEmpty().MaximumLength(128);
    }
}
