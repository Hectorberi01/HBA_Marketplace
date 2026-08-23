import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import 'core/router/driver_router.dart';
import 'core/theme/app_theme.dart';

class HbaDriverApp extends ConsumerWidget {
  const HbaDriverApp({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) => MaterialApp.router(
        title: 'HBA Driver',
        debugShowCheckedModeBanner: false,
        theme: AppTheme.light(),
        routerConfig: ref.watch(driverRouterProvider),

        // FRANÇAIS UNIQUEMENT, ET SANS FICHIERS `.arb` POUR L'INSTANT.
        //
        // Les libellés sont en dur dans les écrans. C'est une dette assumée : les
        // extraire maintenant obligerait à nommer cent clés avant d'avoir arrêté
        // les textes, et à toutes les renommer au premier retour de maquette.
        // À faire avant la première langue supplémentaire — pas après.
        locale: const Locale('fr'),
        supportedLocales: const [Locale('fr')],
        localizationsDelegates: const [
          GlobalMaterialLocalizations.delegate,
          GlobalWidgetsLocalizations.delegate,
          GlobalCupertinoLocalizations.delegate,
        ],
      );
}
