import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../core/theme/app_theme.dart';
import '../../../shared/utils/formatters.dart';
import '../../../shared/widgets/async_views.dart';
import '../../../shared/widgets/partner_widgets.dart';
import '../orders_data.dart';
import 'order_status_pill.dart';

/// ═════════════════════════════════════════════════════════════════════════════
/// DÉTAIL D'UNE COMMANDE REÇUE.
///
/// AUCUNE REQUÊTE ICI : LA COMMANDE EST CHERCHÉE DANS LA LISTE.
///
/// `GET /api/orders/{id}` existe, mais `GetOrderQuery(id, buyerId)` la scope à
/// l'ACHETEUR : un vendeur qui la demande reçoit 404 sur sa propre commande. Il
/// n'existe aucune route de détail vendeur. Comme
/// `GET /api/sellers/{sellerId}/orders` rend la commande COMPLÈTE — lignes et
/// adresse comprises — la résoudre dans la liste donne exactement la même
/// donnée, sans requête supplémentaire (cf. `orderProvider`).
///
/// IL N'Y A AUCUNE BARRE D'ACTIONS, ET CE N'EST PAS UN OUBLI.
///
/// La maquette posait « Refuser » et « Accepter » en bas de cet écran.
/// order-service n'expose AUCUNE route de changement de statut par le vendeur :
/// les transitions viennent d'événements (paiement encaissé, course terminée).
/// Un bouton ici ne pourrait que tomber sur un 404. Accepter et refuser existent
/// pour la RESTAURATION, sur le ticket de cuisine de food-service — pas sur la
/// commande, et pas depuis cet écran.
///
/// NI TÉLÉPHONE CLIENT, NI CRÉNEAU DE LIVRAISON, NI REMISE.
///
/// `OrderSummary` porte le destinataire et son téléphone DANS L'ADRESSE de
/// livraison, quand elle existe — c'est tout. Pas de numéro masqué, pas
/// d'autorisation de contact, pas de créneau demandé, pas de ligne de remise :
/// ces quatre éléments de la maquette venaient du BFF du monolithe et n'ont
/// aucun équivalent. Le sous-total, lui, se recompose depuis les lignes.
/// ═════════════════════════════════════════════════════════════════════════════
class PartnerOrderDetailScreen extends ConsumerWidget {
  const PartnerOrderDetailScreen({super.key, required this.reference});

  /// Identifiant de la commande, tel que la liste le pousse dans l'URL.
  ///
  /// LE PARAMÈTRE GARDE SON NOM POUR NE PAS CASSER LA ROUTE, MAIS CE N'EST
  /// PLUS UNE RÉFÉRENCE D'AFFICHAGE. `CMD-XXXXXXXX` est calculé pour l'écran ;
  /// seul l'identifiant retrouve la commande. `orderProvider` accepte les deux
  /// par prudence — un lien profond ancien pourrait encore porter l'abréviation.
  final String reference;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final colors = AppColors.of(context);
    final async = ref.watch(orderProvider(reference));

    return Scaffold(
      backgroundColor: colors.bg,
      appBar: AppBar(title: const Text('Détail de la commande')),
      body: async.when(
        loading: () => const LoadingView(),

        // Deux causes bien distinctes, et le message du serveur les sépare :
        // la commande n'est pas (ou plus) dans le périmètre du vendeur
        // (`order.not_in_seller_scope`), ou order-service n'a pas répondu.
        error: (e, _) => ErrorView(
          message: e.toString(),
          onRetry: () => ref.invalidate(ordersProvider),
        ),
        data: (order) => ListView(
          padding: const EdgeInsets.fromLTRB(16, 16, 16, 32),
          children: [
            _Header(order: order),
            const SizedBox(height: 20),

            // Motif d'arbitrage : sans lui, le vendeur lit « En arbitrage » sans
            // savoir ce qui lui est reproché, et appelle le support.
            if (order.reviewReason != null) ...[
              _ReviewBanner(reason: order.reviewReason!),
              const SizedBox(height: 20),
            ],

            const PartnerSectionTitle('Vos articles'),
            const SizedBox(height: 10),
            _MyLines(order: order),
            const SizedBox(height: 20),

            const PartnerSectionTitle('Montants'),
            const SizedBox(height: 10),
            _Totals(order: order),
            const SizedBox(height: 20),

            // L'adresse est ABSENTE tant que l'acheteur n'en a pas figé une
            // (retrait en boutique, commande non finalisée). Une carte vide
            // ferait chercher une information qui n'a jamais existé.
            if (order.address != null && order.address!.hasContent) ...[
              const PartnerSectionTitle('Livraison'),
              const SizedBox(height: 10),
              _Delivery(address: order.address!),
            ],
          ],
        ),
      ),
    );
  }
}

class _Header extends StatelessWidget {
  const _Header({required this.order});

  final SellerOrder order;

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(
          order.reference,
          style: TextStyle(fontSize: 24, fontWeight: FontWeight.w800, color: colors.ink),
        ),
        const SizedBox(height: 8),
        Row(
          children: [
            OrderStatusPill(status: order.status),
            const SizedBox(width: 10),
            Text(
              Format.dateTime(order.createdAt),
              style: TextStyle(fontSize: 12.5, color: colors.subtle),
            ),
          ],
        ),
      ],
    );
  }
}

class _ReviewBanner extends StatelessWidget {
  const _ReviewBanner({required this.reason});

  final String reason;

  @override
  Widget build(BuildContext context) => Container(
        padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 12),
        decoration: BoxDecoration(
          color: const Color(0xFFFDECEC),
          borderRadius: BorderRadius.circular(AppTheme.radiusCard),
        ),
        child: Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            const Icon(Icons.gavel_outlined, size: 18, color: AppTheme.danger),
            const SizedBox(width: 10),
            Expanded(
              child: Text(
                reason,
                style: const TextStyle(fontSize: 13, height: 1.4, color: AppTheme.danger),
              ),
            ),
          ],
        ),
      );
}

/// Les lignes DE CE VENDEUR, et rien d'autre.
///
/// `lines` CONTIENT AUSSI CELLES DES CONCURRENTS.
///
/// `ListBySellerAsync` sélectionne les commandes ayant au moins une ligne de ce
/// vendeur, puis rend la commande ENTIÈRE sans filtrer. Sur un panier
/// multi-boutiques, afficher `lines` montrerait les articles et les montants
/// d'une autre boutique. Le tri est fait dans le modèle (`myLines`).
///
/// NI NOM DE PRODUIT, NI PHOTO : `OrderLineSummary` n'en porte aucun. Le SKU
/// est ce que la commande porte réellement, et c'est aussi ce que le vendeur lit
/// sur son bon de préparation. Résoudre le libellé exigerait un appel au
/// catalogue PAR LIGNE, sur des produits qui peuvent avoir été supprimés depuis.
class _MyLines extends StatelessWidget {
  const _MyLines({required this.order});

  final SellerOrder order;

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);

    if (order.myLines.isEmpty) {
      return PartnerCard(
        child: Text(
          'Aucune ligne ne vous concerne sur cette commande.',
          style: TextStyle(fontSize: 13.5, color: colors.subtle),
        ),
      );
    }

    return PartnerCard(
      child: Column(
        children: [
          for (final line in order.myLines)
            Padding(
              padding: const EdgeInsets.symmetric(vertical: 7),
              child: Row(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(
                          line.label,
                          style: TextStyle(
                            fontSize: 14,
                            fontWeight: FontWeight.w700,
                            color: colors.ink,
                          ),
                        ),
                        const SizedBox(height: 2),
                        Text(
                          '${line.quantity} × '
                          '${Format.money(line.unitPrice, order.currency)}',
                          style: TextStyle(fontSize: 12.5, color: colors.subtle),
                        ),
                      ],
                    ),
                  ),
                  const SizedBox(width: 10),
                  Text(
                    Format.money(line.lineTotal, order.currency),
                    style: TextStyle(
                      fontSize: 14,
                      fontWeight: FontWeight.w700,
                      color: colors.ink,
                    ),
                  ),
                ],
              ),
            ),
        ],
      ),
    );
  }
}

class _Totals extends StatelessWidget {
  const _Totals({required this.order});

  final SellerOrder order;

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);

    Widget line(String label, String value, {bool strong = false, String? hint}) => Padding(
          padding: const EdgeInsets.symmetric(vertical: 6),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(
                children: [
                  Expanded(
                    child: Text(
                      label,
                      style: TextStyle(
                        fontSize: 14,
                        fontWeight: strong ? FontWeight.w800 : FontWeight.w500,
                        color: strong ? colors.ink : colors.subtle,
                      ),
                    ),
                  ),
                  Text(
                    value,
                    style: TextStyle(
                      fontSize: strong ? 16 : 14,
                      fontWeight: strong ? FontWeight.w800 : FontWeight.w700,
                      color: strong ? AppTheme.brandGreen : colors.ink,
                    ),
                  ),
                ],
              ),
              if (hint != null) ...[
                const SizedBox(height: 2),
                Text(hint, style: TextStyle(fontSize: 11.5, color: colors.subtle)),
              ],
            ],
          ),
        );

    return PartnerCard(
      child: Column(
        children: [
          // « VOTRE PART » N'EST PAS UN NET À PERCEVOIR.
          //
          // C'est la somme brute des lignes de ce vendeur. La commission
          // réellement appliquée n'est PAS portée par la commande : le taux rendu
          // par merchant-service est le DÉFAUT PLATEFORME, pas celui négocié
          // (tâche « Le vendeur voit encore le taux par défaut, pas le sien »).
          // Déduire 10 % ici afficherait un net faux à qui a un autre taux. Le
          // montant réellement crédité se lit sur le portefeuille.
          line(
            'Votre part',
            Format.money(order.myTotal, order.currency),
            strong: true,
            hint: 'Avant commission — voir votre portefeuille pour le net crédité.',
          ),
          Divider(height: 1, color: colors.line),

          // Le total de la commande ENTIÈRE : sur un panier multi-boutiques, il
          // inclut les lignes des autres vendeurs. Il est affiché pour que le
          // vendeur ne s'étonne pas d'un écart avec ce que le client lui annonce.
          line('Total payé par le client', Format.money(order.grandTotal, order.currency)),
          line('dont livraison', Format.money(order.shippingFee, order.currency)),
        ],
      ),
    );
  }
}

class _Delivery extends StatelessWidget {
  const _Delivery({required this.address});

  final ShippingAddress address;

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);

    return PartnerCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          if (address.recipient.isNotEmpty)
            Text(
              address.recipient,
              style:
                  TextStyle(fontSize: 14.5, fontWeight: FontWeight.w700, color: colors.ink),
            ),

          // Le repère en tête : c'est ce qu'on lit à voix haute à un zem.
          if (address.summary.isNotEmpty) ...[
            const SizedBox(height: 4),
            Text(
              address.summary,
              style: TextStyle(fontSize: 13, height: 1.4, color: colors.subtle),
            ),
          ],

          // LE TÉLÉPHONE EST RENDU EN CLAIR PAR LE CONTRAT.
          //
          // La maquette montrait un numéro masqué (« +229 •• ••• 42 ») et un
          // appel routé par la plateforme. Rien de tel n'existe :
          // `OrderShippingAddressSummary.Phone` est le vrai numéro, figé sur la
          // commande. On l'affiche tel quel — masquer à l'écran un numéro qu'on
          // détient de toute façon ne protège personne.
          if (address.phone.isNotEmpty) ...[
            const SizedBox(height: 6),
            Text(
              address.phone,
              style:
                  TextStyle(fontSize: 13.5, fontWeight: FontWeight.w600, color: colors.ink),
            ),
          ],
        ],
      ),
    );
  }
}
