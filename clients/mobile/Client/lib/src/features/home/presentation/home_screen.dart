import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';

import '../../../core/theme/app_theme.dart';
import '../../../shared/utils/formatters.dart';
import '../../../shared/widgets/ui_kit.dart';

const _foodOrange = Color(0xFFFF8A22);
const _navy = Color(0xFF0E2239);

class HomeScreen extends StatelessWidget {
  const HomeScreen({super.key});

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: const Color(0xFFF4F6F5),
      body: SafeArea(
        bottom: false,
        child: CustomScrollView(
          slivers: [
            const SliverToBoxAdapter(child: _TopPanel()),
            const SliverToBoxAdapter(child: _ServiceHero.express()),
            const SliverToBoxAdapter(child: _ServiceHero.food()),
            const SliverToBoxAdapter(child: SectionHeader(title: 'Commandes en cours')),
            const SliverToBoxAdapter(child: _OrdersPanel()),
            SliverToBoxAdapter(
              child: SectionHeader(
                title: 'À découvrir sur HBAExpress',
                seeAllLabel: 'Voir tout',
                onSeeAll: () => context.go('/search'),
              ),
            ),
            const SliverToBoxAdapter(child: _ProductRail()),
            const SliverToBoxAdapter(child: _ExpressButton()),
            SliverToBoxAdapter(
              child: SectionHeader(
                title: 'Une petite faim ?',
                seeAllLabel: 'Voir tout',
                onSeeAll: () => context.go('/search'),
              ),
            ),
            const SliverToBoxAdapter(child: _FoodRail()),
            const SliverToBoxAdapter(child: _FoodButton()),
            const SliverToBoxAdapter(child: SectionHeader(title: 'Récemment consulté')),
            SliverToBoxAdapter(
              child: Padding(
                padding: EdgeInsets.fromLTRB(16, 0, 16, bottomSafePadding(context, extra: 92)),
                child: const _RecentPanel(),
              ),
            ),
          ],
        ),
      ),
    );
  }
}

class _TopPanel extends StatelessWidget {
  const _TopPanel();

  @override
  Widget build(BuildContext context) {
    return Container(
      margin: const EdgeInsets.fromLTRB(10, 0, 10, 18),
      padding: const EdgeInsets.fromLTRB(18, 18, 18, 20),
      decoration: const BoxDecoration(
        color: Color(0xFF0B865C),
        borderRadius: BorderRadius.vertical(bottom: Radius.circular(26)),
      ),
      child: Column(
        children: [
          Row(
            children: [
              Container(
                width: 32,
                height: 32,
                decoration: BoxDecoration(color: Colors.white, borderRadius: BorderRadius.circular(10)),
                alignment: Alignment.center,
                child: const Text('HB', style: TextStyle(color: AppTheme.brandGreen, fontSize: 12, fontWeight: FontWeight.w900)),
              ),
              const SizedBox(width: 10),
              const Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text('Livrer à', style: TextStyle(color: Color(0xFFD4EFE4), fontSize: 10, fontWeight: FontWeight.w600)),
                    SizedBox(height: 1),
                    Text('Ma maison, Cotonou⌄',
                        maxLines: 1,
                        overflow: TextOverflow.ellipsis,
                        style: TextStyle(color: Colors.white, fontSize: 13, fontWeight: FontWeight.w900)),
                  ],
                ),
              ),
              _TopIcon(icon: Icons.notifications_none_rounded, badge: true, onTap: () => context.push('/notifications')),
              const SizedBox(width: 8),
              _TopIcon(icon: Icons.shopping_cart_outlined, badge: true, label: '3', onTap: () => context.go('/cart')),
              const SizedBox(width: 8),
              GestureDetector(
                onTap: () => context.go('/account'),
                child: Container(
                  width: 38,
                  height: 38,
                  decoration: BoxDecoration(
                    color: const Color(0xFF12A676),
                    shape: BoxShape.circle,
                    border: Border.all(color: Colors.white.withValues(alpha: 0.6)),
                  ),
                  alignment: Alignment.center,
                  child: const Text('AK', style: TextStyle(color: Colors.white, fontWeight: FontWeight.w900)),
                ),
              ),
            ],
          ),
          const SizedBox(height: 24),
          GestureDetector(
            onTap: () => context.go('/search'),
            child: Container(
              height: 42,
              decoration: BoxDecoration(color: Colors.white, borderRadius: BorderRadius.circular(13)),
              padding: const EdgeInsets.symmetric(horizontal: 15),
              child: Row(
                children: [
                  const Icon(Icons.search_rounded, color: Color(0xFF63758C), size: 20),
                  const SizedBox(width: 10),
                  Text('Que recherchez-vous ?', style: TextStyle(color: Colors.blueGrey.shade300, fontWeight: FontWeight.w600)),
                ],
              ),
            ),
          ),
        ],
      ),
    );
  }
}

class _TopIcon extends StatelessWidget {
  const _TopIcon({required this.icon, required this.onTap, this.badge = false, this.label});
  final IconData icon;
  final VoidCallback onTap;
  final bool badge;
  final String? label;

  @override
  Widget build(BuildContext context) {
    return GestureDetector(
      onTap: onTap,
      child: Stack(
        clipBehavior: Clip.none,
        children: [
          Container(
            width: 38,
            height: 38,
            decoration: BoxDecoration(
              color: Colors.white.withValues(alpha: 0.22),
              borderRadius: BorderRadius.circular(11),
            ),
            child: Icon(icon, color: Colors.white, size: 20),
          ),
          if (badge)
            Positioned(
              right: -3,
              top: -4,
              child: Container(
                width: 16,
                height: 16,
                alignment: Alignment.center,
                decoration: const BoxDecoration(color: _foodOrange, shape: BoxShape.circle),
                child: Text(label ?? '', style: const TextStyle(color: Colors.white, fontSize: 9, fontWeight: FontWeight.w900)),
              ),
            ),
        ],
      ),
    );
  }
}

class _ServiceHero extends StatelessWidget {
  const _ServiceHero.express()
      : title = 'HBAExpress',
        subtitle = "Tout ce qu'il vous faut, au même endroit.",
        button = 'Explorer',
        icon = Icons.shopping_bag_outlined,
        badge = 'PRODUITS',
        colors = const [Color(0xFF09855B), Color(0xFF078157)],
        accent = AppTheme.brandGreen,
        route = '/express';

  const _ServiceHero.food()
      : title = 'HBA Food',
        subtitle = 'Une petite faim ? On vous livre.',
        button = 'Commander',
        icon = Icons.restaurant_rounded,
        badge = 'REPAS',
        colors = const [_navy, Color(0xFF4B392E)],
        accent = _foodOrange,
        route = '/food';

  final String title;
  final String subtitle;
  final String button;
  final IconData icon;
  final String badge;
  final List<Color> colors;
  final Color accent;
  final String route;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.fromLTRB(16, 0, 16, 12),
      child: Container(
        height: 208,
        decoration: BoxDecoration(
          gradient: LinearGradient(colors: colors, begin: Alignment.topLeft, end: Alignment.bottomRight),
          borderRadius: BorderRadius.circular(18),
        ),
        clipBehavior: Clip.antiAlias,
        child: Stack(
          children: [
            Positioned(
              right: -42,
              top: -52,
              child: Container(
                width: 160,
                height: 160,
                decoration: BoxDecoration(shape: BoxShape.circle, color: Colors.white.withValues(alpha: 0.20)),
              ),
            ),
            Positioned(
              right: 16,
              bottom: 14,
              child: Container(
                width: 94,
                height: 72,
                decoration: BoxDecoration(color: Colors.white.withValues(alpha: 0.24), borderRadius: BorderRadius.circular(12)),
                alignment: Alignment.center,
                child: Text(
                  badge,
                  style: TextStyle(color: Colors.white.withValues(alpha: 0.72), fontSize: 8, letterSpacing: 2, fontWeight: FontWeight.w900),
                ),
              ),
            ),
            Padding(
              padding: const EdgeInsets.all(18),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Container(
                    width: 38,
                    height: 38,
                    decoration: BoxDecoration(color: Colors.white, borderRadius: BorderRadius.circular(12)),
                    child: Icon(icon, color: accent, size: 20),
                  ),
                  const Spacer(),
                  Text(title, style: const TextStyle(color: Colors.white, fontSize: 20, fontWeight: FontWeight.w900)),
                  const SizedBox(height: 4),
                  SizedBox(
                    width: 188,
                    child: Text(subtitle, style: TextStyle(color: Colors.white.withValues(alpha: 0.88), fontSize: 12, height: 1.22)),
                  ),
                  const SizedBox(height: 14),
                  FilledButton(
                    onPressed: () => context.go(route),
                    style: FilledButton.styleFrom(
                      backgroundColor: accent == _foodOrange ? _foodOrange : Colors.white,
                      foregroundColor: accent == _foodOrange ? Colors.white : AppTheme.brandGreen,
                      minimumSize: const Size(96, 34),
                      padding: const EdgeInsets.symmetric(horizontal: 16),
                      shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(11)),
                    ),
                    child: Row(
                      mainAxisSize: MainAxisSize.min,
                      children: [
                        Text(button, style: const TextStyle(fontSize: 12, fontWeight: FontWeight.w900)),
                        const SizedBox(width: 7),
                        const Icon(Icons.chevron_right_rounded, size: 17),
                      ],
                    ),
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

class _OrdersPanel extends StatelessWidget {
  const _OrdersPanel();

  @override
  Widget build(BuildContext context) {
    return const Padding(
      padding: EdgeInsets.symmetric(horizontal: 16),
      child: Column(
        children: [
          _OrderRow(kind: 'FOOD', title: 'Commande HBA Food · Chez Mama', subtitle: 'En route · 12 min', action: 'Suivre', color: _foodOrange),
          SizedBox(height: 10),
          _OrderRow(kind: 'EXPR', title: 'Commande HBAExpress · HBA Tech', subtitle: 'En préparation', action: 'Voir', color: AppTheme.brandGreen),
        ],
      ),
    );
  }
}

class _OrderRow extends StatelessWidget {
  const _OrderRow({required this.kind, required this.title, required this.subtitle, required this.action, required this.color});
  final String kind;
  final String title;
  final String subtitle;
  final String action;
  final Color color;

  @override
  Widget build(BuildContext context) {
    return GestureDetector(
      onTap: () => context.push('/orders'),
      child: Container(
        padding: const EdgeInsets.all(14),
        decoration: BoxDecoration(
          color: Colors.white,
          borderRadius: BorderRadius.circular(16),
          boxShadow: [BoxShadow(color: Colors.black.withValues(alpha: 0.04), blurRadius: 16, offset: const Offset(0, 8))],
        ),
        child: Row(
          children: [
            Container(
              width: 40,
              height: 40,
              decoration: BoxDecoration(color: color.withValues(alpha: 0.12), borderRadius: BorderRadius.circular(12)),
              alignment: Alignment.center,
              child: Text(kind, style: TextStyle(color: color, fontSize: 8, fontWeight: FontWeight.w900, letterSpacing: 0.8)),
            ),
            const SizedBox(width: 12),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(title, maxLines: 2, overflow: TextOverflow.ellipsis, style: const TextStyle(color: Color(0xFF13243A), fontSize: 13, fontWeight: FontWeight.w900, height: 1.15)),
                  const SizedBox(height: 4),
                  Row(
                    children: [
                      Container(width: 7, height: 7, decoration: BoxDecoration(color: color.withValues(alpha: 0.32), shape: BoxShape.circle)),
                      const SizedBox(width: 5),
                      Expanded(child: Text(subtitle, maxLines: 1, overflow: TextOverflow.ellipsis, style: const TextStyle(color: Color(0xFF7D8B9E), fontSize: 11, fontWeight: FontWeight.w700))),
                    ],
                  ),
                ],
              ),
            ),
            const SizedBox(width: 8),
            Container(
              padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 9),
              decoration: BoxDecoration(color: AppTheme.softGreen, borderRadius: BorderRadius.circular(12)),
              child: Text(action, style: const TextStyle(color: AppTheme.brandGreen, fontSize: 11, fontWeight: FontWeight.w900)),
            ),
          ],
        ),
      ),
    );
  }
}

class _ProductRail extends StatelessWidget {
  const _ProductRail();

  @override
  Widget build(BuildContext context) {
    return SizedBox(
      height: 220,
      child: ListView.separated(
        scrollDirection: Axis.horizontal,
        padding: const EdgeInsets.symmetric(horizontal: 16),
        itemCount: _products.length,
        separatorBuilder: (_, __) => const SizedBox(width: 10),
        itemBuilder: (_, index) => _ProductCard(product: _products[index]),
      ),
    );
  }
}

class _ProductCard extends StatelessWidget {
  const _ProductCard({required this.product});
  final _Product product;

  @override
  Widget build(BuildContext context) {
    return GestureDetector(
      onTap: () => context.push('/product/${product.id}'),
      child: Container(
        width: 132,
        padding: const EdgeInsets.all(8),
        decoration: BoxDecoration(
          color: Colors.white,
          borderRadius: BorderRadius.circular(16),
          boxShadow: [BoxShadow(color: Colors.black.withValues(alpha: 0.045), blurRadius: 14, offset: const Offset(0, 8))],
        ),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Container(
              height: 96,
              decoration: BoxDecoration(color: const Color(0xFFE7EAEE), borderRadius: BorderRadius.circular(10)),
              alignment: Alignment.center,
              child: const Text('PHOTO', style: TextStyle(color: Color(0xFF9AA8BA), fontSize: 8, letterSpacing: 2, fontWeight: FontWeight.w900)),
            ),
            const SizedBox(height: 10),
            Text(product.name, maxLines: 2, overflow: TextOverflow.ellipsis, style: const TextStyle(color: Color(0xFF13243A), fontSize: 12, fontWeight: FontWeight.w800, height: 1.16)),
            const Spacer(),
            Text(Format.money(product.price, 'XOF'), maxLines: 1, overflow: TextOverflow.ellipsis, style: const TextStyle(color: AppTheme.brandGreen, fontSize: 13, fontWeight: FontWeight.w900)),
            const SizedBox(height: 3),
            Row(
              children: [
                const Icon(Icons.star_rounded, size: 13, color: Color(0xFF97A7BA)),
                const SizedBox(width: 2),
                Expanded(child: Text('${product.rating} · ${product.store}', maxLines: 1, overflow: TextOverflow.ellipsis, style: const TextStyle(color: Color(0xFF718198), fontSize: 10, fontWeight: FontWeight.w600))),
              ],
            ),
          ],
        ),
      ),
    );
  }
}

class _ExpressButton extends StatelessWidget {
  const _ExpressButton();

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.fromLTRB(16, 8, 16, 18),
      child: OutlinedButton(
        onPressed: () => context.go('/express'),
        style: OutlinedButton.styleFrom(
          minimumSize: const Size(double.infinity, 38),
          foregroundColor: AppTheme.brandGreen,
          side: BorderSide(color: AppTheme.brandGreen.withValues(alpha: 0.20)),
          shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(14)),
        ),
        child: const Text('Voir HBAExpress', style: TextStyle(fontWeight: FontWeight.w900)),
      ),
    );
  }
}

class _FoodRail extends StatelessWidget {
  const _FoodRail();

  @override
  Widget build(BuildContext context) {
    return Container(
      color: const Color(0xFFFFF7EF),
      padding: const EdgeInsets.fromLTRB(16, 0, 0, 10),
      child: SizedBox(
        height: 204,
        child: ListView.separated(
          scrollDirection: Axis.horizontal,
          itemCount: _restaurants.length,
          separatorBuilder: (_, __) => const SizedBox(width: 10),
          itemBuilder: (_, index) => _FoodCard(restaurant: _restaurants[index]),
        ),
      ),
    );
  }
}

class _FoodCard extends StatelessWidget {
  const _FoodCard({required this.restaurant});
  final _Restaurant restaurant;

  @override
  Widget build(BuildContext context) {
    return GestureDetector(
      onTap: () => context.push('/shop/${restaurant.id}'),
      child: Container(
        width: 192,
        decoration: BoxDecoration(color: Colors.white, borderRadius: BorderRadius.circular(16)),
        clipBehavior: Clip.antiAlias,
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Stack(
              children: [
                Container(
                  height: 98,
                  decoration: const BoxDecoration(color: Color(0xFFE8DED0)),
                  alignment: Alignment.center,
                  child: const Text('PHOTO PLAT', style: TextStyle(color: Color(0xFFB4A48F), fontSize: 8, letterSpacing: 1.7, fontWeight: FontWeight.w900)),
                ),
                Positioned(
                  left: 10,
                  top: 10,
                  child: Container(
                    padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 5),
                    decoration: BoxDecoration(color: restaurant.badgeColor, borderRadius: BorderRadius.circular(999)),
                    child: Text(restaurant.badge, style: const TextStyle(color: Colors.white, fontSize: 10, fontWeight: FontWeight.w900)),
                  ),
                ),
              ],
            ),
            Padding(
              padding: const EdgeInsets.all(12),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Row(
                    children: [
                      Expanded(child: Text(restaurant.name, maxLines: 1, overflow: TextOverflow.ellipsis, style: const TextStyle(color: Color(0xFF13243A), fontSize: 13, fontWeight: FontWeight.w900))),
                      const Icon(Icons.star_rounded, size: 14, color: _navy),
                      Text(restaurant.rating, style: const TextStyle(color: _navy, fontSize: 11, fontWeight: FontWeight.w900)),
                    ],
                  ),
                  const SizedBox(height: 5),
                  Text(restaurant.subtitle, maxLines: 1, overflow: TextOverflow.ellipsis, style: const TextStyle(color: Color(0xFF7C6E62), fontSize: 11, fontWeight: FontWeight.w600)),
                  const SizedBox(height: 7),
                  Text(restaurant.footer, maxLines: 1, overflow: TextOverflow.ellipsis, style: const TextStyle(color: AppTheme.brandGreen, fontSize: 11, fontWeight: FontWeight.w800)),
                ],
              ),
            ),
          ],
        ),
      ),
    );
  }
}

class _FoodButton extends StatelessWidget {
  const _FoodButton();

  @override
  Widget build(BuildContext context) {
    return Container(
      color: const Color(0xFFFFF7EF),
      padding: const EdgeInsets.fromLTRB(16, 0, 16, 18),
      child: FilledButton(
        onPressed: () => context.go('/food'),
        style: FilledButton.styleFrom(
          backgroundColor: _foodOrange,
          foregroundColor: Colors.white,
          minimumSize: const Size(double.infinity, 38),
          shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(14)),
        ),
        child: const Text('Voir HBA Food', style: TextStyle(fontWeight: FontWeight.w900)),
      ),
    );
  }
}

class _RecentPanel extends StatelessWidget {
  const _RecentPanel();

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.all(14),
      decoration: BoxDecoration(color: Colors.white, borderRadius: BorderRadius.circular(18)),
      child: const Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          _RecentGroup(label: 'HBAEXPRESS', color: AppTheme.brandGreen, items: ['Casque Sony', 'Montre Classic']),
          Padding(
            padding: EdgeInsets.symmetric(vertical: 12),
            child: Divider(height: 1),
          ),
          _RecentGroup(label: 'HBA FOOD', color: _foodOrange, items: ['Chez Mama', 'Pizza Bella']),
        ],
      ),
    );
  }
}

class _RecentGroup extends StatelessWidget {
  const _RecentGroup({required this.label, required this.color, required this.items});
  final String label;
  final Color color;
  final List<String> items;

  @override
  Widget build(BuildContext context) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(label, style: TextStyle(color: color, fontSize: 9, fontWeight: FontWeight.w900, letterSpacing: 1)),
        const SizedBox(height: 10),
        Wrap(
          spacing: 10,
          runSpacing: 10,
          children: [
            for (final item in items)
              Container(
                padding: const EdgeInsets.fromLTRB(8, 8, 12, 8),
                decoration: BoxDecoration(color: color.withValues(alpha: 0.08), borderRadius: BorderRadius.circular(10)),
                child: Row(
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    Container(width: 28, height: 28, decoration: BoxDecoration(color: color.withValues(alpha: 0.10), borderRadius: BorderRadius.circular(8))),
                    const SizedBox(width: 8),
                    Text(item, style: const TextStyle(color: Color(0xFF13243A), fontSize: 12, fontWeight: FontWeight.w800)),
                  ],
                ),
              ),
          ],
        ),
      ],
    );
  }
}

class _Product {
  const _Product(this.id, this.name, this.price, this.rating, this.store);
  final String id;
  final String name;
  final double price;
  final String rating;
  final String store;
}

class _Restaurant {
  const _Restaurant(this.id, this.name, this.subtitle, this.footer, this.rating, this.badge, this.badgeColor);
  final String id;
  final String name;
  final String subtitle;
  final String footer;
  final String rating;
  final String badge;
  final Color badgeColor;
}

const _products = [
  _Product('sony-wh-1000xm5', 'Sony WH-1000XM5', 279000, '4,9', 'HBA Tech Store'),
  _Product('iphone-14-pro', 'iPhone 14 Pro 128 Go', 620000, '4,8', 'HBA Tech Store'),
  _Product('sneakers-air', 'Sneakers Air Motion', 42500, '4,6', 'Fashion Hub'),
];

const _restaurants = [
  _Restaurant('chez-mama', 'Chez Mama', 'Cuisine béninoise · Grillades', '20–30 min · Livraison 800 F CFA', '4,8', '-20%', Color(0xFFE9513D)),
  _Restaurant('grill-or', "Le Grill d'Or", 'Grillades · Poisson braisé', '25–35 min · Livraison offerte', '4,7', 'Livraison offerte', _foodOrange),
  _Restaurant('pizza-bella', 'Pizza Bella', 'Pizza · Pâtes fraîches', '30–40 min · Livraison 600 F CFA', '4,6', 'Populaire', Color(0xFF0B865C)),
];
