import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';

import 'package:flutter_riverpod/flutter_riverpod.dart';
import '../../../core/theme/app_theme.dart';
import '../../../shared/utils/formatters.dart';
import '../../../shared/widgets/async_views.dart';
import '../../../shared/widgets/partner_widgets.dart';
import '../../account/account_data.dart';
import '../../activities/activities_data.dart';
import '../../activities/presentation/activity_switcher_sheet.dart';
import '../../activities/selected_activity.dart';
import '../../orders/presentation/order_status_pill.dart';
import '../dashboard_data.dart';

/// ═════════════════════════════════════════════════════════════════════════════
/// TABLEAU DE BORD D'UNE BOUTIQUE HBAEXPRESS.
///
/// CET ÉCRAN EST CONTEXTUEL : IL PARLE D'UNE SEULE ACTIVITÉ.
///
/// À ne pas confondre avec le tableau de bord GLOBAL, qui consolide les deux
/// univers et n'offre aucune action opérationnelle. Ici, tout est actionnable et
/// tout concerne « HBA Tech Store » — d'où l'en-tête qui nomme l'activité et
/// permet d'en changer.
///
/// UN SEUL GRAPHIQUE, EN BAS. C'EST UNE CONTRAINTE DE LA MAQUETTE.
///
/// Un tableau de bord de commerçant sert à savoir QUOI FAIRE MAINTENANT, pas à
/// analyser. Les graphiques se lisent bien et n'aident pas à agir : les empiler
/// repousserait la file d'actions sous la ligne de flottaison, là où personne ne
/// va sur un téléphone.
/// ═════════════════════════════════════════════════════════════════════════════
class ExpressDashboardScreen extends ConsumerWidget {
  const ExpressDashboardScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final colors = AppColors.of(context);
    // `!` EST SÛR ICI, ET UNIQUEMENT ICI.
    //
    // Cet écran n'est construit que par `PartnerHomeScreen`, dont la branche
    // correspondante ne se déclenche que si l'activité est non nulle.
    final activity = ref.watch(selectedActivityProvider)!;
    final async = ref.watch(storeDashboardProvider(activity.id));

    return Scaffold(
      backgroundColor: colors.bg,
      body: SafeArea(
        bottom: false,
        child: RefreshIndicator(
          onRefresh: () async => ref.invalidate(storeDashboardProvider(activity.id)),
          child: async.when(
            loading: () => const LoadingView(),

            // 404 SIGNIFIE ICI « CETTE BOUTIQUE N'EST PAS LA VÔTRE », pas
            // « introuvable ». Le BFF refuse ainsi de confirmer l'existence d'une
            // boutique tierce. En pratique, cela n'arrive que si l'identifiant
            // retenu vient d'une session précédente.
            error: (e, _) => ErrorView(
              message: e.toString(),
              onRetry: () => ref.invalidate(storeDashboardProvider(activity.id)),
            ),
            data: (result) {
              final d = result.data;

              return ListView(
                padding: const EdgeInsets.fromLTRB(16, 8, 16, 24),
                children: [
                  PartnerContextHeader(
                    activity: activity,
                    onSwitchActivity: ActivitySwitcherSheet.show,
                    trailing: _NotificationBell(
                      // Le compteur réel vient de
                      // `GET /api/notifications/unread-count`. Tant qu'il n'a pas
                      // répondu, on n'affiche pas de point : un point permanent
                      // — ce qu'était `hasUnread: true` en dur — cesse d'être un
                      // signal au bout de deux jours.
                      hasUnread: (ref.watch(unreadNotificationsProvider).valueOrNull ?? 0) > 0,
                      onTap: () => context.push('/notifications'),
                    ),
                  ),
                  const SizedBox(height: 14),
                  _OpenStatusPill(isOpen: d.store.isSelling, universe: activity.universe),
                  const SizedBox(height: 18),

                  if (result.isPartial)
                    for (final w in result.warnings) _PartialBanner(message: w.message),

                  // ═════════════════════════════════════════════════════════════
                  // QUATRE INDICATEURS, ET SEULEMENT CE QUE LE JOUR PORTE.
                  //
                  // La maquette annonçait « +12 % vs hier » sous le chiffre
                  // d'affaires, et « sur 6 références » sous les articles vendus.
                  //
                  //   • LE DELTA J-1 N'EXISTE PAS : `MerchantTodayDto` ne rend que
                  //     le JOUR COURANT. Il n'y a aucune valeur d'hier à comparer,
                  //     dans aucun contrat.
                  //   • « ARTICLES VENDUS » N'EXISTE PAS NON PLUS : personne
                  //     n'agrège les quantités vendues par jour.
                  //
                  // Le quatrième indicateur est donc « À traiter »
                  // (`ordersToProcess`), qui lui est calculé — et qui dit au
                  // vendeur quoi faire maintenant, ce qu'un pourcentage ne fait
                  // pas.
                  // ═════════════════════════════════════════════════════════════
                  PartnerKpiGrid(kpis: [
                    PartnerKpi(
                      'CA aujourd\'hui',
                      Format.amount(d.today.revenueToday),
                      unit: d.today.currency ?? Format.cfa,
                    ),
                    PartnerKpi('Commandes', '${d.today.ordersToday}'),
                    PartnerKpi(
                      'Panier moyen',
                      // `null` quand aucune commande n'a été passée aujourd'hui :
                      // le serveur garde la division. « 0 F CFA » laisserait
                      // croire à des ventes à zéro franc.
                      d.today.averageBasket == null
                          ? '—'
                          : Format.amount(d.today.averageBasket),
                      unit: d.today.averageBasket == null
                          ? null
                          : (d.today.currency ?? Format.cfa),
                    ),
                    PartnerKpi(
                      'À traiter',
                      '${d.today.ordersToProcess}',
                      accent: d.today.ordersToProcess > 0 ? AppTheme.foodAmber : null,
                    ),
                  ]),
                  const SizedBox(height: 24),

                  // « À FAIRE MAINTENANT » SE RÉDUIT À UNE SEULE LIGNE.
                  //
                  // La maquette en montrait trois : commandes à préparer,
                  // « 5 produits presque en rupture », « 2 messages clients ».
                  //
                  //   • LE STOCK FAIBLE N'A AUCUN AMONT. `MerchantDashboardDto`
                  //     ne porte aucun bloc stock — le DTO le documente comme une
                  //     absence VOULUE — et rien ne balaie le catalogue d'un
                  //     vendeur pour en tirer une alerte. inventory-service sait
                  //     répondre par SKU, un par un.
                  //   • LES MESSAGES NON LUS existent
                  //     (`unreadCountProvider`), mais ils ont déjà leur badge sur
                  //     l'onglet Messages : les répéter ici ferait deux compteurs
                  //     à tenir d'accord.
                  //
                  // Reste ce qui est vrai ET actionnable : les commandes à
                  // traiter. La carte disparaît entièrement quand il n'y en a
                  // aucune — une file d'actions vide est du bruit.
                  if (d.today.ordersToProcess > 0) ...[
                    const PartnerSectionTitle('À faire maintenant'),
                    const SizedBox(height: 10),
                    _TodoCard(
                      count: d.today.ordersToProcess,
                      label: d.today.ordersToProcess > 1
                          ? 'commandes à préparer'
                          : 'commande à préparer',
                      onTap: () => context.go('/orders'),
                    ),
                    const SizedBox(height: 24),
                  ],

                  PartnerSectionTitle('Commandes récentes',
                      action: 'Voir tout', onTap: () => context.go('/orders')),
                  const SizedBox(height: 10),
                  if (d.recentOrders.isEmpty)
                    _EmptyCard(
                      // Distinguer les deux causes : une liste vide parce qu'il
                      // n'y a rien, ou parce qu'order-service n'a pas répondu.
                      // Les confondre annoncerait « aucune commande » à un
                      // vendeur qui en a.
                      message: result.warnings.any((w) => w.source == 'Order')
                          ? 'Vos commandes ne sont pas joignables pour le moment.'
                          : 'Aucune commande pour l\'instant.',
                    )
                  else
                    for (final order in d.recentOrders) ...[
                      _RecentOrderCard(order: order),
                      const SizedBox(height: 8),
                    ],
                  const SizedBox(height: 16),

                  // ═════════════════════════════════════════════════════════════
                  // « MEILLEURES VENTES » ET « PERFORMANCE · 7 JOURS » ONT ÉTÉ
                  //    RETIRÉES, ET C'EST LE PLUS GROS ÉCART À LA MAQUETTE.
                  //
                  //   • MEILLEURES VENTES — personne n'agrège les ventes par
                  //     produit : ni order-service, ni catalog-service, ni le BFF
                  //     merchant. Les trois lignes affichées (« iPhone 17 Pro,
                  //     14 vendus, 10 920 000 F CFA ») étaient écrites dans le
                  //     fichier de maquette.
                  //   • PERFORMANCE 7 JOURS — aucune série temporelle n'existe.
                  //     Elle était calculée par le BFF vendeur du MONOLITHE. Les
                  //     sept barres étaient des RATIOS inventés (0.42, 0.50,
                  //     0.62…) : le total « 1 640 000 F CFA » ne correspondait
                  //     à aucune somme réelle, et « +18 % » à aucun calcul.
                  //
                  // C'est le module `analytics`, déjà neutralisé en VEN2 pour
                  // l'écran `/analytics`. Le graphique disparaît ici plutôt que
                  // d'être remplacé par un état vide : une carte « pas de données »
                  // au bas d'un tableau de bord se lit comme une panne, alors que
                  // c'est une fonctionnalité qui n'a jamais été construite côté
                  // HBA. La ligne d'accueil vers `/analytics` porte déjà le
                  // message, une fois, au bon endroit.
                  // ═════════════════════════════════════════════════════════════

                  // Le solde, lui, existe — quand financial-service répond.
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

class _EmptyCard extends StatelessWidget {
  const _EmptyCard({required this.message});

  final String message;

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);
    return Container(
      padding: const EdgeInsets.all(16),
      decoration: BoxDecoration(
        color: colors.surface,
        borderRadius: BorderRadius.circular(AppTheme.radiusCard),
        border: Border.all(color: colors.line),
      ),
      child: Text(message, style: TextStyle(fontSize: 13.5, color: colors.subtle)),
    );
  }
}

/// Soldes du tableau de bord.
///
/// TROIS MONTANTS, ET « EN ATTENTE DE RETRAIT » N'EST PAS DÉCORATIF.
///
/// Ces fonds ont quitté le solde disponible mais ne sont pas encore versés. Les
/// masquer donnerait au vendeur l'impression que son argent s'est volatilisé
/// entre sa demande et le virement.
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


class _NotificationBell extends StatelessWidget {
  const _NotificationBell({required this.hasUnread, required this.onTap});

  final bool hasUnread;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);

    return InkWell(
      onTap: onTap,
      borderRadius: BorderRadius.circular(12),
      child: Container(
        width: AppTheme.minTapTarget,
        height: AppTheme.minTapTarget,
        decoration: BoxDecoration(
          color: colors.surface,
          borderRadius: BorderRadius.circular(12),
          border: Border.all(color: colors.line),
        ),
        child: Stack(
          alignment: Alignment.center,
          children: [
            Icon(Icons.notifications_none_rounded, size: 21, color: colors.ink),
            if (hasUnread)
              Positioned(
                top: 12,
                right: 13,
                // Un point, pas un compteur : l'en-tête dit qu'il y a du neuf,
                // l'écran des notifications dira quoi. Un nombre ici obligerait
                // à le tenir à jour pour une information que personne ne compte.
                child: Container(
                  width: 8,
                  height: 8,
                  decoration: const BoxDecoration(
                    color: AppTheme.danger,
                    shape: BoxShape.circle,
                  ),
                ),
              ),
          ],
        ),
      ),
    );
  }
}

class _OpenStatusPill extends StatelessWidget {
  const _OpenStatusPill({required this.isOpen, required this.universe});

  final bool isOpen;
  final HbaUniverse universe;

  /// L'ACCORD EST PORTÉ PAR LE `switch`, PAS PAR UNE RETOUCHE DE CHAÎNE.
  ///
  /// « Boutique ouverte » mais « Restaurant ouvert » : le genre change avec le
  /// métier. La version précédente concaténait puis corrigeait le résultat par
  /// `replaceFirst` — un correctif qui ne survit ni à une traduction, ni à
  /// l'arrivée d'un troisième métier, et qui ne se voit qu'à la relecture.
  String get _label => switch ((universe, isOpen)) {
        (HbaUniverse.food, true) => 'Restaurant ouvert',
        (HbaUniverse.food, false) => 'Restaurant fermé',
        (HbaUniverse.express, true) => 'Boutique ouverte',
        (HbaUniverse.express, false) => 'Boutique fermée',
      };

  @override
  Widget build(BuildContext context) {
    final color = isOpen ? AppTheme.brandGreen : AppTheme.slate;

    return Align(
      alignment: Alignment.centerLeft,
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          Container(
            width: 8,
            height: 8,
            decoration: BoxDecoration(color: color, shape: BoxShape.circle),
          ),
          const SizedBox(width: 7),
          Text(
            // Le mot du MÉTIER, pas un « Ouvert » générique qui laisserait le
            // partenaire se demander de quoi on parle.
            _label,
            style: TextStyle(fontSize: 13.5, fontWeight: FontWeight.w700, color: color),
          ),
        ],
      ),
    );
  }
}




/// Une seule ligne d'action : les commandes à traiter.
///
/// LE BOUTON MÈNE QUELQUE PART, MAINTENANT.
///
/// Il portait `onPressed: () {}` — un bouton inerte, indistinguable d'un bouton
/// en panne. Il ouvre l'écran Commandes, qui est branché.
class _TodoCard extends StatelessWidget {
  const _TodoCard({required this.count, required this.label, required this.onTap});

  final int count;
  final String label;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);

    return Container(
      decoration: BoxDecoration(
        color: colors.surface,
        borderRadius: BorderRadius.circular(AppTheme.radiusCard),
        border: Border.all(color: colors.line),
      ),
      child: Padding(
        padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 10),
        child: Row(
          children: [
            Container(
              width: 32,
              height: 32,
              alignment: Alignment.center,
              decoration: BoxDecoration(
                color: AppTheme.foodAmberSoft,
                borderRadius: BorderRadius.circular(9),
              ),
              child: Text(
                '$count',
                style: const TextStyle(
                    fontSize: 14, fontWeight: FontWeight.w800, color: AppTheme.foodAmber),
              ),
            ),
            const SizedBox(width: 12),
            Expanded(child: Text(label, style: TextStyle(fontSize: 14, color: colors.ink))),
            SizedBox(
              height: 34,
              child: FilledButton(
                onPressed: onTap,
                style: FilledButton.styleFrom(
                  padding: const EdgeInsets.symmetric(horizontal: 18),
                  textStyle: const TextStyle(fontSize: 13, fontWeight: FontWeight.w700),
                ),
                child: const Text('Voir'),
              ),
            ),
          ],
        ),
      ),
    );
  }
}

/// Aperçu d'une commande récente (`MerchantOrderDto`).
///
/// NI NOMBRE D'ARTICLES, NI HEURE « AUJOURD'HUI ».
///
/// La carte affichait « Aujourd'hui · 14:32 · 2 articles ». Le contrat ne porte
/// PAS de nombre d'articles — `MerchantOrderDto` n'a ni lignes ni compteur — et
/// « 2 articles » était écrit en dur, sur toutes les commandes. La date, elle,
/// est réelle : on l'affiche telle quelle, sans supposer qu'elle est d'aujourd'hui.
///
/// LE MONTANT EST CELUI DE LA COMMANDE ENTIÈRE. Sur un panier
/// multi-boutiques, il inclut les lignes des autres vendeurs. La part de ce
/// vendeur ne se calcule que depuis `GET /api/sellers/{id}/orders`, qui rend les
/// lignes — c'est l'écran Commandes qui la donne.
class _RecentOrderCard extends StatelessWidget {
  const _RecentOrderCard({required this.order});

  final DashboardOrder order;

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);

    return InkWell(
      onTap: () => context.push('/order/${order.id}'),
      borderRadius: BorderRadius.circular(AppTheme.radiusCard),
      child: Container(
        padding: const EdgeInsets.all(14),
        decoration: BoxDecoration(
          color: colors.surface,
          borderRadius: BorderRadius.circular(AppTheme.radiusCard),
          border: Border.all(color: colors.line),
        ),
        child: Column(
          children: [
            Row(
              mainAxisAlignment: MainAxisAlignment.spaceBetween,
              children: [
                Text(
                  order.reference,
                  style:
                      TextStyle(fontSize: 15, fontWeight: FontWeight.w800, color: colors.ink),
                ),
                Text(
                  Format.money(order.grandTotal, order.currency),
                  style:
                      TextStyle(fontSize: 15, fontWeight: FontWeight.w800, color: colors.ink),
                ),
              ],
            ),
            const SizedBox(height: 6),
            Row(
              mainAxisAlignment: MainAxisAlignment.spaceBetween,
              children: [
                Text(
                  order.createdAt == null ? '—' : Format.dateTime(order.createdAt!),
                  style: TextStyle(fontSize: 12.5, color: colors.subtle),
                ),
                // MÊME PASTILLE QUE L'ÉCRAN COMMANDES, PAS UNE COPIE.
                //
                // Elle vivait ici en double, avec sa propre table de couleurs.
                // Elle est désormais dans `orders/presentation/` avec les
                // statuts qu'elle traduit.
                OrderStatusPill(status: order.status),
              ],
            ),
          ],
        ),
      ),
    );
  }
}

