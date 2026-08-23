import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';

import '../../../core/theme/app_theme.dart';
import '../../../shared/utils/formatters.dart';
import '../../../shared/widgets/ui_kit.dart';

const _green = AppTheme.brandGreen;
const _navy = Color(0xFF0E2239);
const _danger = Color(0xFFF05243);

class ShopScreen extends StatelessWidget {
  const ShopScreen({super.key, required this.sellerId});
  final String sellerId;

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: const Color(0xFFF4F6F5),
      body: SafeArea(
        bottom: false,
        child: CustomScrollView(
          slivers: [
            SliverToBoxAdapter(child: _ShopHero(onBack: () => context.canPop() ? context.pop() : context.go('/express'))),
            const SliverToBoxAdapter(child: _ShopPromo()),
            const SliverToBoxAdapter(child: _ShopTabs()),
            const SliverToBoxAdapter(child: _ProductsHeader()),
            const SliverPadding(
              padding: EdgeInsets.fromLTRB(16, 0, 16, 0),
              sliver: SliverGrid(
                gridDelegate: SliverGridDelegateWithFixedCrossAxisCount(crossAxisCount: 2, crossAxisSpacing: 12, mainAxisSpacing: 12, childAspectRatio: 0.62),
                delegate: SliverChildListDelegate.fixed([
                  _ShopProduct(name: 'Sony WH-1000XM5', price: 279000, favorite: true, color: Color(0xFFE5EAF0)),
                  _ShopProduct(name: 'iPhone 14 Pro 128 Go', price: 620000, color: Color(0xFFE8EEF2)),
                ]),
              ),
            ),
            SliverToBoxAdapter(child: SizedBox(height: bottomSafePadding(context, extra: 92))),
          ],
        ),
      ),
    );
  }
}

class _ShopHero extends StatelessWidget {
  const _ShopHero({required this.onBack});
  final VoidCallback onBack;

  @override
  Widget build(BuildContext context) {
    return SizedBox(
      height: 438,
      child: Stack(
        children: [
          Container(
            height: 250,
            color: const Color(0xFFE1E8ED),
            alignment: Alignment.center,
            child: const Text('BANNIÈRE BOUTIQUE', style: TextStyle(color: Color(0xFF9EAEBB), fontSize: 11, letterSpacing: 3, fontWeight: FontWeight.w900)),
          ),
          Positioned(left: 18, top: 18, child: _SquareIcon(icon: Icons.chevron_left_rounded, onTap: onBack)),
          const Positioned(right: 82, top: 18, child: _SquareIcon(icon: Icons.favorite_border_rounded)),
          const Positioned(right: 18, top: 18, child: _SquareIcon(icon: Icons.search_rounded)),
          const Positioned(left: 24, top: 22, child: Text('9:41', style: TextStyle(color: _navy, fontSize: 14, fontWeight: FontWeight.w900))),
          Positioned(
            left: 16,
            right: 16,
            bottom: 0,
            child: Container(
              padding: const EdgeInsets.all(20),
              decoration: BoxDecoration(color: Colors.white, borderRadius: BorderRadius.circular(24), boxShadow: [BoxShadow(color: Colors.black.withValues(alpha: 0.05), blurRadius: 18, offset: const Offset(0, 8))]),
              child: Column(
                children: [
                  const Row(
                    children: [
                      _Avatar(label: 'HT', color: Color(0xFFE5EAF0)),
                      SizedBox(width: 16),
                      Expanded(
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                            Text('HBA Tech Store', style: TextStyle(color: _navy, fontSize: 22, fontWeight: FontWeight.w900)),
                            Text('Électronique · Cotonou', style: TextStyle(color: Color(0xFF66778C), fontSize: 14, fontWeight: FontWeight.w600)),
                            SizedBox(height: 10),
                            _VerifiedChip(),
                          ],
                        ),
                      ),
                    ],
                  ),
                  const SizedBox(height: 22),
                  const Row(
                    children: [
                      _StatBox(value: '★ 4,9', label: '1240 avis'),
                      SizedBox(width: 10),
                      _StatBox(value: '186', label: 'Produits'),
                      SizedBox(width: 10),
                      _StatBox(value: '24 h', label: 'Expédition'),
                    ],
                  ),
                  const SizedBox(height: 18),
                  Row(
                    children: [
                      Expanded(
                        child: FilledButton(
                          onPressed: () {},
                          style: FilledButton.styleFrom(backgroundColor: _green, minimumSize: const Size.fromHeight(54), shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(16))),
                          child: const Text('Suivre la boutique', style: TextStyle(fontSize: 15, fontWeight: FontWeight.w900)),
                        ),
                      ),
                      const SizedBox(width: 12),
                      OutlinedButton.icon(
                        onPressed: () => context.push('/chat/hba-tech-store'),
                        style: OutlinedButton.styleFrom(foregroundColor: _navy, side: const BorderSide(color: Color(0xFFD8E0E7)), minimumSize: const Size(130, 54), shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(16))),
                        icon: const Icon(Icons.chat_bubble_outline_rounded, color: _green, size: 18),
                        label: const Text('Contacter', style: TextStyle(fontSize: 14, fontWeight: FontWeight.w900)),
                      ),
                    ],
                  ),
                ],
              ),
            ),
          ),
        ],
      ),
    );
  }
}

class _ShopPromo extends StatelessWidget {
  const _ShopPromo();

  @override
  Widget build(BuildContext context) {
    return Container(
      margin: const EdgeInsets.fromLTRB(16, 20, 16, 20),
      padding: const EdgeInsets.all(16),
      decoration: BoxDecoration(color: const Color(0xFFE1F4EC), borderRadius: BorderRadius.circular(18)),
      child: Row(
        children: [
          Container(width: 50, height: 50, decoration: BoxDecoration(color: _green, borderRadius: BorderRadius.circular(14)), alignment: Alignment.center, child: const Text('%', style: TextStyle(color: Colors.white, fontSize: 18, fontWeight: FontWeight.w900))),
          const SizedBox(width: 16),
          const Expanded(child: Text("-10 % dès 100 000 F CFA d’achat", style: TextStyle(color: Color(0xFF065D49), fontSize: 15, fontWeight: FontWeight.w900))),
          FilledButton(
            onPressed: () {},
            style: FilledButton.styleFrom(backgroundColor: _green, minimumSize: const Size(86, 42), shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(14))),
            child: const Text('Obtenir', style: TextStyle(fontWeight: FontWeight.w900)),
          ),
        ],
      ),
    );
  }
}

class _ShopTabs extends StatelessWidget {
  const _ShopTabs();

  @override
  Widget build(BuildContext context) {
    return SizedBox(
      height: 46,
      child: ListView(
        padding: const EdgeInsets.symmetric(horizontal: 16),
        scrollDirection: Axis.horizontal,
        children: const [
          _TabPill(label: 'Tous les produits', selected: true),
          SizedBox(width: 10),
          _TabPill(label: 'Nouveautés'),
          SizedBox(width: 10),
          _TabPill(label: 'Meilleures ventes'),
          SizedBox(width: 10),
          _TabPill(label: 'Promotions'),
        ],
      ),
    );
  }
}

class _ProductsHeader extends StatelessWidget {
  const _ProductsHeader();

  @override
  Widget build(BuildContext context) {
    return const Padding(
      padding: EdgeInsets.fromLTRB(16, 24, 16, 14),
      child: Row(
        children: [
          Expanded(child: Text('Tous les produits', style: TextStyle(color: _navy, fontSize: 19, fontWeight: FontWeight.w900))),
          Text('2 articles', style: TextStyle(color: Color(0xFF66778C), fontSize: 14, fontWeight: FontWeight.w600)),
        ],
      ),
    );
  }
}

class _ShopProduct extends StatelessWidget {
  const _ShopProduct({required this.name, required this.price, required this.color, this.favorite = false});
  final String name;
  final int price;
  final Color color;
  final bool favorite;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.all(10),
      decoration: BoxDecoration(color: Colors.white, borderRadius: BorderRadius.circular(18)),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Stack(
            children: [
              Container(height: 138, decoration: BoxDecoration(color: color, borderRadius: BorderRadius.circular(14)), alignment: Alignment.center, child: const Text('PHOTO', style: TextStyle(color: Color(0xFF9EAFBC), fontSize: 9, letterSpacing: 2, fontWeight: FontWeight.w900))),
              Positioned(
                right: 8,
                top: 8,
                child: Container(width: 34, height: 34, decoration: BoxDecoration(color: Colors.white, borderRadius: BorderRadius.circular(11)), child: Icon(favorite ? Icons.favorite_rounded : Icons.favorite_border_rounded, color: favorite ? _danger : _navy, size: 17)),
              ),
            ],
          ),
          const SizedBox(height: 14),
          Text(name, maxLines: 2, overflow: TextOverflow.ellipsis, style: const TextStyle(color: _navy, fontSize: 14, fontWeight: FontWeight.w900)),
          const Spacer(),
          Text(Format.money(price, 'XOF'), maxLines: 1, overflow: TextOverflow.ellipsis, style: const TextStyle(color: _green, fontSize: 16, fontWeight: FontWeight.w900)),
        ],
      ),
    );
  }
}

class _SquareIcon extends StatelessWidget {
  const _SquareIcon({required this.icon, this.onTap});
  final IconData icon;
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) {
    return GestureDetector(onTap: onTap, child: Container(width: 54, height: 54, decoration: BoxDecoration(color: Colors.white, borderRadius: BorderRadius.circular(16), boxShadow: [BoxShadow(color: Colors.black.withValues(alpha: 0.08), blurRadius: 14, offset: const Offset(0, 6))]), child: Icon(icon, color: _navy)));
  }
}

class _Avatar extends StatelessWidget {
  const _Avatar({required this.label, required this.color});
  final String label;
  final Color color;

  @override
  Widget build(BuildContext context) {
    return Container(width: 70, height: 70, decoration: BoxDecoration(color: color, borderRadius: BorderRadius.circular(18)), alignment: Alignment.center, child: Text(label, style: const TextStyle(color: _navy, fontSize: 19, fontWeight: FontWeight.w900)));
  }
}

class _VerifiedChip extends StatelessWidget {
  const _VerifiedChip();

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 6),
      decoration: BoxDecoration(color: const Color(0xFFE1F4EC), borderRadius: BorderRadius.circular(10)),
      child: const Row(mainAxisSize: MainAxisSize.min, children: [
        Icon(Icons.check_rounded, color: _green, size: 14),
        SizedBox(width: 5),
        Text('Vendeur vérifié', style: TextStyle(color: _green, fontSize: 12, fontWeight: FontWeight.w900)),
      ]),
    );
  }
}

class _StatBox extends StatelessWidget {
  const _StatBox({required this.value, required this.label});
  final String value;
  final String label;

  @override
  Widget build(BuildContext context) {
    return Expanded(
      child: Container(
        height: 70,
        decoration: BoxDecoration(color: const Color(0xFFF4F6F5), borderRadius: BorderRadius.circular(14)),
        child: Column(
          mainAxisAlignment: MainAxisAlignment.center,
          children: [
            Text(value, style: const TextStyle(color: _navy, fontSize: 16, fontWeight: FontWeight.w900)),
            const SizedBox(height: 3),
            Text(label, style: const TextStyle(color: Color(0xFF66778C), fontSize: 12, fontWeight: FontWeight.w600)),
          ],
        ),
      ),
    );
  }
}

class _TabPill extends StatelessWidget {
  const _TabPill({required this.label, this.selected = false});
  final String label;
  final bool selected;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 18),
      alignment: Alignment.center,
      decoration: BoxDecoration(color: selected ? _green : Colors.white, borderRadius: BorderRadius.circular(14)),
      child: Text(label, style: TextStyle(color: selected ? Colors.white : _navy, fontSize: 14, fontWeight: FontWeight.w900)),
    );
  }
}
