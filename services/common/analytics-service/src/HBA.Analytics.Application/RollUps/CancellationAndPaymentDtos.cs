namespace HBA.Analytics.Application.RollUps;

// LOT 2 — LES DEUX FAMILLES QUE LES CHAMPS OPTIONNELS ONT DEBLOQUEES.

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
public sealed record SellerCancellationSeriesDto(
    Guid SellerId,
    DateOnly From,
    DateOnly To,
    string Currency,
    IReadOnlyList<SellerCancellationPointDto> Points,
    int TotalOrders,
    decimal TotalAmount);

/// <summary>Ce qu'un prestataire a traité sur la période.</summary>
/// <param name="Provider">Le prestataire, en minuscules.</param>
/// <param name="Captured">Tentatives encaissées.</param>
/// <param name="Failed">Tentatives échouées.</param>
/// <param name="CapturedAmount">Volume encaissé.</param>
/// <param name="FailureRate">
/// <c> Failed / (Failed + Captured)</c>, arrondi à quatre décimales.
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
public sealed record PaymentSeriesDto(
    DateOnly From,
    DateOnly To,
    string Currency,
    IReadOnlyList<PaymentPointDto> Points,
    IReadOnlyList<PaymentProviderDto> Providers,
    int TotalCaptured,
    int TotalFailed,
    decimal? FailureRate);
