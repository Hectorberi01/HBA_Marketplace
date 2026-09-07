namespace HBA.Delivery.Pricing.Domain.Policies;

/// <summary>D'OÙ VIENT LA DISTANCE QUI A CHIFFRÉ CETTE COURSE.</summary>
public static class SourcesEstimation
{
    /// <summary>L'appelant a fourni la distance, et le service l'a reprise telle quelle.</summary>
    public const string FournieParAppelant = "CLIENT_PROVIDED";

    /// <summary>
    /// Aucune distance fournie : ligne droite entre les deux points (Haversine),
    /// puis facteur de correction urbaine.
    /// </summary>
    public const string LigneDroiteCorrigee = "FALLBACK_HAVERSINE";
}

/// <summary>LES DEUX CONSTANTES QUI CHIFFRAIENT LES COURSES, SORTIES DU CODE.</summary>
public sealed class EstimationItineraireOptions
{
    public const string SectionName = "EstimationItineraire";

    /// <summary>
    /// Vitesse moyenne retenue pour convertir une distance en durée, en mètres par
    /// seconde.
    /// </summary>
    public double VitesseMoyenneMetresParSeconde { get; init; } = 5.8;

    /// <summary>
    /// Multiplicateur appliqué à la distance à vol d'oiseau pour approcher la
    /// distance routière.
    /// </summary>
    public decimal FacteurCorrectionUrbaine { get; init; } = 1.0m;

    /// <summary>Durée plancher, en secondes.</summary>
    public int DureeMinimaleSecondes { get; init; } = 60;

    /// <summary>
    /// Refuse au démarrage une configuration qui produirait des devis absurdes,
    /// plutôt que de les produire silencieusement.
    /// </summary>
    public void Valider()
    {
        if (VitesseMoyenneMetresParSeconde <= 0)
        {
            throw new InvalidOperationException(
                $"{SectionName}:{nameof(VitesseMoyenneMetresParSeconde)} doit être strictement positif "
                + $"(valeur lue : {VitesseMoyenneMetresParSeconde}). Une vitesse nulle ou négative rendrait "
                + "toute durée infinie ou négative, et donc tout devis faux.");
        }

        if (FacteurCorrectionUrbaine < 1.0m)
        {
            throw new InvalidOperationException(
                $"{SectionName}:{nameof(FacteurCorrectionUrbaine)} ne peut pas être inférieur à 1,0 "
                + $"(valeur lue : {FacteurCorrectionUrbaine}). Un trajet routier n'est jamais plus court "
                + "que la ligne droite qui le sous-tend.");
        }

        if (DureeMinimaleSecondes < 0)
        {
            throw new InvalidOperationException(
                $"{SectionName}:{nameof(DureeMinimaleSecondes)} ne peut pas être négatif "
                + $"(valeur lue : {DureeMinimaleSecondes}).");
        }
    }
}
