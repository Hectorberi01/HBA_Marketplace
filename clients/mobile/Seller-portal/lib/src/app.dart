import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import 'package:hba_express_pro/l10n/app_localizations.dart';
import 'core/push/push_service.dart';
import 'core/router/app_router.dart';
import 'core/theme/app_theme.dart';
import 'features/auth/application/auth_controller.dart';
import 'features/settings/settings_data.dart';

class HbaExpressProApp extends ConsumerStatefulWidget {
  const HbaExpressProApp({super.key});

  @override
  ConsumerState<HbaExpressProApp> createState() => _HbaExpressProAppState();
}

class _HbaExpressProAppState extends ConsumerState<HbaExpressProApp> {
  @override
  Widget build(BuildContext context) {
    final router = ref.watch(routerProvider);

    // Notification tapée : on navigue une fois le routeur prêt.
    //
    // La destination est déposée par le service push, qui n'a pas de contexte.
    // On ne l'honore QUE si la session est ouverte : au démarrage à froid, le
    // message arrive avant la restauration de session — naviguer tout de suite
    // enverrait le vendeur sur un écran protégé, dont le routeur le renverrait
    // aussitôt vers la connexion, en perdant l'intention au passage.
    ref.listen<String?>(pendingPushRouteProvider, (_, route) {
      if (route == null) return;
      if (ref.read(authControllerProvider) != AuthStatus.authenticated) return;

      router.go(route);
      ref.read(pendingPushRouteProvider.notifier).state = null;
    });

    // Session ouverte alors qu'une destination attendait (cas du démarrage à
    // froid) : on la rejoue maintenant.
    ref.listen<AuthStatus>(authControllerProvider, (_, status) {
      if (status != AuthStatus.authenticated) return;

      final pending = ref.read(pendingPushRouteProvider);
      if (pending == null) return;

      router.go(pending);
      ref.read(pendingPushRouteProvider.notifier).state = null;
    });

    final themeMode = ref.watch(themeModeProvider);
    final locale = ref.watch(localeProvider); // null = suit la langue du téléphone

    return MaterialApp.router(
      title: 'HbaExpress PRO',
      debugShowCheckedModeBanner: false,
      theme: AppTheme.light(),
      darkTheme: AppTheme.dark(),
      themeMode: themeMode,
      // Internationalisation : délégués générés + langues supportées (fr, en).
      locale: locale,
      localizationsDelegates: AppLocalizations.localizationsDelegates,
      supportedLocales: AppLocalizations.supportedLocales,
      routerConfig: router,
      builder: (context, child) {
        // En bords à bords, les barres système sont transparentes : c'est
        // l'application qui décide de la couleur de leurs ICÔNES. Sans ça, un
        // écran clair gardait des icônes claires, donc invisibles — et la zone
        // système paraissait sombre sous une interface claire.
        //
        // `Theme.of(context)` et non le mode brut : on suit le thème RÉELLEMENT
        // appliqué, y compris quand il vaut « système ».
        final isDark = Theme.of(context).brightness == Brightness.dark;
        return AnnotatedRegion<SystemUiOverlayStyle>(
          value: SystemUiOverlayStyle(
            statusBarColor: Colors.transparent,
            statusBarIconBrightness: isDark ? Brightness.light : Brightness.dark,
            statusBarBrightness: isDark ? Brightness.dark : Brightness.light,
            systemNavigationBarColor: Colors.transparent,
            systemNavigationBarDividerColor: Colors.transparent,
            systemNavigationBarIconBrightness: isDark ? Brightness.light : Brightness.dark,
          ),
          child: child ?? const SizedBox.shrink(),
        );
      },
    );
  }
}
