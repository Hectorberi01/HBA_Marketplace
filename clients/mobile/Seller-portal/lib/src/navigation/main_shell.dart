import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import 'package:hba_express_pro/l10n/app_localizations.dart';
import '../core/providers/core_providers.dart';
import '../core/theme/app_theme.dart';
import '../features/activities/activities_data.dart';
import '../features/activities/selected_activity.dart';
import '../features/dashboard/dashboard_data.dart';
import '../features/messaging/chat_realtime.dart';
import '../features/messaging/messaging_data.dart';

/// Coquille principale : cinq onglets, ceux de la maquette HBA Partner.
///
/// LA COMPOSITION A CHANGÉ AVEC LE MODÈLE MULTI-ACTIVITÉS.
///
/// Elle était : Accueil · Commandes · Produits · Messages · Plus. Elle est
/// désormais : Accueil · Commandes · Activités · Finances · Compte.
///
/// Deux onglets ont disparu de la barre, et ce n'est pas une perte :
///   • « Produits » n'a plus de sens au premier niveau — un produit appartient à
///     UNE activité, et l'on n'y accède donc que par elle ;
///   • « Messages » et le fourre-tout « Plus » rejoignent « Compte ».
///
/// Deux onglets entrent : « Activités », qui n'existait pas avant qu'un compte
/// puisse porter plusieurs boutiques et restaurants, et « Finances », qui était
/// enfoui dans « Plus » alors que c'est ce qu'un commerçant regarde le plus.
class MainShell extends ConsumerStatefulWidget {
  const MainShell({super.key, required this.child, required this.location});

  final Widget child;
  final String location;

  static const _tabs = ['/home', '/orders', '/activities', '/finance', '/account'];

  @override
  ConsumerState<MainShell> createState() => _MainShellState();
}

class _MainShellState extends ConsumerState<MainShell> {
  final _inbox = InboxRealtime();
  Timer? _poll;

  int get _index {
    final i = MainShell._tabs.indexWhere((t) => widget.location.startsWith(t));
    return i < 0 ? 0 : i;
  }

  @override
  void initState() {
    super.initState();

    _initInbox();

    // Repli si le WebSocket est indisponible : sans lui, le badge de non-lus
    // resterait figé et le vendeur croirait n'avoir aucun message.
    _poll = Timer.periodic(const Duration(seconds: 30), (_) {
      if (mounted) ref.invalidate(conversationsProvider);
    });
  }

  Future<void> _initInbox() async {
    final token = await ref.read(tokenStorageProvider).accessToken;
    if (!mounted) return;
    await _inbox.connect(
      accessToken: token,
      onInbox: () {
        if (mounted) ref.invalidate(conversationsProvider);
      },
    );
  }

  @override
  void dispose() {
    _poll?.cancel();
    _inbox.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final l = AppLocalizations.of(context);

    // ═══════════════════════════════════════════════════════════════════════
    // LE 3ᵉ ONGLET CHANGE DE NOM ET D'ICÔNE AVEC L'ACTIVITÉ CHOISIE.
    //
    //   • vue consolidée → « Activités » : la liste de tout ce que je gère ;
    //   • boutique       → « Produits »  : le catalogue de CETTE boutique ;
    //   • restaurant     → « Menu »      : la carte de CE restaurant.
    //
    // Un restaurateur ne cherche pas ses plats sous « Produits », et un
    // commerçant n'a pas de « Menu ». Garder « Activités » dans les trois cas
    // obligerait à ouvrir l'onglet pour savoir ce qu'il contient — sur l'écran
    // qu'un partenaire visite le plus souvent après l'accueil.
    //
    // SEUL LE LIBELLÉ BOUGE, PAS LA ROUTE.
    //
    // La barre navigue toujours vers `/activities` ; c'est `ActivitiesTabScreen`
    // qui aiguille. Router vers `/products` ou `/menu` casserait au premier
    // changement d'activité : l'onglet actif se calcule par `startsWith`, donc
    // plus rien ne serait surligné, et l'écran affiché resterait celui de
    // l'activité qu'on vient de quitter.
    // ═══════════════════════════════════════════════════════════════════════
    final activity = ref.watch(selectedActivityProvider);
    final universe = activity?.universe;

    return Scaffold(
      body: widget.child,
      bottomNavigationBar: _HbaNavigationBar(
        index: _index,
        ordersBadge: _ordersBadge(activity),
        onSelected: (i) => context.go(MainShell._tabs[i]),
        labels: [
          l.navHome,
          l.navOrders,
          universe?.tabLabel ?? l.navActivities,
          l.navFinance,
          l.navAccount,
        ],
        catalogIcon: universe?.tabIcon ?? Icons.grid_view_outlined,
      ),
    );
  }

  /// ═══════════════════════════════════════════════════════════════════════════
  /// LE BADGE « COMMANDES », ET LES DEUX TROUS QU'IL LAISSE.
  ///
  /// EN VUE CONSOLIDÉE, IL N'Y A PAS DE BADGE — ET CE N'EST PAS UN OUBLI.
  ///
  /// Il valait « 5 », le total écrit dans le fichier de maquette. Aucun amont
  /// n'additionne les activités (module `merchantConsolidated`, cf.
  /// `core/network/not_migrated.dart`) : sommer les tableaux de bord côté client
  /// exigerait N requêtes, et les deux façades sont gardées par des rôles
  /// distincts. Zéro n'est pas « aucune commande » ici, c'est « pas de compteur
  /// disponible » — d'où l'absence totale de pastille plutôt qu'un « 0 ».
  ///
  /// EN CONTEXTE, LE NOMBRE VIENT DU TABLEAU DE BORD DE L'ACTIVITÉ.
  ///
  /// C'est le MÊME appel que celui de l'accueil : Riverpod le met en cache, donc
  /// ouvrir un autre onglet ne le redemande pas. Boutique →
  /// `today.ordersToProcess` (`Paid`/`Confirmed`/`Preparing`) ; restaurant →
  /// `kitchen.pending`, les tickets non encore acceptés.
  ///
  /// Tant que l'appel n'a pas répondu, pas de pastille : un compteur qui
  /// s'affiche puis change de valeur fait douter des deux.
  /// ═══════════════════════════════════════════════════════════════════════════
  int _ordersBadge(SellerActivity? activity) {
    if (activity == null) return 0;

    return switch (activity.universe) {
      HbaUniverse.express =>
        ref.watch(storeDashboardProvider(activity.id)).valueOrNull?.data.today.ordersToProcess ?? 0,
      HbaUniverse.food =>
        ref.watch(restaurantDashboardProvider(activity.id)).valueOrNull?.data.kitchenPending ?? 0,
    };
  }
}


/// Barre du bas conforme à la maquette.
///
/// CE N'EST PAS UNE `NavigationBar` MATERIAL 3, ET C'EST DÉLIBÉRÉ.
///
/// Material 3 dessine derrière l'onglet actif une pastille arrondie
/// (`indicator`) qu'on ne peut pas retirer proprement : la rendre transparente
/// laisse l'animation de survol, la hauteur imposée de 80 px et un espacement
/// icône/libellé qui ne correspondent pas à la maquette. Celle-ci ne montre
/// AUCUNE pastille — l'onglet actif se signale par la seule couleur.
///
/// Cinq colonnes égales et un `InkWell` par onglet donnent exactement le rendu
/// voulu, en moins de code que les contournements qu'il aurait fallu empiler.
class _HbaNavigationBar extends StatelessWidget {
  const _HbaNavigationBar({
    required this.index,
    required this.ordersBadge,
    required this.onSelected,
    required this.labels,
    required this.catalogIcon,
  });

  final int index;
  final int ordersBadge;
  final ValueChanged<int> onSelected;
  final List<String> labels;

  /// Icône du 3ᵉ onglet : grille (activités), carton (produits) ou couverts
  /// (menu). Passée depuis la coquille plutôt que lue ici : cette barre ne
  /// connaît pas l'activité courante, et n'a pas à la connaître.
  final IconData catalogIcon;

  static const List<IconData> _icons = [
    Icons.home_outlined,
    Icons.receipt_long_outlined,
    Icons.grid_view_outlined, // remplacée par `catalogIcon`
    Icons.credit_card_outlined,
    Icons.person_outline,
  ];

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);

    return Container(
      decoration: BoxDecoration(
        color: colors.surface,
        border: Border(top: BorderSide(color: colors.line)),
      ),
      child: SafeArea(
        top: false,
        child: SizedBox(
          height: 62,
          child: Row(
            children: [
              for (var i = 0; i < _icons.length; i++)
                Expanded(
                  child: _NavItem(
                    icon: i == 2 ? catalogIcon : _icons[i],
                    label: labels[i],
                    selected: i == index,
                    // Pastille sur « Commandes » uniquement : c'est le seul
                    // onglet dont le contenu réclame une action datée.
                    badge: i == 1 ? ordersBadge : 0,
                    onTap: () => onSelected(i),
                  ),
                ),
            ],
          ),
        ),
      ),
    );
  }
}

class _NavItem extends StatelessWidget {
  const _NavItem({
    required this.icon,
    required this.label,
    required this.selected,
    required this.badge,
    required this.onTap,
  });

  final IconData icon;
  final String label;
  final bool selected;
  final int badge;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);
    final tint = selected ? colors.ink : colors.subtle;

    return InkWell(
      onTap: onTap,
      // Sans `splashColor` transparent, un halo circulaire déborde sur les
      // onglets voisins — la colonne fait 62 px de haut pour ~75 de large.
      splashColor: Colors.transparent,
      highlightColor: Colors.transparent,
      child: Column(
        mainAxisAlignment: MainAxisAlignment.center,
        children: [
          SizedBox(
            height: 26,
            child: Stack(
              clipBehavior: Clip.none,
              alignment: Alignment.center,
              children: [
                Icon(icon, size: 23, color: tint),
                if (badge > 0)
                  Positioned(
                    top: -4,
                    right: -10,
                    child: Container(
                      padding: const EdgeInsets.symmetric(horizontal: 5, vertical: 1),
                      constraints: const BoxConstraints(minWidth: 18),
                      decoration: BoxDecoration(
                        color: AppTheme.danger,
                        borderRadius: BorderRadius.circular(9),
                      ),
                      child: Text(
                        '$badge',
                        textAlign: TextAlign.center,
                        style: const TextStyle(
                          fontSize: 10.5,
                          height: 1.3,
                          fontWeight: FontWeight.w800,
                          color: Colors.white,
                        ),
                      ),
                    ),
                  ),
              ],
            ),
          ),
          const SizedBox(height: 3),
          Text(
            label,
            maxLines: 1,
            overflow: TextOverflow.ellipsis,
            style: TextStyle(
              fontSize: 11,
              // La graisse change avec la sélection, comme sur la maquette. Sans
              // cela, seul le gris distingue l'onglet actif — insuffisant en
              // plein soleil, qui est la condition d'usage la plus courante ici.
              fontWeight: selected ? FontWeight.w800 : FontWeight.w500,
              color: tint,
            ),
          ),
        ],
      ),
    );
  }
}
