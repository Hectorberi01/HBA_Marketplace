using FluentValidation;

namespace HBA.Catalog.Application.Products.Commands.AddProductMedia;

public sealed class AddProductMediaCommandValidator : AbstractValidator<AddProductMediaCommand>
{
    public AddProductMediaCommandValidator()
    {
        RuleFor(c => c.ProductId).NotEmpty();

        // SANS CETTE RÈGLE, UN `Guid.Empty` TRAVERSE ET N'EST ARRÊTÉ QUE PAR
        // L'AGRÉGAT. La demande échoue alors avec une erreur de domaine au lieu
        // d'une 400 de validation — même refus, message incohérent avec le reste.
        RuleFor(c => c.MediaId).NotEmpty();

        // La règle sur `Url` a disparu avec le champ : l'adresse n'est plus fournie
        // par l'appelant, elle est lue sur le média. Valider une URL du client
        // n'aurait de toute façon jamais rien prouvé — une adresse bien formée peut
        // pointer où elle veut.
        RuleFor(c => c.Type)
            .Must(t => t is "Image" or "Video")
            .WithMessage("Type de média invalide (Image ou Video).");
    }
}
