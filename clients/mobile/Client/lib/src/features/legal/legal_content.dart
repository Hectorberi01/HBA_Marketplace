/// Textes légaux de l'application ACHETEUR — droit béninois.
///
/// Cadre applicable :
///  • Loi n° 2017-20 du 20 avril 2018 portant Code du numérique en République du
///    Bénin, modifiée par la loi n° 2020-35 du 6 janvier 2021.
///      – Livre IV « Du commerce électronique » : information préalable, garanties
///        légales, responsabilité des fournisseurs de biens et services en ligne.
///      – Livre V « De la protection des données à caractère personnel ».
///  • Autorité de contrôle : l'APDP.
///
/// Ce fichier dit la MÊME CHOSE que le site (hbaexpress-site) et que l'app
/// vendeur — mais du point de vue de l'ACHETEUR : ce sont ses droits qui sont
/// énoncés, pas les obligations du vendeur. Le fond juridique est commun ; la
/// rédaction s'adresse à celui qui la lit.
///
/// [Legal.version] est COMMUNE aux deux applications et au site : c'est elle que
/// le serveur enregistre. Elle change à chaque modification substantielle — et
/// alors, tout le monde doit ré-accepter.
library;

/// Une section d'un document légal.
class LegalSection {
  const LegalSection(this.title, this.body);

  final String title;
  final List<String> body;
}

class Legal {
  const Legal._();

  /// DOIT être identique à la version de l'app vendeur et du site.
  static const version = '2026-07-12';

  static const lastUpdated = '12 juillet 2026';

  static const company = 'HBA TECH ET TRADE';
  static const supportEmail = 'support@hba-express.com';

  // -------------------------------------------------------------------------
  // CONDITIONS GÉNÉRALES — côté acheteur
  // -------------------------------------------------------------------------

  static const terms = <LegalSection>[
    LegalSection('1. Objet', [
      "$company exploite la place de marché HBAExpress, qui met en relation des vendeurs professionnels vérifiés et des acheteurs.",
      "Ces conditions sont soumises au Code du numérique en République du Bénin (loi n° 2017-20 du 20 avril 2018, modifiée par la loi n° 2020-35 du 6 janvier 2021). Utiliser l'application vaut acceptation.",
    ]),
    LegalSection('2. Votre compte', [
      "Vous devez être majeur et fournir des informations exactes, notamment une adresse de livraison valable. Vous êtes responsable de vos identifiants.",
    ]),
    LegalSection('3. Information avant l’achat', [
      "Avant de commander, vous disposez : de l'identité du vendeur, des caractéristiques essentielles du produit, de son état (neuf ou occasion), de son prix TOUTES TAXES COMPRISES, des frais de livraison, du délai de préparation annoncé, des modalités de paiement et de livraison.",
      "Vous êtes également informé des conséquences d'une inexécution du vendeur, de la marche à suivre pour réclamer, des procédures de règlement des litiges, de l'existence et des conditions du droit de rétractation, et des modalités de retour, d'échange et de remboursement.",
      "Le récapitulatif de votre commande vous est présenté avant validation, et vous pouvez corriger toute erreur de saisie avant de confirmer.",
    ]),
    LegalSection('4. Paiement — votre argent est retenu', [
      "Vous payez par Mobile Money ou par carte. Aucun numéro de carte n'est stocké par la plateforme.",
      "Votre argent est RETENU par la plateforme : il n'est reversé au vendeur qu'après confirmation de la livraison. C'est ce mécanisme, et lui seul, qui vous protège — un vendeur qui n'expédie pas ne touche rien.",
    ]),
    LegalSection('5. Livraison', [
      "Le vendeur s'engage à expédier dans le délai qu'il a annoncé et à fournir un numéro de suivi.",
      "Le risque de perte ou de détérioration vous est transféré à la remise effective du colis.",
      "Une commande non expédiée dans un délai raisonnable peut être annulée à votre demande, et intégralement remboursée.",
    ]),
    LegalSection('6. Droit de rétractation', [
      "Conformément au Code du numérique, vous disposez d'un droit de rétractation pour les achats conclus à distance. Le délai, son point de départ et ses exceptions sont ceux fixés par la loi ; ils vous sont rappelés sur la fiche produit et avant tout paiement.",
      "Pour l'exercer, ouvrez une demande de retour depuis l'application. Le produit doit être renvoyé complet et dans son état d'origine.",
      "Ce droit ne s'applique pas dans les cas exclus par la loi — notamment les biens sur mesure, les biens périssables, et les biens descellés ne pouvant être renvoyés pour des raisons d'hygiène.",
    ]),
    LegalSection('7. Remboursement', [
      "Tout remboursement vous est versé SANS FRAIS, par le MÊME moyen de paiement que celui que vous avez utilisé — sauf accord exprès de votre part pour un autre moyen, et à condition que cela ne vous coûte rien.",
      "Il intervient sans délai injustifié après acceptation du retour ou après la décision d'arbitrage.",
    ]),
    LegalSection('8. Vos garanties légales', [
      "Le Code du numérique met trois garanties à la charge de tout vendeur en ligne. Elles vous sont dues de plein droit, et aucune clause ne peut les écarter :",
      "CONFORMITÉ — le produit livré correspond exactement à sa description, ses photos et l'état annoncé.",
      "ÉVICTION — le produit ne porte atteinte à aucun droit de propriété intellectuelle d'un tiers.",
      "VICES CACHÉS — le produit est garanti contre les défauts cachés qui le rendent impropre à son usage, ou qui diminuent tellement cet usage que vous ne l'auriez pas acheté, ou en auriez donné un moindre prix, si vous les aviez connus.",
    ]),
    LegalSection('9. Notre responsabilité', [
      "La plateforme est responsable envers vous de la bonne exécution des obligations nées du contrat conclu par son intermédiaire, y compris lorsque ces obligations sont exécutées par le vendeur ou par un prestataire (transporteur, prestataire de paiement).",
      "Elle peut s'en exonérer en prouvant que l'inexécution vous est imputable, qu'elle tient au fait imprévisible et insurmontable d'un tiers étranger à la fourniture, ou à un cas de force majeure.",
    ]),
    LegalSection('10. Réclamations et litiges', [
      "Ouvrez un litige depuis l'application : le vendeur est notifié et doit répondre. Sans réponse de sa part, la décision se prend sur vos seuls éléments.",
      "Vous pouvez aussi écrire à $supportEmail, ou nous joindre par téléphone et par courrier — les coordonnées figurent dans les mentions légales.",
      "Le droit béninois est applicable. À défaut de règlement amiable, les tribunaux compétents de la République du Bénin peuvent être saisis.",
    ]),
    LegalSection('11. Avis', [
      "Vous pouvez publier un avis sur un produit que vous avez acheté. Les avis mensongers, injurieux, ou publiés en échange d'une contrepartie sont supprimés.",
    ]),
    LegalSection('12. Modifications', [
      "Ces conditions peuvent évoluer. Toute modification substantielle vous sera présentée dans l'application, et votre accord vous sera de nouveau demandé.",
    ]),
  ];

  // -------------------------------------------------------------------------
  // CONFIDENTIALITÉ
  // -------------------------------------------------------------------------

  static const privacy = <LegalSection>[
    LegalSection('1. Responsable du traitement', [
      "$company est responsable des traitements de données réalisés au moyen de l'application. Ils relèvent du Livre V du Code du numérique et du contrôle de l'Autorité de protection des données à caractère personnel (APDP) du Bénin.",
      "Pour toute question : $supportEmail.",
    ]),
    LegalSection('2. Ce que nous collectons', [
      "Votre compte : nom, prénom, e-mail, téléphone, mot de passe (conservé sous forme d'empreinte, jamais en clair).",
      "Vos achats : adresses de livraison, commandes, paiements, avis publiés, favoris.",
      "Vos échanges avec les vendeurs via la messagerie, et — si vous les avez acceptées — un identifiant d'appareil pour les notifications.",
    ]),
    LegalSection('3. Pourquoi', [
      "Exécution du contrat : traiter et livrer vos commandes, encaisser le paiement, vous permettre de dialoguer avec le vendeur, gérer les retours et arbitrer les litiges.",
      "Obligation légale : conserver les pièces comptables et fiscales.",
      "Intérêt légitime : prévenir la fraude et sécuriser la plateforme.",
      "Consentement : notifications, et toute prospection commerciale — retirable à tout moment.",
      "Nous ne vendons pas vos données et ne faisons pas de publicité ciblée.",
    ]),
    LegalSection('4. Ce que voit le vendeur', [
      "Uniquement ce qui lui est nécessaire pour livrer : le nom du destinataire, l'adresse et le téléphone de livraison de la commande concernée.",
      "Il ne voit ni votre historique d'achats chez d'autres vendeurs, ni vos moyens de paiement.",
    ]),
    LegalSection('5. Nos prestataires', [
      "Paiement : notre prestataire de services de paiement, par lequel transitent les données de la transaction. Nous ne stockons aucun numéro de carte.",
      "Notifications : notre prestataire de messagerie push, qui reçoit l'identifiant de votre appareil.",
      "Hébergement et traitement d'images. Certains de ces prestataires traitent des données hors du Bénin ; ces transferts sont encadrés par le Livre V du Code du numérique.",
    ]),
    LegalSection('6. Sécurité', [
      "Vos identifiants de session sont conservés dans le coffre-fort sécurisé de votre téléphone (Keychain sur iOS, Keystore sur Android), jamais en clair. Les échanges avec nos serveurs sont chiffrés.",
      "En cas de violation de données susceptible de créer un risque pour vos droits, nous en informons l'APDP et, lorsque la loi l'exige, vous-même.",
    ]),
    LegalSection('7. Combien de temps', [
      "Compte : tant qu'il est actif.",
      "Commandes, facturation et paiements : au-delà, pour la durée imposée par les obligations comptables et fiscales. Ces données ne peuvent pas être effacées sur simple demande — c'est la loi, non un choix commercial.",
    ]),
    LegalSection('8. Vos droits', [
      "Accès, rectification, opposition, suppression, copie de vos données — dans les limites de conservation ci-dessus.",
      "Écrivez à $supportEmail depuis l'adresse de votre compte ; nous répondons sous 30 jours.",
      "Si vous estimez vos droits méconnus, vous pouvez saisir l'APDP.",
    ]),
  ];
}
