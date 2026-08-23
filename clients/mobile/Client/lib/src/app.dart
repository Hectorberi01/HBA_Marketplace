import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import 'core/deeplink/deep_link_service.dart';
import 'core/push/push_service.dart';
import 'core/router/app_router.dart';
import 'core/theme/app_theme.dart';
import 'core/theme/theme_mode_controller.dart';
import 'features/auth/application/auth_controller.dart';

class MarketplaceApp extends ConsumerStatefulWidget {
  const MarketplaceApp({super.key});

  @override
  ConsumerState<MarketplaceApp> createState() => _MarketplaceAppState();
}

class _MarketplaceAppState extends ConsumerState<MarketplaceApp> {
  @override
  void initState() {
    super.initState();
    // Écoute des liens universels dès le lancement (y compris démarrage à froid),
    // sans attendre l'authentification : la destination est mise en attente et
    // rejouée après connexion si besoin.
    ref.read(deepLinkServiceProvider).start();
  }

  @override
  Widget build(BuildContext context) {
    final router = ref.watch(routerProvider);

    // ─────────────────────────────────────────────────────────────────────────
    // NAVIGATION DEPUIS UNE NOTIFICATION TAPÉE.
    //
    // Le service push n'a pas de contexte de navigation : il dépose une intention
    // (`pendingPushRouteProvider`), et c'est ici qu'on la consomme.
    //
    // La condition sur la session n'est pas une précaution de style. Au démarrage
    // à FROID — l'app est tuée, l'utilisateur tape la notification, l'app se lance —
    // le message arrive AVANT que la session ne soit restaurée. Naviguer tout de
    // suite enverrait le client vers /order/123 alors qu'il n'est pas encore
    // authentifié : le routeur le renverrait sur /login, et sa commande serait
    // perdue en route. Il aurait tapé la notification pour rien.
    //
    // On attend donc que la session soit là ; le second `listen` rejoue alors
    // l'intention mise de côté.
    // ─────────────────────────────────────────────────────────────────────────
    // ─────────────────────────────────────────────────────────────────────────
    // UNE DESTINATION PUBLIQUE NE DOIT PAS ATTENDRE LA SESSION.
    //
    // La condition ci-dessus valait quand tout l'application exigeait un compte.
    // Depuis l'ouverture du catalogue aux visiteurs (App Store 5.1.1(v)),
    // « /product/… » et « /shop/… » sont publiques — et ce sont précisément les
    // routes des LIENS PARTAGÉS.
    //
    // Résultat : un nouvel utilisateur qui tapait le lien envoyé par un ami
    // atterrissait sur l'accueil, produit perdu. Le canal d'acquisition principal
    // était cassé, et il l'était PAR la correction Apple.
    //
    // On distingue donc les deux cas : une destination publique part tout de
    // suite, une destination liée au compte attend la session comme avant.
    // ─────────────────────────────────────────────────────────────────────────
    ref.listen<String?>(pendingPushRouteProvider, (_, route) {
      if (route == null) return;

      final authenticated = ref.read(authControllerProvider) == AuthStatus.authenticated;
      if (!authenticated && !isPublicRoute(route)) return;

      router.go(route);
      ref.read(pendingPushRouteProvider.notifier).state = null;
    });

    // La session s'ouvre alors qu'une destination attendait : c'est le cas du
    // démarrage à froid décrit ci-dessus. On la rejoue maintenant.
    ref.listen<AuthStatus>(authControllerProvider, (_, status) {
      if (status != AuthStatus.authenticated) return;

      final pending = ref.read(pendingPushRouteProvider);
      if (pending == null) return;

      router.go(pending);
      ref.read(pendingPushRouteProvider.notifier).state = null;
    });

    // Mode de thème choisi par l'utilisateur (ou système). On aligne le token
    // global AppTheme.brightness AVANT de construire l'arbre, pour que les
    // couleurs de palette (bg, ink, surface…) suivent le même mode que Material.
    final themeMode = ref.watch(themeModeProvider);

    // `MediaQuery.platformBrightnessOf(context)` et NON
    // `WidgetsBinding.instance.platformDispatcher.platformBrightness`.
    //
    // Le second est une simple LECTURE : il n'inscrit aucune dépendance. En mode
    // « système », basculer le téléphone en sombre ne reconstruisait donc pas ce
    // widget — `AppTheme.brightness` restait sur l'ancienne valeur pendant que
    // Material, lui, changeait de thème. D'où des cartes claires sur fond sombre,
    // jusqu'à ce qu'un autre événement force un rebuild.
    final platformBrightness = MediaQuery.platformBrightnessOf(context);

    final brightness = switch (themeMode) {
      ThemeMode.light => Brightness.light,
      ThemeMode.dark => Brightness.dark,
      ThemeMode.system => platformBrightness,
    };
    AppTheme.brightness = brightness;

    final isDark = brightness == Brightness.dark;

    return MaterialApp.router(
      title: 'HbaExpress',
      debugShowCheckedModeBanner: false,
      theme: AppTheme.light(),
      darkTheme: AppTheme.dark(),
      themeMode: themeMode,
      routerConfig: router,
      builder: (context, child) {
        // ─────────────────────────────────────────────────────────────────────
        // LES BARRES SYSTÈME SUIVENT LE THÈME, ET LE SOUS-ARBRE EST RECONSTRUIT.
        //
        // Deux corrections dans le même endroit, parce qu'elles ont la même cause.
        //
        // 1. `AnnotatedRegion` : en bords à bords, les barres d'état et de
        //    navigation sont transparentes et c'est l'application qui décide de la
        //    couleur de leurs ICÔNES. Sans ça, un écran clair gardait des icônes
        //    claires — invisibles — et la zone système paraissait sombre.
        //
        // 2. `KeyedSubtree` clé sur la luminosité : `AppTheme` expose ses couleurs
        //    via un champ STATIQUE. Un statique ne notifie personne : les widgets
        //    déjà construits gardaient les couleurs de l'ancien mode. Changer la
        //    clé force la reconstruction complète du sous-arbre, donc une bascule
        //    franche plutôt qu'un panachage des deux palettes.
        //
        //    C'est un pansement, et il est assumé : la vraie correction serait de
        //    lire les couleurs depuis `Theme.of(context)` partout, ce qui touche
        //    des dizaines d'écrans. Ici, le coût est un rebuild par changement de
        //    mode — quelques fois par jour au plus.
        // ─────────────────────────────────────────────────────────────────────
        return AnnotatedRegion<SystemUiOverlayStyle>(
          value: SystemUiOverlayStyle(
            statusBarColor: Colors.transparent,
            statusBarIconBrightness: isDark ? Brightness.light : Brightness.dark,
            statusBarBrightness: isDark ? Brightness.dark : Brightness.light,
            systemNavigationBarColor: Colors.transparent,
            systemNavigationBarDividerColor: Colors.transparent,
            systemNavigationBarIconBrightness: isDark ? Brightness.light : Brightness.dark,
          ),
          child: KeyedSubtree(key: ValueKey(brightness), child: child ?? const SizedBox.shrink()),
        );
      },
    );
  }
}
