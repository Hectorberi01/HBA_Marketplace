import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:hba_express_pro/l10n/app_localizations.dart';

import '../../../core/config/app_config.dart';
import '../../../core/theme/app_theme.dart';
import '../../../shared/utils/formatters.dart';
import '../../../shared/widgets/ui_kit.dart';
import '../offers_data.dart';
import 'offer_sheets.dart';

/// Carte d'une mise en vente, partagée entre l'écran Mises en vente et la fiche produit : le
/// prix est présenté exactement de la même façon des deux côtés.
class OfferCard extends ConsumerWidget {
  const OfferCard({super.key, required this.offer, this.showProductName = true});

  final Offer offer;

  /// Sur la fiche d'un produit, répéter son nom à chaque mise en vente est du bruit.
  final bool showProductName;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final colors = AppColors.of(context);
    final l = AppLocalizations.of(context);
    return CardSection(
      margin: EdgeInsets.zero,
      padding: const EdgeInsets.all(14),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Expanded(
                child: Text(
                  showProductName ? offer.productName : 'SKU ${offer.sku}',
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: const TextStyle(fontWeight: FontWeight.w700, fontSize: 15),
                ),
              ),
              StatusPill.catalog(l, offer.status),
              PopupMenuButton<String>(
                tooltip: l.offCardActionsTooltip,
                position: PopupMenuPosition.under,
                icon: Icon(Icons.more_vert, color: colors.subtle),
                onSelected: (a) => OfferSheets.runAction(context, ref, offer, a),
                itemBuilder: (_) => [
                  appMenuItem(value: 'price', icon: Icons.sell_outlined, label: l.offCardEditPrice),
                  if (offer.hasDiscount)
                    appMenuItem(value: 'discount_remove', icon: Icons.local_offer_outlined, label: l.offCardRemoveDiscount)
                  else
                    appMenuItem(value: 'discount', icon: Icons.local_offer_outlined, label: l.offCardApplyDiscount),
                  if (offer.status.toLowerCase() == 'active')
                    appMenuItem(value: 'paused', icon: Icons.pause_circle_outline, label: l.offCardPause)
                  else
                    appMenuItem(value: 'active', icon: Icons.play_circle_outline, label: l.offCardReactivate),
                ],
              ),
            ],
          ),
          const SizedBox(height: 4),
          Row(
            children: [
              if (showProductName) ...[
                Text('SKU ${offer.sku}', style: TextStyle(fontSize: 12, color: colors.subtle)),
                const SizedBox(width: 8),
              ],
              // L'état annoncé engage le vendeur vis-à-vis de l'acheteur : il doit
              // être lisible sans ouvrir la mise en vente.
              StatusBadge(
                label: conditionLabel(l, offer.condition),
                color: offer.condition.toLowerCase() == 'new' ? AppTheme.info : AppTheme.promoOrange,
              ),
              const SizedBox(width: 8),
              if (offer.handlingTime > 0)
                Text(l.offCardHandling(offer.handlingTime),
                    style: TextStyle(fontSize: 11, color: colors.subtle)),
            ],
          ),
          Divider(height: 20, color: colors.line),

          // Les deux prix, toujours ensemble : le vendeur doit voir d'un coup
          // d'œil ce qu'il touche ET ce que le client paie.
          KeyValueRow(
            label: l.offCardYouReceive,
            value: Format.money(offer.sellerPrice, offer.currency),
            strong: true,
            color: AppTheme.brandGreen,
          ),
          KeyValueRow(
            label: l.offCardCustomerPays,
            value: Format.money(offer.productPrice, offer.currency),
          ),
          if (offer.hasDiscount)
            Align(
              alignment: Alignment.centerRight,
              child: Row(
                mainAxisSize: MainAxisSize.min,
                children: [
                  StatusBadge(label: l.offCardPromo, color: AppTheme.danger, icon: Icons.local_offer_outlined),
                  const SizedBox(width: 8),
                  Text(
                    l.offCardBefore(Format.money(offer.compareAt!, offer.currency)),
                    style: TextStyle(
                      fontSize: 11,
                      color: colors.subtle,
                      decoration: TextDecoration.lineThrough,
                    ),
                  ),
                ],
              ),
            ),
          const SizedBox(height: 2),
          Text(
            l.offCardSpreadNote(
                (AppConfig.commissionRate * 100).round(), (AppConfig.providerFeeRate * 100).round()),
            style: TextStyle(fontSize: 11, color: colors.subtle, height: 1.4),
          ),
        ],
      ),
    );
  }
}
