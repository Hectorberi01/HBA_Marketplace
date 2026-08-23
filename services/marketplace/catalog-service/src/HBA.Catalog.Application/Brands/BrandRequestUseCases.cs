using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;
using HBA.Catalog.Application.Abstractions;
using HBA.Catalog.Contracts;
using HBA.Catalog.Domain.Brands;

namespace HBA.Catalog.Application.Brands;

// ═════════════════════════════════════════════════════════════════════════════
// LES DEMANDES DE MARQUE (§10, §16).
//
// « Le vendeur ne crée pas directement une nouvelle marque officielle. » Sans ce
// mécanisme, « Samsung », « SAMSUNG », « Samsung Electronics » et « samsumg »
// cohabitent au bout d'un mois, et le filtre par marque de la vitrine devient
// inutilisable.
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>Un vendeur demande une marque absente du référentiel.</summary>
public sealed record RequestBrandCreationCommand(
    Guid SellerId,
    string Name,
    string? Note = null) : ICommand<Guid>;

/// <summary>
/// Un administrateur approuve la demande (§16).
///
/// `ExistingBrandId` EST LE CAS FRÉQUENT, PAS L'EXCEPTION.
///
/// Une demande « samsumg » se rattache au « Samsung » déjà au catalogue. Ne
/// permettre que la création ferait de ce mécanisme la source du problème qu'il
/// devait résoudre : un doublon de plus, validé cette fois.
/// </summary>
public sealed record ApproveBrandRequestCommand(
    Guid RequestId,
    Guid ReviewedBy,
    Guid? ExistingBrandId = null) : ICommand<Guid>;

/// <summary>Un administrateur refuse la demande, avec motif (§16).</summary>
public sealed record RejectBrandRequestCommand(
    Guid RequestId,
    Guid ReviewedBy,
    string Reason) : ICommand;

/// <summary>La file des demandes en attente.</summary>
public sealed record ListPendingBrandRequestsQuery : IQuery<IReadOnlyList<BrandRequestSummary>>;

internal sealed class BrandRequestUseCases
    : ICommandHandler<RequestBrandCreationCommand, Guid>,
      ICommandHandler<ApproveBrandRequestCommand, Guid>,
      ICommandHandler<RejectBrandRequestCommand>,
      IQueryHandler<ListPendingBrandRequestsQuery, IReadOnlyList<BrandRequestSummary>>
{
    private readonly IBrandRequestRepository _requests;
    private readonly IBrandRepository _brands;
    private readonly ICatalogUnitOfWork _unitOfWork;

    public BrandRequestUseCases(
        IBrandRequestRepository requests,
        IBrandRepository brands,
        ICatalogUnitOfWork unitOfWork)
    {
        _requests = requests;
        _brands = brands;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Guid>> Handle(
        RequestBrandCreationCommand command, CancellationToken cancellationToken)
    {
        // IDEMPOTENT SUR LE DOUBLE-CLIC, PAS SUR LA REDEMANDE APRÈS REFUS.
        //
        // Le formulaire est un champ et un bouton : le double envoi est la règle.
        // On rend la demande existante plutôt qu'un conflit — l'utilisateur voulait
        // demander cette marque, c'est fait. En revanche une demande refusée puis
        // corrigée doit pouvoir repartir : d'où le filtre sur les seules demandes
        // EN ATTENTE, ici comme dans l'index partiel `ux_brand_requests_pending`.
        var enCours = await _requests.GetPendingByNameAsync(command.SellerId, command.Name, cancellationToken);
        if (enCours is not null)
        {
            return enCours.Id;
        }

        var demande = BrandRequest.Create(command.SellerId, command.Name, command.Note);
        if (demande.IsFailure)
        {
            return Result.Failure<Guid>(demande.Error);
        }

        await _requests.AddAsync(demande.Value, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return demande.Value.Id;
    }

    public async Task<Result<Guid>> Handle(
        ApproveBrandRequestCommand command, CancellationToken cancellationToken)
    {
        var demande = await _requests.GetByIdAsync(command.RequestId, cancellationToken);
        if (demande is null)
        {
            return Result.Failure<Guid>(Error.NotFound(
                "catalog.brand_request.not_found", $"Demande {command.RequestId} introuvable."));
        }

        Guid brandId;

        if (command.ExistingBrandId is { } existante && existante != Guid.Empty)
        {
            var marque = await _brands.GetByIdAsync(new BrandId(existante), cancellationToken);
            if (marque is null)
            {
                return Result.Failure<Guid>(Error.NotFound(
                    "catalog.brand.not_found", $"Marque {existante} introuvable."));
            }

            brandId = marque.Id.Value;
        }
        else
        {
            var creation = Brand.Create(demande.Name);
            if (creation.IsFailure)
            {
                return Result.Failure<Guid>(creation.Error);
            }

            // LE SLUG PEUT DÉJÀ ÊTRE PRIS, ET LE MESSAGE DOIT LE DIRE.
            //
            // C'est le signal qu'une marque très proche existe — « Samsung » face à
            // « samsung ». L'administrateur doit alors rattacher plutôt que créer,
            // et l'erreur le lui indique au lieu de le laisser devant une violation
            // de contrainte.
            if (await _brands.SlugExistsAsync(creation.Value.Slug.Value, cancellationToken))
            {
                return Result.Failure<Guid>(Error.Conflict(
                    "catalog.brand.slug_taken",
                    $"Une marque porte déjà un nom équivalent à « {demande.Name} ». "
                    + "Rattachez la demande à la marque existante au lieu d'en créer une seconde."));
            }

            await _brands.AddAsync(creation.Value, cancellationToken);
            brandId = creation.Value.Id.Value;
        }

        var decision = demande.Approve(brandId, command.ReviewedBy, DateTimeOffset.UtcNow);
        if (decision.IsFailure)
        {
            return Result.Failure<Guid>(decision.Error);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return brandId;
    }

    public async Task<Result> Handle(
        RejectBrandRequestCommand command, CancellationToken cancellationToken)
    {
        var demande = await _requests.GetByIdAsync(command.RequestId, cancellationToken);
        if (demande is null)
        {
            return Result.Failure(Error.NotFound(
                "catalog.brand_request.not_found", $"Demande {command.RequestId} introuvable."));
        }

        var decision = demande.Reject(command.Reason, command.ReviewedBy, DateTimeOffset.UtcNow);
        if (decision.IsFailure)
        {
            return decision;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result<IReadOnlyList<BrandRequestSummary>>> Handle(
        ListPendingBrandRequestsQuery query, CancellationToken cancellationToken)
    {
        var demandes = await _requests.ListPendingAsync(cancellationToken);

        IReadOnlyList<BrandRequestSummary> resume = demandes
            .Select(r => new BrandRequestSummary(
                r.Id, r.SellerId, r.Name, r.Note, r.Status.ToString(),
                r.BrandId, r.RejectionReason, r.RequestedAtUtc, r.ReviewedAtUtc))
            .ToList();

        return Result.Success(resume);
    }
}
