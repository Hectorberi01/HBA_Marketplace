namespace HBA.Analytics.Domain.RollUps;

/// <summary>
/// Les tentatives de paiement d'un jour, par prestataire, devise et issue.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// LE TAUX D'ÉCHEC SE LIT SUR DEUX LIGNES DE CETTE TABLE, ET SUR RIEN D'AUTRE.
///
///     échecs / (échecs + captures), à (jour, prestataire, devise) fixés
///
/// C'est pourquoi l'issue est dans la CLÉ et non dans une colonne : deux tables,
/// ou deux colonnes, laisseraient le numérateur et le dénominateur diverger sans
/// que rien ne le signale.
///
/// CE QUE CETTE TABLE NE COMPTE PAS, ET IL FAUT LE SAVOIR.
///
/// Une tentative qui n'aboutit à AUCUN des deux événements n'y figure pas : un
/// acheteur qui abandonne la page du prestataire, un webhook jamais reçu, un
/// paiement resté « en attente ». Le taux mesure donc « échecs déclarés sur
/// issues déclarées », et non « échecs sur intentions créées ». Les deux
/// divergent exactement quand un prestataire cesse de répondre — c'est-à-dire au
/// moment où l'on regarde ce graphe.
///
/// La série des intentions vit dans payment-service, qui les crée. La croiser
/// demanderait un troisième événement, `PaymentIntentCreated`, qui n'existe pas.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed class PaiementJournalier
{
    public DateOnly Day { get; init; }

    /// <summary>Le prestataire, en minuscules. « inconnu » avant le lot 2.</summary>
    public string Provider { get; init; } = default!;

    /// <summary>La devise. « XXX » quand le message ne la portait pas.</summary>
    public string Currency { get; init; } = default!;

    /// <summary>« Captured » ou « Failed ».</summary>
    public string Outcome { get; init; } = default!;

    public int Count { get; set; }

    /// <summary>
    /// Somme des montants.
    /// </summary>
    /// <remarks>
    /// SOUS-ESTIMÉ SUR LA LIGNE « inconnu » : `Amount` est optionnel lui aussi, et
    /// les messages d'avant le lot 2 valent zéro. Le compte, lui, reste exact.
    /// </remarks>
    public decimal Amount { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    /// <summary>Ajoute une tentative à la journée.</summary>
    public void Ajouter(decimal montant, DateTime instantUtc)
    {
        Count += 1;
        Amount += montant;
        UpdatedAtUtc = instantUtc;
    }
}
