// ignore: unused_import
import 'package:intl/intl.dart' as intl;
import 'app_localizations.dart';

// ignore_for_file: type=lint

/// The translations for English (`en`).
class AppLocalizationsEn extends AppLocalizations {
  AppLocalizationsEn([String locale = 'en']) : super(locale);

  @override
  String get appTitle => 'HbaExpress PRO';

  @override
  String get commonCancel => 'Cancel';

  @override
  String get commonSave => 'Save';

  @override
  String get commonDelete => 'Delete';

  @override
  String get commonClose => 'Close';

  @override
  String get commonRetry => 'Retry';

  @override
  String get commonConfirm => 'Confirm';

  @override
  String get commonContinue => 'Continue';

  @override
  String get commonBack => 'Back';

  @override
  String get commonNext => 'Next';

  @override
  String get commonAdd => 'Add';

  @override
  String get commonEdit => 'Edit';

  @override
  String get commonSearch => 'Search';

  @override
  String get commonLoading => 'Loading…';

  @override
  String get commonYes => 'Yes';

  @override
  String get commonNo => 'No';

  @override
  String get commonError => 'Something went wrong.';

  @override
  String get settingsAppearanceTitle => 'Appearance';

  @override
  String get settingsTheme => 'Theme';

  @override
  String get settingsThemeAuto => 'Automatic';

  @override
  String get settingsThemeAutoDesc => 'Follows your phone setting';

  @override
  String get settingsThemeLight => 'Light';

  @override
  String get settingsThemeLightDesc => 'Always light background';

  @override
  String get settingsThemeDark => 'Dark';

  @override
  String get settingsThemeDarkDesc => 'Always dark background';

  @override
  String get settingsLanguage => 'Language';

  @override
  String get settingsLanguageSystem => 'Automatic';

  @override
  String get settingsLanguageSystemDesc => 'Follows your phone language';

  @override
  String get settingsLanguageFrench => 'French';

  @override
  String get settingsLanguageEnglish => 'English';

  @override
  String get navHome => 'Home';

  @override
  String get navOrders => 'Orders';

  @override
  String get navProducts => 'Products';

  @override
  String get navMessages => 'Messages';

  @override
  String get navMore => 'More';

  @override
  String get homeTitle => 'Dashboard';

  @override
  String get homeStatsTooltip => 'Statistics';

  @override
  String get homeNotificationsTooltip => 'Notifications';

  @override
  String get homeDefaultShopName => 'My shop';

  @override
  String get homeGreeting => 'Hello';

  @override
  String get homeSubtitle => 'Here is your shop activity.';

  @override
  String get homeKybInReviewTitle => 'Verification in progress';

  @override
  String get homeKybInReviewSubtitle =>
      'You will be able to publish your products once your profile is approved.';

  @override
  String get homeKybRejectedTitle => 'Verification declined';

  @override
  String get homeKybRejectedSubtitle =>
      'Fix and resubmit your documents to publish your products.';

  @override
  String get homeKybPendingTitle => 'Verify your shop';

  @override
  String get homeKybPendingSubtitle =>
      'Submit your documents (KYB) to publish your products and get paid.';

  @override
  String get homeKpiToProcess => 'To process';

  @override
  String get homeKpiOrders => 'Orders';

  @override
  String get homeKpiSales30d => 'Sales (30 d)';

  @override
  String get homeKpiNetPayout30d => 'Net payout (30 d)';

  @override
  String get homeKpiAverageRating => 'Average rating';

  @override
  String get homeKpiReviewsReceived => 'Reviews received';

  @override
  String homeReturnsToProcess(int count) {
    String _temp0 = intl.Intl.pluralLogic(
      count,
      locale: localeName,
      other: '$count returns to process',
      one: '1 return to process',
    );
    return '$_temp0';
  }

  @override
  String get homeReturnsDisputeWarning =>
      'Without a reply, the buyer will open a dispute.';

  @override
  String get homeWalletTitle => 'Wallet';

  @override
  String get homeWalletManage => 'Manage';

  @override
  String get homeWalletAvailable => 'Available balance';

  @override
  String get homeWalletPendingDelivery => 'Awaiting delivery';

  @override
  String get homeWalletPendingWithdrawal => 'Withdrawals in progress';

  @override
  String get homeRecentOrders => 'Latest orders';

  @override
  String get homeStepCompleteShopTitle => 'Complete your shop';

  @override
  String get homeStepCompleteShopSubtitle =>
      'Approved documents + payout account';

  @override
  String get homeStepAddLocationTitle => 'Add a shipping location';

  @override
  String get homeStepAddLocationSubtitle =>
      'The address your parcels ship from';

  @override
  String get homeStepCreateProductTitle => 'Create a product';

  @override
  String get homeStepCreateProductSubtitle => 'Photo, category, description';

  @override
  String get homeStepPublishOfferTitle => 'Put a product on sale';

  @override
  String get homeStepPublishOfferSubtitle => 'Set your price to become buyable';

  @override
  String get homeChecklistTitle => 'Getting started';

  @override
  String homeChecklistProgress(int done, int total) {
    return '$done/$total step(s) — a few more to start selling';
  }

  @override
  String get homeChecklistDismiss => 'Hide';

  @override
  String get ordTitle => 'Orders';

  @override
  String get ordSearchHint => 'Search for an order (reference, SKU)';

  @override
  String get ordEmptyMessage => 'No orders in this view.';

  @override
  String get ordFilterAll => 'All';

  @override
  String get ordFilterToProcess => 'To process';

  @override
  String get ordFilterShipped => 'Shipped';

  @override
  String get ordFilterDelivered => 'Delivered';

  @override
  String get ordPaymentPending => 'Payment pending';

  @override
  String ordCustomerItems(String customer, int count) {
    return '$customer · $count item(s)';
  }

  @override
  String get ordDetailTitle => 'Order details';

  @override
  String get ordClient => 'Customer';

  @override
  String get ordDate => 'Date';

  @override
  String get ordPayment => 'Payment';

  @override
  String get ordPaid => 'Collected';

  @override
  String get ordPending => 'Pending';

  @override
  String get ordTotal => 'Total';

  @override
  String get ordPaymentNotCollectedWarning =>
      'Payment for this order has not been collected yet. Wait for confirmation before preparing the shipment.';

  @override
  String get ordArticles => 'Items';

  @override
  String get ordShipping => 'Shipping';

  @override
  String get ordRecipient => 'Recipient';

  @override
  String get ordAddress => 'Address';

  @override
  String get ordLandmark => 'Landmark';

  @override
  String get ordQuartier => 'Neighbourhood';

  @override
  String get ordCommune => 'Commune';

  @override
  String get ordCity => 'Commune';

  @override
  String get ordPhone => 'Phone';

  @override
  String ordDisputes(int count) {
    return 'Disputes ($count)';
  }

  @override
  String ordDisputeClosed(String date) {
    return 'Closed · $date';
  }

  @override
  String ordDisputeOpen(String age) {
    return 'Opened $age — reply to defend yourself';
  }

  @override
  String ordLineQtyPrice(int qty, String price) {
    return '$qty × $price';
  }

  @override
  String get accTitle => 'My account';

  @override
  String get accLoading => 'Loading…';

  @override
  String get accSectionShop => 'My shop';

  @override
  String get accShopLabel => 'Shop';

  @override
  String get accShopSubtitle => 'Profile, payout account, documents';

  @override
  String get accOffersLabel => 'Listings';

  @override
  String get accOffersSubtitle => 'Price and availability of your products';

  @override
  String get accShipmentsLabel => 'Shipments';

  @override
  String get accLocationsLabel => 'Shipping locations';

  @override
  String get accLocationsSubtitle => 'Addresses your parcels ship from';

  @override
  String get accReturnsLabel => 'Returns';

  @override
  String get accReturnsSubtitle => 'Return requests and refunds';

  @override
  String get accReviewsLabel => 'Customer reviews';

  @override
  String get accSectionMoney => 'Money';

  @override
  String get accWalletLabel => 'Wallet';

  @override
  String get accWalletSubtitle => 'Balances and withdrawals';

  @override
  String get accFinanceLabel => 'Finances';

  @override
  String get accFinanceSubtitle => 'Statement and payouts';

  @override
  String get accSectionAccount => 'Account';

  @override
  String get accNotificationsLabel => 'Notifications';

  @override
  String get accProfileLabel => 'Profile and security';

  @override
  String get accAppearanceLabel => 'Appearance';

  @override
  String get accAppearanceSubtitle => 'Light or dark theme';

  @override
  String get accPrivacyLabel => 'Privacy';

  @override
  String get accPrivacySubtitle => 'Your data and your rights';

  @override
  String get accHelpLabel => 'Help & support';

  @override
  String get accHelpSubtitle => 'FAQ and contact';

  @override
  String get accTermsLabel => 'Terms of use';

  @override
  String get accTermsSubtitle => 'Commission, payouts, disputes';

  @override
  String get accCloseLabel => 'Close my account';

  @override
  String get accCloseSubtitle => 'Pauses selling — reversible';

  @override
  String get accDeleteLabel => 'Permanently delete my account';

  @override
  String get accDeleteSubtitle =>
      'Erases your account and your data · irreversible';

  @override
  String get accLogout => 'Log out';

  @override
  String get accLogoutConfirmTitle => 'Log out?';

  @override
  String get accLogoutConfirmBody =>
      'You will need to enter your credentials again.';

  @override
  String get accCancel => 'Cancel';

  @override
  String get accCloseConfirmTitle => 'Close your account?';

  @override
  String get accCloseConfirmBody =>
      'Your products will be removed from sale immediately. Your account and its history are kept — permanent deletion is decided by the team. You can request reactivation at any time.';

  @override
  String get accCloseConfirmAction => 'Close account';

  @override
  String get accCloseSuccess =>
      'Account closed. Your products are no longer on sale.';

  @override
  String get accDeleteSuccess => 'Your account has been deleted.';

  @override
  String get accDeleteSheetTitle => 'Permanently delete account';

  @override
  String get accDeleteSheetBody =>
      'This action is irreversible. Your account, your shop and your products will be deleted, and your personal data anonymized. Documents required by law (invoices, payouts) are kept with no link to your identity.';

  @override
  String get accDeleteSheetUnderstand =>
      'I understand that this deletion is permanent.';

  @override
  String get accPasswordLabel => 'Your password';

  @override
  String get accPasswordHint => 'To confirm it\'s really you';

  @override
  String get accDeleteSheetAction => 'Delete my account';

  @override
  String get accReactivationPendingTitle => 'Reactivation under review';

  @override
  String get accClosedTitle => 'Account closed';

  @override
  String get accReactivationPendingBody =>
      'Your reactivation request has been sent. An administrator will review it; your products remain off sale in the meantime.';

  @override
  String get accClosedBody =>
      'Your account is closed: your products are no longer on sale. You can request its reactivation.';

  @override
  String get accRequestReactivation => 'Request reactivation';

  @override
  String get accReactivationSent =>
      'Request sent. An administrator will review it.';

  @override
  String get prodTitle => 'Products';

  @override
  String get prodNew => 'New';

  @override
  String get prodSearchHint => 'Search a product';

  @override
  String get prodEmpty =>
      'No products yet.\nAdd your first item to start selling.';

  @override
  String get prodStep1Title => '1. Describe the product';

  @override
  String get prodStep1Subtitle => 'Category, name, description, photos';

  @override
  String get prodStep2Title => '2. The variation';

  @override
  String get prodStep2Subtitle =>
      'The version being sold (size, color… or default)';

  @override
  String get prodStep3Title => '3. Listing & stock';

  @override
  String get prodStep3Subtitle =>
      'Condition, price, ship-from location and quantity';

  @override
  String get prodHowToTitle => 'How to create a product';

  @override
  String get prodHowToSubtitle =>
      'A product can be sold once its 3 steps are completed.';

  @override
  String get prodHowToCta => 'Create my product';

  @override
  String get prodFilterAll => 'All';

  @override
  String get prodFilterActive => 'Live';

  @override
  String get prodFilterDraft => 'Drafts';

  @override
  String get prodFilterArchived => 'Archived';

  @override
  String get prodActionsTooltip => 'Actions';

  @override
  String get prodMenuOpen => 'View details';

  @override
  String get prodMenuPublish => 'Publish';

  @override
  String get prodMenuDraft => 'Move back to draft';

  @override
  String get prodMenuArchive => 'Archive';

  @override
  String get prodUpdated => 'Product updated.';

  @override
  String get pdTitle => 'Product sheet';

  @override
  String get pdActionsTooltip => 'Actions';

  @override
  String get pdMenuPreview => 'Customer preview';

  @override
  String get pdEditSheet => 'Edit product sheet';

  @override
  String get pdUnlist => 'Remove from sale';

  @override
  String get pdMenuPublish => 'Publish';

  @override
  String get pdMenuDraft => 'Move back to draft';

  @override
  String get pdMenuArchive => 'Archive';

  @override
  String get pdUnlistTitle => 'Remove from sale?';

  @override
  String pdUnlistBody(int count, String name) {
    return 'The $count active listing(s) of “$name” will be paused: the product can no longer be bought. Your photos, stock and prices are kept — you can put it back on sale whenever you want.';
  }

  @override
  String get pdUnlistSuccess => 'Product removed from sale.';

  @override
  String get pdDeleteTitle => 'Delete this product?';

  @override
  String get pdDeleteBody =>
      'The product, its photos and its variations will be deleted. This action is permanent — prefer “Archive” to remove it from sale while keeping the history.';

  @override
  String get pdDeleteSuccess => 'Product deleted.';

  @override
  String get pdUpdated => 'Product updated.';

  @override
  String pdPhotosTooLargeIgnored(int count) {
    return '$count photo(s) exceed 5 MB and were ignored.';
  }

  @override
  String pdPhotosAdded(int count) {
    return '$count photo(s) added.';
  }

  @override
  String get pdGalleryEmpty => 'No photo — your buyers will see nothing.';

  @override
  String get pdAddPhotos => 'Add photos';

  @override
  String pdPhotoCount(int count) {
    return '$count photo(s)';
  }

  @override
  String get pdPrimarySet => 'Main photo set.';

  @override
  String get pdPhotoDeleted => 'Photo deleted.';

  @override
  String get pdPrimaryHint =>
      'The “Main” photo is the one buyers see in listings.';

  @override
  String get pdPrimaryBadge => 'Main';

  @override
  String get pdSetPrimary => 'Set as main photo';

  @override
  String get pdDeletePhoto => 'Delete photo';

  @override
  String get pdInfoSection => 'Information';

  @override
  String get pdInfoCategory => 'Category';

  @override
  String get pdInfoVariants => 'Variations';

  @override
  String get pdInfoNoDescription =>
      'No description. Buyers buy what they understand: add one.';

  @override
  String pdVariantsSection(int count) {
    return 'Variations ($count)';
  }

  @override
  String get pdSectionAdd => 'ADD';

  @override
  String get pdVariantsEmpty =>
      'No variation. Stock and listings are attached to a SKU: create at least one variation to be able to sell this product.';

  @override
  String get pdCreateVariant => 'Create a variation';

  @override
  String get pdDeleteVariantTitle => 'Delete variation?';

  @override
  String pdDeleteVariantBody(String label, String sku) {
    return 'The variation “$label” and the stock attached to SKU $sku will no longer be offered.';
  }

  @override
  String get pdVariantDeleted => 'Variation deleted.';

  @override
  String get pdVariantAdded => 'Variation added.';

  @override
  String get pdNewVariant => 'New variation';

  @override
  String get pdVariantSkuIntro =>
      'The SKU is your internal reference: it carries the stock and the listing. It must be unique.';

  @override
  String get pdSkuHelper =>
      'Generated automatically — editable. If left blank, it will be regenerated.';

  @override
  String get pdAttrLabel => 'Attribute';

  @override
  String get pdAttrHintSize => 'Size';

  @override
  String get pdValueLabel => 'Value';

  @override
  String get pdRemoveAttr => 'Remove this attribute';

  @override
  String get pdAddAttr => 'Add an attribute';

  @override
  String get pdBarcodeOptional => 'Barcode (optional)';

  @override
  String get pdWeight => 'Weight';

  @override
  String get pdAddVariant => 'Add variation';

  @override
  String get pdLocationShort => 'Location';

  @override
  String pdStockUnavailable(String error) {
    return 'Stock unavailable: $error';
  }

  @override
  String get pdNoStockForSku => 'No stock recorded for this SKU.';

  @override
  String get pdLocationFallback => 'Location';

  @override
  String pdStockAvailable(int count) {
    return '$count available';
  }

  @override
  String get pdStockLow => 'Low';

  @override
  String pdStockLine(int onHand, int reserved, int threshold) {
    return 'In stock $onHand · Reserved $reserved · Alert threshold $threshold';
  }

  @override
  String get pdStockReceive => 'Receive';

  @override
  String get pdStockAdjust => 'Adjust ±';

  @override
  String get pdThresholdShort => 'Threshold';

  @override
  String get pdReceiveTitle => 'Receive stock';

  @override
  String get pdReceiveHint => 'Quantity received (goods inward).';

  @override
  String get pdQuantity => 'Quantity';

  @override
  String get pdAdjustTitle => 'Adjust stock';

  @override
  String get pdAdjustHint =>
      'Signed correction: −2 for a loss, +2 for a return to shelf.';

  @override
  String get pdAdjustLabel => 'Delta (+ / −)';

  @override
  String get pdAlertThreshold => 'Alert threshold';

  @override
  String get pdThresholdHint => 'Below this level, stock is flagged as low.';

  @override
  String get pdEnterInteger => 'Enter a whole number.';

  @override
  String get pdValuePositive => 'The value must be positive.';

  @override
  String get pdAdjustZero => 'An adjustment of zero changes nothing.';

  @override
  String get pdStockUpdated => 'Stock updated.';

  @override
  String get pdValidate => 'Confirm';

  @override
  String get pdChooseLocation => 'Choose a location.';

  @override
  String get pdStockCreated => 'Stock created.';

  @override
  String pdStockSkuTitle(String sku) {
    return 'Stock — SKU $sku';
  }

  @override
  String pdLocationsUnavailable(String error) {
    return 'Locations unavailable: $error';
  }

  @override
  String get pdNoShipLocation =>
      'No ship-from location. Create one from a listing before recording stock.';

  @override
  String get pdShipLocation => 'Ship-from location';

  @override
  String get pdStockQty => 'Quantity in stock';

  @override
  String get pdCreateStock => 'Create stock';

  @override
  String pdOffersSection(int count) {
    return 'Listings ($count)';
  }

  @override
  String pdOffersUnavailable(String error) {
    return 'Listings unavailable: $error';
  }

  @override
  String get pdNoOfferCanCreate =>
      'No listing: this product cannot be bought, even when online. Create a listing to set your price.';

  @override
  String get pdNoOfferNoVariant =>
      'No listing possible: first create a variation, it is the one that carries the listing SKU.';

  @override
  String get pdPutOnSale => 'Put on sale';

  @override
  String get pdNameMin3 => 'The name must be at least 3 characters.';

  @override
  String get pdSheetUpdated => 'Product sheet updated.';

  @override
  String get pdName => 'Name';

  @override
  String get pdGtinOptional => 'GTIN (optional)';

  @override
  String get pdEanOptional => 'EAN (optional)';

  @override
  String get pwChooseCategory => 'Choose a category.';

  @override
  String get pwNameMin3 => 'The name must be at least 3 characters.';

  @override
  String get pwDescribeMin10 => 'Describe the product (10 characters minimum).';

  @override
  String get pwAddPhoto => 'Add at least one photo.';

  @override
  String get pwPhotoTooLarge => 'A photo exceeds 5 MB. Remove it.';

  @override
  String get pwSkuRequired => 'The variation SKU is required.';

  @override
  String get pwPriceInvalid => 'Enter a valid price.';

  @override
  String get pwChooseLocation => 'Choose a ship-from location.';

  @override
  String get pwStockMin1 => 'Enter a stock of at least 1 unit.';

  @override
  String get pwStepProduct => 'product';

  @override
  String get pwStepVariant => 'variation';

  @override
  String get pwStepOffer => 'listing';

  @override
  String get pwStepStock => 'stock';

  @override
  String get pwCreatedSuccess =>
      'Product ready: variation, listing and stock created.';

  @override
  String get pwNothingKept => 'Nothing was kept.';

  @override
  String get pwDraftKept => 'The draft was kept — delete it from Products.';

  @override
  String pwStepFailed(String step, String error, String tail) {
    return 'Step “$step” failed: $error $tail';
  }

  @override
  String get pwNewProduct => 'New product';

  @override
  String get pwCategoriesUnavailable => 'Categories unavailable.';

  @override
  String get pwProductName => 'Product name';

  @override
  String get pwProductNameHint => 'e.g. Plus-size wax dress';

  @override
  String get pwDescriptionHelper =>
      'Material, dimensions, what\'s included — that\'s what triggers the purchase.';

  @override
  String get pwMoreInfo => 'Additional information (optional)';

  @override
  String get pwBrandOptional => 'Brand (optional)';

  @override
  String get pwNone => 'None';

  @override
  String get pwGtinOptional => 'GTIN code (optional)';

  @override
  String get pwEanOptional => 'EAN code (optional)';

  @override
  String get pwTagsOptional => 'Tags (optional)';

  @override
  String get pwTagsHint => 'e.g. wax, cotton, handmade';

  @override
  String get pwTagsHelper => 'Separate tags with a comma.';

  @override
  String get pwVariantInfo =>
      'A variation is a version of your product (e.g. a size, a color). A simple product has only one. The SKU identifies it and carries the stock.';

  @override
  String get pwSkuLabel => 'SKU (internal reference)';

  @override
  String get pwSkuHelper =>
      'Generated automatically. Editable if you have your own system.';

  @override
  String get pwAttrLogistics => 'Attributes & logistics (optional)';

  @override
  String get pwAttrIntro =>
      'Attributes (size, color…) — only if this variation has any.';

  @override
  String get pwAttrLabel => 'Attribute';

  @override
  String get pwValueLabel => 'Value';

  @override
  String get pwAddAttr => 'Add an attribute';

  @override
  String get pwBarcodeOptional => 'Barcode (optional)';

  @override
  String get pwWeightG => 'Weight (g)';

  @override
  String get pwCondition => 'Product condition';

  @override
  String get pwYourPrice => 'Your price (net received)';

  @override
  String get pwLocationsUnavailable => 'Locations unavailable.';

  @override
  String get pwShipLocation => 'Ship-from location';

  @override
  String get pwAddLocation => 'Add a location';

  @override
  String get pwStockQty => 'Stock (quantity)';

  @override
  String get pwAlertThreshold => 'Alert threshold';

  @override
  String get pwHandlingTime => 'Handling time';

  @override
  String pwDays(int count) {
    String _temp0 = intl.Intl.pluralLogic(
      count,
      locale: localeName,
      other: '$count days',
      one: '1 day',
    );
    return '$_temp0';
  }

  @override
  String get pwReviewTitle => 'Review before creating';

  @override
  String get pwSummaryProduct => 'Product';

  @override
  String get pwName => 'Name';

  @override
  String get pwCategory => 'Category';

  @override
  String pwPhotosCount(int count) {
    return 'Photos ($count)';
  }

  @override
  String get pwSummaryVariant => 'Variation';

  @override
  String get pwAttributes => 'Attributes';

  @override
  String get pwSummaryOffer => 'Listing & stock';

  @override
  String get pwConditionShort => 'Condition';

  @override
  String get pwNetPrice => 'Your net price';

  @override
  String get pwDisplayedPrice => 'Price shown to customer';

  @override
  String get pwConfirmNote =>
      'By confirming, the product, its variation, its listing and its stock are created at once. The product will be visible to buyers once your shop is approved (KYB).';

  @override
  String get pwAddress => 'Address';

  @override
  String get pwCreateAndSell => 'Create and list';

  @override
  String get pwStepBarProduct => 'Product';

  @override
  String get pwStepBarVariant => 'Variation';

  @override
  String get pwStepBarOffer => 'Listing';

  @override
  String get pwStepBarSummary => 'Summary';

  @override
  String pwStepIndicator(int current, int total, String label) {
    return 'Step $current/$total — $label';
  }

  @override
  String get pwNoLocationBody =>
      'No ship-from location. You need one to list (the address your parcels ship from).';

  @override
  String get pwAddShipLocation => 'Add a ship-from location';

  @override
  String get pcChooseCategory => 'Choose a category.';

  @override
  String get pcAddPhoto =>
      'Add at least one photo: without an image, the product cannot be created.';

  @override
  String pcPhotosTooLarge(int count) {
    return '$count photo(s) exceed 5 MB. Remove them or retry the cutout.';
  }

  @override
  String pcDuplicateAttr(String key) {
    return 'Duplicate attribute “$key”.';
  }

  @override
  String get pcCreatedDraft => 'Product created as a draft.';

  @override
  String get pcDiscardTitle => 'Discard this product?';

  @override
  String get pcDiscardBody =>
      'What you have entered, photos included, will be lost.';

  @override
  String get pcKeepEditing => 'Keep editing';

  @override
  String get pcDiscard => 'Discard';

  @override
  String get pcNewProduct => 'New product';

  @override
  String get pcDraftNote =>
      'The product is created as a draft. You will then create a listing (price, stock) to put it on sale.';

  @override
  String get pcLegendInfo => 'Information';

  @override
  String get pcCategoriesUnavailable => 'Categories unavailable.';

  @override
  String get pcProductName => 'Product name';

  @override
  String get pcProductNameHint => 'e.g. Plus-size wax dress';

  @override
  String get pcNameMin3 => '3 characters minimum';

  @override
  String get pcDescriptionHelper =>
      'Material, dimensions, what\'s included — that\'s what triggers the purchase.';

  @override
  String get pcDescribeMin10 => 'Describe the product (10 characters minimum)';

  @override
  String get pcLegendId => 'Identification';

  @override
  String get pcBrandsUnavailable =>
      'Brands unavailable — you can create the product without one.';

  @override
  String get pcBrandOptional => 'Brand (optional)';

  @override
  String get pcNone => 'None';

  @override
  String get pcGtinOptional => 'GTIN code (optional)';

  @override
  String get pcGtinHint => '8 to 14 digits';

  @override
  String get pcEanOptional => 'EAN code (optional)';

  @override
  String get pcEanHint => '8 or 13 digits';

  @override
  String get pcGroupOptional => 'Product group (optional)';

  @override
  String get pcGroupHint => 'Group identifier';

  @override
  String get pcGroupHelper =>
      'Groups related products (same model, different colors).';

  @override
  String get pcInvalidId => 'Invalid identifier';

  @override
  String get pcAddTag => 'Add a tag (optional)';

  @override
  String get pcTagHint => 'e.g. wax, cotton, handmade';

  @override
  String get pcTagHelper =>
      'Helps buyers find the product. Press Enter to confirm.';

  @override
  String get pcLegendAttr => 'Attributes';

  @override
  String get pcAttrIntro =>
      'Characteristics common to all variations (material, warranty…). What changes from one variation to another — size, color — is set in the variations.';

  @override
  String get pcKey => 'Key';

  @override
  String get pcValueLabel => 'Value';

  @override
  String get pcRemoveAttr => 'Remove attribute';

  @override
  String get pcAddAttr => 'Add an attribute';

  @override
  String get pcCreateProduct => 'Create product';

  @override
  String pcBarcodeDigits(String lengths) {
    return '$lengths digits';
  }

  @override
  String pcMaxPhotos(int count) {
    return '$count photos maximum.';
  }

  @override
  String pcOnlyAdded(int room, int max) {
    return 'Only $room photo(s) were added ($max maximum).';
  }

  @override
  String get pcRetryFailed =>
      'The cutout failed again. The photo will be sent as is.';

  @override
  String get pcPrimaryHint =>
      'The 1st photo is the main image — the only one visible in listings. Tap a photo to move it to the front or remove it.';

  @override
  String pcFailedBanner(int count) {
    return '$count photo(s) could not be cut out: they will be sent with their original background. Tap them to retry.';
  }

  @override
  String pcOversizedBanner(int count) {
    return '$count photo(s) exceed 5 MB and will be rejected by the server. Remove them.';
  }

  @override
  String get pcPhotoRequired => 'At least one photo is required.';

  @override
  String get pcPrimaryBadge => 'Main';

  @override
  String get pcSetPrimary => 'Set as main photo';

  @override
  String get pcSetPrimarySub =>
      'This is the image buyers will see in listings.';

  @override
  String get pcRetryProcessing => 'Retry cutout';

  @override
  String get pcRetrySub => 'Automatic white background via the server.';

  @override
  String get pcRemovePhoto => 'Remove photo';

  @override
  String get pcAddPhotos => 'Add photos';

  @override
  String get pcPickerSubtitle =>
      'Automatic cutout on white background · 8 photos maximum';

  @override
  String get pcFromGallery => 'From gallery';

  @override
  String get pcMultiSelect => 'Multiple selection possible.';

  @override
  String get pcTakePhoto => 'Take a photo';

  @override
  String get pcOptional => 'optional';

  @override
  String get cpChooseCategory => 'Choose a category';

  @override
  String cpChooseThis(String name) {
    return 'Choose “$name”';
  }

  @override
  String get cpSubcategories => 'Subcategories';

  @override
  String get cpCategory => 'Category';

  @override
  String get ppvTitle => 'Customer preview';

  @override
  String get ppvPriceUnavailable => 'Price unavailable';

  @override
  String get ppvNoDescription =>
      'No description — the buyer won\'t know what they\'re buying.';

  @override
  String get ppvAddToCart => 'Add to cart';

  @override
  String get ppvUnavailable => 'Unavailable';

  @override
  String get ppvPreviewNote => 'Preview — no real action from this screen.';

  @override
  String get ppvBuyable => 'This product is visible and buyable.';

  @override
  String get ppvReasonBoth =>
      'The product is a draft AND has no active listing.';

  @override
  String get ppvReasonDraft => 'The product is a draft: buyers can\'t see it.';

  @override
  String get ppvReasonNoOffer =>
      'No active listing: the product is visible but CANNOT be bought.';

  @override
  String get ppvNoPhoto => 'No photo — the product won\'t get noticed.';

  @override
  String get ppvInStock => 'In stock';

  @override
  String get ppvOutOfStock => 'Out of stock';

  @override
  String ppvShippedIn(int days) {
    return 'Ships in $days d';
  }

  @override
  String get imgpReady => 'Photos ready';

  @override
  String get imgpPreparing => 'Preparing photos…';

  @override
  String get imgpDoneDesc =>
      'Cut out on white background. Compare, then confirm.';

  @override
  String imgpInProgress(int done, int total) {
    return 'Cutout in progress — $done/$total';
  }

  @override
  String get imgpAllFailed =>
      'The cutout failed. You can send the original photos, but they won\'t have the catalog\'s white background.';

  @override
  String imgpSomeFailed(int count) {
    return '$count photo(s) could not be cut out. They will be sent as is.';
  }

  @override
  String get imgpRetry => 'Retry cutout';

  @override
  String get imgpUse => 'Use';

  @override
  String get imgpBefore => 'Before';

  @override
  String get imgpAfter => 'After';

  @override
  String get imgpNotProcessed => 'Not cut out';

  @override
  String get authLoginTagline =>
      'Shop, restaurant, orders and revenue in one place.';

  @override
  String get authLoginEmailLabel => 'Phone or email';

  @override
  String get authLoginEmailInvalid => 'Enter a valid email';

  @override
  String get authLoginPasswordLabel => 'Password';

  @override
  String get authLoginPasswordMin => '6 characters minimum';

  @override
  String get authLoginForgotPassword => 'Forgot password?';

  @override
  String authLoginEnableBio(String label) {
    return 'Enable $label for future sign-ins';
  }

  @override
  String get authLoginMfaLabel => 'Verification code';

  @override
  String get authLoginResendVerification => 'Resend verification code';

  @override
  String get authLoginSignIn => 'Sign in';

  @override
  String authLoginSignInWithBio(String label) {
    return 'Sign in with $label';
  }

  @override
  String get authLoginNoAccount => 'Don\'t have a seller account yet?';

  @override
  String get authLoginCreateAccount => 'Create a partner account';

  @override
  String get authLoginEnterEmail => 'Enter your email address.';

  @override
  String get authLoginNoAccountToVerify =>
      'No account to verify for this address.';

  @override
  String get authLoginBiometricReason => 'Sign in to HbaExpress PRO';

  @override
  String get authRegisterTitle => 'Create a seller account';

  @override
  String get authRegisterHeadline => 'Open your shop';

  @override
  String get authRegisterSubtitle =>
      'Create your account in a few minutes. You\'ll be able to publish your products once your profile is approved by our team.';

  @override
  String get authRegisterShopNameLabel => 'Shop name';

  @override
  String get authRegisterShopNameHint => 'My Shop';

  @override
  String get authRegisterShopNameRequired => 'Shop name required';

  @override
  String get authRegisterFirstNameLabel => 'First name';

  @override
  String get authRegisterLastNameLabel => 'Last name';

  @override
  String get authRegisterRequired => 'Required';

  @override
  String get authRegisterEmailLabel => 'Email';

  @override
  String get authRegisterEmailInvalid => 'Invalid email';

  @override
  String get authRegisterPhoneLabel => 'Phone';

  @override
  String get authRegisterPhoneRequired => 'Phone required';

  @override
  String get authRegisterPasswordLabel => 'Password';

  @override
  String get authRegisterPasswordMin => '6 characters minimum';

  @override
  String get authRegisterLegalNameLabel => 'Legal name';

  @override
  String get authRegisterActivityLabel => 'Business activity';

  @override
  String get authRegisterActivityHint => 'Clothing, electronics…';

  @override
  String get authRegisterAddressLabel => 'Address';

  @override
  String get authRegisterAddressHint => 'Street, district';

  @override
  String get authRegisterCityLabel => 'Commune';

  @override
  String get authRegisterManagerLabel => 'Manager';

  @override
  String get authRegisterManagerHint => 'Legal representative name';

  @override
  String get authRegisterCompanyPhoneLabel => 'Company phone';

  @override
  String get authRegisterCompanyToggle => 'Company details (optional)';

  @override
  String get authRegisterContinue => 'Continue';

  @override
  String get authRegisterHaveAccount => 'I already have an account — Sign in';

  @override
  String get authVerifyEnterCode => 'Enter the 6-digit code.';

  @override
  String get authVerifyEnterShopName => 'Enter your shop name.';

  @override
  String get authVerifyAccountCreated =>
      'Account created. Sign in to access your shop.';

  @override
  String get authVerifyCodeResent => 'A new code has been sent to you.';

  @override
  String get authVerifyTitle => 'Verification';

  @override
  String get authVerifyHeadline => 'Enter your code';

  @override
  String get authVerifySentTo => 'We sent a 6-digit code to\n';

  @override
  String get authVerifyShopNameLabel => 'Your shop name';

  @override
  String get authVerifyShopNameHint => 'My Shop';

  @override
  String get authVerifyValidate => 'Confirm';

  @override
  String get authVerifySending => 'Sending…';

  @override
  String get authVerifyResend => 'Resend code';

  @override
  String get authForgotTitle => 'Forgot password';

  @override
  String get authForgotHeadline => 'Reset password';

  @override
  String get authForgotSubtitle =>
      'Enter your account email: we\'ll send you a 6-digit code to choose a new password.';

  @override
  String get authForgotEmailInvalid => 'Invalid email';

  @override
  String get authForgotSendCode => 'Send code';

  @override
  String get authForgotBackToLogin => 'Back to sign in';

  @override
  String get authResetSuccess => 'Password reset. Sign in.';

  @override
  String get authResetCodeResent => 'A new code has been sent to you.';

  @override
  String get authResetTitle => 'New password';

  @override
  String get authResetHeadline => 'Choose a new password';

  @override
  String get authResetSentTo => 'Enter the 6-digit code sent to\n';

  @override
  String get authResetCodeLabel => 'Verification code';

  @override
  String get authResetCodeRequired => '6-digit code required';

  @override
  String get authResetPasswordLabel => 'New password';

  @override
  String get authResetPasswordMin => '6 characters minimum';

  @override
  String get authResetSubmit => 'Reset';

  @override
  String get authResetSending => 'Sending…';

  @override
  String get authResetResend => 'Resend code';

  @override
  String get authSplashTagline => 'Your shop, in your pocket';

  @override
  String get offListTitle => 'Listings';

  @override
  String get offListNewOffer => 'New listing';

  @override
  String get offListEmpty =>
      'No listings yet.\nA listing is the price at which you sell a product.';

  @override
  String get offCardActionsTooltip => 'Actions';

  @override
  String get offCardEditPrice => 'Edit price';

  @override
  String get offCardRemoveDiscount => 'Remove discount';

  @override
  String get offCardApplyDiscount => 'Apply a discount';

  @override
  String get offCardPause => 'Pause';

  @override
  String get offCardReactivate => 'Reactivate';

  @override
  String offCardHandling(int days) {
    return 'Prep. $days d';
  }

  @override
  String get offCardYouReceive => 'You receive';

  @override
  String get offCardCustomerPays => 'Customer pays';

  @override
  String get offCardPromo => 'Sale';

  @override
  String offCardBefore(String price) {
    return 'was $price';
  }

  @override
  String offCardSpreadNote(int commission, int fee) {
    return 'Difference: platform commission ($commission%) + payment fees ($fee%).';
  }

  @override
  String get offSheetDiscountRemoved => 'Discount removed.';

  @override
  String get offSheetBackOnSale => 'Back on sale.';

  @override
  String get offSheetSalePaused => 'Listing paused.';

  @override
  String get offSheetYouReceive => 'You receive';

  @override
  String get offSheetCommission => 'Platform commission';

  @override
  String get offSheetPaymentFees => 'Payment fees';

  @override
  String get offSheetDisplayedPrice => 'Price shown to customer';

  @override
  String get offSheetChooseProduct => 'Choose a product.';

  @override
  String get offSheetChooseVariant => 'Choose the variant (SKU) concerned.';

  @override
  String get offSheetChooseStore => 'Choose a store';

  @override
  String get offSheetStore => 'Store';

  @override
  String get offSheetChooseProductFirst => 'Pick a product first.';

  @override
  String get offSheetNoVariants =>
      'This product has no variant. Add one from its page before listing it.';

  @override
  String get offSheetNoStores =>
      'No store on this account. Create one before listing an item.';

  @override
  String get offSheetChooseLocation => 'Choose a shipping location.';

  @override
  String get offSheetOfferCreated => 'Listing created.';

  @override
  String get offSheetDuplicateTitle => 'This variant already has a listing';

  @override
  String get offSheetDuplicateBody =>
      'Each variant has only one listing: its stock is unique and cannot be both new and used at the same time.\n\nTo sell this product in another condition, create a second variant (e.g. “Used”) from the product page, then add its listing.';

  @override
  String get offSheetGotIt => 'Got it';

  @override
  String get offSheetNewOfferTitle => 'New listing';

  @override
  String offSheetProductsUnavailable(String error) {
    return 'Products unavailable: $error';
  }

  @override
  String get offSheetProduct => 'Product';

  @override
  String get offSheetVariantSku => 'Variant (SKU)';

  @override
  String get offSheetSkuInternalRef => 'SKU (internal reference)';

  @override
  String get offSheetSkuRequired => 'SKU required';

  @override
  String get offSheetCondition => 'Product condition';

  @override
  String get offSheetConditionHint =>
      'One variant = one listing. To sell in both New AND Used, create one variant per condition.';

  @override
  String get offSheetYourPrice => 'Your price (net received)';

  @override
  String get offSheetInvalidPrice => 'Invalid price';

  @override
  String offSheetLocationsUnavailable(String error) {
    return 'Shipping locations unavailable: $error';
  }

  @override
  String get offSheetNoLocations =>
      'No shipping location. Add one before listing.';

  @override
  String get offSheetShipFrom => 'Shipping location';

  @override
  String get offSheetHandlingTime => 'Handling time';

  @override
  String get offSheetDay1 => '1 day';

  @override
  String get offSheetDays2 => '2 days';

  @override
  String get offSheetDays3 => '3 days';

  @override
  String get offSheetDays5 => '5 days';

  @override
  String get offSheetPublish => 'List for sale';

  @override
  String get offSheetInvalidPriceNotice => 'Invalid price.';

  @override
  String get offSheetPriceUpdated => 'Price updated.';

  @override
  String get offSheetSave => 'Save';

  @override
  String get offSheetInvalidDiscount =>
      'Invalid discount: the reduced price must stay positive and below the current price.';

  @override
  String get offSheetDiscountApplied => 'Discount applied.';

  @override
  String get offSheetApplyDiscountTitle => 'Apply a discount';

  @override
  String offSheetCurrentPriceNote(String price) {
    return 'Current price: $price. The discount reduces your net price; commission is recalculated on the reduced price.';
  }

  @override
  String get offSheetDiscountType => 'Discount type';

  @override
  String get offSheetPercentage => 'Percentage (%)';

  @override
  String get offSheetFixedAmount => 'Fixed amount';

  @override
  String get offSheetPercentageLabel => 'Percentage';

  @override
  String get offSheetAmountLabel => 'Amount';

  @override
  String get offSheetPromoEndOptional => 'Sale end date (optional)';

  @override
  String offSheetUntil(String date) {
    return 'Until $date';
  }

  @override
  String get offSheetNoEndDate => 'No end date';

  @override
  String get offSheetApplyDiscountBtn => 'Apply discount';

  @override
  String get offLocationsTitle => 'Shipping locations';

  @override
  String get offLocationsAdd => 'Add';

  @override
  String get offLocationsEmpty =>
      'No shipping location.\nAdd the address your parcels ship from: it is required to list items for sale.';

  @override
  String get offLocationsDeleteTitle => 'Delete this location?';

  @override
  String offLocationsDeleteBody(String label) {
    return '“$label” will no longer be offered as a shipping origin. Listings that use it will have to point to another location.';
  }

  @override
  String get offLocationsCancel => 'Cancel';

  @override
  String get offLocationsDelete => 'Delete';

  @override
  String get offLocationsDeleted => 'Location deleted.';

  @override
  String get offLocationsDeleteTooltip => 'Delete';

  @override
  String get offLocationsAdded => 'Location added.';

  @override
  String get offLocationsNewTitle => 'New shipping location';

  @override
  String get offLocationsNewSubtitle =>
      'The address your parcels ship from. It is used as the origin for your listings.';

  @override
  String get offLocationsAddressLabel => 'Address';

  @override
  String get offLocationsAddressHint => 'Street, number, district';

  @override
  String get offLocationsAddressRequired => 'Address required';

  @override
  String get offLocationsAddBtn => 'Add location';

  @override
  String get msgActionUnavailable =>
      'This action isn\'t available on the server yet.';

  @override
  String get msgEmpty => 'No messages yet. Write the first one.';

  @override
  String get msgDeleted => 'Message deleted';

  @override
  String get msgHideForMe => 'Delete for me';

  @override
  String get msgHideForMeHint => 'The customer will still see it';

  @override
  String get msgDeleteForEveryone => 'Delete for everyone';

  @override
  String get msgDeleteForEveryoneHint =>
      'The message will be replaced with “Message deleted”';

  @override
  String get msgAttachImage => 'Attach an image';

  @override
  String get msgInputHint => 'Your message…';

  @override
  String get msgNoConversations =>
      'No conversations yet.\nYour customers will write to you here.';

  @override
  String get msgNewThread => 'New thread';

  @override
  String get revTitle => 'Reviews';

  @override
  String get revEmpty => 'No reviews yet.';

  @override
  String get revYourReply => 'Your reply';

  @override
  String get revReply => 'Reply';

  @override
  String get revFlagTooltip => 'Report this review';

  @override
  String get revReplyTitle => 'Reply to this review';

  @override
  String get revReplyPublicNote =>
      'Your reply is public: all your future customers will read it.';

  @override
  String get revReplyHint => 'Your reply…';

  @override
  String get revReplyPublished => 'Reply published.';

  @override
  String get revPublish => 'Publish';

  @override
  String get revFlagConfirmTitle => 'Report this review?';

  @override
  String get revFlagConfirmBody =>
      'The review will be examined by moderation. It stays visible until a decision is made.';

  @override
  String get revCancel => 'Cancel';

  @override
  String get revFlag => 'Report';

  @override
  String get revFlaggedSuccess => 'Review reported to moderation.';

  @override
  String get dispTitle => 'Dispute';

  @override
  String get dispEmpty => 'No messages yet.';

  @override
  String get dispReplySent => 'Reply sent.';

  @override
  String get dispStatusResolved => 'Resolved';

  @override
  String get dispStatusEscalated => 'Escalated to the platform';

  @override
  String get dispStatusUnderReview => 'Under review';

  @override
  String get dispStatusOpen => 'Open';

  @override
  String dispOpenedAge(String age) {
    return 'Opened $age';
  }

  @override
  String get dispEscalatedWarning =>
      'The platform will arbitrate. Provide your evidence now: without a response from you, the decision will be made on the buyer\'s arguments alone.';

  @override
  String get dispClosedTitle => 'Dispute closed';

  @override
  String get dispRefundedToBuyer => 'Refunded to the buyer';

  @override
  String get dispComposerHint => 'Your reply: facts, dates, evidence…';

  @override
  String get finTitle => 'Finances';

  @override
  String get finRange12Months => '12 months';

  @override
  String finRangeDays(int days) {
    return '$days days';
  }

  @override
  String get finGrossSales => 'Gross sales';

  @override
  String get finPlatformCommission => 'Platform commission';

  @override
  String get finPaymentFees => 'Payment fees';

  @override
  String get finRefunds => 'Refunds';

  @override
  String get finNetForYou => 'Net for you';

  @override
  String get finDetail => 'Details';

  @override
  String get finNoMovements => 'No activity for this period.';

  @override
  String get finPayouts => 'Payouts';

  @override
  String finPayoutProviderDate(String provider, String date) {
    return '$provider · $date';
  }

  @override
  String get walTitle => 'Wallet';

  @override
  String get walWithdrawalHistory => 'Withdrawal history';

  @override
  String get walNoWithdrawals => 'No withdrawals yet.';

  @override
  String get walMovements => 'Activity';

  @override
  String get walAvailableBalance => 'Available balance';

  @override
  String walPendingDelivery(String amount) {
    return 'Pending delivery · $amount';
  }

  @override
  String get walPendingWithdrawals => 'Withdrawals in progress';

  @override
  String get walInvalidAmount => 'Invalid amount.';

  @override
  String get walRequestSaved =>
      'Request saved. It will be paid out after validation.';

  @override
  String get walRequestWithdrawal => 'Request a withdrawal';

  @override
  String get walNoMobileMoneyAccount =>
      'No Mobile Money account registered. Add it in your shop: without it, no payout is possible.';

  @override
  String get walAmount => 'Amount';

  @override
  String walAvailableAmount(String amount) {
    return 'Available: $amount';
  }

  @override
  String get walRequestWithdrawalButton => 'Request withdrawal';

  @override
  String get walFundsHeldNotice =>
      'Funds are held as soon as the request is made, then paid out after validation by the administrator.';

  @override
  String get walWithdrawalProcessing =>
      'Payout sent to your operator. It is confirmed once the money arrives — this can take a few minutes.';

  @override
  String get walFundsRecredited =>
      'The funds have been credited back to your balance.';

  @override
  String get shpTitle => 'Shipments';

  @override
  String get shpEmptyTodo =>
      'Nothing to ship.\nYour pending parcels will appear here.';

  @override
  String get shpEmptyOther => 'No shipments in this view.';

  @override
  String get shpFilterTodo => 'To process';

  @override
  String get shpFilterShipped => 'Shipped';

  @override
  String get shpFilterDelivered => 'Delivered';

  @override
  String get shpFilterCancelled => 'Cancelled';

  @override
  String get shpFilterAll => 'All';

  @override
  String shpFilterWithCount(String label, int count) {
    return '$label ($count)';
  }

  @override
  String shpCustomerItems(String customer, int count) {
    return '$customer · $count item(s)';
  }

  @override
  String get shpTracking => 'Tracking';

  @override
  String get shpMarkPrepared => 'Mark as prepared';

  @override
  String get shpMarkedPrepared => 'Parcel marked as prepared.';

  @override
  String get shpShip => 'Ship';

  @override
  String get shpConfirmDelivery => 'Confirm delivery';

  @override
  String get shpDeliveryConfirmed => 'Delivery confirmed.';

  @override
  String get shpCancel => 'Cancel';

  @override
  String get shpCancelDialogTitle => 'Cancel this shipment?';

  @override
  String shpCancelDialogBody(String orderReference) {
    return 'Order $orderReference will be reported as cancelled to the customer. This action cannot be undone.';
  }

  @override
  String get shpBack => 'Back';

  @override
  String get shpCancelShipment => 'Cancel shipment';

  @override
  String get shpShipmentCancelled => 'Shipment cancelled.';

  @override
  String get shpShipmentSaved => 'Shipment saved.';

  @override
  String shpShipSheetTitle(String orderReference) {
    return 'Ship $orderReference';
  }

  @override
  String get shpTrackingHint =>
      'The tracking number is shared with the customer: without it, any dispute becomes impossible to verify.';

  @override
  String get shpCarrier => 'Carrier';

  @override
  String get shpCarrierRequired => 'Carrier required';

  @override
  String get shpTrackingNumber => 'Tracking number';

  @override
  String get shpTrackingRequired => 'Tracking number required';

  @override
  String get shpConfirmShipment => 'Confirm shipment';

  @override
  String get retTitle => 'Returns';

  @override
  String get retEmptyTodo => 'No returns to process.';

  @override
  String get retEmptyOther => 'No returns in this view.';

  @override
  String get retFilterTodo => 'To process';

  @override
  String get retFilterRefunded => 'Refunded';

  @override
  String get retFilterRejected => 'Rejected';

  @override
  String get retFilterAll => 'All';

  @override
  String retFilterWithCount(String label, int count) {
    return '$label ($count)';
  }

  @override
  String get retStatusRefunded => 'Refunded';

  @override
  String get retStatusRefundPending => 'Transfer in progress';

  @override
  String get retStatusRejected => 'Rejected';

  @override
  String get retStatusReceived => 'Parcel received';

  @override
  String get retStatusApproved => 'Accepted';

  @override
  String get retStatusToReview => 'To review';

  @override
  String get retTracking => 'Tracking';

  @override
  String get retLabelRefunded => 'Refunded';

  @override
  String get retLabelToPay => 'To pay';

  @override
  String get retRefundValidatedNote =>
      'Refund approved. The transfer to the buyer is made by HBA Express within 24 to 72 hours. No action needed on your part.';

  @override
  String get retAccept => 'Accept';

  @override
  String get retReturnAccepted => 'Return accepted. Wait for the parcel.';

  @override
  String get retReject => 'Reject';

  @override
  String get retNoTrackingWarning =>
      'No tracking number: you won\'t be able to prove the parcel\'s return.';

  @override
  String get retEditTracking => 'Edit tracking';

  @override
  String get retEnterTracking => 'Enter tracking';

  @override
  String get retParcelReceived => 'Parcel received';

  @override
  String get retValidateRefund => 'Approve refund';

  @override
  String get retConfirmReceiptTitle => 'Confirm receipt?';

  @override
  String get retConfirmReceiptBody =>
      'Only declare the parcel received after checking its contents. You can then refund — fully or partially if the item is damaged.';

  @override
  String get retCancel => 'Cancel';

  @override
  String get retReceiptSaved => 'Receipt recorded.';

  @override
  String get retExplainRejection =>
      'Explain your rejection (10 characters minimum).';

  @override
  String get retReturnRejected => 'Return rejected.';

  @override
  String get retRejectReturn => 'Reject return';

  @override
  String get retRejectReasonHint =>
      'Your reason will be shared with the buyer. A clearly explained rejection avoids a dispute.';

  @override
  String get retRejectReasonExample =>
      'E.g.: the item has been used and cannot be resold.';

  @override
  String get retCarrierTrackingRequired =>
      'Carrier and tracking number are required.';

  @override
  String get retTrackingSaved => 'Tracking saved.';

  @override
  String get retTrackingSheetTitle => 'Return parcel tracking';

  @override
  String get retCarrier => 'Carrier';

  @override
  String get retTrackingNumber => 'Tracking number';

  @override
  String get retSave => 'Save';

  @override
  String get retInvalidAmount => 'Invalid amount.';

  @override
  String get retValidateRefundTitle => 'Approve this refund?';

  @override
  String retValidateRefundBody(String amount) {
    return '$amount will be refunded to the buyer by HBA Express, and deducted from your balance. This decision is final.';
  }

  @override
  String get retValidate => 'Approve';

  @override
  String get retRefundValidatedNotify =>
      'Refund approved. HBA Express transfers the amount to the buyer within 24 to 72 hours.';

  @override
  String get retPartialRefundHint =>
      'You can issue a partial refund if the item comes back damaged or incomplete.';

  @override
  String get retRefundAmount => 'Amount to refund';

  @override
  String get profTitle => 'Profile & security';

  @override
  String get profName => 'Name';

  @override
  String get profEmail => 'Email';

  @override
  String get profPhone => 'Phone';

  @override
  String get profShop => 'Shop';

  @override
  String get profEditInfo => 'Edit my information';

  @override
  String get profChangePassword => 'Change my password';

  @override
  String get profMfa => 'Two-factor authentication';

  @override
  String get profEnabled => 'Enabled';

  @override
  String get profDisabled => 'Disabled';

  @override
  String get profMyInfo => 'My information';

  @override
  String get profFirstName => 'First name';

  @override
  String get profUpdated => 'Profile updated.';

  @override
  String get profSave => 'Save';

  @override
  String get profCurrentPassword => 'Current password';

  @override
  String get profNewPassword => 'New password';

  @override
  String get profPasswordMinLength => 'Minimum 8 characters.';

  @override
  String get profPasswordChanged => 'Password changed.';

  @override
  String get profModify => 'Change';

  @override
  String get profEnableMfa => 'Enable two-factor authentication';

  @override
  String get profMfaStep1 =>
      '1. Open an authenticator app (Google Authenticator, Authy…).\n2. Add an account by entering this key:';

  @override
  String get profKeyCopied => 'Key copied.';

  @override
  String get profMfaStep3 => '3. Enter the 6-digit code shown by the app:';

  @override
  String get profCodeRequired => '6-digit code required.';

  @override
  String get profMfaEnabled => 'Two-factor authentication enabled.';

  @override
  String get profEnable => 'Enable';

  @override
  String get profDisableMfa => 'Disable two-factor authentication';

  @override
  String get profDisableMfaBody =>
      'Enter a code from your authenticator app to confirm.';

  @override
  String get profMfaDisabled => 'Two-factor authentication disabled.';

  @override
  String get profDisable => 'Disable';

  @override
  String get notifTitle => 'Notifications';

  @override
  String get notifPreferences => 'Preferences';

  @override
  String get notifMarkAllRead => 'Mark all read';

  @override
  String get notifEmpty => 'No notifications.';

  @override
  String get notifprefTitle => 'Notification preferences';

  @override
  String get notifprefIntro =>
      'Choose which push notifications you want to receive. Notifications remain available in your inbox, even when muted.';

  @override
  String get notifprefOrdersTitle => 'Orders & shipments';

  @override
  String get notifprefOrdersDesc => 'New orders, preparation, shipping';

  @override
  String get notifprefReturnsTitle => 'Returns & disputes';

  @override
  String get notifprefReturnsDesc => 'Return requests, refunds, disputes';

  @override
  String get notifprefReviewsTitle => 'Customer reviews';

  @override
  String get notifprefReviewsDesc => 'New reviews on your products';

  @override
  String get notifprefMessagesTitle => 'Messages';

  @override
  String get notifprefMessagesDesc => 'New messages from buyers';

  @override
  String get notifprefAccountTitle => 'Account & payments';

  @override
  String get notifprefAccountDesc => 'Shop approval, withdrawals, payouts';

  @override
  String get privTitle => 'Privacy';

  @override
  String get privHeaderTitle => 'Your data';

  @override
  String get termsTitle => 'Terms & conditions';

  @override
  String get shopTitle => 'My shop';

  @override
  String get shopCompanyInfo => 'Company information';

  @override
  String get shopCompanyInfoEmpty =>
      'Provide your company information (legal name, RCCM, IFU…). It helps validate your profile.';

  @override
  String get shopFillIn => 'Fill in';

  @override
  String get shopLegalName => 'Legal name';

  @override
  String get shopActivity => 'Activity';

  @override
  String get shopAddress => 'Address';

  @override
  String get shopCity => 'Commune';

  @override
  String get shopManager => 'Manager';

  @override
  String get shopPhone => 'Phone';

  @override
  String get shopEdit => 'Edit';

  @override
  String get shopPayoutAccount => 'Payout account';

  @override
  String get shopOperator => 'Operator';

  @override
  String get shopNumber => 'Number';

  @override
  String get shopAccountHolder => 'Account holder';

  @override
  String get shopNoPayoutAccount =>
      'No Mobile Money account registered. Without it, your earnings cannot be paid out to you.';

  @override
  String get shopSaveMyAccount => 'Save my account';

  @override
  String get shopDocumentsKyb => 'Documents (KYB)';

  @override
  String get shopAdd => 'ADD';

  @override
  String get shopNoDocuments =>
      'No document submitted. Verifying your company is required to activate your shop and receive your payouts.';

  @override
  String get shopUploadDocument => 'Upload a document';

  @override
  String get shopDocumentsSecure =>
      'Your documents are stored securely and are never visible to buyers.';

  @override
  String get shopLogoUpdated => 'Logo updated.';

  @override
  String get shopDocumentRejected => 'Rejected — submit a new document';

  @override
  String get shopDelete => 'Delete';

  @override
  String get shopDeleteDocumentTitle => 'Delete this document?';

  @override
  String shopDeleteDocumentBody(String label) {
    return '“$label” will be removed from your file. You will need to submit it again for your shop to be verified.';
  }

  @override
  String get shopCancel => 'Cancel';

  @override
  String get shopDocumentDeleted => 'Document deleted.';

  @override
  String get shopFileTooLarge => 'File too large (max 10 MB).';

  @override
  String get shopChooseFileError => 'Choose a file.';

  @override
  String get shopDocumentSent => 'Document sent. It will be reviewed shortly.';

  @override
  String get shopKybTitle => 'Account verification (KYB)';

  @override
  String get shopKybSubtitle =>
      'These documents prove that your company exists. They are reviewed manually and remain confidential.';

  @override
  String get shopDocumentType => 'Document type';

  @override
  String get shopChooseFile => 'Choose a file';

  @override
  String get shopFileFormats => 'PDF, JPG, PNG or WebP — 10 MB maximum';

  @override
  String get shopSubmitForVerification => 'Submit for verification';

  @override
  String get shopNameRequired => 'Name is required.';

  @override
  String get shopShopUpdated => 'Shop updated.';

  @override
  String get shopProfileTitle => 'Shop profile';

  @override
  String get shopShopName => 'Shop name';

  @override
  String get shopDescription => 'Description';

  @override
  String get shopSave => 'Save';

  @override
  String get shopCompanyInfoUpdated => 'Company information updated.';

  @override
  String get shopAllFieldsOptional =>
      'All fields are optional. This information helps validate your profile.';

  @override
  String get shopPayoutSaved => 'Payout account saved.';

  @override
  String get shopPayoutHint =>
      'This is the number your withdrawals will be paid to. Double-check it: a wrong number sends the money to someone else.';

  @override
  String get shopMobileMoneyNumber => 'Mobile Money number';

  @override
  String get shopInvalidNumber => 'Invalid number';

  @override
  String get shopAccountHolderName => 'Account holder name';

  @override
  String get shopHolderNameRequired => 'Name required';

  @override
  String get helpTitle => 'Help & support';

  @override
  String get helpNeedHelp => 'Need help?';

  @override
  String get helpBrowseFaq =>
      'Browse the frequently asked questions, or write to us directly.';

  @override
  String get helpContactSupport => 'Contact support';

  @override
  String get helpSearchHint => 'Search for a question…';

  @override
  String helpNoResults(String query) {
    return 'No answer for “$query”.';
  }

  @override
  String get helpAskSupport => 'Ask support this question';

  @override
  String helpEmailCopied(String email) {
    return 'Address copied: $email';
  }

  @override
  String get helpEmailSubject => 'Seller support — HbaExpress PRO';

  @override
  String helpEmailBody(String version) {
    return '\n\n———\nPlease describe your problem above.\n(HbaExpress PRO app — T&C ref. $version)';
  }

  @override
  String get consentTitle => 'Before you continue';

  @override
  String get consentIntro =>
      'We need your agreement to our terms and conditions and to our privacy policy. Take the time to read them: they state what you commit to, and what we commit to.';

  @override
  String get consentTermsTab => 'Terms';

  @override
  String get consentPrivacyTab => 'Privacy';

  @override
  String get consentScrollHint =>
      'Scroll both documents to the end to be able to accept them.';

  @override
  String get consentCheckbox =>
      'I have read and accept the terms and conditions and the privacy policy.';

  @override
  String get consentAcceptContinue => 'Accept and continue';

  @override
  String get consentDecline => 'Decline';

  @override
  String get consentDeclineTitle => 'Decline the terms?';

  @override
  String get consentDeclineBody =>
      'Without your agreement, the app cannot be used: you will be logged out. Your shop and your data are not deleted.';

  @override
  String get consentGoBack => 'Go back';

  @override
  String get consentDeclineAndLeave => 'Decline and leave';

  @override
  String consentSaveError(String error) {
    return 'Your agreement could not be saved: $error';
  }

  @override
  String legalVersion(String date, String company) {
    return 'Version of $date · $company';
  }

  @override
  String get statTitle => 'Statistics';

  @override
  String get stat30Days => '30 days';

  @override
  String get stat90Days => '90 days';

  @override
  String get stat12Months => '12 months';

  @override
  String get statRevenue => 'Revenue';

  @override
  String get statNoSales =>
      'No sales in this period.\nYour sales will appear here.';

  @override
  String get statOrders => 'Orders';

  @override
  String get statItemsSold => 'Items sold';

  @override
  String get statOrdersByStatus => 'Orders by status';

  @override
  String get statOrdersByStatusSub => 'All your orders, broken down by state.';

  @override
  String get statGrossToNet => 'Gross → net';

  @override
  String get statGrossToNetSub => 'Last 30 days, after marketplace commission.';

  @override
  String get statBucketToProcess => 'To process';

  @override
  String get statBucketShipped => 'Shipped';

  @override
  String get statBucketDelivered => 'Delivered';

  @override
  String get statBucketCancelled => 'Cancelled';

  @override
  String get statBucketOther => 'Other';

  @override
  String get statOrdersLabel => 'orders';

  @override
  String get statAxisGross => 'Gross';

  @override
  String get statAxisNet => 'Net';

  @override
  String get statCommissionTaken => 'Commission taken';

  @override
  String get condNew => 'New';

  @override
  String get condUsed => 'Used';

  @override
  String get condRefurbished => 'Refurbished';

  @override
  String get payoutMtn => 'MTN Mobile Money (Benin)';

  @override
  String get payoutMoov => 'Moov Money (Benin)';

  @override
  String get payoutCeltis => 'Celtis Cash (Benin)';

  @override
  String get kybBusinessRegistry => 'Business registry';

  @override
  String get kybIdCard => 'ID card';

  @override
  String get kybTaxId => 'Tax ID';

  @override
  String get kybProofOfAddress => 'Proof of address';

  @override
  String get kybFallbackDocument => 'Document';

  @override
  String get ageJustNow => 'Just now';

  @override
  String ageMinutes(int n) {
    return '$n min ago';
  }

  @override
  String ageHours(int n) {
    return '$n h ago';
  }

  @override
  String ageDays(int n) {
    return '$n d ago';
  }

  @override
  String get carrierLoading => 'Loading carriers…';

  @override
  String get carrierSelectHint => 'Choose a carrier';

  @override
  String get carrierOther => 'Other (specify)';

  @override
  String get carrierOtherLabel => 'Carrier name';

  @override
  String get shpTrackingOpenError => 'Couldn\'t open the tracking link.';

  @override
  String get commonFeatureUnavailable =>
      'This feature isn\'t available on the server yet.';

  @override
  String commonNoResultsFor(String query) {
    return 'No results for “$query”.';
  }

  @override
  String get pillPaid => 'Paid';

  @override
  String get pillPayoutProcessing => 'Payout in progress';

  @override
  String get pillPending => 'Pending';

  @override
  String get pillRejectedM => 'Rejected';

  @override
  String get pillFailed => 'Failed';

  @override
  String get pillProcessing => 'Processing';

  @override
  String get pillDelivered => 'Delivered';

  @override
  String get pillShipped => 'Shipped';

  @override
  String get pillToProcess => 'To process';

  @override
  String get pillCancelled => 'Cancelled';

  @override
  String get pillRefunded => 'Refunded';

  @override
  String get pillPrepared => 'Prepared';

  @override
  String get pillToPrepare => 'To prepare';

  @override
  String get pillOnline => 'Online';

  @override
  String get pillPaused => 'Paused';

  @override
  String get pillDraft => 'Draft';

  @override
  String get pillArchived => 'Archived';

  @override
  String get pillVerified => 'Verified';

  @override
  String get pillRejectedF => 'Rejected';

  @override
  String get pillInReview => 'Under review';

  @override
  String get pillToComplete => 'To complete';

  @override
  String get commonSeeAll => 'SEE ALL';

  @override
  String get legalFrenchNote =>
      'This document is provided in French, its legally binding language. An English translation is not available.';

  @override
  String get ordShipmentSection => 'Shipment';

  @override
  String get ordNoShipmentYet =>
      'The shipment is created automatically once payment is captured. If it hasn\'t appeared yet, wait a moment and refresh.';

  @override
  String get commonDescription => 'Description';

  @override
  String get commonSku => 'SKU';

  @override
  String get commonStock => 'Stock';

  @override
  String get routeNotFoundTitle => 'Page not found';

  @override
  String get routeBackHome => 'Back to home';

  @override
  String get updTitle => 'Update required';

  @override
  String get updBody =>
      'This version of the app is no longer supported. Please update to keep managing your shop.';

  @override
  String get updButton => 'Update';

  @override
  String get updAlreadyUpdated => 'I\'ve already updated';

  @override
  String get updNoStoreLink =>
      'Update link unavailable. Search for \"HbaExpress PRO\" in your store.';

  @override
  String get notifprefDeviceTitle => 'This device';

  @override
  String get notifprefDeviceHint =>
      'If you\'re not receiving notifications even though they\'re allowed, re-enable them here to re-register this device.';

  @override
  String get notifprefDeviceButton => 'Enable notifications on this device';

  @override
  String get notifprefDeviceEnabling => 'Enabling…';

  @override
  String get notifprefDeviceRegistered =>
      'This device is registered for notifications.';

  @override
  String get notifprefDevicePermissionDenied =>
      'Notifications denied. Enable them in your phone\'s Settings, then try again.';

  @override
  String get notifprefDeviceApnsUnavailable =>
      'Couldn\'t get the notification token right now. Try again in a moment, with a good connection, on a real device.';

  @override
  String get notifprefDeviceError =>
      'Enabling failed. Check your connection and try again.';

  @override
  String get authLoginBrandKicker => 'HBA PARTNER';

  @override
  String get authLoginTitle => 'Run your business with HBA';

  @override
  String get navActivities => 'Activities';

  @override
  String get navFinance => 'Finances';

  @override
  String get navAccount => 'Account';
}
