namespace HBA.Deliveries.Domain.Deliveries;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// QUELLE PREUVE EXIGER À LA REMISE — DÉCIDÉ ICI, JAMAIS PAR L'APPELANT.
///
/// CE QUI ÉTAIT CASSÉ (ISSUE-057).
///
/// <c>Delivery.RequiredProof</c> existait, était persisté, était projeté vers
/// l'application livreur, et n'était renseigné par PERSONNE. Les deux seuls
/// producteurs réels — <c>CreateDeliveryOnOrderConfirmedHandler</c> côté
/// order-service et <c>FoodOrderBridgeHandlers</c> côté restaurant-service —
/// laissaient la valeur par défaut du contrat, <c>« None »</c>.
///
/// Conséquence : TOUTES les courses de la plateforme naissaient sans exigence de
/// preuve. <c>MarkDelivered</c> ne demande rien quand <c>RequiredProof</c> vaut
/// <c>None</c> : n'importe quel livreur clôturait n'importe quelle course d'un
/// geste, sans code, sans photo, sans signature. Un colis « livré » qu'aucun
/// client n'a reçu n'était contestable par rien.
///
/// Le champ existait, le mécanisme de vérification existait, le compteur de
/// tentatives existait — et rien de tout cela ne s'allumait jamais.
///
/// POURQUOI LA RÈGLE EST ICI ET PAS CHEZ LES APPELANTS.
///
/// C'est précisément ce qui a produit le défaut : chaque producteur pouvait
/// choisir, donc aucun n'a choisi. Un troisième arrivera (un partenaire, une
/// course créée à la main par l'exploitation) et il oublierait aussi. La
/// politique est donc appliquée par <c>Delivery.Create</c>, sur des ENTRÉES que
/// l'appelant décrit — ce qu'il transporte, ce qu'il vaut, si de l'argent change
/// de main — et jamais sur une CONCLUSION qu'il tire lui-même.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// LA RÈGLE, ET CE QUI LA JUSTIFIE.
///
///   1. PAIEMENT À LA LIVRAISON → <c>Pin</c>. De l'argent liquide change de main
///      au seuil de la porte. C'est le seul cas où la remise est aussi un
///      encaissement, et où « livré » sans preuve signifie que le livreur garde
///      à la fois le colis et l'espèce. Le code est dicté PAR le client AU
///      livreur : c'est la seule preuve qui atteste qu'une personne précise a
///      bien reçu, et le livreur ne peut pas la fabriquer — il ne voit jamais
///      <c>IssuedPin</c>.
///
///   2. VALEUR DÉCLARÉE ÉLEVÉE → <c>Pin</c>. Au-dessus de
///      <see cref="HighValueThreshold"/> FCFA, le litige coûte plus cher que la
///      friction d'un code à dicter. En dessous, exiger un code sur chaque
///      course ferait échouer des livraisons pour un client sans téléphone à
///      portée de main — et le remède serait pire que le mal.
///
///   3. TOUT LE RESTE → <c>Photo</c>. Une photo du colis remis ne prouve pas
///      QUI a reçu, mais elle prouve QUE quelque chose a été déposé quelque
///      part, elle est horodatée, et elle ne demande RIEN au client. C'est le
///      plancher : aucune course ne naît plus en <c>None</c>.
///
/// <c>None</c> N'EST PLUS JAMAIS PRODUIT PAR CETTE POLITIQUE. La valeur reste
/// dans l'énumération pour les lignes déjà en base, qu'aucune migration ne peut
/// reclasser — on ne sait pas rétroactivement ce qu'elles transportaient.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// CE QUE CETTE RÈGLE NE COUVRE PAS, ET IL FAUT LE SAVOIR.
///
///   • ELLE NE PRODUIT JAMAIS <c>Signature</c>. Le genre existe, l'agrégat sait
///     le vérifier, et aucune règle ne le choisit. C'est délibéré : la signature
///     n'a de valeur que capturée sur un écran, et l'application livreur ne sait
///     pas encore le faire. Poser la règle avant l'écran rendrait des courses
///     inachevables.
///
///   • LA VALEUR DÉCLARÉE EST CE QUE DIT L'APPELANT. Rien ne la vérifie. Un
///     donneur d'ordre qui déclare zéro obtient une photo là où il aurait dû
///     obtenir un code. Le remède n'est pas ici : c'est à order-service de
///     transmettre le total réel de la commande, ce que le lot 5.3 lui fait
///     faire, et c'est LUI qui connaît le montant encaissé.
///
///   • LE SEUIL EST EN FCFA ET N'EST PAS CONVERTI. La plateforme n'opère qu'au
///     Bénin ; le jour où une course se règle dans une autre monnaie, cette
///     comparaison sera fausse SANS RIEN DIRE. C'est le défaut le plus probable
///     de ce fichier, et il est écrit ici pour qu'on le trouve.
///
///   • ELLE NE REGARDE PAS LE DESTINATAIRE. Une remise en point relais et une
///     remise à domicile sont traitées pareil, faute de savoir les distinguer.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public static class ProofPolicy
{
    /// <summary>
    /// Au-delà de ce montant (FCFA), la remise exige un code dicté par le client.
    ///
    /// 50 000 FCFA, c'est l'ordre de grandeur d'un téléphone d'entrée de gamme :
    /// au-dessus, un colis perdu n'est plus un incident, c'est une réclamation.
    /// En dessous, la friction du code coûterait plus que ce qu'elle protège.
    /// </summary>
    public const decimal HighValueThreshold = 50_000m;

    /// <summary>
    /// La preuve exigée à la remise, d'après ce que la course transporte.
    /// </summary>
    /// <param name="declaredValue">
    /// Valeur des marchandises déclarée par le donneur d'ordre, en FCFA. Nulle
    /// quand il ne la connaît pas — elle est alors traitée comme faible, ce qui
    /// est le choix PRUDENT côté friction et le choix RISQUÉ côté litige. Voir
    /// l'encadré.
    /// </param>
    /// <param name="isCashOnDelivery">
    /// La course encaisse-t-elle le prix des marchandises à la remise ?
    /// </param>
    public static ProofOfDeliveryKind RequiredFor(decimal? declaredValue, bool isCashOnDelivery)
    {
        if (isCashOnDelivery)
        {
            return ProofOfDeliveryKind.Pin;
        }

        if (declaredValue is { } valeur && valeur >= HighValueThreshold)
        {
            return ProofOfDeliveryKind.Pin;
        }

        return ProofOfDeliveryKind.Photo;
    }
}
