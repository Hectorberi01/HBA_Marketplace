import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter/rendering.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../core/providers/core_providers.dart';
import '../features/messaging/inbox_realtime.dart';
import '../features/messaging/messaging_data.dart';

/// Coquille principale : barre de navigation inférieure à 5 onglets.
class MainShell extends ConsumerStatefulWidget {
  const MainShell({super.key, required this.child, required this.location});

  final Widget child;
  final String location;

  static const _tabs = ['/home', '/search', '/orders', '/wishlist', '/account'];

  @override
  ConsumerState<MainShell> createState() => _MainShellState();
}

class _MainShellState extends ConsumerState<MainShell> {
  final _inbox = InboxRealtime();
  Timer? _poll;

  /// Barre visible ? On la replie quand l'utilisateur fait défiler le contenu
  /// vers le bas (geste de balayage vers le HAUT, qui révèle la suite) pour lui
  /// laisser toute la hauteur d'écran, et on la ré-affiche dès qu'il revient
  /// en arrière. Réinitialisée à « visible » à chaque changement d'onglet.
  bool _navVisible = true;

  int get _index {
    final i = MainShell._tabs.indexWhere((t) => widget.location.startsWith(t));
    return i < 0 ? 0 : i;
  }

  @override
  void didUpdateWidget(covariant MainShell oldWidget) {
    super.didUpdateWidget(oldWidget);
    // On change d'onglet : la barre doit réapparaître (le nouvel écran repart en
    // haut, il serait déroutant d'y arriver sans navigation).
    if (oldWidget.location != widget.location && !_navVisible) {
      _navVisible = true;
    }
  }

  /// Traduit un mouvement de défilement en visibilité de la barre.
  /// `reverse` = contenu qui monte (on plonge dans la page) → on cache.
  /// `forward` = contenu qui redescend (on remonte) → on montre.
  bool _onScroll(UserScrollNotification n) {
    // Ignorer les défilements horizontaux (carrousels) : seul l'axe vertical
    // pilote la barre.
    if (n.metrics.axis != Axis.vertical) return false;

    if (n.direction == ScrollDirection.reverse && _navVisible) {
      setState(() => _navVisible = false);
    } else if (n.direction == ScrollDirection.forward && !_navVisible) {
      setState(() => _navVisible = true);
    }
    return false; // laisser la notification poursuivre sa route
  }

  @override
  void initState() {
    super.initState();
    // Badge de non-lus en temps réel via SignalR (groupe utilisateur).
    _initInbox();
    // Repli : on rafraîchit les conversations toutes les 30 s si le WebSocket
    // n'est pas disponible, pour garder le badge à jour.
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
    return Scaffold(
      body: NotificationListener<UserScrollNotification>(
        onNotification: _onScroll,
        child: widget.child,
      ),
      // Repli/dépli animé : `AnimatedAlign` fait varier le facteur de hauteur de
      // 1 à 0, ce qui rétrécit la zone réservée à la barre (le corps s'étend en
      // douceur), pendant que `ClipRect` masque le débordement le temps de
      // l'animation. La barre garde sa hauteur intrinsèque (et sa marge de zone
      // sûre) — on ne fait que la révéler ou la cacher.
      bottomNavigationBar: ClipRect(
        child: AnimatedAlign(
          duration: const Duration(milliseconds: 220),
          curve: Curves.easeInOut,
          alignment: Alignment.topCenter,
          heightFactor: _navVisible ? 1 : 0,
          // ───────────────────────────────────────────────────────────────────
          // BRIDAGE DE L'AGRANDISSEMENT DE POLICE SUR LA BARRE UNIQUEMENT.
          //
          // Les libellés des 5 onglets se partagent la largeur de l'écran : chacun
          // dispose d'un peu plus de 80 px. Dès que l'utilisateur augmente la taille
          // du texte dans les réglages Android, « Commandes » et « Rechercher » n'y
          // tiennent plus et passent sur DEUX lignes, ce qui déforme toute la barre
          // (constaté sur Galaxy S25).
          //
          // Raccourcir les libellés ne réglerait rien : le seuil serait simplement
          // repoussé. On plafonne donc l'échelle POUR CETTE BARRE SEULEMENT — le
          // reste de l'application continue d'honorer intégralement le réglage
          // d'accessibilité, là où le texte peut réellement s'étendre.
          // ───────────────────────────────────────────────────────────────────
          child: MediaQuery.withClampedTextScaling(
            maxScaleFactor: 1.0,
            // Fond, teinte et taille des libellés : voir `AppTheme` — un seul
            // réglage, pour ne pas avoir deux sources de vérité concurrentes.
            child: NavigationBar(
            selectedIndex: _index,
            onDestinationSelected: (i) => context.go(MainShell._tabs[i]),
            destinations: [
              const NavigationDestination(icon: Icon(Icons.home_outlined), selectedIcon: Icon(Icons.home), label: 'Accueil'),
              const NavigationDestination(icon: Icon(Icons.explore_outlined), selectedIcon: Icon(Icons.explore), label: 'Explorer'),
              const NavigationDestination(icon: Icon(Icons.receipt_long_outlined), selectedIcon: Icon(Icons.receipt_long), label: 'Commandes'),
              const NavigationDestination(icon: Icon(Icons.favorite_border_rounded), selectedIcon: Icon(Icons.favorite_rounded), label: 'Favoris'),
              _accountDestination(),
            ],
            ),
          ),
        ),
      ),
    );
  }

  /// Onglet « Compte » porteur du badge de messages non lus (la messagerie est
  /// accessible depuis cet onglet).
  NavigationDestination _accountDestination() {
    final unread = ref.watch(unreadCountProvider);
    return NavigationDestination(
      icon: Badge(
        isLabelVisible: unread > 0,
        label: Text('$unread'),
        child: const Icon(Icons.person_outline),
      ),
      selectedIcon: Badge(
        isLabelVisible: unread > 0,
        label: Text('$unread'),
        child: const Icon(Icons.person),
      ),
      label: 'Compte',
    );
  }
}
