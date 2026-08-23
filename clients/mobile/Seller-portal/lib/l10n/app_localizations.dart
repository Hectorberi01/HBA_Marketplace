import 'dart:async';

import 'package:flutter/foundation.dart';
import 'package:flutter/widgets.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:intl/intl.dart' as intl;

import 'app_localizations_en.dart';
import 'app_localizations_fr.dart';

// ignore_for_file: type=lint

/// Callers can lookup localized strings with an instance of AppLocalizations
/// returned by `AppLocalizations.of(context)`.
///
/// Applications need to include `AppLocalizations.delegate()` in their app's
/// `localizationDelegates` list, and the locales they support in the app's
/// `supportedLocales` list. For example:
///
/// ```dart
/// import 'l10n/app_localizations.dart';
///
/// return MaterialApp(
///   localizationsDelegates: AppLocalizations.localizationsDelegates,
///   supportedLocales: AppLocalizations.supportedLocales,
///   home: MyApplicationHome(),
/// );
/// ```
///
/// ## Update pubspec.yaml
///
/// Please make sure to update your pubspec.yaml to include the following
/// packages:
///
/// ```yaml
/// dependencies:
///   # Internationalization support.
///   flutter_localizations:
///     sdk: flutter
///   intl: any # Use the pinned version from flutter_localizations
///
///   # Rest of dependencies
/// ```
///
/// ## iOS Applications
///
/// iOS applications define key application metadata, including supported
/// locales, in an Info.plist file that is built into the application bundle.
/// To configure the locales supported by your app, you’ll need to edit this
/// file.
///
/// First, open your project’s ios/Runner.xcworkspace Xcode workspace file.
/// Then, in the Project Navigator, open the Info.plist file under the Runner
/// project’s Runner folder.
///
/// Next, select the Information Property List item, select Add Item from the
/// Editor menu, then select Localizations from the pop-up menu.
///
/// Select and expand the newly-created Localizations item then, for each
/// locale your application supports, add a new item and select the locale
/// you wish to add from the pop-up menu in the Value field. This list should
/// be consistent with the languages listed in the AppLocalizations.supportedLocales
/// property.
abstract class AppLocalizations {
  AppLocalizations(String locale)
      : localeName = intl.Intl.canonicalizedLocale(locale.toString());

  final String localeName;

  static AppLocalizations of(BuildContext context) {
    return Localizations.of<AppLocalizations>(context, AppLocalizations)!;
  }

  static const LocalizationsDelegate<AppLocalizations> delegate =
      _AppLocalizationsDelegate();

  /// A list of this localizations delegate along with the default localizations
  /// delegates.
  ///
  /// Returns a list of localizations delegates containing this delegate along with
  /// GlobalMaterialLocalizations.delegate, GlobalCupertinoLocalizations.delegate,
  /// and GlobalWidgetsLocalizations.delegate.
  ///
  /// Additional delegates can be added by appending to this list in
  /// MaterialApp. This list does not have to be used at all if a custom list
  /// of delegates is preferred or required.
  static const List<LocalizationsDelegate<dynamic>> localizationsDelegates =
      <LocalizationsDelegate<dynamic>>[
    delegate,
    GlobalMaterialLocalizations.delegate,
    GlobalCupertinoLocalizations.delegate,
    GlobalWidgetsLocalizations.delegate,
  ];

  /// A list of this localizations delegate's supported locales.
  static const List<Locale> supportedLocales = <Locale>[
    Locale('en'),
    Locale('fr')
  ];

  /// Nom de l'application
  ///
  /// In fr, this message translates to:
  /// **'HbaExpress PRO'**
  String get appTitle;

  /// No description provided for @commonCancel.
  ///
  /// In fr, this message translates to:
  /// **'Annuler'**
  String get commonCancel;

  /// No description provided for @commonSave.
  ///
  /// In fr, this message translates to:
  /// **'Enregistrer'**
  String get commonSave;

  /// No description provided for @commonDelete.
  ///
  /// In fr, this message translates to:
  /// **'Supprimer'**
  String get commonDelete;

  /// No description provided for @commonClose.
  ///
  /// In fr, this message translates to:
  /// **'Fermer'**
  String get commonClose;

  /// No description provided for @commonRetry.
  ///
  /// In fr, this message translates to:
  /// **'Réessayer'**
  String get commonRetry;

  /// No description provided for @commonConfirm.
  ///
  /// In fr, this message translates to:
  /// **'Confirmer'**
  String get commonConfirm;

  /// No description provided for @commonContinue.
  ///
  /// In fr, this message translates to:
  /// **'Continuer'**
  String get commonContinue;

  /// No description provided for @commonBack.
  ///
  /// In fr, this message translates to:
  /// **'Précédent'**
  String get commonBack;

  /// No description provided for @commonNext.
  ///
  /// In fr, this message translates to:
  /// **'Suivant'**
  String get commonNext;

  /// No description provided for @commonAdd.
  ///
  /// In fr, this message translates to:
  /// **'Ajouter'**
  String get commonAdd;

  /// No description provided for @commonEdit.
  ///
  /// In fr, this message translates to:
  /// **'Modifier'**
  String get commonEdit;

  /// No description provided for @commonSearch.
  ///
  /// In fr, this message translates to:
  /// **'Rechercher'**
  String get commonSearch;

  /// No description provided for @commonLoading.
  ///
  /// In fr, this message translates to:
  /// **'Chargement…'**
  String get commonLoading;

  /// No description provided for @commonYes.
  ///
  /// In fr, this message translates to:
  /// **'Oui'**
  String get commonYes;

  /// No description provided for @commonNo.
  ///
  /// In fr, this message translates to:
  /// **'Non'**
  String get commonNo;

  /// No description provided for @commonError.
  ///
  /// In fr, this message translates to:
  /// **'Une erreur est survenue.'**
  String get commonError;

  /// No description provided for @settingsAppearanceTitle.
  ///
  /// In fr, this message translates to:
  /// **'Apparence'**
  String get settingsAppearanceTitle;

  /// No description provided for @settingsTheme.
  ///
  /// In fr, this message translates to:
  /// **'Thème'**
  String get settingsTheme;

  /// No description provided for @settingsThemeAuto.
  ///
  /// In fr, this message translates to:
  /// **'Automatique'**
  String get settingsThemeAuto;

  /// No description provided for @settingsThemeAutoDesc.
  ///
  /// In fr, this message translates to:
  /// **'Suit le réglage de votre téléphone'**
  String get settingsThemeAutoDesc;

  /// No description provided for @settingsThemeLight.
  ///
  /// In fr, this message translates to:
  /// **'Clair'**
  String get settingsThemeLight;

  /// No description provided for @settingsThemeLightDesc.
  ///
  /// In fr, this message translates to:
  /// **'Fond clair en permanence'**
  String get settingsThemeLightDesc;

  /// No description provided for @settingsThemeDark.
  ///
  /// In fr, this message translates to:
  /// **'Sombre'**
  String get settingsThemeDark;

  /// No description provided for @settingsThemeDarkDesc.
  ///
  /// In fr, this message translates to:
  /// **'Fond sombre en permanence'**
  String get settingsThemeDarkDesc;

  /// No description provided for @settingsLanguage.
  ///
  /// In fr, this message translates to:
  /// **'Langue'**
  String get settingsLanguage;

  /// No description provided for @settingsLanguageSystem.
  ///
  /// In fr, this message translates to:
  /// **'Automatique'**
  String get settingsLanguageSystem;

  /// No description provided for @settingsLanguageSystemDesc.
  ///
  /// In fr, this message translates to:
  /// **'Suit la langue de votre téléphone'**
  String get settingsLanguageSystemDesc;

  /// No description provided for @settingsLanguageFrench.
  ///
  /// In fr, this message translates to:
  /// **'Français'**
  String get settingsLanguageFrench;

  /// No description provided for @settingsLanguageEnglish.
  ///
  /// In fr, this message translates to:
  /// **'Anglais'**
  String get settingsLanguageEnglish;

  /// No description provided for @navHome.
  ///
  /// In fr, this message translates to:
  /// **'Accueil'**
  String get navHome;

  /// No description provided for @navOrders.
  ///
  /// In fr, this message translates to:
  /// **'Commandes'**
  String get navOrders;

  /// No description provided for @navProducts.
  ///
  /// In fr, this message translates to:
  /// **'Produits'**
  String get navProducts;

  /// No description provided for @navMessages.
  ///
  /// In fr, this message translates to:
  /// **'Messages'**
  String get navMessages;

  /// No description provided for @navMore.
  ///
  /// In fr, this message translates to:
  /// **'Plus'**
  String get navMore;

  /// No description provided for @homeTitle.
  ///
  /// In fr, this message translates to:
  /// **'Tableau de bord'**
  String get homeTitle;

  /// No description provided for @homeStatsTooltip.
  ///
  /// In fr, this message translates to:
  /// **'Statistiques'**
  String get homeStatsTooltip;

  /// No description provided for @homeNotificationsTooltip.
  ///
  /// In fr, this message translates to:
  /// **'Notifications'**
  String get homeNotificationsTooltip;

  /// No description provided for @homeDefaultShopName.
  ///
  /// In fr, this message translates to:
  /// **'Ma boutique'**
  String get homeDefaultShopName;

  /// No description provided for @homeGreeting.
  ///
  /// In fr, this message translates to:
  /// **'Bonjour'**
  String get homeGreeting;

  /// No description provided for @homeSubtitle.
  ///
  /// In fr, this message translates to:
  /// **'Voici l’activité de votre boutique.'**
  String get homeSubtitle;

  /// No description provided for @homeKybInReviewTitle.
  ///
  /// In fr, this message translates to:
  /// **'Vérification en cours'**
  String get homeKybInReviewTitle;

  /// No description provided for @homeKybInReviewSubtitle.
  ///
  /// In fr, this message translates to:
  /// **'Vous pourrez publier vos produits une fois votre profil validé.'**
  String get homeKybInReviewSubtitle;

  /// No description provided for @homeKybRejectedTitle.
  ///
  /// In fr, this message translates to:
  /// **'Vérification refusée'**
  String get homeKybRejectedTitle;

  /// No description provided for @homeKybRejectedSubtitle.
  ///
  /// In fr, this message translates to:
  /// **'Corrigez et redéposez vos documents pour publier vos produits.'**
  String get homeKybRejectedSubtitle;

  /// No description provided for @homeKybPendingTitle.
  ///
  /// In fr, this message translates to:
  /// **'Vérifiez votre boutique'**
  String get homeKybPendingTitle;

  /// No description provided for @homeKybPendingSubtitle.
  ///
  /// In fr, this message translates to:
  /// **'Déposez vos documents (KYB) pour publier vos produits et être payé.'**
  String get homeKybPendingSubtitle;

  /// No description provided for @homeKpiToProcess.
  ///
  /// In fr, this message translates to:
  /// **'À traiter'**
  String get homeKpiToProcess;

  /// No description provided for @homeKpiOrders.
  ///
  /// In fr, this message translates to:
  /// **'Commandes'**
  String get homeKpiOrders;

  /// No description provided for @homeKpiSales30d.
  ///
  /// In fr, this message translates to:
  /// **'Ventes (30 j)'**
  String get homeKpiSales30d;

  /// No description provided for @homeKpiNetPayout30d.
  ///
  /// In fr, this message translates to:
  /// **'Net à percevoir (30 j)'**
  String get homeKpiNetPayout30d;

  /// No description provided for @homeKpiAverageRating.
  ///
  /// In fr, this message translates to:
  /// **'Note moyenne'**
  String get homeKpiAverageRating;

  /// No description provided for @homeKpiReviewsReceived.
  ///
  /// In fr, this message translates to:
  /// **'Avis reçus'**
  String get homeKpiReviewsReceived;

  /// No description provided for @homeReturnsToProcess.
  ///
  /// In fr, this message translates to:
  /// **'{count, plural, =1{1 retour à traiter} other{{count} retours à traiter}}'**
  String homeReturnsToProcess(int count);

  /// No description provided for @homeReturnsDisputeWarning.
  ///
  /// In fr, this message translates to:
  /// **'Sans réponse, l’acheteur ouvrira un litige.'**
  String get homeReturnsDisputeWarning;

  /// No description provided for @homeWalletTitle.
  ///
  /// In fr, this message translates to:
  /// **'Portefeuille'**
  String get homeWalletTitle;

  /// No description provided for @homeWalletManage.
  ///
  /// In fr, this message translates to:
  /// **'Gérer'**
  String get homeWalletManage;

  /// No description provided for @homeWalletAvailable.
  ///
  /// In fr, this message translates to:
  /// **'Solde disponible'**
  String get homeWalletAvailable;

  /// No description provided for @homeWalletPendingDelivery.
  ///
  /// In fr, this message translates to:
  /// **'En attente de livraison'**
  String get homeWalletPendingDelivery;

  /// No description provided for @homeWalletPendingWithdrawal.
  ///
  /// In fr, this message translates to:
  /// **'Retraits en cours'**
  String get homeWalletPendingWithdrawal;

  /// No description provided for @homeRecentOrders.
  ///
  /// In fr, this message translates to:
  /// **'Dernières commandes'**
  String get homeRecentOrders;

  /// No description provided for @homeStepCompleteShopTitle.
  ///
  /// In fr, this message translates to:
  /// **'Compléter votre boutique'**
  String get homeStepCompleteShopTitle;

  /// No description provided for @homeStepCompleteShopSubtitle.
  ///
  /// In fr, this message translates to:
  /// **'Documents validés + compte de reversement'**
  String get homeStepCompleteShopSubtitle;

  /// No description provided for @homeStepAddLocationTitle.
  ///
  /// In fr, this message translates to:
  /// **'Ajouter un lieu d\'expédition'**
  String get homeStepAddLocationTitle;

  /// No description provided for @homeStepAddLocationSubtitle.
  ///
  /// In fr, this message translates to:
  /// **'L\'adresse d\'où partent vos colis'**
  String get homeStepAddLocationSubtitle;

  /// No description provided for @homeStepCreateProductTitle.
  ///
  /// In fr, this message translates to:
  /// **'Créer un produit'**
  String get homeStepCreateProductTitle;

  /// No description provided for @homeStepCreateProductSubtitle.
  ///
  /// In fr, this message translates to:
  /// **'Photo, catégorie, description'**
  String get homeStepCreateProductSubtitle;

  /// No description provided for @homeStepPublishOfferTitle.
  ///
  /// In fr, this message translates to:
  /// **'Mettre un produit en vente'**
  String get homeStepPublishOfferTitle;

  /// No description provided for @homeStepPublishOfferSubtitle.
  ///
  /// In fr, this message translates to:
  /// **'Fixez votre prix pour être achetable'**
  String get homeStepPublishOfferSubtitle;

  /// No description provided for @homeChecklistTitle.
  ///
  /// In fr, this message translates to:
  /// **'Bien démarrer'**
  String get homeChecklistTitle;

  /// No description provided for @homeChecklistProgress.
  ///
  /// In fr, this message translates to:
  /// **'{done}/{total} étape(s) — encore quelques pas pour vendre'**
  String homeChecklistProgress(int done, int total);

  /// No description provided for @homeChecklistDismiss.
  ///
  /// In fr, this message translates to:
  /// **'Masquer'**
  String get homeChecklistDismiss;

  /// No description provided for @ordTitle.
  ///
  /// In fr, this message translates to:
  /// **'Commandes'**
  String get ordTitle;

  /// No description provided for @ordSearchHint.
  ///
  /// In fr, this message translates to:
  /// **'Rechercher une commande (référence, SKU)'**
  String get ordSearchHint;

  /// No description provided for @ordEmptyMessage.
  ///
  /// In fr, this message translates to:
  /// **'Aucune commande dans cette vue.'**
  String get ordEmptyMessage;

  /// No description provided for @ordFilterAll.
  ///
  /// In fr, this message translates to:
  /// **'Toutes'**
  String get ordFilterAll;

  /// No description provided for @ordFilterToProcess.
  ///
  /// In fr, this message translates to:
  /// **'À traiter'**
  String get ordFilterToProcess;

  /// No description provided for @ordFilterShipped.
  ///
  /// In fr, this message translates to:
  /// **'Expédiées'**
  String get ordFilterShipped;

  /// No description provided for @ordFilterDelivered.
  ///
  /// In fr, this message translates to:
  /// **'Livrées'**
  String get ordFilterDelivered;

  /// No description provided for @ordPaymentPending.
  ///
  /// In fr, this message translates to:
  /// **'Paiement en attente'**
  String get ordPaymentPending;

  /// No description provided for @ordCustomerItems.
  ///
  /// In fr, this message translates to:
  /// **'{customer} · {count} article(s)'**
  String ordCustomerItems(String customer, int count);

  /// No description provided for @ordDetailTitle.
  ///
  /// In fr, this message translates to:
  /// **'Détail de la commande'**
  String get ordDetailTitle;

  /// No description provided for @ordClient.
  ///
  /// In fr, this message translates to:
  /// **'Client'**
  String get ordClient;

  /// No description provided for @ordDate.
  ///
  /// In fr, this message translates to:
  /// **'Date'**
  String get ordDate;

  /// No description provided for @ordPayment.
  ///
  /// In fr, this message translates to:
  /// **'Paiement'**
  String get ordPayment;

  /// No description provided for @ordPaid.
  ///
  /// In fr, this message translates to:
  /// **'Encaissé'**
  String get ordPaid;

  /// No description provided for @ordPending.
  ///
  /// In fr, this message translates to:
  /// **'En attente'**
  String get ordPending;

  /// No description provided for @ordTotal.
  ///
  /// In fr, this message translates to:
  /// **'Total'**
  String get ordTotal;

  /// No description provided for @ordPaymentNotCollectedWarning.
  ///
  /// In fr, this message translates to:
  /// **'Le paiement de cette commande n\'est pas encore encaissé. Attendez la confirmation avant de préparer l\'envoi.'**
  String get ordPaymentNotCollectedWarning;

  /// No description provided for @ordArticles.
  ///
  /// In fr, this message translates to:
  /// **'Articles'**
  String get ordArticles;

  /// No description provided for @ordShipping.
  ///
  /// In fr, this message translates to:
  /// **'Livraison'**
  String get ordShipping;

  /// No description provided for @ordRecipient.
  ///
  /// In fr, this message translates to:
  /// **'Destinataire'**
  String get ordRecipient;

  /// No description provided for @ordAddress.
  ///
  /// In fr, this message translates to:
  /// **'Adresse'**
  String get ordAddress;

  /// No description provided for @ordLandmark.
  ///
  /// In fr, this message translates to:
  /// **'Point de repère'**
  String get ordLandmark;

  /// No description provided for @ordQuartier.
  ///
  /// In fr, this message translates to:
  /// **'Quartier'**
  String get ordQuartier;

  /// No description provided for @ordCommune.
  ///
  /// In fr, this message translates to:
  /// **'Commune'**
  String get ordCommune;

  /// No description provided for @ordCity.
  ///
  /// In fr, this message translates to:
  /// **'Commune'**
  String get ordCity;

  /// No description provided for @ordPhone.
  ///
  /// In fr, this message translates to:
  /// **'Téléphone'**
  String get ordPhone;

  /// No description provided for @ordDisputes.
  ///
  /// In fr, this message translates to:
  /// **'Litiges ({count})'**
  String ordDisputes(int count);

  /// No description provided for @ordDisputeClosed.
  ///
  /// In fr, this message translates to:
  /// **'Clos · {date}'**
  String ordDisputeClosed(String date);

  /// No description provided for @ordDisputeOpen.
  ///
  /// In fr, this message translates to:
  /// **'Ouvert {age} — répondez pour vous défendre'**
  String ordDisputeOpen(String age);

  /// No description provided for @ordLineQtyPrice.
  ///
  /// In fr, this message translates to:
  /// **'{qty} × {price}'**
  String ordLineQtyPrice(int qty, String price);

  /// No description provided for @accTitle.
  ///
  /// In fr, this message translates to:
  /// **'Mon compte'**
  String get accTitle;

  /// No description provided for @accLoading.
  ///
  /// In fr, this message translates to:
  /// **'Chargement…'**
  String get accLoading;

  /// No description provided for @accSectionShop.
  ///
  /// In fr, this message translates to:
  /// **'Ma boutique'**
  String get accSectionShop;

  /// No description provided for @accShopLabel.
  ///
  /// In fr, this message translates to:
  /// **'Boutique'**
  String get accShopLabel;

  /// No description provided for @accShopSubtitle.
  ///
  /// In fr, this message translates to:
  /// **'Profil, compte de versement, documents'**
  String get accShopSubtitle;

  /// No description provided for @accOffersLabel.
  ///
  /// In fr, this message translates to:
  /// **'Mises en vente'**
  String get accOffersLabel;

  /// No description provided for @accOffersSubtitle.
  ///
  /// In fr, this message translates to:
  /// **'Prix et disponibilité de vos produits'**
  String get accOffersSubtitle;

  /// No description provided for @accShipmentsLabel.
  ///
  /// In fr, this message translates to:
  /// **'Expéditions'**
  String get accShipmentsLabel;

  /// No description provided for @accLocationsLabel.
  ///
  /// In fr, this message translates to:
  /// **'Lieux d\'expédition'**
  String get accLocationsLabel;

  /// No description provided for @accLocationsSubtitle.
  ///
  /// In fr, this message translates to:
  /// **'Adresses d\'où partent vos colis'**
  String get accLocationsSubtitle;

  /// No description provided for @accReturnsLabel.
  ///
  /// In fr, this message translates to:
  /// **'Retours'**
  String get accReturnsLabel;

  /// No description provided for @accReturnsSubtitle.
  ///
  /// In fr, this message translates to:
  /// **'Demandes de retour et remboursements'**
  String get accReturnsSubtitle;

  /// No description provided for @accReviewsLabel.
  ///
  /// In fr, this message translates to:
  /// **'Avis clients'**
  String get accReviewsLabel;

  /// No description provided for @accSectionMoney.
  ///
  /// In fr, this message translates to:
  /// **'Argent'**
  String get accSectionMoney;

  /// No description provided for @accWalletLabel.
  ///
  /// In fr, this message translates to:
  /// **'Portefeuille'**
  String get accWalletLabel;

  /// No description provided for @accWalletSubtitle.
  ///
  /// In fr, this message translates to:
  /// **'Soldes et retraits'**
  String get accWalletSubtitle;

  /// No description provided for @accFinanceLabel.
  ///
  /// In fr, this message translates to:
  /// **'Finances'**
  String get accFinanceLabel;

  /// No description provided for @accFinanceSubtitle.
  ///
  /// In fr, this message translates to:
  /// **'Relevé et versements'**
  String get accFinanceSubtitle;

  /// No description provided for @accSectionAccount.
  ///
  /// In fr, this message translates to:
  /// **'Compte'**
  String get accSectionAccount;

  /// No description provided for @accNotificationsLabel.
  ///
  /// In fr, this message translates to:
  /// **'Notifications'**
  String get accNotificationsLabel;

  /// No description provided for @accProfileLabel.
  ///
  /// In fr, this message translates to:
  /// **'Profil et sécurité'**
  String get accProfileLabel;

  /// No description provided for @accAppearanceLabel.
  ///
  /// In fr, this message translates to:
  /// **'Apparence'**
  String get accAppearanceLabel;

  /// No description provided for @accAppearanceSubtitle.
  ///
  /// In fr, this message translates to:
  /// **'Thème clair ou sombre'**
  String get accAppearanceSubtitle;

  /// No description provided for @accPrivacyLabel.
  ///
  /// In fr, this message translates to:
  /// **'Confidentialité'**
  String get accPrivacyLabel;

  /// No description provided for @accPrivacySubtitle.
  ///
  /// In fr, this message translates to:
  /// **'Vos données et vos droits'**
  String get accPrivacySubtitle;

  /// No description provided for @accHelpLabel.
  ///
  /// In fr, this message translates to:
  /// **'Aide & support'**
  String get accHelpLabel;

  /// No description provided for @accHelpSubtitle.
  ///
  /// In fr, this message translates to:
  /// **'FAQ et contact'**
  String get accHelpSubtitle;

  /// No description provided for @accTermsLabel.
  ///
  /// In fr, this message translates to:
  /// **'Conditions d\'utilisation'**
  String get accTermsLabel;

  /// No description provided for @accTermsSubtitle.
  ///
  /// In fr, this message translates to:
  /// **'Commission, versements, litiges'**
  String get accTermsSubtitle;

  /// No description provided for @accCloseLabel.
  ///
  /// In fr, this message translates to:
  /// **'Fermer mon compte'**
  String get accCloseLabel;

  /// No description provided for @accCloseSubtitle.
  ///
  /// In fr, this message translates to:
  /// **'Suspend la vente — réversible'**
  String get accCloseSubtitle;

  /// No description provided for @accDeleteLabel.
  ///
  /// In fr, this message translates to:
  /// **'Supprimer définitivement mon compte'**
  String get accDeleteLabel;

  /// No description provided for @accDeleteSubtitle.
  ///
  /// In fr, this message translates to:
  /// **'Efface votre compte et vos données · irréversible'**
  String get accDeleteSubtitle;

  /// No description provided for @accLogout.
  ///
  /// In fr, this message translates to:
  /// **'Se déconnecter'**
  String get accLogout;

  /// No description provided for @accLogoutConfirmTitle.
  ///
  /// In fr, this message translates to:
  /// **'Se déconnecter ?'**
  String get accLogoutConfirmTitle;

  /// No description provided for @accLogoutConfirmBody.
  ///
  /// In fr, this message translates to:
  /// **'Vous devrez saisir à nouveau vos identifiants.'**
  String get accLogoutConfirmBody;

  /// No description provided for @accCancel.
  ///
  /// In fr, this message translates to:
  /// **'Annuler'**
  String get accCancel;

  /// No description provided for @accCloseConfirmTitle.
  ///
  /// In fr, this message translates to:
  /// **'Fermer votre compte ?'**
  String get accCloseConfirmTitle;

  /// No description provided for @accCloseConfirmBody.
  ///
  /// In fr, this message translates to:
  /// **'Vos produits seront immédiatement retirés de la vente. Votre compte et son historique sont conservés — la suppression définitive est décidée par l\'équipe. Vous pourrez demander la réactivation à tout moment.'**
  String get accCloseConfirmBody;

  /// No description provided for @accCloseConfirmAction.
  ///
  /// In fr, this message translates to:
  /// **'Fermer le compte'**
  String get accCloseConfirmAction;

  /// No description provided for @accCloseSuccess.
  ///
  /// In fr, this message translates to:
  /// **'Compte fermé. Vos produits ne sont plus en vente.'**
  String get accCloseSuccess;

  /// No description provided for @accDeleteSuccess.
  ///
  /// In fr, this message translates to:
  /// **'Votre compte a été supprimé.'**
  String get accDeleteSuccess;

  /// No description provided for @accDeleteSheetTitle.
  ///
  /// In fr, this message translates to:
  /// **'Supprimer définitivement le compte'**
  String get accDeleteSheetTitle;

  /// No description provided for @accDeleteSheetBody.
  ///
  /// In fr, this message translates to:
  /// **'Cette action est irréversible. Votre compte, votre boutique et vos produits seront supprimés, et vos données personnelles anonymisées. Les documents exigés par la loi (factures, versements) sont conservés sans lien avec votre identité.'**
  String get accDeleteSheetBody;

  /// No description provided for @accDeleteSheetUnderstand.
  ///
  /// In fr, this message translates to:
  /// **'Je comprends que cette suppression est définitive.'**
  String get accDeleteSheetUnderstand;

  /// No description provided for @accPasswordLabel.
  ///
  /// In fr, this message translates to:
  /// **'Votre mot de passe'**
  String get accPasswordLabel;

  /// No description provided for @accPasswordHint.
  ///
  /// In fr, this message translates to:
  /// **'Pour confirmer que c\'est bien vous'**
  String get accPasswordHint;

  /// No description provided for @accDeleteSheetAction.
  ///
  /// In fr, this message translates to:
  /// **'Supprimer mon compte'**
  String get accDeleteSheetAction;

  /// No description provided for @accReactivationPendingTitle.
  ///
  /// In fr, this message translates to:
  /// **'Réactivation en cours d\'examen'**
  String get accReactivationPendingTitle;

  /// No description provided for @accClosedTitle.
  ///
  /// In fr, this message translates to:
  /// **'Compte fermé'**
  String get accClosedTitle;

  /// No description provided for @accReactivationPendingBody.
  ///
  /// In fr, this message translates to:
  /// **'Votre demande de réactivation a été envoyée. Un administrateur va l\'examiner ; vos produits restent hors vente en attendant.'**
  String get accReactivationPendingBody;

  /// No description provided for @accClosedBody.
  ///
  /// In fr, this message translates to:
  /// **'Votre compte est fermé : vos produits ne sont plus en vente. Vous pouvez demander sa réactivation.'**
  String get accClosedBody;

  /// No description provided for @accRequestReactivation.
  ///
  /// In fr, this message translates to:
  /// **'Demander la réactivation'**
  String get accRequestReactivation;

  /// No description provided for @accReactivationSent.
  ///
  /// In fr, this message translates to:
  /// **'Demande envoyée. Un administrateur va l\'examiner.'**
  String get accReactivationSent;

  /// No description provided for @prodTitle.
  ///
  /// In fr, this message translates to:
  /// **'Produits'**
  String get prodTitle;

  /// No description provided for @prodNew.
  ///
  /// In fr, this message translates to:
  /// **'Nouveau'**
  String get prodNew;

  /// No description provided for @prodSearchHint.
  ///
  /// In fr, this message translates to:
  /// **'Rechercher un produit'**
  String get prodSearchHint;

  /// No description provided for @prodEmpty.
  ///
  /// In fr, this message translates to:
  /// **'Aucun produit.\nAjoutez votre premier article pour commencer à vendre.'**
  String get prodEmpty;

  /// No description provided for @prodStep1Title.
  ///
  /// In fr, this message translates to:
  /// **'1. Décrire le produit'**
  String get prodStep1Title;

  /// No description provided for @prodStep1Subtitle.
  ///
  /// In fr, this message translates to:
  /// **'Catégorie, nom, description, photos'**
  String get prodStep1Subtitle;

  /// No description provided for @prodStep2Title.
  ///
  /// In fr, this message translates to:
  /// **'2. La déclinaison'**
  String get prodStep2Title;

  /// No description provided for @prodStep2Subtitle.
  ///
  /// In fr, this message translates to:
  /// **'La version vendue (taille, couleur… ou par défaut)'**
  String get prodStep2Subtitle;

  /// No description provided for @prodStep3Title.
  ///
  /// In fr, this message translates to:
  /// **'3. Mise en vente & stock'**
  String get prodStep3Title;

  /// No description provided for @prodStep3Subtitle.
  ///
  /// In fr, this message translates to:
  /// **'État, prix, lieu d\'expédition et quantité'**
  String get prodStep3Subtitle;

  /// No description provided for @prodHowToTitle.
  ///
  /// In fr, this message translates to:
  /// **'Comment créer un produit'**
  String get prodHowToTitle;

  /// No description provided for @prodHowToSubtitle.
  ///
  /// In fr, this message translates to:
  /// **'Un produit est vendable une fois ses 3 étapes complétées.'**
  String get prodHowToSubtitle;

  /// No description provided for @prodHowToCta.
  ///
  /// In fr, this message translates to:
  /// **'Créer mon produit'**
  String get prodHowToCta;

  /// No description provided for @prodFilterAll.
  ///
  /// In fr, this message translates to:
  /// **'Tous'**
  String get prodFilterAll;

  /// No description provided for @prodFilterActive.
  ///
  /// In fr, this message translates to:
  /// **'En ligne'**
  String get prodFilterActive;

  /// No description provided for @prodFilterDraft.
  ///
  /// In fr, this message translates to:
  /// **'Brouillons'**
  String get prodFilterDraft;

  /// No description provided for @prodFilterArchived.
  ///
  /// In fr, this message translates to:
  /// **'Archivés'**
  String get prodFilterArchived;

  /// No description provided for @prodActionsTooltip.
  ///
  /// In fr, this message translates to:
  /// **'Actions'**
  String get prodActionsTooltip;

  /// No description provided for @prodMenuOpen.
  ///
  /// In fr, this message translates to:
  /// **'Voir le détail'**
  String get prodMenuOpen;

  /// No description provided for @prodMenuPublish.
  ///
  /// In fr, this message translates to:
  /// **'Mettre en ligne'**
  String get prodMenuPublish;

  /// No description provided for @prodMenuDraft.
  ///
  /// In fr, this message translates to:
  /// **'Repasser en brouillon'**
  String get prodMenuDraft;

  /// No description provided for @prodMenuArchive.
  ///
  /// In fr, this message translates to:
  /// **'Archiver'**
  String get prodMenuArchive;

  /// No description provided for @prodUpdated.
  ///
  /// In fr, this message translates to:
  /// **'Produit mis à jour.'**
  String get prodUpdated;

  /// No description provided for @pdTitle.
  ///
  /// In fr, this message translates to:
  /// **'Fiche produit'**
  String get pdTitle;

  /// No description provided for @pdActionsTooltip.
  ///
  /// In fr, this message translates to:
  /// **'Actions'**
  String get pdActionsTooltip;

  /// No description provided for @pdMenuPreview.
  ///
  /// In fr, this message translates to:
  /// **'Aperçu client'**
  String get pdMenuPreview;

  /// No description provided for @pdEditSheet.
  ///
  /// In fr, this message translates to:
  /// **'Modifier la fiche'**
  String get pdEditSheet;

  /// No description provided for @pdUnlist.
  ///
  /// In fr, this message translates to:
  /// **'Retirer de la vente'**
  String get pdUnlist;

  /// No description provided for @pdMenuPublish.
  ///
  /// In fr, this message translates to:
  /// **'Mettre en ligne'**
  String get pdMenuPublish;

  /// No description provided for @pdMenuDraft.
  ///
  /// In fr, this message translates to:
  /// **'Repasser en brouillon'**
  String get pdMenuDraft;

  /// No description provided for @pdMenuArchive.
  ///
  /// In fr, this message translates to:
  /// **'Archiver'**
  String get pdMenuArchive;

  /// No description provided for @pdUnlistTitle.
  ///
  /// In fr, this message translates to:
  /// **'Retirer de la vente ?'**
  String get pdUnlistTitle;

  /// No description provided for @pdUnlistBody.
  ///
  /// In fr, this message translates to:
  /// **'Les {count} mise(s) en vente active(s) de « {name} » seront mises en pause : le produit ne pourra plus être acheté. Vos photos, votre stock et vos prix sont conservés — vous pourrez le remettre en vente quand vous voudrez.'**
  String pdUnlistBody(int count, String name);

  /// No description provided for @pdUnlistSuccess.
  ///
  /// In fr, this message translates to:
  /// **'Produit retiré de la vente.'**
  String get pdUnlistSuccess;

  /// No description provided for @pdDeleteTitle.
  ///
  /// In fr, this message translates to:
  /// **'Supprimer ce produit ?'**
  String get pdDeleteTitle;

  /// No description provided for @pdDeleteBody.
  ///
  /// In fr, this message translates to:
  /// **'Le produit, ses photos et ses déclinaisons seront supprimés. Cette action est définitive — préférez « Archiver » pour le retirer de la vente en gardant l\'historique.'**
  String get pdDeleteBody;

  /// No description provided for @pdDeleteSuccess.
  ///
  /// In fr, this message translates to:
  /// **'Produit supprimé.'**
  String get pdDeleteSuccess;

  /// No description provided for @pdUpdated.
  ///
  /// In fr, this message translates to:
  /// **'Produit mis à jour.'**
  String get pdUpdated;

  /// No description provided for @pdPhotosTooLargeIgnored.
  ///
  /// In fr, this message translates to:
  /// **'{count} photo(s) dépassent 5 Mo et ont été ignorées.'**
  String pdPhotosTooLargeIgnored(int count);

  /// No description provided for @pdPhotosAdded.
  ///
  /// In fr, this message translates to:
  /// **'{count} photo(s) ajoutée(s).'**
  String pdPhotosAdded(int count);

  /// No description provided for @pdGalleryEmpty.
  ///
  /// In fr, this message translates to:
  /// **'Aucune photo — vos acheteurs ne verront rien.'**
  String get pdGalleryEmpty;

  /// No description provided for @pdAddPhotos.
  ///
  /// In fr, this message translates to:
  /// **'Ajouter des photos'**
  String get pdAddPhotos;

  /// No description provided for @pdPhotoCount.
  ///
  /// In fr, this message translates to:
  /// **'{count} photo(s)'**
  String pdPhotoCount(int count);

  /// No description provided for @pdPrimarySet.
  ///
  /// In fr, this message translates to:
  /// **'Photo principale définie.'**
  String get pdPrimarySet;

  /// No description provided for @pdPhotoDeleted.
  ///
  /// In fr, this message translates to:
  /// **'Photo supprimée.'**
  String get pdPhotoDeleted;

  /// No description provided for @pdPrimaryHint.
  ///
  /// In fr, this message translates to:
  /// **'La photo « Principale » est celle que voit l\'acheteur dans les listes.'**
  String get pdPrimaryHint;

  /// No description provided for @pdPrimaryBadge.
  ///
  /// In fr, this message translates to:
  /// **'Principale'**
  String get pdPrimaryBadge;

  /// No description provided for @pdSetPrimary.
  ///
  /// In fr, this message translates to:
  /// **'Définir comme photo principale'**
  String get pdSetPrimary;

  /// No description provided for @pdDeletePhoto.
  ///
  /// In fr, this message translates to:
  /// **'Supprimer la photo'**
  String get pdDeletePhoto;

  /// No description provided for @pdInfoSection.
  ///
  /// In fr, this message translates to:
  /// **'Informations'**
  String get pdInfoSection;

  /// No description provided for @pdInfoCategory.
  ///
  /// In fr, this message translates to:
  /// **'Catégorie'**
  String get pdInfoCategory;

  /// No description provided for @pdInfoVariants.
  ///
  /// In fr, this message translates to:
  /// **'Déclinaisons'**
  String get pdInfoVariants;

  /// No description provided for @pdInfoNoDescription.
  ///
  /// In fr, this message translates to:
  /// **'Aucune description. Les acheteurs achètent ce qu\'ils comprennent : ajoutez-en une.'**
  String get pdInfoNoDescription;

  /// No description provided for @pdVariantsSection.
  ///
  /// In fr, this message translates to:
  /// **'Déclinaisons ({count})'**
  String pdVariantsSection(int count);

  /// No description provided for @pdSectionAdd.
  ///
  /// In fr, this message translates to:
  /// **'AJOUTER'**
  String get pdSectionAdd;

  /// No description provided for @pdVariantsEmpty.
  ///
  /// In fr, this message translates to:
  /// **'Aucune déclinaison. Le stock et les mises en vente se rattachent à un SKU : créez au moins une déclinaison pour pouvoir vendre ce produit.'**
  String get pdVariantsEmpty;

  /// No description provided for @pdCreateVariant.
  ///
  /// In fr, this message translates to:
  /// **'Créer une déclinaison'**
  String get pdCreateVariant;

  /// No description provided for @pdDeleteVariantTitle.
  ///
  /// In fr, this message translates to:
  /// **'Supprimer la déclinaison ?'**
  String get pdDeleteVariantTitle;

  /// No description provided for @pdDeleteVariantBody.
  ///
  /// In fr, this message translates to:
  /// **'La déclinaison « {label} » et le stock rattaché au SKU {sku} ne seront plus proposés.'**
  String pdDeleteVariantBody(String label, String sku);

  /// No description provided for @pdVariantDeleted.
  ///
  /// In fr, this message translates to:
  /// **'Déclinaison supprimée.'**
  String get pdVariantDeleted;

  /// No description provided for @pdVariantAdded.
  ///
  /// In fr, this message translates to:
  /// **'Déclinaison ajoutée.'**
  String get pdVariantAdded;

  /// No description provided for @pdNewVariant.
  ///
  /// In fr, this message translates to:
  /// **'Nouvelle déclinaison'**
  String get pdNewVariant;

  /// No description provided for @pdVariantSkuIntro.
  ///
  /// In fr, this message translates to:
  /// **'Le SKU est votre référence interne : il porte le stock et la mise en vente. Il doit être unique.'**
  String get pdVariantSkuIntro;

  /// No description provided for @pdSkuHelper.
  ///
  /// In fr, this message translates to:
  /// **'Généré automatiquement — modifiable. Laissé vide, il sera régénéré.'**
  String get pdSkuHelper;

  /// No description provided for @pdAttrLabel.
  ///
  /// In fr, this message translates to:
  /// **'Attribut'**
  String get pdAttrLabel;

  /// No description provided for @pdAttrHintSize.
  ///
  /// In fr, this message translates to:
  /// **'Taille'**
  String get pdAttrHintSize;

  /// No description provided for @pdValueLabel.
  ///
  /// In fr, this message translates to:
  /// **'Valeur'**
  String get pdValueLabel;

  /// No description provided for @pdRemoveAttr.
  ///
  /// In fr, this message translates to:
  /// **'Retirer cet attribut'**
  String get pdRemoveAttr;

  /// No description provided for @pdAddAttr.
  ///
  /// In fr, this message translates to:
  /// **'Ajouter un attribut'**
  String get pdAddAttr;

  /// No description provided for @pdBarcodeOptional.
  ///
  /// In fr, this message translates to:
  /// **'Code-barres (facultatif)'**
  String get pdBarcodeOptional;

  /// No description provided for @pdWeight.
  ///
  /// In fr, this message translates to:
  /// **'Poids'**
  String get pdWeight;

  /// No description provided for @pdAddVariant.
  ///
  /// In fr, this message translates to:
  /// **'Ajouter la déclinaison'**
  String get pdAddVariant;

  /// No description provided for @pdLocationShort.
  ///
  /// In fr, this message translates to:
  /// **'Lieu'**
  String get pdLocationShort;

  /// No description provided for @pdStockUnavailable.
  ///
  /// In fr, this message translates to:
  /// **'Stock indisponible : {error}'**
  String pdStockUnavailable(String error);

  /// No description provided for @pdNoStockForSku.
  ///
  /// In fr, this message translates to:
  /// **'Aucun stock enregistré pour ce SKU.'**
  String get pdNoStockForSku;

  /// No description provided for @pdLocationFallback.
  ///
  /// In fr, this message translates to:
  /// **'Lieu'**
  String get pdLocationFallback;

  /// No description provided for @pdStockAvailable.
  ///
  /// In fr, this message translates to:
  /// **'{count} dispo'**
  String pdStockAvailable(int count);

  /// No description provided for @pdStockLow.
  ///
  /// In fr, this message translates to:
  /// **'Bas'**
  String get pdStockLow;

  /// No description provided for @pdStockLine.
  ///
  /// In fr, this message translates to:
  /// **'En stock {onHand} · Réservé {reserved} · Seuil d\'\'alerte {threshold}'**
  String pdStockLine(int onHand, int reserved, int threshold);

  /// No description provided for @pdStockReceive.
  ///
  /// In fr, this message translates to:
  /// **'Réceptionner'**
  String get pdStockReceive;

  /// No description provided for @pdStockAdjust.
  ///
  /// In fr, this message translates to:
  /// **'Ajuster ±'**
  String get pdStockAdjust;

  /// No description provided for @pdThresholdShort.
  ///
  /// In fr, this message translates to:
  /// **'Seuil'**
  String get pdThresholdShort;

  /// No description provided for @pdReceiveTitle.
  ///
  /// In fr, this message translates to:
  /// **'Réceptionner du stock'**
  String get pdReceiveTitle;

  /// No description provided for @pdReceiveHint.
  ///
  /// In fr, this message translates to:
  /// **'Quantité reçue (entrée de marchandise).'**
  String get pdReceiveHint;

  /// No description provided for @pdQuantity.
  ///
  /// In fr, this message translates to:
  /// **'Quantité'**
  String get pdQuantity;

  /// No description provided for @pdAdjustTitle.
  ///
  /// In fr, this message translates to:
  /// **'Ajuster le stock'**
  String get pdAdjustTitle;

  /// No description provided for @pdAdjustHint.
  ///
  /// In fr, this message translates to:
  /// **'Correction signée : −2 pour une perte, +2 pour un retour en rayon.'**
  String get pdAdjustHint;

  /// No description provided for @pdAdjustLabel.
  ///
  /// In fr, this message translates to:
  /// **'Écart (+ / −)'**
  String get pdAdjustLabel;

  /// No description provided for @pdAlertThreshold.
  ///
  /// In fr, this message translates to:
  /// **'Seuil d\'alerte'**
  String get pdAlertThreshold;

  /// No description provided for @pdThresholdHint.
  ///
  /// In fr, this message translates to:
  /// **'En dessous de ce niveau, le stock est signalé comme bas.'**
  String get pdThresholdHint;

  /// No description provided for @pdEnterInteger.
  ///
  /// In fr, this message translates to:
  /// **'Saisissez un nombre entier.'**
  String get pdEnterInteger;

  /// No description provided for @pdValuePositive.
  ///
  /// In fr, this message translates to:
  /// **'La valeur doit être positive.'**
  String get pdValuePositive;

  /// No description provided for @pdAdjustZero.
  ///
  /// In fr, this message translates to:
  /// **'Un ajustement de zéro ne change rien.'**
  String get pdAdjustZero;

  /// No description provided for @pdStockUpdated.
  ///
  /// In fr, this message translates to:
  /// **'Stock mis à jour.'**
  String get pdStockUpdated;

  /// No description provided for @pdValidate.
  ///
  /// In fr, this message translates to:
  /// **'Valider'**
  String get pdValidate;

  /// No description provided for @pdChooseLocation.
  ///
  /// In fr, this message translates to:
  /// **'Choisissez un lieu.'**
  String get pdChooseLocation;

  /// No description provided for @pdStockCreated.
  ///
  /// In fr, this message translates to:
  /// **'Stock créé.'**
  String get pdStockCreated;

  /// No description provided for @pdStockSkuTitle.
  ///
  /// In fr, this message translates to:
  /// **'Stock — SKU {sku}'**
  String pdStockSkuTitle(String sku);

  /// No description provided for @pdLocationsUnavailable.
  ///
  /// In fr, this message translates to:
  /// **'Lieux indisponibles : {error}'**
  String pdLocationsUnavailable(String error);

  /// No description provided for @pdNoShipLocation.
  ///
  /// In fr, this message translates to:
  /// **'Aucun lieu d\'expédition. Créez-en un depuis une mise en vente avant d\'enregistrer du stock.'**
  String get pdNoShipLocation;

  /// No description provided for @pdShipLocation.
  ///
  /// In fr, this message translates to:
  /// **'Lieu d\'expédition'**
  String get pdShipLocation;

  /// No description provided for @pdStockQty.
  ///
  /// In fr, this message translates to:
  /// **'Quantité en stock'**
  String get pdStockQty;

  /// No description provided for @pdCreateStock.
  ///
  /// In fr, this message translates to:
  /// **'Créer le stock'**
  String get pdCreateStock;

  /// No description provided for @pdOffersSection.
  ///
  /// In fr, this message translates to:
  /// **'Mises en vente ({count})'**
  String pdOffersSection(int count);

  /// No description provided for @pdOffersUnavailable.
  ///
  /// In fr, this message translates to:
  /// **'Mises en vente indisponibles : {error}'**
  String pdOffersUnavailable(String error);

  /// No description provided for @pdNoOfferCanCreate.
  ///
  /// In fr, this message translates to:
  /// **'Aucune mise en vente : ce produit n\'est pas achetable, même en ligne. Créez une mise en vente pour fixer votre prix.'**
  String get pdNoOfferCanCreate;

  /// No description provided for @pdNoOfferNoVariant.
  ///
  /// In fr, this message translates to:
  /// **'Aucune mise en vente possible : créez d\'abord une déclinaison, c\'est elle qui porte le SKU de la mise en vente.'**
  String get pdNoOfferNoVariant;

  /// No description provided for @pdPutOnSale.
  ///
  /// In fr, this message translates to:
  /// **'Mettre en vente'**
  String get pdPutOnSale;

  /// No description provided for @pdNameMin3.
  ///
  /// In fr, this message translates to:
  /// **'Le nom doit faire au moins 3 caractères.'**
  String get pdNameMin3;

  /// No description provided for @pdSheetUpdated.
  ///
  /// In fr, this message translates to:
  /// **'Fiche mise à jour.'**
  String get pdSheetUpdated;

  /// No description provided for @pdName.
  ///
  /// In fr, this message translates to:
  /// **'Nom'**
  String get pdName;

  /// No description provided for @pdGtinOptional.
  ///
  /// In fr, this message translates to:
  /// **'GTIN (facultatif)'**
  String get pdGtinOptional;

  /// No description provided for @pdEanOptional.
  ///
  /// In fr, this message translates to:
  /// **'EAN (facultatif)'**
  String get pdEanOptional;

  /// No description provided for @pwChooseCategory.
  ///
  /// In fr, this message translates to:
  /// **'Choisissez une catégorie.'**
  String get pwChooseCategory;

  /// No description provided for @pwNameMin3.
  ///
  /// In fr, this message translates to:
  /// **'Le nom doit faire au moins 3 caractères.'**
  String get pwNameMin3;

  /// No description provided for @pwDescribeMin10.
  ///
  /// In fr, this message translates to:
  /// **'Décrivez le produit (10 caractères minimum).'**
  String get pwDescribeMin10;

  /// No description provided for @pwAddPhoto.
  ///
  /// In fr, this message translates to:
  /// **'Ajoutez au moins une photo.'**
  String get pwAddPhoto;

  /// No description provided for @pwPhotoTooLarge.
  ///
  /// In fr, this message translates to:
  /// **'Une photo dépasse 5 Mo. Retirez-la.'**
  String get pwPhotoTooLarge;

  /// No description provided for @pwSkuRequired.
  ///
  /// In fr, this message translates to:
  /// **'Le SKU de la déclinaison est obligatoire.'**
  String get pwSkuRequired;

  /// No description provided for @pwPriceInvalid.
  ///
  /// In fr, this message translates to:
  /// **'Indiquez un prix valide.'**
  String get pwPriceInvalid;

  /// No description provided for @pwChooseLocation.
  ///
  /// In fr, this message translates to:
  /// **'Choisissez un lieu d\'expédition.'**
  String get pwChooseLocation;

  /// No description provided for @pwStockMin1.
  ///
  /// In fr, this message translates to:
  /// **'Indiquez un stock d\'au moins 1 unité.'**
  String get pwStockMin1;

  /// No description provided for @pwStepProduct.
  ///
  /// In fr, this message translates to:
  /// **'produit'**
  String get pwStepProduct;

  /// No description provided for @pwStepVariant.
  ///
  /// In fr, this message translates to:
  /// **'déclinaison'**
  String get pwStepVariant;

  /// No description provided for @pwStepOffer.
  ///
  /// In fr, this message translates to:
  /// **'mise en vente'**
  String get pwStepOffer;

  /// No description provided for @pwStepStock.
  ///
  /// In fr, this message translates to:
  /// **'stock'**
  String get pwStepStock;

  /// No description provided for @pwCreatedSuccess.
  ///
  /// In fr, this message translates to:
  /// **'Produit prêt : déclinaison, mise en vente et stock créés.'**
  String get pwCreatedSuccess;

  /// No description provided for @pwNothingKept.
  ///
  /// In fr, this message translates to:
  /// **'Rien n\'a été conservé.'**
  String get pwNothingKept;

  /// No description provided for @pwDraftKept.
  ///
  /// In fr, this message translates to:
  /// **'Le brouillon a été gardé — supprimez-le depuis Produits.'**
  String get pwDraftKept;

  /// No description provided for @pwStepFailed.
  ///
  /// In fr, this message translates to:
  /// **'Échec à l\'\'étape « {step} » : {error} {tail}'**
  String pwStepFailed(String step, String error, String tail);

  /// No description provided for @pwNewProduct.
  ///
  /// In fr, this message translates to:
  /// **'Nouveau produit'**
  String get pwNewProduct;

  /// No description provided for @pwCategoriesUnavailable.
  ///
  /// In fr, this message translates to:
  /// **'Catégories indisponibles.'**
  String get pwCategoriesUnavailable;

  /// No description provided for @pwProductName.
  ///
  /// In fr, this message translates to:
  /// **'Nom du produit'**
  String get pwProductName;

  /// No description provided for @pwProductNameHint.
  ///
  /// In fr, this message translates to:
  /// **'ex. Robe wax grande taille'**
  String get pwProductNameHint;

  /// No description provided for @pwDescriptionHelper.
  ///
  /// In fr, this message translates to:
  /// **'Matière, dimensions, ce qui est inclus — c\'est ce qui déclenche l\'achat.'**
  String get pwDescriptionHelper;

  /// No description provided for @pwMoreInfo.
  ///
  /// In fr, this message translates to:
  /// **'Informations complémentaires (facultatif)'**
  String get pwMoreInfo;

  /// No description provided for @pwBrandOptional.
  ///
  /// In fr, this message translates to:
  /// **'Marque (facultatif)'**
  String get pwBrandOptional;

  /// No description provided for @pwNone.
  ///
  /// In fr, this message translates to:
  /// **'Aucune'**
  String get pwNone;

  /// No description provided for @pwGtinOptional.
  ///
  /// In fr, this message translates to:
  /// **'Code GTIN (facultatif)'**
  String get pwGtinOptional;

  /// No description provided for @pwEanOptional.
  ///
  /// In fr, this message translates to:
  /// **'Code EAN (facultatif)'**
  String get pwEanOptional;

  /// No description provided for @pwTagsOptional.
  ///
  /// In fr, this message translates to:
  /// **'Tags (facultatif)'**
  String get pwTagsOptional;

  /// No description provided for @pwTagsHint.
  ///
  /// In fr, this message translates to:
  /// **'ex. wax, coton, fait main'**
  String get pwTagsHint;

  /// No description provided for @pwTagsHelper.
  ///
  /// In fr, this message translates to:
  /// **'Séparez les tags par une virgule.'**
  String get pwTagsHelper;

  /// No description provided for @pwVariantInfo.
  ///
  /// In fr, this message translates to:
  /// **'Une déclinaison est une version de votre produit (ex. une taille, une couleur). Un produit simple n\'en a qu\'une. Le SKU l\'identifie et porte le stock.'**
  String get pwVariantInfo;

  /// No description provided for @pwSkuLabel.
  ///
  /// In fr, this message translates to:
  /// **'SKU (référence interne)'**
  String get pwSkuLabel;

  /// No description provided for @pwSkuHelper.
  ///
  /// In fr, this message translates to:
  /// **'Généré automatiquement. Modifiable si vous avez votre propre système.'**
  String get pwSkuHelper;

  /// No description provided for @pwAttrLogistics.
  ///
  /// In fr, this message translates to:
  /// **'Attributs & logistique (facultatif)'**
  String get pwAttrLogistics;

  /// No description provided for @pwAttrIntro.
  ///
  /// In fr, this message translates to:
  /// **'Attributs (taille, couleur…) — seulement si cette déclinaison en a.'**
  String get pwAttrIntro;

  /// No description provided for @pwAttrLabel.
  ///
  /// In fr, this message translates to:
  /// **'Attribut'**
  String get pwAttrLabel;

  /// No description provided for @pwValueLabel.
  ///
  /// In fr, this message translates to:
  /// **'Valeur'**
  String get pwValueLabel;

  /// No description provided for @pwAddAttr.
  ///
  /// In fr, this message translates to:
  /// **'Ajouter un attribut'**
  String get pwAddAttr;

  /// No description provided for @pwBarcodeOptional.
  ///
  /// In fr, this message translates to:
  /// **'Code-barres (facultatif)'**
  String get pwBarcodeOptional;

  /// No description provided for @pwWeightG.
  ///
  /// In fr, this message translates to:
  /// **'Poids (g)'**
  String get pwWeightG;

  /// No description provided for @pwCondition.
  ///
  /// In fr, this message translates to:
  /// **'État du produit'**
  String get pwCondition;

  /// No description provided for @pwYourPrice.
  ///
  /// In fr, this message translates to:
  /// **'Votre prix (net perçu)'**
  String get pwYourPrice;

  /// No description provided for @pwLocationsUnavailable.
  ///
  /// In fr, this message translates to:
  /// **'Lieux indisponibles.'**
  String get pwLocationsUnavailable;

  /// No description provided for @pwShipLocation.
  ///
  /// In fr, this message translates to:
  /// **'Lieu d\'expédition'**
  String get pwShipLocation;

  /// No description provided for @pwAddLocation.
  ///
  /// In fr, this message translates to:
  /// **'Ajouter un lieu'**
  String get pwAddLocation;

  /// No description provided for @pwStockQty.
  ///
  /// In fr, this message translates to:
  /// **'Stock (quantité)'**
  String get pwStockQty;

  /// No description provided for @pwAlertThreshold.
  ///
  /// In fr, this message translates to:
  /// **'Seuil d\'alerte'**
  String get pwAlertThreshold;

  /// No description provided for @pwHandlingTime.
  ///
  /// In fr, this message translates to:
  /// **'Délai de préparation'**
  String get pwHandlingTime;

  /// No description provided for @pwDays.
  ///
  /// In fr, this message translates to:
  /// **'{count, plural, =1{1 jour} other{{count} jours}}'**
  String pwDays(int count);

  /// No description provided for @pwReviewTitle.
  ///
  /// In fr, this message translates to:
  /// **'Vérifiez avant de créer'**
  String get pwReviewTitle;

  /// No description provided for @pwSummaryProduct.
  ///
  /// In fr, this message translates to:
  /// **'Produit'**
  String get pwSummaryProduct;

  /// No description provided for @pwName.
  ///
  /// In fr, this message translates to:
  /// **'Nom'**
  String get pwName;

  /// No description provided for @pwCategory.
  ///
  /// In fr, this message translates to:
  /// **'Catégorie'**
  String get pwCategory;

  /// No description provided for @pwPhotosCount.
  ///
  /// In fr, this message translates to:
  /// **'Photos ({count})'**
  String pwPhotosCount(int count);

  /// No description provided for @pwSummaryVariant.
  ///
  /// In fr, this message translates to:
  /// **'Déclinaison'**
  String get pwSummaryVariant;

  /// No description provided for @pwAttributes.
  ///
  /// In fr, this message translates to:
  /// **'Attributs'**
  String get pwAttributes;

  /// No description provided for @pwSummaryOffer.
  ///
  /// In fr, this message translates to:
  /// **'Mise en vente & stock'**
  String get pwSummaryOffer;

  /// No description provided for @pwConditionShort.
  ///
  /// In fr, this message translates to:
  /// **'État'**
  String get pwConditionShort;

  /// No description provided for @pwNetPrice.
  ///
  /// In fr, this message translates to:
  /// **'Votre prix net'**
  String get pwNetPrice;

  /// No description provided for @pwDisplayedPrice.
  ///
  /// In fr, this message translates to:
  /// **'Prix affiché client'**
  String get pwDisplayedPrice;

  /// No description provided for @pwConfirmNote.
  ///
  /// In fr, this message translates to:
  /// **'En confirmant, le produit, sa déclinaison, sa mise en vente et son stock sont créés d\'un coup. Le produit sera visible des acheteurs une fois votre boutique validée (KYB).'**
  String get pwConfirmNote;

  /// No description provided for @pwAddress.
  ///
  /// In fr, this message translates to:
  /// **'Adresse'**
  String get pwAddress;

  /// No description provided for @pwCreateAndSell.
  ///
  /// In fr, this message translates to:
  /// **'Créer et mettre en vente'**
  String get pwCreateAndSell;

  /// No description provided for @pwStepBarProduct.
  ///
  /// In fr, this message translates to:
  /// **'Produit'**
  String get pwStepBarProduct;

  /// No description provided for @pwStepBarVariant.
  ///
  /// In fr, this message translates to:
  /// **'Déclinaison'**
  String get pwStepBarVariant;

  /// No description provided for @pwStepBarOffer.
  ///
  /// In fr, this message translates to:
  /// **'Mise en vente'**
  String get pwStepBarOffer;

  /// No description provided for @pwStepBarSummary.
  ///
  /// In fr, this message translates to:
  /// **'Résumé'**
  String get pwStepBarSummary;

  /// No description provided for @pwStepIndicator.
  ///
  /// In fr, this message translates to:
  /// **'Étape {current}/{total} — {label}'**
  String pwStepIndicator(int current, int total, String label);

  /// No description provided for @pwNoLocationBody.
  ///
  /// In fr, this message translates to:
  /// **'Aucun lieu d\'expédition. Il en faut un pour mettre en vente (l\'adresse d\'où partent vos colis).'**
  String get pwNoLocationBody;

  /// No description provided for @pwAddShipLocation.
  ///
  /// In fr, this message translates to:
  /// **'Ajouter un lieu d\'expédition'**
  String get pwAddShipLocation;

  /// No description provided for @pcChooseCategory.
  ///
  /// In fr, this message translates to:
  /// **'Choisissez une catégorie.'**
  String get pcChooseCategory;

  /// No description provided for @pcAddPhoto.
  ///
  /// In fr, this message translates to:
  /// **'Ajoutez au moins une photo : sans image, le produit ne peut pas être créé.'**
  String get pcAddPhoto;

  /// No description provided for @pcPhotosTooLarge.
  ///
  /// In fr, this message translates to:
  /// **'{count} photo(s) dépassent 5 Mo. Retirez-les ou réessayez le détourage.'**
  String pcPhotosTooLarge(int count);

  /// No description provided for @pcDuplicateAttr.
  ///
  /// In fr, this message translates to:
  /// **'Attribut « {key} » en double.'**
  String pcDuplicateAttr(String key);

  /// No description provided for @pcCreatedDraft.
  ///
  /// In fr, this message translates to:
  /// **'Produit créé en brouillon.'**
  String get pcCreatedDraft;

  /// No description provided for @pcDiscardTitle.
  ///
  /// In fr, this message translates to:
  /// **'Abandonner ce produit ?'**
  String get pcDiscardTitle;

  /// No description provided for @pcDiscardBody.
  ///
  /// In fr, this message translates to:
  /// **'Ce que vous avez saisi, photos comprises, sera perdu.'**
  String get pcDiscardBody;

  /// No description provided for @pcKeepEditing.
  ///
  /// In fr, this message translates to:
  /// **'Continuer la saisie'**
  String get pcKeepEditing;

  /// No description provided for @pcDiscard.
  ///
  /// In fr, this message translates to:
  /// **'Abandonner'**
  String get pcDiscard;

  /// No description provided for @pcNewProduct.
  ///
  /// In fr, this message translates to:
  /// **'Nouveau produit'**
  String get pcNewProduct;

  /// No description provided for @pcDraftNote.
  ///
  /// In fr, this message translates to:
  /// **'Le produit est créé en brouillon. Vous créerez ensuite une mise en vente (prix, stock) pour le mettre en vente.'**
  String get pcDraftNote;

  /// No description provided for @pcLegendInfo.
  ///
  /// In fr, this message translates to:
  /// **'Informations'**
  String get pcLegendInfo;

  /// No description provided for @pcCategoriesUnavailable.
  ///
  /// In fr, this message translates to:
  /// **'Catégories indisponibles.'**
  String get pcCategoriesUnavailable;

  /// No description provided for @pcProductName.
  ///
  /// In fr, this message translates to:
  /// **'Nom du produit'**
  String get pcProductName;

  /// No description provided for @pcProductNameHint.
  ///
  /// In fr, this message translates to:
  /// **'ex. Robe wax grande taille'**
  String get pcProductNameHint;

  /// No description provided for @pcNameMin3.
  ///
  /// In fr, this message translates to:
  /// **'3 caractères minimum'**
  String get pcNameMin3;

  /// No description provided for @pcDescriptionHelper.
  ///
  /// In fr, this message translates to:
  /// **'Matière, dimensions, ce qui est inclus — c\'est ce qui déclenche l\'achat.'**
  String get pcDescriptionHelper;

  /// No description provided for @pcDescribeMin10.
  ///
  /// In fr, this message translates to:
  /// **'Décrivez le produit (10 caractères minimum)'**
  String get pcDescribeMin10;

  /// No description provided for @pcLegendId.
  ///
  /// In fr, this message translates to:
  /// **'Identification'**
  String get pcLegendId;

  /// No description provided for @pcBrandsUnavailable.
  ///
  /// In fr, this message translates to:
  /// **'Marques indisponibles — vous pouvez créer le produit sans.'**
  String get pcBrandsUnavailable;

  /// No description provided for @pcBrandOptional.
  ///
  /// In fr, this message translates to:
  /// **'Marque (facultatif)'**
  String get pcBrandOptional;

  /// No description provided for @pcNone.
  ///
  /// In fr, this message translates to:
  /// **'Aucune'**
  String get pcNone;

  /// No description provided for @pcGtinOptional.
  ///
  /// In fr, this message translates to:
  /// **'Code GTIN (facultatif)'**
  String get pcGtinOptional;

  /// No description provided for @pcGtinHint.
  ///
  /// In fr, this message translates to:
  /// **'8 à 14 chiffres'**
  String get pcGtinHint;

  /// No description provided for @pcEanOptional.
  ///
  /// In fr, this message translates to:
  /// **'Code EAN (facultatif)'**
  String get pcEanOptional;

  /// No description provided for @pcEanHint.
  ///
  /// In fr, this message translates to:
  /// **'8 ou 13 chiffres'**
  String get pcEanHint;

  /// No description provided for @pcGroupOptional.
  ///
  /// In fr, this message translates to:
  /// **'Groupe de produit (facultatif)'**
  String get pcGroupOptional;

  /// No description provided for @pcGroupHint.
  ///
  /// In fr, this message translates to:
  /// **'Identifiant du groupe'**
  String get pcGroupHint;

  /// No description provided for @pcGroupHelper.
  ///
  /// In fr, this message translates to:
  /// **'Regroupe des produits liés (même modèle, coloris différents).'**
  String get pcGroupHelper;

  /// No description provided for @pcInvalidId.
  ///
  /// In fr, this message translates to:
  /// **'Identifiant invalide'**
  String get pcInvalidId;

  /// No description provided for @pcAddTag.
  ///
  /// In fr, this message translates to:
  /// **'Ajouter un tag (facultatif)'**
  String get pcAddTag;

  /// No description provided for @pcTagHint.
  ///
  /// In fr, this message translates to:
  /// **'ex. wax, coton, fait main'**
  String get pcTagHint;

  /// No description provided for @pcTagHelper.
  ///
  /// In fr, this message translates to:
  /// **'Aide les acheteurs à trouver le produit. Entrée pour valider.'**
  String get pcTagHelper;

  /// No description provided for @pcLegendAttr.
  ///
  /// In fr, this message translates to:
  /// **'Attributs'**
  String get pcLegendAttr;

  /// No description provided for @pcAttrIntro.
  ///
  /// In fr, this message translates to:
  /// **'Caractéristiques communes à toutes les déclinaisons (matière, garantie…). Ce qui change d\'une déclinaison à l\'autre — taille, couleur — se règle dans les déclinaisons.'**
  String get pcAttrIntro;

  /// No description provided for @pcKey.
  ///
  /// In fr, this message translates to:
  /// **'Clé'**
  String get pcKey;

  /// No description provided for @pcValueLabel.
  ///
  /// In fr, this message translates to:
  /// **'Valeur'**
  String get pcValueLabel;

  /// No description provided for @pcRemoveAttr.
  ///
  /// In fr, this message translates to:
  /// **'Retirer l\'attribut'**
  String get pcRemoveAttr;

  /// No description provided for @pcAddAttr.
  ///
  /// In fr, this message translates to:
  /// **'Ajouter un attribut'**
  String get pcAddAttr;

  /// No description provided for @pcCreateProduct.
  ///
  /// In fr, this message translates to:
  /// **'Créer le produit'**
  String get pcCreateProduct;

  /// No description provided for @pcBarcodeDigits.
  ///
  /// In fr, this message translates to:
  /// **'{lengths} chiffres'**
  String pcBarcodeDigits(String lengths);

  /// No description provided for @pcMaxPhotos.
  ///
  /// In fr, this message translates to:
  /// **'{count} photos au maximum.'**
  String pcMaxPhotos(int count);

  /// No description provided for @pcOnlyAdded.
  ///
  /// In fr, this message translates to:
  /// **'Seules {room} photo(s) ont été ajoutées ({max} au maximum).'**
  String pcOnlyAdded(int room, int max);

  /// No description provided for @pcRetryFailed.
  ///
  /// In fr, this message translates to:
  /// **'Le détourage a encore échoué. La photo partira telle quelle.'**
  String get pcRetryFailed;

  /// No description provided for @pcPrimaryHint.
  ///
  /// In fr, this message translates to:
  /// **'La 1re photo est l\'image principale — la seule visible dans les listes. Appuyez sur une photo pour la déplacer en tête ou la retirer.'**
  String get pcPrimaryHint;

  /// No description provided for @pcFailedBanner.
  ///
  /// In fr, this message translates to:
  /// **'{count} photo(s) n\'\'ont pas pu être détourées : elles partiront avec leur fond d\'\'origine. Appuyez dessus pour réessayer.'**
  String pcFailedBanner(int count);

  /// No description provided for @pcOversizedBanner.
  ///
  /// In fr, this message translates to:
  /// **'{count} photo(s) dépassent 5 Mo et seront refusées par le serveur. Retirez-les.'**
  String pcOversizedBanner(int count);

  /// No description provided for @pcPhotoRequired.
  ///
  /// In fr, this message translates to:
  /// **'Au moins une photo est requise.'**
  String get pcPhotoRequired;

  /// No description provided for @pcPrimaryBadge.
  ///
  /// In fr, this message translates to:
  /// **'Principale'**
  String get pcPrimaryBadge;

  /// No description provided for @pcSetPrimary.
  ///
  /// In fr, this message translates to:
  /// **'Définir comme photo principale'**
  String get pcSetPrimary;

  /// No description provided for @pcSetPrimarySub.
  ///
  /// In fr, this message translates to:
  /// **'C\'est l\'image que verront les acheteurs dans les listes.'**
  String get pcSetPrimarySub;

  /// No description provided for @pcRetryProcessing.
  ///
  /// In fr, this message translates to:
  /// **'Réessayer le détourage'**
  String get pcRetryProcessing;

  /// No description provided for @pcRetrySub.
  ///
  /// In fr, this message translates to:
  /// **'Fond blanc automatique via le serveur.'**
  String get pcRetrySub;

  /// No description provided for @pcRemovePhoto.
  ///
  /// In fr, this message translates to:
  /// **'Retirer la photo'**
  String get pcRemovePhoto;

  /// No description provided for @pcAddPhotos.
  ///
  /// In fr, this message translates to:
  /// **'Ajouter des photos'**
  String get pcAddPhotos;

  /// No description provided for @pcPickerSubtitle.
  ///
  /// In fr, this message translates to:
  /// **'Détourage automatique sur fond blanc · 8 photos maximum'**
  String get pcPickerSubtitle;

  /// No description provided for @pcFromGallery.
  ///
  /// In fr, this message translates to:
  /// **'Depuis la galerie'**
  String get pcFromGallery;

  /// No description provided for @pcMultiSelect.
  ///
  /// In fr, this message translates to:
  /// **'Sélection multiple possible.'**
  String get pcMultiSelect;

  /// No description provided for @pcTakePhoto.
  ///
  /// In fr, this message translates to:
  /// **'Prendre une photo'**
  String get pcTakePhoto;

  /// No description provided for @pcOptional.
  ///
  /// In fr, this message translates to:
  /// **'facultatif'**
  String get pcOptional;

  /// No description provided for @cpChooseCategory.
  ///
  /// In fr, this message translates to:
  /// **'Choisissez une catégorie'**
  String get cpChooseCategory;

  /// No description provided for @cpChooseThis.
  ///
  /// In fr, this message translates to:
  /// **'Choisir « {name} »'**
  String cpChooseThis(String name);

  /// No description provided for @cpSubcategories.
  ///
  /// In fr, this message translates to:
  /// **'Sous-catégories'**
  String get cpSubcategories;

  /// No description provided for @cpCategory.
  ///
  /// In fr, this message translates to:
  /// **'Catégorie'**
  String get cpCategory;

  /// No description provided for @ppvTitle.
  ///
  /// In fr, this message translates to:
  /// **'Aperçu client'**
  String get ppvTitle;

  /// No description provided for @ppvPriceUnavailable.
  ///
  /// In fr, this message translates to:
  /// **'Prix indisponible'**
  String get ppvPriceUnavailable;

  /// No description provided for @ppvNoDescription.
  ///
  /// In fr, this message translates to:
  /// **'Aucune description — l\'acheteur ne saura pas ce qu\'il achète.'**
  String get ppvNoDescription;

  /// No description provided for @ppvAddToCart.
  ///
  /// In fr, this message translates to:
  /// **'Ajouter au panier'**
  String get ppvAddToCart;

  /// No description provided for @ppvUnavailable.
  ///
  /// In fr, this message translates to:
  /// **'Indisponible'**
  String get ppvUnavailable;

  /// No description provided for @ppvPreviewNote.
  ///
  /// In fr, this message translates to:
  /// **'Aperçu — aucune action réelle depuis cet écran.'**
  String get ppvPreviewNote;

  /// No description provided for @ppvBuyable.
  ///
  /// In fr, this message translates to:
  /// **'Ce produit est visible et achetable.'**
  String get ppvBuyable;

  /// No description provided for @ppvReasonBoth.
  ///
  /// In fr, this message translates to:
  /// **'Le produit est en brouillon ET n\'a aucune mise en vente active.'**
  String get ppvReasonBoth;

  /// No description provided for @ppvReasonDraft.
  ///
  /// In fr, this message translates to:
  /// **'Le produit est en brouillon : les acheteurs ne le voient pas.'**
  String get ppvReasonDraft;

  /// No description provided for @ppvReasonNoOffer.
  ///
  /// In fr, this message translates to:
  /// **'Aucune mise en vente active : le produit est visible mais NE PEUT PAS être acheté.'**
  String get ppvReasonNoOffer;

  /// No description provided for @ppvNoPhoto.
  ///
  /// In fr, this message translates to:
  /// **'Aucune photo — le produit ne sera pas remarqué.'**
  String get ppvNoPhoto;

  /// No description provided for @ppvInStock.
  ///
  /// In fr, this message translates to:
  /// **'En stock'**
  String get ppvInStock;

  /// No description provided for @ppvOutOfStock.
  ///
  /// In fr, this message translates to:
  /// **'Rupture de stock'**
  String get ppvOutOfStock;

  /// No description provided for @ppvShippedIn.
  ///
  /// In fr, this message translates to:
  /// **'Expédié sous {days} j'**
  String ppvShippedIn(int days);

  /// No description provided for @imgpReady.
  ///
  /// In fr, this message translates to:
  /// **'Photos préparées'**
  String get imgpReady;

  /// No description provided for @imgpPreparing.
  ///
  /// In fr, this message translates to:
  /// **'Préparation des photos…'**
  String get imgpPreparing;

  /// No description provided for @imgpDoneDesc.
  ///
  /// In fr, this message translates to:
  /// **'Détourées sur fond blanc. Comparez, puis validez.'**
  String get imgpDoneDesc;

  /// No description provided for @imgpInProgress.
  ///
  /// In fr, this message translates to:
  /// **'Détourage en cours — {done}/{total}'**
  String imgpInProgress(int done, int total);

  /// No description provided for @imgpAllFailed.
  ///
  /// In fr, this message translates to:
  /// **'Le détourage a échoué. Vous pouvez envoyer les photos d\'origine, mais elles n\'auront pas le fond blanc du catalogue.'**
  String get imgpAllFailed;

  /// No description provided for @imgpSomeFailed.
  ///
  /// In fr, this message translates to:
  /// **'{count} photo(s) n\'\'ont pas pu être détourées. Elles seront envoyées telles quelles.'**
  String imgpSomeFailed(int count);

  /// No description provided for @imgpRetry.
  ///
  /// In fr, this message translates to:
  /// **'Réessayer le détourage'**
  String get imgpRetry;

  /// No description provided for @imgpUse.
  ///
  /// In fr, this message translates to:
  /// **'Utiliser'**
  String get imgpUse;

  /// No description provided for @imgpBefore.
  ///
  /// In fr, this message translates to:
  /// **'Avant'**
  String get imgpBefore;

  /// No description provided for @imgpAfter.
  ///
  /// In fr, this message translates to:
  /// **'Après'**
  String get imgpAfter;

  /// No description provided for @imgpNotProcessed.
  ///
  /// In fr, this message translates to:
  /// **'Non détourée'**
  String get imgpNotProcessed;

  /// No description provided for @authLoginTagline.
  ///
  /// In fr, this message translates to:
  /// **'Boutique, restaurant, commandes et revenus au même endroit.'**
  String get authLoginTagline;

  /// No description provided for @authLoginEmailLabel.
  ///
  /// In fr, this message translates to:
  /// **'Téléphone ou e-mail'**
  String get authLoginEmailLabel;

  /// No description provided for @authLoginEmailInvalid.
  ///
  /// In fr, this message translates to:
  /// **'Saisissez un e-mail valide'**
  String get authLoginEmailInvalid;

  /// No description provided for @authLoginPasswordLabel.
  ///
  /// In fr, this message translates to:
  /// **'Mot de passe'**
  String get authLoginPasswordLabel;

  /// No description provided for @authLoginPasswordMin.
  ///
  /// In fr, this message translates to:
  /// **'6 caractères minimum'**
  String get authLoginPasswordMin;

  /// No description provided for @authLoginForgotPassword.
  ///
  /// In fr, this message translates to:
  /// **'Mot de passe oublié ?'**
  String get authLoginForgotPassword;

  /// No description provided for @authLoginEnableBio.
  ///
  /// In fr, this message translates to:
  /// **'Activer {label} pour les prochaines connexions'**
  String authLoginEnableBio(String label);

  /// No description provided for @authLoginMfaLabel.
  ///
  /// In fr, this message translates to:
  /// **'Code de vérification'**
  String get authLoginMfaLabel;

  /// No description provided for @authLoginResendVerification.
  ///
  /// In fr, this message translates to:
  /// **'Renvoyer le code de vérification'**
  String get authLoginResendVerification;

  /// No description provided for @authLoginSignIn.
  ///
  /// In fr, this message translates to:
  /// **'Se connecter'**
  String get authLoginSignIn;

  /// No description provided for @authLoginSignInWithBio.
  ///
  /// In fr, this message translates to:
  /// **'Se connecter avec {label}'**
  String authLoginSignInWithBio(String label);

  /// No description provided for @authLoginNoAccount.
  ///
  /// In fr, this message translates to:
  /// **'Vous n\'avez pas encore de compte vendeur ?'**
  String get authLoginNoAccount;

  /// No description provided for @authLoginCreateAccount.
  ///
  /// In fr, this message translates to:
  /// **'Créer un compte partenaire'**
  String get authLoginCreateAccount;

  /// No description provided for @authLoginEnterEmail.
  ///
  /// In fr, this message translates to:
  /// **'Saisissez votre adresse e-mail.'**
  String get authLoginEnterEmail;

  /// No description provided for @authLoginNoAccountToVerify.
  ///
  /// In fr, this message translates to:
  /// **'Aucun compte à vérifier pour cette adresse.'**
  String get authLoginNoAccountToVerify;

  /// No description provided for @authLoginBiometricReason.
  ///
  /// In fr, this message translates to:
  /// **'Connectez-vous à HbaExpress PRO'**
  String get authLoginBiometricReason;

  /// No description provided for @authRegisterTitle.
  ///
  /// In fr, this message translates to:
  /// **'Créer un compte vendeur'**
  String get authRegisterTitle;

  /// No description provided for @authRegisterHeadline.
  ///
  /// In fr, this message translates to:
  /// **'Ouvrez votre boutique'**
  String get authRegisterHeadline;

  /// No description provided for @authRegisterSubtitle.
  ///
  /// In fr, this message translates to:
  /// **'Créez votre compte en quelques minutes. Vous pourrez publier vos produits une fois votre profil validé par notre équipe.'**
  String get authRegisterSubtitle;

  /// No description provided for @authRegisterShopNameLabel.
  ///
  /// In fr, this message translates to:
  /// **'Nom de la boutique'**
  String get authRegisterShopNameLabel;

  /// No description provided for @authRegisterShopNameHint.
  ///
  /// In fr, this message translates to:
  /// **'Ma Boutique'**
  String get authRegisterShopNameHint;

  /// No description provided for @authRegisterShopNameRequired.
  ///
  /// In fr, this message translates to:
  /// **'Nom de boutique requis'**
  String get authRegisterShopNameRequired;

  /// No description provided for @authRegisterFirstNameLabel.
  ///
  /// In fr, this message translates to:
  /// **'Prénom'**
  String get authRegisterFirstNameLabel;

  /// No description provided for @authRegisterLastNameLabel.
  ///
  /// In fr, this message translates to:
  /// **'Nom'**
  String get authRegisterLastNameLabel;

  /// No description provided for @authRegisterRequired.
  ///
  /// In fr, this message translates to:
  /// **'Requis'**
  String get authRegisterRequired;

  /// No description provided for @authRegisterEmailLabel.
  ///
  /// In fr, this message translates to:
  /// **'Email'**
  String get authRegisterEmailLabel;

  /// No description provided for @authRegisterEmailInvalid.
  ///
  /// In fr, this message translates to:
  /// **'E-mail invalide'**
  String get authRegisterEmailInvalid;

  /// No description provided for @authRegisterPhoneLabel.
  ///
  /// In fr, this message translates to:
  /// **'Téléphone'**
  String get authRegisterPhoneLabel;

  /// No description provided for @authRegisterPhoneRequired.
  ///
  /// In fr, this message translates to:
  /// **'Téléphone requis'**
  String get authRegisterPhoneRequired;

  /// No description provided for @authRegisterPasswordLabel.
  ///
  /// In fr, this message translates to:
  /// **'Mot de passe'**
  String get authRegisterPasswordLabel;

  /// No description provided for @authRegisterPasswordMin.
  ///
  /// In fr, this message translates to:
  /// **'6 caractères minimum'**
  String get authRegisterPasswordMin;

  /// No description provided for @authRegisterLegalNameLabel.
  ///
  /// In fr, this message translates to:
  /// **'Raison sociale'**
  String get authRegisterLegalNameLabel;

  /// No description provided for @authRegisterActivityLabel.
  ///
  /// In fr, this message translates to:
  /// **'Activité'**
  String get authRegisterActivityLabel;

  /// No description provided for @authRegisterActivityHint.
  ///
  /// In fr, this message translates to:
  /// **'Prêt-à-porter, électronique…'**
  String get authRegisterActivityHint;

  /// No description provided for @authRegisterAddressLabel.
  ///
  /// In fr, this message translates to:
  /// **'Adresse'**
  String get authRegisterAddressLabel;

  /// No description provided for @authRegisterAddressHint.
  ///
  /// In fr, this message translates to:
  /// **'Rue, quartier'**
  String get authRegisterAddressHint;

  /// No description provided for @authRegisterCityLabel.
  ///
  /// In fr, this message translates to:
  /// **'Commune'**
  String get authRegisterCityLabel;

  /// No description provided for @authRegisterManagerLabel.
  ///
  /// In fr, this message translates to:
  /// **'Gérant'**
  String get authRegisterManagerLabel;

  /// No description provided for @authRegisterManagerHint.
  ///
  /// In fr, this message translates to:
  /// **'Nom du représentant légal'**
  String get authRegisterManagerHint;

  /// No description provided for @authRegisterCompanyPhoneLabel.
  ///
  /// In fr, this message translates to:
  /// **'Téléphone société'**
  String get authRegisterCompanyPhoneLabel;

  /// No description provided for @authRegisterCompanyToggle.
  ///
  /// In fr, this message translates to:
  /// **'Informations société (facultatif)'**
  String get authRegisterCompanyToggle;

  /// No description provided for @authRegisterContinue.
  ///
  /// In fr, this message translates to:
  /// **'Continuer'**
  String get authRegisterContinue;

  /// No description provided for @authRegisterHaveAccount.
  ///
  /// In fr, this message translates to:
  /// **'J\'ai déjà un compte — Se connecter'**
  String get authRegisterHaveAccount;

  /// No description provided for @authVerifyEnterCode.
  ///
  /// In fr, this message translates to:
  /// **'Entrez le code à 6 chiffres.'**
  String get authVerifyEnterCode;

  /// No description provided for @authVerifyEnterShopName.
  ///
  /// In fr, this message translates to:
  /// **'Entrez le nom de votre boutique.'**
  String get authVerifyEnterShopName;

  /// No description provided for @authVerifyAccountCreated.
  ///
  /// In fr, this message translates to:
  /// **'Compte créé. Connectez-vous pour accéder à votre boutique.'**
  String get authVerifyAccountCreated;

  /// No description provided for @authVerifyCodeResent.
  ///
  /// In fr, this message translates to:
  /// **'Un nouveau code vous a été envoyé.'**
  String get authVerifyCodeResent;

  /// No description provided for @authVerifyTitle.
  ///
  /// In fr, this message translates to:
  /// **'Vérification'**
  String get authVerifyTitle;

  /// No description provided for @authVerifyHeadline.
  ///
  /// In fr, this message translates to:
  /// **'Entrez votre code'**
  String get authVerifyHeadline;

  /// No description provided for @authVerifySentTo.
  ///
  /// In fr, this message translates to:
  /// **'Nous avons envoyé un code à 6 chiffres à\n'**
  String get authVerifySentTo;

  /// No description provided for @authVerifyShopNameLabel.
  ///
  /// In fr, this message translates to:
  /// **'Nom de votre boutique'**
  String get authVerifyShopNameLabel;

  /// No description provided for @authVerifyShopNameHint.
  ///
  /// In fr, this message translates to:
  /// **'Ma Boutique'**
  String get authVerifyShopNameHint;

  /// No description provided for @authVerifyValidate.
  ///
  /// In fr, this message translates to:
  /// **'Valider'**
  String get authVerifyValidate;

  /// No description provided for @authVerifySending.
  ///
  /// In fr, this message translates to:
  /// **'Envoi…'**
  String get authVerifySending;

  /// No description provided for @authVerifyResend.
  ///
  /// In fr, this message translates to:
  /// **'Renvoyer le code'**
  String get authVerifyResend;

  /// No description provided for @authForgotTitle.
  ///
  /// In fr, this message translates to:
  /// **'Mot de passe oublié'**
  String get authForgotTitle;

  /// No description provided for @authForgotHeadline.
  ///
  /// In fr, this message translates to:
  /// **'Réinitialiser le mot de passe'**
  String get authForgotHeadline;

  /// No description provided for @authForgotSubtitle.
  ///
  /// In fr, this message translates to:
  /// **'Entrez l’e-mail de votre compte : nous vous enverrons un code à 6 chiffres pour choisir un nouveau mot de passe.'**
  String get authForgotSubtitle;

  /// No description provided for @authForgotEmailInvalid.
  ///
  /// In fr, this message translates to:
  /// **'E-mail invalide'**
  String get authForgotEmailInvalid;

  /// No description provided for @authForgotSendCode.
  ///
  /// In fr, this message translates to:
  /// **'Envoyer le code'**
  String get authForgotSendCode;

  /// No description provided for @authForgotBackToLogin.
  ///
  /// In fr, this message translates to:
  /// **'Retour à la connexion'**
  String get authForgotBackToLogin;

  /// No description provided for @authResetSuccess.
  ///
  /// In fr, this message translates to:
  /// **'Mot de passe réinitialisé. Connectez-vous.'**
  String get authResetSuccess;

  /// No description provided for @authResetCodeResent.
  ///
  /// In fr, this message translates to:
  /// **'Un nouveau code vous a été envoyé.'**
  String get authResetCodeResent;

  /// No description provided for @authResetTitle.
  ///
  /// In fr, this message translates to:
  /// **'Nouveau mot de passe'**
  String get authResetTitle;

  /// No description provided for @authResetHeadline.
  ///
  /// In fr, this message translates to:
  /// **'Choisir un nouveau mot de passe'**
  String get authResetHeadline;

  /// No description provided for @authResetSentTo.
  ///
  /// In fr, this message translates to:
  /// **'Entrez le code à 6 chiffres envoyé à\n'**
  String get authResetSentTo;

  /// No description provided for @authResetCodeLabel.
  ///
  /// In fr, this message translates to:
  /// **'Code de vérification'**
  String get authResetCodeLabel;

  /// No description provided for @authResetCodeRequired.
  ///
  /// In fr, this message translates to:
  /// **'Code à 6 chiffres requis'**
  String get authResetCodeRequired;

  /// No description provided for @authResetPasswordLabel.
  ///
  /// In fr, this message translates to:
  /// **'Nouveau mot de passe'**
  String get authResetPasswordLabel;

  /// No description provided for @authResetPasswordMin.
  ///
  /// In fr, this message translates to:
  /// **'6 caractères minimum'**
  String get authResetPasswordMin;

  /// No description provided for @authResetSubmit.
  ///
  /// In fr, this message translates to:
  /// **'Réinitialiser'**
  String get authResetSubmit;

  /// No description provided for @authResetSending.
  ///
  /// In fr, this message translates to:
  /// **'Envoi…'**
  String get authResetSending;

  /// No description provided for @authResetResend.
  ///
  /// In fr, this message translates to:
  /// **'Renvoyer le code'**
  String get authResetResend;

  /// No description provided for @authSplashTagline.
  ///
  /// In fr, this message translates to:
  /// **'Votre boutique, dans votre poche'**
  String get authSplashTagline;

  /// No description provided for @offListTitle.
  ///
  /// In fr, this message translates to:
  /// **'Mises en vente'**
  String get offListTitle;

  /// No description provided for @offListNewOffer.
  ///
  /// In fr, this message translates to:
  /// **'Nouvelle mise en vente'**
  String get offListNewOffer;

  /// No description provided for @offListEmpty.
  ///
  /// In fr, this message translates to:
  /// **'Aucune mise en vente.\nUne mise en vente, c\'est le prix auquel vous vendez un produit.'**
  String get offListEmpty;

  /// No description provided for @offCardActionsTooltip.
  ///
  /// In fr, this message translates to:
  /// **'Actions'**
  String get offCardActionsTooltip;

  /// No description provided for @offCardEditPrice.
  ///
  /// In fr, this message translates to:
  /// **'Modifier le prix'**
  String get offCardEditPrice;

  /// No description provided for @offCardRemoveDiscount.
  ///
  /// In fr, this message translates to:
  /// **'Retirer la remise'**
  String get offCardRemoveDiscount;

  /// No description provided for @offCardApplyDiscount.
  ///
  /// In fr, this message translates to:
  /// **'Appliquer une remise'**
  String get offCardApplyDiscount;

  /// No description provided for @offCardPause.
  ///
  /// In fr, this message translates to:
  /// **'Mettre en pause'**
  String get offCardPause;

  /// No description provided for @offCardReactivate.
  ///
  /// In fr, this message translates to:
  /// **'Réactiver'**
  String get offCardReactivate;

  /// No description provided for @offCardHandling.
  ///
  /// In fr, this message translates to:
  /// **'Préparation {days} j'**
  String offCardHandling(int days);

  /// No description provided for @offCardYouReceive.
  ///
  /// In fr, this message translates to:
  /// **'Vous percevez'**
  String get offCardYouReceive;

  /// No description provided for @offCardCustomerPays.
  ///
  /// In fr, this message translates to:
  /// **'Le client paie'**
  String get offCardCustomerPays;

  /// No description provided for @offCardPromo.
  ///
  /// In fr, this message translates to:
  /// **'Promo'**
  String get offCardPromo;

  /// No description provided for @offCardBefore.
  ///
  /// In fr, this message translates to:
  /// **'avant {price}'**
  String offCardBefore(String price);

  /// No description provided for @offCardSpreadNote.
  ///
  /// In fr, this message translates to:
  /// **'Écart : commission plateforme ({commission} %) + frais de paiement ({fee} %).'**
  String offCardSpreadNote(int commission, int fee);

  /// No description provided for @offSheetDiscountRemoved.
  ///
  /// In fr, this message translates to:
  /// **'Remise retirée.'**
  String get offSheetDiscountRemoved;

  /// No description provided for @offSheetBackOnSale.
  ///
  /// In fr, this message translates to:
  /// **'Remise en vente.'**
  String get offSheetBackOnSale;

  /// No description provided for @offSheetSalePaused.
  ///
  /// In fr, this message translates to:
  /// **'Vente mise en pause.'**
  String get offSheetSalePaused;

  /// No description provided for @offSheetYouReceive.
  ///
  /// In fr, this message translates to:
  /// **'Vous percevez'**
  String get offSheetYouReceive;

  /// No description provided for @offSheetCommission.
  ///
  /// In fr, this message translates to:
  /// **'Commission plateforme'**
  String get offSheetCommission;

  /// No description provided for @offSheetPaymentFees.
  ///
  /// In fr, this message translates to:
  /// **'Frais de paiement'**
  String get offSheetPaymentFees;

  /// No description provided for @offSheetDisplayedPrice.
  ///
  /// In fr, this message translates to:
  /// **'Prix affiché au client'**
  String get offSheetDisplayedPrice;

  /// No description provided for @offSheetChooseProduct.
  ///
  /// In fr, this message translates to:
  /// **'Choisissez un produit.'**
  String get offSheetChooseProduct;

  /// No description provided for @offSheetChooseVariant.
  ///
  /// In fr, this message translates to:
  /// **'Choisissez la déclinaison (SKU) concernée.'**
  String get offSheetChooseVariant;

  /// No description provided for @offSheetChooseStore.
  ///
  /// In fr, this message translates to:
  /// **'Choisissez une boutique'**
  String get offSheetChooseStore;

  /// No description provided for @offSheetStore.
  ///
  /// In fr, this message translates to:
  /// **'Boutique'**
  String get offSheetStore;

  /// No description provided for @offSheetChooseProductFirst.
  ///
  /// In fr, this message translates to:
  /// **'Choisissez d’abord un produit.'**
  String get offSheetChooseProductFirst;

  /// No description provided for @offSheetNoVariants.
  ///
  /// In fr, this message translates to:
  /// **'Ce produit n’a aucune déclinaison. Ajoutez-en une depuis sa fiche avant de le mettre en vente.'**
  String get offSheetNoVariants;

  /// No description provided for @offSheetNoStores.
  ///
  /// In fr, this message translates to:
  /// **'Aucune boutique sur ce compte. Créez-en une avant de mettre un article en vente.'**
  String get offSheetNoStores;

  /// No description provided for @offSheetChooseLocation.
  ///
  /// In fr, this message translates to:
  /// **'Choisissez un lieu d\'expédition.'**
  String get offSheetChooseLocation;

  /// No description provided for @offSheetOfferCreated.
  ///
  /// In fr, this message translates to:
  /// **'Mise en vente créée.'**
  String get offSheetOfferCreated;

  /// No description provided for @offSheetDuplicateTitle.
  ///
  /// In fr, this message translates to:
  /// **'Cette déclinaison a déjà une mise en vente'**
  String get offSheetDuplicateTitle;

  /// No description provided for @offSheetDuplicateBody.
  ///
  /// In fr, this message translates to:
  /// **'Chaque déclinaison ne porte qu\'une seule mise en vente : son stock est unique et ne peut pas être à la fois neuf et d\'occasion.\n\nPour vendre ce produit dans une autre condition, créez une seconde déclinaison (ex. « Occasion ») depuis la fiche produit, puis ajoutez-y son mise en vente.'**
  String get offSheetDuplicateBody;

  /// No description provided for @offSheetGotIt.
  ///
  /// In fr, this message translates to:
  /// **'Compris'**
  String get offSheetGotIt;

  /// No description provided for @offSheetNewOfferTitle.
  ///
  /// In fr, this message translates to:
  /// **'Nouvelle mise en vente'**
  String get offSheetNewOfferTitle;

  /// No description provided for @offSheetProductsUnavailable.
  ///
  /// In fr, this message translates to:
  /// **'Produits indisponibles : {error}'**
  String offSheetProductsUnavailable(String error);

  /// No description provided for @offSheetProduct.
  ///
  /// In fr, this message translates to:
  /// **'Produit'**
  String get offSheetProduct;

  /// No description provided for @offSheetVariantSku.
  ///
  /// In fr, this message translates to:
  /// **'Déclinaison (SKU)'**
  String get offSheetVariantSku;

  /// No description provided for @offSheetSkuInternalRef.
  ///
  /// In fr, this message translates to:
  /// **'SKU (référence interne)'**
  String get offSheetSkuInternalRef;

  /// No description provided for @offSheetSkuRequired.
  ///
  /// In fr, this message translates to:
  /// **'SKU obligatoire'**
  String get offSheetSkuRequired;

  /// No description provided for @offSheetCondition.
  ///
  /// In fr, this message translates to:
  /// **'État du produit'**
  String get offSheetCondition;

  /// No description provided for @offSheetConditionHint.
  ///
  /// In fr, this message translates to:
  /// **'Une déclinaison = une seule mise en vente. Pour vendre en Neuf ET Occasion, créez une déclinaison par condition.'**
  String get offSheetConditionHint;

  /// No description provided for @offSheetYourPrice.
  ///
  /// In fr, this message translates to:
  /// **'Votre prix (net perçu)'**
  String get offSheetYourPrice;

  /// No description provided for @offSheetInvalidPrice.
  ///
  /// In fr, this message translates to:
  /// **'Prix invalide'**
  String get offSheetInvalidPrice;

  /// No description provided for @offSheetLocationsUnavailable.
  ///
  /// In fr, this message translates to:
  /// **'Lieux d\'\'expédition indisponibles : {error}'**
  String offSheetLocationsUnavailable(String error);

  /// No description provided for @offSheetNoLocations.
  ///
  /// In fr, this message translates to:
  /// **'Aucun lieu d\'expédition. Ajoutez-en un avant de mettre en vente.'**
  String get offSheetNoLocations;

  /// No description provided for @offSheetShipFrom.
  ///
  /// In fr, this message translates to:
  /// **'Lieu d\'expédition'**
  String get offSheetShipFrom;

  /// No description provided for @offSheetHandlingTime.
  ///
  /// In fr, this message translates to:
  /// **'Délai de préparation'**
  String get offSheetHandlingTime;

  /// No description provided for @offSheetDay1.
  ///
  /// In fr, this message translates to:
  /// **'1 jour'**
  String get offSheetDay1;

  /// No description provided for @offSheetDays2.
  ///
  /// In fr, this message translates to:
  /// **'2 jours'**
  String get offSheetDays2;

  /// No description provided for @offSheetDays3.
  ///
  /// In fr, this message translates to:
  /// **'3 jours'**
  String get offSheetDays3;

  /// No description provided for @offSheetDays5.
  ///
  /// In fr, this message translates to:
  /// **'5 jours'**
  String get offSheetDays5;

  /// No description provided for @offSheetPublish.
  ///
  /// In fr, this message translates to:
  /// **'Mettre en vente'**
  String get offSheetPublish;

  /// No description provided for @offSheetInvalidPriceNotice.
  ///
  /// In fr, this message translates to:
  /// **'Prix invalide.'**
  String get offSheetInvalidPriceNotice;

  /// No description provided for @offSheetPriceUpdated.
  ///
  /// In fr, this message translates to:
  /// **'Prix mis à jour.'**
  String get offSheetPriceUpdated;

  /// No description provided for @offSheetSave.
  ///
  /// In fr, this message translates to:
  /// **'Enregistrer'**
  String get offSheetSave;

  /// No description provided for @offSheetInvalidDiscount.
  ///
  /// In fr, this message translates to:
  /// **'Remise invalide : le prix réduit doit rester positif et inférieur au prix actuel.'**
  String get offSheetInvalidDiscount;

  /// No description provided for @offSheetDiscountApplied.
  ///
  /// In fr, this message translates to:
  /// **'Remise appliquée.'**
  String get offSheetDiscountApplied;

  /// No description provided for @offSheetApplyDiscountTitle.
  ///
  /// In fr, this message translates to:
  /// **'Appliquer une remise'**
  String get offSheetApplyDiscountTitle;

  /// No description provided for @offSheetCurrentPriceNote.
  ///
  /// In fr, this message translates to:
  /// **'Prix actuel : {price}. La remise réduit votre prix net ; la commission est recalculée sur le prix réduit.'**
  String offSheetCurrentPriceNote(String price);

  /// No description provided for @offSheetDiscountType.
  ///
  /// In fr, this message translates to:
  /// **'Type de remise'**
  String get offSheetDiscountType;

  /// No description provided for @offSheetPercentage.
  ///
  /// In fr, this message translates to:
  /// **'Pourcentage (%)'**
  String get offSheetPercentage;

  /// No description provided for @offSheetFixedAmount.
  ///
  /// In fr, this message translates to:
  /// **'Montant fixe'**
  String get offSheetFixedAmount;

  /// No description provided for @offSheetPercentageLabel.
  ///
  /// In fr, this message translates to:
  /// **'Pourcentage'**
  String get offSheetPercentageLabel;

  /// No description provided for @offSheetAmountLabel.
  ///
  /// In fr, this message translates to:
  /// **'Montant'**
  String get offSheetAmountLabel;

  /// No description provided for @offSheetPromoEndOptional.
  ///
  /// In fr, this message translates to:
  /// **'Fin de la promo (optionnel)'**
  String get offSheetPromoEndOptional;

  /// No description provided for @offSheetUntil.
  ///
  /// In fr, this message translates to:
  /// **'Jusqu\'\'au {date}'**
  String offSheetUntil(String date);

  /// No description provided for @offSheetNoEndDate.
  ///
  /// In fr, this message translates to:
  /// **'Sans échéance'**
  String get offSheetNoEndDate;

  /// No description provided for @offSheetApplyDiscountBtn.
  ///
  /// In fr, this message translates to:
  /// **'Appliquer la remise'**
  String get offSheetApplyDiscountBtn;

  /// No description provided for @offLocationsTitle.
  ///
  /// In fr, this message translates to:
  /// **'Lieux d\'expédition'**
  String get offLocationsTitle;

  /// No description provided for @offLocationsAdd.
  ///
  /// In fr, this message translates to:
  /// **'Ajouter'**
  String get offLocationsAdd;

  /// No description provided for @offLocationsEmpty.
  ///
  /// In fr, this message translates to:
  /// **'Aucun lieu d\'expédition.\nAjoutez l\'adresse d\'où partent vos colis : elle est indispensable pour mettre en vente.'**
  String get offLocationsEmpty;

  /// No description provided for @offLocationsDeleteTitle.
  ///
  /// In fr, this message translates to:
  /// **'Supprimer ce lieu ?'**
  String get offLocationsDeleteTitle;

  /// No description provided for @offLocationsDeleteBody.
  ///
  /// In fr, this message translates to:
  /// **'« {label} » ne sera plus proposé comme point de départ. Les mises en vente qui l\'\'utilisent devront pointer vers un autre lieu.'**
  String offLocationsDeleteBody(String label);

  /// No description provided for @offLocationsCancel.
  ///
  /// In fr, this message translates to:
  /// **'Annuler'**
  String get offLocationsCancel;

  /// No description provided for @offLocationsDelete.
  ///
  /// In fr, this message translates to:
  /// **'Supprimer'**
  String get offLocationsDelete;

  /// No description provided for @offLocationsDeleted.
  ///
  /// In fr, this message translates to:
  /// **'Lieu supprimé.'**
  String get offLocationsDeleted;

  /// No description provided for @offLocationsDeleteTooltip.
  ///
  /// In fr, this message translates to:
  /// **'Supprimer'**
  String get offLocationsDeleteTooltip;

  /// No description provided for @offLocationsAdded.
  ///
  /// In fr, this message translates to:
  /// **'Lieu ajouté.'**
  String get offLocationsAdded;

  /// No description provided for @offLocationsNewTitle.
  ///
  /// In fr, this message translates to:
  /// **'Nouveau lieu d\'expédition'**
  String get offLocationsNewTitle;

  /// No description provided for @offLocationsNewSubtitle.
  ///
  /// In fr, this message translates to:
  /// **'L\'adresse d\'où partent vos colis. Elle sert de point de départ à vos mises en vente.'**
  String get offLocationsNewSubtitle;

  /// No description provided for @offLocationsAddressLabel.
  ///
  /// In fr, this message translates to:
  /// **'Adresse'**
  String get offLocationsAddressLabel;

  /// No description provided for @offLocationsAddressHint.
  ///
  /// In fr, this message translates to:
  /// **'Rue, numéro, quartier'**
  String get offLocationsAddressHint;

  /// No description provided for @offLocationsAddressRequired.
  ///
  /// In fr, this message translates to:
  /// **'Adresse obligatoire'**
  String get offLocationsAddressRequired;

  /// No description provided for @offLocationsAddBtn.
  ///
  /// In fr, this message translates to:
  /// **'Ajouter le lieu'**
  String get offLocationsAddBtn;

  /// No description provided for @msgActionUnavailable.
  ///
  /// In fr, this message translates to:
  /// **'Cette action n\'est pas encore disponible sur le serveur.'**
  String get msgActionUnavailable;

  /// No description provided for @msgEmpty.
  ///
  /// In fr, this message translates to:
  /// **'Aucun message. Écrivez le premier.'**
  String get msgEmpty;

  /// No description provided for @msgDeleted.
  ///
  /// In fr, this message translates to:
  /// **'Message supprimé'**
  String get msgDeleted;

  /// No description provided for @msgHideForMe.
  ///
  /// In fr, this message translates to:
  /// **'Supprimer pour moi'**
  String get msgHideForMe;

  /// No description provided for @msgHideForMeHint.
  ///
  /// In fr, this message translates to:
  /// **'Le client continuera de le voir'**
  String get msgHideForMeHint;

  /// No description provided for @msgDeleteForEveryone.
  ///
  /// In fr, this message translates to:
  /// **'Supprimer pour tout le monde'**
  String get msgDeleteForEveryone;

  /// No description provided for @msgDeleteForEveryoneHint.
  ///
  /// In fr, this message translates to:
  /// **'Le message sera remplacé par « Message supprimé »'**
  String get msgDeleteForEveryoneHint;

  /// No description provided for @msgAttachImage.
  ///
  /// In fr, this message translates to:
  /// **'Joindre une image'**
  String get msgAttachImage;

  /// No description provided for @msgInputHint.
  ///
  /// In fr, this message translates to:
  /// **'Votre message…'**
  String get msgInputHint;

  /// No description provided for @msgNoConversations.
  ///
  /// In fr, this message translates to:
  /// **'Aucune conversation.\nVos clients vous écriront ici.'**
  String get msgNoConversations;

  /// No description provided for @msgNewThread.
  ///
  /// In fr, this message translates to:
  /// **'Nouveau fil'**
  String get msgNewThread;

  /// No description provided for @revTitle.
  ///
  /// In fr, this message translates to:
  /// **'Avis'**
  String get revTitle;

  /// No description provided for @revEmpty.
  ///
  /// In fr, this message translates to:
  /// **'Aucun avis pour le moment.'**
  String get revEmpty;

  /// No description provided for @revYourReply.
  ///
  /// In fr, this message translates to:
  /// **'Votre réponse'**
  String get revYourReply;

  /// No description provided for @revReply.
  ///
  /// In fr, this message translates to:
  /// **'Répondre'**
  String get revReply;

  /// No description provided for @revFlagTooltip.
  ///
  /// In fr, this message translates to:
  /// **'Signaler cet avis'**
  String get revFlagTooltip;

  /// No description provided for @revReplyTitle.
  ///
  /// In fr, this message translates to:
  /// **'Répondre à cet avis'**
  String get revReplyTitle;

  /// No description provided for @revReplyPublicNote.
  ///
  /// In fr, this message translates to:
  /// **'Votre réponse est publique : elle sera lue par tous vos futurs clients.'**
  String get revReplyPublicNote;

  /// No description provided for @revReplyHint.
  ///
  /// In fr, this message translates to:
  /// **'Votre réponse…'**
  String get revReplyHint;

  /// No description provided for @revReplyPublished.
  ///
  /// In fr, this message translates to:
  /// **'Réponse publiée.'**
  String get revReplyPublished;

  /// No description provided for @revPublish.
  ///
  /// In fr, this message translates to:
  /// **'Publier'**
  String get revPublish;

  /// No description provided for @revFlagConfirmTitle.
  ///
  /// In fr, this message translates to:
  /// **'Signaler cet avis ?'**
  String get revFlagConfirmTitle;

  /// No description provided for @revFlagConfirmBody.
  ///
  /// In fr, this message translates to:
  /// **'L\'avis sera examiné par la modération. Il reste visible tant qu\'aucune décision n\'est prise.'**
  String get revFlagConfirmBody;

  /// No description provided for @revCancel.
  ///
  /// In fr, this message translates to:
  /// **'Annuler'**
  String get revCancel;

  /// No description provided for @revFlag.
  ///
  /// In fr, this message translates to:
  /// **'Signaler'**
  String get revFlag;

  /// No description provided for @revFlaggedSuccess.
  ///
  /// In fr, this message translates to:
  /// **'Avis signalé à la modération.'**
  String get revFlaggedSuccess;

  /// No description provided for @dispTitle.
  ///
  /// In fr, this message translates to:
  /// **'Litige'**
  String get dispTitle;

  /// No description provided for @dispEmpty.
  ///
  /// In fr, this message translates to:
  /// **'Aucun échange pour le moment.'**
  String get dispEmpty;

  /// No description provided for @dispReplySent.
  ///
  /// In fr, this message translates to:
  /// **'Réponse envoyée.'**
  String get dispReplySent;

  /// No description provided for @dispStatusResolved.
  ///
  /// In fr, this message translates to:
  /// **'Résolu'**
  String get dispStatusResolved;

  /// No description provided for @dispStatusEscalated.
  ///
  /// In fr, this message translates to:
  /// **'Escaladé à la plateforme'**
  String get dispStatusEscalated;

  /// No description provided for @dispStatusUnderReview.
  ///
  /// In fr, this message translates to:
  /// **'En cours d’examen'**
  String get dispStatusUnderReview;

  /// No description provided for @dispStatusOpen.
  ///
  /// In fr, this message translates to:
  /// **'Ouvert'**
  String get dispStatusOpen;

  /// No description provided for @dispOpenedAge.
  ///
  /// In fr, this message translates to:
  /// **'Ouvert {age}'**
  String dispOpenedAge(String age);

  /// No description provided for @dispEscalatedWarning.
  ///
  /// In fr, this message translates to:
  /// **'La plateforme va arbitrer. Apportez vos éléments maintenant : sans réponse de votre part, la décision se prendra sur les seuls arguments de l\'acheteur.'**
  String get dispEscalatedWarning;

  /// No description provided for @dispClosedTitle.
  ///
  /// In fr, this message translates to:
  /// **'Litige clos'**
  String get dispClosedTitle;

  /// No description provided for @dispRefundedToBuyer.
  ///
  /// In fr, this message translates to:
  /// **'Remboursé à l\'acheteur'**
  String get dispRefundedToBuyer;

  /// No description provided for @dispComposerHint.
  ///
  /// In fr, this message translates to:
  /// **'Votre réponse : faits, dates, preuves…'**
  String get dispComposerHint;

  /// No description provided for @finTitle.
  ///
  /// In fr, this message translates to:
  /// **'Finances'**
  String get finTitle;

  /// No description provided for @finRange12Months.
  ///
  /// In fr, this message translates to:
  /// **'12 mois'**
  String get finRange12Months;

  /// No description provided for @finRangeDays.
  ///
  /// In fr, this message translates to:
  /// **'{days} jours'**
  String finRangeDays(int days);

  /// No description provided for @finGrossSales.
  ///
  /// In fr, this message translates to:
  /// **'Ventes brutes'**
  String get finGrossSales;

  /// No description provided for @finPlatformCommission.
  ///
  /// In fr, this message translates to:
  /// **'Commission plateforme'**
  String get finPlatformCommission;

  /// No description provided for @finPaymentFees.
  ///
  /// In fr, this message translates to:
  /// **'Frais de paiement'**
  String get finPaymentFees;

  /// No description provided for @finRefunds.
  ///
  /// In fr, this message translates to:
  /// **'Remboursements'**
  String get finRefunds;

  /// No description provided for @finNetForYou.
  ///
  /// In fr, this message translates to:
  /// **'Net pour vous'**
  String get finNetForYou;

  /// No description provided for @finDetail.
  ///
  /// In fr, this message translates to:
  /// **'Détail'**
  String get finDetail;

  /// No description provided for @finNoMovements.
  ///
  /// In fr, this message translates to:
  /// **'Aucun mouvement sur la période.'**
  String get finNoMovements;

  /// No description provided for @finPayouts.
  ///
  /// In fr, this message translates to:
  /// **'Versements'**
  String get finPayouts;

  /// No description provided for @finPayoutProviderDate.
  ///
  /// In fr, this message translates to:
  /// **'{provider} · {date}'**
  String finPayoutProviderDate(String provider, String date);

  /// No description provided for @walTitle.
  ///
  /// In fr, this message translates to:
  /// **'Portefeuille'**
  String get walTitle;

  /// No description provided for @walWithdrawalHistory.
  ///
  /// In fr, this message translates to:
  /// **'Historique des retraits'**
  String get walWithdrawalHistory;

  /// No description provided for @walNoWithdrawals.
  ///
  /// In fr, this message translates to:
  /// **'Aucun retrait pour le moment.'**
  String get walNoWithdrawals;

  /// No description provided for @walMovements.
  ///
  /// In fr, this message translates to:
  /// **'Mouvements'**
  String get walMovements;

  /// No description provided for @walAvailableBalance.
  ///
  /// In fr, this message translates to:
  /// **'Solde disponible'**
  String get walAvailableBalance;

  /// No description provided for @walPendingDelivery.
  ///
  /// In fr, this message translates to:
  /// **'En attente de livraison · {amount}'**
  String walPendingDelivery(String amount);

  /// No description provided for @walPendingWithdrawals.
  ///
  /// In fr, this message translates to:
  /// **'Retraits en cours'**
  String get walPendingWithdrawals;

  /// No description provided for @walInvalidAmount.
  ///
  /// In fr, this message translates to:
  /// **'Montant invalide.'**
  String get walInvalidAmount;

  /// No description provided for @walRequestSaved.
  ///
  /// In fr, this message translates to:
  /// **'Demande enregistrée. Elle sera versée après validation.'**
  String get walRequestSaved;

  /// No description provided for @walRequestWithdrawal.
  ///
  /// In fr, this message translates to:
  /// **'Demander un retrait'**
  String get walRequestWithdrawal;

  /// No description provided for @walNoMobileMoneyAccount.
  ///
  /// In fr, this message translates to:
  /// **'Aucun compte Mobile Money enregistré. Renseignez-le dans votre boutique : sans lui, aucun versement n\'est possible.'**
  String get walNoMobileMoneyAccount;

  /// No description provided for @walAmount.
  ///
  /// In fr, this message translates to:
  /// **'Montant'**
  String get walAmount;

  /// No description provided for @walAvailableAmount.
  ///
  /// In fr, this message translates to:
  /// **'Disponible : {amount}'**
  String walAvailableAmount(String amount);

  /// No description provided for @walRequestWithdrawalButton.
  ///
  /// In fr, this message translates to:
  /// **'Demander le retrait'**
  String get walRequestWithdrawalButton;

  /// No description provided for @walFundsHeldNotice.
  ///
  /// In fr, this message translates to:
  /// **'Les fonds sont retenus dès la demande, puis versés après validation par l’administrateur.'**
  String get walFundsHeldNotice;

  /// No description provided for @walWithdrawalProcessing.
  ///
  /// In fr, this message translates to:
  /// **'Versement transmis à votre opérateur. Il est confirmé dès que l\'argent arrive — cela peut prendre quelques minutes.'**
  String get walWithdrawalProcessing;

  /// No description provided for @walFundsRecredited.
  ///
  /// In fr, this message translates to:
  /// **'Les fonds ont été recrédités sur votre solde.'**
  String get walFundsRecredited;

  /// No description provided for @shpTitle.
  ///
  /// In fr, this message translates to:
  /// **'Expéditions'**
  String get shpTitle;

  /// No description provided for @shpEmptyTodo.
  ///
  /// In fr, this message translates to:
  /// **'Rien à expédier.\nVos colis en attente apparaîtront ici.'**
  String get shpEmptyTodo;

  /// No description provided for @shpEmptyOther.
  ///
  /// In fr, this message translates to:
  /// **'Aucune expédition dans cette vue.'**
  String get shpEmptyOther;

  /// No description provided for @shpFilterTodo.
  ///
  /// In fr, this message translates to:
  /// **'À traiter'**
  String get shpFilterTodo;

  /// No description provided for @shpFilterShipped.
  ///
  /// In fr, this message translates to:
  /// **'Expédiées'**
  String get shpFilterShipped;

  /// No description provided for @shpFilterDelivered.
  ///
  /// In fr, this message translates to:
  /// **'Livrées'**
  String get shpFilterDelivered;

  /// No description provided for @shpFilterCancelled.
  ///
  /// In fr, this message translates to:
  /// **'Annulées'**
  String get shpFilterCancelled;

  /// No description provided for @shpFilterAll.
  ///
  /// In fr, this message translates to:
  /// **'Toutes'**
  String get shpFilterAll;

  /// No description provided for @shpFilterWithCount.
  ///
  /// In fr, this message translates to:
  /// **'{label} ({count})'**
  String shpFilterWithCount(String label, int count);

  /// No description provided for @shpCustomerItems.
  ///
  /// In fr, this message translates to:
  /// **'{customer} · {count} article(s)'**
  String shpCustomerItems(String customer, int count);

  /// No description provided for @shpTracking.
  ///
  /// In fr, this message translates to:
  /// **'Suivi'**
  String get shpTracking;

  /// No description provided for @shpMarkPrepared.
  ///
  /// In fr, this message translates to:
  /// **'Marquer préparée'**
  String get shpMarkPrepared;

  /// No description provided for @shpMarkedPrepared.
  ///
  /// In fr, this message translates to:
  /// **'Colis marqué comme préparé.'**
  String get shpMarkedPrepared;

  /// No description provided for @shpShip.
  ///
  /// In fr, this message translates to:
  /// **'Expédier'**
  String get shpShip;

  /// No description provided for @shpConfirmDelivery.
  ///
  /// In fr, this message translates to:
  /// **'Confirmer la livraison'**
  String get shpConfirmDelivery;

  /// No description provided for @shpDeliveryConfirmed.
  ///
  /// In fr, this message translates to:
  /// **'Livraison confirmée.'**
  String get shpDeliveryConfirmed;

  /// No description provided for @shpCancel.
  ///
  /// In fr, this message translates to:
  /// **'Annuler'**
  String get shpCancel;

  /// No description provided for @shpCancelDialogTitle.
  ///
  /// In fr, this message translates to:
  /// **'Annuler cette expédition ?'**
  String get shpCancelDialogTitle;

  /// No description provided for @shpCancelDialogBody.
  ///
  /// In fr, this message translates to:
  /// **'La commande {orderReference} sera signalée comme annulée au client. Cette action ne peut pas être défaite.'**
  String shpCancelDialogBody(String orderReference);

  /// No description provided for @shpBack.
  ///
  /// In fr, this message translates to:
  /// **'Retour'**
  String get shpBack;

  /// No description provided for @shpCancelShipment.
  ///
  /// In fr, this message translates to:
  /// **'Annuler l’expédition'**
  String get shpCancelShipment;

  /// No description provided for @shpShipmentCancelled.
  ///
  /// In fr, this message translates to:
  /// **'Expédition annulée.'**
  String get shpShipmentCancelled;

  /// No description provided for @shpShipmentSaved.
  ///
  /// In fr, this message translates to:
  /// **'Expédition enregistrée.'**
  String get shpShipmentSaved;

  /// No description provided for @shpShipSheetTitle.
  ///
  /// In fr, this message translates to:
  /// **'Expédier {orderReference}'**
  String shpShipSheetTitle(String orderReference);

  /// No description provided for @shpTrackingHint.
  ///
  /// In fr, this message translates to:
  /// **'Le numéro de suivi est communiqué au client : sans lui, la moindre contestation devient invérifiable.'**
  String get shpTrackingHint;

  /// No description provided for @shpCarrier.
  ///
  /// In fr, this message translates to:
  /// **'Transporteur'**
  String get shpCarrier;

  /// No description provided for @shpCarrierRequired.
  ///
  /// In fr, this message translates to:
  /// **'Transporteur obligatoire'**
  String get shpCarrierRequired;

  /// No description provided for @shpTrackingNumber.
  ///
  /// In fr, this message translates to:
  /// **'Numéro de suivi'**
  String get shpTrackingNumber;

  /// No description provided for @shpTrackingRequired.
  ///
  /// In fr, this message translates to:
  /// **'Numéro obligatoire'**
  String get shpTrackingRequired;

  /// No description provided for @shpConfirmShipment.
  ///
  /// In fr, this message translates to:
  /// **'Confirmer l’expédition'**
  String get shpConfirmShipment;

  /// No description provided for @retTitle.
  ///
  /// In fr, this message translates to:
  /// **'Retours'**
  String get retTitle;

  /// No description provided for @retEmptyTodo.
  ///
  /// In fr, this message translates to:
  /// **'Aucun retour à traiter.'**
  String get retEmptyTodo;

  /// No description provided for @retEmptyOther.
  ///
  /// In fr, this message translates to:
  /// **'Aucun retour dans cette vue.'**
  String get retEmptyOther;

  /// No description provided for @retFilterTodo.
  ///
  /// In fr, this message translates to:
  /// **'À traiter'**
  String get retFilterTodo;

  /// No description provided for @retFilterRefunded.
  ///
  /// In fr, this message translates to:
  /// **'Remboursés'**
  String get retFilterRefunded;

  /// No description provided for @retFilterRejected.
  ///
  /// In fr, this message translates to:
  /// **'Refusés'**
  String get retFilterRejected;

  /// No description provided for @retFilterAll.
  ///
  /// In fr, this message translates to:
  /// **'Tous'**
  String get retFilterAll;

  /// No description provided for @retFilterWithCount.
  ///
  /// In fr, this message translates to:
  /// **'{label} ({count})'**
  String retFilterWithCount(String label, int count);

  /// No description provided for @retStatusRefunded.
  ///
  /// In fr, this message translates to:
  /// **'Remboursé'**
  String get retStatusRefunded;

  /// No description provided for @retStatusRefundPending.
  ///
  /// In fr, this message translates to:
  /// **'Versement en cours'**
  String get retStatusRefundPending;

  /// No description provided for @retStatusRejected.
  ///
  /// In fr, this message translates to:
  /// **'Refusé'**
  String get retStatusRejected;

  /// No description provided for @retStatusReceived.
  ///
  /// In fr, this message translates to:
  /// **'Colis reçu'**
  String get retStatusReceived;

  /// No description provided for @retStatusApproved.
  ///
  /// In fr, this message translates to:
  /// **'Accepté'**
  String get retStatusApproved;

  /// No description provided for @retStatusToReview.
  ///
  /// In fr, this message translates to:
  /// **'À examiner'**
  String get retStatusToReview;

  /// No description provided for @retTracking.
  ///
  /// In fr, this message translates to:
  /// **'Suivi'**
  String get retTracking;

  /// No description provided for @retLabelRefunded.
  ///
  /// In fr, this message translates to:
  /// **'Remboursé'**
  String get retLabelRefunded;

  /// No description provided for @retLabelToPay.
  ///
  /// In fr, this message translates to:
  /// **'À verser'**
  String get retLabelToPay;

  /// No description provided for @retRefundValidatedNote.
  ///
  /// In fr, this message translates to:
  /// **'Remboursement validé. Le versement à l\'acheteur est effectué par HBA Express sous 24 à 72 heures. Aucune action de votre part.'**
  String get retRefundValidatedNote;

  /// No description provided for @retAccept.
  ///
  /// In fr, this message translates to:
  /// **'Accepter'**
  String get retAccept;

  /// No description provided for @retReturnAccepted.
  ///
  /// In fr, this message translates to:
  /// **'Retour accepté. Attendez le colis.'**
  String get retReturnAccepted;

  /// No description provided for @retReject.
  ///
  /// In fr, this message translates to:
  /// **'Refuser'**
  String get retReject;

  /// No description provided for @retNoTrackingWarning.
  ///
  /// In fr, this message translates to:
  /// **'Aucun numéro de suivi : vous ne pourrez pas prouver le retour du colis.'**
  String get retNoTrackingWarning;

  /// No description provided for @retEditTracking.
  ///
  /// In fr, this message translates to:
  /// **'Modifier le suivi'**
  String get retEditTracking;

  /// No description provided for @retEnterTracking.
  ///
  /// In fr, this message translates to:
  /// **'Saisir le suivi'**
  String get retEnterTracking;

  /// No description provided for @retParcelReceived.
  ///
  /// In fr, this message translates to:
  /// **'Colis reçu'**
  String get retParcelReceived;

  /// No description provided for @retValidateRefund.
  ///
  /// In fr, this message translates to:
  /// **'Valider le remboursement'**
  String get retValidateRefund;

  /// No description provided for @retConfirmReceiptTitle.
  ///
  /// In fr, this message translates to:
  /// **'Confirmer la réception ?'**
  String get retConfirmReceiptTitle;

  /// No description provided for @retConfirmReceiptBody.
  ///
  /// In fr, this message translates to:
  /// **'Déclarez le colis reçu seulement après avoir vérifié son contenu. Vous pourrez ensuite rembourser — en totalité ou en partie si l\'article est abîmé.'**
  String get retConfirmReceiptBody;

  /// No description provided for @retCancel.
  ///
  /// In fr, this message translates to:
  /// **'Annuler'**
  String get retCancel;

  /// No description provided for @retReceiptSaved.
  ///
  /// In fr, this message translates to:
  /// **'Réception enregistrée.'**
  String get retReceiptSaved;

  /// No description provided for @retExplainRejection.
  ///
  /// In fr, this message translates to:
  /// **'Expliquez votre refus (10 caractères minimum).'**
  String get retExplainRejection;

  /// No description provided for @retReturnRejected.
  ///
  /// In fr, this message translates to:
  /// **'Retour refusé.'**
  String get retReturnRejected;

  /// No description provided for @retRejectReturn.
  ///
  /// In fr, this message translates to:
  /// **'Refuser le retour'**
  String get retRejectReturn;

  /// No description provided for @retRejectReasonHint.
  ///
  /// In fr, this message translates to:
  /// **'Votre motif sera communiqué à l\'acheteur. Un refus clairement expliqué évite le litige.'**
  String get retRejectReasonHint;

  /// No description provided for @retRejectReasonExample.
  ///
  /// In fr, this message translates to:
  /// **'Ex. : l\'article a été utilisé et ne peut pas être revendu.'**
  String get retRejectReasonExample;

  /// No description provided for @retCarrierTrackingRequired.
  ///
  /// In fr, this message translates to:
  /// **'Transporteur et numéro sont requis.'**
  String get retCarrierTrackingRequired;

  /// No description provided for @retTrackingSaved.
  ///
  /// In fr, this message translates to:
  /// **'Suivi enregistré.'**
  String get retTrackingSaved;

  /// No description provided for @retTrackingSheetTitle.
  ///
  /// In fr, this message translates to:
  /// **'Suivi du colis retour'**
  String get retTrackingSheetTitle;

  /// No description provided for @retCarrier.
  ///
  /// In fr, this message translates to:
  /// **'Transporteur'**
  String get retCarrier;

  /// No description provided for @retTrackingNumber.
  ///
  /// In fr, this message translates to:
  /// **'Numéro de suivi'**
  String get retTrackingNumber;

  /// No description provided for @retSave.
  ///
  /// In fr, this message translates to:
  /// **'Enregistrer'**
  String get retSave;

  /// No description provided for @retInvalidAmount.
  ///
  /// In fr, this message translates to:
  /// **'Montant invalide.'**
  String get retInvalidAmount;

  /// No description provided for @retValidateRefundTitle.
  ///
  /// In fr, this message translates to:
  /// **'Valider ce remboursement ?'**
  String get retValidateRefundTitle;

  /// No description provided for @retValidateRefundBody.
  ///
  /// In fr, this message translates to:
  /// **'{amount} seront remboursés à l\'\'acheteur par HBA Express, et déduits de votre solde. Cette décision est définitive.'**
  String retValidateRefundBody(String amount);

  /// No description provided for @retValidate.
  ///
  /// In fr, this message translates to:
  /// **'Valider'**
  String get retValidate;

  /// No description provided for @retRefundValidatedNotify.
  ///
  /// In fr, this message translates to:
  /// **'Remboursement validé. HBA Express verse la somme à l\'acheteur sous 24 à 72 h.'**
  String get retRefundValidatedNotify;

  /// No description provided for @retPartialRefundHint.
  ///
  /// In fr, this message translates to:
  /// **'Vous pouvez rembourser partiellement si l\'article revient abîmé ou incomplet.'**
  String get retPartialRefundHint;

  /// No description provided for @retRefundAmount.
  ///
  /// In fr, this message translates to:
  /// **'Montant à rembourser'**
  String get retRefundAmount;

  /// No description provided for @profTitle.
  ///
  /// In fr, this message translates to:
  /// **'Profil et sécurité'**
  String get profTitle;

  /// No description provided for @profName.
  ///
  /// In fr, this message translates to:
  /// **'Nom'**
  String get profName;

  /// No description provided for @profEmail.
  ///
  /// In fr, this message translates to:
  /// **'E-mail'**
  String get profEmail;

  /// No description provided for @profPhone.
  ///
  /// In fr, this message translates to:
  /// **'Téléphone'**
  String get profPhone;

  /// No description provided for @profShop.
  ///
  /// In fr, this message translates to:
  /// **'Boutique'**
  String get profShop;

  /// No description provided for @profEditInfo.
  ///
  /// In fr, this message translates to:
  /// **'Modifier mes informations'**
  String get profEditInfo;

  /// No description provided for @profChangePassword.
  ///
  /// In fr, this message translates to:
  /// **'Changer mon mot de passe'**
  String get profChangePassword;

  /// No description provided for @profMfa.
  ///
  /// In fr, this message translates to:
  /// **'Double authentification'**
  String get profMfa;

  /// No description provided for @profEnabled.
  ///
  /// In fr, this message translates to:
  /// **'Activée'**
  String get profEnabled;

  /// No description provided for @profDisabled.
  ///
  /// In fr, this message translates to:
  /// **'Désactivée'**
  String get profDisabled;

  /// No description provided for @profMyInfo.
  ///
  /// In fr, this message translates to:
  /// **'Mes informations'**
  String get profMyInfo;

  /// No description provided for @profFirstName.
  ///
  /// In fr, this message translates to:
  /// **'Prénom'**
  String get profFirstName;

  /// No description provided for @profUpdated.
  ///
  /// In fr, this message translates to:
  /// **'Profil mis à jour.'**
  String get profUpdated;

  /// No description provided for @profSave.
  ///
  /// In fr, this message translates to:
  /// **'Enregistrer'**
  String get profSave;

  /// No description provided for @profCurrentPassword.
  ///
  /// In fr, this message translates to:
  /// **'Mot de passe actuel'**
  String get profCurrentPassword;

  /// No description provided for @profNewPassword.
  ///
  /// In fr, this message translates to:
  /// **'Nouveau mot de passe'**
  String get profNewPassword;

  /// No description provided for @profPasswordMinLength.
  ///
  /// In fr, this message translates to:
  /// **'8 caractères minimum.'**
  String get profPasswordMinLength;

  /// No description provided for @profPasswordChanged.
  ///
  /// In fr, this message translates to:
  /// **'Mot de passe modifié.'**
  String get profPasswordChanged;

  /// No description provided for @profModify.
  ///
  /// In fr, this message translates to:
  /// **'Modifier'**
  String get profModify;

  /// No description provided for @profEnableMfa.
  ///
  /// In fr, this message translates to:
  /// **'Activer la double authentification'**
  String get profEnableMfa;

  /// No description provided for @profMfaStep1.
  ///
  /// In fr, this message translates to:
  /// **'1. Ouvrez une application d\'authentification (Google Authenticator, Authy…).\n2. Ajoutez un compte en saisissant cette clé :'**
  String get profMfaStep1;

  /// No description provided for @profKeyCopied.
  ///
  /// In fr, this message translates to:
  /// **'Clé copiée.'**
  String get profKeyCopied;

  /// No description provided for @profMfaStep3.
  ///
  /// In fr, this message translates to:
  /// **'3. Entrez le code à 6 chiffres affiché par l’application :'**
  String get profMfaStep3;

  /// No description provided for @profCodeRequired.
  ///
  /// In fr, this message translates to:
  /// **'Code à 6 chiffres requis.'**
  String get profCodeRequired;

  /// No description provided for @profMfaEnabled.
  ///
  /// In fr, this message translates to:
  /// **'Double authentification activée.'**
  String get profMfaEnabled;

  /// No description provided for @profEnable.
  ///
  /// In fr, this message translates to:
  /// **'Activer'**
  String get profEnable;

  /// No description provided for @profDisableMfa.
  ///
  /// In fr, this message translates to:
  /// **'Désactiver la double authentification'**
  String get profDisableMfa;

  /// No description provided for @profDisableMfaBody.
  ///
  /// In fr, this message translates to:
  /// **'Entrez un code de votre application d’authentification pour confirmer.'**
  String get profDisableMfaBody;

  /// No description provided for @profMfaDisabled.
  ///
  /// In fr, this message translates to:
  /// **'Double authentification désactivée.'**
  String get profMfaDisabled;

  /// No description provided for @profDisable.
  ///
  /// In fr, this message translates to:
  /// **'Désactiver'**
  String get profDisable;

  /// No description provided for @notifTitle.
  ///
  /// In fr, this message translates to:
  /// **'Notifications'**
  String get notifTitle;

  /// No description provided for @notifPreferences.
  ///
  /// In fr, this message translates to:
  /// **'Préférences'**
  String get notifPreferences;

  /// No description provided for @notifMarkAllRead.
  ///
  /// In fr, this message translates to:
  /// **'Tout marquer lu'**
  String get notifMarkAllRead;

  /// No description provided for @notifEmpty.
  ///
  /// In fr, this message translates to:
  /// **'Aucune notification.'**
  String get notifEmpty;

  /// No description provided for @notifprefTitle.
  ///
  /// In fr, this message translates to:
  /// **'Préférences de notification'**
  String get notifprefTitle;

  /// No description provided for @notifprefIntro.
  ///
  /// In fr, this message translates to:
  /// **'Choisissez les notifications push que vous souhaitez recevoir. Les notifications restent consultables dans votre boîte de réception, même coupées.'**
  String get notifprefIntro;

  /// No description provided for @notifprefOrdersTitle.
  ///
  /// In fr, this message translates to:
  /// **'Commandes & expéditions'**
  String get notifprefOrdersTitle;

  /// No description provided for @notifprefOrdersDesc.
  ///
  /// In fr, this message translates to:
  /// **'Nouvelles commandes, préparation, expédition'**
  String get notifprefOrdersDesc;

  /// No description provided for @notifprefReturnsTitle.
  ///
  /// In fr, this message translates to:
  /// **'Retours & litiges'**
  String get notifprefReturnsTitle;

  /// No description provided for @notifprefReturnsDesc.
  ///
  /// In fr, this message translates to:
  /// **'Demandes de retour, remboursements, litiges'**
  String get notifprefReturnsDesc;

  /// No description provided for @notifprefReviewsTitle.
  ///
  /// In fr, this message translates to:
  /// **'Avis clients'**
  String get notifprefReviewsTitle;

  /// No description provided for @notifprefReviewsDesc.
  ///
  /// In fr, this message translates to:
  /// **'Nouveaux avis sur vos produits'**
  String get notifprefReviewsDesc;

  /// No description provided for @notifprefMessagesTitle.
  ///
  /// In fr, this message translates to:
  /// **'Messages'**
  String get notifprefMessagesTitle;

  /// No description provided for @notifprefMessagesDesc.
  ///
  /// In fr, this message translates to:
  /// **'Nouveaux messages des acheteurs'**
  String get notifprefMessagesDesc;

  /// No description provided for @notifprefAccountTitle.
  ///
  /// In fr, this message translates to:
  /// **'Compte & paiements'**
  String get notifprefAccountTitle;

  /// No description provided for @notifprefAccountDesc.
  ///
  /// In fr, this message translates to:
  /// **'Validation de boutique, retraits, versements'**
  String get notifprefAccountDesc;

  /// No description provided for @privTitle.
  ///
  /// In fr, this message translates to:
  /// **'Confidentialité'**
  String get privTitle;

  /// No description provided for @privHeaderTitle.
  ///
  /// In fr, this message translates to:
  /// **'Vos données'**
  String get privHeaderTitle;

  /// No description provided for @termsTitle.
  ///
  /// In fr, this message translates to:
  /// **'Conditions générales'**
  String get termsTitle;

  /// No description provided for @shopTitle.
  ///
  /// In fr, this message translates to:
  /// **'Ma boutique'**
  String get shopTitle;

  /// No description provided for @shopCompanyInfo.
  ///
  /// In fr, this message translates to:
  /// **'Informations société'**
  String get shopCompanyInfo;

  /// No description provided for @shopCompanyInfoEmpty.
  ///
  /// In fr, this message translates to:
  /// **'Renseignez les informations de votre entreprise (raison sociale, RCCM, IFU…). Elles facilitent la validation de votre profil.'**
  String get shopCompanyInfoEmpty;

  /// No description provided for @shopFillIn.
  ///
  /// In fr, this message translates to:
  /// **'Renseigner'**
  String get shopFillIn;

  /// No description provided for @shopLegalName.
  ///
  /// In fr, this message translates to:
  /// **'Raison sociale'**
  String get shopLegalName;

  /// No description provided for @shopActivity.
  ///
  /// In fr, this message translates to:
  /// **'Activité'**
  String get shopActivity;

  /// No description provided for @shopAddress.
  ///
  /// In fr, this message translates to:
  /// **'Adresse'**
  String get shopAddress;

  /// No description provided for @shopCity.
  ///
  /// In fr, this message translates to:
  /// **'Commune'**
  String get shopCity;

  /// No description provided for @shopManager.
  ///
  /// In fr, this message translates to:
  /// **'Gérant'**
  String get shopManager;

  /// No description provided for @shopPhone.
  ///
  /// In fr, this message translates to:
  /// **'Téléphone'**
  String get shopPhone;

  /// No description provided for @shopEdit.
  ///
  /// In fr, this message translates to:
  /// **'Modifier'**
  String get shopEdit;

  /// No description provided for @shopPayoutAccount.
  ///
  /// In fr, this message translates to:
  /// **'Compte de versement'**
  String get shopPayoutAccount;

  /// No description provided for @shopOperator.
  ///
  /// In fr, this message translates to:
  /// **'Opérateur'**
  String get shopOperator;

  /// No description provided for @shopNumber.
  ///
  /// In fr, this message translates to:
  /// **'Numéro'**
  String get shopNumber;

  /// No description provided for @shopAccountHolder.
  ///
  /// In fr, this message translates to:
  /// **'Titulaire'**
  String get shopAccountHolder;

  /// No description provided for @shopNoPayoutAccount.
  ///
  /// In fr, this message translates to:
  /// **'Aucun compte Mobile Money enregistré. Sans lui, vos gains ne peuvent pas vous être versés.'**
  String get shopNoPayoutAccount;

  /// No description provided for @shopSaveMyAccount.
  ///
  /// In fr, this message translates to:
  /// **'Enregistrer mon compte'**
  String get shopSaveMyAccount;

  /// No description provided for @shopDocumentsKyb.
  ///
  /// In fr, this message translates to:
  /// **'Documents (KYB)'**
  String get shopDocumentsKyb;

  /// No description provided for @shopAdd.
  ///
  /// In fr, this message translates to:
  /// **'AJOUTER'**
  String get shopAdd;

  /// No description provided for @shopNoDocuments.
  ///
  /// In fr, this message translates to:
  /// **'Aucun document déposé. La vérification de votre entreprise est requise pour activer votre boutique et recevoir vos versements.'**
  String get shopNoDocuments;

  /// No description provided for @shopUploadDocument.
  ///
  /// In fr, this message translates to:
  /// **'Déposer un document'**
  String get shopUploadDocument;

  /// No description provided for @shopDocumentsSecure.
  ///
  /// In fr, this message translates to:
  /// **'Vos pièces sont stockées de façon sécurisée et ne sont jamais visibles par les acheteurs.'**
  String get shopDocumentsSecure;

  /// No description provided for @shopLogoUpdated.
  ///
  /// In fr, this message translates to:
  /// **'Logo mis à jour.'**
  String get shopLogoUpdated;

  /// No description provided for @shopDocumentRejected.
  ///
  /// In fr, this message translates to:
  /// **'Refusée — déposez une nouvelle pièce'**
  String get shopDocumentRejected;

  /// No description provided for @shopDelete.
  ///
  /// In fr, this message translates to:
  /// **'Supprimer'**
  String get shopDelete;

  /// No description provided for @shopDeleteDocumentTitle.
  ///
  /// In fr, this message translates to:
  /// **'Supprimer ce document ?'**
  String get shopDeleteDocumentTitle;

  /// No description provided for @shopDeleteDocumentBody.
  ///
  /// In fr, this message translates to:
  /// **'« {label} » sera retiré de votre dossier. Vous devrez le redéposer pour que votre boutique soit vérifiée.'**
  String shopDeleteDocumentBody(String label);

  /// No description provided for @shopCancel.
  ///
  /// In fr, this message translates to:
  /// **'Annuler'**
  String get shopCancel;

  /// No description provided for @shopDocumentDeleted.
  ///
  /// In fr, this message translates to:
  /// **'Document supprimé.'**
  String get shopDocumentDeleted;

  /// No description provided for @shopFileTooLarge.
  ///
  /// In fr, this message translates to:
  /// **'Fichier trop volumineux (max 10 Mo).'**
  String get shopFileTooLarge;

  /// No description provided for @shopChooseFileError.
  ///
  /// In fr, this message translates to:
  /// **'Choisissez un fichier.'**
  String get shopChooseFileError;

  /// No description provided for @shopDocumentSent.
  ///
  /// In fr, this message translates to:
  /// **'Document envoyé. Il sera examiné sous peu.'**
  String get shopDocumentSent;

  /// No description provided for @shopKybTitle.
  ///
  /// In fr, this message translates to:
  /// **'Vérification de compte (KYB)'**
  String get shopKybTitle;

  /// No description provided for @shopKybSubtitle.
  ///
  /// In fr, this message translates to:
  /// **'Ces pièces prouvent l\'existence de votre entreprise. Elles sont examinées manuellement et restent confidentielles.'**
  String get shopKybSubtitle;

  /// No description provided for @shopDocumentType.
  ///
  /// In fr, this message translates to:
  /// **'Type de document'**
  String get shopDocumentType;

  /// No description provided for @shopChooseFile.
  ///
  /// In fr, this message translates to:
  /// **'Choisir un fichier'**
  String get shopChooseFile;

  /// No description provided for @shopFileFormats.
  ///
  /// In fr, this message translates to:
  /// **'PDF, JPG, PNG ou WebP — 10 Mo maximum'**
  String get shopFileFormats;

  /// No description provided for @shopSubmitForVerification.
  ///
  /// In fr, this message translates to:
  /// **'Soumettre pour vérification'**
  String get shopSubmitForVerification;

  /// No description provided for @shopNameRequired.
  ///
  /// In fr, this message translates to:
  /// **'Le nom est obligatoire.'**
  String get shopNameRequired;

  /// No description provided for @shopShopUpdated.
  ///
  /// In fr, this message translates to:
  /// **'Boutique mise à jour.'**
  String get shopShopUpdated;

  /// No description provided for @shopProfileTitle.
  ///
  /// In fr, this message translates to:
  /// **'Profil de la boutique'**
  String get shopProfileTitle;

  /// No description provided for @shopShopName.
  ///
  /// In fr, this message translates to:
  /// **'Nom de la boutique'**
  String get shopShopName;

  /// No description provided for @shopDescription.
  ///
  /// In fr, this message translates to:
  /// **'Description'**
  String get shopDescription;

  /// No description provided for @shopSave.
  ///
  /// In fr, this message translates to:
  /// **'Enregistrer'**
  String get shopSave;

  /// No description provided for @shopCompanyInfoUpdated.
  ///
  /// In fr, this message translates to:
  /// **'Informations société mises à jour.'**
  String get shopCompanyInfoUpdated;

  /// No description provided for @shopAllFieldsOptional.
  ///
  /// In fr, this message translates to:
  /// **'Tous les champs sont facultatifs. Ces informations aident à valider votre profil.'**
  String get shopAllFieldsOptional;

  /// No description provided for @shopPayoutSaved.
  ///
  /// In fr, this message translates to:
  /// **'Compte de versement enregistré.'**
  String get shopPayoutSaved;

  /// No description provided for @shopPayoutHint.
  ///
  /// In fr, this message translates to:
  /// **'C\'est sur ce numéro que vos retraits seront versés. Vérifiez-le : un numéro erroné envoie l\'argent à quelqu\'un d\'autre.'**
  String get shopPayoutHint;

  /// No description provided for @shopMobileMoneyNumber.
  ///
  /// In fr, this message translates to:
  /// **'Numéro Mobile Money'**
  String get shopMobileMoneyNumber;

  /// No description provided for @shopInvalidNumber.
  ///
  /// In fr, this message translates to:
  /// **'Numéro invalide'**
  String get shopInvalidNumber;

  /// No description provided for @shopAccountHolderName.
  ///
  /// In fr, this message translates to:
  /// **'Nom du titulaire'**
  String get shopAccountHolderName;

  /// No description provided for @shopHolderNameRequired.
  ///
  /// In fr, this message translates to:
  /// **'Nom obligatoire'**
  String get shopHolderNameRequired;

  /// No description provided for @helpTitle.
  ///
  /// In fr, this message translates to:
  /// **'Aide & support'**
  String get helpTitle;

  /// No description provided for @helpNeedHelp.
  ///
  /// In fr, this message translates to:
  /// **'Besoin d\'aide ?'**
  String get helpNeedHelp;

  /// No description provided for @helpBrowseFaq.
  ///
  /// In fr, this message translates to:
  /// **'Parcourez les questions fréquentes, ou écrivez-nous directement.'**
  String get helpBrowseFaq;

  /// No description provided for @helpContactSupport.
  ///
  /// In fr, this message translates to:
  /// **'Contacter le support'**
  String get helpContactSupport;

  /// No description provided for @helpSearchHint.
  ///
  /// In fr, this message translates to:
  /// **'Rechercher une question…'**
  String get helpSearchHint;

  /// No description provided for @helpNoResults.
  ///
  /// In fr, this message translates to:
  /// **'Aucune réponse pour « {query} ».'**
  String helpNoResults(String query);

  /// No description provided for @helpAskSupport.
  ///
  /// In fr, this message translates to:
  /// **'Poser la question au support'**
  String get helpAskSupport;

  /// No description provided for @helpEmailCopied.
  ///
  /// In fr, this message translates to:
  /// **'Adresse copiée : {email}'**
  String helpEmailCopied(String email);

  /// No description provided for @helpEmailSubject.
  ///
  /// In fr, this message translates to:
  /// **'Support vendeur — HbaExpress PRO'**
  String get helpEmailSubject;

  /// No description provided for @helpEmailBody.
  ///
  /// In fr, this message translates to:
  /// **'\n\n———\nMerci de décrire votre problème ci-dessus.\n(App HbaExpress PRO — réf. CGU {version})'**
  String helpEmailBody(String version);

  /// No description provided for @consentTitle.
  ///
  /// In fr, this message translates to:
  /// **'Avant de continuer'**
  String get consentTitle;

  /// No description provided for @consentIntro.
  ///
  /// In fr, this message translates to:
  /// **'Nous avons besoin de votre accord sur nos conditions générales et sur notre politique de confidentialité. Prenez le temps de les lire : elles disent ce à quoi vous vous engagez, et ce à quoi nous nous engageons.'**
  String get consentIntro;

  /// No description provided for @consentTermsTab.
  ///
  /// In fr, this message translates to:
  /// **'Conditions'**
  String get consentTermsTab;

  /// No description provided for @consentPrivacyTab.
  ///
  /// In fr, this message translates to:
  /// **'Confidentialité'**
  String get consentPrivacyTab;

  /// No description provided for @consentScrollHint.
  ///
  /// In fr, this message translates to:
  /// **'Faites défiler les deux documents jusqu’au bout pour pouvoir les accepter.'**
  String get consentScrollHint;

  /// No description provided for @consentCheckbox.
  ///
  /// In fr, this message translates to:
  /// **'J’ai lu et j’accepte les conditions générales et la politique de confidentialité.'**
  String get consentCheckbox;

  /// No description provided for @consentAcceptContinue.
  ///
  /// In fr, this message translates to:
  /// **'Accepter et continuer'**
  String get consentAcceptContinue;

  /// No description provided for @consentDecline.
  ///
  /// In fr, this message translates to:
  /// **'Refuser'**
  String get consentDecline;

  /// No description provided for @consentDeclineTitle.
  ///
  /// In fr, this message translates to:
  /// **'Refuser les conditions ?'**
  String get consentDeclineTitle;

  /// No description provided for @consentDeclineBody.
  ///
  /// In fr, this message translates to:
  /// **'Sans votre accord, l\'application ne peut pas être utilisée : vous serez déconnecté. Votre boutique et vos données ne sont pas supprimées.'**
  String get consentDeclineBody;

  /// No description provided for @consentGoBack.
  ///
  /// In fr, this message translates to:
  /// **'Revenir'**
  String get consentGoBack;

  /// No description provided for @consentDeclineAndLeave.
  ///
  /// In fr, this message translates to:
  /// **'Refuser et quitter'**
  String get consentDeclineAndLeave;

  /// No description provided for @consentSaveError.
  ///
  /// In fr, this message translates to:
  /// **'Votre accord n\'\'a pas pu être enregistré : {error}'**
  String consentSaveError(String error);

  /// No description provided for @legalVersion.
  ///
  /// In fr, this message translates to:
  /// **'Version du {date} · {company}'**
  String legalVersion(String date, String company);

  /// No description provided for @statTitle.
  ///
  /// In fr, this message translates to:
  /// **'Statistiques'**
  String get statTitle;

  /// No description provided for @stat30Days.
  ///
  /// In fr, this message translates to:
  /// **'30 jours'**
  String get stat30Days;

  /// No description provided for @stat90Days.
  ///
  /// In fr, this message translates to:
  /// **'90 jours'**
  String get stat90Days;

  /// No description provided for @stat12Months.
  ///
  /// In fr, this message translates to:
  /// **'12 mois'**
  String get stat12Months;

  /// No description provided for @statRevenue.
  ///
  /// In fr, this message translates to:
  /// **'Chiffre d\'affaires'**
  String get statRevenue;

  /// No description provided for @statNoSales.
  ///
  /// In fr, this message translates to:
  /// **'Aucune vente sur cette période.\nVos ventes apparaîtront ici.'**
  String get statNoSales;

  /// No description provided for @statOrders.
  ///
  /// In fr, this message translates to:
  /// **'Commandes'**
  String get statOrders;

  /// No description provided for @statItemsSold.
  ///
  /// In fr, this message translates to:
  /// **'Articles vendus'**
  String get statItemsSold;

  /// No description provided for @statOrdersByStatus.
  ///
  /// In fr, this message translates to:
  /// **'Commandes par statut'**
  String get statOrdersByStatus;

  /// No description provided for @statOrdersByStatusSub.
  ///
  /// In fr, this message translates to:
  /// **'Toutes vos commandes, réparties par état.'**
  String get statOrdersByStatusSub;

  /// No description provided for @statGrossToNet.
  ///
  /// In fr, this message translates to:
  /// **'Brut → net'**
  String get statGrossToNet;

  /// No description provided for @statGrossToNetSub.
  ///
  /// In fr, this message translates to:
  /// **'30 derniers jours, après commission de la marketplace.'**
  String get statGrossToNetSub;

  /// No description provided for @statBucketToProcess.
  ///
  /// In fr, this message translates to:
  /// **'À traiter'**
  String get statBucketToProcess;

  /// No description provided for @statBucketShipped.
  ///
  /// In fr, this message translates to:
  /// **'Expédiées'**
  String get statBucketShipped;

  /// No description provided for @statBucketDelivered.
  ///
  /// In fr, this message translates to:
  /// **'Livrées'**
  String get statBucketDelivered;

  /// No description provided for @statBucketCancelled.
  ///
  /// In fr, this message translates to:
  /// **'Annulées'**
  String get statBucketCancelled;

  /// No description provided for @statBucketOther.
  ///
  /// In fr, this message translates to:
  /// **'Autres'**
  String get statBucketOther;

  /// No description provided for @statOrdersLabel.
  ///
  /// In fr, this message translates to:
  /// **'commandes'**
  String get statOrdersLabel;

  /// No description provided for @statAxisGross.
  ///
  /// In fr, this message translates to:
  /// **'Brut'**
  String get statAxisGross;

  /// No description provided for @statAxisNet.
  ///
  /// In fr, this message translates to:
  /// **'Net'**
  String get statAxisNet;

  /// No description provided for @statCommissionTaken.
  ///
  /// In fr, this message translates to:
  /// **'Commission prélevée'**
  String get statCommissionTaken;

  /// No description provided for @condNew.
  ///
  /// In fr, this message translates to:
  /// **'Neuf'**
  String get condNew;

  /// No description provided for @condUsed.
  ///
  /// In fr, this message translates to:
  /// **'Occasion'**
  String get condUsed;

  /// No description provided for @condRefurbished.
  ///
  /// In fr, this message translates to:
  /// **'Reconditionné'**
  String get condRefurbished;

  /// No description provided for @payoutMtn.
  ///
  /// In fr, this message translates to:
  /// **'MTN Mobile Money (Bénin)'**
  String get payoutMtn;

  /// No description provided for @payoutMoov.
  ///
  /// In fr, this message translates to:
  /// **'Moov Money (Bénin)'**
  String get payoutMoov;

  /// No description provided for @payoutCeltis.
  ///
  /// In fr, this message translates to:
  /// **'Celtis Cash (Bénin)'**
  String get payoutCeltis;

  /// No description provided for @kybBusinessRegistry.
  ///
  /// In fr, this message translates to:
  /// **'Registre du commerce'**
  String get kybBusinessRegistry;

  /// No description provided for @kybIdCard.
  ///
  /// In fr, this message translates to:
  /// **'Pièce d\'identité'**
  String get kybIdCard;

  /// No description provided for @kybTaxId.
  ///
  /// In fr, this message translates to:
  /// **'Identifiant fiscal'**
  String get kybTaxId;

  /// No description provided for @kybProofOfAddress.
  ///
  /// In fr, this message translates to:
  /// **'Justificatif de domicile'**
  String get kybProofOfAddress;

  /// No description provided for @kybFallbackDocument.
  ///
  /// In fr, this message translates to:
  /// **'Document'**
  String get kybFallbackDocument;

  /// No description provided for @ageJustNow.
  ///
  /// In fr, this message translates to:
  /// **'À l\'instant'**
  String get ageJustNow;

  /// No description provided for @ageMinutes.
  ///
  /// In fr, this message translates to:
  /// **'il y a {n} min'**
  String ageMinutes(int n);

  /// No description provided for @ageHours.
  ///
  /// In fr, this message translates to:
  /// **'il y a {n} h'**
  String ageHours(int n);

  /// No description provided for @ageDays.
  ///
  /// In fr, this message translates to:
  /// **'il y a {n} j'**
  String ageDays(int n);

  /// No description provided for @carrierLoading.
  ///
  /// In fr, this message translates to:
  /// **'Chargement des transporteurs…'**
  String get carrierLoading;

  /// No description provided for @carrierSelectHint.
  ///
  /// In fr, this message translates to:
  /// **'Choisir un transporteur'**
  String get carrierSelectHint;

  /// No description provided for @carrierOther.
  ///
  /// In fr, this message translates to:
  /// **'Autre (préciser)'**
  String get carrierOther;

  /// No description provided for @carrierOtherLabel.
  ///
  /// In fr, this message translates to:
  /// **'Nom du transporteur'**
  String get carrierOtherLabel;

  /// No description provided for @shpTrackingOpenError.
  ///
  /// In fr, this message translates to:
  /// **'Impossible d\'ouvrir le lien de suivi.'**
  String get shpTrackingOpenError;

  /// No description provided for @commonFeatureUnavailable.
  ///
  /// In fr, this message translates to:
  /// **'Cette fonctionnalité n\'est pas encore disponible sur le serveur.'**
  String get commonFeatureUnavailable;

  /// No description provided for @commonNoResultsFor.
  ///
  /// In fr, this message translates to:
  /// **'Aucun résultat pour « {query} ».'**
  String commonNoResultsFor(String query);

  /// No description provided for @pillPaid.
  ///
  /// In fr, this message translates to:
  /// **'Payé'**
  String get pillPaid;

  /// No description provided for @pillPayoutProcessing.
  ///
  /// In fr, this message translates to:
  /// **'Versement en cours'**
  String get pillPayoutProcessing;

  /// No description provided for @pillPending.
  ///
  /// In fr, this message translates to:
  /// **'En attente'**
  String get pillPending;

  /// No description provided for @pillRejectedM.
  ///
  /// In fr, this message translates to:
  /// **'Refusé'**
  String get pillRejectedM;

  /// No description provided for @pillFailed.
  ///
  /// In fr, this message translates to:
  /// **'Échec'**
  String get pillFailed;

  /// No description provided for @pillProcessing.
  ///
  /// In fr, this message translates to:
  /// **'En traitement'**
  String get pillProcessing;

  /// No description provided for @pillDelivered.
  ///
  /// In fr, this message translates to:
  /// **'Livrée'**
  String get pillDelivered;

  /// No description provided for @pillShipped.
  ///
  /// In fr, this message translates to:
  /// **'Expédiée'**
  String get pillShipped;

  /// No description provided for @pillToProcess.
  ///
  /// In fr, this message translates to:
  /// **'À traiter'**
  String get pillToProcess;

  /// No description provided for @pillCancelled.
  ///
  /// In fr, this message translates to:
  /// **'Annulée'**
  String get pillCancelled;

  /// No description provided for @pillRefunded.
  ///
  /// In fr, this message translates to:
  /// **'Remboursée'**
  String get pillRefunded;

  /// No description provided for @pillPrepared.
  ///
  /// In fr, this message translates to:
  /// **'Préparée'**
  String get pillPrepared;

  /// No description provided for @pillToPrepare.
  ///
  /// In fr, this message translates to:
  /// **'À préparer'**
  String get pillToPrepare;

  /// No description provided for @pillOnline.
  ///
  /// In fr, this message translates to:
  /// **'En ligne'**
  String get pillOnline;

  /// No description provided for @pillPaused.
  ///
  /// In fr, this message translates to:
  /// **'En pause'**
  String get pillPaused;

  /// No description provided for @pillDraft.
  ///
  /// In fr, this message translates to:
  /// **'Brouillon'**
  String get pillDraft;

  /// No description provided for @pillArchived.
  ///
  /// In fr, this message translates to:
  /// **'Archivé'**
  String get pillArchived;

  /// No description provided for @pillVerified.
  ///
  /// In fr, this message translates to:
  /// **'Vérifiée'**
  String get pillVerified;

  /// No description provided for @pillRejectedF.
  ///
  /// In fr, this message translates to:
  /// **'Refusée'**
  String get pillRejectedF;

  /// No description provided for @pillInReview.
  ///
  /// In fr, this message translates to:
  /// **'En vérification'**
  String get pillInReview;

  /// No description provided for @pillToComplete.
  ///
  /// In fr, this message translates to:
  /// **'À compléter'**
  String get pillToComplete;

  /// No description provided for @commonSeeAll.
  ///
  /// In fr, this message translates to:
  /// **'VOIR TOUT'**
  String get commonSeeAll;

  /// No description provided for @legalFrenchNote.
  ///
  /// In fr, this message translates to:
  /// **'Ce document est publié en français, sa langue qui fait foi.'**
  String get legalFrenchNote;

  /// No description provided for @ordShipmentSection.
  ///
  /// In fr, this message translates to:
  /// **'Expédition'**
  String get ordShipmentSection;

  /// No description provided for @ordNoShipmentYet.
  ///
  /// In fr, this message translates to:
  /// **'L\'expédition est créée automatiquement une fois le paiement encaissé. Si elle n\'apparaît pas encore, patientez un instant puis rafraîchissez.'**
  String get ordNoShipmentYet;

  /// No description provided for @commonDescription.
  ///
  /// In fr, this message translates to:
  /// **'Description'**
  String get commonDescription;

  /// No description provided for @commonSku.
  ///
  /// In fr, this message translates to:
  /// **'SKU'**
  String get commonSku;

  /// No description provided for @commonStock.
  ///
  /// In fr, this message translates to:
  /// **'Stock'**
  String get commonStock;

  /// No description provided for @routeNotFoundTitle.
  ///
  /// In fr, this message translates to:
  /// **'Page introuvable'**
  String get routeNotFoundTitle;

  /// No description provided for @routeBackHome.
  ///
  /// In fr, this message translates to:
  /// **'Retour à l\'accueil'**
  String get routeBackHome;

  /// No description provided for @updTitle.
  ///
  /// In fr, this message translates to:
  /// **'Mise à jour requise'**
  String get updTitle;

  /// No description provided for @updBody.
  ///
  /// In fr, this message translates to:
  /// **'Cette version de l\'application n\'est plus prise en charge. Mettez-la à jour pour continuer à gérer votre boutique.'**
  String get updBody;

  /// No description provided for @updButton.
  ///
  /// In fr, this message translates to:
  /// **'Mettre à jour'**
  String get updButton;

  /// No description provided for @updAlreadyUpdated.
  ///
  /// In fr, this message translates to:
  /// **'J\'ai déjà mis à jour'**
  String get updAlreadyUpdated;

  /// No description provided for @updNoStoreLink.
  ///
  /// In fr, this message translates to:
  /// **'Lien de mise à jour indisponible. Recherchez « HbaExpress PRO » dans votre store.'**
  String get updNoStoreLink;

  /// No description provided for @notifprefDeviceTitle.
  ///
  /// In fr, this message translates to:
  /// **'Cet appareil'**
  String get notifprefDeviceTitle;

  /// No description provided for @notifprefDeviceHint.
  ///
  /// In fr, this message translates to:
  /// **'Si vous ne recevez pas les notifications alors qu\'elles sont autorisées, réactivez-les ici pour réenregistrer cet appareil.'**
  String get notifprefDeviceHint;

  /// No description provided for @notifprefDeviceButton.
  ///
  /// In fr, this message translates to:
  /// **'Activer les notifications sur cet appareil'**
  String get notifprefDeviceButton;

  /// No description provided for @notifprefDeviceEnabling.
  ///
  /// In fr, this message translates to:
  /// **'Activation…'**
  String get notifprefDeviceEnabling;

  /// No description provided for @notifprefDeviceRegistered.
  ///
  /// In fr, this message translates to:
  /// **'Cet appareil est enregistré pour les notifications.'**
  String get notifprefDeviceRegistered;

  /// No description provided for @notifprefDevicePermissionDenied.
  ///
  /// In fr, this message translates to:
  /// **'Notifications refusées. Activez-les dans les Réglages du téléphone, puis réessayez.'**
  String get notifprefDevicePermissionDenied;

  /// No description provided for @notifprefDeviceApnsUnavailable.
  ///
  /// In fr, this message translates to:
  /// **'Impossible d\'obtenir le jeton de notification pour l\'instant. Réessayez dans un instant, avec une bonne connexion, sur un appareil réel.'**
  String get notifprefDeviceApnsUnavailable;

  /// No description provided for @notifprefDeviceError.
  ///
  /// In fr, this message translates to:
  /// **'Échec de l\'activation. Vérifiez votre connexion et réessayez.'**
  String get notifprefDeviceError;

  /// No description provided for @authLoginBrandKicker.
  ///
  /// In fr, this message translates to:
  /// **'HBA PARTNER'**
  String get authLoginBrandKicker;

  /// No description provided for @authLoginTitle.
  ///
  /// In fr, this message translates to:
  /// **'Gérez votre activité avec HBA'**
  String get authLoginTitle;

  /// No description provided for @navActivities.
  ///
  /// In fr, this message translates to:
  /// **'Activités'**
  String get navActivities;

  /// No description provided for @navFinance.
  ///
  /// In fr, this message translates to:
  /// **'Finances'**
  String get navFinance;

  /// No description provided for @navAccount.
  ///
  /// In fr, this message translates to:
  /// **'Compte'**
  String get navAccount;
}

class _AppLocalizationsDelegate
    extends LocalizationsDelegate<AppLocalizations> {
  const _AppLocalizationsDelegate();

  @override
  Future<AppLocalizations> load(Locale locale) {
    return SynchronousFuture<AppLocalizations>(lookupAppLocalizations(locale));
  }

  @override
  bool isSupported(Locale locale) =>
      <String>['en', 'fr'].contains(locale.languageCode);

  @override
  bool shouldReload(_AppLocalizationsDelegate old) => false;
}

AppLocalizations lookupAppLocalizations(Locale locale) {
  // Lookup logic when only language code is specified.
  switch (locale.languageCode) {
    case 'en':
      return AppLocalizationsEn();
    case 'fr':
      return AppLocalizationsFr();
  }

  throw FlutterError(
      'AppLocalizations.delegate failed to load unsupported locale "$locale". This is likely '
      'an issue with the localizations generation tool. Please file an issue '
      'on GitHub with a reproducible sample app and the gen-l10n configuration '
      'that was used.');
}
