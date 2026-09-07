using HBA.Shared.Domain.Primitives;
using HBA.Shared.Domain.Results;
using HBA.Catalog.Domain.Brands.Events;

namespace HBA.Catalog.Domain.Brands;

public enum BrandRequestStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2
}

/// <summary>UNE DEMANDE DE MARQUE — TABLE <c>brand_requests</c> (§10, §20).</summary>
public sealed class BrandRequest : AggregateRoot<Guid>
{
    private BrandRequest()
    {
    }

    private BrandRequest(Guid id, Guid sellerId, string name, string? note)
        : base(id)
    {
        SellerId = sellerId;
        Name = name;
        Note = note;
        Status = BrandRequestStatus.Pending;
        RequestedAtUtc = DateTimeOffset.UtcNow;
    }

    public Guid SellerId { get; private set; }

    /// <summary>Le nom demandé, tel que le vendeur l'a écrit.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>Précision libre du vendeur — un site officiel, un pays d'origine.</summary>
    public string? Note { get; private set; }

    public BrandRequestStatus Status { get; private set; }

    /// <summary>La marque retenue à l'approbation : créée, ou existante.</summary>
    public Guid? BrandId { get; private set; }

    /// <summary>Le motif du refus, destiné au vendeur.</summary>
    public string? RejectionReason { get; private set; }

    public Guid? ReviewedBy { get; private set; }
    public DateTimeOffset RequestedAtUtc { get; private set; }
    public DateTimeOffset? ReviewedAtUtc { get; private set; }

    public static Result<BrandRequest> Create(Guid sellerId, string name, string? note = null)
    {
        if (sellerId == Guid.Empty)
        {
            return Error.Validation("catalog.brand_request.seller_required", "La demande doit désigner son vendeur.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return Error.Validation("catalog.brand_request.name_required", "Le nom de la marque est obligatoire.");
        }

        var demande = new BrandRequest(
            Guid.NewGuid(),
            sellerId,
            name.Trim(),
            string.IsNullOrWhiteSpace(note) ? null : note.Trim());

        demande.Raise(new BrandRequestedDomainEvent(demande.Id, sellerId, demande.Name));
        return demande;
    }

    /// <summary>
    /// Approuve la demande en la rattachant à une marque — nouvelle ou existante.
    /// </summary>
    public Result Approve(Guid brandId, Guid reviewedBy, DateTimeOffset nowUtc)
    {
        if (Status is not BrandRequestStatus.Pending)
        {
            return Result.Failure(Error.Conflict(
                "catalog.brand_request.already_reviewed",
                "Cette demande a déjà reçu une décision."));
        }

        if (brandId == Guid.Empty)
        {
            return Result.Failure(Error.Validation(
                "catalog.brand_request.brand_required",
                "L'approbation doit désigner la marque retenue."));
        }

        Status = BrandRequestStatus.Approved;
        BrandId = brandId;
        ReviewedBy = reviewedBy;
        ReviewedAtUtc = nowUtc;

        Raise(new BrandRequestApprovedDomainEvent(Id, SellerId, brandId, Name));
        return Result.Success();
    }

    /// <summary>Refuse la demande.</summary>
    public Result Reject(string reason, Guid reviewedBy, DateTimeOffset nowUtc)
    {
        if (Status is not BrandRequestStatus.Pending)
        {
            return Result.Failure(Error.Conflict(
                "catalog.brand_request.already_reviewed",
                "Cette demande a déjà reçu une décision."));
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result.Failure(Error.Validation(
                "catalog.brand_request.reason_required",
                "Un refus doit indiquer son motif."));
        }

        Status = BrandRequestStatus.Rejected;
        RejectionReason = reason.Trim();
        ReviewedBy = reviewedBy;
        ReviewedAtUtc = nowUtc;
        return Result.Success();
    }
}
