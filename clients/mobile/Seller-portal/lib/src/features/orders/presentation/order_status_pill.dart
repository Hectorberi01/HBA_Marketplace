import 'package:flutter/material.dart';

import '../../../core/theme/app_theme.dart';
import '../../../shared/widgets/partner_widgets.dart';
import '../orders_data.dart';

/// Pastille d'état d'une commande, sur les statuts RÉELS de order-service.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// TABLE UNIQUE, ET C'EST TOUTE LA RAISON D'ÊTRE DE CE FICHIER.
///
/// Le tableau de bord et la liste des commandes affichaient chacun leur propre
/// table de couleurs. Une commande « Confirmée » ambre d'un côté et verte de
/// l'autre n'est pas une nuance de style : c'est le vendeur qui apprend deux fois
/// le même code, puis cesse de s'y fier.
///
/// LES LIBELLÉS VIENNENT DE [SellerOrderStatus.label], PAS D'ICI.
///
/// Un statut inconnu y rend son code brut plutôt qu'un libellé inventé : le jour
/// où order-service en ajoute un, le vendeur le voit, et nous aussi.
/// ═════════════════════════════════════════════════════════════════════════════
class OrderStatusPill extends StatelessWidget {
  const OrderStatusPill({super.key, required this.status});

  /// Valeur brute de `OrderStatus` (PascalCase).
  final String status;

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);

    // Le gris est réservé à ce qui ne réclame plus rien du vendeur. Colorer aussi
    // le terminé remplirait la liste de pastilles vives où l'œil ne
    // distinguerait plus ce qui attend une action.
    final (tint, background) = switch (status) {
      // Encaissée ou confirmée : c'est là que le vendeur doit préparer.
      SellerOrderStatus.paid ||
      SellerOrderStatus.confirmed =>
        (AppTheme.foodAmber, AppTheme.foodAmberSoft),

      // Rouge : arbitrage ou échec — dans les deux cas l'argent est en suspens.
      SellerOrderStatus.underReview ||
      SellerOrderStatus.cancelled ||
      SellerOrderStatus.failed =>
        (AppTheme.danger, const Color(0xFFFDECEC)),

      SellerOrderStatus.delivered => (AppTheme.brandGreen, AppTheme.brandGreenSoft),

      // `Pending`, `AwaitingPayment` et tout statut futur : neutre. Rien n'est
      // encore dû au vendeur tant que le paiement n'est pas passé.
      _ => (colors.subtle, colors.bg),
    };

    return PartnerStatusDot(
      label: SellerOrderStatus.label(status),
      color: tint,
      background: background,
    );
  }
}
