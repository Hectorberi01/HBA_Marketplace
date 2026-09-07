using FluentValidation;

namespace HBA.Catalog.Application.Products.Commands.ChangeProductStatus;

/// <summary>
/// Vérifie que le statut cible est l'une des valeurs que le VENDEUR peut demander.
/// </summary>
public sealed class ChangeProductStatusCommandValidator : AbstractValidator<ChangeProductStatusCommand>
{
    private static readonly string[] CiblesVendeur =
    {
        "PendingReview", "Published", "Unpublished", "Archived"
    };

    public ChangeProductStatusCommandValidator()
    {
        RuleFor(c => c.ProductId).NotEmpty();

        RuleFor(c => c.Status)
            .Must(EstUneCibleVendeur)
            .WithMessage("Statut invalide. Attendu : PENDING_REVIEW, PUBLISHED, UNPUBLISHED ou ARCHIVED.");
    }

    private static bool EstUneCibleVendeur(string? statut)
    {
        if (string.IsNullOrWhiteSpace(statut))
        {
            return false;
        }

        var normalise = statut.Replace("_", string.Empty).Trim();
        return CiblesVendeur.Any(c => string.Equals(c, normalise, StringComparison.OrdinalIgnoreCase));
    }
}
