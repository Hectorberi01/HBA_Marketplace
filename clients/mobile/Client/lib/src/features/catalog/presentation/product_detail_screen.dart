import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';

import '../../../core/theme/app_theme.dart';
import '../../../shared/utils/formatters.dart';
import '../../../shared/widgets/ui_kit.dart';

const _green = AppTheme.brandGreen;
const _navy = Color(0xFF0E2239);
const _orange = Color(0xFFFF9F1C);
const _danger = Color(0xFFF05243);

class ProductDetailScreen extends StatelessWidget {
  const ProductDetailScreen({super.key, required this.productId});
  final String productId;

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: const Color(0xFFF4F6F5),
      body: SafeArea(
        bottom: false,
        child: Stack(
          children: [
            CustomScrollView(
              slivers: [
                SliverToBoxAdapter(child: _Gallery(onBack: () => context.canPop() ? context.pop() : context.go('/express'))),
                const SliverToBoxAdapter(child: _ProductSummary()),
                const SliverToBoxAdapter(child: _OptionsCard()),
                const SliverToBoxAdapter(child: _SellerDeliveryCard()),
                const SliverToBoxAdapter(child: _DescriptionCard()),
                const SliverToBoxAdapter(child: _SimilarProducts()),
                SliverToBoxAdapter(child: SizedBox(height: bottomSafePadding(context, extra: 104))),
              ],
            ),
            const Positioned(left: 0, right: 0, bottom: 0, child: _BottomActions()),
          ],
        ),
      ),
    );
  }
}

class _Gallery extends StatelessWidget {
  const _Gallery({required this.onBack});
  final VoidCallback onBack;

  @override
  Widget build(BuildContext context) {
    return SizedBox(
      height: 320,
      child: Stack(
        children: [
          Container(
            color: const Color(0xFFE1E8ED),
            alignment: Alignment.center,
            child: const Text('GALERIE PRODUIT', style: TextStyle(color: Color(0xFF9EAEBB), fontSize: 11, letterSpacing: 3, fontWeight: FontWeight.w900)),
          ),
          const Positioned(
            left: 0,
            right: 0,
            bottom: 18,
            child: Row(
              mainAxisAlignment: MainAxisAlignment.center,
              children: [
                _Dot(active: true),
                SizedBox(width: 8),
                _Dot(active: false),
                SizedBox(width: 8),
                _Dot(active: false),
              ],
            ),
          ),
          Positioned(left: 18, top: 18, child: _SquareIcon(icon: Icons.chevron_left_rounded, onTap: onBack)),
          const Positioned(right: 82, top: 18, child: _SquareIcon(icon: Icons.favorite_rounded, color: _danger)),
          const Positioned(right: 18, top: 18, child: _SquareIcon(icon: Icons.ios_share_rounded)),
          const Positioned(left: 24, top: 22, child: Text('9:41', style: TextStyle(color: _navy, fontSize: 14, fontWeight: FontWeight.w900))),
        ],
      ),
    );
  }
}

class _ProductSummary extends StatelessWidget {
  const _ProductSummary();

  @override
  Widget build(BuildContext context) {
    return Transform.translate(
      offset: const Offset(0, -28),
      child: _Card(
        margin: const EdgeInsets.symmetric(horizontal: 16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            const Text('Sony WH-1000XM5', style: TextStyle(color: _navy, fontSize: 23, fontWeight: FontWeight.w900)),
            const SizedBox(height: 10),
            const Row(
              children: [
                Text('★★★★★', style: TextStyle(color: _orange, fontSize: 16, fontWeight: FontWeight.w900)),
                SizedBox(width: 12),
                Text('4,9', style: TextStyle(color: _navy, fontSize: 14, fontWeight: FontWeight.w900)),
                SizedBox(width: 8),
                Text('· 412 avis', style: TextStyle(color: Color(0xFF66778C), fontSize: 14, fontWeight: FontWeight.w600)),
              ],
            ),
            const SizedBox(height: 14),
            Row(
              children: [
                Expanded(child: Text(Format.money(279000, 'XOF'), style: const TextStyle(color: _green, fontSize: 26, fontWeight: FontWeight.w900))),
                const Text('325 000 F CFA', style: TextStyle(color: Color(0xFF9AA8B6), fontSize: 14, decoration: TextDecoration.lineThrough, fontWeight: FontWeight.w700)),
                const SizedBox(width: 10),
                const _DiscountChip(),
              ],
            ),
            const SizedBox(height: 14),
            const Row(
              children: [
                Icon(Icons.circle, color: _green, size: 9),
                SizedBox(width: 8),
                Text('En stock · 12 disponibles', style: TextStyle(color: _green, fontSize: 14, fontWeight: FontWeight.w800)),
              ],
            ),
          ],
        ),
      ),
    );
  }
}

class _OptionsCard extends StatelessWidget {
  const _OptionsCard();

  @override
  Widget build(BuildContext context) {
    return Transform.translate(
      offset: const Offset(0, -10),
      child: const _Card(
        margin: EdgeInsets.fromLTRB(16, 0, 16, 16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text('Couleur', style: TextStyle(color: _navy, fontSize: 16, fontWeight: FontWeight.w900)),
            SizedBox(height: 16),
            Wrap(
              spacing: 10,
              children: [
                _Choice(label: 'Noir', selected: true),
                _Choice(label: 'Argent'),
                _Choice(label: 'Bleu nuit'),
              ],
            ),
          ],
        ),
      ),
    );
  }
}

class _SellerDeliveryCard extends StatelessWidget {
  const _SellerDeliveryCard();

  @override
  Widget build(BuildContext context) {
    return _Card(
      margin: const EdgeInsets.fromLTRB(16, 0, 16, 16),
      child: Column(
        children: [
          Row(
            children: [
              const _Avatar(label: 'HT', color: Color(0xFFE1F4EC), textColor: _green),
              const SizedBox(width: 14),
              const Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text('HBA Tech Store', style: TextStyle(color: _navy, fontSize: 16, fontWeight: FontWeight.w900)),
                    Text("Vendeur vérifié · 98 % d'avis\npositifs", style: TextStyle(color: Color(0xFF66778C), fontSize: 13, height: 1.15, fontWeight: FontWeight.w600)),
                  ],
                ),
              ),
              _IconPill(icon: Icons.chat_bubble_outline_rounded, onTap: () => context.push('/chat/hba-tech-store')),
              const SizedBox(width: 8),
              FilledButton(
                onPressed: () => context.push('/shop/hba-tech-store'),
                style: FilledButton.styleFrom(backgroundColor: const Color(0xFFE1F4EC), foregroundColor: _green, elevation: 0, minimumSize: const Size(92, 46), shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(14))),
                child: const Text('Boutique', style: TextStyle(fontWeight: FontWeight.w900)),
              ),
            ],
          ),
          const Padding(padding: EdgeInsets.symmetric(vertical: 18), child: Divider(height: 1, color: Color(0xFFE1E7EC))),
          const Row(
            children: [
              _Avatar(label: '', color: Color(0xFFEAF0F4), icon: Icons.local_shipping_outlined),
              SizedBox(width: 14),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text('HBA Delivery · 2 500 F CFA', style: TextStyle(color: _navy, fontSize: 16, fontWeight: FontWeight.w900)),
                    Text('Livraison estimée mercredi 14 août', style: TextStyle(color: Color(0xFF66778C), fontSize: 13, fontWeight: FontWeight.w600)),
                  ],
                ),
              ),
            ],
          ),
        ],
      ),
    );
  }
}

class _DescriptionCard extends StatelessWidget {
  const _DescriptionCard();

  @override
  Widget build(BuildContext context) {
    return const _Card(
      margin: EdgeInsets.fromLTRB(16, 0, 16, 18),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text('Description', style: TextStyle(color: _navy, fontSize: 17, fontWeight: FontWeight.w900)),
          SizedBox(height: 16),
          Text('Casque à réduction de bruit active, autonomie 30 h, son haute résolution. Livré avec étui rigide et câble jack.',
              style: TextStyle(color: Color(0xFF66778C), fontSize: 15, height: 1.45, fontWeight: FontWeight.w600)),
          SizedBox(height: 22),
          _SpecRow(label: 'Autonomie', value: '30 heures'),
          _SpecRow(label: 'Bluetooth', value: '5.2'),
          _SpecRow(label: 'Poids', value: '250 g'),
        ],
      ),
    );
  }
}

class _SimilarProducts extends StatelessWidget {
  const _SimilarProducts();

  @override
  Widget build(BuildContext context) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        const Padding(
          padding: EdgeInsets.fromLTRB(16, 6, 16, 14),
          child: Text('Produits similaires', style: TextStyle(color: _navy, fontSize: 20, fontWeight: FontWeight.w900)),
        ),
        SizedBox(
          height: 220,
          child: ListView.separated(
            padding: const EdgeInsets.symmetric(horizontal: 16),
            scrollDirection: Axis.horizontal,
            itemCount: _similar.length,
            separatorBuilder: (_, __) => const SizedBox(width: 14),
            itemBuilder: (_, i) => _SimilarCard(item: _similar[i]),
          ),
        ),
      ],
    );
  }
}

class _BottomActions extends StatelessWidget {
  const _BottomActions();

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: EdgeInsets.fromLTRB(16, 14, 16, bottomSafePadding(context, extra: 14)),
      decoration: const BoxDecoration(color: Colors.white, border: Border(top: BorderSide(color: Color(0xFFE1E7EC)))),
      child: Row(
        children: [
          Expanded(
            child: OutlinedButton(
              onPressed: () => context.go('/cart'),
              style: OutlinedButton.styleFrom(foregroundColor: _green, side: const BorderSide(color: _green, width: 1.5), minimumSize: const Size.fromHeight(58), shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(16))),
              child: const Text('Ajouter au panier', style: TextStyle(fontSize: 15, fontWeight: FontWeight.w900)),
            ),
          ),
          const SizedBox(width: 12),
          Expanded(
            child: FilledButton(
              onPressed: () => context.go('/checkout'),
              style: FilledButton.styleFrom(backgroundColor: _green, minimumSize: const Size.fromHeight(58), shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(16))),
              child: const Text('Acheter maintenant', style: TextStyle(fontSize: 15, fontWeight: FontWeight.w900)),
            ),
          ),
        ],
      ),
    );
  }
}

class _Card extends StatelessWidget {
  const _Card({required this.child, required this.margin});
  final Widget child;
  final EdgeInsetsGeometry margin;

  @override
  Widget build(BuildContext context) {
    return Container(
      margin: margin,
      padding: const EdgeInsets.all(18),
      decoration: BoxDecoration(color: Colors.white, borderRadius: BorderRadius.circular(22), boxShadow: [BoxShadow(color: Colors.black.withValues(alpha: 0.04), blurRadius: 18, offset: const Offset(0, 8))]),
      child: child,
    );
  }
}

class _SquareIcon extends StatelessWidget {
  const _SquareIcon({required this.icon, this.onTap, this.color = _navy});
  final IconData icon;
  final VoidCallback? onTap;
  final Color color;

  @override
  Widget build(BuildContext context) {
    return GestureDetector(
      onTap: onTap,
      child: Container(width: 54, height: 54, decoration: BoxDecoration(color: Colors.white, borderRadius: BorderRadius.circular(16), boxShadow: [BoxShadow(color: Colors.black.withValues(alpha: 0.08), blurRadius: 14, offset: const Offset(0, 6))]), child: Icon(icon, color: color)),
    );
  }
}

class _Dot extends StatelessWidget {
  const _Dot({required this.active});
  final bool active;

  @override
  Widget build(BuildContext context) {
    return Container(width: active ? 28 : 7, height: 7, decoration: BoxDecoration(color: active ? _navy : const Color(0xFFB6C1CC), borderRadius: BorderRadius.circular(99)));
  }
}

class _DiscountChip extends StatelessWidget {
  const _DiscountChip();

  @override
  Widget build(BuildContext context) {
    return Container(padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 7), decoration: BoxDecoration(color: const Color(0xFFFFE8DF), borderRadius: BorderRadius.circular(10)), child: const Text('-14%', style: TextStyle(color: _danger, fontSize: 13, fontWeight: FontWeight.w900)));
  }
}

class _Choice extends StatelessWidget {
  const _Choice({required this.label, this.selected = false});
  final String label;
  final bool selected;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 20, vertical: 13),
      decoration: BoxDecoration(color: selected ? const Color(0xFFE1F4EC) : Colors.white, borderRadius: BorderRadius.circular(13), border: Border.all(color: selected ? _green : const Color(0xFFDDE5EB), width: 1.4)),
      child: Text(label, style: TextStyle(color: _navy, fontSize: 14, fontWeight: selected ? FontWeight.w900 : FontWeight.w800)),
    );
  }
}

class _Avatar extends StatelessWidget {
  const _Avatar({required this.label, required this.color, this.textColor = _navy, this.icon});
  final String label;
  final Color color;
  final Color textColor;
  final IconData? icon;

  @override
  Widget build(BuildContext context) {
    return Container(width: 56, height: 56, decoration: BoxDecoration(color: color, borderRadius: BorderRadius.circular(14)), alignment: Alignment.center, child: icon == null ? Text(label, style: TextStyle(color: textColor, fontSize: 15, fontWeight: FontWeight.w900)) : Icon(icon, color: _navy));
  }
}

class _IconPill extends StatelessWidget {
  const _IconPill({required this.icon, this.onTap});
  final IconData icon;
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) {
    return GestureDetector(onTap: onTap, child: Container(width: 46, height: 46, decoration: BoxDecoration(color: const Color(0xFFF4F6F5), borderRadius: BorderRadius.circular(14)), child: Icon(icon, color: _green, size: 20)));
  }
}

class _SpecRow extends StatelessWidget {
  const _SpecRow({required this.label, required this.value});
  final String label;
  final String value;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.only(bottom: 13),
      child: Row(children: [Expanded(child: Text(label, style: const TextStyle(color: Color(0xFF66778C), fontSize: 14, fontWeight: FontWeight.w600))), Text(value, style: const TextStyle(color: _navy, fontSize: 14, fontWeight: FontWeight.w900))]),
    );
  }
}

class _SimilarCard extends StatelessWidget {
  const _SimilarCard({required this.item});
  final _Similar item;

  @override
  Widget build(BuildContext context) {
    return SizedBox(
      width: 148,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Container(height: 138, decoration: BoxDecoration(color: item.color, borderRadius: BorderRadius.circular(16)), alignment: Alignment.center, child: const Text('PHOTO', style: TextStyle(color: Color(0xFF9EAFBC), fontSize: 9, letterSpacing: 2, fontWeight: FontWeight.w900))),
          const SizedBox(height: 10),
          Text(item.name, maxLines: 1, overflow: TextOverflow.ellipsis, style: const TextStyle(color: _navy, fontSize: 14, fontWeight: FontWeight.w900)),
          Text(Format.money(item.price, 'XOF'), maxLines: 1, overflow: TextOverflow.ellipsis, style: const TextStyle(color: _green, fontSize: 14, fontWeight: FontWeight.w900)),
        ],
      ),
    );
  }
}

class _Similar {
  const _Similar(this.name, this.price, this.color);
  final String name;
  final int price;
  final Color color;
}

const _similar = [
  _Similar('iPhone 14 Pro 128 Go', 620000, Color(0xFFE5EAF0)),
  _Similar('Sneakers Air Runner', 42500, Color(0xFFECE6DF)),
  _Similar('Montre Classic Steel', 89000, Color(0xFFE6E9EE)),
];
