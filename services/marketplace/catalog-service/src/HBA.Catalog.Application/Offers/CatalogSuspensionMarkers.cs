using HBA.Catalog.Domain.Offers;

namespace HBA.Catalog.Application.Offers;

/// <summary>
/// Marqueur des offres retirées parce que LEUR VENDEUR est suspendu.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// CE MARQUEUR EST LA CLÉ DE LA RÉHABILITATION, PAS UNE DÉCORATION.
///
/// Sans lui, lever une suspension remettrait en vente TOUT ce qui est suspendu —
/// y compris une offre qu'un modérateur avait retirée pour contrefaçon ou prix
/// aberrant. Le vendeur sanctionné obtiendrait, en prime de son rétablissement,
/// l'annulation de sanctions sans rapport. Et personne ne le verrait : l'offre
/// redeviendrait simplement achetable.
///
/// Le préfixe est stable : le changer sans migrer les offres déjà marquées les
/// laisserait suspendues pour toujours.
///
/// SEUL LE VOLET « OFFRE » EST REPRIS DE `SellerCatalogSuspension`.
///
/// Dans le monolithe, la suspension d'un vendeur dépubliait ses FICHES et
/// retirait ses OFFRES, dans une même boucle. Ici on ne pose que le marqueur et
/// la logique d'offre : la fiche produit de catalog-service a son propre
/// `ProductStatus` à trois valeurs, et lui appliquer le cycle de suspension du
/// monolithe — qui en a six — demanderait d'abord de trancher lequel des deux
/// modèles fait foi. C'est un travail de PRODUIT, pas d'offre.
///
/// CE MARQUEUR A ÉTÉ ÉCRIT AVANT SON CONSOMMATEUR, ET A ATTENDU (ISSUE-025).
///
/// Pendant tout ce temps, suspendre un vendeur ne retirait AUCUNE de ses offres de
/// la vente : le marqueur existait, personne ne l'appelait. Il est désormais posé
/// par `SellerSuspendedOfferWithdrawalHandler`, côté catalog-service, et relu par
/// `SellerSuspensionLiftedOfferReinstatementHandler`.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
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

/// <summary>
/// Marqueur des offres retirées parce que LEUR BOUTIQUE a fermé.
/// </summary>
/// <remarks>
/// NE PARTAGE AUCUN PRÉFIXE AVEC <see cref="SellerCatalogSuspension"/>, et
/// c'est la condition qui rend les deux retraits superposables.
///
/// Un vendeur suspendu peut avoir une boutique déjà fermée. Chaque levée ne doit
/// relever QUE ce qu'elle avait posé — sinon rouvrir une boutique remettrait en
/// vente le catalogue d'un vendeur toujours sanctionné.
///
/// Si l'un des deux préfixes était le début de l'autre, `StartsWith` les
/// confondrait. « store_closed » et « seller_suspended » n'ont même pas la même
/// première lettre : ce n'est pas un hasard, c'est la propriété qu'on teste.
/// </remarks>
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

/// <summary>
/// L'encodage commun aux deux marqueurs.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// CE QUE LE MOTIF PORTE, ET POURQUOI IL DOIT LE PORTER.
///
/// Format : `préfixe[ÉtatDAvant]: motif lisible`.
///
/// Le retrait ramène tout à `Suspended` — une offre `Active` comme une offre
/// `OutOfStock`. Si la levée les relevait toutes en `Active`, celles qui étaient
/// en rupture redeviendraient achetables sans une seule unité derrière : le client
/// commanderait, et la réservation de stock échouerait APRÈS le paiement.
///
/// Rien d'autre ne peut porter cette information. `OfferStatus` n'a qu'une valeur
/// à la fois, et catalog-service ne connaît pas inventory — il ne peut donc pas
/// redemander le stock au moment de la levée. `StatusReason` est la seule colonne
/// où l'état d'avant survit à la suspension.
///
/// LE PRÉFIXE RESTE EN TÊTE, ET C'EST CE QUI COMPTE. La reconnaissance se fait
/// par `StartsWith` : la parenthèse d'état s'insère APRÈS le préfixe, donc une
/// ligne écrite avant cette évolution reste reconnue et se relève en `Active` —
/// le repli exact de `LireEtatDAvant`.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
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

    /// <summary>
    /// Relit l'état d'avant. Rend <c>Active</c> quand le motif n'en porte pas.
    ///
    /// `Active` EST LE BON REPLI, ET CE N'EST PAS UN CHOIX PAR DÉFAUT.
    ///
    /// Un motif sans parenthèse vient d'une suspension posée à la main par un
    /// exploitant. Relever en `Paused` obligerait le vendeur à réactiver lui-même
    /// une offre qu'il n'a jamais retirée ; relever en `OutOfStock` afficherait une
    /// rupture inventée. `Active` est le seul état qui ne raconte rien de faux.
    /// </summary>
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
/// <remarks>
/// LE SKU EST RENDU PARCE QUE CE MODULE NE CONNAÎT PAS LE STOCK.
///
/// Une offre réactivée redevient `Active` sans que rien ici ne sache s'il reste
/// de la marchandise. C'est l'appelant — au composition root — qui interroge
/// Inventory et remet en rupture ce qui doit l'être. Sans ce retour, il faudrait
/// relire toutes les offres du vendeur pour retrouver lesquelles viennent d'être
/// relevées.
/// </remarks>
public sealed record ReinstatedOffer(Guid OfferId, string? Sku);
