import 'package:cached_network_image/cached_network_image.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:hba_express_pro/l10n/app_localizations.dart';

import '../../../core/theme/app_theme.dart';
import '../../../shared/utils/formatters.dart';
import '../../../shared/widgets/async_views.dart';
import '../../../shared/widgets/ui_kit.dart';
import '../../inventory/inventory_data.dart';
import '../../offers/offers_data.dart';
import '../catalog_data.dart';

/// Aperçu client : la fiche telle que l'acheteur la verra.
///
/// C'est un miroir, pas un écran de gestion. Il montre EXACTEMENT ce qui est
/// publié — prix payé par l'acheteur (et non le prix net du vendeur), photo
/// principale, état annoncé, disponibilité. Les erreurs de mise en vente (aucune
/// mise en vente active, produit en brouillon, stock à zéro) sautent alors aux yeux
/// avant que le vendeur ne s'étonne de ne rien vendre.
class ProductPreviewScreen extends ConsumerWidget {
  const ProductPreviewScreen({super.key, required this.productId});
  final String productId;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final colors = AppColors.of(context);
    final l = AppLocalizations.of(context);
    final product = ref.watch(productProvider(productId));
    final offers = ref.watch(offersProvider);

    return Scaffold(
      appBar: AppBar(
        title: Text(l.ppvTitle),
      ),
      body: product.when(
        loading: () => const LoadingView(),
        error: (e, _) => ErrorView(
          message: e.toString(),
          onRetry: () => ref.invalidate(productProvider(productId)),
        ),
        data: (p) {
          final all = offers.valueOrNull ?? const <Offer>[];

          // Seule une mise en vente ACTIVE rend le produit achetable. On prend la moins
          // chère : c'est celle que la marketplace met en avant.
          final active = all
              .where((o) => o.productId == p.id && o.status.toLowerCase() == 'active')
              .toList()
            ..sort((a, b) => a.productPrice.compareTo(b.productPrice));

          final offer = active.isEmpty ? null : active.first;
          final visible = p.isActive && offer != null;

          return ListView(
            padding: EdgeInsets.zero,
            children: [
              _Banner(visible: visible, productActive: p.isActive, hasActiveOffer: offer != null),
              _Photos(product: p),
              Padding(
                padding: const EdgeInsets.fromLTRB(20, 18, 20, 28),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      p.name,
                      style: TextStyle(fontSize: 21, fontWeight: FontWeight.w800, color: colors.ink),
                    ),
                    const SizedBox(height: 10),

                    // Le prix affiché est celui que l'ACHETEUR paie — jamais le
                    // net vendeur. Les confondre ici viderait l'aperçu de son sens.
                    if (offer != null)
                      Row(
                        crossAxisAlignment: CrossAxisAlignment.center,
                        children: [
                          Text(
                            Format.money(offer.productPrice, offer.currency),
                            style: const TextStyle(
                              fontSize: 26,
                              fontWeight: FontWeight.w800,
                              color: AppTheme.brandGreen,
                            ),
                          ),
                          const SizedBox(width: 10),
                          StatusBadge(
                            label: conditionLabel(l, offer.condition),
                            color: offer.condition.toLowerCase() == 'new'
                                ? AppTheme.info
                                : AppTheme.promoOrange,
                          ),
                        ],
                      )
                    else
                      Text(
                        l.ppvPriceUnavailable,
                        style: TextStyle(fontSize: 20, fontWeight: FontWeight.w800, color: colors.subtle),
                      ),

                    if (offer != null) ...[
                      const SizedBox(height: 6),
                      _Availability(sku: offer.sku, handlingTime: offer.handlingTime),
                    ],

                    const SizedBox(height: 20),
                    Text(l.commonDescription,
                        style: TextStyle(fontSize: 15, fontWeight: FontWeight.w800, color: colors.ink)),
                    const SizedBox(height: 6),
                    Text(
                      p.description.isEmpty
                          ? l.ppvNoDescription
                          : p.description,
                      style: TextStyle(
                        fontSize: 14,
                        height: 1.55,
                        color: p.description.isEmpty ? colors.subtle : colors.ink,
                        fontStyle: p.description.isEmpty ? FontStyle.italic : FontStyle.normal,
                      ),
                    ),

                    const SizedBox(height: 24),
                    // Bouton inerte : c'est un aperçu, pas la boutique. Le rendre
                    // cliquable laisserait croire qu'on peut acheter son propre produit.
                    FilledButton(
                      onPressed: null,
                      style: FilledButton.styleFrom(
                        minimumSize: const Size.fromHeight(52),
                        disabledBackgroundColor:
                            visible ? AppTheme.brandGreen.withValues(alpha: 0.5) : colors.line,
                      ),
                      child: Text(visible ? l.ppvAddToCart : l.ppvUnavailable),
                    ),
                    const SizedBox(height: 8),
                    Center(
                      child: Text(
                        l.ppvPreviewNote,
                        style: TextStyle(fontSize: 11, color: colors.subtle),
                      ),
                    ),
                  ],
                ),
              ),
            ],
          );
        },
      ),
    );
  }
}

/// Bandeau de vérité : le produit est-il réellement achetable, oui ou non ?
class _Banner extends StatelessWidget {
  const _Banner({required this.visible, required this.productActive, required this.hasActiveOffer});

  final bool visible;
  final bool productActive;
  final bool hasActiveOffer;

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);
    final l = AppLocalizations.of(context);
    if (visible) {
      return Container(
        width: double.infinity,
        color: colors.softGreen,
        padding: const EdgeInsets.symmetric(horizontal: 20, vertical: 10),
        child: Row(
          children: [
            const Icon(Icons.check_circle, size: 16, color: AppTheme.brandGreen),
            const SizedBox(width: 8),
            Expanded(
              child: Text(l.ppvBuyable,
                  style: const TextStyle(fontSize: 12, color: AppTheme.brandGreenDark, fontWeight: FontWeight.w600)),
            ),
          ],
        ),
      );
    }

    // On nomme la cause exacte : « indisponible » sans motif oblige le vendeur à
    // deviner, et c'est précisément là qu'il abandonne.
    final reason = !productActive && !hasActiveOffer
        ? l.ppvReasonBoth
        : !productActive
            ? l.ppvReasonDraft
            : l.ppvReasonNoOffer;

    return Container(
      width: double.infinity,
      color: AppTheme.promoOrange.withValues(alpha: 0.12),
      padding: const EdgeInsets.symmetric(horizontal: 20, vertical: 10),
      child: Row(
        children: [
          const Icon(Icons.warning_amber_rounded, size: 16, color: AppTheme.promoOrange),
          const SizedBox(width: 8),
          Expanded(
            child: Text(reason,
                style: TextStyle(fontSize: 12, color: colors.ink, fontWeight: FontWeight.w600)),
          ),
        ],
      ),
    );
  }
}

class _Photos extends StatelessWidget {
  const _Photos({required this.product});
  final SellerProduct product;

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);
    final l = AppLocalizations.of(context);
    if (product.media.isEmpty) {
      return Container(
        height: 260,
        color: colors.bg,
        alignment: Alignment.center,
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Icon(Icons.image_not_supported_outlined, size: 36, color: colors.subtle),
            const SizedBox(height: 8),
            Text(l.ppvNoPhoto,
                style: TextStyle(fontSize: 12, color: colors.subtle)),
          ],
        ),
      );
    }

    return SizedBox(
      height: 260,
      child: PageView.builder(
        itemCount: product.media.length,
        itemBuilder: (_, i) => Padding(
          padding: const EdgeInsets.all(12),
          child: CachedNetworkImage(imageUrl: product.media[i].url, fit: BoxFit.contain),
        ),
      ),
    );
  }
}

/// Disponibilité réelle : c'est le stock qui décide, pas l'intention du vendeur.
class _Availability extends ConsumerWidget {
  const _Availability({required this.sku, required this.handlingTime});

  final String sku;
  final int handlingTime;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final colors = AppColors.of(context);
    final l = AppLocalizations.of(context);
    final stock = ref.watch(inventoryBySkuProvider(sku));

    return stock.when(
      loading: () => const SizedBox(height: 18),
      error: (_, __) => const SizedBox(height: 18),
      data: (items) {
        final available = items.fold<int>(0, (sum, i) => sum + i.available);
        final inStock = available > 0;

        return Row(
          children: [
            Icon(
              inStock ? Icons.check_circle_outline : Icons.remove_circle_outline,
              size: 15,
              color: inStock ? AppTheme.brandGreen : AppTheme.danger,
            ),
            const SizedBox(width: 6),
            Text(
              inStock ? l.ppvInStock : l.ppvOutOfStock,
              style: TextStyle(
                fontSize: 12.5,
                fontWeight: FontWeight.w700,
                color: inStock ? AppTheme.brandGreen : AppTheme.danger,
              ),
            ),
            if (inStock && handlingTime > 0) ...[
              Text(' · ', style: TextStyle(color: colors.subtle)),
              Text(l.ppvShippedIn(handlingTime),
                  style: TextStyle(fontSize: 12.5, color: colors.subtle)),
            ],
          ],
        );
      },
    );
  }
}
