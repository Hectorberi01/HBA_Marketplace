import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';

import '../../../core/theme/app_theme.dart';
import '../../../shared/widgets/ui_kit.dart';

const _green = AppTheme.brandGreen;
const _navy = Color(0xFF0E2239);
const _orange = Color(0xFFE56400);

class OrdersScreen extends StatefulWidget {
  const OrdersScreen({super.key});

  @override
  State<OrdersScreen> createState() => _OrdersScreenState();
}

class _OrdersScreenState extends State<OrdersScreen> {
  bool _history = false;
  bool _express = false;

  @override
  Widget build(BuildContext context) {
    final accent = _express ? _green : _orange;
    final orders = _history
        ? (_express ? _expressHistory : _foodHistory)
        : (_express ? _expressCurrent : _foodCurrent);

    return Scaffold(
      backgroundColor: const Color(0xFFF4F6F5),
      body: SafeArea(
        bottom: false,
        child: CustomScrollView(
          slivers: [
            SliverToBoxAdapter(
              child: _OrdersHeader(
                history: _history,
                express: _express,
                onHistoryChanged: (v) => setState(() => _history = v),
                onServiceChanged: (v) => setState(() => _express = v),
              ),
            ),
            SliverToBoxAdapter(
              child: Padding(
                padding: const EdgeInsets.fromLTRB(16, 26, 16, 16),
                child: Row(
                  children: [
                    _Capsule(label: _express ? 'HBAEXPRESS' : 'HBA FOOD', color: accent),
                    const SizedBox(width: 12),
                    Text('${orders.length} commande${orders.length > 1 ? 's' : ''}',
                        style: const TextStyle(color: Color(0xFF65768B), fontSize: 15, fontWeight: FontWeight.w700)),
                  ],
                ),
              ),
            ),
            SliverPadding(
              padding: const EdgeInsets.symmetric(horizontal: 16),
              sliver: SliverList.separated(
                itemCount: orders.length,
                separatorBuilder: (_, __) => const SizedBox(height: 14),
                itemBuilder: (_, index) => _OrderCard(order: orders[index], accent: accent),
              ),
            ),
            SliverToBoxAdapter(child: SizedBox(height: bottomSafePadding(context, extra: 92))),
          ],
        ),
      ),
    );
  }
}

class _OrdersHeader extends StatelessWidget {
  const _OrdersHeader({
    required this.history,
    required this.express,
    required this.onHistoryChanged,
    required this.onServiceChanged,
  });

  final bool history;
  final bool express;
  final ValueChanged<bool> onHistoryChanged;
  final ValueChanged<bool> onServiceChanged;

  @override
  Widget build(BuildContext context) {
    return Container(
      color: Colors.white,
      padding: const EdgeInsets.fromLTRB(18, 18, 18, 24),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          const Text('9:41', style: TextStyle(color: _navy, fontSize: 14, fontWeight: FontWeight.w900)),
          const SizedBox(height: 16),
          const Text('Commandes', style: TextStyle(color: _navy, fontSize: 28, fontWeight: FontWeight.w900)),
          const SizedBox(height: 20),
          Row(
            children: [
              _TopTab(label: 'En cours', selected: !history, color: _green, onTap: () => onHistoryChanged(false)),
              const SizedBox(width: 28),
              _TopTab(label: 'Historique', selected: history, color: _green, onTap: () => onHistoryChanged(true)),
            ],
          ),
          const SizedBox(height: 22),
          Container(
            height: 60,
            padding: const EdgeInsets.all(7),
            decoration: BoxDecoration(color: const Color(0xFFF0F2F4), borderRadius: BorderRadius.circular(18)),
            child: Row(
              children: [
                Expanded(child: _Segment(label: 'HBA Food', selected: !express, onTap: () => onServiceChanged(false))),
                Expanded(child: _Segment(label: 'HBAExpress', selected: express, onTap: () => onServiceChanged(true))),
              ],
            ),
          ),
        ],
      ),
    );
  }
}

class _TopTab extends StatelessWidget {
  const _TopTab({required this.label, required this.selected, required this.color, required this.onTap});
  final String label;
  final bool selected;
  final Color color;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    return GestureDetector(
      onTap: onTap,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(label, style: TextStyle(color: selected ? _navy : const Color(0xFF6F7F91), fontSize: 16, fontWeight: FontWeight.w900)),
          const SizedBox(height: 14),
          Container(width: 64, height: 3, color: selected ? color : Colors.transparent),
        ],
      ),
    );
  }
}

class _Segment extends StatelessWidget {
  const _Segment({required this.label, required this.selected, required this.onTap});
  final String label;
  final bool selected;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    return GestureDetector(
      onTap: onTap,
      child: AnimatedContainer(
        duration: const Duration(milliseconds: 160),
        alignment: Alignment.center,
        decoration: BoxDecoration(
          color: selected ? Colors.white : Colors.transparent,
          borderRadius: BorderRadius.circular(14),
          boxShadow: selected ? [BoxShadow(color: Colors.black.withValues(alpha: 0.08), blurRadius: 12, offset: const Offset(0, 4))] : null,
        ),
        child: Text(label, style: TextStyle(color: selected ? _navy : const Color(0xFF708197), fontSize: 15, fontWeight: FontWeight.w900)),
      ),
    );
  }
}

class _OrderCard extends StatelessWidget {
  const _OrderCard({required this.order, required this.accent});
  final _MockOrder order;
  final Color accent;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.all(18),
      decoration: BoxDecoration(
        color: Colors.white,
        borderRadius: BorderRadius.circular(18),
        boxShadow: [BoxShadow(color: Colors.black.withValues(alpha: 0.05), blurRadius: 18, offset: const Offset(0, 8))],
      ),
      child: Column(
        children: [
          Row(
            children: [
              _Capsule(label: order.kind, color: accent),
              const Spacer(),
              Text(order.date, style: const TextStyle(color: Color(0xFF96A6B6), fontSize: 13, fontWeight: FontWeight.w700)),
            ],
          ),
          const SizedBox(height: 18),
          Row(
            children: [
              Container(width: 58, height: 58, decoration: BoxDecoration(color: order.photoColor, borderRadius: BorderRadius.circular(14))),
              const SizedBox(width: 14),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(order.title, maxLines: 1, overflow: TextOverflow.ellipsis, style: const TextStyle(color: _navy, fontSize: 17, fontWeight: FontWeight.w900)),
                    const SizedBox(height: 2),
                    Text(order.subtitle, maxLines: 1, overflow: TextOverflow.ellipsis, style: const TextStyle(color: Color(0xFF65768B), fontSize: 13, fontWeight: FontWeight.w600)),
                  ],
                ),
              ),
              const SizedBox(width: 8),
              Text(order.amount, style: const TextStyle(color: _navy, fontSize: 17, fontWeight: FontWeight.w900)),
            ],
          ),
          const SizedBox(height: 20),
          Row(
            children: [
              Icon(Icons.circle, color: order.statusColor, size: 9),
              const SizedBox(width: 8),
              Expanded(child: Text(order.status, maxLines: 1, overflow: TextOverflow.ellipsis, style: TextStyle(color: order.statusColor, fontSize: 14, fontWeight: FontWeight.w800))),
              if (order.action != null)
                FilledButton(
                  onPressed: () => order.track ? context.go('/order/food-current/tracking') : null,
                  style: FilledButton.styleFrom(
                    backgroundColor: order.actionColor,
                    foregroundColor: order.actionTextColor,
                    elevation: 0,
                    minimumSize: const Size(82, 42),
                    padding: const EdgeInsets.symmetric(horizontal: 16),
                    tapTargetSize: MaterialTapTargetSize.shrinkWrap,
                    shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(14)),
                  ),
                  child: Text(order.action!, style: const TextStyle(fontSize: 14, fontWeight: FontWeight.w900)),
                ),
            ],
          ),
        ],
      ),
    );
  }
}

class _Capsule extends StatelessWidget {
  const _Capsule({required this.label, required this.color});
  final String label;
  final Color color;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 6),
      decoration: BoxDecoration(color: color.withValues(alpha: 0.11), borderRadius: BorderRadius.circular(9)),
      child: Text(label, style: TextStyle(color: color, fontSize: 11, letterSpacing: 1.2, fontWeight: FontWeight.w900)),
    );
  }
}

class _MockOrder {
  const _MockOrder({
    required this.kind,
    required this.date,
    required this.title,
    required this.subtitle,
    required this.amount,
    required this.status,
    required this.statusColor,
    required this.photoColor,
    this.action,
    this.actionColor = const Color(0xFFF2F4F6),
    this.actionTextColor = _navy,
    this.track = false,
  });

  final String kind;
  final String date;
  final String title;
  final String subtitle;
  final String amount;
  final String status;
  final Color statusColor;
  final Color photoColor;
  final String? action;
  final Color actionColor;
  final Color actionTextColor;
  final bool track;
}

const _foodCurrent = [
  _MockOrder(
    kind: 'FOOD',
    date: "Aujourd'hui 12:40",
    title: 'Chez Mama',
    subtitle: 'Poulet braisé, Jus de bissap',
    amount: '12 500 F',
    status: 'En livraison · 12 min',
    statusColor: _green,
    photoColor: Color(0xFFE8DDCF),
    action: 'Suivre',
    actionColor: Color(0xFFE1F4EC),
    actionTextColor: _green,
    track: true,
  ),
];

const _expressCurrent = [
  _MockOrder(
    kind: 'EXPRESS',
    date: "Aujourd'hui 09:15",
    title: 'HBA Tech Store',
    subtitle: 'iPhone 14 Pro, Sneakers',
    amount: '520 000 F',
    status: 'En préparation',
    statusColor: _orange,
    photoColor: Color(0xFFE5EAF0),
    action: 'Voir',
  ),
];

const _foodHistory = [
  _MockOrder(kind: 'FOOD', date: '9 août', title: 'Chicken House', subtitle: 'Menu poulet épicé ×2', amount: '9 800 F', status: 'Livrée', statusColor: Color(0xFF718196), photoColor: Color(0xFFE9E3D6), action: 'Commander à nouveau', actionColor: Color(0xFFFFF0DF), actionTextColor: _orange),
  _MockOrder(kind: 'FOOD', date: '28 juillet', title: 'Pizza Bella', subtitle: 'Pizza Margherita', amount: '7 200 F', status: 'Annulée', statusColor: Color(0xFFF05243), photoColor: Color(0xFFE7DDD3), action: 'Détails'),
];

const _expressHistory = [
  _MockOrder(kind: 'EXPRESS', date: '2 août', title: 'Time & Co', subtitle: 'Montre Classic Steel', amount: '89 000 F', status: 'Livrée', statusColor: Color(0xFF718196), photoColor: Color(0xFFE6E9EE), action: 'Voir la facture'),
];

class OrderStatusChip extends StatelessWidget {
  const OrderStatusChip({super.key, required this.status});
  final String status;

  @override
  Widget build(BuildContext context) {
    final (color, label, icon) = _style(status);
    return StatusBadge(label: label, color: color, icon: icon);
  }

  (Color, String, IconData) _style(String status) {
    switch (status.toLowerCase()) {
      case 'paid':
      case 'confirmed':
        return (_green, 'Payée', Icons.check_circle_outline);
      case 'processing':
        return (_orange, 'En préparation', Icons.inventory_2_outlined);
      case 'shipped':
        return (_orange, 'Expédiée', Icons.local_shipping_outlined);
      case 'delivered':
        return (_green, 'Livrée', Icons.check_circle_outline);
      case 'cancelled':
        return (AppTheme.danger, 'Annulée', Icons.cancel_outlined);
      case 'pending':
      case 'awaitingpayment':
        return (const Color(0xFF8A8F8C), 'En attente', Icons.schedule);
      default:
        return (const Color(0xFF8A8F8C), status, Icons.info_outline);
    }
  }
}
