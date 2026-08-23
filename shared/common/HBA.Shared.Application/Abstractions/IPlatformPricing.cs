namespace HBA.Shared.Application.Abstractions;

/// <summary>
/// Le barème de la plateforme, vu par les couches Application.
///
/// EXISTE POUR QUE LE VENDEUR LISE LE TAUX QU'ON LUI APPLIQUE.
///
/// Son résumé servait <c>Seller.CommissionRate</c> — une colonne écrite à
/// l'inscription et consultée par aucun calcul. Un marchand y lisait « 10 % »
/// pendant que la configuration en appliquait éventuellement un autre : une
/// affirmation fausse sur de l'argent, que rien ne signalait.
///
/// L'implémentation (PlatformPricing) vit en Infrastructure, où se lit la
/// configuration ; cette interface permet aux couches Application de la
/// consulter sans dépendre d'elle.
/// </summary>
public interface IPlatformPricing
{
    /// <summary>
    /// Commission plateforme, en fraction du prix vendeur net (0.10 = 10 %).
    ///
    /// C'EST LE DÉFAUT DU MOTEUR DE COMMISSION, PAS FORCÉMENT LE TAUX APPLIQUÉ.
    ///
    /// La comptabilisation des gains interroge le moteur de règles de Billing, qui
    /// peut porter « ce vendeur à 5 % ». Ce taux-ci est ce qu'il applique À DÉFAUT
    /// de règle — soit, aujourd'hui, la quasi-totalité des ventes. Un écran qui
    /// l'affiche annonce donc le cas général, pas le cas d'un vendeur ayant une
    /// règle propre.
    /// </summary>
    decimal CommissionRate { get; }

    /// <summary>Frais prestataire de paiement, en fraction du prix vendeur net.</summary>
    decimal ProviderFeeRate { get; }

    /// <summary>
    /// Commission sur la restauration, en fraction du prix restaurant net.
    ///
    /// DISTINCTE DE <see cref="CommissionRate"/>, ET IL LE FAUT.
    ///
    /// Une place de marché prélève quelques pour cent sur un produit qu'elle n'a
    /// ni stocké ni transporté ; la livraison de repas supporte la course, le
    /// délai et la fraîcheur, et se pratique bien plus haut. Un réglage unique
    /// aurait forcé à trancher entre deux métiers à chaque ajustement.
    /// </summary>
    decimal FoodCommissionRate { get; }
}
