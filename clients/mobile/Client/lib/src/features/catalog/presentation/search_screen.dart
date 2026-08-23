import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';

import '../../../core/theme/app_theme.dart';
import '../../../shared/widgets/ui_kit.dart';

const _green = AppTheme.brandGreen;
const _navy = Color(0xFF0E2239);
const _orange = Color(0xFFE56400);

class SearchScreen extends StatefulWidget {
  const SearchScreen({super.key});

  @override
  State<SearchScreen> createState() => _SearchScreenState();
}

class _SearchScreenState extends State<SearchScreen> {
  bool _food = false;

  @override
  Widget build(BuildContext context) {
    final accent = _food ? _orange : _green;
    return Scaffold(
      backgroundColor: const Color(0xFFF4F6F5),
      body: SafeArea(
        bottom: false,
        child: CustomScrollView(
          slivers: [
            SliverToBoxAdapter(child: _ExplorerHeader(food: _food, accent: accent, onChanged: (v) => setState(() => _food = v))),
            if (_food) ...const [
              SliverToBoxAdapter(child: _FoodCategories()),
              SliverToBoxAdapter(child: _FoodPopularRail()),
              SliverToBoxAdapter(child: _FoodRestaurants()),
              SliverToBoxAdapter(child: _TrendPanel.food()),
              SliverToBoxAdapter(child: _OfferRail.food()),
            ] else ...const [
              SliverToBoxAdapter(child: _ExpressCategories()),
              SliverToBoxAdapter(child: _BrandRail()),
              SliverToBoxAdapter(child: _ExpressStores()),
              SliverToBoxAdapter(child: _TrendPanel.express()),
              SliverToBoxAdapter(child: _OfferRail.express()),
            ],
            SliverToBoxAdapter(child: SizedBox(height: bottomSafePadding(context, extra: 92))),
          ],
        ),
      ),
    );
  }
}

class _ExplorerHeader extends StatelessWidget {
  const _ExplorerHeader({required this.food, required this.accent, required this.onChanged});
  final bool food;
  final Color accent;
  final ValueChanged<bool> onChanged;

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
          const Text('Explorer', style: TextStyle(color: _navy, fontSize: 28, fontWeight: FontWeight.w900)),
          const SizedBox(height: 18),
          GestureDetector(
            onTap: () {},
            child: Container(
              height: 56,
              padding: const EdgeInsets.symmetric(horizontal: 16),
              decoration: BoxDecoration(color: const Color(0xFFF0F2F4), borderRadius: BorderRadius.circular(18)),
              child: Row(
                children: [
                  const Icon(Icons.search_rounded, color: Color(0xFF6D7F93), size: 22),
                  const SizedBox(width: 14),
                  Expanded(
                    child: Text(
                      food ? 'Rechercher un restaurant ou un plat' : 'Rechercher un produit, une marque...',
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                      style: const TextStyle(color: Color(0xFF9AA7B6), fontSize: 15, fontWeight: FontWeight.w600),
                    ),
                  ),
                ],
              ),
            ),
          ),
          const SizedBox(height: 16),
          Container(
            height: 62,
            padding: const EdgeInsets.all(7),
            decoration: BoxDecoration(color: const Color(0xFFF0F2F4), borderRadius: BorderRadius.circular(20)),
            child: Row(
              children: [
                Expanded(child: _Segment(label: 'HBAExpress', selected: !food, color: accent, onTap: () => onChanged(false))),
                Expanded(child: _Segment(label: 'HBA Food', selected: food, color: accent, onTap: () => onChanged(true))),
              ],
            ),
          ),
        ],
      ),
    );
  }
}

class _Segment extends StatelessWidget {
  const _Segment({required this.label, required this.selected, required this.color, required this.onTap});
  final String label;
  final bool selected;
  final Color color;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    return GestureDetector(
      onTap: onTap,
      child: AnimatedContainer(
        duration: const Duration(milliseconds: 160),
        height: double.infinity,
        alignment: Alignment.center,
        decoration: BoxDecoration(
          color: selected ? Colors.white : Colors.transparent,
          borderRadius: BorderRadius.circular(15),
          boxShadow: selected ? [BoxShadow(color: Colors.black.withValues(alpha: 0.08), blurRadius: 12, offset: const Offset(0, 4))] : null,
        ),
        child: Text(label, style: TextStyle(color: selected ? _navy : const Color(0xFF708197), fontSize: 15, fontWeight: FontWeight.w900)),
      ),
    );
  }
}

class _ExpressCategories extends StatelessWidget {
  const _ExpressCategories();

  @override
  Widget build(BuildContext context) {
    return const _CategoryGrid(
      title: 'Catégories produits',
      subtitle: 'Parcourir tout le catalogue Express',
      actionColor: _green,
      items: [
        _CategoryItem('Téléphones', '1 240 articles', Color(0xFFE5EAF0)),
        _CategoryItem('Informatique', '860 articles', Color(0xFFE7ECEF)),
        _CategoryItem('Mode', '2 100 articles', Color(0xFFECE6DF)),
        _CategoryItem('Maison', '940 articles', Color(0xFFE2EBE6)),
        _CategoryItem('Beauté', '520 articles', Color(0xFFEDE5EA)),
        _CategoryItem('Auto', '310 articles', Color(0xFFE6E9EE)),
        _CategoryItem('Sport', '480 articles', Color(0xFFE2EBE6)),
        _CategoryItem('Électronique', '1 020 articles', Color(0xFFE6E9EE)),
      ],
    );
  }
}

class _FoodCategories extends StatelessWidget {
  const _FoodCategories();

  @override
  Widget build(BuildContext context) {
    return const _CategoryGrid(
      title: 'Cuisines',
      subtitle: 'Ce que vous pouvez commander près de vous',
      actionColor: _orange,
      items: [
        _CategoryItem('Cuisine\nbéninoise', '46 restaurants', Color(0xFFE8DDCF)),
        _CategoryItem('Fast-food', '32 restaurants', Color(0xFFE9E3D6)),
        _CategoryItem('Pizza', '18 restaurants', Color(0xFFE7DDD3)),
        _CategoryItem('Poulet', '24 restaurants', Color(0xFFECE2D5)),
        _CategoryItem('Grillades', '21 restaurants', Color(0xFFE4E1D7)),
        _CategoryItem('Desserts', '15 restaurants', Color(0xFFEDE0DE)),
        _CategoryItem('Boissons', '28 restaurants', Color(0xFFE3E2D9)),
        _CategoryItem('Petit-déjeuner', '12 restaurants', Color(0xFFE9E3D6)),
      ],
    );
  }
}

class _CategoryGrid extends StatelessWidget {
  const _CategoryGrid({required this.title, required this.subtitle, required this.actionColor, required this.items});
  final String title;
  final String subtitle;
  final Color actionColor;
  final List<_CategoryItem> items;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.fromLTRB(16, 26, 16, 0),
      child: Column(
        children: [
          _SectionHeader(title: title, subtitle: subtitle, color: actionColor),
          const SizedBox(height: 14),
          GridView.builder(
            shrinkWrap: true,
            physics: const NeverScrollableScrollPhysics(),
            itemCount: items.length,
            gridDelegate: const SliverGridDelegateWithFixedCrossAxisCount(
              crossAxisCount: 2,
              mainAxisSpacing: 12,
              crossAxisSpacing: 12,
              childAspectRatio: 2.0,
            ),
            itemBuilder: (_, index) => _CategoryCard(item: items[index]),
          ),
        ],
      ),
    );
  }
}

class _CategoryCard extends StatelessWidget {
  const _CategoryCard({required this.item});
  final _CategoryItem item;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.all(14),
      decoration: BoxDecoration(color: Colors.white, borderRadius: BorderRadius.circular(16)),
      clipBehavior: Clip.antiAlias,
      child: Stack(
        children: [
          Positioned(
            right: -18,
            bottom: -20,
            child: Container(width: 84, height: 70, decoration: BoxDecoration(color: item.color, borderRadius: BorderRadius.circular(26))),
          ),
          Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(item.name, maxLines: 2, overflow: TextOverflow.ellipsis, style: const TextStyle(color: _navy, fontSize: 15, height: 1.1, fontWeight: FontWeight.w900)),
              const Spacer(),
              Text(item.count, maxLines: 1, overflow: TextOverflow.ellipsis, style: const TextStyle(color: Color(0xFF8797AA), fontSize: 12, fontWeight: FontWeight.w600)),
            ],
          ),
        ],
      ),
    );
  }
}

class _BrandRail extends StatelessWidget {
  const _BrandRail();

  @override
  Widget build(BuildContext context) {
    return const _CircleRail(
      title: 'Marques',
      color: _green,
      items: [
        _CircleItem('SA', 'Samsung'),
        _CircleItem('SO', 'Sony'),
        _CircleItem('AP', 'Apple'),
        _CircleItem('NI', 'Nike'),
        _CircleItem('PH', 'Philips'),
      ],
    );
  }
}

class _FoodPopularRail extends StatelessWidget {
  const _FoodPopularRail();

  @override
  Widget build(BuildContext context) {
    return const _CircleRail(
      title: 'Plats populaires',
      color: _orange,
      items: [
        _CircleItem('PO', 'Poulet braisé'),
        _CircleItem('AT', 'Attiéké'),
        _CircleItem('AM', 'Amiwo'),
        _CircleItem('PI', 'Pizza'),
        _CircleItem('SH', 'Shawarma'),
      ],
    );
  }
}

class _CircleRail extends StatelessWidget {
  const _CircleRail({required this.title, required this.color, required this.items});
  final String title;
  final Color color;
  final List<_CircleItem> items;

  @override
  Widget build(BuildContext context) {
    return Column(
      children: [
        Padding(
          padding: const EdgeInsets.fromLTRB(16, 28, 16, 12),
          child: _SectionHeader(title: title, color: color),
        ),
        SizedBox(
          height: 102,
          child: ListView.separated(
            padding: const EdgeInsets.symmetric(horizontal: 16),
            scrollDirection: Axis.horizontal,
            itemCount: items.length,
            separatorBuilder: (_, __) => const SizedBox(width: 18),
            itemBuilder: (_, index) {
              final item = items[index];
              return SizedBox(
                width: 72,
                child: Column(
                  children: [
                    Container(
                      width: 68,
                      height: 68,
                      decoration: BoxDecoration(color: Colors.white, shape: BoxShape.circle, boxShadow: [BoxShadow(color: Colors.black.withValues(alpha: 0.06), blurRadius: 12, offset: const Offset(0, 3))]),
                      alignment: Alignment.center,
                      child: Text(item.initials, style: const TextStyle(color: _navy, fontSize: 15, fontWeight: FontWeight.w900)),
                    ),
                    const SizedBox(height: 10),
                    Text(item.name, maxLines: 1, overflow: TextOverflow.ellipsis, textAlign: TextAlign.center, style: const TextStyle(color: _navy, fontSize: 11, fontWeight: FontWeight.w700)),
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

class _ExpressStores extends StatelessWidget {
  const _ExpressStores();

  @override
  Widget build(BuildContext context) {
    return const _ListSection(
      title: 'Boutiques',
      color: _green,
      children: [
        _StoreRow(initials: 'HT', title: 'HBA Tech Store', subtitle: 'Électronique · ★ 4,9', detail: '186 produits · Expédition 24 h', color: Color(0xFFE5EAF0), accent: _green),
        _StoreRow(initials: 'FH', title: 'Fashion House', subtitle: 'Mode · ★ 4,7', detail: '304 produits · Expédition 48 h', color: Color(0xFFECE6DF), accent: _green),
        _StoreRow(initials: 'MP', title: 'Maison Plus', subtitle: 'Maison · ★ 4,6', detail: '128 produits · Expédition 24 h', color: Color(0xFFE2EBE6), accent: _green),
        _StoreRow(initials: 'TC', title: 'Time & Co', subtitle: 'Montres · ★ 4,8', detail: '64 produits · Expédition 48 h', color: Color(0xFFE6E9EE), accent: _green),
      ],
    );
  }
}

class _FoodRestaurants extends StatelessWidget {
  const _FoodRestaurants();

  @override
  Widget build(BuildContext context) {
    return const _ListSection(
      title: 'Restaurants',
      color: _orange,
      children: [
        _StoreRow(initials: 'CH', title: 'Chez Mama', subtitle: 'Cuisine béninoise · Grillades · ★ 4,8', detail: '20–30 min · Livraison 800 F CFA', color: Color(0xFFE8DDCF), accent: _orange),
        _StoreRow(initials: 'LE', title: "Le Grill d'Or", subtitle: 'Grillades · Poisson braisé · ★ 4,6', detail: '25–35 min · Livraison offerte', color: Color(0xFFE4E1D7), accent: _orange),
        _StoreRow(initials: 'CH', title: 'Chicken House', subtitle: 'Poulet · Fast-food · ★ 4,5', detail: '15–25 min · Livraison 700 F CFA', color: Color(0xFFE9E3D6), accent: _orange),
        _StoreRow(initials: 'PI', title: 'Pizza Bella', subtitle: 'Pizza · Italien · ★ 4,7', detail: '30–40 min · Livraison 900 F CFA', color: Color(0xFFE7DDD3), accent: _orange),
      ],
    );
  }
}

class _ListSection extends StatelessWidget {
  const _ListSection({required this.title, required this.color, required this.children});
  final String title;
  final Color color;
  final List<Widget> children;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.fromLTRB(16, 26, 16, 0),
      child: Column(
        children: [
          _SectionHeader(title: title, color: color),
          const SizedBox(height: 14),
          ...children.map((child) => Padding(padding: const EdgeInsets.only(bottom: 12), child: child)),
        ],
      ),
    );
  }
}

class _StoreRow extends StatelessWidget {
  const _StoreRow({required this.initials, required this.title, required this.subtitle, required this.detail, required this.color, required this.accent});
  final String initials;
  final String title;
  final String subtitle;
  final String detail;
  final Color color;
  final Color accent;

  @override
  Widget build(BuildContext context) {
    return InkWell(
      borderRadius: BorderRadius.circular(18),
      onTap: () => context.push('/shop/${title.toLowerCase().replaceAll(' ', '-')}'),
      child: Container(
        height: 92,
        padding: const EdgeInsets.all(14),
        decoration: BoxDecoration(color: Colors.white, borderRadius: BorderRadius.circular(18)),
        child: Row(
          children: [
            Container(width: 62, height: 62, decoration: BoxDecoration(color: color, borderRadius: BorderRadius.circular(16)), alignment: Alignment.center, child: Text(initials, style: const TextStyle(color: _navy, fontSize: 17, fontWeight: FontWeight.w900))),
            const SizedBox(width: 14),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                mainAxisAlignment: MainAxisAlignment.center,
                children: [
                  Row(
                    children: [
                      Flexible(child: Text(title, maxLines: 1, overflow: TextOverflow.ellipsis, style: const TextStyle(color: _navy, fontSize: 16, fontWeight: FontWeight.w900))),
                      const SizedBox(width: 6),
                      Icon(Icons.verified_rounded, color: accent, size: 14),
                    ],
                  ),
                  const SizedBox(height: 2),
                  Text(subtitle, maxLines: 1, overflow: TextOverflow.ellipsis, style: const TextStyle(color: Color(0xFF66778C), fontSize: 13)),
                  const SizedBox(height: 4),
                  Text(detail, maxLines: 1, overflow: TextOverflow.ellipsis, style: TextStyle(color: accent, fontSize: 13, fontWeight: FontWeight.w800)),
                ],
              ),
            ),
            const Icon(Icons.chevron_right_rounded, color: Color(0xFFB7C1CC)),
          ],
        ),
      ),
    );
  }
}

class _TrendPanel extends StatelessWidget {
  const _TrendPanel.express()
      : title = 'Tendances cette semaine',
        color = _green,
        rows = const [
          _Trend('01', 'Écouteurs sans fil', '+38 %'),
          _Trend('02', 'Ventilateurs rechargeables', '+24 %'),
          _Trend('03', 'Sacs à dos urbains', '+19 %'),
          _Trend('04', 'Montres connectées', '+12 %'),
        ];

  const _TrendPanel.food()
      : title = 'Tendances près de vous',
        color = _orange,
        rows = const [
          _Trend('01', 'Poulet braisé', '+41 %'),
          _Trend('02', 'Attiéké poisson', '+27 %'),
          _Trend('03', 'Shawarma', '+18 %'),
          _Trend('04', 'Pizza 4 fromages', '+11 %'),
        ];

  final String title;
  final Color color;
  final List<_Trend> rows;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.fromLTRB(16, 26, 16, 0),
      child: Column(
        children: [
          _SectionHeader(title: title, color: color),
          const SizedBox(height: 14),
          Container(
            padding: const EdgeInsets.symmetric(horizontal: 18, vertical: 12),
            decoration: BoxDecoration(color: Colors.white, borderRadius: BorderRadius.circular(18)),
            child: Column(
              children: [
                for (var i = 0; i < rows.length; i++) ...[
                  _TrendRow(row: rows[i], color: color),
                  if (i < rows.length - 1) const Divider(height: 22, color: Color(0xFFE8ECEF)),
                ],
              ],
            ),
          ),
        ],
      ),
    );
  }
}

class _TrendRow extends StatelessWidget {
  const _TrendRow({required this.row, required this.color});
  final _Trend row;
  final Color color;

  @override
  Widget build(BuildContext context) {
    return Row(
      children: [
        SizedBox(width: 40, child: Text(row.rank, style: TextStyle(color: row.rank == '01' || row.rank == '02' ? color : const Color(0xFF98A6B5), fontSize: 16, fontWeight: FontWeight.w900))),
        Expanded(child: Text(row.label, maxLines: 1, overflow: TextOverflow.ellipsis, style: const TextStyle(color: _navy, fontSize: 15, fontWeight: FontWeight.w900))),
        const Icon(Icons.trending_up_rounded, color: _green, size: 16),
        const SizedBox(width: 6),
        Text(row.growth, style: const TextStyle(color: _green, fontSize: 13, fontWeight: FontWeight.w900)),
      ],
    );
  }
}

class _OfferRail extends StatelessWidget {
  const _OfferRail.express()
      : title = 'Offres HBAExpress',
        cards = const [
          _Offer('VENTE FLASH', "Jusqu’à -40 %\nhigh-tech", 'Se termine dans 2 h 14', _navy, Color(0xFF1CE0A4)),
          _Offer('LIVRAISON', 'Livraison offerte dès\n50 000 F', 'HBA Delivery · Cotonou', Color(0xFFE1F4EC), _green),
        ];

  const _OfferRail.food()
      : title = 'Offres HBA Food',
        cards = const [
          _Offer('1 + 1 OFFERT', 'Pizza Bella le mercredi', 'Sur toutes les pizzas 33 cm', _navy, Color(0xFFFFD13F)),
          _Offer('-20 %', 'Plats du jour\nChez Mama', 'Jusqu’à 18 h', Color(0xFFFFF0DF), _orange),
          _Offer('LIVRAISON', 'Livraison offerte', '8 restaurants partenaires', Color(0xFFE1F4EC), _green),
        ];

  final String title;
  final List<_Offer> cards;

  @override
  Widget build(BuildContext context) {
    return Column(
      children: [
        Padding(
          padding: const EdgeInsets.fromLTRB(16, 28, 16, 12),
          child: Align(alignment: Alignment.centerLeft, child: Text(title, style: const TextStyle(color: _navy, fontSize: 19, fontWeight: FontWeight.w900))),
        ),
        SizedBox(
          height: 128,
          child: ListView.separated(
            padding: const EdgeInsets.symmetric(horizontal: 16),
            scrollDirection: Axis.horizontal,
            itemCount: cards.length,
            separatorBuilder: (_, __) => const SizedBox(width: 12),
            itemBuilder: (_, index) => _OfferCard(offer: cards[index]),
          ),
        ),
      ],
    );
  }
}

class _OfferCard extends StatelessWidget {
  const _OfferCard({required this.offer});
  final _Offer offer;

  @override
  Widget build(BuildContext context) {
    final dark = offer.background == _navy;
    return Container(
      width: 210,
      padding: const EdgeInsets.all(18),
      decoration: BoxDecoration(color: offer.background, borderRadius: BorderRadius.circular(18)),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(offer.kicker, maxLines: 1, overflow: TextOverflow.ellipsis, style: TextStyle(color: offer.accent, fontSize: 13, letterSpacing: 1.2, fontWeight: FontWeight.w900)),
          const Spacer(),
          Text(offer.title, maxLines: 2, overflow: TextOverflow.ellipsis, style: TextStyle(color: dark ? Colors.white : _navy, fontSize: 17, height: 1.1, fontWeight: FontWeight.w900)),
          const SizedBox(height: 8),
          Text(offer.subtitle, maxLines: 1, overflow: TextOverflow.ellipsis, style: TextStyle(color: dark ? const Color(0xFFB9C5D0) : offer.accent.withValues(alpha: 0.76), fontSize: 12, fontWeight: FontWeight.w600)),
        ],
      ),
    );
  }
}

class _SectionHeader extends StatelessWidget {
  const _SectionHeader({required this.title, required this.color, this.subtitle});
  final String title;
  final Color color;
  final String? subtitle;

  @override
  Widget build(BuildContext context) {
    return Row(
      children: [
        Expanded(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(title, style: const TextStyle(color: _navy, fontSize: 20, fontWeight: FontWeight.w900)),
              if (subtitle != null) Text(subtitle!, maxLines: 1, overflow: TextOverflow.ellipsis, style: const TextStyle(color: Color(0xFF66778C), fontSize: 13)),
            ],
          ),
        ),
        TextButton(onPressed: () {}, child: Text('Voir tout', style: TextStyle(color: color, fontSize: 13, fontWeight: FontWeight.w900))),
      ],
    );
  }
}

class _CategoryItem {
  const _CategoryItem(this.name, this.count, this.color);
  final String name;
  final String count;
  final Color color;
}

class _CircleItem {
  const _CircleItem(this.initials, this.name);
  final String initials;
  final String name;
}

class _Trend {
  const _Trend(this.rank, this.label, this.growth);
  final String rank;
  final String label;
  final String growth;
}

class _Offer {
  const _Offer(this.kicker, this.title, this.subtitle, this.background, this.accent);
  final String kicker;
  final String title;
  final String subtitle;
  final Color background;
  final Color accent;
}
