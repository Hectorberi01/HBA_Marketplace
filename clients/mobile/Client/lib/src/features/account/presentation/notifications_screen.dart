import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';

import '../../../core/theme/app_theme.dart';
import '../../../shared/widgets/ui_kit.dart';

const _ink = Color(0xFF10233A);
const _muted = Color(0xFF728399);
const _pageBg = Color(0xFFF4F6F5);

class NotificationsScreen extends StatefulWidget {
  const NotificationsScreen({super.key});

  @override
  State<NotificationsScreen> createState() => _NotificationsScreenState();
}

class _NotificationsScreenState extends State<NotificationsScreen> {
  int _tab = 0;

  static const _all = [
    _NotificationMock('FOOD', 'Votre commande arrive dans 12 min', 'Ibrahim est en route avec votre commande Chez Mama.', '12:44', _Kind.food),
    _NotificationMock('EXPR', 'Commande confirmée · HBA Tech Store', 'Votre iPhone 14 Pro est en préparation, expédition sous 24 h.', '09:15', _Kind.express),
    _NotificationMock('-20 %', 'Vente flash high-tech', 'Jusqu\'à -40 % sur une sélection HBAExpress, se termine ce soir.', 'Hier', _Kind.promo),
    _NotificationMock('1+1', 'Pizza Bella : 1 achetée = 1 offerte', 'Valable tous les mercredis sur les pizzas 33 cm.', 'Hier', _Kind.promo),
    _NotificationMock('HBA', 'Nouvelle adresse enregistrée', 'Bureau Parakou a été ajouté à vos adresses de livraison.', '12 août', _Kind.express),
  ];

  List<_NotificationMock> get _items => switch (_tab) {
        1 => _all.where((n) => n.kind == _Kind.food || n.kind == _Kind.express).take(2).toList(),
        2 => _all.where((n) => n.kind == _Kind.promo).toList(),
        _ => _all,
      };

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: _pageBg,
      body: SafeArea(
        top: false,
        bottom: false,
        child: Column(
          children: [
            Container(
              color: Colors.white,
              padding: EdgeInsets.fromLTRB(16, MediaQuery.paddingOf(context).top + 16, 16, 24),
              child: Column(
                children: [
                  const _StatusLine(),
                  const SizedBox(height: 18),
                  Row(
                    children: [
                      _SquareButton(icon: Icons.chevron_left_rounded, onTap: () => context.pop()),
                      const SizedBox(width: 14),
                      const Expanded(
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                            Text('Notifications', style: TextStyle(color: _ink, fontSize: 28, fontWeight: FontWeight.w900)),
                            Text('5 non lues', style: TextStyle(color: _muted, fontSize: 15, fontWeight: FontWeight.w600)),
                          ],
                        ),
                      ),
                      TextButton(
                        onPressed: () {},
                        child: const Text('Tout lire', style: TextStyle(color: AppTheme.brandGreen, fontSize: 15, fontWeight: FontWeight.w900)),
                      ),
                    ],
                  ),
                  const SizedBox(height: 22),
                  _SegmentedTabs(
                    labels: const ['Tout', 'Commandes', 'Promotions'],
                    selected: _tab,
                    onChanged: (value) => setState(() => _tab = value),
                  ),
                ],
              ),
            ),
            Expanded(
              child: ListView.separated(
                padding: EdgeInsets.fromLTRB(16, 24, 16, bottomSafePadding(context, extra: 24)),
                itemBuilder: (_, i) => _NotificationCard(item: _items[i]),
                separatorBuilder: (_, __) => const SizedBox(height: 14),
                itemCount: _items.length,
              ),
            ),
          ],
        ),
      ),
    );
  }
}

enum _Kind { food, express, promo }

class _NotificationMock {
  const _NotificationMock(this.code, this.title, this.body, this.time, this.kind);

  final String code;
  final String title;
  final String body;
  final String time;
  final _Kind kind;
}

class _NotificationCard extends StatelessWidget {
  const _NotificationCard({required this.item});

  final _NotificationMock item;

  Color get _chipBg => switch (item.kind) {
        _Kind.food => const Color(0xFFFFF1E2),
        _Kind.express => AppTheme.softGreen,
        _Kind.promo => const Color(0xFFFFEEE7),
      };

  Color get _chipColor => switch (item.kind) {
        _Kind.food || _Kind.promo => const Color(0xFFE56E13),
        _Kind.express => AppTheme.brandGreen,
      };

  @override
  Widget build(BuildContext context) {
    return ConstrainedBox(
      constraints: const BoxConstraints(minHeight: 118),
      child: Container(
        padding: const EdgeInsets.all(18),
        decoration: BoxDecoration(color: Colors.white, borderRadius: BorderRadius.circular(24)),
        child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Container(
            width: 58,
            height: 58,
            alignment: Alignment.center,
            decoration: BoxDecoration(color: _chipBg, borderRadius: BorderRadius.circular(15)),
            child: Text(item.code, style: TextStyle(color: _chipColor, fontSize: 13, fontWeight: FontWeight.w900, letterSpacing: 0)),
          ),
          const SizedBox(width: 18),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Row(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Expanded(
                      child: Text(
                        item.title,
                        maxLines: 2,
                        overflow: TextOverflow.ellipsis,
                        style: const TextStyle(color: _ink, fontSize: 16, fontWeight: FontWeight.w900, height: 1.15),
                      ),
                    ),
                    const SizedBox(width: 8),
                    Text(item.time, style: const TextStyle(color: Color(0xFF92A1B1), fontSize: 13, fontWeight: FontWeight.w600)),
                    const SizedBox(width: 10),
                    const CircleAvatar(radius: 5, backgroundColor: AppTheme.brandGreen),
                  ],
                ),
                const SizedBox(height: 10),
                Text(
                  item.body,
                  maxLines: 3,
                  overflow: TextOverflow.ellipsis,
                  style: const TextStyle(color: _muted, fontSize: 16, fontWeight: FontWeight.w600, height: 1.35),
                ),
              ],
            ),
          ),
        ],
        ),
      ),
    );
  }
}

class _SegmentedTabs extends StatelessWidget {
  const _SegmentedTabs({required this.labels, required this.selected, required this.onChanged});

  final List<String> labels;
  final int selected;
  final ValueChanged<int> onChanged;

  @override
  Widget build(BuildContext context) {
    return Container(
      height: 64,
      padding: const EdgeInsets.all(7),
      decoration: BoxDecoration(color: const Color(0xFFF0F2F3), borderRadius: BorderRadius.circular(18)),
      child: Row(
        children: [
          for (var i = 0; i < labels.length; i++)
            Expanded(
              child: InkWell(
                onTap: () => onChanged(i),
                borderRadius: BorderRadius.circular(15),
                child: AnimatedContainer(
                  duration: const Duration(milliseconds: 160),
                  alignment: Alignment.center,
                  decoration: BoxDecoration(
                    color: selected == i ? Colors.white : Colors.transparent,
                    borderRadius: BorderRadius.circular(15),
                    boxShadow: selected == i
                        ? [BoxShadow(color: Colors.black.withValues(alpha: 0.06), blurRadius: 12, offset: const Offset(0, 4))]
                        : null,
                  ),
                  child: Text(
                    labels[i],
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                    style: TextStyle(
                      color: selected == i ? _ink : _muted,
                      fontSize: 16,
                      fontWeight: FontWeight.w900,
                    ),
                  ),
                ),
              ),
            ),
        ],
      ),
    );
  }
}

class _StatusLine extends StatelessWidget {
  const _StatusLine();

  @override
  Widget build(BuildContext context) {
    return Row(
      children: [
        const Text('9:41', style: TextStyle(color: _ink, fontSize: 16, fontWeight: FontWeight.w900)),
        const Spacer(),
        const Icon(Icons.signal_cellular_alt, color: _ink, size: 18),
        const SizedBox(width: 5),
        Container(
          width: 25,
          height: 13,
          decoration: BoxDecoration(border: Border.all(color: _ink, width: 1.4), borderRadius: BorderRadius.circular(4)),
          child: Align(
            alignment: Alignment.centerLeft,
            child: Container(
              width: 16,
              margin: const EdgeInsets.all(2),
              decoration: BoxDecoration(color: _ink, borderRadius: BorderRadius.circular(2)),
            ),
          ),
        ),
      ],
    );
  }
}

class _SquareButton extends StatelessWidget {
  const _SquareButton({required this.icon, required this.onTap});

  final IconData icon;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    return InkWell(
      onTap: onTap,
      borderRadius: BorderRadius.circular(15),
      child: Container(
        width: 52,
        height: 52,
        decoration: BoxDecoration(color: const Color(0xFFF2F4F5), borderRadius: BorderRadius.circular(15)),
        child: Icon(icon, color: _ink, size: 27),
      ),
    );
  }
}
