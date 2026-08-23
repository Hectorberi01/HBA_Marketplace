import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../core/theme/app_theme.dart';
import '../../../shared/widgets/async_views.dart';
import '../../../shared/widgets/partner_widgets.dart';
import '../offers_data.dart';
import 'offer_card.dart';
import 'offer_sheets.dart';

/// ═════════════════════════════════════════════════════════════════════════════
/// MISES EN VENTE — `/api/catalog/seller/stores/{storeId}/offers`.
///
/// CET ÉCRAN ÉTAIT « SANS AMONT », ET IL NE L'EST PLUS.
///
/// Le diagnostic d'origine était juste sur les faits — aucune route `/api/offers`
/// nulle part — et faux sur la conclusion : il attendait l'extraction d'un
/// `products-service` qui n'arrivera jamais. Les offres ont été GREFFÉES dans
/// catalog-service (phase 3), parce que `Product`, `Variant` et `ProductOffer`
/// forment un seul invariant : l'offre porte une déclinaison, qui porte le SKU,
/// qui porte le stock.
///
/// CE QUI SE JOUE ICI RESTE DE L'ARGENT, ET LA CARTE LE DIT.
///
/// Une offre porte DEUX prix — le net encaissé par le vendeur et le prix payé par
/// l'acheteur, séparés par la commission et les frais prestataire. Les deux
/// viennent maintenant du serveur (`sellerPrice`, `effectivePrice`), et ne sont
/// plus reconstitués à partir d'un multiplicateur local : c'est la différence
/// entre afficher un revenu et l'estimer.
///
/// LA LISTE PEUT COUVRIR PLUSIEURS BOUTIQUES.
///
/// `offersProvider` interroge chaque boutique du vendeur quand aucune activité
/// n'est sélectionnée — voir l'encadré qui l'accompagne. Le nom du produit est
/// donc affiché sur chaque carte : sans lui, deux mises en vente du même article
/// dans deux boutiques différentes seraient indiscernables.
/// ═════════════════════════════════════════════════════════════════════════════
class OffersScreen extends ConsumerWidget {
  const OffersScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final colors = AppColors.of(context);
    final async = ref.watch(offersProvider);

    return Scaffold(
      backgroundColor: colors.bg,
      appBar: AppBar(title: const Text('Mes mises en vente')),

      // LE BOUTON RESTE OFFERT MÊME QUAND LA LISTE EST VIDE OU EN ERREUR.
      //
      // Le premier geste d'un vendeur qui arrive ici est précisément de créer sa
      // première mise en vente ; le cacher derrière un chargement réussi ferait
      // d'un écran vide une impasse. La feuille sait elle-même dire ce qui
      // manque — un produit, une déclinaison, une boutique, un lieu d'expédition.
      floatingActionButton: FloatingActionButton.extended(
        onPressed: () => OfferSheets.create(context),
        icon: const Icon(Icons.sell_outlined),
        label: const Text('Mettre en vente'),
      ),

      body: async.when(
        loading: () => const LoadingView(),
        error: (e, _) => ErrorView(
          message: e.toString(),
          onRetry: () => ref.invalidate(offersProvider),
        ),
        data: (offres) => RefreshIndicator(
          onRefresh: () async => ref.invalidate(offersProvider),
          child: offres.isEmpty
              // `ListView` ET NON `Center` : sans liste défilante, le geste de
              // rafraîchissement n'existe pas sur un écran vide, et le vendeur
              // n'a aucun moyen de retenter après avoir créé un produit.
              ? ListView(
                  children: const [
                    SizedBox(height: 80),
                    PartnerEmptyState(
                      icon: Icons.sell_outlined,
                      message:
                          'Aucune mise en vente. Un produit en ligne sans mise en '
                          'vente active n\'est pas achetable : c\'est ici que le '
                          'prix se fixe.',
                    ),
                  ],
                )
              : ListView.separated(
                  padding: const EdgeInsets.fromLTRB(16, 16, 16, 96),
                  itemCount: offres.length,
                  separatorBuilder: (_, __) => const SizedBox(height: 10),
                  itemBuilder: (_, i) => OfferCard(offer: offres[i]),
                ),
        ),
      ),
    );
  }
}
