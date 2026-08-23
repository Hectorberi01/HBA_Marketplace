import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:hba_express_pro/l10n/app_localizations.dart';

import '../../../core/config/app_config.dart';
import '../../../core/network/api_exception.dart';
import '../../../core/theme/app_theme.dart';
import '../../../shared/utils/formatters.dart';
import '../../../shared/widgets/app_notify.dart';
import '../../../shared/widgets/ui_kit.dart';
import '../../activities/activities_data.dart';
import '../../catalog/catalog_data.dart';
import '../offers_data.dart';

/// Feuilles de saisie d'une mise en vente, PARTAGÉES entre l'écran Mises en vente et la fiche
/// produit.
///
/// Le calcul du prix (net vendeur → prix acheteur) n'existe qu'ici : le dupliquer
/// pour la fiche produit garantirait qu'un jour les deux divergent, et le vendeur
/// verrait deux prix différents pour la même mise en vente.
class OfferSheets {
  const OfferSheets._();

  /// Création. Depuis la fiche produit, [productId] est imposé et [variants]
  /// porte ses déclinaisons.
  ///
  /// DES DÉCLINAISONS, PLUS DES SKU — ET LA SAISIE LIBRE A DISPARU.
  ///
  /// L'ancienne feuille laissait taper un SKU à la main quand le produit était
  /// choisi dans la liste. catalog-service n'accepte plus qu'un `variantId` :
  /// c'est la correction de sécurité de la phase 3 (mettre en vente le produit A
  /// avec le SKU du produit B faisait décrémenter le mauvais stock). Un champ
  /// libre n'a donc plus rien à envoyer, et le rétablir supposerait de rouvrir
  /// la faille.
  static void create(
    BuildContext context, {
    String? productId,
    List<ProductVariant> variants = const [],
  }) =>
      _show(context, _CreateOfferSheet(fixedProductId: productId, fixedVariants: variants));

  static void changePrice(BuildContext context, Offer offer) =>
      _show(context, _ChangePriceSheet(offer: offer));

  /// Actions de menu d'une mise en vente (prix, pause, réactivation), au même endroit
  /// pour les deux écrans.
  static Future<void> runAction(
    BuildContext context,
    WidgetRef ref,
    Offer offer,
    String action,
  ) async {
    final l = AppLocalizations.of(context);
    if (action == 'price') {
      changePrice(context, offer);
      return;
    }

    if (action == 'discount') {
      _show(context, _DiscountSheet(offer: offer));
      return;
    }

    if (action == 'discount_remove') {
      try {
        await ref.read(offersApiProvider).removeDiscount(offer.id);
        ref.invalidate(offersProvider);
        if (context.mounted) AppNotify.success(context, l.offSheetDiscountRemoved);
      } catch (e) {
        if (context.mounted) AppNotify.error(context, e.toString());
      }
      return;
    }

    try {
      await ref.read(offersApiProvider).changeStatus(offer.id, action);
      ref.invalidate(offersProvider);
      if (context.mounted) {
        AppNotify.success(
          context,
          action == 'active' ? l.offSheetBackOnSale : l.offSheetSalePaused,
        );
      }
    } catch (e) {
      if (context.mounted) AppNotify.error(context, e.toString());
    }
  }

  static void _show(BuildContext context, Widget child) {
    showModalBottomSheet<void>(
      context: context,
      isScrollControlled: true,
      shape: const RoundedRectangleBorder(borderRadius: BorderRadius.vertical(top: Radius.circular(20))),
      builder: (_) => child,
    );
  }
}

/// Aperçu en direct de la décomposition du prix pendant la saisie.
class PricePreview extends StatelessWidget {
  const PricePreview({super.key, required this.sellerPrice, required this.currency});

  final double sellerPrice;
  final String currency;

  @override
  Widget build(BuildContext context) {
    if (sellerPrice <= 0) return const SizedBox.shrink();

    final colors = AppColors.of(context);
    final l = AppLocalizations.of(context);
    return Container(
      padding: const EdgeInsets.all(12),
      decoration: BoxDecoration(
        color: colors.softGreen,
        borderRadius: BorderRadius.circular(12),
      ),
      child: Column(
        children: [
          KeyValueRow(label: l.offSheetYouReceive, value: Format.money(sellerPrice, currency), strong: true),
          KeyValueRow(
              label: l.offSheetCommission, value: Format.money(Pricing.commission(sellerPrice), currency)),
          KeyValueRow(
              label: l.offSheetPaymentFees, value: Format.money(Pricing.providerFee(sellerPrice), currency)),
          const Divider(height: 16),
          KeyValueRow(
            label: l.offSheetDisplayedPrice,
            value: Format.money(Pricing.productPrice(sellerPrice), currency),
            strong: true,
            color: AppTheme.brandGreenDark,
          ),
        ],
      ),
    );
  }
}

class _CreateOfferSheet extends ConsumerStatefulWidget {
  const _CreateOfferSheet({this.fixedProductId, this.fixedVariants = const []});

  /// Non nul quand la mise en vente est créée depuis la fiche d'un produit précis.
  final String? fixedProductId;

  /// Déclinaisons du produit imposé. Vide quand le produit se choisit dans la
  /// liste — elles sont alors lues sur le produit sélectionné.
  final List<ProductVariant> fixedVariants;

  @override
  ConsumerState<_CreateOfferSheet> createState() => _CreateOfferSheetState();
}

class _CreateOfferSheetState extends ConsumerState<_CreateOfferSheet> {
  final _form = GlobalKey<FormState>();
  final _price = TextEditingController();

  String? _productId;
  String? _variantId;
  String? _storeId;
  String? _locationId;
  String _condition = kOfferConditions.first.value; // « Neuf » par défaut
  int _handlingTime = 2;
  bool _saving = false;

  double get _sellerPrice => double.tryParse(_price.text.replaceAll(',', '.')) ?? 0;

  bool get _fromProduct => widget.fixedProductId != null;

  @override
  void initState() {
    super.initState();
    _productId = widget.fixedProductId;
    // Une seule déclinaison : on la présélectionne, il n'y a rien à choisir.
    if (widget.fixedVariants.length == 1) _variantId = widget.fixedVariants.first.id;
  }

  @override
  void dispose() {
    _price.dispose();
    super.dispose();
  }

  /// Les déclinaisons proposées : celles du produit imposé, ou celles du produit
  /// choisi dans la liste.
  ///
  /// AUCUN APPEL SUPPLÉMENTAIRE. `SellerProduct` porte déjà ses `variants` —
  /// aller les rechercher par `GET /products/{id}` à chaque changement de
  /// sélection ferait une requête par frappe sur une donnée déjà en mémoire.
  List<ProductVariant> _variantsOf(List<SellerProduct> products) {
    if (widget.fixedVariants.isNotEmpty) return widget.fixedVariants;
    if (_productId == null) return const [];
    for (final p in products) {
      if (p.id == _productId) return p.variants;
    }
    return const [];
  }

  Future<void> _save() async {
    if (!_form.currentState!.validate()) return;

    final l = AppLocalizations.of(context);
    if (_productId == null) {
      AppNotify.error(context, l.offSheetChooseProduct);
      return;
    }
    if (_variantId == null) {
      AppNotify.error(context, l.offSheetChooseVariant);
      return;
    }
    // RÉSOLU ICI, PAS DANS `build` : écrire l'état pendant la construction
    // de l'arbre est une faute, et un `addPostFrameCallback` qui appelle
    // `setState` reconstruit tout l'écran pour une valeur qu'on connaît déjà.
    final boutiques = (ref.read(activitiesProvider).valueOrNull?.data ?? const <SellerActivity>[])
        .where((a) => a.universe == HbaUniverse.express)
        .toList();
    final storeId = _storeId ?? (boutiques.length == 1 ? boutiques.first.id : null);
    if (storeId == null) {
      // Sans boutique, la route n'existe pas : c'est elle qui porte la garde
      // d'appartenance côté serveur.
      AppNotify.error(context, l.offSheetChooseStore);
      return;
    }
    if (_locationId == null) {
      AppNotify.error(context, l.offSheetChooseLocation);
      return;
    }

    setState(() => _saving = true);
    try {
      await ref.read(offersApiProvider).create(
            productId: _productId!,
            variantId: _variantId!,
            storeId: storeId,
            sellerPrice: _sellerPrice,
            shipFromLocationId: _locationId!,
            condition: _condition,
            handlingTime: _handlingTime,
          );
      ref.invalidate(offersProvider);
      if (mounted) {
        Navigator.of(context).pop();
        AppNotify.success(context, l.offSheetOfferCreated);
      }
    } catch (e) {
      if (!mounted) return;
      if (_isDuplicateOffer(e)) {
        // Une déclinaison ne porte qu'UNE mise en vente (son stock est unique). On explique
        // le bon geste au lieu d'afficher un message d'erreur brut.
        await _showDuplicateHelp(context);
      } else {
        AppNotify.error(context, e.toString());
      }
    } finally {
      if (mounted) setState(() => _saving = false);
    }
  }

  /// Vrai si l'échec vient de la règle « une seule mise en vente par déclinaison ».
  bool _isDuplicateOffer(Object e) {
    if (e is ApiException) {
      return e.code == 'offers.offer.duplicate' ||
          e.statusCode == 409 ||
          e.message.toLowerCase().contains('déjà une mise en vente');
    }
    return false;
  }

  Future<void> _showDuplicateHelp(BuildContext context) => showDialog<void>(
        context: context,
        builder: (dialogContext) {
          final l = AppLocalizations.of(dialogContext);
          return AlertDialog(
            title: Text(l.offSheetDuplicateTitle),
            content: Text(l.offSheetDuplicateBody),
            actions: [
              FilledButton(
                onPressed: () => Navigator.pop(dialogContext),
                child: Text(l.offSheetGotIt),
              ),
            ],
          );
        },
      );

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);
    final l = AppLocalizations.of(context);
    final products = ref.watch(productsProvider);
    final locations = ref.watch(locationsProvider);
    final activities = ref.watch(activitiesProvider);

    return Padding(
      padding: sheetPadding(context),
      child: SingleChildScrollView(
        child: Form(
          key: _form,
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              const SheetHandle(),
              Text(l.offSheetNewOfferTitle,
                  style: TextStyle(fontSize: 18, fontWeight: FontWeight.w800, color: colors.ink)),
              const SizedBox(height: 18),

              // Depuis la fiche produit, le produit est imposé : le laisser
              // modifiable inviterait à créer la mise en vente sur le mauvais article.
              if (!_fromProduct)
                products.when(
                  loading: () => const LinearProgressIndicator(),
                  error: (e, _) => Text(l.offSheetProductsUnavailable(e.toString()),
                      style: const TextStyle(color: AppTheme.danger, fontSize: 12)),
                  data: (list) => AppDropdown<String>(
                    value: _productId,
                    label: l.offSheetProduct,
                    options: [for (final p in list) (value: p.id, label: p.name)],
                    onChanged: (v) => setState(() {
                      _productId = v;
                      // LA DÉCLINAISON NE SURVIT PAS AU CHANGEMENT DE PRODUIT :
                      // elle appartient à l'ancien, et l'envoyer créerait une
                      // offre sur un produit qui ne la porte pas — que le serveur
                      // refuserait, sans que le vendeur comprenne pourquoi.
                      _variantId = null;
                    }),
                  ),
                ),
              if (!_fromProduct) const SizedBox(height: 14),

              // ON VEND UNE DÉCLINAISON, PAS UN PRODUIT.
              //
              // Le libellé montre les attributs (« Taille: 42 · Couleur: Noir »)
              // et non le SKU : c'est ce que le vendeur reconnaît. La référence
              // technique, elle, n'est plus saisie du tout — le serveur la déduit
              // de l'identifiant.
              Builder(builder: (_) {
                final variants = _variantsOf(products.valueOrNull ?? const []);
                if (variants.isEmpty) {
                  return Text(
                    _productId == null
                        ? l.offSheetChooseProductFirst
                        : l.offSheetNoVariants,
                    style: const TextStyle(color: AppTheme.promoOrange, fontSize: 12),
                  );
                }
                return AppDropdown<String>(
                  value: _variantId,
                  label: l.offSheetVariantSku,
                  options: [for (final v in variants) (value: v.id, label: v.label)],
                  onChanged: (v) => setState(() => _variantId = v),
                );
              }),
              const SizedBox(height: 14),

              // LA BOUTIQUE EST UNE DONNÉE DE LA MISE EN VENTE, PAS DU PRODUIT.
              //
              // Un vendeur peut en tenir plusieurs, et l'unicité « une offre par
              // déclinaison » se compte PAR BOUTIQUE : le même article peut donc
              // être mis en vente dans deux boutiques à deux prix. La déduire du
              // produit interdirait ce cas et choisirait au hasard.
              Builder(builder: (_) {
                final boutiques = (activities.valueOrNull?.data ?? const <SellerActivity>[])
                    .where((a) => a.universe == HbaUniverse.express)
                    .toList();
                if (boutiques.isEmpty) {
                  return Text(l.offSheetNoStores,
                      style: const TextStyle(color: AppTheme.promoOrange, fontSize: 12));
                }
                // Une seule boutique : on la NOMME sans offrir un menu à une
                // entrée. Le vendeur doit voir où part sa mise en vente — la
                // taire rendrait le champ invisible le jour où il en ouvre une
                // seconde et où le menu apparaît sans prévenir.
                if (boutiques.length == 1) {
                  return KeyValueRow(label: l.offSheetStore, value: boutiques.first.name);
                }
                return AppDropdown<String>(
                  value: _storeId,
                  label: l.offSheetStore,
                  options: [for (final b in boutiques) (value: b.id, label: b.name)],
                  onChanged: (v) => setState(() => _storeId = v),
                );
              }),
              const SizedBox(height: 14),

              // L'état est une information CONTRACTUELLE : vendre de l'occasion
              // annoncée « Neuf » est le motif de litige le plus courant. Le
              // champ est donc explicite, jamais deviné.
              AppDropdown<String>(
                value: _condition,
                label: l.offSheetCondition,
                options: offerConditionOptions(l),
                onChanged: (v) => setState(() => _condition = v ?? _condition),
              ),
              const SizedBox(height: 6),
              Text(
                l.offSheetConditionHint,
                style: TextStyle(fontSize: 11, color: colors.subtle, height: 1.3),
              ),
              const SizedBox(height: 14),

              TextFormField(
                controller: _price,
                keyboardType: const TextInputType.numberWithOptions(decimal: true),
                decoration: InputDecoration(
                  labelText: l.offSheetYourPrice,
                  suffixText: AppConfig.defaultCurrency,
                ),
                onChanged: (_) => setState(() {}),
                validator: (v) {
                  final value = double.tryParse((v ?? '').replaceAll(',', '.')) ?? 0;
                  return value <= 0 ? l.offSheetInvalidPrice : null;
                },
              ),
              const SizedBox(height: 14),

              PricePreview(sellerPrice: _sellerPrice, currency: AppConfig.defaultCurrency),
              const SizedBox(height: 14),

              locations.when(
                loading: () => const LinearProgressIndicator(),
                error: (e, _) => Text(l.offSheetLocationsUnavailable(e.toString()),
                    style: const TextStyle(color: AppTheme.danger, fontSize: 12)),
                data: (list) => list.isEmpty
                    // Sans lieu d'expédition, la mise en vente est impossible : on le dit
                    // au lieu d'afficher une liste vide sans explication.
                    ? Text(
                        l.offSheetNoLocations,
                        style: const TextStyle(color: AppTheme.promoOrange, fontSize: 12),
                      )
                    : AppDropdown<String>(
                        value: _locationId,
                        label: l.offSheetShipFrom,
                        options: [for (final l in list) (value: l.id, label: l.label)],
                        onChanged: (v) => setState(() => _locationId = v),
                      ),
              ),
              const SizedBox(height: 14),

              AppDropdown<int>(
                value: _handlingTime,
                label: l.offSheetHandlingTime,
                options: [
                  (value: 1, label: l.offSheetDay1),
                  (value: 2, label: l.offSheetDays2),
                  (value: 3, label: l.offSheetDays3),
                  (value: 5, label: l.offSheetDays5),
                ],
                onChanged: (v) => setState(() => _handlingTime = v ?? 2),
              ),
              const SizedBox(height: 22),

              // DÉSACTIVÉ QUAND IL MANQUE UNE DÉCLINAISON.
              //
              // L'avertissement orange au-dessus disait déjà que le produit n'en
              // a aucune, mais le bouton restait offert : le vendeur appuyait, et
              // recevait la MÊME phrase en rouge, en bas de l'écran. Un message
              // qui se répète à l'identique quand on agit n'est pas une
              // explication, c'est une impasse — l'action doit être fermée là où
              // la raison est écrite.
              FilledButton(
                onPressed: (_saving || _variantId == null) ? null : _save,
                child: _saving
                    ? const SizedBox(
                        width: 22,
                        height: 22,
                        child: CircularProgressIndicator(strokeWidth: 2.4, color: Colors.white))
                    : Text(l.offSheetPublish),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

class _ChangePriceSheet extends ConsumerStatefulWidget {
  const _ChangePriceSheet({required this.offer});
  final Offer offer;

  @override
  ConsumerState<_ChangePriceSheet> createState() => _ChangePriceSheetState();
}

class _ChangePriceSheetState extends ConsumerState<_ChangePriceSheet> {
  late final TextEditingController _price =
      TextEditingController(text: widget.offer.sellerPrice.toStringAsFixed(0));
  bool _saving = false;

  double get _sellerPrice => double.tryParse(_price.text.replaceAll(',', '.')) ?? 0;

  @override
  void dispose() {
    _price.dispose();
    super.dispose();
  }

  Future<void> _save() async {
    final l = AppLocalizations.of(context);
    if (_sellerPrice <= 0) {
      AppNotify.error(context, l.offSheetInvalidPriceNotice);
      return;
    }

    setState(() => _saving = true);
    try {
      await ref.read(offersApiProvider).changePrice(widget.offer.id, _sellerPrice, widget.offer.currency);
      ref.invalidate(offersProvider);
      if (mounted) {
        Navigator.of(context).pop();
        AppNotify.success(context, l.offSheetPriceUpdated);
      }
    } catch (e) {
      if (mounted) AppNotify.error(context, e.toString());
    } finally {
      if (mounted) setState(() => _saving = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);
    final l = AppLocalizations.of(context);

    return Padding(
      padding: sheetPadding(context),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          const SheetHandle(),
          Text(widget.offer.productName,
              style: TextStyle(fontSize: 18, fontWeight: FontWeight.w800, color: colors.ink)),
          Text('SKU ${widget.offer.sku}',
              style: TextStyle(fontSize: 12, color: colors.subtle)),
          const SizedBox(height: 18),
          TextField(
            controller: _price,
            keyboardType: const TextInputType.numberWithOptions(decimal: true),
            autofocus: true,
            decoration: InputDecoration(
              labelText: l.offSheetYourPrice,
              suffixText: widget.offer.currency,
            ),
            onChanged: (_) => setState(() {}),
          ),
          const SizedBox(height: 14),
          PricePreview(sellerPrice: _sellerPrice, currency: widget.offer.currency),
          const SizedBox(height: 22),
          FilledButton(
            onPressed: _saving ? null : _save,
            child: _saving
                ? const SizedBox(
                    width: 22,
                    height: 22,
                    child: CircularProgressIndicator(strokeWidth: 2.4, color: Colors.white))
                : Text(l.offSheetSave),
          ),
        ],
      ),
    );
  }
}

/// Feuille d'application d'une remise (promo vendeur) sur une mise en vente.
class _DiscountSheet extends ConsumerStatefulWidget {
  const _DiscountSheet({required this.offer});
  final Offer offer;

  @override
  ConsumerState<_DiscountSheet> createState() => _DiscountSheetState();
}

class _DiscountSheetState extends ConsumerState<_DiscountSheet> {
  String _type = 'Percentage';
  final _value = TextEditingController();
  DateTime? _endsOn;
  bool _saving = false;

  double get _v => double.tryParse(_value.text.replaceAll(',', '.')) ?? 0;

  /// Prix net vendeur après remise (aperçu). Le backend recalcule et fait foi.
  double get _discountedSellerPrice {
    final base = widget.offer.sellerPrice;
    return _type == 'Percentage' ? base * (1 - _v / 100) : base - _v;
  }

  bool get _valid {
    if (_v <= 0) return false;
    if (_type == 'Percentage' && _v >= 100) return false;
    final d = _discountedSellerPrice;
    return d > 0 && d < widget.offer.sellerPrice;
  }

  @override
  void dispose() {
    _value.dispose();
    super.dispose();
  }

  Future<void> _pickDate() async {
    final now = DateTime.now();
    final picked = await showDatePicker(
      context: context,
      firstDate: now.add(const Duration(days: 1)),
      lastDate: now.add(const Duration(days: 365)),
      initialDate: _endsOn ?? now.add(const Duration(days: 7)),
    );
    if (picked != null) setState(() => _endsOn = picked);
  }

  Future<void> _save() async {
    final l = AppLocalizations.of(context);
    if (!_valid) {
      AppNotify.error(context, l.offSheetInvalidDiscount);
      return;
    }
    setState(() => _saving = true);
    try {
      await ref.read(offersApiProvider).applyDiscount(
            widget.offer.id,
            type: _type,
            value: _v,
            endsOn: _endsOn,
            // Le net vendeur COURANT : c'est la base sur laquelle la feuille a
            // calculé son aperçu, et le serveur attend le résultat, pas la règle.
            sellerPrice: widget.offer.sellerPrice,
          );
      ref.invalidate(offersProvider);
      if (mounted) {
        Navigator.pop(context);
        AppNotify.success(context, l.offSheetDiscountApplied);
      }
    } catch (e) {
      if (mounted) AppNotify.error(context, e.toString());
    } finally {
      if (mounted) setState(() => _saving = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);
    final l = AppLocalizations.of(context);
    return Padding(
      padding: sheetPadding(context),
      child: SingleChildScrollView(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            const SheetHandle(),
            Text(l.offSheetApplyDiscountTitle,
                style: TextStyle(fontSize: 18, fontWeight: FontWeight.w800, color: colors.ink)),
            const SizedBox(height: 6),
            Text(
              l.offSheetCurrentPriceNote(Format.money(widget.offer.productPrice, widget.offer.currency)),
              style: TextStyle(fontSize: 12, color: colors.subtle, height: 1.4),
            ),
            const SizedBox(height: 18),
            AppDropdown<String>(
              value: _type,
              label: l.offSheetDiscountType,
              options: [
                (value: 'Percentage', label: l.offSheetPercentage),
                (value: 'Amount', label: l.offSheetFixedAmount),
              ],
              onChanged: (v) => setState(() => _type = v ?? 'Percentage'),
            ),
            const SizedBox(height: 14),
            TextField(
              controller: _value,
              keyboardType: const TextInputType.numberWithOptions(decimal: true),
              onChanged: (_) => setState(() {}),
              decoration: InputDecoration(
                labelText: _type == 'Percentage' ? l.offSheetPercentageLabel : l.offSheetAmountLabel,
                suffixText: _type == 'Percentage' ? '%' : widget.offer.currency,
              ),
            ),
            const SizedBox(height: 14),
            OutlinedButton.icon(
              onPressed: _pickDate,
              icon: const Icon(Icons.event_outlined),
              label: Text(_endsOn == null
                  ? l.offSheetPromoEndOptional
                  : l.offSheetUntil('${_endsOn!.day}/${_endsOn!.month}/${_endsOn!.year}')),
            ),
            if (_endsOn != null)
              Align(
                alignment: Alignment.centerRight,
                child: TextButton(
                  onPressed: () => setState(() => _endsOn = null),
                  child: Text(l.offSheetNoEndDate),
                ),
              ),
            const SizedBox(height: 14),
            if (_valid) PricePreview(sellerPrice: _discountedSellerPrice, currency: widget.offer.currency),
            const SizedBox(height: 22),
            FilledButton(
              onPressed: _saving ? null : _save,
              child: _saving
                  ? const SizedBox(
                      width: 22,
                      height: 22,
                      child: CircularProgressIndicator(strokeWidth: 2.4, color: Colors.white))
                  : Text(l.offSheetApplyDiscountBtn),
            ),
          ],
        ),
      ),
    );
  }
}
