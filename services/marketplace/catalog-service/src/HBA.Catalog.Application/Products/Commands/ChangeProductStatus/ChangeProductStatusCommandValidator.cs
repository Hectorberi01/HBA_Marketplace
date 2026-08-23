using FluentValidation;

namespace HBA.Catalog.Application.Products.Commands.ChangeProductStatus;

/// <summary>
/// Vérifie que le statut cible est l'une des valeurs que le VENDEUR peut demander.
///
/// CETTE LISTE EST PLUS COURTE QUE L'ÉNUMÉRATION, ET C'EST VOULU.
///
/// Approved, Rejected, Suspended et Draft en sont absents : ce sont des décisions
/// d'administration ou des conséquences, pas des demandes. Le handler les refuse
/// aussi — la double barrière est délibérée, le §4 précisant que « le frontend ne
/// constitue jamais la barrière de sécurité ». Un validateur seul se contourne
/// dès qu'un autre chemin d'appel apparaît.
///
/// Les deux formes du cahier sont acceptées : « PENDING_REVIEW » dans les JSON
/// (§5), « PendingReview » en C# (§7). Refuser la première ferait échouer
/// exactement les valeurs que la documentation donne en exemple.
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
