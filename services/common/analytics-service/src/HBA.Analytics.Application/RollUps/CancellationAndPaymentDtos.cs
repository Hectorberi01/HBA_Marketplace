namespace HBA.Analytics.Application.RollUps;

// ═════════════════════════════════════════════════════════════════════════════
// LOT 2 — LES DEUX FAMILLES QUE LES CHAMPS OPTIONNELS ONT DEBLOQUEES.
//
// Chaque parametre est documente, ou aucun : `GenerateDocumentationFile` est
// allume pour tout le depot et CS1573 n'est pas tu.
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>Un jour d'annulations pour un vendeur.</summary>
/// <param name="Day">La journée UTC.</param>
/// <param name="OrdersCount">Commandes annulées où ce vendeur avait une part.</param>
/// <param name="Amount">
/// Somme des parts perdues. C'est le montant de la commande, PAS celui qui sera
/// remboursé — les deux coïncident tant qu'une annulation est totale.
/// </param>
public sealed record SellerCancellationPointDto(DateOnly Day, int OrdersCount, decimal Amount);

/// <summary>La série d'annulations d'un vendeur sur une période.</summary>
/// <param name="SellerId">Le vendeur, tel qu'il est désigné dans l'URL.</param>
/// <param name="From">Première journée rendue, incluse.</param>
/// <param name="To">Dernière journée rendue, incluse.</param>
/// <param name="Currency">La devise de toute la série.</param>
/// <param name="Points">Un point par journée, zéros compris.</param>
/// <param name="TotalOrders">Commandes annulées sur la période.</param>
/// <param name="TotalAmount">Montant perdu sur la période.</param>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// CETTE SÉRIE NE SE DIVISE PAS PAR CELLE DES VENTES POUR OBTENIR UN TAUX.
///
/// Une commande est annulée à une date, et vendue à une autre. Le rapport
/// « annulations du jour / ventes du jour » compare deux populations
/// différentes, et il monte mécaniquement le lendemain d'une grosse journée.
///
/// Le taux qui aurait un sens — « part des commandes d'un jour finalement
/// annulées » — demanderait de rattacher l'annulation au jour de la VENTE.
/// `OrderCancelled` ne porte pas cette date, et la remonter supposerait de
/// relire la commande chez son propriétaire. Ce n'est pas dans ce lot.
///
/// UNE ANNULATION SANS PART VENDEUR EST INVISIBLE ICI : un repas n'a pas de
/// vendeur, et un message d'avant le lot 2 n'en nomme aucun. Voir
/// `AnnulationJournaliereVendeur`.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed record SellerCancellationSeriesDto(
    Guid SellerId,
    DateOnly From,
    DateOnly To,
    string Currency,
    IReadOnlyList<SellerCancellationPointDto> Points,
    int TotalOrders,
    decimal TotalAmount);

/// <summary>Ce qu'un prestataire a traité sur la période.</summary>
/// <param name="Provider">Le prestataire, en minuscules. « inconnu » avant le lot 2.</param>
/// <param name="Captured">Tentatives encaissées.</param>
/// <param name="Failed">Tentatives échouées.</param>
/// <param name="CapturedAmount">Volume encaissé.</param>
/// <param name="FailureRate">
/// <c>Failed / (Failed + Captured)</c>, arrondi à quatre décimales. `null` quand
/// le prestataire n'a eu AUCUNE issue sur la période : un taux sur zéro
/// tentative n'est pas « 0 % », il n'existe pas.
/// </param>
public sealed record PaymentProviderDto(
    string Provider,
    int Captured,
    int Failed,
    decimal CapturedAmount,
    decimal? FailureRate);

/// <summary>Un jour de tentatives de paiement, toutes issues confondues.</summary>
/// <param name="Day">La journée UTC.</param>
/// <param name="Captured">Tentatives encaissées ce jour-là.</param>
/// <param name="Failed">Tentatives échouées ce jour-là.</param>
/// <param name="CapturedAmount">Volume encaissé ce jour-là.</param>
public sealed record PaymentPointDto(DateOnly Day, int Captured, int Failed, decimal CapturedAmount);

/// <summary>Les paiements de la plateforme sur une période.</summary>
/// <param name="From">Première journée rendue, incluse.</param>
/// <param name="To">Dernière journée rendue, incluse.</param>
/// <param name="Currency">La devise retenue.</param>
/// <param name="Points">La courbe jour par jour, tous prestataires confondus.</param>
/// <param name="Providers">
/// Le classement par prestataire sur la période, du plus gros volume encaissé au
/// plus petit.
/// </param>
/// <param name="TotalCaptured">Tentatives encaissées sur la période.</param>
/// <param name="TotalFailed">Tentatives échouées sur la période.</param>
/// <param name="FailureRate">Taux d'échec global. `null` sans aucune tentative.</param>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// LE TAUX MESURE « ÉCHECS SUR ISSUES DÉCLARÉES », PAS « ÉCHECS SUR TENTATIVES ».
///
/// Une intention de paiement qui n'aboutit à aucun des deux événements n'entre
/// dans aucun des deux termes : un acheteur qui abandonne la page du
/// prestataire, un webhook jamais reçu, un paiement resté en attente. Les deux
/// définitions divergent exactement quand un prestataire cesse de répondre —
/// c'est-à-dire au moment où l'on regarde ce graphe.
///
/// LE SEAU « inconnu » DOIT DÉCROÎTRE JUSQU'À ZÉRO dans les jours qui suivent le
/// déploiement du lot 2. S'il persiste, c'est qu'un producteur ne remplit pas
/// `Provider` — et le taux par prestataire est alors incomplet, pas faux.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed record PaymentSeriesDto(
    DateOnly From,
    DateOnly To,
    string Currency,
    IReadOnlyList<PaymentPointDto> Points,
    IReadOnlyList<PaymentProviderDto> Providers,
    int TotalCaptured,
    int TotalFailed,
    decimal? FailureRate);
