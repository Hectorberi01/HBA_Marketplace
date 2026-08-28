namespace HBA.Delivery.Pricing.Domain.Policies;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// D'OÙ VIENT LA DISTANCE QUI A CHIFFRÉ CETTE COURSE.
///
/// CE QUI ÉTAIT CASSÉ. Le devis portait un nombre de mètres et un nombre de
/// secondes, et RIEN ne disait comment ils avaient été obtenus. Or il y a deux
/// origines possibles, et elles n'ont pas la même valeur :
///
///   • l'appelant a fourni <c>DistanceMeters</c> — il a donc mesuré quelque
///     chose, éventuellement sur un vrai réseau routier ;
///   • l'appelant n'a rien fourni — et le service a calculé une LIGNE DROITE
///     entre les deux points.
///
/// Les deux produisaient un devis d'apparence identique. Un litige sur le prix
/// d'une course ne pouvait donc pas être tranché : on ne savait plus si le
/// chiffre venait d'une mesure ou d'une approximation géométrique.
///
/// C'EST UN ENJEU D'ARGENT, PAS D'AFFICHAGE. La distance entre directement dans
/// <c>PricingPolicy.BuildBreakdown</c> — <c>distanceFee = km × PerKmFee</c> — et
/// la durée dans <c>minuteFee</c>. Elle décide aussi de la desserte, via le
/// plafond de 25 km de <c>ServiceabilityPolicy</c>.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
/// <remarks>
/// AU PLURIEL, ET C'EST DÉLIBÉRÉ. <c>DeliveryQuote.SourceEstimation</c> est la
/// propriété qui PORTE la valeur ; cette classe est le catalogue des valeurs
/// POSSIBLES. Deux choses différentes, deux noms différents — sans quoi il faut
/// écrire des qualifications complètes dans chaque initialiseur d'objet pour
/// lever une ambiguïté qui n'avait pas lieu d'exister.
/// </remarks>
public static class SourcesEstimation
{
    /// <summary>
    /// L'appelant a fourni la distance, et le service l'a reprise telle quelle.
    /// Le service NE VÉRIFIE PAS d'où l'appelant la tient : c'est une traçabilité
    /// de provenance, pas une garantie de qualité.
    /// </summary>
    public const string FournieParAppelant = "CLIENT_PROVIDED";

    /// <summary>
    /// Aucune distance fournie : ligne droite entre les deux points (Haversine),
    /// puis facteur de correction urbaine. Voir <see cref="EstimationItineraireOptions"/>.
    /// </summary>
    public const string LigneDroiteCorrigee = "FALLBACK_HAVERSINE";
}

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LES DEUX CONSTANTES QUI CHIFFRAIENT LES COURSES, SORTIES DU CODE.
///
/// CE QUI ÉTAIT CASSÉ. <c>EfDeliveryPricingStore</c> contenait, en dur, dans une
/// ligne de calcul : <c>Math.Max(60, (int)(distance / 5.8))</c>. Cinq virgule
/// huit mètres par seconde — 20,9 km/h — était le seul modèle de circulation de
/// toute la plateforme, et il n'était nommé nulle part. La même constante était
/// dupliquée dans <c>route-service</c>, sans que rien ne relie les deux.
///
/// CE QUE CE FICHIER CHANGE, ET CE QU'IL NE CHANGE PAS.
///
/// Il NOMME les deux leviers et les rend configurables. Il ne prétend pas
/// remplacer un moteur de routage : la ligne droite reste une ligne droite.
///
/// LE FACTEUR VAUT 1,0 PAR DÉFAUT, ET C'EST DÉLIBÉRÉ.
///
/// Un facteur de détour urbain est typiquement compris entre 1,2 et 1,4 en tissu
/// dense. Le poser à 1,3 « parce que c'est l'usage » augmenterait le prix de
/// TOUTES les courses d'environ trente pour cent, sur la foi d'un chiffre que
/// personne n'a mesuré à Cotonou. On pose donc le levier sans le tirer : le
/// comportement d'aujourd'hui est conservé À L'IDENTIQUE, et le jour où l'écart
/// réel est mesuré, il se règle par configuration et non par un correctif.
///
/// CE QUE CE CHOIX NE COUVRE PAS, ET IL FAUT LE SAVOIR.
///
///   • TANT QUE LE FACTEUR VAUT 1,0, LA PLATEFORME SOUS-FACTURE. Le trajet réel
///     est toujours plus long que la ligne droite ; la course est donc chiffrée
///     en dessous de son coût, et l'écart croît avec la distance. Ce n'est pas
///     un défaut que ce fichier corrige — c'est un défaut qu'il rend RÉGLABLE et
///     VISIBLE, au lieu de le laisser enfoui dans une division.
///
///   • ET ELLE ACCEPTE DES COURSES HORS ZONE. Le plafond de desserte de 25 km
///     est comparé à une ligne droite. Une course dont le trajet routier fait
///     30 km peut avoir 24 km à vol d'oiseau : elle est acceptée, puis effectuée
///     à perte. Le même facteur corrige les deux, quand il sera réglé.
///
///   • UN FACTEUR UNIQUE POUR TOUT LE PAYS. Le détour n'est pas le même dans le
///     centre de Cotonou et sur la route de Porto-Novo. Un facteur par zone
///     demanderait de savoir dans quelle zone on est, ce que
///     <c>DeliveryZone</c> sait déjà faire — c'est le prolongement naturel, il
///     n'est pas fait.
///
///   • LE FACTEUR NE S'APPLIQUE QU'À LA LIGNE DROITE. Une distance fournie par
///     l'appelant est reprise telle quelle : la corriger reviendrait à majorer
///     une mesure déjà routière.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class EstimationItineraireOptions
{
    public const string SectionName = "EstimationItineraire";

    /// <summary>
    /// Vitesse moyenne retenue pour convertir une distance en durée, en mètres
    /// par seconde. 5,8 m/s (20,9 km/h) est la valeur qui était codée en dur.
    /// </summary>
    public double VitesseMoyenneMetresParSeconde { get; init; } = 5.8;

    /// <summary>
    /// Multiplicateur appliqué à la distance à vol d'oiseau pour approcher la
    /// distance routière. 1,0 = aucune correction, soit le comportement
    /// historique. Ne descend jamais sous 1,0 : un trajet ne peut pas être plus
    /// court que la ligne droite.
    /// </summary>
    public decimal FacteurCorrectionUrbaine { get; init; } = 1.0m;

    /// <summary>
    /// Durée plancher, en secondes. Empêche qu'une course de cinquante mètres
    /// soit facturée zéro minute.
    /// </summary>
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
