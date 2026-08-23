import 'dart:async';
import 'dart:ui';

import 'package:firebase_core/firebase_core.dart';
import 'package:firebase_crashlytics/firebase_crashlytics.dart';
import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:google_fonts/google_fonts.dart';
import 'package:intl/date_symbol_data_local.dart';

import 'src/app.dart';
import 'src/core/config/app_config.dart';
import 'src/core/theme/app_theme.dart';

Future<void> main() async {
  // runZonedGuarded englobe TOUT : les erreurs asynchrones (un Future qui échoue
  // sans catch) n'atteignent pas FlutterError.onError et seraient perdues sans
  // cette zone — on les remonte à Crashlytics.
  runZonedGuarded(() async {
    WidgetsFlutterBinding.ensureInitialized();

    // ───────────────────────────────────────────────────────────────────────────
    // LA TYPOGRAPHIE NE SE TÉLÉCHARGE PLUS AU LANCEMENT.
    //
    // Par défaut, `google_fonts` va chercher Plus Jakarta Sans sur
    // fonts.gstatic.com au premier rendu de texte. Trois conséquences, toutes
    // constatées en production (Crashlytics, 1er août 2026) :
    //
    //   • quand le réseau ne répond pas, le paquet LÈVE une exception, remontée
    //     en plantage FATAL — sur un réseau mobile béninois, ce n'est pas un cas
    //     rare mais l'ordinaire ;
    //   • le premier affichage attend une requête HTTP ;
    //   • l'application expédie l'adresse IP de chaque utilisateur à Google au
    //     démarrage, ce que la politique de confidentialité ne mentionne pas.
    //
    // Les fichiers de police sont désormais EMBARQUÉS (assets/google_fonts/).
    // Ce réglage interdit tout repli réseau : si un fichier manque, l'anomalie
    // apparaît au développement et non chez l'utilisateur.
    // ───────────────────────────────────────────────────────────────────────────
    GoogleFonts.config.allowRuntimeFetching = false;

    await initializeDateFormatting('fr');

    await _initCrashReporting();

    // Filet de sécurité : en release, une exception au build d'un widget affiche
    // sinon un ÉCRAN NOIR opaque. On le remplace par un écran d'erreur lisible.
    ErrorWidget.builder = (FlutterErrorDetails details) => _AppErrorScreen(details: details);

    // ───────────────────────────────────────────────────────────────────────────
    // GARDE-FOU DE BUILD : un release sans API_BASE_URL ne DOIT pas démarrer.
    // L'URL par défaut est le STAGING ; un build release qui l'oublie partirait
    // sur le serveur de test — en silence. On échoue ici, au premier lancement.
    //
    // Deux cas bloqués, et non plus un seul :
    //   1. aucune URL fournie → l'app partirait sur le repli STAGING ;
    //   2. une URL fournie mais visant un serveur de TEST, sans l'opt-in
    //      ALLOW_STAGING_RELEASE. Fournir une URL ne prouve pas qu'on a fourni
    //      la bonne — et les scripts produisent le binaire « staging » avec
    //      l'identifiant de PRODUCTION, donc rien ne les distingue.
    // ───────────────────────────────────────────────────────────────────────────
    if (kReleaseMode &&
        (!AppConfig.isExplicitlyConfigured || AppConfig.isForbiddenReleaseTarget)) {
      runApp(const _MisconfiguredBuildApp());
      return;
    }

    // ───────────────────────────────────────────────────────────────────────────
    // BORDS À BORDS, DÉCLARÉ PLUTÔT QUE SUBI.
    //
    // Android 15 impose le mode bords à bords à toute application ciblant l'API 35 :
    // le contenu passe SOUS la barre de navigation système, qu'on l'ait demandé ou
    // non. Ne rien déclarer laissait donc le bas des écrans caché derrière les trois
    // boutons du système — et la zone système gardait une couleur qui ne suivait pas
    // le thème, d'où la bande sombre au bas d'un écran clair.
    //
    // On l'assume explicitement : les barres deviennent transparentes, et chaque
    // écran doit tenir compte de `viewPadding`. C'est plus honnête que de croire
    // qu'on contrôle encore la hauteur utile.
    // ───────────────────────────────────────────────────────────────────────────
    await SystemChrome.setEnabledSystemUIMode(SystemUiMode.edgeToEdge);

    runApp(const ProviderScope(child: MarketplaceApp()));
  }, (error, stack) {
    FirebaseCrashlytics.instance.recordError(error, stack, fatal: true);
  });
}

/// Initialise Firebase + Crashlytics.
///
/// TOLÉRANT : si l'initialisation échoue (config absente, service indisponible),
/// l'app démarre quand même — sans rapport de crash. Refuser de lancer l'app
/// parce que la télémétrie ne répond pas serait absurde.
Future<void> _initCrashReporting() async {
  try {
    await Firebase.initializeApp();

    // En debug, on ne pollue pas Crashlytics avec les plantages du développeur :
    // les rapports ne partent qu'en release.
    await FirebaseCrashlytics.instance.setCrashlyticsCollectionEnabled(kReleaseMode);

    // Erreurs du framework Flutter (build, layout, paint).
    FlutterError.onError = FirebaseCrashlytics.instance.recordFlutterFatalError;

    // Erreurs de la plateforme (canaux natifs, isolats).
    PlatformDispatcher.instance.onError = (error, stack) {
      FirebaseCrashlytics.instance.recordError(error, stack, fatal: true);
      return true;
    };
  } catch (e) {
    debugPrint('Crashlytics indisponible : $e');
  }
}

/// Écran affiché quand un build de RELEASE a été produit sans URL d'API explicite.
///
/// L'application ne démarre pas. C'est délibéré : elle fonctionnerait, mais contre le
/// mauvais serveur — et c'est bien pire qu'une app qui ne démarre pas.
class _MisconfiguredBuildApp extends StatelessWidget {
  const _MisconfiguredBuildApp();

  @override
  Widget build(BuildContext context) {
    return const MaterialApp(
      debugShowCheckedModeBanner: false,
      home: Scaffold(
        backgroundColor: Color(0xFF7F1D1D),
        body: Center(
          child: Padding(
            padding: EdgeInsets.all(28),
            child: Column(
              mainAxisAlignment: MainAxisAlignment.center,
              children: [
                Icon(Icons.error_outline, color: Colors.white, size: 56),
                SizedBox(height: 20),
                Text(
                  'Build mal configuré',
                  textAlign: TextAlign.center,
                  style: TextStyle(color: Colors.white, fontSize: 22, fontWeight: FontWeight.w800),
                ),
                SizedBox(height: 14),
                Text(
                  'Cette version a été compilée sans API_BASE_URL et pointerait sur le '
                  'serveur de test.\n\nElle ne doit pas être distribuée.',
                  textAlign: TextAlign.center,
                  style: TextStyle(color: Colors.white70, fontSize: 14, height: 1.5),
                ),
                SizedBox(height: 22),
                Text(
                  'flutter build appbundle \\\n  --dart-define=API_BASE_URL=https://…',
                  textAlign: TextAlign.center,
                  style: TextStyle(color: Colors.white, fontSize: 12, fontFamily: 'monospace'),
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}

/// Écran affiché à la place de l'« écran noir » quand un widget plante.
class _AppErrorScreen extends StatelessWidget {
  const _AppErrorScreen({required this.details});
  final FlutterErrorDetails details;

  @override
  Widget build(BuildContext context) {
    // Rendu autonome (peut s'afficher hors d'un MaterialApp).
    return Directionality(
      textDirection: TextDirection.ltr,
      child: Container(
        color: AppTheme.surface,
        alignment: Alignment.center,
        padding: const EdgeInsets.all(28),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            const Icon(Icons.error_outline, color: Color(0xFFD64541), size: 52),
            const SizedBox(height: 16),
            const Text(
              'Oups, un souci est survenu',
              textAlign: TextAlign.center,
              style: TextStyle(fontSize: 18, fontWeight: FontWeight.w800, color: Color(0xFF0E1A15)),
            ),
            const SizedBox(height: 8),
            const Text(
              'Reviens en arrière et réessaie.',
              textAlign: TextAlign.center,
              style: TextStyle(color: Color(0xFF7A8580)),
            ),
            const SizedBox(height: 16),
            // Première ligne de l'erreur, utile pour diagnostiquer un build beta.
            Text(
              _shortMessage(details),
              textAlign: TextAlign.center,
              maxLines: 3,
              overflow: TextOverflow.ellipsis,
              style: const TextStyle(fontSize: 12, color: Color(0xFFA0A8A4)),
            ),
          ],
        ),
      ),
    );
  }

  String _shortMessage(FlutterErrorDetails d) {
    final s = d.exceptionAsString();
    final firstLine = s.split('\n').first;
    return kReleaseMode ? firstLine : s;
  }
}
