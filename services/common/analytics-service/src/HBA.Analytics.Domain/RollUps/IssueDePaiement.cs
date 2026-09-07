namespace HBA.Analytics.Domain.RollUps;

/// <summary>
/// Ce qu'une tentative de paiement est devenue, et par qui elle est passée.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// LES DEUX ISSUES SONT DANS LA MÊME TABLE, ET C'EST LA CONDITION DU TAUX.
///
/// Un taux d'échec se calcule sur deux séries comparables. Deux tables — une pour
/// les captures, une pour les échecs — obligeraient à les rapprocher à chaque
/// lecture, et le jour où l'une aurait une ligne que l'autre n'a pas, le taux
/// serait faux sans que rien ne le dise. Une clé qui porte l'issue rend la
/// division locale.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public static class IssueDePaiement
{
    public const string Encaisse = "Captured";
    public const string Echoue = "Failed";

    /// <summary>Longueur retenue en base pour cette colonne.</summary>
    public const int LongueurMax = 16;
}

/// <summary>
/// Le prestataire de paiement, tel que la table le range.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// « inconnu » N'EST PAS UN ÉCHEC DE PLUS, C'EST UN MESSAGE D'AVANT LE LOT 2.
///
/// `Provider` est un champ OPTIONNEL de `PaymentCaptured` et `PaymentFailed` :
/// il est nul sur tout message émis avant son ajout, et sur tout message resté
/// dans l'outbox au moment du déploiement.
///
/// DEUX RÉPONSES ÉTAIENT POSSIBLES, ET LA MOINS INTUITIVE EST LA BONNE.
///
/// Ignorer ces messages aurait laissé la table propre — et le TAUX D'ÉCHEC
/// FAUX : le dénominateur aurait perdu des tentatives que le numérateur garde,
/// ou l'inverse, selon lesquels arrivent en premier. Un chiffre faux est pire
/// qu'un trou, parce qu'il est silencieux.
///
/// Ils sont donc rangés sous « inconnu », qui apparaît DANS LE GRAPHE, décroît
/// jusqu'à zéro dans les jours qui suivent le déploiement, et n'y revient plus.
/// C'est un indicateur de migration autant qu'une donnée.
///
/// SUR CETTE LIGNE-LÀ, SEUL LE COMPTE EST FIABLE. `Amount` est optionnel lui
/// aussi : les anciennes lignes valent zéro. Le montant du seau « inconnu » est
/// donc sous-estimé, et il ne doit pas entrer dans un total de volume.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public static class FournisseurDePaiement
{
    /// <summary>Le seau des messages qui ne portaient pas encore le champ.</summary>
    public const string Inconnu = "inconnu";

    /// <summary>
    /// La devise des messages qui ne la portaient pas encore.
    /// </summary>
    /// <remarks>
    /// `XXX` EST UN VRAI CODE ISO 4217 — « aucune devise » — et non un sentinelle
    /// inventé. Un code inventé finirait par être affiché tel quel dans une
    /// interface, ou pire, comparé à une vraie devise.
    /// </remarks>
    public const string DeviseInconnue = "XXX";

    /// <summary>Longueur retenue en base pour cette colonne.</summary>
    public const int LongueurMax = 40;

    /// <summary>
    /// Range un nom de prestataire venu du fil.
    /// </summary>
    /// <remarks>
    /// MISE EN MINUSCULES, PARCE QUE LE NOM EST DANS LA CLÉ PRIMAIRE. « FedaPay »
    /// et « fedapay » y feraient deux séries pour un seul prestataire, et
    /// personne ne le verrait avant de comparer deux totaux qui ne se rejoignent
    /// pas. Le domaine écrit `Provider` en clair ; rien ne garantit sa casse dans
    /// la durée.
    /// </remarks>
    public static string Normaliser(string? duFil)
    {
        if (string.IsNullOrWhiteSpace(duFil))
        {
            return Inconnu;
        }

        var nettoye = duFil.Trim().ToLowerInvariant();
        return nettoye.Length <= LongueurMax ? nettoye : nettoye[..LongueurMax];
    }

    /// <summary>Range une devise venue du fil.</summary>
    public static string NormaliserLaDevise(string? duFil)
        => string.IsNullOrWhiteSpace(duFil) ? DeviseInconnue : duFil.Trim().ToUpperInvariant();
}
