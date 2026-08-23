import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';

import '../../../core/theme/app_theme.dart';
import '../../../shared/utils/formatters.dart';
import '../../../shared/widgets/ui_kit.dart';

const _green = AppTheme.brandGreen;
const _navy = Color(0xFF0E2239);
const _orange = Color(0xFFFF7A2A);

class ExpressHomeScreen extends StatelessWidget {
  const ExpressHomeScreen({super.key});

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: const Color(0xFFF4F6F5),
      body: SafeArea(
        bottom: false,
        child: CustomScrollView(
          slivers: [
            const SliverToBoxAdapter(child: _ExpressHeader()),
            const SliverToBoxAdapter(child: _DailyOffer()),
            const SliverToBoxAdapter(child: _CategoryRail()),
            const SliverToBoxAdapter(child: _FlashDeals()),
            const SliverToBoxAdapter(child: _SectionTitle(title: 'Pour vous sur HBAExpress', subtitle: "D'après vos recherches récentes")),
            const _ProductGrid(),
            const SliverToBoxAdapter(child: _RecommendedStores()),
            const SliverToBoxAdapter(child: _NewArrivals()),
            SliverToBoxAdapter(child: SizedBox(height: bottomSafePadding(context, extra: 92))),
          ],
        ),
      ),
    );
  }
}

class _ExpressHeader extends StatelessWidget {
  const _ExpressHeader();

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.fromLTRB(18, 16, 18, 18),
      decoration: const BoxDecoration(color: Color(0xFF0B865C)),
      child: Column(
        children: [
          Row(
            children: [
              _HeaderIcon(icon: Icons.chevron_left_rounded, onTap: () => context.go('/home')),
              const SizedBox(width: 10),
              const Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text('HBAExpress', style: TextStyle(color: Colors.white, fontSize: 15, fontWeight: FontWeight.w900)),
                    SizedBox(height: 1),
                    Text('Livraison à Ma maison', style: TextStyle(color: Color(0xFFD4EFE4), fontSize: 10, fontWeight: FontWeight.w700)),
                  ],
                ),
              ),
              _HeaderIcon(icon: Icons.notifications_none_rounded, onTap: () => context.push('/notifications')),
              const SizedBox(width: 8),
              _HeaderIcon(icon: Icons.shopping_cart_outlined, badge: '2', onTap: () => context.go('/cart')),
            ],
          ),
          const SizedBox(height: 16),
          GestureDetector(
            onTap: () => context.go('/search'),
            child: Container(
              height: 40,
              padding: const EdgeInsets.symmetric(horizontal: 14),
              decoration: BoxDecoration(color: Colors.white, borderRadius: BorderRadius.circular(12)),
              child: Row(
                children: [
                  const Icon(Icons.search_rounded, color: Color(0xFF63758C), size: 19),
                  const SizedBox(width: 10),
                  Text('Rechercher un produit, une marque...', style: TextStyle(color: Colors.blueGrey.shade300, fontSize: 12)),
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
            decoration: BoxDecoration(color: Colors.white.withValues(alpha: 0.18), borderRadius: BorderRadius.circular(10)),
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

class _DailyOffer extends StatelessWidget {
  const _DailyOffer();

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.fromLTRB(16, 14, 16, 14),
      child: Container(
        height: 172,
        padding: const EdgeInsets.all(16),
        decoration: BoxDecoration(color: _navy, borderRadius: BorderRadius.circular(18)),
        child: Row(
          children: [
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  const _Chip(label: "JUSQU'À -40%", color: _green, textColor: Colors.white),
                  const SizedBox(height: 14),
                  const Text('Offres du jour', style: TextStyle(color: Colors.white, fontSize: 20, fontWeight: FontWeight.w900)),
                  const SizedBox(height: 4),
                  const Text('High-tech, mode et maison à\nprix réduits.', style: TextStyle(color: Color(0xFFD7DFE7), fontSize: 12, height: 1.25)),
                  const Spacer(),
                  FilledButton(
                    onPressed: () => context.go('/search'),
                    style: FilledButton.styleFrom(
                      backgroundColor: Colors.white,
                      foregroundColor: _navy,
                      minimumSize: const Size(82, 34),
                      tapTargetSize: MaterialTapTargetSize.shrinkWrap,
                      shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(10)),
                    ),
                    child: const Text('Découvrir', style: TextStyle(fontSize: 12, fontWeight: FontWeight.w900)),
                  ),
                ],
              ),
            ),
            Container(
              width: 96,
              height: 140,
              decoration: BoxDecoration(color: const Color(0xFF3A4A5D), borderRadius: BorderRadius.circular(12)),
              alignment: Alignment.center,
              child: Text('PRODUIT', style: TextStyle(color: Colors.white.withValues(alpha: 0.6), fontSize: 8, letterSpacing: 2, fontWeight: FontWeight.w900)),
            ),
          ],
        ),
      ),
    );
  }
}

class _CategoryRail extends StatelessWidget {
  const _CategoryRail();

  static const items = [
    _Category('Téléphones', Color(0xFFE5E9EF)),
    _Category('Informatique', Color(0xFFE8EEF2)),
    _Category('Mode', Color(0xFFEFE8DF)),
    _Category('Maison', Color(0xFFE3ECE8)),
    _Category('Beauté', Color(0xFFF1E5EB)),
  ];

  @override
  Widget build(BuildContext context) {
    return SizedBox(
      height: 82,
      child: ListView.separated(
        padding: const EdgeInsets.symmetric(horizontal: 20),
        scrollDirection: Axis.horizontal,
        itemCount: items.length,
        separatorBuilder: (_, __) => const SizedBox(width: 18),
        itemBuilder: (_, index) {
          final item = items[index];
          return SizedBox(
            width: 56,
            child: Column(
              children: [
                Container(
                  width: 54,
                  height: 50,
                  decoration: BoxDecoration(color: item.color, borderRadius: BorderRadius.circular(14)),
                  alignment: Alignment.center,
                  child: const Text('ICON', style: TextStyle(color: Color(0xFF8CA0B0), fontSize: 7, letterSpacing: 1.5, fontWeight: FontWeight.w900)),
                ),
                const SizedBox(height: 8),
                Text(item.name, maxLines: 1, overflow: TextOverflow.ellipsis, style: const TextStyle(fontSize: 10, fontWeight: FontWeight.w700)),
              ],
            ),
          );
        },
      ),
    );
  }
}

class _FlashDeals extends StatelessWidget {
  const _FlashDeals();

  @override
  Widget build(BuildContext context) {
    return Column(
      children: [
        const _SectionTitle(title: 'Offres flash', trailing: '02:14:09'),
        SizedBox(
          height: 232,
          child: ListView.separated(
            padding: const EdgeInsets.symmetric(horizontal: 16),
            scrollDirection: Axis.horizontal,
            itemCount: _flashProducts.length,
            separatorBuilder: (_, __) => const SizedBox(width: 10),
            itemBuilder: (_, index) => _FlashCard(product: _flashProducts[index]),
          ),
        ),
      ],
    );
  }
}

class _FlashCard extends StatelessWidget {
  const _FlashCard({required this.product});
  final _Product product;

  @override
  Widget build(BuildContext context) {
    return SizedBox(
      width: 142,
      child: _Surface(
        padding: const EdgeInsets.all(9),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            _PhotoPlaceholder(height: 98, color: product.photoColor, badge: product.discount),
            const SizedBox(height: 9),
            Text(product.name, maxLines: 2, overflow: TextOverflow.ellipsis, style: const TextStyle(fontSize: 12, fontWeight: FontWeight.w800)),
            const Spacer(),
            Text(Format.money(product.price, 'XOF'), style: const TextStyle(color: _green, fontSize: 13, fontWeight: FontWeight.w900)),
            Row(
              children: [
                Expanded(child: Text(Format.money(product.oldPrice!, 'XOF'), style: const TextStyle(color: Color(0xFF9FAEBC), fontSize: 10, decoration: TextDecoration.lineThrough))),
                Text(product.rating, style: const TextStyle(color: Color(0xFF5F7288), fontSize: 10)),
              ],
            ),
            const SizedBox(height: 5),
            ClipRRect(
              borderRadius: BorderRadius.circular(999),
              child: LinearProgressIndicator(value: product.stock, minHeight: 3, backgroundColor: const Color(0xFFE6EDF0), color: _orange),
            ),
            const SizedBox(height: 5),
            Text(product.stockLabel,
                maxLines: 1,
                overflow: TextOverflow.ellipsis,
                style: const TextStyle(color: Color(0xFF7B8DA1), fontSize: 9, fontWeight: FontWeight.w700)),
          ],
        ),
      ),
    );
  }
}

class _ProductGrid extends StatelessWidget {
  const _ProductGrid();

  @override
  Widget build(BuildContext context) {
    return SliverPadding(
      padding: const EdgeInsets.symmetric(horizontal: 16),
      sliver: SliverGrid.builder(
        itemCount: _recommendedProducts.length,
        gridDelegate: const SliverGridDelegateWithFixedCrossAxisCount(
          crossAxisCount: 2,
          crossAxisSpacing: 10,
          mainAxisSpacing: 12,
          childAspectRatio: 0.58,
        ),
        itemBuilder: (_, index) => _ProductCard(product: _recommendedProducts[index]),
      ),
    );
  }
}

class _ProductCard extends StatelessWidget {
  const _ProductCard({required this.product});
  final _Product product;

  @override
  Widget build(BuildContext context) {
    return _Surface(
      padding: const EdgeInsets.all(9),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Stack(
            children: [
              _PhotoPlaceholder(height: 106, color: product.photoColor),
              Positioned(
                right: 6,
                top: 6,
                child: Container(
                  width: 26,
                  height: 26,
                  decoration: BoxDecoration(color: Colors.white, borderRadius: BorderRadius.circular(9)),
                  child: Icon(product.favorite ? Icons.favorite_rounded : Icons.favorite_border_rounded, color: product.favorite ? const Color(0xFFF35B42) : _navy, size: 15),
                ),
              ),
            ],
          ),
          const SizedBox(height: 9),
          Text(product.name, maxLines: 2, overflow: TextOverflow.ellipsis, style: const TextStyle(fontSize: 12, fontWeight: FontWeight.w800)),
          const Spacer(),
          Text(
            Format.money(product.price, 'XOF'),
            maxLines: 1,
            overflow: TextOverflow.ellipsis,
            style: const TextStyle(color: _green, fontSize: 13, fontWeight: FontWeight.w900),
          ),
          Row(
            children: [
              const Icon(Icons.star_rounded, color: _navy, size: 13),
              const SizedBox(width: 2),
              Expanded(child: Text('${product.rating}\n${product.shop}', maxLines: 2, overflow: TextOverflow.ellipsis, style: const TextStyle(color: Color(0xFF65778C), fontSize: 10, height: 1.2))),
              Container(
                width: 30,
                height: 30,
                decoration: BoxDecoration(color: _green, borderRadius: BorderRadius.circular(10)),
                child: const Icon(Icons.add_rounded, color: Colors.white, size: 19),
              ),
            ],
          ),
        ],
      ),
    );
  }
}

class _RecommendedStores extends StatelessWidget {
  const _RecommendedStores();

  @override
  Widget build(BuildContext context) {
    return Column(
      children: [
        const _SectionTitle(title: 'Boutiques recommandées'),
        SizedBox(
          height: 104,
          child: ListView.separated(
            padding: const EdgeInsets.symmetric(horizontal: 16),
            scrollDirection: Axis.horizontal,
            itemCount: _stores.length,
            separatorBuilder: (_, __) => const SizedBox(width: 10),
            itemBuilder: (_, index) {
              final store = _stores[index];
              return SizedBox(
                width: 216,
                child: _Surface(
                  padding: const EdgeInsets.all(12),
                  child: Row(
                    children: [
                      CircleAvatar(backgroundColor: store.color, child: Text(store.initials, style: const TextStyle(color: _navy, fontSize: 12, fontWeight: FontWeight.w900))),
                      const SizedBox(width: 10),
                      Expanded(
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          mainAxisAlignment: MainAxisAlignment.center,
                          children: [
                            Text(store.name, maxLines: 1, overflow: TextOverflow.ellipsis, style: const TextStyle(fontSize: 12, fontWeight: FontWeight.w900)),
                            Text('${store.category} • ★ ${store.rating}', style: const TextStyle(color: Color(0xFF66798C), fontSize: 10)),
                            const SizedBox(height: 8),
                            Row(
                              children: [
                                const Flexible(child: _Chip(label: '✓  Vendeur vérifié', color: Color(0xFFE0F4EA), textColor: _green)),
                                TextButton(
                                  onPressed: () => context.push('/shop/${store.id}'),
                                  style: TextButton.styleFrom(
                                    visualDensity: VisualDensity.compact,
                                    minimumSize: const Size(44, 28),
                                    padding: const EdgeInsets.symmetric(horizontal: 6),
                                    tapTargetSize: MaterialTapTargetSize.shrinkWrap,
                                  ),
                                  child: const Text('Visiter', style: TextStyle(fontSize: 10, fontWeight: FontWeight.w900)),
                                ),
                              ],
                            ),
                          ],
                        ),
                      ),
                    ],
                  ),
                ),
              );
            },
          ),
        ),
      ],
    );
  }
}

class _NewArrivals extends StatelessWidget {
  const _NewArrivals();

  @override
  Widget build(BuildContext context) {
    return Column(
      children: [
        const _SectionTitle(title: 'Nouveautés'),
        SizedBox(
          height: 152,
          child: ListView.separated(
            padding: const EdgeInsets.symmetric(horizontal: 16),
            scrollDirection: Axis.horizontal,
            itemCount: _newProducts.length,
            separatorBuilder: (_, __) => const SizedBox(width: 10),
            itemBuilder: (_, index) {
              final product = _newProducts[index];
              return SizedBox(
                width: 120,
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    _PhotoPlaceholder(height: 88, color: product.photoColor),
                    const SizedBox(height: 8),
                    Text(product.name, maxLines: 1, overflow: TextOverflow.ellipsis, style: const TextStyle(fontSize: 12, fontWeight: FontWeight.w800)),
                    Text(Format.money(product.price, 'XOF'), style: const TextStyle(color: _green, fontSize: 12, fontWeight: FontWeight.w900)),
                  ],
                ),
              );
            },
          ),
        ),
      ],
    );
  }
}

class _SectionTitle extends StatelessWidget {
  const _SectionTitle({required this.title, this.subtitle, this.trailing});
  final String title;
  final String? subtitle;
  final String? trailing;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.fromLTRB(16, 18, 16, 10),
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
          if (trailing != null)
            Container(
              padding: const EdgeInsets.symmetric(horizontal: 9, vertical: 5),
              decoration: BoxDecoration(color: const Color(0xFFFFE8DD), borderRadius: BorderRadius.circular(9)),
              child: Text(trailing!, style: const TextStyle(color: _orange, fontSize: 10, fontWeight: FontWeight.w900)),
            )
          else
            TextButton(onPressed: () => context.go('/search'), child: const Text('Voir tout', style: TextStyle(color: _green, fontSize: 11, fontWeight: FontWeight.w900))),
        ],
      ),
    );
  }
}

class _PhotoPlaceholder extends StatelessWidget {
  const _PhotoPlaceholder({required this.height, required this.color, this.badge});
  final double height;
  final Color color;
  final String? badge;

  @override
  Widget build(BuildContext context) {
    return Stack(
      children: [
        Container(
          height: height,
          decoration: BoxDecoration(color: color, borderRadius: BorderRadius.circular(12)),
          alignment: Alignment.center,
          child: const Text('PHOTO', style: TextStyle(color: Color(0xFF9FAFBD), fontSize: 8, letterSpacing: 2, fontWeight: FontWeight.w900)),
        ),
        if (badge != null)
          Positioned(left: 7, top: 7, child: _Chip(label: badge!, color: const Color(0xFFF15B43), textColor: Colors.white)),
      ],
    );
  }
}

class _Surface extends StatelessWidget {
  const _Surface({required this.child, required this.padding});
  final Widget child;
  final EdgeInsetsGeometry padding;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: padding,
      decoration: BoxDecoration(
        color: Colors.white,
        borderRadius: BorderRadius.circular(14),
        boxShadow: [BoxShadow(color: Colors.black.withValues(alpha: 0.05), blurRadius: 18, offset: const Offset(0, 8))],
      ),
      child: child,
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
      padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 4),
      decoration: BoxDecoration(color: color, borderRadius: BorderRadius.circular(999)),
      child: Text(label, style: TextStyle(color: textColor, fontSize: 9, fontWeight: FontWeight.w900)),
    );
  }
}

class _Category {
  const _Category(this.name, this.color);
  final String name;
  final Color color;
}

class _Product {
  const _Product({
    required this.name,
    required this.price,
    required this.rating,
    required this.shop,
    required this.photoColor,
    this.oldPrice,
    this.discount,
    this.stock = 0.55,
    this.stockLabel = '',
    this.favorite = false,
  });

  final String name;
  final int price;
  final String rating;
  final String shop;
  final Color photoColor;
  final int? oldPrice;
  final String? discount;
  final double stock;
  final String stockLabel;
  final bool favorite;
}

class _Store {
  const _Store(this.id, this.initials, this.name, this.category, this.rating, this.color);
  final String id;
  final String initials;
  final String name;
  final String category;
  final String rating;
  final Color color;
}

const _flashProducts = [
  _Product(name: 'Sony WH-1000XM5', price: 279000, oldPrice: 325000, discount: '-14%', rating: '4,9', shop: 'HBA Tech Store', photoColor: Color(0xFFE5E9EF), stock: 0.72, stockLabel: 'Plus que 12 en stock'),
  _Product(name: 'Blender Pro 1200 W', price: 64000, oldPrice: 72000, discount: '-11%', rating: '4,7', shop: 'Home Market', photoColor: Color(0xFFE3ECE8), stock: 0.44, stockLabel: 'Bientôt épuisé'),
  _Product(name: 'Sneakers Air Runner', price: 42500, oldPrice: 55000, discount: '-23%', rating: '4,6', shop: 'Fashion House', photoColor: Color(0xFFEDE7DF), stock: 0.62, stockLabel: 'Vente flash'),
];

const _recommendedProducts = [
  _Product(name: 'Sony WH-1000XM5', price: 279000, rating: '4,9', shop: 'HBA Tech Store', photoColor: Color(0xFFE5E9EF), favorite: true),
  _Product(name: 'iPhone 14 Pro 128 Go', price: 620000, rating: '4,8', shop: 'HBA Tech Store', photoColor: Color(0xFFE8EEF2)),
  _Product(name: 'Sneakers Air Runner', price: 42500, rating: '4,6', shop: 'Fashion House', photoColor: Color(0xFFEDE7DF)),
  _Product(name: 'Montre Classic Steel', price: 89000, rating: '4,7', shop: 'Time & Co', photoColor: Color(0xFFE5E9EF), favorite: true),
];

const _newProducts = [
  _Product(name: 'Montre Classic Steel', price: 89000, rating: '4,7', shop: 'Time & Co', photoColor: Color(0xFFE5E9EF)),
  _Product(name: 'Blender Pro 1200 W', price: 64000, rating: '4,7', shop: 'Home Market', photoColor: Color(0xFFE3ECE8)),
  _Product(name: 'Sac à dos Urban', price: 27500, rating: '4,5', shop: 'Fashion House', photoColor: Color(0xFFEDE7DF)),
];

const _stores = [
  _Store('hba-tech-store', 'HT', 'HBA Tech Store', 'Électronique', '4,9', Color(0xFFE5E9EF)),
  _Store('fashion-house', 'FH', 'Fashion House', 'Mode', '4,7', Color(0xFFEDE7DF)),
];
