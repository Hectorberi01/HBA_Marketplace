import 'package:cached_network_image/cached_network_image.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/theme/app_theme.dart';
import '../../features/account/account_data.dart';
import '../../core/security/require_auth.dart';
import '../utils/formatters.dart';
import 'app_notify.dart';

/// Vignette produit réutilisable (grille / carrousel), calquée sur les maquettes :
/// image + cœur favori, badge promo, note+avis, prix (barré optionnel), bouton AJOUTER.
class ProductCardTile extends ConsumerWidget {
  const ProductCardTile({
    super.key,
    required this.id,
    required this.name,
    required this.url,
    required this.price,
    required this.currency,
    this.rating = 0,
    this.reviewCount = 0,
    this.originalPrice,
    this.promoLabel,
    this.inStock = true,
    this.showAddButton = false,
    this.onAdd,
    this.width,
  });

  final String id;
  final String name;
  final String? url;
  final double price;
  final String currency;
  final double rating;
  final int reviewCount;
  final double? originalPrice;
  final String? promoLabel;
  final bool inStock;
  final bool showAddButton;
  final VoidCallback? onAdd;
  final double? width;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final isFav = ref.watch(favoriteIdsProvider).contains(id);
    return GestureDetector(
      onTap: () => context.push('/product/$id'),
      child: Container(
        width: width,
        decoration: BoxDecoration(
          color: AppTheme.surface,
          borderRadius: BorderRadius.circular(16),
          border: Border.all(color: AppTheme.line),
        ),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            // ─────────────────────────────────────────────────────────────────
            // L'IMAGE PREND LA HAUTEUR RESTANTE — elle ne l'impose pas.
            //
            // AVANT : `AspectRatio(aspectRatio: 1)`. L'image réclamait une hauteur
            // égale à sa largeur, et le texte s'ajoutait par-dessus. La carte
            // dépassait donc dès que la somme franchissait la hauteur allouée par
            // la grille — d'où le « BOTTOM OVERFLOWED BY 3.6 PIXELS ».
            //
            // Le débordement dépendait du contenu (titre sur 1 ou 2 lignes, présence
            // d'une note, prix barré) ET de la taille de police du système : régler
            // le `childAspectRatio` de chaque grille au cas par cas ne fait que
            // déplacer le seuil, sans jamais le supprimer.
            //
            // Ici, le texte est mesuré d'abord et l'image absorbe ce qui reste.
            // La carte s'adapte à n'importe quel ratio de grille et à n'importe
            // quel réglage d'accessibilité, sans jamais déborder.
            // ─────────────────────────────────────────────────────────────────
            Expanded(
              child: Stack(
                fit: StackFit.expand,
                children: [
                  ClipRRect(
                    borderRadius: const BorderRadius.vertical(top: Radius.circular(16)),
                    child: ProductImage(url: url),
                  ),
                  if (!inStock)
                  Positioned.fill(
                    child: ClipRRect(
                      borderRadius: const BorderRadius.vertical(top: Radius.circular(16)),
                      child: Container(
                        color: Colors.white.withValues(alpha: 0.55),
                        alignment: Alignment.center,
                        child: Container(
                          padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 5),
                          decoration: BoxDecoration(color: Colors.white, borderRadius: BorderRadius.circular(8)),
                          child: Text('RUPTURE',
                              style: TextStyle(fontSize: 11, fontWeight: FontWeight.w800, color: AppTheme.ink)),
                        ),
                      ),
                    ),
                  ),
                if (promoLabel != null)
                  Positioned(
                    left: 8,
                    top: 8,
                    child: Container(
                      padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 3),
                      decoration: BoxDecoration(color: AppTheme.promoOrange, borderRadius: BorderRadius.circular(6)),
                      child: Text(promoLabel!,
                          style: const TextStyle(color: Colors.white, fontSize: 10, fontWeight: FontWeight.w800)),
                    ),
                  ),
                Positioned(
                  right: 8,
                  top: 8,
                  child: _HeartButton(
                    active: isFav,
                    onTap: () async {
                      // Écran public : les favoris sont rattachés au compte.
                      if (!requireAuth(context, ref, action: 'enregistrer un favori')) return;
                      try {
                        await ref.read(wishlistControllerProvider.notifier).toggle(id);
                      } catch (e) {
                        if (context.mounted) {
                          AppNotify.error(context, 'Impossible de mettre à jour les favoris : $e');
                        }
                      }
                    },
                  ),
                ),
                ],
              ),
            ),
            Padding(
              padding: const EdgeInsets.fromLTRB(10, 10, 10, 8),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(name,
                      maxLines: 2,
                      overflow: TextOverflow.ellipsis,
                      style: TextStyle(
                          fontWeight: FontWeight.w600,
                          height: 1.2,
                          color: inStock ? AppTheme.ink : AppTheme.subtle)),
                  if (rating > 0) ...[
                    const SizedBox(height: 6),
                    Row(children: [
                      const Icon(Icons.star, size: 14, color: AppTheme.star),
                      const SizedBox(width: 3),
                      Text(rating.toStringAsFixed(1), style: const TextStyle(fontSize: 12, fontWeight: FontWeight.w600)),
                      if (reviewCount > 0)
                        Text('  ($reviewCount)', style: TextStyle(fontSize: 12, color: AppTheme.subtle)),
                    ]),
                  ],
                  const SizedBox(height: 6),
                  Row(crossAxisAlignment: CrossAxisAlignment.end, children: [
                    Flexible(
                      child: Text(Format.money(price, currency),
                          maxLines: 1,
                          overflow: TextOverflow.ellipsis,
                          style: const TextStyle(color: AppTheme.brandGreen, fontWeight: FontWeight.w800, fontSize: 15)),
                    ),
                    if (originalPrice != null && originalPrice! > price) ...[
                      const SizedBox(width: 6),
                      Text(Format.money(originalPrice, currency),
                          style: TextStyle(
                              color: AppTheme.subtle,
                              fontSize: 12,
                              decoration: TextDecoration.lineThrough)),
                    ],
                  ]),
                  if (showAddButton) ...[
                    const SizedBox(height: 8),
                    SizedBox(
                      width: double.infinity,
                      child: _AddButton(enabled: inStock, onTap: onAdd),
                    ),
                  ],
                ],
              ),
            ),
          ],
        ),
      ),
    );
  }
}

class _HeartButton extends StatelessWidget {
  const _HeartButton({required this.active, required this.onTap});
  final bool active;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    return Semantics(
      button: true,
      label: active ? 'Retirer des favoris' : 'Ajouter aux favoris',
      child: Material(
        color: AppTheme.surface,
        shape: const CircleBorder(),
        elevation: 1,
        shadowColor: Colors.black26,
        child: InkWell(
          customBorder: const CircleBorder(),
          onTap: onTap,
          child: Padding(
            padding: const EdgeInsets.all(6),
            child: Icon(active ? Icons.favorite : Icons.favorite_border,
                size: 18, color: active ? AppTheme.brandGreen : AppTheme.subtle),
          ),
        ),
      ),
    );
  }
}

class _AddButton extends StatelessWidget {
  const _AddButton({required this.enabled, required this.onTap});
  final bool enabled;
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) {
    return TextButton.icon(
      onPressed: enabled ? onTap : null,
      style: TextButton.styleFrom(
        backgroundColor: enabled ? AppTheme.softGreen : AppTheme.bg,
        foregroundColor: enabled ? AppTheme.brandGreen : AppTheme.subtle,
        padding: const EdgeInsets.symmetric(vertical: 8),
        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(10)),
      ),
      icon: Icon(enabled ? Icons.add_shopping_cart : Icons.block, size: 16),
      label: Text(enabled ? 'AJOUTER' : 'INDISPONIBLE',
          style: const TextStyle(fontSize: 12, fontWeight: FontWeight.w700)),
    );
  }
}

/// Image produit avec placeholder/erreur.
class ProductImage extends StatelessWidget {
  const ProductImage({super.key, required this.url, this.fit = BoxFit.cover});
  final String? url;
  final BoxFit fit;

  @override
  Widget build(BuildContext context) {
    final bg = AppTheme.bg;
    if (url == null || url!.isEmpty) {
      return Container(color: bg, child: Icon(Icons.image_outlined, color: AppTheme.subtle));
    }
    return CachedNetworkImage(
      imageUrl: url!,
      fit: fit,
      placeholder: (_, __) => Container(color: bg),
      errorWidget: (_, __, ___) =>
          Container(color: bg, child: Icon(Icons.broken_image_outlined, color: AppTheme.subtle)),
    );
  }
}
