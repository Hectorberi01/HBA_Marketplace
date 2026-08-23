import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../../core/theme/app_theme.dart';
import '../../../shared/utils/formatters.dart';
import '../../../shared/widgets/app_notify.dart';
import '../../../shared/widgets/async_views.dart';
import '../../../shared/widgets/product_card.dart';
import '../../../shared/widgets/ui_kit.dart';
import '../../engagement/presentation/dispute_sheet.dart';
import '../../engagement/presentation/review_sheet.dart';
import '../../messaging/messaging_data.dart';
import '../orders_data.dart';
import 'orders_screen.dart';
import 'pay_again.dart';

class OrderDetailScreen extends ConsumerWidget {
  const OrderDetailScreen({super.key, required this.orderId});
  final String orderId;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final bundle = ref.watch(orderDetailProvider(orderId));
    return Scaffold(
      backgroundColor: AppTheme.bg,
      appBar: AppBar(
        title: const Text('Détails de la commande'),
        leading: IconButton(
          icon: const Icon(Icons.arrow_back),
          onPressed: () => context.canPop() ? context.pop() : context.go('/orders'),
        ),
        actions: [
          PopupMenuButton<String>(
            icon: const Icon(Icons.more_vert),
            onSelected: (v) {
              if (v == 'cancel') _confirmCancel(context, ref, orderId);
              if (v == 'dispute') showDisputeSheet(context, orderId: orderId);
            },
            itemBuilder: (_) => [
              const PopupMenuItem(value: 'dispute', child: Text('Signaler un problème')),
              const PopupMenuItem(value: 'cancel', child: Text('Annuler la commande')),
            ],
          ),
        ],
      ),
      body: bundle.when(
        loading: () => const LoadingView(),
        error: (e, _) => ErrorView(message: e.toString(), onRetry: () => ref.invalidate(orderDetailProvider(orderId))),
        data: (b) {
          final o = b.order;
          // Commande livrée : le suivi de colis n'a plus lieu d'être.
          final delivered = fulfillmentStep(o.status, b.shipments) >= 4;
          return ListView(
            padding: const EdgeInsets.fromLTRB(16, 12, 16, 16),
            children: [
              _HeaderCard(order: o, shipments: b.shipments),
              const SizedBox(height: 12),
              _InfoCard(
                icon: Icons.location_on_outlined,
                title: 'Adresse de livraison',
                body: o.shippingAddress != null
                    ? [
                        if (o.shippingAddress!.recipient.isNotEmpty) o.shippingAddress!.recipient,
                        o.shippingAddress!.summary,
                        if (o.shippingAddress!.phone.isNotEmpty) o.shippingAddress!.phone,
                      ].where((s) => s.isNotEmpty).join('\n')
                    : 'Adresse enregistrée. Modifiable depuis votre compte.',
              ),
              const SizedBox(height: 12),
              _PaymentCard(total: o.total, currency: o.currency, status: o.status),
              const SizedBox(height: 12),
              _ItemsCard(
                order: o,
                step: fulfillmentStep(o.status, b.shipments),
                onReturn: (l) => _return(context, ref, o.id, l),
                onReview: (l) => showReviewSheet(context, productId: l.productId, orderId: o.id, productName: l.name),
              ),
              const SizedBox(height: 12),
              _TotalsCard(order: o),
              const SizedBox(height: 16),
              Row(children: [
                Expanded(
                  child: OutlinedButton.icon(
                    onPressed: () => _openSellerChat(context, ref, o),
                    style: OutlinedButton.styleFrom(
                      foregroundColor: AppTheme.ink,
                      side: BorderSide(color: AppTheme.line),
                      minimumSize: const Size(0, 50),
                      shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(12)),
                    ),
                    icon: const Icon(Icons.chat_bubble_outline, size: 18),
                    label: const Text('Vendeur'),
                  ),
                ),
                // ─────────────────────────────────────────────────────────────
                // TANT QUE LA COMMANDE N'EST PAS PAYÉE, L'ACTION PRINCIPALE EST
                // DE LA PAYER — PAS DE SUIVRE UN COLIS QUI N'EXISTE PAS.
                //
                // « Suivre le colis » s'affichait même sur une commande impayée :
                // il menait à un écran de suivi vide, sans rien indiquer de ce
                // qu'il fallait faire. L'acheteur en concluait raisonnablement que
                // la plateforme était en panne.
                // ─────────────────────────────────────────────────────────────
                if (o.isAwaitingPayment) ...[
                  const SizedBox(width: 10),
                  Expanded(
                    flex: 2,
                    child: FilledButton.icon(
                      onPressed: () => payOrderAgain(context, ref, o),
                      style: FilledButton.styleFrom(minimumSize: const Size(0, 50)),
                      icon: const Icon(Icons.credit_card, size: 18),
                      label: const Text('Payer maintenant'),
                    ),
                  ),
                ] else if (!delivered) ...[
                  const SizedBox(width: 10),
                  Expanded(
                    flex: 2,
                    child: FilledButton.icon(
                      onPressed: () => context.push('/order/$orderId/tracking'),
                      style: FilledButton.styleFrom(minimumSize: const Size(0, 50)),
                      icon: const Icon(Icons.local_shipping_outlined, size: 18),
                      label: const Text('Suivre le colis'),
                    ),
                  ),
                ],
              ]),
              const SizedBox(height: 14),
              Center(
                child: Text.rich(TextSpan(children: [
                  TextSpan(text: 'Un problème avec votre commande ? ', style: TextStyle(color: AppTheme.subtle, fontSize: 13)),
                  const TextSpan(
                    text: 'Aide & Support',
                    style: TextStyle(color: AppTheme.brandGreen, fontWeight: FontWeight.w800, fontSize: 13, decoration: TextDecoration.underline),
                  ),
                ])),
              ),
            ],
          );
        },
      ),
    );
  }

  Future<void> _confirmCancel(BuildContext context, WidgetRef ref, String id) async {
    final ok = await showDialog<bool>(
      context: context,
      builder: (_) => AlertDialog(
        title: const Text('Annuler la commande ?'),
        content: const Text('Cette action est définitive.'),
        actions: [
          TextButton(onPressed: () => Navigator.pop(context, false), child: const Text('Non')),
          FilledButton(onPressed: () => Navigator.pop(context, true), child: const Text('Oui, annuler')),
        ],
      ),
    );
    if (ok != true) return;
    try {
      await ref.read(ordersApiProvider).cancel(id, 'Annulée par le client');
      ref.invalidate(orderDetailProvider(id));
      ref.invalidate(ordersListProvider);
    } catch (e) {
      if (context.mounted) AppNotify.error(context, e.toString());
    }
  }

  /// Ouvre (ou crée) la conversation avec LE vendeur de cette commande, puis y
  /// navigue. Repli sur la liste des conversations si le vendeur est inconnu ou
  /// si l'ouverture échoue.
  Future<void> _openSellerChat(BuildContext context, WidgetRef ref, OrderItem order) async {
    final sellerId = order.lines.map((l) => l.sellerId).firstWhere((s) => s.isNotEmpty, orElse: () => '');
    if (sellerId.isEmpty) {
      context.push('/conversations');
      return;
    }
    try {
      final convId = await ref.read(messagingApiProvider).startWithSeller(sellerId);
      if (!context.mounted) return;
      context.push(convId.isNotEmpty ? '/chat/$convId' : '/conversations');
    } catch (e) {
      if (context.mounted) AppNotify.error(context, e.toString());
    }
  }

  Future<void> _return(BuildContext context, WidgetRef ref, String orderId, OrderLine line) async {
    // Motifs structurés (alignés sur les catégories gérées côté back-office) +
    // détails facultatifs. On envoie une chaîne composée, l'API attend un `reason`.
    const reasons = <String>[
      'Produit défectueux ou endommagé',
      'Non conforme à la description',
      'Mauvaise taille / ne convient pas',
      'Erreur de commande',
      'Autre',
    ];
    var selected = reasons.first;
    final details = TextEditingController();

    final composed = await showModalBottomSheet<String>(
      context: context,
      isScrollControlled: true,
      backgroundColor: AppTheme.surface,
      shape: const RoundedRectangleBorder(borderRadius: BorderRadius.vertical(top: Radius.circular(20))),
      builder: (sheetCtx) => Padding(
        padding: EdgeInsets.only(bottom: MediaQuery.of(sheetCtx).viewInsets.bottom),
        child: StatefulBuilder(
          builder: (sheetCtx, setSheet) => SafeArea(
            child: Padding(
              padding: const EdgeInsets.fromLTRB(20, 16, 20, 20),
              child: Column(mainAxisSize: MainAxisSize.min, crossAxisAlignment: CrossAxisAlignment.start, children: [
                Text('Retourner « ${line.name} »',
                    style: const TextStyle(fontWeight: FontWeight.w800, fontSize: 16)),
                const SizedBox(height: 4),
                Text('Indiquez le motif du retour.', style: TextStyle(color: AppTheme.subtle, fontSize: 13)),
                const SizedBox(height: 8),
                // `groupValue` et `onChanged` sont dépréciés SUR LA TUILE depuis
                // Flutter 3.32 : ils vivent désormais une seule fois, sur
                // l'ancêtre `RadioGroup`. Les tuiles ne portent plus que leur
                // propre valeur.
                RadioGroup<String>(
                  groupValue: selected,
                  // `?? selected` plutôt que `v!` : un motif de retour ne peut
                  // pas être désélectionné, et une assertion sur `null` ferait
                  // planter la feuille au lieu de ne rien faire.
                  onChanged: (v) => setSheet(() => selected = v ?? selected),
                  child: Column(
                    mainAxisSize: MainAxisSize.min,
                    children: [
                      for (final r in reasons)
                        RadioListTile<String>(
                          value: r,
                          title: Text(r, style: const TextStyle(fontSize: 14)),
                          contentPadding: EdgeInsets.zero,
                          activeColor: AppTheme.brandGreen,
                          dense: true,
                        ),
                    ],
                  ),
                ),
                const SizedBox(height: 8),
                TextField(
                  controller: details,
                  maxLines: 2,
                  decoration: const InputDecoration(labelText: 'Détails (facultatif)', alignLabelWithHint: true),
                ),
                const SizedBox(height: 16),
                SizedBox(
                  width: double.infinity,
                  child: FilledButton(
                    onPressed: () {
                      final d = details.text.trim();
                      Navigator.pop(sheetCtx, d.isEmpty ? selected : '$selected — $d');
                    },
                    child: const Text('Demander le retour'),
                  ),
                ),
              ]),
            ),
          ),
        ),
      ),
    );

    if (composed == null || composed.isEmpty) return;
    try {
      await ref.read(ordersApiProvider).requestReturn(orderId, line.offerId, composed);
      if (context.mounted) {
        AppNotify.success(context, 'Demande de retour envoyée.');
      }
    } catch (e) {
      if (context.mounted) AppNotify.error(context, e.toString());
    }
  }
}

class _HeaderCard extends StatelessWidget {
  const _HeaderCard({required this.order, required this.shipments});
  final OrderItem order;
  final List<Shipment> shipments;

  @override
  Widget build(BuildContext context) {
    final idx = fulfillmentStep(order.status, shipments);
    final cancelled = order.status.toLowerCase() == 'cancelled';
    return CardSection(
      margin: EdgeInsets.zero,
      padding: const EdgeInsets.all(16),
      child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
        Row(children: [
          Text('RÉF : ${order.reference}', style: TextStyle(fontSize: 11, fontWeight: FontWeight.w700, color: AppTheme.subtle)),
          const Spacer(),
          OrderStatusChip(status: order.status),
        ]),
        const SizedBox(height: 6),
        Text(order.createdAt != null ? Format.date(order.createdAt) : '—',
            style: const TextStyle(fontSize: 18, fontWeight: FontWeight.w800)),
        const SizedBox(height: 16),
        if (cancelled)
          const Row(children: [
            Icon(Icons.cancel, color: AppTheme.danger, size: 20),
            SizedBox(width: 8),
            Text('Commande annulée', style: TextStyle(fontWeight: FontWeight.w700, color: AppTheme.danger)),
          ])
        else ...[
          _step('Commande confirmée', idx >= 1, idx == 1, isLast: false),
          _step('Préparée avec soin', idx >= 2, idx == 2, isLast: false),
          _step('Expédiée', idx >= 3, idx == 3, isLast: false),
          _step('Livrée', idx >= 4, idx == 4, isLast: true),
        ],
      ]),
    );
  }

  Widget _step(String label, bool done, bool active, {required bool isLast}) {
    return IntrinsicHeight(
      child: Row(crossAxisAlignment: CrossAxisAlignment.start, children: [
        Column(children: [
          Container(
            width: 24, height: 24,
            decoration: BoxDecoration(
              color: done ? AppTheme.brandGreen : Colors.white,
              shape: BoxShape.circle,
              border: Border.all(color: active ? AppTheme.promoOrange : (done ? AppTheme.brandGreen : AppTheme.line), width: 2),
            ),
            child: done ? const Icon(Icons.check, size: 14, color: Colors.white) : null,
          ),
          if (!isLast)
            Expanded(child: Container(width: 2, color: done ? AppTheme.brandGreen : AppTheme.line)),
        ]),
        const SizedBox(width: 12),
        Padding(
          padding: const EdgeInsets.only(bottom: 16),
          child: Text(label,
              style: TextStyle(
                  fontWeight: FontWeight.w700,
                  color: (done || active) ? AppTheme.ink : AppTheme.subtle)),
        ),
      ]),
    );
  }
}

class _InfoCard extends StatelessWidget {
  const _InfoCard({required this.icon, required this.title, required this.body});
  final IconData icon;
  final String title;
  final String body;

  @override
  Widget build(BuildContext context) {
    return CardSection(
      margin: EdgeInsets.zero,
      padding: const EdgeInsets.all(16),
      child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
        Row(children: [
          Icon(icon, size: 20, color: AppTheme.subtle),
          const SizedBox(width: 8),
          Text(title, style: const TextStyle(fontWeight: FontWeight.w800)),
        ]),
        const SizedBox(height: 10),
        Text(body, style: TextStyle(color: AppTheme.subtle, height: 1.4)),
      ]),
    );
  }
}

class _PaymentCard extends StatelessWidget {
  const _PaymentCard({required this.total, required this.currency, required this.status});
  final double total;
  final String currency;
  final String status;

  @override
  Widget build(BuildContext context) {
    // Le backend ne renvoie pas le moyen exact : tous les paiements passent par
    // FedaPay (Mobile Money OU carte). On affiche donc un libellé exact plutôt
    // qu'un faux « Mobile Money ». Le statut « réglé » se déduit de l'état de la
    // commande : une commande encore « en attente » n'est pas payée.
    final s = status.toLowerCase();
    final pending = s == 'pending' || s == 'awaitingpayment' || s == 'en attente';
    final cancelled = s == 'cancelled' || s == 'canceled' || s == 'annulée';

    final (String label, Color color) = cancelled
        ? ('Paiement annulé', AppTheme.subtle)
        : pending
            ? ('En attente de paiement', AppTheme.subtle)
            : ('Paiement validé', AppTheme.brandGreen);

    return CardSection(
      margin: EdgeInsets.zero,
      padding: const EdgeInsets.all(16),
      child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
        Row(children: [
          Icon(Icons.credit_card, size: 20, color: AppTheme.subtle),
          const SizedBox(width: 8),
          const Text('Mode de paiement', style: TextStyle(fontWeight: FontWeight.w800)),
        ]),
        const SizedBox(height: 12),
        Row(children: [
          Container(
            padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 4),
            decoration: BoxDecoration(color: AppTheme.softGreen, borderRadius: BorderRadius.circular(6)),
            child: const Text('PAIEMENT EN LIGNE',
                style: TextStyle(color: AppTheme.brandGreen, fontSize: 11, fontWeight: FontWeight.w800)),
          ),
          const SizedBox(width: 10),
          Expanded(child: Text(label, style: TextStyle(color: color))),
        ]),
        const SizedBox(height: 4),
        Text('Réglé via FedaPay (Mobile Money ou carte).',
            style: TextStyle(color: AppTheme.subtle, fontSize: 12)),
      ]),
    );
  }
}

class _ItemsCard extends StatelessWidget {
  const _ItemsCard({required this.order, required this.step, required this.onReturn, required this.onReview});
  final OrderItem order;
  final int step; // 3 = expédiée, 4 = livrée
  final void Function(OrderLine) onReturn;
  final void Function(OrderLine) onReview;

  bool get _canReturn => step >= 3;
  bool get _canReview => step >= 4;

  @override
  Widget build(BuildContext context) {
    return CardSection(
      margin: EdgeInsets.zero,
      padding: const EdgeInsets.all(16),
      child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
        Text('Articles (${order.itemCount})', style: const TextStyle(fontWeight: FontWeight.w800)),
        const SizedBox(height: 12),
        for (final l in order.lines) ...[
          Row(children: [
            ClipRRect(
              borderRadius: BorderRadius.circular(10),
              child: SizedBox(width: 48, height: 48, child: ProductImage(url: l.imageUrl)),
            ),
            const SizedBox(width: 12),
            Expanded(
              child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                Text(l.name, maxLines: 1, overflow: TextOverflow.ellipsis, style: const TextStyle(fontWeight: FontWeight.w700)),
                Text('Qté : ${l.quantity}', style: TextStyle(color: AppTheme.subtle, fontSize: 12)),
                Row(children: [
                  if (_canReview && l.productId.isNotEmpty)
                    GestureDetector(
                      onTap: () => onReview(l),
                      child: const Padding(
                        padding: EdgeInsets.only(top: 2, right: 14),
                        child: Text('Noter', style: TextStyle(color: AppTheme.promoOrange, fontWeight: FontWeight.w700, fontSize: 12)),
                      ),
                    ),
                  if (_canReturn && l.offerId.isNotEmpty)
                    GestureDetector(
                      onTap: () => onReturn(l),
                      child: const Padding(padding: EdgeInsets.only(top: 2), child: Text('Retourner', style: TextStyle(color: AppTheme.brandGreen, fontWeight: FontWeight.w700, fontSize: 12))),
                    ),
                ]),
              ]),
            ),
            Text(Format.money(l.lineTotal, order.currency), style: const TextStyle(fontWeight: FontWeight.w800)),
          ]),
          const SizedBox(height: 12),
        ],
      ]),
    );
  }
}

class _TotalsCard extends StatelessWidget {
  const _TotalsCard({required this.order});
  final OrderItem order;

  @override
  Widget build(BuildContext context) {
    return CardSection(
      margin: EdgeInsets.zero,
      padding: const EdgeInsets.all(16),
      child: Column(children: [
        // Le « Sous-total » affichait jusqu'ici le TOTAL, et la livraison
        // n'apparaissait nulle part : un article à 1 150 XOF donnait un
        // « sous-total » de 2 650, écart que rien n'expliquait.
        Row(mainAxisAlignment: MainAxisAlignment.spaceBetween, children: [
          Text('Sous-total', style: TextStyle(color: AppTheme.subtle)),
          Text(Format.money(order.subtotal, order.currency), style: const TextStyle(fontWeight: FontWeight.w600)),
        ]),
        // Ligne masquée quand la livraison est offerte ou inconnue : afficher
        // « Livraison 0 » sur une commande ancienne, dont le serveur ne renvoie pas
        // le montant, laisserait croire à une livraison gratuite.
        if (order.shippingFee > 0) ...[
          const SizedBox(height: 8),
          Row(mainAxisAlignment: MainAxisAlignment.spaceBetween, children: [
            Text('Livraison', style: TextStyle(color: AppTheme.subtle)),
            Text(Format.money(order.shippingFee, order.currency),
                style: const TextStyle(fontWeight: FontWeight.w600)),
          ]),
        ],
        Padding(padding: const EdgeInsets.symmetric(vertical: 12), child: Divider(height: 1, color: AppTheme.line)),
        Row(mainAxisAlignment: MainAxisAlignment.spaceBetween, children: [
          const Text('Total', style: TextStyle(fontWeight: FontWeight.w800, fontSize: 16)),
          Text(Format.money(order.total, order.currency), style: const TextStyle(fontWeight: FontWeight.w800, fontSize: 18, color: AppTheme.brandGreen)),
        ]),
      ]),
    );
  }
}
