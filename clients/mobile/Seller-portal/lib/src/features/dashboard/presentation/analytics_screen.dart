import 'package:flutter/material.dart';

import '../../../core/network/not_migrated.dart';

/// ═════════════════════════════════════════════════════════════════════════════
/// STATISTIQUES — SANS AMONT.
///
/// AUCUNE AGRÉGATION N'EXISTE CÔTÉ SERVEUR. CE N'EST PAS UN CHEMIN À CORRIGER.
///
/// L'écran tirait trois lectures de `/seller/analytics` et `/seller/dashboard` :
/// la courbe des ventes sur 30/90 jours ou 12 mois, la répartition des commandes
/// par statut, et le passage du brut au net après commission. Ces trois calculs
/// étaient faits par le BFF vendeur du MONOLITHE, à partir de sa base unique.
///
/// Aucun service HBA ne calcule de série temporelle : ni `order-service`, qui
/// rend des commandes, ni `financial-service`, qui rend des mouvements. Le BFF
/// merchant (`/api/v1/bff/merchant`) n'expose rien de tel non plus. Rebrancher
/// suppose donc d'écrire l'agrégation, pas de changer une URL.
///
/// ET C'EST LE PIRE ÉCRAN OÙ INVENTER DES CHIFFRES.
///
/// Un vendeur décide de ses achats et de ses prix sur cette courbe. Une tendance
/// fabriquée n'est pas un défaut d'affichage : c'est une décision commerciale
/// prise sur des données qui n'existent pas.
///
/// CET ÉCRAN N'EST PLUS ROUTÉ (tâche #221), ET C'EST VOLONTAIRE.
///
/// `/analytics` était déclarée dans `app_router.dart` sans qu'AUCUN écran n'y
/// mène — la seule route du fichier dans ce cas. Le routeur affirmait par ailleurs
/// qu'`AnalyticsScreen` était « conservé hors routeur » : le code contredisait son
/// propre commentaire.
///
/// La distinction avec `/shipments` et `/returns`, qui mènent aussi à un état
/// « pas encore disponible » mais RESTENT routées, tient à ce qu'on promet.
/// Leur module existe — il vit dans le monolithe et sera extrait ; « bientôt » y
/// est exact. L'analytique, elle, n'est en chantier NULLE PART. Offrir une entrée
/// vers elle annoncerait un travail qui n'a pas commencé.
///
/// POUR REBRANCHER : ajouter l'agrégation au BFF merchant (série GMV, commandes
/// par statut, brut/net), publier la route, redéclarer la `GoRoute` dans
/// `app_router.dart`, AJOUTER L'ENTRÉE dans `account_screen.dart` — sans elle,
/// l'écran redeviendrait inatteignable — puis restaurer `dashboard_data.dart`
/// (supprimé avec cet écran) et les graphiques `fl_chart` que git conserve.
/// ═════════════════════════════════════════════════════════════════════════════
class AnalyticsScreen extends StatelessWidget {
  const AnalyticsScreen({super.key});

  @override
  Widget build(BuildContext context) => const NotMigratedScreen(
        title: 'Statistiques',
        message:
            'Vos statistiques de vente arrivent bientôt. En attendant, le détail '
            'de vos encaissements reste visible dans l\'onglet Finances.',
      );
}
