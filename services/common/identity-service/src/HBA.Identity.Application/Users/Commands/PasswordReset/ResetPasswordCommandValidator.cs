using FluentValidation;

namespace HBA.Identity.Application.Users.Commands.PasswordReset;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// VALIDATION D'ENTRÉE DE LA RÉINITIALISATION.
///
/// CE VALIDATEUR N'EXISTAIT PAS, ET C'ÉTAIT LE TROU LE PLUS DISCRET DU MODULE.
///
/// L'inscription exige huit caractères. Le changement de mot de passe exige huit
/// caractères. La réinitialisation — le troisième chemin, celui qu'emprunte
/// quelqu'un qui a perdu son accès — n'exigeait RIEN : un mot de passe d'un
/// caractère passait.
///
/// Personne ne l'aurait vu en lisant le handler, qui appelle bien
/// <c>user.ResetPassword</c> comme il faut. La règle manquait à l'étage du
/// dessus, là où l'absence d'un fichier ne se remarque pas.
///
/// Les règles sont IDENTIQUES à celles du changement de mot de passe. Deux
/// chemins qui posent le même secret ne peuvent pas avoir deux exigences
/// différentes : la plus faible devient la vraie.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class ResetPasswordCommandValidator : AbstractValidator<ResetPasswordCommand>
{
    public ResetPasswordCommandValidator()
    {
        RuleFor(c => c.Email).NotEmpty().EmailAddress();
        RuleFor(c => c.Token).NotEmpty();
        RuleFor(c => c.NewPassword).NotEmpty().MinimumLength(8).MaximumLength(128);
    }
}
