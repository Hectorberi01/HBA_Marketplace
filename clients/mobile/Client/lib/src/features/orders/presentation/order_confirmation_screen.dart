import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../../core/theme/app_theme.dart';
import '../../../shared/utils/formatters.dart';
import '../../../shared/widgets/async_views.dart';
import '../orders_data.dart';

class OrderConfirmationScreen extends ConsumerWidget {
  const OrderConfirmationScreen({super.key, required this.orderId});
  final String orderId;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final bundle = ref.watch(orderDetailProvider(orderId));
    return Scaffold(
      backgroundColor: AppTheme.bg,
      body: SafeArea(
        child: bundle.when(
          loading: () => const LoadingView(),
          error: (_, __) => _Body(orderId: orderId, order: null),
          data: (b) => _Body(orderId: orderId, order: b.order),
        ),
      ),
    );
  }
}

class _Body extends StatelessWidget {
  const _Body({required this.orderId, required this.order});
  final String orderId;
  final OrderItem? order;

  @override
  Widget build(BuildContext context) {
    return Column(
      children: [
        Expanded(
          child: ListView(
            padding: const EdgeInsets.fromLTRB(20, 28, 20, 16),
            children: [
              Center(
                child: Container(
                  width: 96,
                  height: 96,
                  decoration: BoxDecoration(color: AppTheme.softGreen, shape: BoxShape.circle),
                  child: const Icon(Icons.check_circle, color: AppTheme.brandGreen, size: 64),
                ),
              ),
              const SizedBox(height: 20),
              Center(
                child: Text('Commande confirmée !',
                    style: TextStyle(fontSize: 22, fontWeight: FontWeight.w800, color: AppTheme.ink)),
              ),
              const SizedBox(height: 8),
              Center(
                child: Text(
                  'Merci pour votre achat. Vous recevrez une notification à chaque étape.',
                  textAlign: TextAlign.center,
                  style: TextStyle(color: AppTheme.subtle, height: 1.4),
                ),
              ),
              const SizedBox(height: 24),
              Container(
                padding: const EdgeInsets.all(16),
                decoration: BoxDecoration(
                  color: AppTheme.surface,
                  borderRadius: BorderRadius.circular(16),
                  border: Border.all(color: AppTheme.line),
                ),
                child: Column(children: [
                  _row('Numéro de commande', order?.reference ?? '—'),
                  if (order != null) ...[
                    Divider(height: 24, color: AppTheme.line),
                    _row('Date', Format.date(order!.createdAt)),
                    Divider(height: 24, color: AppTheme.line),
                    _row('Articles', '${order!.itemCount}'),
                    Divider(height: 24, color: AppTheme.line),
                    _row('Total payé', Format.money(order!.total, order!.currency), strong: true),
                  ],
                ]),
              ),
              if (order?.shippingAddress != null) ...[
                const SizedBox(height: 12),
                Container(
                  padding: const EdgeInsets.all(16),
                  decoration: BoxDecoration(
                    color: AppTheme.surface,
                    borderRadius: BorderRadius.circular(16),
                    border: Border.all(color: AppTheme.line),
                  ),
                  child: Row(crossAxisAlignment: CrossAxisAlignment.start, children: [
                    const Icon(Icons.location_on_outlined, color: AppTheme.brandGreen, size: 20),
                    const SizedBox(width: 10),
                    Expanded(
                      child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                        Text('Livraison à', style: TextStyle(color: AppTheme.subtle, fontSize: 12)),
                        const SizedBox(height: 2),
                        if (order!.shippingAddress!.recipient.isNotEmpty)
                          Text(order!.shippingAddress!.recipient, style: const TextStyle(fontWeight: FontWeight.w700)),
                        Text(order!.shippingAddress!.summary, style: TextStyle(color: AppTheme.subtle, height: 1.3)),
                      ]),
                    ),
                  ]),
                ),
              ],
              const SizedBox(height: 20),
              const _NextSteps(),
            ],
          ),
        ),
        Padding(
          padding: const EdgeInsets.fromLTRB(20, 4, 20, 12),
          child: Column(mainAxisSize: MainAxisSize.min, children: [
            FilledButton.icon(
              onPressed: () => context.go('/order/$orderId/tracking'),
              style: FilledButton.styleFrom(minimumSize: const Size.fromHeight(52)),
              icon: const Icon(Icons.local_shipping_outlined, size: 18),
              label: const Text('Suivre ma commande'),
            ),
            const SizedBox(height: 8),
            Row(children: [
              Expanded(
                child: OutlinedButton(
                  onPressed: () => context.go('/order/$orderId'),
                  style: OutlinedButton.styleFrom(
                    foregroundColor: AppTheme.ink,
                    side: BorderSide(color: AppTheme.line),
                    minimumSize: const Size(0, 50),
                    shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(12)),
                  ),
                  child: const Text('Voir le détail'),
                ),
              ),
              const SizedBox(width: 10),
              Expanded(
                child: OutlinedButton(
                  onPressed: () => context.go('/home'),
                  style: OutlinedButton.styleFrom(
                    foregroundColor: AppTheme.brandGreen,
                    side: const BorderSide(color: AppTheme.brandGreen),
                    minimumSize: const Size(0, 50),
                    shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(12)),
                  ),
                  child: const Text('Continuer'),
                ),
              ),
            ]),
          ]),
        ),
      ],
    );
  }

  Widget _row(String label, String value, {bool strong = false}) => Row(
        mainAxisAlignment: MainAxisAlignment.spaceBetween,
        children: [
          Text(label, style: TextStyle(color: AppTheme.subtle)),
          Text(value,
              style: TextStyle(
                  fontWeight: strong ? FontWeight.w800 : FontWeight.w600,
                  fontSize: strong ? 16 : 14,
                  color: strong ? AppTheme.brandGreen : AppTheme.ink)),
        ],
      );
}

class _NextSteps extends StatelessWidget {
  const _NextSteps();

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.all(16),
      decoration: BoxDecoration(color: AppTheme.softGreen, borderRadius: BorderRadius.circular(16)),
      child: const Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
        Text('Et maintenant ?', style: TextStyle(fontWeight: FontWeight.w800, color: AppTheme.brandGreenDark)),
        SizedBox(height: 8),
        _Step(icon: Icons.inventory_2_outlined, text: 'Le vendeur prépare votre colis.'),
        SizedBox(height: 6),
        _Step(icon: Icons.local_shipping_outlined, text: 'Vous serez notifié à l\'expédition.'),
        SizedBox(height: 6),
        _Step(icon: Icons.home_outlined, text: 'Réception et confirmation de livraison.'),
      ]),
    );
  }
}

class _Step extends StatelessWidget {
  const _Step({required this.icon, required this.text});
  final IconData icon;
  final String text;

  @override
  Widget build(BuildContext context) {
    return Row(children: [
      Icon(icon, size: 18, color: AppTheme.brandGreen),
      const SizedBox(width: 10),
      Expanded(child: Text(text, style: TextStyle(color: AppTheme.ink, fontSize: 13))),
    ]);
  }
}
