import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';

import '../../../core/theme/app_theme.dart';
import '../../../shared/utils/formatters.dart';
import '../../../shared/widgets/ui_kit.dart';

const _green = AppTheme.brandGreen;
const _navy = Color(0xFF0E2239);
const _orange = Color(0xFFFF8A22);

class FoodHomeScreen extends StatelessWidget {
  const FoodHomeScreen({super.key});

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: const Color(0xFFF5F6F7),
      body: SafeArea(
        bottom: false,
        child: CustomScrollView(
          slivers: [
            const SliverToBoxAdapter(child: _FoodHeader()),
            const SliverToBoxAdapter(child: _FoodCategories()),
            const SliverToBoxAdapter(child: _SectionTitle(title: 'Près de chez vous', subtitle: 'Basé sur votre adresse')),
            const SliverToBoxAdapter(child: _RestaurantList()),
            const SliverToBoxAdapter(child: _SectionTitle(title: 'Moins de 30 min')),
            const SliverToBoxAdapter(child: _FastRail()),
            const SliverToBoxAdapter(child: _DeliveryPromo()),
            const SliverToBoxAdapter(child: _SectionTitle(title: 'Commandez à nouveau')),
            const SliverToBoxAdapter(child: _ReorderRail()),
            SliverToBoxAdapter(child: SizedBox(height: bottomSafePadding(context, extra: 92))),
          ],
        ),
      ),
    );
  }
}

class _FoodHeader extends StatelessWidget {
  const _FoodHeader();

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.fromLTRB(18, 16, 18, 20),
      decoration: const BoxDecoration(color: _navy),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              _HeaderIcon(icon: Icons.chevron_left_rounded, onTap: () => context.go('/home')),
              const SizedBox(width: 10),
              const Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text('HBA Food', style: TextStyle(color: Colors.white, fontSize: 15, fontWeight: FontWeight.w900)),
                    SizedBox(height: 1),
                    Text('Livrer à Ma maison, Cotonou', style: TextStyle(color: Color(0xFFE9DED1), fontSize: 10, fontWeight: FontWeight.w700)),
                  ],
                ),
              ),
              _HeaderIcon(icon: Icons.notifications_none_rounded, onTap: () => context.push('/notifications')),
              const SizedBox(width: 8),
              _HeaderIcon(icon: Icons.shopping_cart_outlined, badge: '1', onTap: () => context.go('/cart')),
            ],
          ),
          const SizedBox(height: 34),
          Text('PHOTOGRAPHIE CULINAIRE', style: TextStyle(color: Colors.white.withValues(alpha: 0.42), fontSize: 9, letterSpacing: 2, fontWeight: FontWeight.w900)),
          const SizedBox(height: 8),
          const Text('Envie de quelque chose\nde bon ?', style: TextStyle(color: Colors.white, fontSize: 23, height: 1.1, fontWeight: FontWeight.w900)),
          const SizedBox(height: 8),
          const Text('Découvrez les restaurants près de chez\nvous.', style: TextStyle(color: Color(0xFFDCE5EE), fontSize: 12, height: 1.25)),
          const SizedBox(height: 24),
          GestureDetector(
            onTap: () => context.go('/search'),
            child: Container(
              height: 50,
              padding: const EdgeInsets.fromLTRB(14, 0, 8, 0),
              decoration: BoxDecoration(color: Colors.white, borderRadius: BorderRadius.circular(13)),
              child: Row(
                children: [
                  const Icon(Icons.search_rounded, color: Color(0xFF63758C), size: 19),
                  const SizedBox(width: 9),
                  Expanded(child: Text('Rechercher un restaurant ou un plat', style: TextStyle(color: Colors.blueGrey.shade300, fontSize: 12))),
                  Container(
                    height: 30,
                    padding: const EdgeInsets.symmetric(horizontal: 14),
                    decoration: BoxDecoration(color: _orange, borderRadius: BorderRadius.circular(10)),
                    alignment: Alignment.center,
                    child: const Text('Commander', style: TextStyle(color: Colors.white, fontSize: 11, fontWeight: FontWeight.w900)),
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

class _HeaderIcon extends StatelessWidget {
  const _HeaderIcon({required this.icon, required this.onTap, this.badge});
  final IconData icon;
  final VoidCallback onTap;
  final String? badge;

  @override
  Widget build(BuildContext context) {
    return GestureDetector(
      onTap: onTap,
      child: Stack(
        clipBehavior: Clip.none,
        children: [
          Container(
            width: 32,
            height: 32,
            decoration: BoxDecoration(color: Colors.white.withValues(alpha: 0.16), borderRadius: BorderRadius.circular(10)),
            child: Icon(icon, color: Colors.white, size: 19),
          ),
          if (badge != null)
            Positioned(
              right: -4,
              top: -5,
              child: Container(
                width: 16,
                height: 16,
                alignment: Alignment.center,
                decoration: const BoxDecoration(color: _orange, shape: BoxShape.circle),
                child: Text(badge!, style: const TextStyle(color: Colors.white, fontSize: 9, fontWeight: FontWeight.w900)),
              ),
            ),
        ],
      ),
    );
  }
}

class _FoodCategories extends StatelessWidget {
  const _FoodCategories();

  @override
  Widget build(BuildContext context) {
    return SizedBox(
      height: 104,
      child: ListView.separated(
        padding: const EdgeInsets.fromLTRB(20, 16, 20, 8),
        scrollDirection: Axis.horizontal,
        itemCount: _categories.length,
        separatorBuilder: (_, __) => const SizedBox(width: 18),
        itemBuilder: (_, index) {
          final item = _categories[index];
          return SizedBox(
            width: 62,
            child: Column(
              children: [
                Container(
                  width: 56,
                  height: 56,
                  decoration: BoxDecoration(color: item.color, borderRadius: BorderRadius.circular(14)),
                  alignment: Alignment.center,
                  child: const Text('PHOTO', style: TextStyle(color: Color(0xFFA29082), fontSize: 7, letterSpacing: 1.2, fontWeight: FontWeight.w900)),
                ),
                const SizedBox(height: 5),
                Expanded(
                  child: Text(
                    item.name,
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                    textAlign: TextAlign.center,
                    style: const TextStyle(fontSize: 10, fontWeight: FontWeight.w700),
                  ),
                ),
              ],
            ),
          );
        },
      ),
    );
  }
}

class _RestaurantList extends StatelessWidget {
  const _RestaurantList();

  @override
  Widget build(BuildContext context) {
    return Column(
      children: _restaurants.map((item) => Padding(padding: const EdgeInsets.fromLTRB(16, 0, 16, 12), child: _RestaurantCard(restaurant: item))).toList(),
    );
  }
}

class _RestaurantCard extends StatelessWidget {
  const _RestaurantCard({required this.restaurant});
  final _Restaurant restaurant;

  @override
  Widget build(BuildContext context) {
    return InkWell(
      onTap: () => context.push('/restaurant/chez-mama'),
      borderRadius: BorderRadius.circular(16),
      child: Container(
      decoration: BoxDecoration(
        color: Colors.white,
        borderRadius: BorderRadius.circular(16),
        boxShadow: [BoxShadow(color: Colors.black.withValues(alpha: 0.05), blurRadius: 18, offset: const Offset(0, 8))],
      ),
      clipBehavior: Clip.antiAlias,
      child: Column(
        children: [
          Stack(
            children: [
              Container(
                height: 130,
                color: restaurant.photoColor,
                alignment: Alignment.center,
                child: const Text('PHOTO RESTAURANT', style: TextStyle(color: Color(0xFFA89687), fontSize: 8, letterSpacing: 2, fontWeight: FontWeight.w900)),
              ),
              Positioned(left: 12, top: 12, child: _Chip(label: restaurant.badge, color: _orange, textColor: Colors.white)),
              Positioned(
                right: 12,
                top: 12,
                child: Container(width: 28, height: 28, decoration: BoxDecoration(color: Colors.white, borderRadius: BorderRadius.circular(10)), child: const Icon(Icons.favorite_rounded, color: Color(0xFFF35B42), size: 15)),
              ),
              const Positioned(left: 12, bottom: 12, child: _Chip(label: 'Ouvert', color: Colors.white, textColor: _green)),
            ],
          ),
          Padding(
            padding: const EdgeInsets.fromLTRB(14, 12, 14, 14),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Row(
                  children: [
                    Expanded(child: Text(restaurant.name, style: const TextStyle(color: _navy, fontSize: 15, fontWeight: FontWeight.w900))),
                    const Icon(Icons.star_rounded, color: _orange, size: 15),
                    Text(restaurant.rating, style: const TextStyle(color: _navy, fontSize: 12, fontWeight: FontWeight.w900)),
                  ],
                ),
                const SizedBox(height: 3),
                Text(restaurant.subtitle, style: const TextStyle(color: Color(0xFF65778C), fontSize: 11)),
                const SizedBox(height: 10),
                Row(
                  children: [
                    _InfoPill(icon: Icons.timer_outlined, text: restaurant.time),
                    const SizedBox(width: 8),
                    _InfoPill(text: restaurant.delivery),
                  ],
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

class _FastRail extends StatelessWidget {
  const _FastRail();

  @override
  Widget build(BuildContext context) {
    return SizedBox(
      height: 150,
      child: ListView.separated(
        padding: const EdgeInsets.symmetric(horizontal: 16),
        scrollDirection: Axis.horizontal,
        itemCount: _restaurants.length,
        separatorBuilder: (_, __) => const SizedBox(width: 10),
        itemBuilder: (_, index) {
          final item = _restaurants[index];
          return InkWell(
            onTap: () => context.push('/restaurant/chez-mama'),
            borderRadius: BorderRadius.circular(13),
            child: SizedBox(
              width: 142,
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Container(height: 94, decoration: BoxDecoration(color: item.photoColor, borderRadius: BorderRadius.circular(13)), alignment: Alignment.center, child: const Text('PHOTO', style: TextStyle(color: Color(0xFFA89687), fontSize: 8, letterSpacing: 2, fontWeight: FontWeight.w900))),
                  const SizedBox(height: 8),
                  Text(item.name, maxLines: 1, overflow: TextOverflow.ellipsis, style: const TextStyle(color: _navy, fontSize: 12, fontWeight: FontWeight.w900)),
                  Text('${item.time} • ★ ${item.rating}', style: const TextStyle(color: Color(0xFF65778C), fontSize: 10)),
                ],
              ),
            ),
          );
        },
      ),
    );
  }
}

class _DeliveryPromo extends StatelessWidget {
  const _DeliveryPromo();

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.fromLTRB(16, 10, 16, 18),
      child: Container(
        height: 70,
        padding: const EdgeInsets.all(14),
        decoration: BoxDecoration(color: const Color(0xFFE2F6EC), borderRadius: BorderRadius.circular(16)),
        child: Row(
          children: [
            Container(
              width: 40,
              height: 40,
              decoration: BoxDecoration(color: _green, borderRadius: BorderRadius.circular(12)),
              child: const Icon(Icons.local_shipping_outlined, color: Colors.white, size: 21),
            ),
            const SizedBox(width: 12),
            const Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                mainAxisAlignment: MainAxisAlignment.center,
                children: [
                  Text('Livraison offerte cette semaine', style: TextStyle(color: _green, fontSize: 12, fontWeight: FontWeight.w900)),
                  SizedBox(height: 2),
                  Text('Sur 8 restaurants partenaires HBA Delivery', style: TextStyle(color: Color(0xFF4F806E), fontSize: 10)),
                ],
              ),
            ),
            const Icon(Icons.chevron_right_rounded, color: _green),
          ],
        ),
      ),
    );
  }
}

class _ReorderRail extends StatelessWidget {
  const _ReorderRail();

  @override
  Widget build(BuildContext context) {
    return SizedBox(
      height: 86,
      child: ListView.separated(
        padding: const EdgeInsets.symmetric(horizontal: 16),
        scrollDirection: Axis.horizontal,
        itemCount: _reorders.length,
        separatorBuilder: (_, __) => const SizedBox(width: 10),
        itemBuilder: (_, index) {
          final item = _reorders[index];
          return Container(
            width: 206,
            padding: const EdgeInsets.all(12),
            decoration: BoxDecoration(color: Colors.white, borderRadius: BorderRadius.circular(14), boxShadow: [BoxShadow(color: Colors.black.withValues(alpha: 0.05), blurRadius: 18, offset: const Offset(0, 8))]),
            child: Row(
              children: [
                Container(width: 50, height: 50, decoration: BoxDecoration(color: item.photoColor, borderRadius: BorderRadius.circular(10))),
                const SizedBox(width: 10),
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    mainAxisAlignment: MainAxisAlignment.center,
                    children: [
                      Text(item.name, maxLines: 1, overflow: TextOverflow.ellipsis, style: const TextStyle(color: _navy, fontSize: 12, fontWeight: FontWeight.w900)),
                      Text(item.shop, style: const TextStyle(color: Color(0xFF65778C), fontSize: 10)),
                      Text(Format.money(item.price, 'XOF'), style: const TextStyle(color: _green, fontSize: 12, fontWeight: FontWeight.w900)),
                    ],
                  ),
                ),
                Container(width: 30, height: 30, decoration: BoxDecoration(color: _orange, borderRadius: BorderRadius.circular(10)), child: const Icon(Icons.add_rounded, color: Colors.white, size: 18)),
              ],
            ),
          );
        },
      ),
    );
  }
}

class _SectionTitle extends StatelessWidget {
  const _SectionTitle({required this.title, this.subtitle});
  final String title;
  final String? subtitle;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.fromLTRB(16, 20, 16, 10),
      child: Row(
        children: [
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(title, style: const TextStyle(color: _navy, fontSize: 16, fontWeight: FontWeight.w900)),
                if (subtitle != null) Text(subtitle!, style: const TextStyle(color: Color(0xFF718196), fontSize: 11)),
              ],
            ),
          ),
          TextButton(onPressed: () => context.go('/search'), child: const Text('Voir tout', style: TextStyle(color: _orange, fontSize: 11, fontWeight: FontWeight.w900))),
        ],
      ),
    );
  }
}

class _Chip extends StatelessWidget {
  const _Chip({required this.label, required this.color, required this.textColor});
  final String label;
  final Color color;
  final Color textColor;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 9, vertical: 5),
      decoration: BoxDecoration(color: color, borderRadius: BorderRadius.circular(999)),
      child: Text(label, style: TextStyle(color: textColor, fontSize: 9, fontWeight: FontWeight.w900)),
    );
  }
}

class _InfoPill extends StatelessWidget {
  const _InfoPill({required this.text, this.icon});
  final String text;
  final IconData? icon;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 9, vertical: 5),
      decoration: BoxDecoration(color: const Color(0xFFF1F4F6), borderRadius: BorderRadius.circular(999)),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          if (icon != null) ...[
            Icon(icon, color: _green, size: 12),
            const SizedBox(width: 4),
          ],
          Text(text, style: const TextStyle(color: _navy, fontSize: 10, fontWeight: FontWeight.w700)),
        ],
      ),
    );
  }
}

class _Category {
  const _Category(this.name, this.color);
  final String name;
  final Color color;
}

class _Restaurant {
  const _Restaurant(this.name, this.subtitle, this.rating, this.time, this.delivery, this.badge, this.photoColor);
  final String name;
  final String subtitle;
  final String rating;
  final String time;
  final String delivery;
  final String badge;
  final Color photoColor;
}

class _Reorder {
  const _Reorder(this.name, this.shop, this.price, this.photoColor);
  final String name;
  final String shop;
  final int price;
  final Color photoColor;
}

const _categories = [
  _Category('Cuisine béninoise', Color(0xFFEDE3D5)),
  _Category('Fast-food', Color(0xFFEAE4D8)),
  _Category('Pizza', Color(0xFFECE2D7)),
  _Category('Poulet', Color(0xFFEFE5D7)),
];

const _restaurants = [
  _Restaurant('Chez Mama', 'Cuisine béninoise · Grillades', '4,8', '20–30 min', 'Livraison 800 F CFA', '-20 %', Color(0xFFE8DDCF)),
  _Restaurant("Le Grill d'Or", 'Grillades · Poisson braisé', '4,6', '25–35 min', 'Livraison offerte', 'Livraison offerte', Color(0xFFE9E4D8)),
  _Restaurant('Chicken House', 'Poulet · Fast-food', '4,5', '15–25 min', 'Livraison 700 F CFA', 'Livraison offerte', Color(0xFFECE4D9)),
];

const _reorders = [
  _Reorder('Poulet braisé', 'Chez Mama', 5500, Color(0xFFE8DDCF)),
  _Reorder('Menu poulet épicé', 'Chicken House', 4900, Color(0xFFECE4D9)),
];
