using HBA.Catalog.Domain.Offers;

namespace HBA.Catalog.Application.Offers;

/// <summary>Marqueur des offres retirées parce que LEUR VENDEUR est suspendu.</summary>
public static class SellerCatalogSuspension
{
    public const string ReasonPrefix = "seller_suspended";

    public static string ComposeReason(string? adminReason, OfferStatus previousStatus)
        => MarqueurDeRetrait.Composer(ReasonPrefix, adminReason, previousStatus);

    public static bool IsSellerSuspension(string? statusReason)
        => MarqueurDeRetrait.Correspond(ReasonPrefix, statusReason);

    public static OfferStatus ReadPreviousStatus(string? statusReason)
        => MarqueurDeRetrait.LireEtatDAvant(statusReason);
}

/// <summary>Marqueur des offres retirées parce que LEUR BOUTIQUE a fermé.</summary>
public static class StoreCatalogClosure
{
    public const string ReasonPrefix = "store_closed";

    public static string ComposeReason(string? reason, OfferStatus previousStatus)
        => MarqueurDeRetrait.Composer(ReasonPrefix, reason, previousStatus);

    public static bool IsStoreClosure(string? statusReason)
        => MarqueurDeRetrait.Correspond(ReasonPrefix, statusReason);

    public static OfferStatus ReadPreviousStatus(string? statusReason)
        => MarqueurDeRetrait.LireEtatDAvant(statusReason);
}

/// <summary>L'encodage commun aux deux marqueurs.</summary>
internal static class MarqueurDeRetrait
{
    public static string Composer(string prefixe, string? motif, OfferStatus etatDAvant)
    {
        var tete = $"{prefixe}[{etatDAvant}]";

        return string.IsNullOrWhiteSpace(motif)
            ? tete
            : $"{tete}: {motif.Trim()}";
    }

    public static bool Correspond(string prefixe, string? statusReason)
        => statusReason is not null
           && statusReason.StartsWith(prefixe, StringComparison.Ordinal);

    /// <summary>Relit l'état d'avant. Rend <c>Active</c> quand le motif n'en porte pas.</summary>
    public static OfferStatus LireEtatDAvant(string? statusReason)
    {
        if (statusReason is null)
        {
            return OfferStatus.Active;
        }

        var ouvrante = statusReason.IndexOf('[');
        var fermante = statusReason.IndexOf(']');

        if (ouvrante < 0 || fermante <= ouvrante + 1)
        {
            return OfferStatus.Active;
        }

        var brut = statusReason[(ouvrante + 1)..fermante];

        return Enum.TryParse<OfferStatus>(brut, ignoreCase: false, out var statut)
            ? statut
            : OfferStatus.Active;
    }
}

/// <summary>Une offre remise en vente, et le SKU dont il faut revérifier le stock.</summary>
public sealed record ReinstatedOffer(Guid OfferId, string? Sku);
