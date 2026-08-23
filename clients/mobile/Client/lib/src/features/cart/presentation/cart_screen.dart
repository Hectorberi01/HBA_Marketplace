import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../../core/theme/app_theme.dart';
import '../../../shared/utils/formatters.dart';
import '../../../shared/widgets/app_notify.dart';
import '../../../shared/widgets/async_views.dart';
import '../../../shared/widgets/product_card.dart';
import '../../../shared/widgets/ui_kit.dart';
import '../../account/address_data.dart';
import '../cart_data.dart';

class CartScreen extends ConsumerWidget {
  const CartScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final cart = ref.watch(cartControllerProvider);
    return Scaffold(
      backgroundColor: AppTheme.bg,
      appBar: AppBar(
        title: const Text('Mon Panier'),
        actions: [
          cart.maybeWhen(
            data: (c) => c.isEmpty
                ? const SizedBox.shrink()
                : Padding(
                    padding: const EdgeInsets.only(right: 16),
                    child: Center(
                      child: Container(
                        padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 6),
                        decoration: BoxDecoration(color: AppTheme.softGreen, borderRadius: BorderRadius.circular(20)),
                        child: Text('${c.itemCount} Article${c.itemCount > 1 ? 's' : ''}',
                            style: const TextStyle(color: AppTheme.brandGreen, fontWeight: FontWeight.w700, fontSize: 13)),
                      ),
                    ),
                  ),
            orElse: () => const SizedBox.shrink(),
          ),
        ],
      ),
      body: SafeArea(
        top: false,
        child: cart.when(
          loading: () => const LoadingView(),
          error: (e, _) => ErrorView(message: e.toString(), onRetry: () => ref.invalidate(cartControllerProvider)),
          data: (c) {
            if (c.isEmpty) {
              return Column(mainAxisAlignment: MainAxisAlignment.center, children: [
                const EmptyView(message: 'Votre panier est vide.', icon: Icons.shopping_cart_outlined),
                FilledButton(onPressed: () => context.go('/home'), child: const Text('Découvrir des produits')),
                const SizedBox(height: 40),
              ]);
            }
            return Column(
              children: [
                Expanded(
                  child: ListView(
                    padding: const EdgeInsets.fromLTRB(16, 12, 16, 12),
                    children: [
                      for (final l in c.lines) ...[_LineCard(line: l), const SizedBox(height: 12)],
                      const _PromoField(),
                      const SizedBox(height: 12),
                      _SummaryCard(cart: c),
                    ],
                  ),
                ),
                _CheckoutBar(cart: c),
              ],
            );
          },
        ),
      ),
    );
  }
}

/// Exécute une mutation panier et signale toute erreur à l'utilisateur
/// (sinon l'échec serait silencieux puisque l'appel est en « fire-and-forget »).
Future<void> _run(BuildContext context, Future<void> Function() action) async {
  try {
    await action();
  } catch (e) {
    if (context.mounted) {
      AppNotify.error(context, 'Action impossible : $e');
    }
  }
}

class _LineCard extends ConsumerWidget {
  const _LineCard({required this.line});
  final CartLine line;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final notifier = ref.read(cartControllerProvider.notifier);
    return CardSection(
      margin: EdgeInsets.zero,
      padding: const EdgeInsets.all(10),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          ClipRRect(
            borderRadius: BorderRadius.circular(12),
            child: SizedBox(width: 72, height: 72, child: ProductImage(url: line.imageUrl)),
          ),
          const SizedBox(width: 12),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Row(children: [
                  Expanded(
                    child: Text(line.productName, maxLines: 2, overflow: TextOverflow.ellipsis,
                        style: TextStyle(fontWeight: FontWeight.w700, color: AppTheme.ink)),
                  ),
                  GestureDetector(
                    onTap: () => _run(context, () => notifier.remove(line.offerId)),
                    child: Icon(Icons.delete_outline, size: 20, color: AppTheme.subtle),
                  ),
                ]),
                const SizedBox(height: 8),
                Row(children: [
                  Text(Format.money(line.unitPrice, line.currency),
                      style: const TextStyle(color: AppTheme.brandGreen, fontWeight: FontWeight.w800, fontSize: 16)),
                  const Spacer(),
                  QuantityStepper(
                    value: line.quantity,
                    onChanged: (q) => _run(context, () => notifier.setQuantity(line.offerId, q)),
                  ),
                ]),
              ],
            ),
          ),
        ],
      ),
    );
  }
}

class _PromoField extends ConsumerStatefulWidget {
  const _PromoField();

  @override
  ConsumerState<_PromoField> createState() => _PromoFieldState();
}

class _PromoFieldState extends ConsumerState<_PromoField> {
  final _code = TextEditingController();
  bool _loading = false;

  @override
  void dispose() {
    _code.dispose();
    super.dispose();
  }

  Future<void> _apply() async {
    final code = _code.text.trim();
    if (code.isEmpty) return;
    setState(() => _loading = true);
    try {
      // Le contrôleur applique le code PUIS recharge le panier : le résumé
      // reflète alors les montants réels du serveur.
      final outcome = await ref.read(cartControllerProvider.notifier).applyCoupon(code);
      if (outcome.applied) _code.clear();
      if (mounted) {
        if (outcome.applied) {
          AppNotify.success(context, 'Code appliqué.');
        } else {
          AppNotify.info(context, outcome.message);
        }
      }
    } catch (e) {
      if (mounted) AppNotify.error(context, e.toString());
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  Future<void> _remove() async {
    setState(() => _loading = true);
    try {
      await ref.read(cartControllerProvider.notifier).removeCoupon();
    } catch (e) {
      if (mounted) AppNotify.error(context, e.toString());
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    // L'état « appliqué » vient désormais du panier lui-même (source unique),
    // et non d'un provider client qui pouvait diverger du serveur.
    final cart = ref.watch(cartControllerProvider).valueOrNull;
    final applied = cart?.hasCoupon ?? false;

    if (applied) {
      return CardSection(
        margin: EdgeInsets.zero,
        padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 6),
        child: Row(children: [
          const Icon(Icons.check_circle, color: AppTheme.brandGreen, size: 20),
          const SizedBox(width: 10),
          Expanded(
            child: Text('Code « ${cart!.promotionCode} » appliqué',
                style: TextStyle(fontWeight: FontWeight.w700, color: AppTheme.ink)),
          ),
          TextButton(
            onPressed: _loading ? null : _remove,
            child: _loading
                ? const SizedBox(height: 16, width: 16, child: CircularProgressIndicator(strokeWidth: 2))
                : Text('Retirer', style: TextStyle(color: AppTheme.subtle, fontWeight: FontWeight.w800)),
          ),
        ]),
      );
    }

    return CardSection(
      margin: EdgeInsets.zero,
      padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 6),
      child: Row(children: [
        Icon(Icons.local_offer_outlined, color: AppTheme.subtle, size: 20),
        const SizedBox(width: 10),
        Expanded(
          child: TextField(
            controller: _code,
            textCapitalization: TextCapitalization.characters,
            onSubmitted: (_) => _apply(),
            decoration: const InputDecoration(
              hintText: 'Code promo',
              border: InputBorder.none,
              enabledBorder: InputBorder.none,
              focusedBorder: InputBorder.none,
              filled: false,
              contentPadding: EdgeInsets.symmetric(vertical: 12),
            ),
          ),
        ),
        TextButton(
          onPressed: _loading ? null : _apply,
          child: _loading
              ? const SizedBox(height: 16, width: 16, child: CircularProgressIndicator(strokeWidth: 2))
              : const Text('Appliquer', style: TextStyle(color: AppTheme.brandGreen, fontWeight: FontWeight.w800)),
        ),
      ]),
    );
  }
}

class _SummaryCard extends StatelessWidget {
  const _SummaryCard({required this.cart});
  final Cart cart;

  @override
  Widget build(BuildContext context) {
    // Tous les montants proviennent du serveur : sous-total, remise (= subtotal
    // - grandTotal) et total à payer. Plus aucun recalcul client.
    final currency = cart.currency;
    return CardSection(
      margin: EdgeInsets.zero,
      padding: const EdgeInsets.all(16),
      child: Column(children: [
        const Align(alignment: Alignment.centerLeft, child: Text('Résumé de la commande', style: TextStyle(fontWeight: FontWeight.w800, fontSize: 16))),
        const SizedBox(height: 14),
        _row('Sous-total', Format.money(cart.subtotal, currency)),
        const SizedBox(height: 10),
        _row('Livraison', 'Calculée à l’étape suivante', muted: true),
        if (cart.hasCoupon && cart.discount > 0) ...[
          const SizedBox(height: 10),
          Row(mainAxisAlignment: MainAxisAlignment.spaceBetween, children: [
            Text('Remise (${cart.promotionCode})', style: const TextStyle(color: AppTheme.brandGreen)),
            Text('- ${Format.money(cart.discount, currency)}', style: const TextStyle(color: AppTheme.brandGreen, fontWeight: FontWeight.w700)),
          ]),
        ],
        Padding(padding: const EdgeInsets.symmetric(vertical: 12), child: Divider(height: 1, color: AppTheme.line)),
        Row(mainAxisAlignment: MainAxisAlignment.spaceBetween, children: [
          const Text('Total', style: TextStyle(fontWeight: FontWeight.w800, fontSize: 16)),
          Text(Format.money(cart.grandTotal, currency), style: const TextStyle(fontWeight: FontWeight.w800, fontSize: 18, color: AppTheme.brandGreen)),
        ]),
      ]),
    );
  }

  Widget _row(String label, String value, {bool muted = false}) => Row(
        mainAxisAlignment: MainAxisAlignment.spaceBetween,
        children: [
          Text(label, style: TextStyle(color: AppTheme.subtle)),
          Text(value, style: TextStyle(fontWeight: FontWeight.w600, color: muted ? AppTheme.subtle : AppTheme.ink, fontSize: muted ? 13 : 14)),
        ],
      );
}

class _CheckoutBar extends ConsumerWidget {
  const _CheckoutBar({required this.cart});
  final Cart cart;

  /// Exige une adresse de livraison avant d'aller au paiement.
  Future<void> _goToCheckout(BuildContext context, WidgetRef ref) async {
    List<Address> addresses;
    try {
      addresses = await ref.read(addressControllerProvider.future);
    } catch (_) {
      addresses = const [];
    }
    if (!context.mounted) return;

    // ── « AUCUNE ADRESSE COMPLÈTE », PAS « AUCUNE ADRESSE » ────────────────────
    //
    // Un acheteur dont toutes les adresses datent d'avant la refonte en a bien
    // une : `addresses.isEmpty` était donc faux, il franchissait le panier, et se
    // faisait arrêter au checkout — un écran plus loin, après avoir cru avancer.
    // On l'arrête ici, avec le message qui dit quoi corriger.
    final usable = addresses.where((a) => a.isComplete).toList();
    if (usable.isEmpty) {
      final hasIncomplete = addresses.isNotEmpty;
      final add = await showDialog<bool>(
        context: context,
        builder: (dialogContext) => AlertDialog(
          title: Text(hasIncomplete ? 'Adresse à compléter' : 'Adresse de livraison requise'),
          content: Text(hasIncomplete
              ? 'Vos adresses enregistrées sont incomplètes. Ajoutez la commune, un point '
                  'de repère et un numéro de téléphone pour pouvoir être livré.'
              : 'Ajoutez une adresse de livraison pour finaliser votre commande.'),
          actions: [
            TextButton(onPressed: () => Navigator.pop(dialogContext, false), child: const Text('Plus tard')),
            FilledButton(
                onPressed: () => Navigator.pop(dialogContext, true),
                child: Text(hasIncomplete ? 'Compléter' : 'Ajouter')),
          ],
        ),
      );
      if (add == true && context.mounted) context.push('/account/addresses');
      return;
    }
    if (context.mounted) context.push('/checkout');
  }

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final grand = cart.grandTotal;
    final currency = cart.currency;
    return Container(
      decoration: BoxDecoration(color: AppTheme.bg),
      padding: EdgeInsets.fromLTRB(16, 8, 16, 12 + MediaQuery.of(context).padding.bottom),
      child: FilledButton(
        onPressed: () => _goToCheckout(context, ref),
        style: FilledButton.styleFrom(minimumSize: const Size.fromHeight(54)),
        child: Row(mainAxisAlignment: MainAxisAlignment.center, children: [
          const Text('Passer la commande'),
          const SizedBox(width: 10),
          Container(
            padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 4),
            decoration: BoxDecoration(color: Colors.white.withValues(alpha: 0.2), borderRadius: BorderRadius.circular(8)),
            child: Text(Format.money(grand, currency), style: const TextStyle(fontWeight: FontWeight.w800)),
          ),
        ]),
      ),
    );
  }
}
