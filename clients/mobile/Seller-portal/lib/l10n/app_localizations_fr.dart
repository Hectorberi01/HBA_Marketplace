// ignore: unused_import
import 'package:intl/intl.dart' as intl;
import 'app_localizations.dart';

// ignore_for_file: type=lint

/// The translations for French (`fr`).
class AppLocalizationsFr extends AppLocalizations {
  AppLocalizationsFr([String locale = 'fr']) : super(locale);

  @override
  String get appTitle => 'HbaExpress PRO';

  @override
  String get commonCancel => 'Annuler';

  @override
  String get commonSave => 'Enregistrer';

  @override
  String get commonDelete => 'Supprimer';

  @override
  String get commonClose => 'Fermer';

  @override
  String get commonRetry => 'Réessayer';

  @override
  String get commonConfirm => 'Confirmer';

  @override
  String get commonContinue => 'Continuer';

  @override
  String get commonBack => 'Précédent';

  @override
  String get commonNext => 'Suivant';

  @override
  String get commonAdd => 'Ajouter';

  @override
  String get commonEdit => 'Modifier';

  @override
  String get commonSearch => 'Rechercher';

  @override
  String get commonLoading => 'Chargement…';

  @override
  String get commonYes => 'Oui';

  @override
  String get commonNo => 'Non';

  @override
  String get commonError => 'Une erreur est survenue.';

  @override
  String get settingsAppearanceTitle => 'Apparence';

  @override
  String get settingsTheme => 'Thème';

  @override
  String get settingsThemeAuto => 'Automatique';

  @override
  String get settingsThemeAutoDesc => 'Suit le réglage de votre téléphone';

  @override
  String get settingsThemeLight => 'Clair';

  @override
  String get settingsThemeLightDesc => 'Fond clair en permanence';

  @override
  String get settingsThemeDark => 'Sombre';

  @override
  String get settingsThemeDarkDesc => 'Fond sombre en permanence';

  @override
  String get settingsLanguage => 'Langue';

  @override
  String get settingsLanguageSystem => 'Automatique';

  @override
  String get settingsLanguageSystemDesc => 'Suit la langue de votre téléphone';

  @override
  String get settingsLanguageFrench => 'Français';

  @override
  String get settingsLanguageEnglish => 'Anglais';

  @override
  String get navHome => 'Accueil';

  @override
  String get navOrders => 'Commandes';

  @override
  String get navProducts => 'Produits';

  @override
  String get navMessages => 'Messages';

  @override
  String get navMore => 'Plus';

  @override
  String get homeTitle => 'Tableau de bord';

  @override
  String get homeStatsTooltip => 'Statistiques';

  @override
  String get homeNotificationsTooltip => 'Notifications';

  @override
  String get homeDefaultShopName => 'Ma boutique';

  @override
  String get homeGreeting => 'Bonjour';

  @override
  String get homeSubtitle => 'Voici l’activité de votre boutique.';

  @override
  String get homeKybInReviewTitle => 'Vérification en cours';

  @override
  String get homeKybInReviewSubtitle =>
      'Vous pourrez publier vos produits une fois votre profil validé.';

  @override
  String get homeKybRejectedTitle => 'Vérification refusée';

  @override
  String get homeKybRejectedSubtitle =>
      'Corrigez et redéposez vos documents pour publier vos produits.';

  @override
  String get homeKybPendingTitle => 'Vérifiez votre boutique';

  @override
  String get homeKybPendingSubtitle =>
      'Déposez vos documents (KYB) pour publier vos produits et être payé.';

  @override
  String get homeKpiToProcess => 'À traiter';

  @override
  String get homeKpiOrders => 'Commandes';

  @override
  String get homeKpiSales30d => 'Ventes (30 j)';

  @override
  String get homeKpiNetPayout30d => 'Net à percevoir (30 j)';

  @override
  String get homeKpiAverageRating => 'Note moyenne';

  @override
  String get homeKpiReviewsReceived => 'Avis reçus';

  @override
  String homeReturnsToProcess(int count) {
    String _temp0 = intl.Intl.pluralLogic(
      count,
      locale: localeName,
      other: '$count retours à traiter',
      one: '1 retour à traiter',
    );
    return '$_temp0';
  }

  @override
  String get homeReturnsDisputeWarning =>
      'Sans réponse, l’acheteur ouvrira un litige.';

  @override
  String get homeWalletTitle => 'Portefeuille';

  @override
  String get homeWalletManage => 'Gérer';

  @override
  String get homeWalletAvailable => 'Solde disponible';

  @override
  String get homeWalletPendingDelivery => 'En attente de livraison';

  @override
  String get homeWalletPendingWithdrawal => 'Retraits en cours';

  @override
  String get homeRecentOrders => 'Dernières commandes';

  @override
  String get homeStepCompleteShopTitle => 'Compléter votre boutique';

  @override
  String get homeStepCompleteShopSubtitle =>
      'Documents validés + compte de reversement';

  @override
  String get homeStepAddLocationTitle => 'Ajouter un lieu d\'expédition';

  @override
  String get homeStepAddLocationSubtitle =>
      'L\'adresse d\'où partent vos colis';

  @override
  String get homeStepCreateProductTitle => 'Créer un produit';

  @override
  String get homeStepCreateProductSubtitle => 'Photo, catégorie, description';

  @override
  String get homeStepPublishOfferTitle => 'Mettre un produit en vente';

  @override
  String get homeStepPublishOfferSubtitle =>
      'Fixez votre prix pour être achetable';

  @override
  String get homeChecklistTitle => 'Bien démarrer';

  @override
  String homeChecklistProgress(int done, int total) {
    return '$done/$total étape(s) — encore quelques pas pour vendre';
  }

  @override
  String get homeChecklistDismiss => 'Masquer';

  @override
  String get ordTitle => 'Commandes';

  @override
  String get ordSearchHint => 'Rechercher une commande (référence, SKU)';

  @override
  String get ordEmptyMessage => 'Aucune commande dans cette vue.';

  @override
  String get ordFilterAll => 'Toutes';

  @override
  String get ordFilterToProcess => 'À traiter';

  @override
  String get ordFilterShipped => 'Expédiées';

  @override
  String get ordFilterDelivered => 'Livrées';

  @override
  String get ordPaymentPending => 'Paiement en attente';

  @override
  String ordCustomerItems(String customer, int count) {
    return '$customer · $count article(s)';
  }

  @override
  String get ordDetailTitle => 'Détail de la commande';

  @override
  String get ordClient => 'Client';

  @override
  String get ordDate => 'Date';

  @override
  String get ordPayment => 'Paiement';

  @override
  String get ordPaid => 'Encaissé';

  @override
  String get ordPending => 'En attente';

  @override
  String get ordTotal => 'Total';

  @override
  String get ordPaymentNotCollectedWarning =>
      'Le paiement de cette commande n\'est pas encore encaissé. Attendez la confirmation avant de préparer l\'envoi.';

  @override
  String get ordArticles => 'Articles';

  @override
  String get ordShipping => 'Livraison';

  @override
  String get ordRecipient => 'Destinataire';

  @override
  String get ordAddress => 'Adresse';

  @override
  String get ordLandmark => 'Point de repère';

  @override
  String get ordQuartier => 'Quartier';

  @override
  String get ordCommune => 'Commune';

  @override
  String get ordCity => 'Commune';

  @override
  String get ordPhone => 'Téléphone';

  @override
  String ordDisputes(int count) {
    return 'Litiges ($count)';
  }

  @override
  String ordDisputeClosed(String date) {
    return 'Clos · $date';
  }

  @override
  String ordDisputeOpen(String age) {
    return 'Ouvert $age — répondez pour vous défendre';
  }

  @override
  String ordLineQtyPrice(int qty, String price) {
    return '$qty × $price';
  }

  @override
  String get accTitle => 'Mon compte';

  @override
  String get accLoading => 'Chargement…';

  @override
  String get accSectionShop => 'Ma boutique';

  @override
  String get accShopLabel => 'Boutique';

  @override
  String get accShopSubtitle => 'Profil, compte de versement, documents';

  @override
  String get accOffersLabel => 'Mises en vente';

  @override
  String get accOffersSubtitle => 'Prix et disponibilité de vos produits';

  @override
  String get accShipmentsLabel => 'Expéditions';

  @override
  String get accLocationsLabel => 'Lieux d\'expédition';

  @override
  String get accLocationsSubtitle => 'Adresses d\'où partent vos colis';

  @override
  String get accReturnsLabel => 'Retours';

  @override
  String get accReturnsSubtitle => 'Demandes de retour et remboursements';

  @override
  String get accReviewsLabel => 'Avis clients';

  @override
  String get accSectionMoney => 'Argent';

  @override
  String get accWalletLabel => 'Portefeuille';

  @override
  String get accWalletSubtitle => 'Soldes et retraits';

  @override
  String get accFinanceLabel => 'Finances';

  @override
  String get accFinanceSubtitle => 'Relevé et versements';

  @override
  String get accSectionAccount => 'Compte';

  @override
  String get accNotificationsLabel => 'Notifications';

  @override
  String get accProfileLabel => 'Profil et sécurité';

  @override
  String get accAppearanceLabel => 'Apparence';

  @override
  String get accAppearanceSubtitle => 'Thème clair ou sombre';

  @override
  String get accPrivacyLabel => 'Confidentialité';

  @override
  String get accPrivacySubtitle => 'Vos données et vos droits';

  @override
  String get accHelpLabel => 'Aide & support';

  @override
  String get accHelpSubtitle => 'FAQ et contact';

  @override
  String get accTermsLabel => 'Conditions d\'utilisation';

  @override
  String get accTermsSubtitle => 'Commission, versements, litiges';

  @override
  String get accCloseLabel => 'Fermer mon compte';

  @override
  String get accCloseSubtitle => 'Suspend la vente — réversible';

  @override
  String get accDeleteLabel => 'Supprimer définitivement mon compte';

  @override
  String get accDeleteSubtitle =>
      'Efface votre compte et vos données · irréversible';

  @override
  String get accLogout => 'Se déconnecter';

  @override
  String get accLogoutConfirmTitle => 'Se déconnecter ?';

  @override
  String get accLogoutConfirmBody =>
      'Vous devrez saisir à nouveau vos identifiants.';

  @override
  String get accCancel => 'Annuler';

  @override
  String get accCloseConfirmTitle => 'Fermer votre compte ?';

  @override
  String get accCloseConfirmBody =>
      'Vos produits seront immédiatement retirés de la vente. Votre compte et son historique sont conservés — la suppression définitive est décidée par l\'équipe. Vous pourrez demander la réactivation à tout moment.';

  @override
  String get accCloseConfirmAction => 'Fermer le compte';

  @override
  String get accCloseSuccess =>
      'Compte fermé. Vos produits ne sont plus en vente.';

  @override
  String get accDeleteSuccess => 'Votre compte a été supprimé.';

  @override
  String get accDeleteSheetTitle => 'Supprimer définitivement le compte';

  @override
  String get accDeleteSheetBody =>
      'Cette action est irréversible. Votre compte, votre boutique et vos produits seront supprimés, et vos données personnelles anonymisées. Les documents exigés par la loi (factures, versements) sont conservés sans lien avec votre identité.';

  @override
  String get accDeleteSheetUnderstand =>
      'Je comprends que cette suppression est définitive.';

  @override
  String get accPasswordLabel => 'Votre mot de passe';

  @override
  String get accPasswordHint => 'Pour confirmer que c\'est bien vous';

  @override
  String get accDeleteSheetAction => 'Supprimer mon compte';

  @override
  String get accReactivationPendingTitle => 'Réactivation en cours d\'examen';

  @override
  String get accClosedTitle => 'Compte fermé';

  @override
  String get accReactivationPendingBody =>
      'Votre demande de réactivation a été envoyée. Un administrateur va l\'examiner ; vos produits restent hors vente en attendant.';

  @override
  String get accClosedBody =>
      'Votre compte est fermé : vos produits ne sont plus en vente. Vous pouvez demander sa réactivation.';

  @override
  String get accRequestReactivation => 'Demander la réactivation';

  @override
  String get accReactivationSent =>
      'Demande envoyée. Un administrateur va l\'examiner.';

  @override
  String get prodTitle => 'Produits';

  @override
  String get prodNew => 'Nouveau';

  @override
  String get prodSearchHint => 'Rechercher un produit';

  @override
  String get prodEmpty =>
      'Aucun produit.\nAjoutez votre premier article pour commencer à vendre.';

  @override
  String get prodStep1Title => '1. Décrire le produit';

  @override
  String get prodStep1Subtitle => 'Catégorie, nom, description, photos';

  @override
  String get prodStep2Title => '2. La déclinaison';

  @override
  String get prodStep2Subtitle =>
      'La version vendue (taille, couleur… ou par défaut)';

  @override
  String get prodStep3Title => '3. Mise en vente & stock';

  @override
  String get prodStep3Subtitle => 'État, prix, lieu d\'expédition et quantité';

  @override
  String get prodHowToTitle => 'Comment créer un produit';

  @override
  String get prodHowToSubtitle =>
      'Un produit est vendable une fois ses 3 étapes complétées.';

  @override
  String get prodHowToCta => 'Créer mon produit';

  @override
  String get prodFilterAll => 'Tous';

  @override
  String get prodFilterActive => 'En ligne';

  @override
  String get prodFilterDraft => 'Brouillons';

  @override
  String get prodFilterArchived => 'Archivés';

  @override
  String get prodActionsTooltip => 'Actions';

  @override
  String get prodMenuOpen => 'Voir le détail';

  @override
  String get prodMenuPublish => 'Mettre en ligne';

  @override
  String get prodMenuDraft => 'Repasser en brouillon';

  @override
  String get prodMenuArchive => 'Archiver';

  @override
  String get prodUpdated => 'Produit mis à jour.';

  @override
  String get pdTitle => 'Fiche produit';

  @override
  String get pdActionsTooltip => 'Actions';

  @override
  String get pdMenuPreview => 'Aperçu client';

  @override
  String get pdEditSheet => 'Modifier la fiche';

  @override
  String get pdUnlist => 'Retirer de la vente';

  @override
  String get pdMenuPublish => 'Mettre en ligne';

  @override
  String get pdMenuDraft => 'Repasser en brouillon';

  @override
  String get pdMenuArchive => 'Archiver';

  @override
  String get pdUnlistTitle => 'Retirer de la vente ?';

  @override
  String pdUnlistBody(int count, String name) {
    return 'Les $count mise(s) en vente active(s) de « $name » seront mises en pause : le produit ne pourra plus être acheté. Vos photos, votre stock et vos prix sont conservés — vous pourrez le remettre en vente quand vous voudrez.';
  }

  @override
  String get pdUnlistSuccess => 'Produit retiré de la vente.';

  @override
  String get pdDeleteTitle => 'Supprimer ce produit ?';

  @override
  String get pdDeleteBody =>
      'Le produit, ses photos et ses déclinaisons seront supprimés. Cette action est définitive — préférez « Archiver » pour le retirer de la vente en gardant l\'historique.';

  @override
  String get pdDeleteSuccess => 'Produit supprimé.';

  @override
  String get pdUpdated => 'Produit mis à jour.';

  @override
  String pdPhotosTooLargeIgnored(int count) {
    return '$count photo(s) dépassent 5 Mo et ont été ignorées.';
  }

  @override
  String pdPhotosAdded(int count) {
    return '$count photo(s) ajoutée(s).';
  }

  @override
  String get pdGalleryEmpty => 'Aucune photo — vos acheteurs ne verront rien.';

  @override
  String get pdAddPhotos => 'Ajouter des photos';

  @override
  String pdPhotoCount(int count) {
    return '$count photo(s)';
  }

  @override
  String get pdPrimarySet => 'Photo principale définie.';

  @override
  String get pdPhotoDeleted => 'Photo supprimée.';

  @override
  String get pdPrimaryHint =>
      'La photo « Principale » est celle que voit l\'acheteur dans les listes.';

  @override
  String get pdPrimaryBadge => 'Principale';

  @override
  String get pdSetPrimary => 'Définir comme photo principale';

  @override
  String get pdDeletePhoto => 'Supprimer la photo';

  @override
  String get pdInfoSection => 'Informations';

  @override
  String get pdInfoCategory => 'Catégorie';

  @override
  String get pdInfoVariants => 'Déclinaisons';

  @override
  String get pdInfoNoDescription =>
      'Aucune description. Les acheteurs achètent ce qu\'ils comprennent : ajoutez-en une.';

  @override
  String pdVariantsSection(int count) {
    return 'Déclinaisons ($count)';
  }

  @override
  String get pdSectionAdd => 'AJOUTER';

  @override
  String get pdVariantsEmpty =>
      'Aucune déclinaison. Le stock et les mises en vente se rattachent à un SKU : créez au moins une déclinaison pour pouvoir vendre ce produit.';

  @override
  String get pdCreateVariant => 'Créer une déclinaison';

  @override
  String get pdDeleteVariantTitle => 'Supprimer la déclinaison ?';

  @override
  String pdDeleteVariantBody(String label, String sku) {
    return 'La déclinaison « $label » et le stock rattaché au SKU $sku ne seront plus proposés.';
  }

  @override
  String get pdVariantDeleted => 'Déclinaison supprimée.';

  @override
  String get pdVariantAdded => 'Déclinaison ajoutée.';

  @override
  String get pdNewVariant => 'Nouvelle déclinaison';

  @override
  String get pdVariantSkuIntro =>
      'Le SKU est votre référence interne : il porte le stock et la mise en vente. Il doit être unique.';

  @override
  String get pdSkuHelper =>
      'Généré automatiquement — modifiable. Laissé vide, il sera régénéré.';

  @override
  String get pdAttrLabel => 'Attribut';

  @override
  String get pdAttrHintSize => 'Taille';

  @override
  String get pdValueLabel => 'Valeur';

  @override
  String get pdRemoveAttr => 'Retirer cet attribut';

  @override
  String get pdAddAttr => 'Ajouter un attribut';

  @override
  String get pdBarcodeOptional => 'Code-barres (facultatif)';

  @override
  String get pdWeight => 'Poids';

  @override
  String get pdAddVariant => 'Ajouter la déclinaison';

  @override
  String get pdLocationShort => 'Lieu';

  @override
  String pdStockUnavailable(String error) {
    return 'Stock indisponible : $error';
  }

  @override
  String get pdNoStockForSku => 'Aucun stock enregistré pour ce SKU.';

  @override
  String get pdLocationFallback => 'Lieu';

  @override
  String pdStockAvailable(int count) {
    return '$count dispo';
  }

  @override
  String get pdStockLow => 'Bas';

  @override
  String pdStockLine(int onHand, int reserved, int threshold) {
    return 'En stock $onHand · Réservé $reserved · Seuil d\'\'alerte $threshold';
  }

  @override
  String get pdStockReceive => 'Réceptionner';

  @override
  String get pdStockAdjust => 'Ajuster ±';

  @override
  String get pdThresholdShort => 'Seuil';

  @override
  String get pdReceiveTitle => 'Réceptionner du stock';

  @override
  String get pdReceiveHint => 'Quantité reçue (entrée de marchandise).';

  @override
  String get pdQuantity => 'Quantité';

  @override
  String get pdAdjustTitle => 'Ajuster le stock';

  @override
  String get pdAdjustHint =>
      'Correction signée : −2 pour une perte, +2 pour un retour en rayon.';

  @override
  String get pdAdjustLabel => 'Écart (+ / −)';

  @override
  String get pdAlertThreshold => 'Seuil d\'alerte';

  @override
  String get pdThresholdHint =>
      'En dessous de ce niveau, le stock est signalé comme bas.';

  @override
  String get pdEnterInteger => 'Saisissez un nombre entier.';

  @override
  String get pdValuePositive => 'La valeur doit être positive.';

  @override
  String get pdAdjustZero => 'Un ajustement de zéro ne change rien.';

  @override
  String get pdStockUpdated => 'Stock mis à jour.';

  @override
  String get pdValidate => 'Valider';

  @override
  String get pdChooseLocation => 'Choisissez un lieu.';

  @override
  String get pdStockCreated => 'Stock créé.';

  @override
  String pdStockSkuTitle(String sku) {
    return 'Stock — SKU $sku';
  }

  @override
  String pdLocationsUnavailable(String error) {
    return 'Lieux indisponibles : $error';
  }

  @override
  String get pdNoShipLocation =>
      'Aucun lieu d\'expédition. Créez-en un depuis une mise en vente avant d\'enregistrer du stock.';

  @override
  String get pdShipLocation => 'Lieu d\'expédition';

  @override
  String get pdStockQty => 'Quantité en stock';

  @override
  String get pdCreateStock => 'Créer le stock';

  @override
  String pdOffersSection(int count) {
    return 'Mises en vente ($count)';
  }

  @override
  String pdOffersUnavailable(String error) {
    return 'Mises en vente indisponibles : $error';
  }

  @override
  String get pdNoOfferCanCreate =>
      'Aucune mise en vente : ce produit n\'est pas achetable, même en ligne. Créez une mise en vente pour fixer votre prix.';

  @override
  String get pdNoOfferNoVariant =>
      'Aucune mise en vente possible : créez d\'abord une déclinaison, c\'est elle qui porte le SKU de la mise en vente.';

  @override
  String get pdPutOnSale => 'Mettre en vente';

  @override
  String get pdNameMin3 => 'Le nom doit faire au moins 3 caractères.';

  @override
  String get pdSheetUpdated => 'Fiche mise à jour.';

  @override
  String get pdName => 'Nom';

  @override
  String get pdGtinOptional => 'GTIN (facultatif)';

  @override
  String get pdEanOptional => 'EAN (facultatif)';

  @override
  String get pwChooseCategory => 'Choisissez une catégorie.';

  @override
  String get pwNameMin3 => 'Le nom doit faire au moins 3 caractères.';

  @override
  String get pwDescribeMin10 => 'Décrivez le produit (10 caractères minimum).';

  @override
  String get pwAddPhoto => 'Ajoutez au moins une photo.';

  @override
  String get pwPhotoTooLarge => 'Une photo dépasse 5 Mo. Retirez-la.';

  @override
  String get pwSkuRequired => 'Le SKU de la déclinaison est obligatoire.';

  @override
  String get pwPriceInvalid => 'Indiquez un prix valide.';

  @override
  String get pwChooseLocation => 'Choisissez un lieu d\'expédition.';

  @override
  String get pwStockMin1 => 'Indiquez un stock d\'au moins 1 unité.';

  @override
  String get pwStepProduct => 'produit';

  @override
  String get pwStepVariant => 'déclinaison';

  @override
  String get pwStepOffer => 'mise en vente';

  @override
  String get pwStepStock => 'stock';

  @override
  String get pwCreatedSuccess =>
      'Produit prêt : déclinaison, mise en vente et stock créés.';

  @override
  String get pwNothingKept => 'Rien n\'a été conservé.';

  @override
  String get pwDraftKept =>
      'Le brouillon a été gardé — supprimez-le depuis Produits.';

  @override
  String pwStepFailed(String step, String error, String tail) {
    return 'Échec à l\'\'étape « $step » : $error $tail';
  }

  @override
  String get pwNewProduct => 'Nouveau produit';

  @override
  String get pwCategoriesUnavailable => 'Catégories indisponibles.';

  @override
  String get pwProductName => 'Nom du produit';

  @override
  String get pwProductNameHint => 'ex. Robe wax grande taille';

  @override
  String get pwDescriptionHelper =>
      'Matière, dimensions, ce qui est inclus — c\'est ce qui déclenche l\'achat.';

  @override
  String get pwMoreInfo => 'Informations complémentaires (facultatif)';

  @override
  String get pwBrandOptional => 'Marque (facultatif)';

  @override
  String get pwNone => 'Aucune';

  @override
  String get pwGtinOptional => 'Code GTIN (facultatif)';

  @override
  String get pwEanOptional => 'Code EAN (facultatif)';

  @override
  String get pwTagsOptional => 'Tags (facultatif)';

  @override
  String get pwTagsHint => 'ex. wax, coton, fait main';

  @override
  String get pwTagsHelper => 'Séparez les tags par une virgule.';

  @override
  String get pwVariantInfo =>
      'Une déclinaison est une version de votre produit (ex. une taille, une couleur). Un produit simple n\'en a qu\'une. Le SKU l\'identifie et porte le stock.';

  @override
  String get pwSkuLabel => 'SKU (référence interne)';

  @override
  String get pwSkuHelper =>
      'Généré automatiquement. Modifiable si vous avez votre propre système.';

  @override
  String get pwAttrLogistics => 'Attributs & logistique (facultatif)';

  @override
  String get pwAttrIntro =>
      'Attributs (taille, couleur…) — seulement si cette déclinaison en a.';

  @override
  String get pwAttrLabel => 'Attribut';

  @override
  String get pwValueLabel => 'Valeur';

  @override
  String get pwAddAttr => 'Ajouter un attribut';

  @override
  String get pwBarcodeOptional => 'Code-barres (facultatif)';

  @override
  String get pwWeightG => 'Poids (g)';

  @override
  String get pwCondition => 'État du produit';

  @override
  String get pwYourPrice => 'Votre prix (net perçu)';

  @override
  String get pwLocationsUnavailable => 'Lieux indisponibles.';

  @override
  String get pwShipLocation => 'Lieu d\'expédition';

  @override
  String get pwAddLocation => 'Ajouter un lieu';

  @override
  String get pwStockQty => 'Stock (quantité)';

  @override
  String get pwAlertThreshold => 'Seuil d\'alerte';

  @override
  String get pwHandlingTime => 'Délai de préparation';

  @override
  String pwDays(int count) {
    String _temp0 = intl.Intl.pluralLogic(
      count,
      locale: localeName,
      other: '$count jours',
      one: '1 jour',
    );
    return '$_temp0';
  }

  @override
  String get pwReviewTitle => 'Vérifiez avant de créer';

  @override
  String get pwSummaryProduct => 'Produit';

  @override
  String get pwName => 'Nom';

  @override
  String get pwCategory => 'Catégorie';

  @override
  String pwPhotosCount(int count) {
    return 'Photos ($count)';
  }

  @override
  String get pwSummaryVariant => 'Déclinaison';

  @override
  String get pwAttributes => 'Attributs';

  @override
  String get pwSummaryOffer => 'Mise en vente & stock';

  @override
  String get pwConditionShort => 'État';

  @override
  String get pwNetPrice => 'Votre prix net';

  @override
  String get pwDisplayedPrice => 'Prix affiché client';

  @override
  String get pwConfirmNote =>
      'En confirmant, le produit, sa déclinaison, sa mise en vente et son stock sont créés d\'un coup. Le produit sera visible des acheteurs une fois votre boutique validée (KYB).';

  @override
  String get pwAddress => 'Adresse';

  @override
  String get pwCreateAndSell => 'Créer et mettre en vente';

  @override
  String get pwStepBarProduct => 'Produit';

  @override
  String get pwStepBarVariant => 'Déclinaison';

  @override
  String get pwStepBarOffer => 'Mise en vente';

  @override
  String get pwStepBarSummary => 'Résumé';

  @override
  String pwStepIndicator(int current, int total, String label) {
    return 'Étape $current/$total — $label';
  }

  @override
  String get pwNoLocationBody =>
      'Aucun lieu d\'expédition. Il en faut un pour mettre en vente (l\'adresse d\'où partent vos colis).';

  @override
  String get pwAddShipLocation => 'Ajouter un lieu d\'expédition';

  @override
  String get pcChooseCategory => 'Choisissez une catégorie.';

  @override
  String get pcAddPhoto =>
      'Ajoutez au moins une photo : sans image, le produit ne peut pas être créé.';

  @override
  String pcPhotosTooLarge(int count) {
    return '$count photo(s) dépassent 5 Mo. Retirez-les ou réessayez le détourage.';
  }

  @override
  String pcDuplicateAttr(String key) {
    return 'Attribut « $key » en double.';
  }

  @override
  String get pcCreatedDraft => 'Produit créé en brouillon.';

  @override
  String get pcDiscardTitle => 'Abandonner ce produit ?';

  @override
  String get pcDiscardBody =>
      'Ce que vous avez saisi, photos comprises, sera perdu.';

  @override
  String get pcKeepEditing => 'Continuer la saisie';

  @override
  String get pcDiscard => 'Abandonner';

  @override
  String get pcNewProduct => 'Nouveau produit';

  @override
  String get pcDraftNote =>
      'Le produit est créé en brouillon. Vous créerez ensuite une mise en vente (prix, stock) pour le mettre en vente.';

  @override
  String get pcLegendInfo => 'Informations';

  @override
  String get pcCategoriesUnavailable => 'Catégories indisponibles.';

  @override
  String get pcProductName => 'Nom du produit';

  @override
  String get pcProductNameHint => 'ex. Robe wax grande taille';

  @override
  String get pcNameMin3 => '3 caractères minimum';

  @override
  String get pcDescriptionHelper =>
      'Matière, dimensions, ce qui est inclus — c\'est ce qui déclenche l\'achat.';

  @override
  String get pcDescribeMin10 => 'Décrivez le produit (10 caractères minimum)';

  @override
  String get pcLegendId => 'Identification';

  @override
  String get pcBrandsUnavailable =>
      'Marques indisponibles — vous pouvez créer le produit sans.';

  @override
  String get pcBrandOptional => 'Marque (facultatif)';

  @override
  String get pcNone => 'Aucune';

  @override
  String get pcGtinOptional => 'Code GTIN (facultatif)';

  @override
  String get pcGtinHint => '8 à 14 chiffres';

  @override
  String get pcEanOptional => 'Code EAN (facultatif)';

  @override
  String get pcEanHint => '8 ou 13 chiffres';

  @override
  String get pcGroupOptional => 'Groupe de produit (facultatif)';

  @override
  String get pcGroupHint => 'Identifiant du groupe';

  @override
  String get pcGroupHelper =>
      'Regroupe des produits liés (même modèle, coloris différents).';

  @override
  String get pcInvalidId => 'Identifiant invalide';

  @override
  String get pcAddTag => 'Ajouter un tag (facultatif)';

  @override
  String get pcTagHint => 'ex. wax, coton, fait main';

  @override
  String get pcTagHelper =>
      'Aide les acheteurs à trouver le produit. Entrée pour valider.';

  @override
  String get pcLegendAttr => 'Attributs';

  @override
  String get pcAttrIntro =>
      'Caractéristiques communes à toutes les déclinaisons (matière, garantie…). Ce qui change d\'une déclinaison à l\'autre — taille, couleur — se règle dans les déclinaisons.';

  @override
  String get pcKey => 'Clé';

  @override
  String get pcValueLabel => 'Valeur';

  @override
  String get pcRemoveAttr => 'Retirer l\'attribut';

  @override
  String get pcAddAttr => 'Ajouter un attribut';

  @override
  String get pcCreateProduct => 'Créer le produit';

  @override
  String pcBarcodeDigits(String lengths) {
    return '$lengths chiffres';
  }

  @override
  String pcMaxPhotos(int count) {
    return '$count photos au maximum.';
  }

  @override
  String pcOnlyAdded(int room, int max) {
    return 'Seules $room photo(s) ont été ajoutées ($max au maximum).';
  }

  @override
  String get pcRetryFailed =>
      'Le détourage a encore échoué. La photo partira telle quelle.';

  @override
  String get pcPrimaryHint =>
      'La 1re photo est l\'image principale — la seule visible dans les listes. Appuyez sur une photo pour la déplacer en tête ou la retirer.';

  @override
  String pcFailedBanner(int count) {
    return '$count photo(s) n\'\'ont pas pu être détourées : elles partiront avec leur fond d\'\'origine. Appuyez dessus pour réessayer.';
  }

  @override
  String pcOversizedBanner(int count) {
    return '$count photo(s) dépassent 5 Mo et seront refusées par le serveur. Retirez-les.';
  }

  @override
  String get pcPhotoRequired => 'Au moins une photo est requise.';

  @override
  String get pcPrimaryBadge => 'Principale';

  @override
  String get pcSetPrimary => 'Définir comme photo principale';

  @override
  String get pcSetPrimarySub =>
      'C\'est l\'image que verront les acheteurs dans les listes.';

  @override
  String get pcRetryProcessing => 'Réessayer le détourage';

  @override
  String get pcRetrySub => 'Fond blanc automatique via le serveur.';

  @override
  String get pcRemovePhoto => 'Retirer la photo';

  @override
  String get pcAddPhotos => 'Ajouter des photos';

  @override
  String get pcPickerSubtitle =>
      'Détourage automatique sur fond blanc · 8 photos maximum';

  @override
  String get pcFromGallery => 'Depuis la galerie';

  @override
  String get pcMultiSelect => 'Sélection multiple possible.';

  @override
  String get pcTakePhoto => 'Prendre une photo';

  @override
  String get pcOptional => 'facultatif';

  @override
  String get cpChooseCategory => 'Choisissez une catégorie';

  @override
  String cpChooseThis(String name) {
    return 'Choisir « $name »';
  }

  @override
  String get cpSubcategories => 'Sous-catégories';

  @override
  String get cpCategory => 'Catégorie';

  @override
  String get ppvTitle => 'Aperçu client';

  @override
  String get ppvPriceUnavailable => 'Prix indisponible';

  @override
  String get ppvNoDescription =>
      'Aucune description — l\'acheteur ne saura pas ce qu\'il achète.';

  @override
  String get ppvAddToCart => 'Ajouter au panier';

  @override
  String get ppvUnavailable => 'Indisponible';

  @override
  String get ppvPreviewNote =>
      'Aperçu — aucune action réelle depuis cet écran.';

  @override
  String get ppvBuyable => 'Ce produit est visible et achetable.';

  @override
  String get ppvReasonBoth =>
      'Le produit est en brouillon ET n\'a aucune mise en vente active.';

  @override
  String get ppvReasonDraft =>
      'Le produit est en brouillon : les acheteurs ne le voient pas.';

  @override
  String get ppvReasonNoOffer =>
      'Aucune mise en vente active : le produit est visible mais NE PEUT PAS être acheté.';

  @override
  String get ppvNoPhoto => 'Aucune photo — le produit ne sera pas remarqué.';

  @override
  String get ppvInStock => 'En stock';

  @override
  String get ppvOutOfStock => 'Rupture de stock';

  @override
  String ppvShippedIn(int days) {
    return 'Expédié sous $days j';
  }

  @override
  String get imgpReady => 'Photos préparées';

  @override
  String get imgpPreparing => 'Préparation des photos…';

  @override
  String get imgpDoneDesc =>
      'Détourées sur fond blanc. Comparez, puis validez.';

  @override
  String imgpInProgress(int done, int total) {
    return 'Détourage en cours — $done/$total';
  }

  @override
  String get imgpAllFailed =>
      'Le détourage a échoué. Vous pouvez envoyer les photos d\'origine, mais elles n\'auront pas le fond blanc du catalogue.';

  @override
  String imgpSomeFailed(int count) {
    return '$count photo(s) n\'\'ont pas pu être détourées. Elles seront envoyées telles quelles.';
  }

  @override
  String get imgpRetry => 'Réessayer le détourage';

  @override
  String get imgpUse => 'Utiliser';

  @override
  String get imgpBefore => 'Avant';

  @override
  String get imgpAfter => 'Après';

  @override
  String get imgpNotProcessed => 'Non détourée';

  @override
  String get authLoginTagline =>
      'Boutique, restaurant, commandes et revenus au même endroit.';

  @override
  String get authLoginEmailLabel => 'Téléphone ou e-mail';

  @override
  String get authLoginEmailInvalid => 'Saisissez un e-mail valide';

  @override
  String get authLoginPasswordLabel => 'Mot de passe';

  @override
  String get authLoginPasswordMin => '6 caractères minimum';

  @override
  String get authLoginForgotPassword => 'Mot de passe oublié ?';

  @override
  String authLoginEnableBio(String label) {
    return 'Activer $label pour les prochaines connexions';
  }

  @override
  String get authLoginMfaLabel => 'Code de vérification';

  @override
  String get authLoginResendVerification => 'Renvoyer le code de vérification';

  @override
  String get authLoginSignIn => 'Se connecter';

  @override
  String authLoginSignInWithBio(String label) {
    return 'Se connecter avec $label';
  }

  @override
  String get authLoginNoAccount =>
      'Vous n\'avez pas encore de compte vendeur ?';

  @override
  String get authLoginCreateAccount => 'Créer un compte partenaire';

  @override
  String get authLoginEnterEmail => 'Saisissez votre adresse e-mail.';

  @override
  String get authLoginNoAccountToVerify =>
      'Aucun compte à vérifier pour cette adresse.';

  @override
  String get authLoginBiometricReason => 'Connectez-vous à HbaExpress PRO';

  @override
  String get authRegisterTitle => 'Créer un compte vendeur';

  @override
  String get authRegisterHeadline => 'Ouvrez votre boutique';

  @override
  String get authRegisterSubtitle =>
      'Créez votre compte en quelques minutes. Vous pourrez publier vos produits une fois votre profil validé par notre équipe.';

  @override
  String get authRegisterShopNameLabel => 'Nom de la boutique';

  @override
  String get authRegisterShopNameHint => 'Ma Boutique';

  @override
  String get authRegisterShopNameRequired => 'Nom de boutique requis';

  @override
  String get authRegisterFirstNameLabel => 'Prénom';

  @override
  String get authRegisterLastNameLabel => 'Nom';

  @override
  String get authRegisterRequired => 'Requis';

  @override
  String get authRegisterEmailLabel => 'Email';

  @override
  String get authRegisterEmailInvalid => 'E-mail invalide';

  @override
  String get authRegisterPhoneLabel => 'Téléphone';

  @override
  String get authRegisterPhoneRequired => 'Téléphone requis';

  @override
  String get authRegisterPasswordLabel => 'Mot de passe';

  @override
  String get authRegisterPasswordMin => '6 caractères minimum';

  @override
  String get authRegisterLegalNameLabel => 'Raison sociale';

  @override
  String get authRegisterActivityLabel => 'Activité';

  @override
  String get authRegisterActivityHint => 'Prêt-à-porter, électronique…';

  @override
  String get authRegisterAddressLabel => 'Adresse';

  @override
  String get authRegisterAddressHint => 'Rue, quartier';

  @override
  String get authRegisterCityLabel => 'Commune';

  @override
  String get authRegisterManagerLabel => 'Gérant';

  @override
  String get authRegisterManagerHint => 'Nom du représentant légal';

  @override
  String get authRegisterCompanyPhoneLabel => 'Téléphone société';

  @override
  String get authRegisterCompanyToggle => 'Informations société (facultatif)';

  @override
  String get authRegisterContinue => 'Continuer';

  @override
  String get authRegisterHaveAccount => 'J\'ai déjà un compte — Se connecter';

  @override
  String get authVerifyEnterCode => 'Entrez le code à 6 chiffres.';

  @override
  String get authVerifyEnterShopName => 'Entrez le nom de votre boutique.';

  @override
  String get authVerifyAccountCreated =>
      'Compte créé. Connectez-vous pour accéder à votre boutique.';

  @override
  String get authVerifyCodeResent => 'Un nouveau code vous a été envoyé.';

  @override
  String get authVerifyTitle => 'Vérification';

  @override
  String get authVerifyHeadline => 'Entrez votre code';

  @override
  String get authVerifySentTo => 'Nous avons envoyé un code à 6 chiffres à\n';

  @override
  String get authVerifyShopNameLabel => 'Nom de votre boutique';

  @override
  String get authVerifyShopNameHint => 'Ma Boutique';

  @override
  String get authVerifyValidate => 'Valider';

  @override
  String get authVerifySending => 'Envoi…';

  @override
  String get authVerifyResend => 'Renvoyer le code';

  @override
  String get authForgotTitle => 'Mot de passe oublié';

  @override
  String get authForgotHeadline => 'Réinitialiser le mot de passe';

  @override
  String get authForgotSubtitle =>
      'Entrez l’e-mail de votre compte : nous vous enverrons un code à 6 chiffres pour choisir un nouveau mot de passe.';

  @override
  String get authForgotEmailInvalid => 'E-mail invalide';

  @override
  String get authForgotSendCode => 'Envoyer le code';

  @override
  String get authForgotBackToLogin => 'Retour à la connexion';

  @override
  String get authResetSuccess => 'Mot de passe réinitialisé. Connectez-vous.';

  @override
  String get authResetCodeResent => 'Un nouveau code vous a été envoyé.';

  @override
  String get authResetTitle => 'Nouveau mot de passe';

  @override
  String get authResetHeadline => 'Choisir un nouveau mot de passe';

  @override
  String get authResetSentTo => 'Entrez le code à 6 chiffres envoyé à\n';

  @override
  String get authResetCodeLabel => 'Code de vérification';

  @override
  String get authResetCodeRequired => 'Code à 6 chiffres requis';

  @override
  String get authResetPasswordLabel => 'Nouveau mot de passe';

  @override
  String get authResetPasswordMin => '6 caractères minimum';

  @override
  String get authResetSubmit => 'Réinitialiser';

  @override
  String get authResetSending => 'Envoi…';

  @override
  String get authResetResend => 'Renvoyer le code';

  @override
  String get authSplashTagline => 'Votre boutique, dans votre poche';

  @override
  String get offListTitle => 'Mises en vente';

  @override
  String get offListNewOffer => 'Nouvelle mise en vente';

  @override
  String get offListEmpty =>
      'Aucune mise en vente.\nUne mise en vente, c\'est le prix auquel vous vendez un produit.';

  @override
  String get offCardActionsTooltip => 'Actions';

  @override
  String get offCardEditPrice => 'Modifier le prix';

  @override
  String get offCardRemoveDiscount => 'Retirer la remise';

  @override
  String get offCardApplyDiscount => 'Appliquer une remise';

  @override
  String get offCardPause => 'Mettre en pause';

  @override
  String get offCardReactivate => 'Réactiver';

  @override
  String offCardHandling(int days) {
    return 'Préparation $days j';
  }

  @override
  String get offCardYouReceive => 'Vous percevez';

  @override
  String get offCardCustomerPays => 'Le client paie';

  @override
  String get offCardPromo => 'Promo';

  @override
  String offCardBefore(String price) {
    return 'avant $price';
  }

  @override
  String offCardSpreadNote(int commission, int fee) {
    return 'Écart : commission plateforme ($commission %) + frais de paiement ($fee %).';
  }

  @override
  String get offSheetDiscountRemoved => 'Remise retirée.';

  @override
  String get offSheetBackOnSale => 'Remise en vente.';

  @override
  String get offSheetSalePaused => 'Vente mise en pause.';

  @override
  String get offSheetYouReceive => 'Vous percevez';

  @override
  String get offSheetCommission => 'Commission plateforme';

  @override
  String get offSheetPaymentFees => 'Frais de paiement';

  @override
  String get offSheetDisplayedPrice => 'Prix affiché au client';

  @override
  String get offSheetChooseProduct => 'Choisissez un produit.';

  @override
  String get offSheetChooseVariant =>
      'Choisissez la déclinaison (SKU) concernée.';

  @override
  String get offSheetChooseStore => 'Choisissez une boutique';

  @override
  String get offSheetStore => 'Boutique';

  @override
  String get offSheetChooseProductFirst => 'Choisissez d’abord un produit.';

  @override
  String get offSheetNoVariants =>
      'Ce produit n’a aucune déclinaison. Ajoutez-en une depuis sa fiche avant de le mettre en vente.';

  @override
  String get offSheetNoStores =>
      'Aucune boutique sur ce compte. Créez-en une avant de mettre un article en vente.';

  @override
  String get offSheetChooseLocation => 'Choisissez un lieu d\'expédition.';

  @override
  String get offSheetOfferCreated => 'Mise en vente créée.';

  @override
  String get offSheetDuplicateTitle =>
      'Cette déclinaison a déjà une mise en vente';

  @override
  String get offSheetDuplicateBody =>
      'Chaque déclinaison ne porte qu\'une seule mise en vente : son stock est unique et ne peut pas être à la fois neuf et d\'occasion.\n\nPour vendre ce produit dans une autre condition, créez une seconde déclinaison (ex. « Occasion ») depuis la fiche produit, puis ajoutez-y son mise en vente.';

  @override
  String get offSheetGotIt => 'Compris';

  @override
  String get offSheetNewOfferTitle => 'Nouvelle mise en vente';

  @override
  String offSheetProductsUnavailable(String error) {
    return 'Produits indisponibles : $error';
  }

  @override
  String get offSheetProduct => 'Produit';

  @override
  String get offSheetVariantSku => 'Déclinaison (SKU)';

  @override
  String get offSheetSkuInternalRef => 'SKU (référence interne)';

  @override
  String get offSheetSkuRequired => 'SKU obligatoire';

  @override
  String get offSheetCondition => 'État du produit';

  @override
  String get offSheetConditionHint =>
      'Une déclinaison = une seule mise en vente. Pour vendre en Neuf ET Occasion, créez une déclinaison par condition.';

  @override
  String get offSheetYourPrice => 'Votre prix (net perçu)';

  @override
  String get offSheetInvalidPrice => 'Prix invalide';

  @override
  String offSheetLocationsUnavailable(String error) {
    return 'Lieux d\'\'expédition indisponibles : $error';
  }

  @override
  String get offSheetNoLocations =>
      'Aucun lieu d\'expédition. Ajoutez-en un avant de mettre en vente.';

  @override
  String get offSheetShipFrom => 'Lieu d\'expédition';

  @override
  String get offSheetHandlingTime => 'Délai de préparation';

  @override
  String get offSheetDay1 => '1 jour';

  @override
  String get offSheetDays2 => '2 jours';

  @override
  String get offSheetDays3 => '3 jours';

  @override
  String get offSheetDays5 => '5 jours';

  @override
  String get offSheetPublish => 'Mettre en vente';

  @override
  String get offSheetInvalidPriceNotice => 'Prix invalide.';

  @override
  String get offSheetPriceUpdated => 'Prix mis à jour.';

  @override
  String get offSheetSave => 'Enregistrer';

  @override
  String get offSheetInvalidDiscount =>
      'Remise invalide : le prix réduit doit rester positif et inférieur au prix actuel.';

  @override
  String get offSheetDiscountApplied => 'Remise appliquée.';

  @override
  String get offSheetApplyDiscountTitle => 'Appliquer une remise';

  @override
  String offSheetCurrentPriceNote(String price) {
    return 'Prix actuel : $price. La remise réduit votre prix net ; la commission est recalculée sur le prix réduit.';
  }

  @override
  String get offSheetDiscountType => 'Type de remise';

  @override
  String get offSheetPercentage => 'Pourcentage (%)';

  @override
  String get offSheetFixedAmount => 'Montant fixe';

  @override
  String get offSheetPercentageLabel => 'Pourcentage';

  @override
  String get offSheetAmountLabel => 'Montant';

  @override
  String get offSheetPromoEndOptional => 'Fin de la promo (optionnel)';

  @override
  String offSheetUntil(String date) {
    return 'Jusqu\'\'au $date';
  }

  @override
  String get offSheetNoEndDate => 'Sans échéance';

  @override
  String get offSheetApplyDiscountBtn => 'Appliquer la remise';

  @override
  String get offLocationsTitle => 'Lieux d\'expédition';

  @override
  String get offLocationsAdd => 'Ajouter';

  @override
  String get offLocationsEmpty =>
      'Aucun lieu d\'expédition.\nAjoutez l\'adresse d\'où partent vos colis : elle est indispensable pour mettre en vente.';

  @override
  String get offLocationsDeleteTitle => 'Supprimer ce lieu ?';

  @override
  String offLocationsDeleteBody(String label) {
    return '« $label » ne sera plus proposé comme point de départ. Les mises en vente qui l\'\'utilisent devront pointer vers un autre lieu.';
  }

  @override
  String get offLocationsCancel => 'Annuler';

  @override
  String get offLocationsDelete => 'Supprimer';

  @override
  String get offLocationsDeleted => 'Lieu supprimé.';

  @override
  String get offLocationsDeleteTooltip => 'Supprimer';

  @override
  String get offLocationsAdded => 'Lieu ajouté.';

  @override
  String get offLocationsNewTitle => 'Nouveau lieu d\'expédition';

  @override
  String get offLocationsNewSubtitle =>
      'L\'adresse d\'où partent vos colis. Elle sert de point de départ à vos mises en vente.';

  @override
  String get offLocationsAddressLabel => 'Adresse';

  @override
  String get offLocationsAddressHint => 'Rue, numéro, quartier';

  @override
  String get offLocationsAddressRequired => 'Adresse obligatoire';

  @override
  String get offLocationsAddBtn => 'Ajouter le lieu';

  @override
  String get msgActionUnavailable =>
      'Cette action n\'est pas encore disponible sur le serveur.';

  @override
  String get msgEmpty => 'Aucun message. Écrivez le premier.';

  @override
  String get msgDeleted => 'Message supprimé';

  @override
  String get msgHideForMe => 'Supprimer pour moi';

  @override
  String get msgHideForMeHint => 'Le client continuera de le voir';

  @override
  String get msgDeleteForEveryone => 'Supprimer pour tout le monde';

  @override
  String get msgDeleteForEveryoneHint =>
      'Le message sera remplacé par « Message supprimé »';

  @override
  String get msgAttachImage => 'Joindre une image';

  @override
  String get msgInputHint => 'Votre message…';

  @override
  String get msgNoConversations =>
      'Aucune conversation.\nVos clients vous écriront ici.';

  @override
  String get msgNewThread => 'Nouveau fil';

  @override
  String get revTitle => 'Avis';

  @override
  String get revEmpty => 'Aucun avis pour le moment.';

  @override
  String get revYourReply => 'Votre réponse';

  @override
  String get revReply => 'Répondre';

  @override
  String get revFlagTooltip => 'Signaler cet avis';

  @override
  String get revReplyTitle => 'Répondre à cet avis';

  @override
  String get revReplyPublicNote =>
      'Votre réponse est publique : elle sera lue par tous vos futurs clients.';

  @override
  String get revReplyHint => 'Votre réponse…';

  @override
  String get revReplyPublished => 'Réponse publiée.';

  @override
  String get revPublish => 'Publier';

  @override
  String get revFlagConfirmTitle => 'Signaler cet avis ?';

  @override
  String get revFlagConfirmBody =>
      'L\'avis sera examiné par la modération. Il reste visible tant qu\'aucune décision n\'est prise.';

  @override
  String get revCancel => 'Annuler';

  @override
  String get revFlag => 'Signaler';

  @override
  String get revFlaggedSuccess => 'Avis signalé à la modération.';

  @override
  String get dispTitle => 'Litige';

  @override
  String get dispEmpty => 'Aucun échange pour le moment.';

  @override
  String get dispReplySent => 'Réponse envoyée.';

  @override
  String get dispStatusResolved => 'Résolu';

  @override
  String get dispStatusEscalated => 'Escaladé à la plateforme';

  @override
  String get dispStatusUnderReview => 'En cours d’examen';

  @override
  String get dispStatusOpen => 'Ouvert';

  @override
  String dispOpenedAge(String age) {
    return 'Ouvert $age';
  }

  @override
  String get dispEscalatedWarning =>
      'La plateforme va arbitrer. Apportez vos éléments maintenant : sans réponse de votre part, la décision se prendra sur les seuls arguments de l\'acheteur.';

  @override
  String get dispClosedTitle => 'Litige clos';

  @override
  String get dispRefundedToBuyer => 'Remboursé à l\'acheteur';

  @override
  String get dispComposerHint => 'Votre réponse : faits, dates, preuves…';

  @override
  String get finTitle => 'Finances';

  @override
  String get finRange12Months => '12 mois';

  @override
  String finRangeDays(int days) {
    return '$days jours';
  }

  @override
  String get finGrossSales => 'Ventes brutes';

  @override
  String get finPlatformCommission => 'Commission plateforme';

  @override
  String get finPaymentFees => 'Frais de paiement';

  @override
  String get finRefunds => 'Remboursements';

  @override
  String get finNetForYou => 'Net pour vous';

  @override
  String get finDetail => 'Détail';

  @override
  String get finNoMovements => 'Aucun mouvement sur la période.';

  @override
  String get finPayouts => 'Versements';

  @override
  String finPayoutProviderDate(String provider, String date) {
    return '$provider · $date';
  }

  @override
  String get walTitle => 'Portefeuille';

  @override
  String get walWithdrawalHistory => 'Historique des retraits';

  @override
  String get walNoWithdrawals => 'Aucun retrait pour le moment.';

  @override
  String get walMovements => 'Mouvements';

  @override
  String get walAvailableBalance => 'Solde disponible';

  @override
  String walPendingDelivery(String amount) {
    return 'En attente de livraison · $amount';
  }

  @override
  String get walPendingWithdrawals => 'Retraits en cours';

  @override
  String get walInvalidAmount => 'Montant invalide.';

  @override
  String get walRequestSaved =>
      'Demande enregistrée. Elle sera versée après validation.';

  @override
  String get walRequestWithdrawal => 'Demander un retrait';

  @override
  String get walNoMobileMoneyAccount =>
      'Aucun compte Mobile Money enregistré. Renseignez-le dans votre boutique : sans lui, aucun versement n\'est possible.';

  @override
  String get walAmount => 'Montant';

  @override
  String walAvailableAmount(String amount) {
    return 'Disponible : $amount';
  }

  @override
  String get walRequestWithdrawalButton => 'Demander le retrait';

  @override
  String get walFundsHeldNotice =>
      'Les fonds sont retenus dès la demande, puis versés après validation par l’administrateur.';

  @override
  String get walWithdrawalProcessing =>
      'Versement transmis à votre opérateur. Il est confirmé dès que l\'argent arrive — cela peut prendre quelques minutes.';

  @override
  String get walFundsRecredited =>
      'Les fonds ont été recrédités sur votre solde.';

  @override
  String get shpTitle => 'Expéditions';

  @override
  String get shpEmptyTodo =>
      'Rien à expédier.\nVos colis en attente apparaîtront ici.';

  @override
  String get shpEmptyOther => 'Aucune expédition dans cette vue.';

  @override
  String get shpFilterTodo => 'À traiter';

  @override
  String get shpFilterShipped => 'Expédiées';

  @override
  String get shpFilterDelivered => 'Livrées';

  @override
  String get shpFilterCancelled => 'Annulées';

  @override
  String get shpFilterAll => 'Toutes';

  @override
  String shpFilterWithCount(String label, int count) {
    return '$label ($count)';
  }

  @override
  String shpCustomerItems(String customer, int count) {
    return '$customer · $count article(s)';
  }

  @override
  String get shpTracking => 'Suivi';

  @override
  String get shpMarkPrepared => 'Marquer préparée';

  @override
  String get shpMarkedPrepared => 'Colis marqué comme préparé.';

  @override
  String get shpShip => 'Expédier';

  @override
  String get shpConfirmDelivery => 'Confirmer la livraison';

  @override
  String get shpDeliveryConfirmed => 'Livraison confirmée.';

  @override
  String get shpCancel => 'Annuler';

  @override
  String get shpCancelDialogTitle => 'Annuler cette expédition ?';

  @override
  String shpCancelDialogBody(String orderReference) {
    return 'La commande $orderReference sera signalée comme annulée au client. Cette action ne peut pas être défaite.';
  }

  @override
  String get shpBack => 'Retour';

  @override
  String get shpCancelShipment => 'Annuler l’expédition';

  @override
  String get shpShipmentCancelled => 'Expédition annulée.';

  @override
  String get shpShipmentSaved => 'Expédition enregistrée.';

  @override
  String shpShipSheetTitle(String orderReference) {
    return 'Expédier $orderReference';
  }

  @override
  String get shpTrackingHint =>
      'Le numéro de suivi est communiqué au client : sans lui, la moindre contestation devient invérifiable.';

  @override
  String get shpCarrier => 'Transporteur';

  @override
  String get shpCarrierRequired => 'Transporteur obligatoire';

  @override
  String get shpTrackingNumber => 'Numéro de suivi';

  @override
  String get shpTrackingRequired => 'Numéro obligatoire';

  @override
  String get shpConfirmShipment => 'Confirmer l’expédition';

  @override
  String get retTitle => 'Retours';

  @override
  String get retEmptyTodo => 'Aucun retour à traiter.';

  @override
  String get retEmptyOther => 'Aucun retour dans cette vue.';

  @override
  String get retFilterTodo => 'À traiter';

  @override
  String get retFilterRefunded => 'Remboursés';

  @override
  String get retFilterRejected => 'Refusés';

  @override
  String get retFilterAll => 'Tous';

  @override
  String retFilterWithCount(String label, int count) {
    return '$label ($count)';
  }

  @override
  String get retStatusRefunded => 'Remboursé';

  @override
  String get retStatusRefundPending => 'Versement en cours';

  @override
  String get retStatusRejected => 'Refusé';

  @override
  String get retStatusReceived => 'Colis reçu';

  @override
  String get retStatusApproved => 'Accepté';

  @override
  String get retStatusToReview => 'À examiner';

  @override
  String get retTracking => 'Suivi';

  @override
  String get retLabelRefunded => 'Remboursé';

  @override
  String get retLabelToPay => 'À verser';

  @override
  String get retRefundValidatedNote =>
      'Remboursement validé. Le versement à l\'acheteur est effectué par HBA Express sous 24 à 72 heures. Aucune action de votre part.';

  @override
  String get retAccept => 'Accepter';

  @override
  String get retReturnAccepted => 'Retour accepté. Attendez le colis.';

  @override
  String get retReject => 'Refuser';

  @override
  String get retNoTrackingWarning =>
      'Aucun numéro de suivi : vous ne pourrez pas prouver le retour du colis.';

  @override
  String get retEditTracking => 'Modifier le suivi';

  @override
  String get retEnterTracking => 'Saisir le suivi';

  @override
  String get retParcelReceived => 'Colis reçu';

  @override
  String get retValidateRefund => 'Valider le remboursement';

  @override
  String get retConfirmReceiptTitle => 'Confirmer la réception ?';

  @override
  String get retConfirmReceiptBody =>
      'Déclarez le colis reçu seulement après avoir vérifié son contenu. Vous pourrez ensuite rembourser — en totalité ou en partie si l\'article est abîmé.';

  @override
  String get retCancel => 'Annuler';

  @override
  String get retReceiptSaved => 'Réception enregistrée.';

  @override
  String get retExplainRejection =>
      'Expliquez votre refus (10 caractères minimum).';

  @override
  String get retReturnRejected => 'Retour refusé.';

  @override
  String get retRejectReturn => 'Refuser le retour';

  @override
  String get retRejectReasonHint =>
      'Votre motif sera communiqué à l\'acheteur. Un refus clairement expliqué évite le litige.';

  @override
  String get retRejectReasonExample =>
      'Ex. : l\'article a été utilisé et ne peut pas être revendu.';

  @override
  String get retCarrierTrackingRequired =>
      'Transporteur et numéro sont requis.';

  @override
  String get retTrackingSaved => 'Suivi enregistré.';

  @override
  String get retTrackingSheetTitle => 'Suivi du colis retour';

  @override
  String get retCarrier => 'Transporteur';

  @override
  String get retTrackingNumber => 'Numéro de suivi';

  @override
  String get retSave => 'Enregistrer';

  @override
  String get retInvalidAmount => 'Montant invalide.';

  @override
  String get retValidateRefundTitle => 'Valider ce remboursement ?';

  @override
  String retValidateRefundBody(String amount) {
    return '$amount seront remboursés à l\'\'acheteur par HBA Express, et déduits de votre solde. Cette décision est définitive.';
  }

  @override
  String get retValidate => 'Valider';

  @override
  String get retRefundValidatedNotify =>
      'Remboursement validé. HBA Express verse la somme à l\'acheteur sous 24 à 72 h.';

  @override
  String get retPartialRefundHint =>
      'Vous pouvez rembourser partiellement si l\'article revient abîmé ou incomplet.';

  @override
  String get retRefundAmount => 'Montant à rembourser';

  @override
  String get profTitle => 'Profil et sécurité';

  @override
  String get profName => 'Nom';

  @override
  String get profEmail => 'E-mail';

  @override
  String get profPhone => 'Téléphone';

  @override
  String get profShop => 'Boutique';

  @override
  String get profEditInfo => 'Modifier mes informations';

  @override
  String get profChangePassword => 'Changer mon mot de passe';

  @override
  String get profMfa => 'Double authentification';

  @override
  String get profEnabled => 'Activée';

  @override
  String get profDisabled => 'Désactivée';

  @override
  String get profMyInfo => 'Mes informations';

  @override
  String get profFirstName => 'Prénom';

  @override
  String get profUpdated => 'Profil mis à jour.';

  @override
  String get profSave => 'Enregistrer';

  @override
  String get profCurrentPassword => 'Mot de passe actuel';

  @override
  String get profNewPassword => 'Nouveau mot de passe';

  @override
  String get profPasswordMinLength => '8 caractères minimum.';

  @override
  String get profPasswordChanged => 'Mot de passe modifié.';

  @override
  String get profModify => 'Modifier';

  @override
  String get profEnableMfa => 'Activer la double authentification';

  @override
  String get profMfaStep1 =>
      '1. Ouvrez une application d\'authentification (Google Authenticator, Authy…).\n2. Ajoutez un compte en saisissant cette clé :';

  @override
  String get profKeyCopied => 'Clé copiée.';

  @override
  String get profMfaStep3 =>
      '3. Entrez le code à 6 chiffres affiché par l’application :';

  @override
  String get profCodeRequired => 'Code à 6 chiffres requis.';

  @override
  String get profMfaEnabled => 'Double authentification activée.';

  @override
  String get profEnable => 'Activer';

  @override
  String get profDisableMfa => 'Désactiver la double authentification';

  @override
  String get profDisableMfaBody =>
      'Entrez un code de votre application d’authentification pour confirmer.';

  @override
  String get profMfaDisabled => 'Double authentification désactivée.';

  @override
  String get profDisable => 'Désactiver';

  @override
  String get notifTitle => 'Notifications';

  @override
  String get notifPreferences => 'Préférences';

  @override
  String get notifMarkAllRead => 'Tout marquer lu';

  @override
  String get notifEmpty => 'Aucune notification.';

  @override
  String get notifprefTitle => 'Préférences de notification';

  @override
  String get notifprefIntro =>
      'Choisissez les notifications push que vous souhaitez recevoir. Les notifications restent consultables dans votre boîte de réception, même coupées.';

  @override
  String get notifprefOrdersTitle => 'Commandes & expéditions';

  @override
  String get notifprefOrdersDesc =>
      'Nouvelles commandes, préparation, expédition';

  @override
  String get notifprefReturnsTitle => 'Retours & litiges';

  @override
  String get notifprefReturnsDesc =>
      'Demandes de retour, remboursements, litiges';

  @override
  String get notifprefReviewsTitle => 'Avis clients';

  @override
  String get notifprefReviewsDesc => 'Nouveaux avis sur vos produits';

  @override
  String get notifprefMessagesTitle => 'Messages';

  @override
  String get notifprefMessagesDesc => 'Nouveaux messages des acheteurs';

  @override
  String get notifprefAccountTitle => 'Compte & paiements';

  @override
  String get notifprefAccountDesc =>
      'Validation de boutique, retraits, versements';

  @override
  String get privTitle => 'Confidentialité';

  @override
  String get privHeaderTitle => 'Vos données';

  @override
  String get termsTitle => 'Conditions générales';

  @override
  String get shopTitle => 'Ma boutique';

  @override
  String get shopCompanyInfo => 'Informations société';

  @override
  String get shopCompanyInfoEmpty =>
      'Renseignez les informations de votre entreprise (raison sociale, RCCM, IFU…). Elles facilitent la validation de votre profil.';

  @override
  String get shopFillIn => 'Renseigner';

  @override
  String get shopLegalName => 'Raison sociale';

  @override
  String get shopActivity => 'Activité';

  @override
  String get shopAddress => 'Adresse';

  @override
  String get shopCity => 'Commune';

  @override
  String get shopManager => 'Gérant';

  @override
  String get shopPhone => 'Téléphone';

  @override
  String get shopEdit => 'Modifier';

  @override
  String get shopPayoutAccount => 'Compte de versement';

  @override
  String get shopOperator => 'Opérateur';

  @override
  String get shopNumber => 'Numéro';

  @override
  String get shopAccountHolder => 'Titulaire';

  @override
  String get shopNoPayoutAccount =>
      'Aucun compte Mobile Money enregistré. Sans lui, vos gains ne peuvent pas vous être versés.';

  @override
  String get shopSaveMyAccount => 'Enregistrer mon compte';

  @override
  String get shopDocumentsKyb => 'Documents (KYB)';

  @override
  String get shopAdd => 'AJOUTER';

  @override
  String get shopNoDocuments =>
      'Aucun document déposé. La vérification de votre entreprise est requise pour activer votre boutique et recevoir vos versements.';

  @override
  String get shopUploadDocument => 'Déposer un document';

  @override
  String get shopDocumentsSecure =>
      'Vos pièces sont stockées de façon sécurisée et ne sont jamais visibles par les acheteurs.';

  @override
  String get shopLogoUpdated => 'Logo mis à jour.';

  @override
  String get shopDocumentRejected => 'Refusée — déposez une nouvelle pièce';

  @override
  String get shopDelete => 'Supprimer';

  @override
  String get shopDeleteDocumentTitle => 'Supprimer ce document ?';

  @override
  String shopDeleteDocumentBody(String label) {
    return '« $label » sera retiré de votre dossier. Vous devrez le redéposer pour que votre boutique soit vérifiée.';
  }

  @override
  String get shopCancel => 'Annuler';

  @override
  String get shopDocumentDeleted => 'Document supprimé.';

  @override
  String get shopFileTooLarge => 'Fichier trop volumineux (max 10 Mo).';

  @override
  String get shopChooseFileError => 'Choisissez un fichier.';

  @override
  String get shopDocumentSent => 'Document envoyé. Il sera examiné sous peu.';

  @override
  String get shopKybTitle => 'Vérification de compte (KYB)';

  @override
  String get shopKybSubtitle =>
      'Ces pièces prouvent l\'existence de votre entreprise. Elles sont examinées manuellement et restent confidentielles.';

  @override
  String get shopDocumentType => 'Type de document';

  @override
  String get shopChooseFile => 'Choisir un fichier';

  @override
  String get shopFileFormats => 'PDF, JPG, PNG ou WebP — 10 Mo maximum';

  @override
  String get shopSubmitForVerification => 'Soumettre pour vérification';

  @override
  String get shopNameRequired => 'Le nom est obligatoire.';

  @override
  String get shopShopUpdated => 'Boutique mise à jour.';

  @override
  String get shopProfileTitle => 'Profil de la boutique';

  @override
  String get shopShopName => 'Nom de la boutique';

  @override
  String get shopDescription => 'Description';

  @override
  String get shopSave => 'Enregistrer';

  @override
  String get shopCompanyInfoUpdated => 'Informations société mises à jour.';

  @override
  String get shopAllFieldsOptional =>
      'Tous les champs sont facultatifs. Ces informations aident à valider votre profil.';

  @override
  String get shopPayoutSaved => 'Compte de versement enregistré.';

  @override
  String get shopPayoutHint =>
      'C\'est sur ce numéro que vos retraits seront versés. Vérifiez-le : un numéro erroné envoie l\'argent à quelqu\'un d\'autre.';

  @override
  String get shopMobileMoneyNumber => 'Numéro Mobile Money';

  @override
  String get shopInvalidNumber => 'Numéro invalide';

  @override
  String get shopAccountHolderName => 'Nom du titulaire';

  @override
  String get shopHolderNameRequired => 'Nom obligatoire';

  @override
  String get helpTitle => 'Aide & support';

  @override
  String get helpNeedHelp => 'Besoin d\'aide ?';

  @override
  String get helpBrowseFaq =>
      'Parcourez les questions fréquentes, ou écrivez-nous directement.';

  @override
  String get helpContactSupport => 'Contacter le support';

  @override
  String get helpSearchHint => 'Rechercher une question…';

  @override
  String helpNoResults(String query) {
    return 'Aucune réponse pour « $query ».';
  }

  @override
  String get helpAskSupport => 'Poser la question au support';

  @override
  String helpEmailCopied(String email) {
    return 'Adresse copiée : $email';
  }

  @override
  String get helpEmailSubject => 'Support vendeur — HbaExpress PRO';

  @override
  String helpEmailBody(String version) {
    return '\n\n———\nMerci de décrire votre problème ci-dessus.\n(App HbaExpress PRO — réf. CGU $version)';
  }

  @override
  String get consentTitle => 'Avant de continuer';

  @override
  String get consentIntro =>
      'Nous avons besoin de votre accord sur nos conditions générales et sur notre politique de confidentialité. Prenez le temps de les lire : elles disent ce à quoi vous vous engagez, et ce à quoi nous nous engageons.';

  @override
  String get consentTermsTab => 'Conditions';

  @override
  String get consentPrivacyTab => 'Confidentialité';

  @override
  String get consentScrollHint =>
      'Faites défiler les deux documents jusqu’au bout pour pouvoir les accepter.';

  @override
  String get consentCheckbox =>
      'J’ai lu et j’accepte les conditions générales et la politique de confidentialité.';

  @override
  String get consentAcceptContinue => 'Accepter et continuer';

  @override
  String get consentDecline => 'Refuser';

  @override
  String get consentDeclineTitle => 'Refuser les conditions ?';

  @override
  String get consentDeclineBody =>
      'Sans votre accord, l\'application ne peut pas être utilisée : vous serez déconnecté. Votre boutique et vos données ne sont pas supprimées.';

  @override
  String get consentGoBack => 'Revenir';

  @override
  String get consentDeclineAndLeave => 'Refuser et quitter';

  @override
  String consentSaveError(String error) {
    return 'Votre accord n\'\'a pas pu être enregistré : $error';
  }

  @override
  String legalVersion(String date, String company) {
    return 'Version du $date · $company';
  }

  @override
  String get statTitle => 'Statistiques';

  @override
  String get stat30Days => '30 jours';

  @override
  String get stat90Days => '90 jours';

  @override
  String get stat12Months => '12 mois';

  @override
  String get statRevenue => 'Chiffre d\'affaires';

  @override
  String get statNoSales =>
      'Aucune vente sur cette période.\nVos ventes apparaîtront ici.';

  @override
  String get statOrders => 'Commandes';

  @override
  String get statItemsSold => 'Articles vendus';

  @override
  String get statOrdersByStatus => 'Commandes par statut';

  @override
  String get statOrdersByStatusSub =>
      'Toutes vos commandes, réparties par état.';

  @override
  String get statGrossToNet => 'Brut → net';

  @override
  String get statGrossToNetSub =>
      '30 derniers jours, après commission de la marketplace.';

  @override
  String get statBucketToProcess => 'À traiter';

  @override
  String get statBucketShipped => 'Expédiées';

  @override
  String get statBucketDelivered => 'Livrées';

  @override
  String get statBucketCancelled => 'Annulées';

  @override
  String get statBucketOther => 'Autres';

  @override
  String get statOrdersLabel => 'commandes';

  @override
  String get statAxisGross => 'Brut';

  @override
  String get statAxisNet => 'Net';

  @override
  String get statCommissionTaken => 'Commission prélevée';

  @override
  String get condNew => 'Neuf';

  @override
  String get condUsed => 'Occasion';

  @override
  String get condRefurbished => 'Reconditionné';

  @override
  String get payoutMtn => 'MTN Mobile Money (Bénin)';

  @override
  String get payoutMoov => 'Moov Money (Bénin)';

  @override
  String get payoutCeltis => 'Celtis Cash (Bénin)';

  @override
  String get kybBusinessRegistry => 'Registre du commerce';

  @override
  String get kybIdCard => 'Pièce d\'identité';

  @override
  String get kybTaxId => 'Identifiant fiscal';

  @override
  String get kybProofOfAddress => 'Justificatif de domicile';

  @override
  String get kybFallbackDocument => 'Document';

  @override
  String get ageJustNow => 'À l\'instant';

  @override
  String ageMinutes(int n) {
    return 'il y a $n min';
  }

  @override
  String ageHours(int n) {
    return 'il y a $n h';
  }

  @override
  String ageDays(int n) {
    return 'il y a $n j';
  }

  @override
  String get carrierLoading => 'Chargement des transporteurs…';

  @override
  String get carrierSelectHint => 'Choisir un transporteur';

  @override
  String get carrierOther => 'Autre (préciser)';

  @override
  String get carrierOtherLabel => 'Nom du transporteur';

  @override
  String get shpTrackingOpenError => 'Impossible d\'ouvrir le lien de suivi.';

  @override
  String get commonFeatureUnavailable =>
      'Cette fonctionnalité n\'est pas encore disponible sur le serveur.';

  @override
  String commonNoResultsFor(String query) {
    return 'Aucun résultat pour « $query ».';
  }

  @override
  String get pillPaid => 'Payé';

  @override
  String get pillPayoutProcessing => 'Versement en cours';

  @override
  String get pillPending => 'En attente';

  @override
  String get pillRejectedM => 'Refusé';

  @override
  String get pillFailed => 'Échec';

  @override
  String get pillProcessing => 'En traitement';

  @override
  String get pillDelivered => 'Livrée';

  @override
  String get pillShipped => 'Expédiée';

  @override
  String get pillToProcess => 'À traiter';

  @override
  String get pillCancelled => 'Annulée';

  @override
  String get pillRefunded => 'Remboursée';

  @override
  String get pillPrepared => 'Préparée';

  @override
  String get pillToPrepare => 'À préparer';

  @override
  String get pillOnline => 'En ligne';

  @override
  String get pillPaused => 'En pause';

  @override
  String get pillDraft => 'Brouillon';

  @override
  String get pillArchived => 'Archivé';

  @override
  String get pillVerified => 'Vérifiée';

  @override
  String get pillRejectedF => 'Refusée';

  @override
  String get pillInReview => 'En vérification';

  @override
  String get pillToComplete => 'À compléter';

  @override
  String get commonSeeAll => 'VOIR TOUT';

  @override
  String get legalFrenchNote =>
      'Ce document est publié en français, sa langue qui fait foi.';

  @override
  String get ordShipmentSection => 'Expédition';

  @override
  String get ordNoShipmentYet =>
      'L\'expédition est créée automatiquement une fois le paiement encaissé. Si elle n\'apparaît pas encore, patientez un instant puis rafraîchissez.';

  @override
  String get commonDescription => 'Description';

  @override
  String get commonSku => 'SKU';

  @override
  String get commonStock => 'Stock';

  @override
  String get routeNotFoundTitle => 'Page introuvable';

  @override
  String get routeBackHome => 'Retour à l\'accueil';

  @override
  String get updTitle => 'Mise à jour requise';

  @override
  String get updBody =>
      'Cette version de l\'application n\'est plus prise en charge. Mettez-la à jour pour continuer à gérer votre boutique.';

  @override
  String get updButton => 'Mettre à jour';

  @override
  String get updAlreadyUpdated => 'J\'ai déjà mis à jour';

  @override
  String get updNoStoreLink =>
      'Lien de mise à jour indisponible. Recherchez « HbaExpress PRO » dans votre store.';

  @override
  String get notifprefDeviceTitle => 'Cet appareil';

  @override
  String get notifprefDeviceHint =>
      'Si vous ne recevez pas les notifications alors qu\'elles sont autorisées, réactivez-les ici pour réenregistrer cet appareil.';

  @override
  String get notifprefDeviceButton =>
      'Activer les notifications sur cet appareil';

  @override
  String get notifprefDeviceEnabling => 'Activation…';

  @override
  String get notifprefDeviceRegistered =>
      'Cet appareil est enregistré pour les notifications.';

  @override
  String get notifprefDevicePermissionDenied =>
      'Notifications refusées. Activez-les dans les Réglages du téléphone, puis réessayez.';

  @override
  String get notifprefDeviceApnsUnavailable =>
      'Impossible d\'obtenir le jeton de notification pour l\'instant. Réessayez dans un instant, avec une bonne connexion, sur un appareil réel.';

  @override
  String get notifprefDeviceError =>
      'Échec de l\'activation. Vérifiez votre connexion et réessayez.';

  @override
  String get authLoginBrandKicker => 'HBA PARTNER';

  @override
  String get authLoginTitle => 'Gérez votre activité avec HBA';

  @override
  String get navActivities => 'Activités';

  @override
  String get navFinance => 'Finances';

  @override
  String get navAccount => 'Compte';
}
