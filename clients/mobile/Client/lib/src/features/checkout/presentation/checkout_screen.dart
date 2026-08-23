import 'dart:math';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../../core/theme/app_theme.dart';
import '../../../shared/utils/formatters.dart';
import '../../../shared/widgets/app_notify.dart';
import '../../../shared/widgets/ui_kit.dart';
import '../../account/address_data.dart';
// DÉSACTIVÉ — moyens de paiement enregistrés (voir le bandeau ci-dessous).
// import '../../account/payment_method_data.dart';
import '../../cart/cart_data.dart';
import '../checkout_data.dart';
import 'payment_webview_screen.dart';

// ---------------------------------------------------------------------------
// PAIEMENT : UN SEUL MOYEN ACTIF — LA PAGE HÉBERGÉE FEDAPAY
// ---------------------------------------------------------------------------
//
// Tout le reste (Mobile Money en saisie directe, Carte Bancaire, moyens
// enregistrés) est MIS EN COMMENTAIRE, pas supprimé : le code est intact et
// prêt à revivre. Chaque bloc neutralisé porte le marqueur « DÉSACTIVÉ ».
//
// La raison n'est pas cosmétique. FedaPay est de toute façon le prestataire qui
// exécute la transaction dans les trois cas : « Mobile Money » et « Carte
// Bancaire » n'étaient pas des circuits concurrents, seulement deux portes
// différentes vers la même page FedaPay. Les exposer séparément demandait à
// l'acheteur de choisir son opérateur et de retaper son numéro — deux occasions
// de se tromper — pour aboutir à l'écran qui, précisément, lui reposait la
// question. La page hébergée FedaPay présente MTN, Moov, Wave et la carte,
// à jour, sans que nous ayons à maintenir cette liste.
//
// Pour tout réactiver : décommenter l'import ci-dessus, l'enum `_PayGroup`, les
// champs d'état, `_manualMethod`, `_providerToPayMethod`, le corps d'origine de
// `_effective`, la section « 3. Mode de paiement » de `build`, et les quatre
// widgets en fin de fichier.
// ---------------------------------------------------------------------------

// DÉSACTIVÉ — sélection du groupe de paiement.
// enum _PayGroup { mobile, card, fedapay }

class CheckoutScreen extends ConsumerStatefulWidget {
  const CheckoutScreen({super.key});

  @override
  ConsumerState<CheckoutScreen> createState() => _CheckoutScreenState();
}

class _CheckoutScreenState extends ConsumerState<CheckoutScreen> {
  // DÉSACTIVÉ — état des moyens de paiement alternatifs.
  // _PayGroup _group = _PayGroup.mobile;
  // PayMethod _mobileProvider = PayMethod.mtnMomo;
  // final _phone = TextEditingController();
  // String? _selectedSavedId; // null => saisie manuelle

  String _shippingCode = 'standard';
  bool _paying = false;
  String? _error;

  /// Clé d'idempotence de la tentative d'achat en cours. Voir `_pay`.
  String? _idempotencyKey;

  /// Identifiant unique par tentative d'achat.
  ///
  /// Horodatage en microsecondes + aléa : suffisant pour distinguer deux achats
  /// d'un même appareil, sans ajouter la dépendance `uuid` au projet pour une
  /// seule chaîne. La clé n'a pas besoin d'être imprévisible — juste unique.
  static String _newIdempotencyKey() {
    final micros = DateTime.now().microsecondsSinceEpoch.toRadixString(16);
    final noise = Random().nextInt(0xFFFFFF).toRadixString(16).padLeft(6, '0');
    return '$micros-$noise';
  }

  static const _fallbackOptions = [
    ShippingOption(code: 'standard', label: 'Standard', eta: 'Livraison sous 24h à 48h', amount: 1500, currency: 'XOF'),
    ShippingOption(code: 'express', label: 'Express', eta: 'Livraison en moins de 3h', amount: 3000, currency: 'XOF'),
  ];

  // DÉSACTIVÉ — dérivation de la méthode depuis le groupe choisi.
  // PayMethod get _manualMethod => switch (_group) {
  //       _PayGroup.card => PayMethod.card,
  //       _PayGroup.fedapay => PayMethod.fedapay,
  //       _PayGroup.mobile => _mobileProvider,
  //     };
  //
  // @override
  // void dispose() {
  //   _phone.dispose();
  //   super.dispose();
  // }
  //
  // PayMethod _providerToPayMethod(String provider) {
  //   final p = provider.toLowerCase();
  //   if (p.contains('moov')) return PayMethod.moovMoney;
  //   if (p.contains('wave')) return PayMethod.wave;
  //   return PayMethod.mtnMomo;
  // }
  //
  // /// Méthode + téléphone effectifs (moyen enregistré ou saisie manuelle).
  // ({PayMethod method, String? phone}) _effective(List<PaymentMethod> saved) {
  //   if (_selectedSavedId != null) {
  //     for (final m in saved) {
  //       if (m.id == _selectedSavedId) {
  //         if (m.isCard) return (method: PayMethod.card, phone: null);
  //         return (method: _providerToPayMethod(m.provider), phone: m.display);
  //       }
  //     }
  //   }
  //   return (method: _manualMethod, phone: _manualMethod.requiresPhone ? _phone.text.trim() : null);
  // }

  /// Méthode effective : FedaPay, toujours.
  ///
  /// Aucun numéro n'est demandé ici — c'est la page hébergée qui le collecte,
  /// et elle le fait mieux : elle connaît le format attendu par chaque
  /// opérateur, nous non.
  ({PayMethod method, String? phone}) _effective() =>
      (method: PayMethod.fedapay, phone: null);

  Future<void> _pay() async {
    final address = ref.read(checkoutAddressProvider);
    if (address == null) {
      setState(() => _error = 'Veuillez choisir une adresse de livraison avant de payer.');
      return;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ADRESSE INCOMPLÈTE : ON ARRÊTE AVANT LE PAIEMENT.
    //
    // Le serveur refuse déjà ces adresses — ce contrôle ne le remplace pas, il
    // évite un aller-retour qui se solderait par une erreur technique après que
    // l'acheteur a cru payer. Ici, le message dit quoi corriger et l'écran des
    // adresses est à un geste.
    //
    // Ne concerne que les adresses enregistrées AVANT la refonte : sans commune
    // normalisée, sans point de repère, parfois sans téléphone. Un coursier ne
    // les trouve pas.
    // ─────────────────────────────────────────────────────────────────────────
    if (!address.isComplete) {
      setState(() => _error = 'Cette adresse est incomplète : ajoutez la commune, '
          'un point de repère et un numéro de téléphone avant de commander.');
      return;
    }
    final eff = _effective();

    // ─────────────────────────────────────────────────────────────────────────
    // LA CLÉ EST GÉNÉRÉE UNE FOIS, PUIS RÉUTILISÉE À CHAQUE NOUVELLE TENTATIVE.
    //
    // C'est tout l'intérêt : si le premier appel expire alors que le serveur
    // l'avait bien reçu, la seconde tentative porte LA MÊME clé et sera reconnue
    // comme un doublon plutôt que traitée comme un nouvel achat.
    //
    // Elle n'est remise à zéro qu'après un paiement abouti — voir plus bas.
    // ─────────────────────────────────────────────────────────────────────────
    _idempotencyKey ??= _newIdempotencyKey();

    setState(() {
      _paying = true;
      _error = null;
    });
    try {
      final result = await ref.read(checkoutApiProvider).pay(
            method: eff.method,
            idempotencyKey: _idempotencyKey!,
            payerPhone: eff.method.requiresPhone ? eff.phone : null,
            addressId: address.id,
            shippingCode: _shippingCode,
            returnUrl: 'marketplace://payment/return',
            cancelUrl: 'marketplace://payment/cancel',
          );

      // Commande créée : l'achat suivant devra porter une clé neuve.
      _idempotencyKey = null;

      ref.invalidate(cartControllerProvider);
      if (!mounted) return;
      if (result.requiresAction && eff.method.isHosted && result.redirectUrl != null && result.redirectUrl!.isNotEmpty) {
        // Page hébergée (FedaPay / carte) : on ouvre la WebView. Elle se FERME
        // TOUTE SEULE dès que le serveur voit le paiement terminé (sondage), puis
        // on confirme le statut au retour (le serveur fait foi).
        await openPaymentWebView(
          context,
          result.redirectUrl!,
          onPoll: result.paymentId.isEmpty
              ? null
              : () async {
                  try {
                    final s = await ref.read(checkoutApiProvider).status(result.paymentId);
                    return _isTerminal(s);
                  } catch (_) {
                    return false; // on ne ferme pas sur une erreur réseau
                  }
                },
        );
        if (mounted && result.paymentId.isNotEmpty) {
          await ref.read(checkoutApiProvider).confirmRedirect(result.paymentId);
          if (mounted) await _confirmPayment(result.paymentId, eff.method);
        }
      } else if (result.requiresAction) {
        await _showPendingDialog(result, eff.method);
      } else if (result.paymentId.isNotEmpty) {
        await _confirmPayment(result.paymentId, eff.method);
      }
      if (mounted) {
        if (result.orderId.isNotEmpty) {
          context.go('/order/${result.orderId}/confirmation');
        } else {
          context.go('/orders');
        }
      }
    } catch (e) {
      setState(() => _error = e.toString());
    } finally {
      if (mounted) setState(() => _paying = false);
    }
  }

  Future<void> _confirmPayment(String paymentId, PayMethod method) async {
    final navigator = Navigator.of(context);
    showDialog<void>(
      context: context,
      barrierDismissible: false,
      builder: (_) => const AlertDialog(
        title: Text('Validation du paiement'),
        content: Row(children: [
          SizedBox(width: 22, height: 22, child: CircularProgressIndicator(strokeWidth: 2)),
          SizedBox(width: 16),
          Expanded(child: Text('Validez la demande sur votre téléphone…')),
        ]),
      ),
    );
    var status = 'Pending';
    for (var i = 0; i < 8; i++) {
      await Future.delayed(const Duration(seconds: 2));
      try {
        status = await ref.read(checkoutApiProvider).status(paymentId);
      } catch (_) {}
      if (_isTerminal(status)) break;
    }
    if (mounted) navigator.pop();
    if (!mounted) return;
    final ok = _isSuccess(status);
    // Passé sur AppNotify comme le reste de l'app : ce message arrivait en bas,
    // par-dessus le bouton « Payer », au moment précis où l'on quitte l'écran
    // pour la confirmation de commande.
    final message = ok
        ? 'Paiement confirmé.'
        : status.toLowerCase() == 'failed'
            ? 'Paiement échoué.'
            : 'Paiement en attente de confirmation.';
    if (ok) {
      AppNotify.success(context, message);
    } else {
      AppNotify.error(context, message);
    }
  }

  bool _isTerminal(String s) =>
      const ['captured', 'succeeded', 'paid', 'completed', 'failed', 'cancelled', 'declined'].contains(s.toLowerCase());
  bool _isSuccess(String s) => const ['captured', 'succeeded', 'paid', 'completed'].contains(s.toLowerCase());

  Future<void> _showPendingDialog(PaymentResult result, PayMethod method) async {
    await showDialog<void>(
      context: context,
      builder: (_) => AlertDialog(
        title: const Text('Confirmez le paiement'),
        content: Text(method.requiresPhone
            ? 'Une demande de paiement a été envoyée sur votre téléphone. Validez-la avec votre code ${method.label}.'
            : 'Finalisez le paiement via le lien sécurisé :\n${result.redirectUrl}'),
        actions: [TextButton(onPressed: () => Navigator.pop(context), child: const Text('J’ai compris'))],
      ),
    );
  }

  void _openAddressPicker(List<Address> addresses) {
    showModalBottomSheet(
      context: context,
      backgroundColor: AppTheme.surface,
      shape: const RoundedRectangleBorder(borderRadius: BorderRadius.vertical(top: Radius.circular(20))),
      builder: (_) => _AddressPickerSheet(
        addresses: addresses,
        selectedId: ref.read(checkoutAddressProvider)?.id,
        onPick: (id) {
          ref.read(selectedCheckoutAddressIdProvider.notifier).state = id;
          setState(() => _error = null);
          Navigator.pop(context);
        },
        onAdd: () {
          Navigator.pop(context);
          context.push('/account/addresses');
        },
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    final cart = ref.watch(cartControllerProvider);
    // DEVIS SERVEUR pour l'option choisie : sous-total, frais de port et TOTAL
    // réels (= ce qui sera débité). Repli sur le calcul client si indisponible.
    final quote = ref.watch(checkoutQuoteProvider(_shippingCode)).valueOrNull;
    final currency = quote?.currency ?? cart.valueOrNull?.currency ?? 'XOF';
    final count = cart.valueOrNull?.itemCount ?? 0;
    final options = (quote != null && quote.options.isNotEmpty)
        ? quote.options
        : (ref.watch(shippingOptionsProvider).valueOrNull ?? _fallbackOptions);
    final selectedShipping = options.firstWhere((o) => o.code == _shippingCode, orElse: () => options.first);
    final subtotal = quote?.subtotal ?? cart.valueOrNull?.total ?? 0;
    final shippingAmount = quote?.shippingAmount ?? selectedShipping.amount;
    final total = quote?.total ?? (subtotal + shippingAmount);
    final addresses = ref.watch(addressControllerProvider).valueOrNull ?? const [];
    final address = ref.watch(checkoutAddressProvider);
    // DÉSACTIVÉ — moyens de paiement enregistrés.
    // final savedMethods = ref.watch(paymentMethodControllerProvider).valueOrNull ?? const [];

    return Scaffold(
      backgroundColor: AppTheme.bg,
      appBar: AppBar(
        title: const Text('Finaliser l’achat'),
        actions: [
          Center(child: Text('Étape 2/2', style: TextStyle(color: AppTheme.subtle, fontSize: 13))),
          const SizedBox(width: 16),
        ],
      ),
      body: SafeArea(
        top: false,
        child: ListView(
          padding: const EdgeInsets.fromLTRB(16, 12, 16, 16),
          children: [
            _StepLabel('1', 'Adresse de livraison',
                trailing: address == null ? 'Ajouter' : 'Changer',
                onTrailing: () => addresses.isEmpty ? context.push('/account/addresses') : _openAddressPicker(addresses)),
            const SizedBox(height: 8),
            _AddressCard(address: address, onTap: () => addresses.isEmpty ? context.push('/account/addresses') : _openAddressPicker(addresses)),
            const _StepLabel('2', 'Mode de livraison'),
            const SizedBox(height: 8),
            for (var i = 0; i < options.length; i++) ...[
              _ShippingOption(
                selected: selectedShipping.code == options[i].code,
                title: options[i].label,
                subtitle: options[i].eta,
                price: Format.money(options[i].amount, currency),
                onTap: () => setState(() => _shippingCode = options[i].code),
              ),
              if (i < options.length - 1) const SizedBox(height: 10),
            ],
            const _StepLabel('3', 'Mode de paiement'),
            const SizedBox(height: 8),

            // Un seul moyen : la page hébergée FedaPay. La tuile reste affichée
            // (et non remplacée par du vide) pour que l'acheteur SACHE par où
            // passe son argent avant d'appuyer sur « Payer ».
            //
            // `selected: true` en dur, `onSelect` sans effet : il n'y a rien à
            // choisir. Un bouton radio qui ne peut pas être désélectionné est
            // honnête ; une case à cocher qui ne fait rien ne l'est pas.
            const _PaymentFedaPay(selected: true, onSelect: null),

            // DÉSACTIVÉ — moyens enregistrés + « Utiliser un autre moyen ».
            // if (savedMethods.isNotEmpty) ...[
            //   for (final m in savedMethods) ...[
            //     _SavedMethodTile(
            //       method: m,
            //       selected: _selectedSavedId == m.id,
            //       onTap: () => setState(() {
            //         _selectedSavedId = m.id;
            //         _error = null;
            //       }),
            //     ),
            //     const SizedBox(height: 10),
            //   ],
            //   _NewMethodTile(
            //     selected: _selectedSavedId == null,
            //     onTap: () => setState(() => _selectedSavedId = null),
            //   ),
            //   const SizedBox(height: 10),
            // ],
            //
            // DÉSACTIVÉ — Mobile Money en saisie directe + Carte Bancaire.
            // if (_selectedSavedId == null) ...[
            //   _PaymentMobile(
            //     selected: _group == _PayGroup.mobile,
            //     provider: _mobileProvider,
            //     phone: _phone,
            //     onSelect: () => setState(() => _group = _PayGroup.mobile),
            //     onProvider: (p) => setState(() => _mobileProvider = p),
            //   ),
            //   const SizedBox(height: 10),
            //   _PaymentCard(
            //     selected: _group == _PayGroup.card,
            //     onSelect: () => setState(() => _group = _PayGroup.card),
            //   ),
            //   const SizedBox(height: 10),
            //   _PaymentFedaPay(
            //     selected: _group == _PayGroup.fedapay,
            //     onSelect: () => setState(() => _group = _PayGroup.fedapay),
            //   ),
            // ],

            const SizedBox(height: 18),
            _SummaryDark(count: count, subtotal: subtotal, shipping: shippingAmount, total: total, currency: currency, shippingLabel: selectedShipping.label),
            if (_error != null) ...[
              const SizedBox(height: 12),
              Row(children: [
                const Icon(Icons.error_outline, color: AppTheme.danger, size: 18),
                const SizedBox(width: 6),
                Expanded(child: Text(_error!, style: const TextStyle(color: AppTheme.danger))),
              ]),
            ],
            const SizedBox(height: 18),
            Center(
              child: Row(mainAxisAlignment: MainAxisAlignment.center, children: [
                Icon(Icons.lock_outline, size: 14, color: AppTheme.subtle),
                const SizedBox(width: 6),
                Text('Paiement 100% sécurisé et crypté', style: TextStyle(color: AppTheme.subtle, fontSize: 12)),
              ]),
            ),
          ],
        ),
      ),
      bottomNavigationBar: SafeArea(
        child: Padding(
          padding: const EdgeInsets.fromLTRB(16, 6, 16, 10),
          child: Column(mainAxisSize: MainAxisSize.min, children: [
            FilledButton.icon(
              onPressed: _paying ? null : _pay,
              style: FilledButton.styleFrom(minimumSize: const Size.fromHeight(54)),
              icon: _paying
                  ? const SizedBox(height: 18, width: 18, child: CircularProgressIndicator(strokeWidth: 2, color: Colors.white))
                  : const Icon(Icons.shield_outlined, size: 18),
              label: Text(_paying ? 'Traitement…' : 'Payer ${Format.money(total, currency)}'),
            ),
            const SizedBox(height: 6),
            // La mention renvoie désormais au texte qu'elle fait accepter. Elle
            // l'affirmait sans donner aucun moyen de le lire.
            TextButton(
              onPressed: () => context.push('/terms'),
              style: TextButton.styleFrom(
                foregroundColor: AppTheme.subtle,
                minimumSize: const Size(0, 32),
                padding: const EdgeInsets.symmetric(horizontal: 8),
              ),
              child: const Text(
                'EN PAYANT, VOUS ACCEPTEZ LES CGV',
                style: TextStyle(
                  fontSize: 10,
                  fontWeight: FontWeight.w700,
                  letterSpacing: 0.4,
                  decoration: TextDecoration.underline,
                ),
              ),
            ),
          ]),
        ),
      ),
    );
  }
}

class _AddressCard extends StatelessWidget {
  const _AddressCard({required this.address, required this.onTap});
  final Address? address;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    return CardSection(
      margin: EdgeInsets.zero,
      padding: const EdgeInsets.all(14),
      child: InkWell(
        onTap: onTap,
        child: Row(crossAxisAlignment: CrossAxisAlignment.start, children: [
          Container(
            width: 36, height: 36,
            decoration: BoxDecoration(color: AppTheme.softGreen, borderRadius: BorderRadius.circular(10)),
            child: const Icon(Icons.location_on_outlined, color: AppTheme.brandGreen, size: 20),
          ),
          const SizedBox(width: 12),
          Expanded(
            child: address == null
                ? Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                    const Text('Aucune adresse', style: TextStyle(fontWeight: FontWeight.w800)),
                    const SizedBox(height: 2),
                    Text('Touchez « Ajouter » pour enregistrer une adresse de livraison.',
                        style: TextStyle(color: AppTheme.subtle, fontSize: 13, height: 1.3)),
                  ])
                : Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                    Text(address!.label, style: const TextStyle(fontWeight: FontWeight.w800)),
                    const SizedBox(height: 2),
                    if (address!.recipient.isNotEmpty)
                      Text(address!.recipient, style: TextStyle(color: AppTheme.subtle, fontSize: 13)),
                    Text(address!.summary, style: TextStyle(color: AppTheme.subtle, fontSize: 13, height: 1.3)),
                    if (address!.phone.isNotEmpty)
                      Text(address!.phone, style: TextStyle(color: AppTheme.subtle, fontSize: 13)),
                    // Le signaler ICI aussi : c'est l'écran où l'acheteur décide.
                    if (!address!.isComplete) ...[
                      const SizedBox(height: 4),
                      const Row(children: [
                        Icon(Icons.error_outline, size: 14, color: AppTheme.danger),
                        SizedBox(width: 4),
                        Expanded(
                          child: Text('Adresse incomplète — à compléter pour commander.',
                              style: TextStyle(color: AppTheme.danger, fontSize: 12, height: 1.3)),
                        ),
                      ]),
                    ],
                  ]),
          ),
          Icon(Icons.chevron_right, color: AppTheme.subtle),
        ]),
      ),
    );
  }
}

class _AddressPickerSheet extends StatelessWidget {
  const _AddressPickerSheet({required this.addresses, required this.selectedId, required this.onPick, required this.onAdd});
  final List<Address> addresses;
  final String? selectedId;
  final ValueChanged<String> onPick;
  final VoidCallback onAdd;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: EdgeInsets.fromLTRB(16, 16, 16, sheetBottomInset(context)),
      child: Column(mainAxisSize: MainAxisSize.min, crossAxisAlignment: CrossAxisAlignment.stretch, children: [
        const Text('Choisir une adresse', style: TextStyle(fontSize: 18, fontWeight: FontWeight.w800)),
        const SizedBox(height: 12),
        for (final a in addresses)
          Padding(
            padding: const EdgeInsets.only(bottom: 10),
            // ── UNE ADRESSE INCOMPLÈTE N'EST PAS SÉLECTIONNABLE ────────────────
            //
            // Le paiement la refuse déjà. La laisser choisissable ici reviendrait à
            // faire découvrir le refus au dernier écran, après que l'acheteur a cru
            // avoir fini. On l'affiche — la masquer serait pire, il ne comprendrait
            // pas où est passée son adresse — mais grisée, avec la raison et de quoi
            // la corriger.
            child: GestureDetector(
              onTap: a.isComplete ? () => onPick(a.id) : null,
              child: Opacity(
                opacity: a.isComplete ? 1 : 0.6,
                child: Container(
                  padding: const EdgeInsets.all(12),
                  decoration: BoxDecoration(
                    color: AppTheme.surface,
                    borderRadius: BorderRadius.circular(12),
                    border: Border.all(
                      color: !a.isComplete
                          ? AppTheme.danger
                          : a.id == selectedId
                              ? AppTheme.brandGreen
                              : AppTheme.line,
                      width: a.id == selectedId ? 1.6 : 1,
                    ),
                  ),
                  child: Row(children: [
                    Icon(
                      !a.isComplete
                          ? Icons.error_outline
                          : a.id == selectedId
                              ? Icons.radio_button_checked
                              : Icons.radio_button_off,
                      color: !a.isComplete
                          ? AppTheme.danger
                          : a.id == selectedId
                              ? AppTheme.brandGreen
                              : AppTheme.subtle,
                    ),
                    const SizedBox(width: 10),
                    Expanded(
                      child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                        Text(a.label, style: const TextStyle(fontWeight: FontWeight.w800)),
                        Text(a.summary, style: TextStyle(color: AppTheme.subtle, fontSize: 13)),
                        if (!a.isComplete)
                          const Padding(
                            padding: EdgeInsets.only(top: 2),
                            child: Text(
                              'Incomplète — ajoutez commune, repère et téléphone.',
                              style: TextStyle(color: AppTheme.danger, fontSize: 12),
                            ),
                          ),
                      ]),
                    ),
                  ]),
                ),
              ),
            ),
          ),
        const SizedBox(height: 4),
        OutlinedButton.icon(
          onPressed: onAdd,
          style: OutlinedButton.styleFrom(
            foregroundColor: AppTheme.brandGreen,
            side: const BorderSide(color: AppTheme.brandGreen),
            minimumSize: const Size.fromHeight(48),
            shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(12)),
          ),
          icon: const Icon(Icons.add, size: 18),
          label: const Text('Ajouter une adresse', style: TextStyle(fontWeight: FontWeight.w700)),
        ),
      ]),
    );
  }
}

// DÉSACTIVÉ — tuiles des moyens enregistrés. Conservées intactes : pour les
// réactiver, retirer les deux délimiteurs de commentaire qui encadrent ce bloc
// (et l'import de `payment_method_data.dart` en tête de fichier).
/*
class _SavedMethodTile extends StatelessWidget {
  const _SavedMethodTile({required this.method, required this.selected, required this.onTap});
  final PaymentMethod method;
  final bool selected;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    return GestureDetector(
      onTap: onTap,
      child: Container(
        padding: const EdgeInsets.all(14),
        decoration: BoxDecoration(
          color: AppTheme.surface,
          borderRadius: BorderRadius.circular(14),
          border: Border.all(color: selected ? AppTheme.brandGreen : AppTheme.line, width: selected ? 1.6 : 1),
        ),
        child: Row(children: [
          Icon(selected ? Icons.radio_button_checked : Icons.radio_button_off, color: selected ? AppTheme.brandGreen : AppTheme.subtle),
          const SizedBox(width: 12),
          Icon(method.isCard ? Icons.credit_card : Icons.smartphone, size: 20, color: AppTheme.subtle),
          const SizedBox(width: 10),
          Expanded(
            child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
              Text(method.label, style: const TextStyle(fontWeight: FontWeight.w800)),
              Text(method.display, style: const TextStyle(color: AppTheme.subtle, fontSize: 13)),
            ]),
          ),
          if (method.isDefault)
            Container(
              padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 3),
              decoration: BoxDecoration(color: AppTheme.softGreen, borderRadius: BorderRadius.circular(6)),
              child: const Text('DÉFAUT', style: TextStyle(color: AppTheme.brandGreen, fontSize: 10, fontWeight: FontWeight.w800)),
            ),
        ]),
      ),
    );
  }
}

class _NewMethodTile extends StatelessWidget {
  const _NewMethodTile({required this.selected, required this.onTap});
  final bool selected;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    return GestureDetector(
      onTap: onTap,
      child: Container(
        padding: const EdgeInsets.all(14),
        decoration: BoxDecoration(
          color: AppTheme.surface,
          borderRadius: BorderRadius.circular(14),
          border: Border.all(color: selected ? AppTheme.brandGreen : AppTheme.line, width: selected ? 1.6 : 1),
        ),
        child: Row(children: [
          Icon(selected ? Icons.radio_button_checked : Icons.radio_button_off, color: selected ? AppTheme.brandGreen : AppTheme.subtle),
          const SizedBox(width: 12),
          const Icon(Icons.add_card, size: 20, color: AppTheme.subtle),
          const SizedBox(width: 10),
          const Text('Utiliser un autre moyen', style: TextStyle(fontWeight: FontWeight.w800)),
        ]),
      ),
    );
  }
}
*/

class _StepLabel extends StatelessWidget {
  const _StepLabel(this.number, this.label, {this.trailing, this.onTrailing});
  final String number;
  final String label;
  final String? trailing;
  final VoidCallback? onTrailing;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.only(top: 20, bottom: 2),
      child: Row(children: [
        Text('$number. ${label.toUpperCase()}',
            style: TextStyle(fontSize: 12.5, fontWeight: FontWeight.w800, color: AppTheme.subtle, letterSpacing: 0.4)),
        const Spacer(),
        if (trailing != null)
          GestureDetector(
            onTap: onTrailing,
            child: Text(trailing!, style: const TextStyle(color: AppTheme.brandGreen, fontWeight: FontWeight.w700, fontSize: 13)),
          ),
      ]),
    );
  }
}

class _ShippingOption extends StatelessWidget {
  const _ShippingOption({required this.selected, required this.title, required this.subtitle, required this.price, required this.onTap});
  final bool selected;
  final String title;
  final String subtitle;
  final String price;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    return GestureDetector(
      onTap: onTap,
      child: Container(
        padding: const EdgeInsets.all(14),
        decoration: BoxDecoration(
          color: AppTheme.surface,
          borderRadius: BorderRadius.circular(14),
          border: Border.all(color: selected ? AppTheme.brandGreen : AppTheme.line, width: selected ? 1.6 : 1),
        ),
        child: Row(children: [
          Icon(selected ? Icons.radio_button_checked : Icons.radio_button_off, color: selected ? AppTheme.brandGreen : AppTheme.subtle),
          const SizedBox(width: 12),
          Expanded(
            child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
              Text(title, style: const TextStyle(fontWeight: FontWeight.w800)),
              Text(subtitle, style: TextStyle(color: AppTheme.subtle, fontSize: 12.5)),
            ]),
          ),
          Text(price, style: const TextStyle(fontWeight: FontWeight.w800)),
        ]),
      ),
    );
  }
}

// DÉSACTIVÉ — Mobile Money en saisie directe, et Carte Bancaire.
//
// Ces deux tuiles menaient de toute façon à la même page hébergée FedaPay :
// elles demandaient à l'acheteur de choisir son opérateur et de saisir son
// numéro, pour aboutir à l'écran qui lui reposait exactement ces questions.
// Conservées intactes — retirer les deux délimiteurs pour les réactiver.
/*
class _PaymentMobile extends StatelessWidget {
  const _PaymentMobile({required this.selected, required this.provider, required this.phone, required this.onSelect, required this.onProvider});
  final bool selected;
  final PayMethod provider;
  final TextEditingController phone;
  final VoidCallback onSelect;
  final ValueChanged<PayMethod> onProvider;

  @override
  Widget build(BuildContext context) {
    return GestureDetector(
      onTap: onSelect,
      child: Container(
        padding: const EdgeInsets.all(14),
        decoration: BoxDecoration(
          color: AppTheme.surface,
          borderRadius: BorderRadius.circular(14),
          border: Border.all(color: selected ? AppTheme.brandGreen : AppTheme.line, width: selected ? 1.6 : 1),
        ),
        child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
          Row(children: [
            Icon(selected ? Icons.radio_button_checked : Icons.radio_button_off, color: selected ? AppTheme.brandGreen : AppTheme.subtle),
            const SizedBox(width: 12),
            const Text('Mobile Money', style: TextStyle(fontWeight: FontWeight.w800)),
            const Spacer(),
            const Icon(Icons.phone_android, color: AppTheme.subtle, size: 18),
          ]),
          if (selected) ...[
            const SizedBox(height: 12),
            Wrap(spacing: 8, children: [
              for (final p in [PayMethod.mtnMomo, PayMethod.moovMoney, PayMethod.wave])
                FilterChipPill(label: p.label.replaceAll(' Mobile Money', '').replaceAll(' Money', ''), selected: provider == p, onTap: () => onProvider(p)),
            ]),
            const SizedBox(height: 12),
            const Text('Numéro de téléphone', style: TextStyle(fontWeight: FontWeight.w700, fontSize: 13)),
            const SizedBox(height: 6),
            TextField(
              controller: phone,
              keyboardType: TextInputType.phone,
              decoration: const InputDecoration(hintText: '+229 00 00 00 00', prefixIcon: Icon(Icons.phone)),
            ),
            const SizedBox(height: 8),
            const Row(children: [
              Icon(Icons.info_outline, size: 14, color: AppTheme.subtle),
              SizedBox(width: 6),
              Expanded(child: Text('Vous recevrez une notification de validation sur votre téléphone.', style: TextStyle(color: AppTheme.subtle, fontSize: 12))),
            ]),
          ],
        ]),
      ),
    );
  }
}

class _PaymentCard extends StatelessWidget {
  const _PaymentCard({required this.selected, required this.onSelect});
  final bool selected;
  final VoidCallback onSelect;

  @override
  Widget build(BuildContext context) {
    return GestureDetector(
      onTap: onSelect,
      child: Container(
        padding: const EdgeInsets.all(14),
        decoration: BoxDecoration(
          color: AppTheme.surface,
          borderRadius: BorderRadius.circular(14),
          border: Border.all(color: selected ? AppTheme.brandGreen : AppTheme.line, width: selected ? 1.6 : 1),
        ),
        child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
          Row(children: [
            Icon(selected ? Icons.radio_button_checked : Icons.radio_button_off, color: selected ? AppTheme.brandGreen : AppTheme.subtle),
            const SizedBox(width: 12),
            const Text('Carte Bancaire', style: TextStyle(fontWeight: FontWeight.w800)),
            const Spacer(),
            const Icon(Icons.credit_card, color: AppTheme.subtle, size: 20),
          ]),
          if (selected) ...[
            const SizedBox(height: 8),
            const Row(children: [
              Icon(Icons.lock_outline, size: 14, color: AppTheme.subtle),
              SizedBox(width: 6),
              Expanded(
                child: Text('Visa / MasterCard via la page sécurisée FedaPay. Le paiement s\'ouvre dans l\'app.',
                    style: TextStyle(color: AppTheme.subtle, fontSize: 12)),
              ),
            ]),
          ],
        ]),
      ),
    );
  }
}
*/

class _PaymentFedaPay extends StatelessWidget {
  const _PaymentFedaPay({required this.selected, this.onSelect});
  final bool selected;

  /// Nullable depuis que FedaPay est le seul moyen : il n'y a plus rien à
  /// sélectionner. `GestureDetector` accepte un `onTap` nul et devient alors
  /// inerte — la tuile s'affiche sans faire semblant d'être cliquable.
  final VoidCallback? onSelect;

  @override
  Widget build(BuildContext context) {
    return GestureDetector(
      onTap: onSelect,
      child: Container(
        padding: const EdgeInsets.all(14),
        decoration: BoxDecoration(
          color: AppTheme.surface,
          borderRadius: BorderRadius.circular(14),
          border: Border.all(color: selected ? AppTheme.brandGreen : AppTheme.line, width: selected ? 1.6 : 1),
        ),
        child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
          Row(children: [
            Icon(selected ? Icons.radio_button_checked : Icons.radio_button_off, color: selected ? AppTheme.brandGreen : AppTheme.subtle),
            const SizedBox(width: 12),
            const Text('FedaPay', style: TextStyle(fontWeight: FontWeight.w800)),
            const Spacer(),
            Icon(Icons.account_balance_wallet_outlined, color: AppTheme.subtle, size: 20),
          ]),
          if (selected) ...[
            const SizedBox(height: 8),
            Row(children: [
              Icon(Icons.info_outline, size: 14, color: AppTheme.subtle),
              const SizedBox(width: 6),
              Expanded(
                child: Text('Page sécurisée FedaPay (Mobile Money & carte). Le paiement s\'ouvre dans l\'app.',
                    style: TextStyle(color: AppTheme.subtle, fontSize: 12)),
              ),
            ]),
          ],
        ]),
      ),
    );
  }
}

class _SummaryDark extends StatelessWidget {
  const _SummaryDark({required this.count, required this.subtotal, required this.shipping, required this.total, required this.currency, required this.shippingLabel});
  final int count;
  final double subtotal;
  final double shipping;
  final double total;
  final String currency;
  final String shippingLabel;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.all(16),
      decoration: BoxDecoration(color: const Color(0xFF18211C), borderRadius: BorderRadius.circular(16)),
      child: Column(children: [
        _row('Articles ($count)', Format.money(subtotal, currency)),
        const SizedBox(height: 8),
        _row('Livraison $shippingLabel', Format.money(shipping, currency)),
        Padding(padding: const EdgeInsets.symmetric(vertical: 12), child: Divider(height: 1, color: Colors.white.withValues(alpha: 0.15))),
        Row(mainAxisAlignment: MainAxisAlignment.spaceBetween, children: [
          const Text('Total à payer', style: TextStyle(color: Colors.white, fontWeight: FontWeight.w700)),
          Text(Format.money(total, currency), style: const TextStyle(color: Colors.white, fontWeight: FontWeight.w800, fontSize: 18)),
        ]),
      ]),
    );
  }

  Widget _row(String label, String value) => Row(
        mainAxisAlignment: MainAxisAlignment.spaceBetween,
        children: [
          Text(label, style: TextStyle(color: Colors.white.withValues(alpha: 0.7))),
          Text(value, style: const TextStyle(color: Colors.white, fontWeight: FontWeight.w600)),
        ],
      );
}
