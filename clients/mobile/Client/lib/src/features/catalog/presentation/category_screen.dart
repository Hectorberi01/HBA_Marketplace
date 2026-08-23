import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../core/theme/app_theme.dart';
import '../../../shared/widgets/async_views.dart';
import '../../../shared/widgets/product_card.dart';
import '../../../shared/widgets/ui_kit.dart';
import '../catalog_data.dart';

/// Parcours d'un RAYON : affiche les produits de la catégorie ET de tout son
/// sous-arbre (sous-catégories, sous-sous-catégories…), en LAZY LOADING. Une
/// rangée de sous-catégories sert de filtre ; le tri est appliqué côté serveur.
class CategoryScreen extends ConsumerStatefulWidget {
  const CategoryScreen({super.key, required this.categoryId, this.categoryName});

  final String categoryId;
  final String? categoryName;

  @override
  ConsumerState<CategoryScreen> createState() => _CategoryScreenState();
}

class _CategoryScreenState extends ConsumerState<CategoryScreen> {
  final _scroll = ScrollController();
  static const _pageSize = 20;
  static const _sorts = ['Pertinence', 'Prix croissant', 'Mieux notés'];

  int _sort = 0;
  String? _selectedSubId; // null = tout le rayon (parent + sous-arbre)
  final List<ProductCard> _items = [];
  int _page = 0;
  int _total = 0;
  bool _hasMore = true;
  bool _loading = false;
  Object? _error;
  bool _started = false;

  String? get _sortKey => switch (_sort) { 1 => 'price', 2 => 'rating', _ => null };

  @override
  void initState() {
    super.initState();
    _scroll.addListener(_onScroll);
  }

  @override
  void dispose() {
    _scroll.removeListener(_onScroll);
    _scroll.dispose();
    super.dispose();
  }

  void _onScroll() {
    // Précharge la page suivante avant d'atteindre le bas.
    if (_scroll.position.pixels >= _scroll.position.maxScrollExtent - 400) {
      _loadMore();
    }
  }

  Future<void> _reset() async {
    setState(() {
      _items.clear();
      _page = 0;
      _total = 0;
      _hasMore = true;
      _error = null;
    });
    await _loadMore();
  }

  Future<void> _loadMore() async {
    if (_loading || !_hasMore) return;
    final cats = ref.read(categoriesProvider).valueOrNull ?? const <Category>[];
    setState(() => _loading = true);
    try {
      final root = _selectedSubId ?? widget.categoryId;
      final ids = categorySubtreeIds(root, cats);
      final next = _page + 1;
      final res = await ref.read(catalogApiProvider).searchPaged(
            categoryIds: ids,
            sort: _sortKey,
            page: next,
            pageSize: _pageSize,
          );
      if (!mounted) return;
      setState(() {
        _items.addAll(res.items);
        _page = next;
        _total = res.total;
        _hasMore = res.hasMore;
        _error = null;
      });
    } catch (e) {
      if (mounted) setState(() => _error = e);
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final catsAsync = ref.watch(categoriesProvider);
    final title = (widget.categoryName?.trim().isNotEmpty ?? false) ? widget.categoryName!.trim() : 'Catégorie';

    return Scaffold(
      appBar: AppBar(
        title: Text(title),
        actions: [
          // Tri déplacé ici (icône) pour laisser toute la largeur au carrousel
          // de sous-catégories.
          PopupMenuButton<int>(
            icon: const Icon(Icons.swap_vert),
            tooltip: 'Trier',
            initialValue: _sort,
            onSelected: (v) {
              if (v == _sort) return;
              setState(() => _sort = v);
              _reset();
            },
            itemBuilder: (_) => [
              for (var i = 0; i < _sorts.length; i++)
                PopupMenuItem<int>(
                  value: i,
                  child: Row(children: [
                    Icon(Icons.check, size: 18, color: i == _sort ? AppTheme.brandGreen : Colors.transparent),
                    const SizedBox(width: 8),
                    Text(_sorts[i]),
                  ]),
                ),
            ],
          ),
        ],
      ),
      body: catsAsync.when(
        loading: () => const LoadingView(),
        error: (e, _) => ErrorView(message: e.toString(), onRetry: () => ref.invalidate(categoriesProvider)),
        data: (cats) {
          // Premier chargement dès que l'arbre est disponible.
          if (!_started) {
            _started = true;
            WidgetsBinding.instance.addPostFrameCallback((_) => _loadMore());
          }

          final subcats = cats.where((c) => c.parentId == widget.categoryId).toList();

          // Échec du tout premier chargement : rien à montrer → écran d'erreur.
          if (_error != null && _items.isEmpty) {
            return ErrorView(message: _error.toString(), onRetry: _reset);
          }

          return CustomScrollView(controller: _scroll, slivers: [
            // Filtre sous-catégories (si la catégorie en a).
            if (subcats.isNotEmpty)
              SliverToBoxAdapter(
                child: _SubcategoryBar(
                  subcats: subcats,
                  selectedId: _selectedSubId,
                  onSelect: (id) {
                    setState(() => _selectedSubId = id);
                    _reset();
                  },
                ),
              ),

            if (_total > 0)
              SliverToBoxAdapter(
                child: Padding(
                  padding: const EdgeInsets.fromLTRB(16, 0, 16, 8),
                  child: Text('$_total produit${_total > 1 ? 's' : ''}',
                      style: TextStyle(color: AppTheme.subtle, fontSize: 13)),
                ),
              ),

            // Grille produits (ce qui est chargé).
            SliverPadding(
              padding: const EdgeInsets.fromLTRB(16, 0, 16, 8),
              sliver: SliverGrid(
                gridDelegate: const SliverGridDelegateWithMaxCrossAxisExtent(
                  maxCrossAxisExtent: 220,
                  childAspectRatio: 0.62,
                  crossAxisSpacing: 12,
                  mainAxisSpacing: 12,
                ),
                delegate: SliverChildBuilderDelegate(
                  (_, i) {
                    final p = _items[i];
                    return ProductCardTile(id: p.id, name: p.name, url: p.url, price: p.price, currency: p.currency, rating: p.rating, originalPrice: p.compareAtPrice, promoLabel: p.isOnSale ? '-${p.discountPercent}%' : null);
                  },
                  childCount: _items.length,
                ),
              ),
            ),

            // Pied : chargement, vide, ou fin de liste.
            SliverToBoxAdapter(child: _Footer(loading: _loading, empty: _items.isEmpty, hasMore: _hasMore)),
          ]);
        },
      ),
    );
  }
}

/// Rangée horizontale de sous-catégories (chip « Tout » + chaque sous-catégorie).
class _SubcategoryBar extends StatelessWidget {
  const _SubcategoryBar({required this.subcats, required this.selectedId, required this.onSelect});
  final List<Category> subcats;
  final String? selectedId;
  final void Function(String?) onSelect;

  @override
  Widget build(BuildContext context) {
    // Carrousel horizontal des sous-catégories, puces de MÊME taille que le tri.
    // Un fondu sur le bord droit (ShaderMask) signale qu'on peut faire défiler :
    // la dernière puce se fond au lieu d'être coupée net.
    return SizedBox(
      height: 56,
      child: ShaderMask(
        // dstIn : l'alpha du dégradé devient l'opacité du contenu. Opaque jusqu'à
        // 90 % puis fondu vers transparent à droite. (RGB indifférent en dstIn,
        // donc identique en clair comme en sombre.)
        shaderCallback: (rect) => const LinearGradient(
          begin: Alignment.centerLeft,
          end: Alignment.centerRight,
          colors: [Colors.white, Colors.white, Colors.transparent],
          stops: [0.0, 0.9, 1.0],
        ).createShader(rect),
        blendMode: BlendMode.dstIn,
        child: ListView.separated(
          scrollDirection: Axis.horizontal,
          padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 10),
          itemCount: subcats.length + 1,
          separatorBuilder: (_, __) => const SizedBox(width: 8),
          itemBuilder: (_, i) {
            // `Center` : la puce garde sa hauteur NATURELLE (elle n'est pas étirée
            // à la hauteur de la rangée), ce qui laisse la place aux jambages
            // (g, q, p…) au lieu de les rogner.
            final chip = i == 0
                ? FilterChipPill(label: 'Tout', selected: selectedId == null, onTap: () => onSelect(null))
                : FilterChipPill(
                    label: subcats[i - 1].name,
                    selected: selectedId == subcats[i - 1].id,
                    onTap: () => onSelect(subcats[i - 1].id),
                  );
            return Center(child: chip);
          },
        ),
      ),
    );
  }
}

class _Footer extends StatelessWidget {
  const _Footer({required this.loading, required this.empty, required this.hasMore});
  final bool loading;
  final bool empty;
  final bool hasMore;

  @override
  Widget build(BuildContext context) {
    if (loading) {
      return const Padding(
        padding: EdgeInsets.symmetric(vertical: 24),
        child: Center(child: SizedBox(width: 24, height: 24, child: CircularProgressIndicator(strokeWidth: 2.5))),
      );
    }
    if (empty) {
      return const Padding(
        padding: EdgeInsets.only(top: 60),
        child: EmptyView(message: 'Aucun produit dans ce rayon pour l’instant.', icon: Icons.inventory_2_outlined),
      );
    }
    if (!hasMore) {
      return Padding(
        padding: const EdgeInsets.symmetric(vertical: 16),
        child: Center(child: Text('Vous avez tout vu', style: TextStyle(color: AppTheme.subtle, fontSize: 12))),
      );
    }
    return const SizedBox(height: 24);
  }
}
