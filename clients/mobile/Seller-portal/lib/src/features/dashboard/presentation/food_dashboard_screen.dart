import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';

import 'package:flutter_riverpod/flutter_riverpod.dart';
import '../../../core/theme/app_theme.dart';
import '../../../shared/utils/formatters.dart';
import '../../../shared/widgets/async_views.dart';
import '../../../shared/widgets/partner_widgets.dart';
import '../../activities/presentation/activity_switcher_sheet.dart';
import '../../activities/selected_activity.dart';
import '../../menu/menu_data.dart';
import '../dashboard_data.dart';

/// ═════════════════════════════════════════════════════════════════════════════
/// TABLEAU DE BORD D'UN RESTAURANT HBA FOOD —
/// `GET /api/v1/bff/restaurant/restaurants/{id}/dashboard`.
///
/// IL EXIGE LE RÔLE `FoodPartner`, PAS `Seller`.
///
/// Un partenaire qui n'a que l'une des deux casquettes reçoit 403 sur l'autre.
/// Ce n'est pas une panne : c'est un métier qu'il n'exerce pas. `ErrorView`
/// affiche donc le message du serveur tel quel, sans le qualifier d'incident.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// CE QUE LA MAQUETTE MONTRAIT ET QUE `RestaurantDashboardDto` NE PORTE PAS.
///
/// Quatre blocs ont été retirés, et il faut le lire une fois pour toutes :
///
///   • LES QUATRE INDICATEURS — « Commandes aujourd'hui », « CA », « Temps moyen
///     de préparation », « Commandes en cuisine ». Le contrat rend `restaurant`,
///     `service`, `wallet` et `kitchen`, et RIEN d'autre. Il n'existe ni chiffre
///     d'affaires du jour côté Food (le BFF merchant en a un, mais pour une
///     BOUTIQUE), ni compteur de commandes du jour, ni temps de préparation
///     moyen — cette dernière valeur n'est calculée nulle part sur la
///     plateforme. Seuls les trois compteurs de cuisine sont réels, et ils ont
///     leur propre carte plus bas.
///   • « PERFORMANCE · 7 JOURS » — aucune série temporelle n'existe (module
///     `analytics`, cf. `core/network/not_migrated.dart`). Les sept barres et le
///     « +9 % » étaient écrits dans le fichier de maquette.
///   • LE BOUTON « SIMULER » et sa feuille de commande entrante — un outil de
///     démonstration qui fabriquait une commande, ses trois lignes et son total.
///     Il n'a plus d'objet : les commandes réellement reçues sont dans le seau
///     `pending` du tableau de cuisine.
///   • « FERMER TEMPORAIREMENT » — `PauseRestaurantCommand` et
///     `ResumeRestaurantCommand` existent dans food-service et n'ont AUCUNE
///     route (module `foodServiceToggle`). Le bouton est conservé mais INERTE et
///     grisé : le geste est trop central dans le métier pour disparaître sans
///     laisser de trace de ce qui manque.
/// ═════════════════════════════════════════════════════════════════════════════
class FoodDashboardScreen extends ConsumerWidget {
  const FoodDashboardScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final colors = AppColors.of(context);
    // `!` EST SÛR ICI, ET UNIQUEMENT ICI.
    //
    // Cet écran n'est construit que par `PartnerHomeScreen`, dont la branche
    // correspondante ne se déclenche que si l'activité est non nulle.
    final restaurant = ref.watch(selectedActivityProvider)!;
    final async = ref.watch(restaurantDashboardProvider(restaurant.id));

    return Scaffold(
      backgroundColor: colors.bg,
      body: SafeArea(
        bottom: false,
        child: RefreshIndicator(
          onRefresh: () async => ref.invalidate(restaurantDashboardProvider(restaurant.id)),
          child: async.when(
            loading: () => const LoadingView(),
            error: (e, _) => ErrorView(
              message: e.toString(),
              onRetry: () => ref.invalidate(restaurantDashboardProvider(restaurant.id)),
            ),
            data: (result) {
              final d = result.data;

              return ListView(
                padding: const EdgeInsets.fromLTRB(16, 8, 16, 24),
                children: [
                  PartnerContextHeader(
                    activity: restaurant,
                    onSwitchActivity: ActivitySwitcherSheet.show,
                  ),
                  const SizedBox(height: 14),

                  _ServiceRow(
                    acceptsOrdersNow: d.acceptsOrdersNow,
                    blockedReason: d.blockedReason,
                  ),
                  const SizedBox(height: 16),

                  if (result.isPartial)
                    for (final w in result.warnings) _PartialBanner(message: w.message),

                  // « À traiter » passe AVANT tout le reste, et ce n'est pas un
                  // détail de mise en page. Côté boutique, un colis non préparé
                  // attend quelques heures sans conséquence ; côté restaurant,
                  // une commande non acceptée laisse un client devant son
                  // téléphone pendant que le plat refroidit.
                  //
                  // La carte disparaît quand la file est vide : un bloc
                  // d'urgence permanent cesse d'être un signal.
                  if (d.kitchenPending > 0) ...[
                    _PendingAcceptanceCard(count: d.kitchenPending),
                    const SizedBox(height: 16),
                  ],

                  const PartnerSectionTitle('Cuisine'),
                  const SizedBox(height: 10),
                  _KitchenCard(
                    preparing: d.kitchenPreparing,
                    ready: d.kitchenReady,
                    // L'écran de cuisine existe désormais (`/kitchen`). Il ne
                    // porte QUE les trois seaux du ticket — à préparer, en
                    // cours, prêt. Accepter et refuser n'y figurent pas : le
                    // ticket n'est créé qu'après acceptation, et la liste des
                    // commandes en attente de décision n'est exposée par aucune
                    // route (voir l'en-tête de `KitchenScreen`).
                    onOpen: () => context.push('/kitchen', extra: restaurant.id),
                  ),
                  const SizedBox(height: 24),

                  // Les plats indisponibles se COMPTENT sur la carte : le
                  // tableau de bord ne les porte pas. C'est un second appel, et
                  // il n'est émis que si le membre a le droit de lire la carte —
                  // un cuisinier recevrait 403.
                  if (d.restaurant.can(FoodPermission.menuManage)) ...[
                    const PartnerSectionTitle('Disponibilité'),
                    const SizedBox(height: 10),
                    _UnavailableDishesRow(restaurantId: restaurant.id),
                    const SizedBox(height: 24),
                  ],

                  // `wallet` EST `null` DANS DEUX CAS INDISCERNABLES, ET
                  // AUCUN NE PRODUIT D'AVERTISSEMENT : soit le membre n'a pas le
                  // droit de lire les finances (ou aucun vendeur de reversement
                  // n'est rattaché), soit financial-service est injoignable. La
                  // carte est donc masquée sans message — on ne sait pas
                  // laquelle des deux raisons annoncer.
                  if (d.wallet != null) ...[
                    const PartnerSectionTitle('Solde'),
                    const SizedBox(height: 10),
                    _WalletCard(wallet: d.wallet!),
                  ],
                ],
              );
            },
          ),
        ),
      ),
    );
  }
}

/// Bandeau d'un rendu partiel du BFF.
class _PartialBanner extends StatelessWidget {
  const _PartialBanner({required this.message});

  final String message;

  @override
  Widget build(BuildContext context) => Container(
        margin: const EdgeInsets.only(bottom: 12),
        padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 10),
        decoration: BoxDecoration(
          color: AppTheme.foodAmberSoft,
          borderRadius: BorderRadius.circular(12),
        ),
        child: Row(
          children: [
            const Icon(Icons.cloud_off_outlined, size: 18, color: AppTheme.foodAmber),
            const SizedBox(width: 10),
            Expanded(
              child: Text(message,
                  style: const TextStyle(fontSize: 12.5, color: AppTheme.foodAmber)),
            ),
          ],
        ),
      );
}

/// État du service, et le bouton qui ne peut pas encore le changer.
class _ServiceRow extends StatelessWidget {
  const _ServiceRow({required this.acceptsOrdersNow, required this.blockedReason});

  final bool acceptsOrdersNow;

  /// Chaîne VIDE quand rien ne bloque — non nullable dans le contrat.
  final String blockedReason;

  /// Le motif, en français, pour un restaurateur.
  ///
  /// UN MOTIF INCONNU N'EST PAS TRADUIT : on rend la chaîne brute plutôt
  /// qu'un libellé approximatif. Le jour où food-service en ajoute un, le
  /// restaurateur le voit — et nous aussi.
  String? get _reason => switch (blockedReason) {
        '' => null,
        'NotInService' => 'Établissement non validé',
        'Closed' => 'Hors horaires d\'ouverture',
        'Paused' => 'Réception mise en pause',
        'NothingAvailable' => 'Aucun plat disponible',
        _ => blockedReason,
      };

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);
    final color = acceptsOrdersNow ? AppTheme.brandGreen : AppTheme.slate;
    final reason = _reason;

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Row(
          children: [
            Container(
              padding: const EdgeInsets.symmetric(horizontal: 11, vertical: 7),
              decoration: BoxDecoration(
                color: acceptsOrdersNow ? AppTheme.brandGreenSoft : colors.bg,
                borderRadius: BorderRadius.circular(20),
              ),
              child: Row(
                mainAxisSize: MainAxisSize.min,
                children: [
                  Container(
                    width: 7,
                    height: 7,
                    decoration: BoxDecoration(color: color, shape: BoxShape.circle),
                  ),
                  const SizedBox(width: 7),
                  Text(
                    acceptsOrdersNow ? 'Restaurant ouvert' : 'Restaurant fermé',
                    style:
                        TextStyle(fontSize: 12.5, fontWeight: FontWeight.w700, color: color),
                  ),
                ],
              ),
            ),
            const SizedBox(width: 8),

            // INERTE : `PauseRestaurantCommand` / `ResumeRestaurantCommand`
            // sont écrites dans food-service et n'ont AUCUNE route HTTP (module
            // `foodServiceToggle`). Le geste le plus fréquent d'un coup de feu
            // n'a pas d'amont. Grisé plutôt que retiré : c'est la seule trace
            // visible de ce qu'il reste à ouvrir côté serveur.
            OutlinedButton(
              onPressed: null,
              style: OutlinedButton.styleFrom(
                minimumSize: const Size(0, 34),
                padding: const EdgeInsets.symmetric(horizontal: 12),
                side: BorderSide(color: colors.line),
                backgroundColor: colors.surface,
                textStyle: const TextStyle(fontSize: 12.5, fontWeight: FontWeight.w600),
                shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(20)),
              ),
              child: const Text('Fermer temporairement'),
            ),
          ],
        ),

        // Le motif du blocage : sans lui, un restaurateur voit « fermé » à 12 h
        // sans savoir si c'est ses horaires, sa pause, ou sa carte vide.
        if (reason != null) ...[
          const SizedBox(height: 6),
          Text(reason, style: TextStyle(fontSize: 12.5, color: colors.subtle)),
        ],
      ],
    );
  }
}

class _PendingAcceptanceCard extends StatelessWidget {
  const _PendingAcceptanceCard({required this.count});

  final int count;

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);

    return PartnerCard(
      borderColor: AppTheme.foodAmber.withValues(alpha: 0.45),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Container(
                width: 7,
                height: 7,
                decoration: const BoxDecoration(
                  color: AppTheme.danger,
                  shape: BoxShape.circle,
                ),
              ),
              const SizedBox(width: 7),
              const Text(
                'À TRAITER',
                style: TextStyle(
                  fontSize: 11,
                  fontWeight: FontWeight.w800,
                  letterSpacing: 0.9,
                  color: AppTheme.foodAmber,
                ),
              ),
            ],
          ),
          const SizedBox(height: 10),
          Text(
            '$count nouvelle${count > 1 ? 's' : ''} commande${count > 1 ? 's' : ''}',
            style: TextStyle(fontSize: 21, fontWeight: FontWeight.w800, color: colors.ink),
          ),
          const SizedBox(height: 6),

          // « À VALIDER SOUS 3 MINUTES » A ÉTÉ RETIRÉ, ET C'ÉTAIT UNE PROMESSE.
          //
          // Le domaine Food ne porte AUCUNE échéance d'acceptation : ni date
          // limite sur la commande, ni service qui expire une commande non
          // acceptée. Le compte à rebours de la maquette arrivait à zéro sans
          // que rien ne se produise. La phrase ci-dessous ne promet donc plus
          // qu'un délai est appliqué — elle dit ce qui attend.
          Text(
            'Ces commandes attendent votre acceptation.',
            style: TextStyle(fontSize: 13, color: colors.subtle),
          ),
          const SizedBox(height: 14),
          FilledButton(
            onPressed: () => context.go('/orders'),
            style: FilledButton.styleFrom(
              minimumSize: const Size.fromHeight(AppTheme.primaryButtonHeight),
            ),
            child: const Text('Voir les commandes'),
          ),
        ],
      ),
    );
  }
}

/// Carte sombre de la cuisine.
///
/// LE FOND SOMBRE N'EST PAS UN CHOIX ESTHÉTIQUE.
///
/// Il annonce l'écran de cuisine, qui est entièrement sombre — un poste de
/// travail consulté à un mètre, dans une cuisine éclairée, souvent les mains
/// occupées. La carte est un avant-goût de cet environnement.
class _KitchenCard extends StatelessWidget {
  const _KitchenCard({
    required this.preparing,
    required this.ready,
    required this.onOpen,
  });

  final int preparing;
  final int ready;

  /// `null` grise le bouton. Il l'a été tant que l'écran n'existait pas ; il ne
  /// devrait plus jamais l'être, mais le type garde la possibilité ouverte —
  /// un membre du personnel sans `restaurant.kitchen.manage` n'a rien à y faire.
  final VoidCallback? onOpen;

  @override
  Widget build(BuildContext context) => Container(
        padding: const EdgeInsets.all(16),
        decoration: BoxDecoration(
          color: AppTheme.charcoal,
          borderRadius: BorderRadius.circular(AppTheme.radiusCard),
        ),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                _KitchenCount(
                  value: preparing,
                  label: 'en préparation',
                  color: AppTheme.foodAmber,
                ),
                const SizedBox(width: 28),
                _KitchenCount(
                  value: ready,
                  label: ready > 1 ? 'prêtes' : 'prête',
                  color: AppTheme.brandGreenSoft,
                ),
              ],
            ),
            const SizedBox(height: 16),
            FilledButton(
              onPressed: onOpen,
              style: FilledButton.styleFrom(
                // Vert clair sur fond sombre : le vert de marque n'aurait pas
                // assez de contraste sur le charbon pour rester lisible.
                backgroundColor: const Color(0xFF7FC9A4),
                foregroundColor: AppTheme.charcoal,
                disabledBackgroundColor: const Color(0xFF3B4650),
                disabledForegroundColor: const Color(0xFF9AA7B2),
                minimumSize: const Size.fromHeight(AppTheme.primaryButtonHeight),
              ),
              child: Text(onOpen == null ? 'Écran cuisine — indisponible' : 'Ouvrir la cuisine'),
            ),
          ],
        ),
      );
}

class _KitchenCount extends StatelessWidget {
  const _KitchenCount({required this.value, required this.label, required this.color});

  final int value;
  final String label;
  final Color color;

  @override
  Widget build(BuildContext context) => Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            '$value',
            style: TextStyle(fontSize: 24, fontWeight: FontWeight.w800, color: color),
          ),
          const SizedBox(height: 2),
          Text(
            label,
            style: const TextStyle(fontSize: 11.5, color: Color(0xFF9AA7B2)),
          ),
        ],
      );
}

/// Plats indisponibles, comptés sur la carte du restaurateur.
///
/// AUCUN COMPTEUR N'EXISTE DANS LE CONTRAT : `RestaurantMenu.unavailableDishes`
/// parcourt les plats rendus. C'est exact parce que l'audience « Owner » rend
/// TOUT, y compris les plats épuisés — la vitrine publique, elle, les cache.
class _UnavailableDishesRow extends ConsumerWidget {
  const _UnavailableDishesRow({required this.restaurantId});

  final String restaurantId;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final colors = AppColors.of(context);
    final menu = ref.watch(restaurantMenuProvider(restaurantId));

    // Tant que la carte n'a pas répondu — ou si elle échoue — on n'affiche rien
    // plutôt qu'un « 0 plat indisponible » qu'un restaurateur en rupture lirait
    // comme une confirmation.
    final count = menu.valueOrNull?.unavailableDishes;
    if (count == null) return const SizedBox.shrink();

    return PartnerCard(
      child: Row(
        children: [
          Container(
            width: 32,
            height: 32,
            alignment: Alignment.center,
            decoration: BoxDecoration(
              color: count > 0 ? AppTheme.foodAmberSoft : colors.bg,
              borderRadius: BorderRadius.circular(9),
            ),
            child: Text(
              '$count',
              style: TextStyle(
                fontSize: 14,
                fontWeight: FontWeight.w800,
                color: count > 0 ? AppTheme.foodAmber : colors.subtle,
              ),
            ),
          ),
          const SizedBox(width: 12),
          Expanded(
            child: Text(
              count > 1 ? 'plats indisponibles' : 'plat indisponible',
              style: TextStyle(fontSize: 14, color: colors.ink),
            ),
          ),
          SizedBox(
            height: 34,
            child: FilledButton(
              onPressed: () => context.go('/activities'),
              style: FilledButton.styleFrom(
                padding: const EdgeInsets.symmetric(horizontal: 18),
                textStyle: const TextStyle(fontSize: 13, fontWeight: FontWeight.w700),
              ),
              child: const Text('Voir'),
            ),
          ),
        ],
      ),
    );
  }
}

/// Soldes du restaurant.
///
/// « EN ATTENTE DE RETRAIT » N'EST PAS DÉCORATIF : ces fonds ont quitté le
/// solde disponible mais ne sont pas encore versés. Les masquer donnerait
/// l'impression que l'argent s'est volatilisé entre la demande et le virement.
class _WalletCard extends StatelessWidget {
  const _WalletCard({required this.wallet});

  final DashboardWallet wallet;

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);

    Widget line(String label, double value, {bool strong = false}) => Padding(
          padding: const EdgeInsets.symmetric(vertical: 5),
          child: Row(
            children: [
              Expanded(
                child: Text(label, style: TextStyle(fontSize: 13.5, color: colors.subtle)),
              ),
              Text(
                Format.money(value, wallet.currency),
                style: TextStyle(
                  fontSize: strong ? 16 : 14,
                  fontWeight: strong ? FontWeight.w800 : FontWeight.w700,
                  color: colors.ink,
                ),
              ),
            ],
          ),
        );

    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 10),
      decoration: BoxDecoration(
        color: colors.surface,
        borderRadius: BorderRadius.circular(AppTheme.radiusCard),
        border: Border.all(color: colors.line),
      ),
      child: Column(
        children: [
          line('Disponible', wallet.availableBalance, strong: true),
          Divider(height: 1, color: colors.line),
          line('En attente de livraison', wallet.pendingBalance),
          line('Retrait en cours', wallet.pendingWithdrawal),
        ],
      ),
    );
  }
}
