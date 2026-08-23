import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../core/theme/app_theme.dart';
import '../../../shared/widgets/async_views.dart';
import '../catalog_data.dart';

/// Liste complète des avis d'un produit : filtre par note + chargement progressif.
class ProductReviewsScreen extends ConsumerStatefulWidget {
  const ProductReviewsScreen({super.key, required this.productId});
  final String productId;

  @override
  ConsumerState<ProductReviewsScreen> createState() => _ProductReviewsScreenState();
}

class _ProductReviewsScreenState extends ConsumerState<ProductReviewsScreen> {
  static const _pageSize = 8;
  final _scroll = ScrollController();
  int? _filter; // null = toutes les notes
  int _visible = _pageSize;

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
    if (_scroll.position.pixels >= _scroll.position.maxScrollExtent - 200) {
      setState(() => _visible += _pageSize);
    }
  }

  void _setFilter(int? rating) {
    setState(() {
      _filter = rating;
      _visible = _pageSize; // on repart du début à chaque changement de filtre
    });
  }

  @override
  Widget build(BuildContext context) {
    final reviews = ref.watch(productReviewsProvider(widget.productId));
    return Scaffold(
      backgroundColor: AppTheme.bg,
      appBar: AppBar(title: const Text('Avis clients')),
      body: reviews.when(
        loading: () => const LoadingView(),
        error: (e, _) => ErrorView(message: e.toString(), onRetry: () => ref.invalidate(productReviewsProvider(widget.productId))),
        data: (all) {
          if (all.isEmpty) {
            return const EmptyView(message: 'Aucun avis pour ce produit.', icon: Icons.rate_review_outlined);
          }

          final avg = all.fold<int>(0, (s, r) => s + r.rating) / all.length;
          final counts = <int, int>{for (var i = 1; i <= 5; i++) i: all.where((r) => r.rating == i).length};
          final filtered = _filter == null ? all : all.where((r) => r.rating == _filter).toList();
          final visible = filtered.take(_visible).toList();
          final remaining = filtered.length - visible.length;

          return ListView(
            controller: _scroll,
            padding: const EdgeInsets.fromLTRB(16, 12, 16, 24),
            children: [
              _SummaryHeader(average: avg, count: all.length),
              const SizedBox(height: 12),
              _FilterBar(active: _filter, total: all.length, counts: counts, onSelect: _setFilter),
              const SizedBox(height: 12),
              if (filtered.isEmpty)
                const Padding(
                  padding: EdgeInsets.only(top: 24),
                  child: EmptyView(message: 'Aucun avis pour cette note.', icon: Icons.filter_alt_off_outlined),
                )
              else ...[
                for (final r in visible) _ReviewCard(review: r),
                if (remaining > 0)
                  Padding(
                    padding: const EdgeInsets.only(top: 8),
                    child: Center(
                      child: TextButton(
                        onPressed: () => setState(() => _visible += _pageSize),
                        child: Text('Voir plus ($remaining)', style: const TextStyle(color: AppTheme.brandGreen, fontWeight: FontWeight.w700)),
                      ),
                    ),
                  ),
              ],
            ],
          );
        },
      ),
    );
  }
}

class _FilterBar extends StatelessWidget {
  const _FilterBar({required this.active, required this.total, required this.counts, required this.onSelect});
  final int? active;
  final int total;
  final Map<int, int> counts;
  final ValueChanged<int?> onSelect;

  @override
  Widget build(BuildContext context) {
    return SingleChildScrollView(
      scrollDirection: Axis.horizontal,
      child: Row(children: [
        _chip(label: 'Tous ($total)', selected: active == null, onTap: () => onSelect(null)),
        for (var r = 5; r >= 1; r--) ...[
          const SizedBox(width: 8),
          _chip(label: '$r★ (${counts[r] ?? 0})', selected: active == r, onTap: () => onSelect(r)),
        ],
      ]),
    );
  }

  Widget _chip({required String label, required bool selected, required VoidCallback onTap}) => GestureDetector(
        onTap: onTap,
        child: Container(
          padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 8),
          decoration: BoxDecoration(
            color: selected ? AppTheme.brandGreen : AppTheme.surface,
            borderRadius: BorderRadius.circular(20),
            border: Border.all(color: selected ? AppTheme.brandGreen : AppTheme.line),
          ),
          child: Text(label,
              style: TextStyle(
                  color: selected ? Colors.white : AppTheme.ink, fontWeight: FontWeight.w700, fontSize: 13)),
        ),
      );
}

class _SummaryHeader extends StatelessWidget {
  const _SummaryHeader({required this.average, required this.count});
  final double average;
  final int count;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.all(16),
      decoration: BoxDecoration(color: Colors.white, borderRadius: BorderRadius.circular(14), border: Border.all(color: AppTheme.line)),
      child: Row(children: [
        Text(average.toStringAsFixed(1), style: const TextStyle(fontSize: 32, fontWeight: FontWeight.w800)),
        const SizedBox(width: 12),
        Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
          _Stars(rating: average.round()),
          const SizedBox(height: 4),
          Text('$count avis', style: TextStyle(color: AppTheme.subtle)),
        ]),
      ]),
    );
  }
}

class _ReviewCard extends StatelessWidget {
  const _ReviewCard({required this.review});
  final Review review;

  @override
  Widget build(BuildContext context) {
    return Container(
      margin: const EdgeInsets.only(bottom: 12),
      padding: const EdgeInsets.all(14),
      decoration: BoxDecoration(color: Colors.white, borderRadius: BorderRadius.circular(14), border: Border.all(color: AppTheme.line)),
      child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
        Row(children: [
          CircleAvatar(
            radius: 16,
            backgroundColor: AppTheme.softGreen,
            child: Text(review.author.isNotEmpty ? review.author.characters.first.toUpperCase() : '?',
                style: const TextStyle(color: AppTheme.brandGreen, fontWeight: FontWeight.w800)),
          ),
          const SizedBox(width: 10),
          Expanded(child: Text(review.author, style: const TextStyle(fontWeight: FontWeight.w700))),
          _Stars(rating: review.rating),
        ]),
        if (review.title.isNotEmpty) ...[
          const SizedBox(height: 10),
          Text(review.title, style: const TextStyle(fontWeight: FontWeight.w700)),
        ],
        if (review.body.isNotEmpty) ...[
          const SizedBox(height: 4),
          Text(review.body, style: TextStyle(color: AppTheme.ink, height: 1.4)),
        ],
        if (review.reply != null && review.reply!.isNotEmpty) ...[
          const SizedBox(height: 12),
          Container(
            padding: const EdgeInsets.all(12),
            decoration: BoxDecoration(color: AppTheme.bg, borderRadius: BorderRadius.circular(10)),
            child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
              const Row(children: [
                Icon(Icons.storefront, size: 15, color: AppTheme.brandGreen),
                SizedBox(width: 6),
                Text('Réponse du vendeur', style: TextStyle(fontWeight: FontWeight.w800, fontSize: 12, color: AppTheme.brandGreen)),
              ]),
              const SizedBox(height: 4),
              Text(review.reply!, style: TextStyle(color: AppTheme.subtle, height: 1.4)),
            ]),
          ),
        ],
      ]),
    );
  }
}

class _Stars extends StatelessWidget {
  const _Stars({required this.rating});
  final int rating;

  @override
  Widget build(BuildContext context) {
    return Row(mainAxisSize: MainAxisSize.min, children: [
      for (var i = 1; i <= 5; i++)
        Icon(i <= rating ? Icons.star : Icons.star_border, size: 16, color: AppTheme.promoOrange),
    ]);
  }
}
