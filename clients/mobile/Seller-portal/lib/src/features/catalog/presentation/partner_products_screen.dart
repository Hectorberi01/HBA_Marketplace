import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../core/theme/app_theme.dart';
import '../../../shared/widgets/async_views.dart';
import '../../../shared/widgets/partner_widgets.dart';
import '../../activities/activities_data.dart';
import '../catalog_data.dart';

/// ═════════════════════════════════════════════════════════════════════════════
/// MES PRODUITS — `GET /api/catalog/sellers/{sellerId}/products`.
///
/// LE CATALOGUE EST CELUI DU VENDEUR, PAS DE LA BOUTIQUE AFFICHÉE.
///
/// La route est scopée par `sellerId` — le compte marchand — et `ProductSummary`
/// ne porte AUCUN identifiant de boutique. Un vendeur à deux boutiques voit donc
/// le même catalogue sous chacune. Le lien produit↔boutique existe côté serveur
/// (`Store`, tâche S6c, via les OFFRES), mais pas dans ce contrat.
/// POUR COMBLER : exposer `storeId` sur `ProductSummary`, ou une route
/// `/api/catalog/stores/{storeId}/products`.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// NI PRIX NI STOCK. TROIS BLOCS DE LA MAQUETTE DISPARAISSENT AVEC EUX.
///
/// `ProductSummary` porte l'identité du produit — nom, description, catégorie,
/// marque, attributs, tags, médias, déclinaisons — et RIEN d'autre.
///
///   • LE PRIX vit sur `OfferSummary`, dans le module Products/Offers encore
///     enfermé dans le monolithe (tâche « AUDIT2-1 »). Aucune route.
///   • LE STOCK vit dans inventory-service, PAR SKU, une déclinaison à la fois :
///     rien ne balaie le catalogue d'un vendeur. Et toutes les écritures sont
///     sous `MapAdminGroup` — un vendeur reçoit 403 (module
///     `sellerInventoryWrite`).
///
/// Conséquences, assumées :
///   • les filtres « Rupture » et « Stock faible » sont RETIRÉS. Les afficher
///     vides ferait chercher la condition qui les remplirait ; les afficher
///     avec un stock à zéro annoncerait une rupture générale.
///   • la carte n'affiche ni montant ni quantité. Un « 0 F CFA » sur un produit
///     à 24 500 est pire qu'une absence.
///   • le bouton « Stock » reste, GRISÉ : c'est la seule trace visible de ce
///     qu'il reste à ouvrir côté serveur.
/// ═════════════════════════════════════════════════════════════════════════════
class PartnerProductsScreen extends ConsumerStatefulWidget {
  const PartnerProductsScreen({super.key, required this.activity});

  final SellerActivity activity;

  @override
  ConsumerState<PartnerProductsScreen> createState() => _PartnerProductsScreenState();
}

/// Filtres réellement calculables : l'état de PUBLICATION, seul champ d'état que
/// `ProductSummary` porte (`draft` | `active` | `archived`).
enum _ProductFilter {
  all('Tous', null),
  active('Actifs', 'active'),
  draft('Brouillons', 'draft'),
  archived('Archivés', 'archived');

  const _ProductFilter(this.label, this._status);

  final String label;
  final String? _status;

  bool matches(SellerProduct p) => _status == null || p.status.toLowerCase() == _status;
}

class _PartnerProductsScreenState extends ConsumerState<PartnerProductsScreen> {
  _ProductFilter _filter = _ProductFilter.all;
  String _query = '';

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);
    final async = ref.watch(productsProvider);

    return Scaffold(
      backgroundColor: colors.bg,
      body: SafeArea(
        bottom: false,
        child: Column(
          children: [
            Padding(
              padding: const EdgeInsets.fromLTRB(16, 10, 16, 0),
              child: PartnerScreenHeader(
                title: 'Mes produits',
                activity: widget.activity,
                // Inerte : voir le bloc d'en-tête (module `sellerInventoryWrite`).
                trailing: const _GhostButton(label: 'Stock', onTap: null),
              ),
            ),
            const SizedBox(height: 14),

            Padding(
              padding: const EdgeInsets.symmetric(horizontal: 16),
              child: PartnerSearchField(
                hint: 'Rechercher un produit',
                onChanged: (v) => setState(() => _query = v),
              ),
            ),
            const SizedBox(height: 12),

            // Défilement horizontal : les puces ne tiennent pas sur la largeur
            // d'un téléphone, et les replier dans un menu déroulant coûterait un
            // tap de plus sur l'action la plus fréquente de l'écran.
            SizedBox(
              height: 36,
              child: ListView(
                scrollDirection: Axis.horizontal,
                padding: const EdgeInsets.symmetric(horizontal: 16),
                children: [
                  for (final f in _ProductFilter.values)
                    Padding(
                      padding: const EdgeInsets.only(right: 8),
                      child: PartnerFilterChip(
                        label: f.label,
                        selected: f == _filter,
                        onTap: () => setState(() => _filter = f),
                      ),
                    ),
                ],
              ),
            ),
            const SizedBox(height: 14),

            Expanded(
              child: async.when(
                loading: () => const LoadingView(),
                error: (e, _) => ErrorView(
                  message: e.toString(),
                  onRetry: () => ref.invalidate(productsProvider),
                ),
                data: (all) {
                  // La recherche est LOCALE : la route n'accepte aucun
                  // paramètre et rend tout le catalogue d'un bloc. Elle porte
                  // sur le nom et les SKU, seuls textes dont on dispose.
                  final needle = _query.trim().toLowerCase();
                  final visible = [
                    for (final p in all)
                      if (_filter.matches(p) &&
                          (needle.isEmpty ||
                              p.name.toLowerCase().contains(needle) ||
                              p.variants.any((v) => v.sku.toLowerCase().contains(needle))))
                        p,
                  ];

                  if (visible.isEmpty) {
                    return RefreshIndicator(
                      onRefresh: () async => ref.invalidate(productsProvider),
                      child: ListView(
                        children: [
                          PartnerEmptyState(
                            icon: Icons.inventory_2_outlined,
                            message: needle.isNotEmpty
                                ? 'Aucun produit ne correspond à « $_query ».'
                                : all.isEmpty
                                    ? 'Votre catalogue est vide.\n'
                                        'Ajoutez un premier produit pour commencer à vendre.'
                                    : 'Aucun produit dans « ${_filter.label} ».',
                          ),
                        ],
                      ),
                    );
                  }

                  return RefreshIndicator(
                    onRefresh: () async => ref.invalidate(productsProvider),
                    child: ListView.separated(
                      // 96 px de marge basse : le bouton flottant recouvrirait
                      // sinon la dernière carte, dont les actions deviendraient
                      // inatteignables.
                      padding: const EdgeInsets.fromLTRB(16, 0, 16, 96),
                      itemCount: visible.length,
                      separatorBuilder: (_, __) => const SizedBox(height: 12),
                      itemBuilder: (_, i) => _ProductCard(product: visible[i]),
                    ),
                  );
                },
              ),
            ),
          ],
        ),
      ),
      floatingActionButton: FloatingActionButton.extended(
        onPressed: () => context.push('/product/new'),
        backgroundColor: AppTheme.brandGreen,
        foregroundColor: Colors.white,
        icon: const Icon(Icons.add, size: 20),
        label: const Text(
          'Ajouter un produit',
          style: TextStyle(fontSize: 14.5, fontWeight: FontWeight.w700),
        ),
      ),
    );
  }
}

class _ProductCard extends StatelessWidget {
  const _ProductCard({required this.product});

  final SellerProduct product;

  /// Couleur et fond de la pastille de PUBLICATION.
  static (Color, Color) _tone(String status) => switch (status.toLowerCase()) {
        'active' => (AppTheme.brandGreen, AppTheme.brandGreenSoft),
        'archived' => (AppTheme.danger, const Color(0xFFFDECEC)),
        // `draft` et tout état futur : neutre plutôt qu'une couleur choisie au
        // hasard pour une valeur qu'on ne connaît pas.
        _ => (AppTheme.slate, const Color(0xFFEDEFF1)),
      };

  static String _label(String status) => switch (status.toLowerCase()) {
        'active' => 'Actif',
        'draft' => 'Brouillon',
        'archived' => 'Archivé',
        // Statut inconnu : le code brut. Le jour où catalog-service en ajoute
        // un, le vendeur le voit, et nous aussi.
        _ => status,
      };

  /// Initiales DÉRIVÉES du nom, faute de photo. Rien n'est stocké quelque part
  /// qui les porterait.
  String get _initials {
    final words =
        product.name.trim().split(RegExp(r'\s+')).where((w) => w.isNotEmpty).toList();
    if (words.isEmpty) return '?';
    if (words.length == 1) {
      return words.first.substring(0, words.first.length >= 2 ? 2 : 1).toUpperCase();
    }
    return '${words[0][0]}${words[1][0]}'.toUpperCase();
  }

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);
    final (tint, background) = _tone(product.status);
    final image = product.imageUrl;

    return PartnerCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              // La photo principale quand elle existe — c'est celle que
              // l'acheteur verra. À défaut, les initiales : un produit sans
              // photo se remarque, et c'est utile.
              ClipRRect(
                borderRadius: BorderRadius.circular(11),
                child: SizedBox(
                  width: 52,
                  height: 52,
                  child: image == null
                      ? Container(
                          color: colors.bg,
                          alignment: Alignment.center,
                          child: Text(
                            _initials,
                            style: TextStyle(
                              fontSize: 13,
                              fontWeight: FontWeight.w800,
                              color: colors.subtle,
                            ),
                          ),
                        )
                      : Image.network(
                          image,
                          fit: BoxFit.cover,
                          // Une URL signée peut avoir expiré : on retombe sur la
                          // vignette neutre plutôt que sur l'icône d'image
                          // cassée de Flutter.
                          errorBuilder: (_, __, ___) => Container(
                            color: colors.bg,
                            alignment: Alignment.center,
                            child: Icon(Icons.image_not_supported_outlined,
                                size: 20, color: colors.subtle),
                          ),
                        ),
                ),
              ),
              const SizedBox(width: 12),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      product.name,
                      maxLines: 2,
                      overflow: TextOverflow.ellipsis,
                      style: TextStyle(
                        fontSize: 14.5,
                        fontWeight: FontWeight.w700,
                        color: colors.ink,
                      ),
                    ),
                    const SizedBox(height: 6),
                    Row(
                      children: [
                        PartnerStatusDot(
                          label: _label(product.status),
                          color: tint,
                          background: background,
                        ),
                        const SizedBox(width: 8),

                        // Le nombre de déclinaisons est RÉEL et utile : c'est
                        // l'unité qui se vend, et ce que le stock suit — quand
                        // il sera lisible.
                        if (product.variants.isNotEmpty)
                          Expanded(
                            child: Text(
                              product.variants.length > 1
                                  ? '${product.variants.length} déclinaisons'
                                  : product.variants.first.sku,
                              maxLines: 1,
                              overflow: TextOverflow.ellipsis,
                              style: TextStyle(fontSize: 12.5, color: colors.subtle),
                            ),
                          ),
                      ],
                    ),
                  ],
                ),
              ),
            ],
          ),
          const SizedBox(height: 12),

          Row(
            children: [
              Expanded(
                child: _GhostButton(
                  label: 'Modifier',
                  onTap: () => context.push('/product/${product.id}'),
                ),
              ),
              const SizedBox(width: 8),
              // Inerte : les écritures de stock sont réservées à
              // l'administration dans inventory-service (module
              // `sellerInventoryWrite`).
              //const Expanded(child: _GhostButton(label: 'Stock', onTap: null)),
              const SizedBox(width: 8),
              _GhostButton(
                icon: Icons.visibility_outlined,
                onTap: () => context.push('/product/${product.id}/preview'),
              ),
            ],
          ),
        ],
      ),
    );
  }
}

/// Bouton à contour fin. `onTap: null` le grise — un bouton visible et inerte
/// dit ce qui manque ; un bouton absent laisse croire que l'écran est fini.
class _GhostButton extends StatelessWidget {
  const _GhostButton({this.label, this.icon, required this.onTap});

  final String? label;
  final IconData? icon;
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);
    final enabled = onTap != null;
    final tint = enabled ? colors.ink : colors.subtle;

    return InkWell(
      onTap: onTap,
      borderRadius: BorderRadius.circular(10),
      child: Container(
        height: 40,
        alignment: Alignment.center,
        padding: EdgeInsets.symmetric(horizontal: icon != null ? 12 : 14),
        decoration: BoxDecoration(
          color: colors.surface,
          borderRadius: BorderRadius.circular(10),
          border: Border.all(color: colors.line),
        ),
        child: icon != null
            ? Icon(icon, size: 19, color: tint)
            : Text(
                label!,
                style: TextStyle(fontSize: 13.5, fontWeight: FontWeight.w700, color: tint),
              ),
      ),
    );
  }
}
