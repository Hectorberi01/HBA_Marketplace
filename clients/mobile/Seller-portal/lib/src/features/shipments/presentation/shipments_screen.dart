import 'package:flutter/material.dart';

import '../../../core/network/not_migrated.dart';

/// ═════════════════════════════════════════════════════════════════════════════
/// EXPÉDITIONS — SANS AMONT.
///
/// LE MODULE SHIPPING N'A JAMAIS ÉTÉ EXTRAIT DU MONOLITHE.
///
/// L'écran parlait à `/seller/shipments*` : la file d'attente, `by-order/{id}`,
/// puis `prepare`, `ship`, `deliver`, `cancel`. Tout cela vivait dans le BFF
/// vendeur du monolithe, disparu avec lui. Aucune entrée de
/// `ReverseProxy:Routes` (HBA.Gateway.Api/appsettings.json) ne mène à un service
/// d'expédition : il n'y a pas de `shipping-service`, et `/api/delivery` — qui
/// existe — sert les COURSES du livreur, pas le cycle
/// Pending → Prepared → Shipped → Delivered d'un colis vendeur.
///
/// CE N'ÉTAIT PAS UN ÉCRAN VIDE, MAIS UN ÉCRAN FAUX.
///
/// Il affichait une file de colis à préparer avec des boutons d'action. Un
/// vendeur qui appuie sur « Marquer préparée » croit avoir prévenu son acheteur.
/// Mieux vaut lui dire qu'il doit s'en charger autrement.
///
/// POUR REBRANCHER : extraire Shipping, publier une route de passerelle, puis
/// restaurer `shipments_data.dart` et `carriers_data.dart` (supprimés avec cet
/// écran — l'historique git les garde) ainsi que le sélecteur de transporteur.
/// ═════════════════════════════════════════════════════════════════════════════
class ShipmentsScreen extends StatelessWidget {
  const ShipmentsScreen({super.key});

  @override
  Widget build(BuildContext context) => const NotMigratedScreen(
        title: 'Expéditions',
        message:
            'Le suivi de vos expéditions arrive bientôt. En attendant, convenez '
            'de la remise du colis directement avec votre client.',
      );
}
