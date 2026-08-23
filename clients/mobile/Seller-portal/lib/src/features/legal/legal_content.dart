/// Textes légaux de l'application — droit BÉNINOIS.
///
/// Cadre applicable :
///  • Loi n° 2017-20 du 20 avril 2018 portant Code du numérique en République du
///    Bénin, modifiée par la loi n° 2020-35 du 6 janvier 2021.
///      – Livre IV « Du commerce électronique » : information préalable, garanties
///        légales, responsabilité des fournisseurs de biens et services en ligne.
///      – Livre V « De la protection des données à caractère personnel ».
///  • Autorité de contrôle : l'APDP.
///
/// CE FICHIER EST LA COPIE FIDÈLE des textes du site (hbaexpress-site,
/// src/content/legal.ts). Les deux DOIVENT dire la même chose : un vendeur qui
/// accepte dans l'app un texte différent de celui publié sur le site n'a rien
/// accepté d'opposable. Toute modification se fait DES DEUX CÔTÉS, et
/// [Legal.version] change.
///
/// Deux points restent à faire trancher par un juriste béninois, et sont donc
/// rédigés SANS être inventés :
///   1. le DÉLAI de rétractation exact (annoncer 7 jours quand la loi en prévoit
///      15 est une infraction ; en annoncer 15 quand elle en prévoit 7 nous engage
///      sur la promesse la plus forte) ;
///   2. les transferts de données hors du Bénin (Cloudinary, Firebase), qui
///      peuvent exiger une autorisation de l'APDP.
library;

/// Une section d'un document légal.
class LegalSection {
  const LegalSection(this.title, this.body);

  final String title;

  /// Un paragraphe par entrée.
  final List<String> body;
}

class Legal {
  const Legal._();

  /// Version des conditions.
  ///
  /// C'est ELLE que le serveur enregistre à l'acceptation, et c'est elle qu'on
  /// compare à la version acceptée par le compte. Une date, pas un numéro : on
  /// veut pouvoir répondre « quel texte, à quelle date » sans consulter un
  /// tableau de correspondance.
  ///
  /// À CHANGER à chaque modification substantielle des textes ci-dessous —
  /// et à changer À L'IDENTIQUE dans l'app cliente et sur le site. Ne pas la
  /// changer, c'est faire vivre les utilisateurs sous un texte qu'ils n'ont
  /// jamais lu ; la changer pour une virgule, c'est les harceler.
  static const version = '2026-07-12';

  static const lastUpdated = '12 juillet 2026';

  static const company = 'HBA TECH ET TRADE';
  static const supportEmail = 'support@hba-marketplace.fr';

  /// Commission plateforme et frais de paiement — DOIVENT rester alignés sur
  /// AppConfig et sur la section « Pricing » du backend.
  static const commissionPercent = 10;
  static const providerFeePercent = 5;

  // -------------------------------------------------------------------------
  // CONDITIONS GÉNÉRALES
  // -------------------------------------------------------------------------

  static const terms = <LegalSection>[
    LegalSection('1. Objet', [
      "$company exploite la place de marché HBAExpress, qui met en relation des vendeurs professionnels et des acheteurs.",
      "Ces conditions sont soumises au Code du numérique en République du Bénin (loi n° 2017-20 du 20 avril 2018, modifiée par la loi n° 2020-35 du 6 janvier 2021). Utiliser l'application vaut acceptation.",
    ]),
    LegalSection('2. Votre compte', [
      "Un compte vendeur est ouvert après vérification de votre entreprise : registre du commerce (RCCM), identité du gérant, justificatifs. Les informations fournies doivent être exactes et tenues à jour.",
      "Vous êtes responsable de vos identifiants et de tout ce qui est fait depuis votre compte.",
    ]),
    LegalSection('3. Commission et frais', [
      "Vous fixez votre prix NET — ce que vous percevez. La plateforme ajoute une commission de $commissionPercent % et des frais de paiement de $providerFeePercent % pour obtenir le prix payé par l'acheteur. Le calcul s'affiche avant chaque mise en vente.",
      "Aucun abonnement, aucun frais de publication, aucun frais de retrait. Un mois sans vente est un mois sans facture.",
    ]),
    LegalSection('4. Information de l’acheteur', [
      "Avant toute commande, l'acheteur doit disposer des caractéristiques essentielles du produit, de son état réel, du prix toutes taxes comprises, des frais de livraison, du délai de préparation que vous annoncez, et des modalités de retour et de remboursement.",
      "Une description inexacte engage votre responsabilité, et la nôtre à votre place : c'est pourquoi elle est un motif de retrait immédiat de la mise en vente.",
    ]),
    LegalSection('5. Garanties légales', [
      "Le Code du numérique met trois garanties à la charge de tout vendeur en ligne. Aucune clause ne peut les écarter, et vous les devez à chaque acheteur :",
      "CONFORMITÉ — le produit livré correspond exactement à sa description, ses photos et son état annoncé. Annoncer « neuf » un produit d'occasion est une inexécution.",
      "ÉVICTION — le produit ne porte atteinte à aucun droit de propriété intellectuelle. La contrefaçon entraîne la suspension.",
      "VICES CACHÉS — le produit est garanti contre les défauts cachés qui le rendent impropre à son usage, ou qui diminuent tellement cet usage que l'acheteur ne l'aurait pas acheté, ou en aurait donné un moindre prix, s'il les avait connus.",
    ]),
    LegalSection('6. Commandes et expéditions', [
      "Vous vous engagez à préparer et expédier toute commande payée dans le délai que vous avez annoncé, et à fournir un numéro de suivi.",
      "Une commande non expédiée dans un délai raisonnable peut être annulée et intégralement remboursée à l'acheteur.",
    ]),
    LegalSection('7. Paiement et séquestre', [
      "L'acheteur paie à la commande, mais les fonds sont RETENUS par la plateforme : ils ne vous sont pas immédiatement versés.",
      "Vous êtes crédité après confirmation de la livraison. C'est ce mécanisme qui protège l'acheteur — et qui, en retour, le convainc d'acheter chez vous.",
    ]),
    LegalSection('8. Retours, rétractation et remboursement', [
      "L'acheteur consommateur dispose d'un droit de rétractation pour les contrats conclus à distance, dans les conditions et le délai fixés par la loi, qui lui sont rappelés avant tout paiement.",
      "Tout remboursement est effectué SANS FRAIS pour l'acheteur, par le MÊME moyen de paiement que celui utilisé — c'est une règle du Code du numérique, pas une politique commerciale.",
      "Vous traitez les demandes de retour dans un délai raisonnable et motivez tout refus.",
    ]),
    LegalSection('9. Litiges', [
      "En cas de litige, la plateforme peut arbitrer. Sans réponse de votre part, la décision se prend sur les seuls éléments fournis par l'acheteur.",
      "Un remboursement décidé par arbitrage est prélevé sur votre solde.",
      "Le droit béninois est applicable. À défaut de règlement amiable, les tribunaux compétents de la République du Bénin peuvent être saisis.",
    ]),
    LegalSection('10. Vos versements', [
      "Vos gains sont crédités après confirmation de la livraison. Vous pouvez en demander le retrait vers votre compte Mobile Money : les fonds sont retenus dès la demande, puis versés après validation.",
      "Un versement refusé par l'opérateur est intégralement recrédité sur votre solde. Aucun frais de retrait n'est prélevé.",
    ]),
    LegalSection('11. Suspension', [
      "La plateforme peut suspendre une mise en vente, une boutique ou un compte en cas de manquement grave : contrefaçon, produit dangereux, non-expédition répétée, fraude, manipulation des avis.",
      "Vos gains acquis restent dus, sous réserve des litiges en cours et des remboursements dus aux acheteurs.",
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
      "$company est responsable des traitements de données réalisés au moyen de l'application. Ces traitements relèvent du Livre V du Code du numérique et du contrôle de l'Autorité de protection des données à caractère personnel (APDP) du Bénin.",
      "Pour toute question : $supportEmail.",
    ]),
    LegalSection('2. Ce que nous collectons', [
      "Votre compte : nom, prénom, e-mail, téléphone, mot de passe (conservé sous forme d'empreinte, jamais en clair).",
      "Votre boutique : nom, description, logo, pièces de vérification de l'entreprise, catalogue (produits, photos, prix, stocks), coordonnées de versement (opérateur Mobile Money, numéro), historique des retraits.",
      "Vos échanges avec les clients, et — si vous les avez acceptées — un identifiant d'appareil pour les notifications.",
    ]),
    LegalSection('3. Pourquoi', [
      "Exécution du contrat : afficher vos produits, traiter les commandes, vous verser vos gains, vous permettre de répondre aux clients, arbitrer les litiges.",
      "Obligation légale : vérifier l'existence de votre entreprise, conserver les pièces comptables et fiscales.",
      "Intérêt légitime : prévenir la fraude.",
      "Consentement : notifications, et toute prospection commerciale — retirable à tout moment.",
      "Nous ne vendons pas vos données et ne faisons pas de publicité ciblée.",
    ]),
    LegalSection('4. Ce que voient les acheteurs', [
      "Le nom de votre boutique, votre logo, votre description, vos produits, vos prix, votre note et vos réponses publiques aux avis.",
      "JAMAIS : votre e-mail, votre téléphone, vos pièces de vérification, vos coordonnées de versement, votre chiffre d'affaires.",
    ]),
    LegalSection('5. Nos prestataires', [
      "Paiements et versements Mobile Money : notre prestataire de paiement. Nous ne stockons aucun numéro de carte.",
      "Photos de produits : notre prestataire de traitement d'images, qui les détoure sur fond blanc puis les héberge. N'y faites figurer aucune donnée personnelle.",
      "Notifications : notre prestataire de messagerie push, qui reçoit l'identifiant de votre appareil.",
      "Certains de ces prestataires traitent des données hors du Bénin. Ces transferts sont encadrés par le Livre V du Code du numérique.",
    ]),
    LegalSection('6. Sécurité', [
      "Vos identifiants de session sont conservés dans le coffre-fort sécurisé de votre téléphone (Keychain sur iOS, Keystore sur Android), jamais en clair. Les échanges avec nos serveurs sont chiffrés.",
      "Votre session expire automatiquement et peut être révoquée à tout moment par déconnexion.",
      "En cas de violation de données susceptible de créer un risque pour vos droits, nous en informons l'APDP et, lorsque la loi l'exige, vous-même.",
    ]),
    LegalSection('7. Combien de temps', [
      "Compte : tant que votre boutique est active.",
      "Commandes, facturation et versements : au-delà, pour la durée imposée par les obligations comptables et fiscales. Ces données ne peuvent pas être effacées sur simple demande — c'est la loi, non un choix commercial.",
    ]),
    LegalSection('8. Vos droits', [
      "Accès, rectification, opposition, suppression, copie de vos données — dans les limites de conservation ci-dessus. La plupart des informations se corrigent directement dans l'application.",
      "Écrivez à $supportEmail depuis l'adresse de votre compte ; nous répondons sous 30 jours.",
      "Si vous estimez vos droits méconnus, vous pouvez saisir l'APDP.",
    ]),
  ];
}
