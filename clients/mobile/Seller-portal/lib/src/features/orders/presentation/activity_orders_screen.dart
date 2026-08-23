import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../../core/theme/app_theme.dart';
import '../../../shared/utils/formatters.dart';
import '../../../shared/widgets/async_views.dart';
import '../../../shared/widgets/partner_widgets.dart';
import '../../activities/activities_data.dart';
import '../../activities/selected_activity.dart';
import '../orders_data.dart';
import 'order_filters.dart';
import 'order_status_pill.dart';

/// ═════════════════════════════════════════════════════════════════════════════
/// COMMANDES REÇUES — `GET /api/sellers/{sellerId}/orders`.
///
/// LA LISTE EST CELLE DU VENDEUR, PAS CELLE DE L'ACTIVITÉ. C'EST LE PLUS
///    GROS ÉCART À L'INTENTION DE L'ÉCRAN, ET IL N'EST PAS COMBLABLE ICI.
///
/// La route est scopée par `sellerId` — le compte marchand — et `OrderSummary`
/// ne porte AUCUN identifiant de boutique : ni sur la commande, ni sur ses
/// lignes (`OrderLineSummary` a `productId`, `sellerId`, `sku`, quantités et
/// montants, rien d'autre). Un vendeur à deux boutiques voit donc, sous chacune,
/// les commandes des deux.
///
/// Le seul découpage RÉEL est `kind` (`Goods` / `Food`) : on filtre dessus pour
/// que la file d'un restaurant ne mélange pas des colis, et inversement. C'est
/// exact, mais ce n'est pas « les commandes de CETTE boutique ».
///
/// POUR COMBLER : porter le `storeId` sur `OrderSummary` (order-service le
/// connaît, `Store` existe depuis la tâche S6), puis filtrer ici — ou mieux,
/// côté serveur.
///
/// AUCUN BOUTON D'AVANCEMENT SUR LES CARTES, NI AILLEURS.
///
/// order-service n'expose AUCUNE route de changement de statut par le vendeur :
/// ni accepter, ni préparer, ni expédier. Les transitions sont pilotées par
/// événements (paiement encaissé, course terminée). Le couple
/// « Refuser / Accepter » de la maquette n'existe que côté RESTAURATION, sur le
/// ticket de cuisine de food-service — un autre écran, un autre amont.
///
/// LA FLÈCHE DE RETOUR NE DÉPILE RIEN : ELLE REMONTE AU CONSOLIDÉ.
///
/// Cet écran occupe un onglet racine : `Navigator.pop` n'aurait rien à dépiler
/// et lèverait dès qu'on l'ouvre depuis la barre du bas. Le chevron remet donc
/// l'activité courante à `null`, ce qui ramène à la vue consolidée — et remet du
/// même coup l'accueil, le catalogue et les finances en consolidé, puisque le
/// contexte est unique.
/// ═════════════════════════════════════════════════════════════════════════════
class ActivityOrdersScreen extends ConsumerStatefulWidget {
  const ActivityOrdersScreen({super.key, required this.activity});

  final SellerActivity activity;

  @override
  ConsumerState<ActivityOrdersScreen> createState() => _ActivityOrdersScreenState();
}

class _ActivityOrdersScreenState extends ConsumerState<ActivityOrdersScreen> {
  OrderFilter _filter = OrderFilter.all;

  /// `kind` attendu pour l'univers de l'activité — les deux seules valeurs
  /// qu'order-service émet (`OrderKind`).
  String get _kind => widget.activity.universe == HbaUniverse.food ? 'Food' : 'Goods';

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);
    final filters = OrderFilter.forSeller;
    final async = ref.watch(ordersProvider);

    return Scaffold(
      backgroundColor: colors.bg,
      body: SafeArea(
        bottom: false,
        child: Column(
          children: [
            Padding(
              padding: const EdgeInsets.fromLTRB(8, 6, 16, 0),
              child: Row(
                children: [
                  _BackToGlobal(
                    onTap: () =>
                        ref.read(selectedActivityIdProvider.notifier).choisir(null),
                  ),
                  const SizedBox(width: 4),
                  Expanded(
                    child: PartnerScreenHeader(
                      title: 'Commandes',
                      activity: widget.activity,
                    ),
                  ),
                ],
              ),
            ),
            const SizedBox(height: 14),

            SizedBox(
              height: 36,
              child: ListView(
                scrollDirection: Axis.horizontal,
                padding: const EdgeInsets.symmetric(horizontal: 16),
                children: [
                  for (final f in filters)
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

                // « PAS DE BOUTIQUE » ARRIVE ICI AUSSI : `ordersProvider`
                // passe par `requiredSellerIdProvider`, qui lève une erreur
                // NOMMÉE (`seller.no_shop`) plutôt que d'envoyer un identifiant
                // vide et de récolter un 403 incompréhensible.
                error: (e, _) => ErrorView(
                  message: e.toString(),
                  onRetry: () => ref.invalidate(ordersProvider),
                ),
                data: (all) {
                  final mine = [
                    for (final o in all)
                      if (o.kind == _kind) o,
                  ];
                  final visible = [
                    for (final o in mine)
                      if (_filter.matches(o)) o,
                  ];

                  if (visible.isEmpty) {
                    return RefreshIndicator(
                      onRefresh: () async => ref.invalidate(ordersProvider),
                      // `ListView` obligatoire sous un `RefreshIndicator` : un
                      // état vide non défilable ne laisse rien à tirer.
                      child: ListView(
                        children: [
                          PartnerEmptyState(
                            icon: Icons.receipt_long_outlined,
                            message: mine.isEmpty
                                ? 'Aucune commande pour ${widget.activity.name}.'
                                : 'Aucune commande dans « ${_filter.label} ».',
                          ),
                        ],
                      ),
                    );
                  }

                  return RefreshIndicator(
                    onRefresh: () async => ref.invalidate(ordersProvider),
                    child: ListView.separated(
                      padding: const EdgeInsets.fromLTRB(16, 0, 16, 24),
                      itemCount: visible.length,
                      separatorBuilder: (_, __) => const SizedBox(height: 12),
                      itemBuilder: (_, i) => OrderListCard(order: visible[i]),
                    ),
                  );
                },
              ),
            ),
          ],
        ),
      ),
    );
  }
}

/// La carte de la file de travail.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// TROIS CHOSES QUE LA MAQUETTE AFFICHAIT ET QUI N'ONT PAS D'AMONT.
///
///   • LE NOM DU CLIENT — `OrderSummary` ne rend que `BuyerId`, un GUID. Le seul
///     nom réel est celui du DESTINATAIRE de la livraison, quand l'acheteur a
///     renseigné une adresse : c'est [SellerOrder.recipientName], et il est
///     `null` le reste du temps. On n'affiche alors que le nombre d'articles.
///   • « AUJOURD'HUI · 14:32 » — la date est réelle, mais rien ne dit qu'elle est
///     d'aujourd'hui. On l'écrit telle quelle.
///   • LE MONTANT — `grandTotal` est celui de la commande ENTIÈRE, frais de
///     livraison et lignes des autres vendeurs compris. C'est [myTotal] qu'on
///     affiche : la somme des lignes de CE vendeur, seule valeur qui soit « sa »
///     vente.
/// ═════════════════════════════════════════════════════════════════════════════
class OrderListCard extends StatelessWidget {
  const OrderListCard({super.key, required this.order});

  final SellerOrder order;

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);

    // La carte s'accentue quand la commande est encaissée et attend d'être
    // servie : c'est la seule ligne où quelque chose est dû, dans une liste
    // autrement uniforme.
    final highlighted = order.status == SellerOrderStatus.paid ||
        order.status == SellerOrderStatus.confirmed;

    final recipient = order.recipientName;
    final items =
        '${order.itemCount} ${order.itemCount > 1 ? 'articles' : 'article'}';

    return PartnerCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Expanded(
                child: Text(
                  order.reference,
                  style: TextStyle(
                    fontSize: 15,
                    fontWeight: FontWeight.w800,
                    color: colors.ink,
                  ),
                ),
              ),
              Text(
                Format.dateTime(order.createdAt),
                style: TextStyle(fontSize: 12, color: colors.subtle),
              ),
            ],
          ),
          const SizedBox(height: 6),

          Row(
            crossAxisAlignment: CrossAxisAlignment.end,
            children: [
              Expanded(
                child: Text(
                  // « 1 article » / « 2 articles » : l'accord se fait ici plutôt
                  // que par un « (s) » qui n'a jamais existé en français écrit.
                  recipient == null ? items : '$recipient · $items',
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: TextStyle(fontSize: 13, color: colors.subtle),
                ),
              ),
              const SizedBox(width: 8),
              Text(
                Format.money(order.myTotal, order.currency),
                style: TextStyle(
                  fontSize: 16,
                  fontWeight: FontWeight.w800,
                  color: colors.ink,
                ),
              ),
            ],
          ),
          const SizedBox(height: 12),

          Row(
            children: [
              OrderStatusPill(status: order.status),
              const Spacer(),
              OutlinedButton(
                // ON POUSSE L'IDENTIFIANT, PAS LA RÉFÉRENCE AFFICHÉE.
                //
                // `CMD-XXXXXXXX` est une abréviation calculée pour l'écran ;
                // seul l'identifiant permet de retrouver la commande dans la
                // liste (cf. `orderProvider`).
                onPressed: () => context.push('/order/${order.id}'),
                style: OutlinedButton.styleFrom(
                  minimumSize: const Size(0, 40),
                  padding: const EdgeInsets.symmetric(horizontal: 16),
                  side: BorderSide(
                    color: highlighted ? AppTheme.brandGreen : colors.line,
                  ),
                  foregroundColor: highlighted ? AppTheme.brandGreen : colors.ink,
                  textStyle: const TextStyle(
                    fontSize: 13.5,
                    fontWeight: FontWeight.w700,
                  ),
                ),
                child: const Text('Voir la commande'),
              ),
            ],
          ),
        ],
      ),
    );
  }
}

class _BackToGlobal extends StatelessWidget {
  const _BackToGlobal({required this.onTap});

  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);

    return InkWell(
      onTap: onTap,
      borderRadius: BorderRadius.circular(12),
      child: SizedBox(
        width: AppTheme.minTapTarget,
        height: AppTheme.minTapTarget,
        child: Icon(Icons.chevron_left, size: 26, color: colors.ink),
      ),
    );
  }
}
