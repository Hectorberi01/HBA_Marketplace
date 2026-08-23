import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';

import '../../../core/theme/app_theme.dart';
import '../../../shared/utils/formatters.dart';
import '../../../shared/widgets/ui_kit.dart';

const _ink = Color(0xFF10233A);
const _muted = Color(0xFF728399);
const _pageBg = Color(0xFFF4F6F5);
const _photo = Color(0xFFE8DDCF);

class RestaurantDetailScreen extends StatelessWidget {
  const RestaurantDetailScreen({super.key, required this.restaurantId});

  final String restaurantId;

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: _pageBg,
      body: SafeArea(
        top: false,
        bottom: false,
        child: Stack(
          children: [
            CustomScrollView(
              slivers: [
                const SliverToBoxAdapter(child: _RestaurantHeader()),
                const SliverToBoxAdapter(child: _CategoryChips()),
                const SliverToBoxAdapter(child: _SectionTitle('Plats populaires')),
                SliverPadding(
                  padding: EdgeInsets.fromLTRB(16, 0, 16, bottomSafePadding(context, extra: 112)),
                  sliver: SliverList.separated(
                    itemCount: _dishes.length,
                    separatorBuilder: (_, __) => const SizedBox(height: 14),
                    itemBuilder: (_, index) => _DishCard(dish: _dishes[index]),
                  ),
                ),
              ],
            ),
            Positioned(
              left: 0,
              right: 0,
              bottom: 0,
              child: _CartBar(bottomPadding: bottomSafePadding(context, extra: 16)),
            ),
          ],
        ),
      ),
    );
  }
}

class _RestaurantHeader extends StatelessWidget {
  const _RestaurantHeader();

  @override
  Widget build(BuildContext context) {
    return Stack(
      clipBehavior: Clip.none,
      children: [
        Container(
          height: 230,
          padding: EdgeInsets.fromLTRB(20, MediaQuery.paddingOf(context).top + 18, 20, 0),
          decoration: const BoxDecoration(
            color: Color(0xFFE8DDCF),
            borderRadius: BorderRadius.vertical(bottom: Radius.circular(28)),
          ),
          child: const _StatusLine(),
        ),
        Positioned(
          left: 20,
          right: 20,
          top: MediaQuery.paddingOf(context).top + 116,
          child: const _RestaurantSummaryCard(),
        ),
      ],
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
          width: 24,
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

class _RestaurantSummaryCard extends StatelessWidget {
  const _RestaurantSummaryCard();

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.all(20),
      decoration: BoxDecoration(
        color: Colors.white,
        borderRadius: BorderRadius.circular(22),
        boxShadow: [BoxShadow(color: Colors.black.withValues(alpha: 0.06), blurRadius: 24, offset: const Offset(0, 12))],
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              _SquareButton(icon: Icons.chevron_left, onTap: () => context.pop()),
              const Spacer(),
              Container(
                padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 10),
                decoration: BoxDecoration(color: AppTheme.softGreen, borderRadius: BorderRadius.circular(14)),
                child: const Text('Ouvert', style: TextStyle(color: AppTheme.brandGreen, fontSize: 14, fontWeight: FontWeight.w900)),
              ),
            ],
          ),
          const SizedBox(height: 16),
          const Text('Chez Mama', style: TextStyle(color: _ink, fontSize: 26, fontWeight: FontWeight.w900)),
          const SizedBox(height: 4),
          const Text('Cuisine béninoise · Grillades', style: TextStyle(color: _muted, fontSize: 17, fontWeight: FontWeight.w600)),
          const SizedBox(height: 18),
          const Row(
            children: [
              Expanded(child: _MetricTile(title: '★ 4,8', subtitle: '340 avis')),
              SizedBox(width: 8),
              Expanded(child: _MetricTile(title: '20–30 min', subtitle: 'Livraison')),
              SizedBox(width: 8),
              Expanded(child: _MetricTile(title: '800 F CFA', subtitle: 'Frais')),
            ],
          ),
          const SizedBox(height: 16),
          Container(
            height: 66,
            padding: const EdgeInsets.symmetric(horizontal: 16),
            decoration: BoxDecoration(color: const Color(0xFFFFE9DF), borderRadius: BorderRadius.circular(16)),
            child: Row(
              children: [
                Container(
                  width: 46,
                  height: 46,
                  alignment: Alignment.center,
                  decoration: BoxDecoration(color: const Color(0xFFE9513E), borderRadius: BorderRadius.circular(12)),
                  child: const Text('%', style: TextStyle(color: Colors.white, fontSize: 19, fontWeight: FontWeight.w900)),
                ),
                const SizedBox(width: 14),
                const Expanded(
                  child: Text(
                    '-20 % sur les plats du jour jusqu’à 18 h',
                    maxLines: 2,
                    overflow: TextOverflow.ellipsis,
                    style: TextStyle(color: Color(0xFFB53724), fontSize: 15, fontWeight: FontWeight.w900),
                  ),
                ),
              ],
            ),
          ),
        ],
      ),
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
      borderRadius: BorderRadius.circular(14),
      child: Container(
        width: 46,
        height: 46,
        alignment: Alignment.center,
        decoration: BoxDecoration(color: _pageBg, borderRadius: BorderRadius.circular(14)),
        child: Icon(icon, color: _ink, size: 26),
      ),
    );
  }
}

class _MetricTile extends StatelessWidget {
  const _MetricTile({required this.title, required this.subtitle});

  final String title;
  final String subtitle;

  @override
  Widget build(BuildContext context) {
    return Container(
      height: 66,
      alignment: Alignment.center,
      decoration: BoxDecoration(color: const Color(0xFFF3F4F5), borderRadius: BorderRadius.circular(14)),
      child: Column(
        mainAxisAlignment: MainAxisAlignment.center,
        children: [
          FittedBox(child: Text(title, style: const TextStyle(color: _ink, fontSize: 17, fontWeight: FontWeight.w900))),
          const SizedBox(height: 2),
          Text(subtitle, style: const TextStyle(color: _muted, fontSize: 12, fontWeight: FontWeight.w600)),
        ],
      ),
    );
  }
}

class _CategoryChips extends StatelessWidget {
  const _CategoryChips();

  @override
  Widget build(BuildContext context) {
    return SizedBox(
      height: 182,
      child: Align(
        alignment: Alignment.bottomLeft,
        child: SizedBox(
          height: 58,
          child: ListView.separated(
            padding: const EdgeInsets.symmetric(horizontal: 16),
            scrollDirection: Axis.horizontal,
            itemCount: _categories.length,
            separatorBuilder: (_, __) => const SizedBox(width: 10),
            itemBuilder: (_, index) => _FoodChip(label: _categories[index], selected: index == 0),
          ),
        ),
      ),
    );
  }
}

class _FoodChip extends StatelessWidget {
  const _FoodChip({required this.label, required this.selected});

  final String label;
  final bool selected;

  @override
  Widget build(BuildContext context) {
    return Container(
      height: 44,
      alignment: Alignment.center,
      padding: const EdgeInsets.symmetric(horizontal: 20),
      decoration: BoxDecoration(
        color: selected ? AppTheme.brandGreen : Colors.white,
        borderRadius: BorderRadius.circular(14),
      ),
      child: Text(
        label,
        style: TextStyle(
          color: selected ? Colors.white : _ink,
          fontSize: 15,
          fontWeight: FontWeight.w900,
        ),
      ),
    );
  }
}

class _SectionTitle extends StatelessWidget {
  const _SectionTitle(this.title);

  final String title;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.fromLTRB(16, 10, 16, 16),
      child: Text(title, style: const TextStyle(color: _ink, fontSize: 21, fontWeight: FontWeight.w900)),
    );
  }
}

class _DishCard extends StatelessWidget {
  const _DishCard({required this.dish});

  final _Dish dish;

  @override
  Widget build(BuildContext context) {
    return Container(
      height: 132,
      padding: const EdgeInsets.fromLTRB(16, 14, 14, 14),
      decoration: BoxDecoration(
        color: Colors.white,
        borderRadius: BorderRadius.circular(20),
        boxShadow: [BoxShadow(color: Colors.black.withValues(alpha: 0.035), blurRadius: 18, offset: const Offset(0, 9))],
      ),
      child: Row(
        children: [
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Row(
                  children: [
                    Flexible(
                      child: Text(
                        dish.name,
                        maxLines: 1,
                        overflow: TextOverflow.ellipsis,
                        style: const TextStyle(color: _ink, fontSize: 18, fontWeight: FontWeight.w900),
                      ),
                    ),
                    if (dish.popular) ...[
                      const SizedBox(width: 8),
                      Container(
                        padding: const EdgeInsets.symmetric(horizontal: 9, vertical: 5),
                        decoration: BoxDecoration(color: const Color(0xFFFFF0E3), borderRadius: BorderRadius.circular(999)),
                        child: const Text('POPULAIRE', style: TextStyle(color: Color(0xFFE26A10), fontSize: 10, fontWeight: FontWeight.w900)),
                      ),
                    ],
                  ],
                ),
                const SizedBox(height: 10),
                Text(
                  dish.description,
                  maxLines: 2,
                  overflow: TextOverflow.ellipsis,
                  style: const TextStyle(color: _muted, fontSize: 15, height: 1.35, fontWeight: FontWeight.w600),
                ),
                const Spacer(),
                Text(Format.money(dish.price, 'XOF'), style: const TextStyle(color: AppTheme.brandGreen, fontSize: 17, fontWeight: FontWeight.w900)),
              ],
            ),
          ),
          const SizedBox(width: 14),
          Stack(
            clipBehavior: Clip.none,
            children: [
              Container(
                width: 112,
                height: 104,
                alignment: Alignment.center,
                decoration: BoxDecoration(color: _photo, borderRadius: BorderRadius.circular(18)),
                child: const Text('PHOTO', style: TextStyle(color: Color(0xFFA89687), fontSize: 8, letterSpacing: 1.6, fontWeight: FontWeight.w900)),
              ),
              Positioned(
                right: -6,
                bottom: -6,
                child: Container(
                  width: 40,
                  height: 40,
                  alignment: Alignment.center,
                  decoration: BoxDecoration(
                    color: AppTheme.brandGreen,
                    borderRadius: BorderRadius.circular(12),
                    border: Border.all(color: Colors.white, width: 3),
                  ),
                  child: const Icon(Icons.add, color: Colors.white, size: 22),
                ),
              ),
            ],
          ),
        ],
      ),
    );
  }
}

class _CartBar extends StatelessWidget {
  const _CartBar({required this.bottomPadding});

  final double bottomPadding;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: EdgeInsets.fromLTRB(16, 14, 16, bottomPadding),
      decoration: BoxDecoration(
        gradient: LinearGradient(
          begin: Alignment.topCenter,
          end: Alignment.bottomCenter,
          colors: [Colors.white.withValues(alpha: 0), Colors.white, Colors.white],
        ),
      ),
      child: Container(
        height: 64,
        padding: const EdgeInsets.symmetric(horizontal: 16),
        decoration: BoxDecoration(
          color: AppTheme.brandGreen,
          borderRadius: BorderRadius.circular(18),
          boxShadow: [BoxShadow(color: AppTheme.brandGreen.withValues(alpha: 0.24), blurRadius: 24, offset: const Offset(0, 14))],
        ),
        child: Row(
          children: [
            Container(
              width: 34,
              height: 34,
              alignment: Alignment.center,
              decoration: BoxDecoration(color: Colors.white.withValues(alpha: 0.2), borderRadius: BorderRadius.circular(10)),
              child: const Text('1', style: TextStyle(color: Colors.white, fontSize: 16, fontWeight: FontWeight.w900)),
            ),
            const SizedBox(width: 14),
            const Expanded(child: Text('Voir le panier', style: TextStyle(color: Colors.white, fontSize: 18, fontWeight: FontWeight.w900))),
            const Text('6 000 F CFA', style: TextStyle(color: Colors.white, fontSize: 18, fontWeight: FontWeight.w900)),
          ],
        ),
      ),
    );
  }
}

class _Dish {
  const _Dish(this.name, this.description, this.price, {this.popular = false});

  final String name;
  final String description;
  final int price;
  final bool popular;
}

const _categories = ['Plats populaires', 'Entrées', 'Plats principaux', 'Boissons', 'Desserts'];

const _dishes = [
  _Dish('Poulet braisé', 'Poulet grillé, épices maison, accompagnement au choix', 5500, popular: true),
  _Dish('Amiwo poisson', 'Pâte rouge, poisson frit, sauce tomate épicée', 4500),
  _Dish('Attiéké poisson braisé', 'Attiéké, poisson braisé entier, piment vert', 6000, popular: true),
  _Dish('Jus de bissap', 'Bissap frais, sucre léger, menthe', 3500),
];
