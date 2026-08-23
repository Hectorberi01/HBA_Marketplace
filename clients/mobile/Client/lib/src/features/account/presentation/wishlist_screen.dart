import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';

import '../../../core/theme/app_theme.dart';
import '../../../shared/utils/formatters.dart';
import '../../../shared/widgets/ui_kit.dart';

const _green = AppTheme.brandGreen;
const _navy = Color(0xFF0E2239);
const _danger = Color(0xFFF05243);

class WishlistScreen extends StatefulWidget {
  const WishlistScreen({super.key});

  @override
  State<WishlistScreen> createState() => _WishlistScreenState();
}

class _WishlistScreenState extends State<WishlistScreen> {
  int _tab = 0;

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: const Color(0xFFF4F6F5),
      body: SafeArea(
        bottom: false,
        child: CustomScrollView(
          slivers: [
            SliverToBoxAdapter(
              child: Container(
                color: Colors.white,
                padding: const EdgeInsets.fromLTRB(18, 18, 18, 24),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    const Text('9:41', style: TextStyle(color: _navy, fontSize: 14, fontWeight: FontWeight.w900)),
                    const SizedBox(height: 16),
                    const Text('Favoris', style: TextStyle(color: _navy, fontSize: 28, fontWeight: FontWeight.w900)),
                    const SizedBox(height: 22),
                    _Tabs(selected: _tab, onChanged: (v) => setState(() => _tab = v)),
                  ],
                ),
              ),
            ),
            if (_tab == 0)
              const SliverPadding(
                padding: EdgeInsets.fromLTRB(16, 26, 16, 0),
                sliver: SliverGrid(
                  gridDelegate: SliverGridDelegateWithFixedCrossAxisCount(crossAxisCount: 2, crossAxisSpacing: 12, mainAxisSpacing: 12, childAspectRatio: 0.72),
                  delegate: SliverChildListDelegate.fixed([
                    _FavoriteProduct(name: 'Sony WH-1000XM5', price: 279000, color: Color(0xFFE5EAF0)),
                    _FavoriteProduct(name: 'Montre Classic Steel', price: 89000, color: Color(0xFFE6E7EA)),
                  ]),
                ),
              )
            else if (_tab == 1)
              const SliverToBoxAdapter(child: _RestaurantFavorite())
            else
              const SliverToBoxAdapter(child: _ShopFavorites()),
            SliverToBoxAdapter(child: SizedBox(height: bottomSafePadding(context, extra: 92))),
          ],
        ),
      ),
    );
  }
}

class _Tabs extends StatelessWidget {
  const _Tabs({required this.selected, required this.onChanged});
  final int selected;
  final ValueChanged<int> onChanged;

  @override
  Widget build(BuildContext context) {
    return Container(
      height: 60,
      padding: const EdgeInsets.all(7),
      decoration: BoxDecoration(color: const Color(0xFFF0F2F4), borderRadius: BorderRadius.circular(18)),
      child: Row(
        children: [
          Expanded(child: _TabButton(label: 'Produits', selected: selected == 0, onTap: () => onChanged(0))),
          Expanded(child: _TabButton(label: 'Restaurants', selected: selected == 1, onTap: () => onChanged(1))),
          Expanded(child: _TabButton(label: 'Boutiques', selected: selected == 2, onTap: () => onChanged(2))),
        ],
      ),
    );
  }
}

class _TabButton extends StatelessWidget {
  const _TabButton({required this.label, required this.selected, required this.onTap});
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
        child: Text(label, maxLines: 1, overflow: TextOverflow.ellipsis, style: TextStyle(color: selected ? _navy : const Color(0xFF708197), fontSize: 14, fontWeight: FontWeight.w900)),
      ),
    );
  }
}

class _FavoriteProduct extends StatelessWidget {
  const _FavoriteProduct({required this.name, required this.price, required this.color});
  final String name;
  final int price;
  final Color color;

  @override
  Widget build(BuildContext context) {
    return InkWell(
      borderRadius: BorderRadius.circular(18),
      onTap: () => context.push('/product/${name.toLowerCase().replaceAll(' ', '-')}'),
      child: Container(
        padding: const EdgeInsets.all(10),
        decoration: BoxDecoration(color: Colors.white, borderRadius: BorderRadius.circular(18), boxShadow: [BoxShadow(color: Colors.black.withValues(alpha: 0.04), blurRadius: 18, offset: const Offset(0, 8))]),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Stack(
              children: [
                Container(
                  height: 128,
                  decoration: BoxDecoration(color: color, borderRadius: BorderRadius.circular(14)),
                  alignment: Alignment.center,
                  child: const Text('PHOTO', style: TextStyle(color: Color(0xFF9EAFBC), fontSize: 9, letterSpacing: 2, fontWeight: FontWeight.w900)),
                ),
                Positioned(
                  right: 8,
                  top: 8,
                  child: Container(width: 34, height: 34, decoration: BoxDecoration(color: Colors.white, borderRadius: BorderRadius.circular(11)), child: const Icon(Icons.favorite_rounded, color: _danger, size: 17)),
                ),
              ],
            ),
            const SizedBox(height: 14),
            Text(name, maxLines: 2, overflow: TextOverflow.ellipsis, style: const TextStyle(color: _navy, fontSize: 14, fontWeight: FontWeight.w900)),
            const Spacer(),
            Text(Format.money(price, 'XOF'), maxLines: 1, overflow: TextOverflow.ellipsis, style: const TextStyle(color: _green, fontSize: 16, fontWeight: FontWeight.w900)),
          ],
        ),
      ),
    );
  }
}

class _RestaurantFavorite extends StatelessWidget {
  const _RestaurantFavorite();

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.fromLTRB(16, 26, 16, 0),
      child: InkWell(
        borderRadius: BorderRadius.circular(18),
        onTap: () => context.go('/food'),
        child: Container(
          height: 116,
          padding: const EdgeInsets.all(16),
          decoration: BoxDecoration(color: Colors.white, borderRadius: BorderRadius.circular(18), boxShadow: [BoxShadow(color: Colors.black.withValues(alpha: 0.04), blurRadius: 18, offset: const Offset(0, 8))]),
          child: Row(
            children: [
              Container(width: 74, height: 74, decoration: BoxDecoration(color: const Color(0xFFE8DDCF), borderRadius: BorderRadius.circular(14))),
              const SizedBox(width: 16),
              const Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  mainAxisAlignment: MainAxisAlignment.center,
                  children: [
                    Text('Chez Mama', maxLines: 1, overflow: TextOverflow.ellipsis, style: TextStyle(color: _navy, fontSize: 17, fontWeight: FontWeight.w900)),
                    SizedBox(height: 3),
                    Text('Cuisine béninoise · Grillades · ★ 4,8', maxLines: 1, overflow: TextOverflow.ellipsis, style: TextStyle(color: Color(0xFF66778C), fontSize: 13, fontWeight: FontWeight.w600)),
                    SizedBox(height: 5),
                    Text('20–30 min · Livraison 800 F CFA', maxLines: 1, overflow: TextOverflow.ellipsis, style: TextStyle(color: _green, fontSize: 13, fontWeight: FontWeight.w900)),
                  ],
                ),
              ),
              const SizedBox(width: 8),
              const Icon(Icons.favorite_rounded, color: _danger, size: 20),
            ],
          ),
        ),
      ),
    );
  }
}

class _ShopFavorites extends StatelessWidget {
  const _ShopFavorites();

  @override
  Widget build(BuildContext context) {
    return const Padding(
      padding: EdgeInsets.fromLTRB(16, 26, 16, 0),
      child: Column(
        children: [
          _FavoriteShop(initials: 'HT', name: 'HBA Tech Store', subtitle: 'Électronique · ★ 4,9', color: Color(0xFFE5EAF0)),
          SizedBox(height: 12),
          _FavoriteShop(initials: 'FH', name: 'Fashion House', subtitle: 'Mode · ★ 4,7', color: Color(0xFFECE6DF)),
          SizedBox(height: 12),
          _FavoriteShop(initials: 'MP', name: 'Maison Plus', subtitle: 'Maison · ★ 4,6', color: Color(0xFFE2EBE6)),
          SizedBox(height: 12),
          _FavoriteShop(initials: 'TC', name: 'Time & Co', subtitle: 'Montres · ★ 4,8', color: Color(0xFFE6E7EA)),
        ],
      ),
    );
  }
}

class _FavoriteShop extends StatelessWidget {
  const _FavoriteShop({required this.initials, required this.name, required this.subtitle, required this.color});
  final String initials;
  final String name;
  final String subtitle;
  final Color color;

  @override
  Widget build(BuildContext context) {
    return InkWell(
      borderRadius: BorderRadius.circular(18),
      onTap: () => context.push('/shop/${name.toLowerCase().replaceAll(' ', '-')}'),
      child: Container(
        height: 96,
        padding: const EdgeInsets.all(16),
        decoration: BoxDecoration(color: Colors.white, borderRadius: BorderRadius.circular(18), boxShadow: [BoxShadow(color: Colors.black.withValues(alpha: 0.04), blurRadius: 18, offset: const Offset(0, 8))]),
        child: Row(
          children: [
            Container(width: 60, height: 60, decoration: BoxDecoration(color: color, borderRadius: BorderRadius.circular(14)), alignment: Alignment.center, child: Text(initials, style: const TextStyle(color: _navy, fontSize: 16, fontWeight: FontWeight.w900))),
            const SizedBox(width: 16),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                mainAxisAlignment: MainAxisAlignment.center,
                children: [
                  Text(name, maxLines: 1, overflow: TextOverflow.ellipsis, style: const TextStyle(color: _navy, fontSize: 16, fontWeight: FontWeight.w900)),
                  const SizedBox(height: 3),
                  Text(subtitle, maxLines: 1, overflow: TextOverflow.ellipsis, style: const TextStyle(color: Color(0xFF66778C), fontSize: 13, fontWeight: FontWeight.w600)),
                ],
              ),
            ),
            const SizedBox(width: 10),
            Container(
              height: 42,
              padding: const EdgeInsets.symmetric(horizontal: 18),
              alignment: Alignment.center,
              decoration: BoxDecoration(color: const Color(0xFFE1F4EC), borderRadius: BorderRadius.circular(14)),
              child: const Text('Visiter', style: TextStyle(color: _green, fontSize: 13, fontWeight: FontWeight.w900)),
            ),
          ],
        ),
      ),
    );
  }
}
