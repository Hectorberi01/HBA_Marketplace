import 'dart:async';
import 'dart:ui';

import 'package:firebase_core/firebase_core.dart';
import 'package:firebase_crashlytics/firebase_crashlytics.dart';
import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:intl/date_symbol_data_local.dart';

import 'src/app.dart';
import 'src/core/config/app_config.dart';

Future<void> main() async {
  // runZonedGuarded englobe TOUT : les erreurs asynchrones (un Future qui
  // échoue sans catch) n'atteignent pas FlutterError.onError et seraient
  // perdues sans cette zone.
  runZonedGuarded(() async {
    WidgetsFlutterBinding.ensureInitialized();
    await initializeDateFormatting('fr');

    await _initFirebase();

    // Filet de sécurité : en release, une exception au build d'un widget affiche
    // sinon un ÉCRAN NOIR opaque, sans le moindre indice. On le remplace par un
    // écran lisible qui dit quoi faire.
    ErrorWidget.builder = (details) => _AppErrorScreen(details: details);

    // ───────────────────────────────────────────────────────────────────────────
    // GARDE-FOU DE BUILD : un release sans API_BASE_URL ne DOIT pas démarrer.
    //
    // L'URL par défaut est le STAGING. Sans cette vérification, un build de release
    // qui oublie `--dart-define` partirait sur le serveur de test — en silence.
    //
    // Pour une app VENDEUR, c'est particulièrement vicieux : le vendeur ne voit
    // aucune de ses vraies commandes, déclare ses expéditions dans le vide, et
    // conclut que la plateforme est en panne. Il n'a aucun moyen de deviner que son
    // application parle au mauvais serveur.
    //
    // On échoue donc ici, au premier lancement, quand cela ne coûte encore rien.
    // ───────────────────────────────────────────────────────────────────────────
    // Deux cas de mauvaise configuration bloqués ici :
    //   1. Aucune API_BASE_URL fournie → l'app partirait sur le repli STAGING.
    //   2. Une URL fournie mais qui vise un serveur de TEST (staging/local) sans
    //      l'opt-in ALLOW_STAGING_RELEASE → un build « prod » servirait des données
    //      de test aux vrais vendeurs.
    if (kReleaseMode &&
        (!AppConfig.isExplicitlyConfigured || AppConfig.isForbiddenReleaseTarget)) {
      runApp(const _MisconfiguredBuildApp());
      return;
    }

    // Bords à bords : Android 15 l'impose aux applications ciblant l'API 35. Le
    // déclarer explicitement permet de rendre les barres transparentes et d'en
    // piloter les icônes ; ne rien déclarer laissait le bas des écrans caché
    // derrière les boutons du système.
    await SystemChrome.setEnabledSystemUIMode(SystemUiMode.edgeToEdge);

    runApp(const ProviderScope(child: HbaExpressProApp()));
  }, (error, stack) {
    FirebaseCrashlytics.instance.recordError(error, stack, fatal: true);
  });
}

/// Firebase + Crashlytics.
///
/// TOLÉRANT : si l'initialisation échoue (fichier de configuration absent,
/// service indisponible), l'app démarre quand même — sans push ni rapport de
/// crash. Refuser de lancer une app de gestion parce que la télémétrie ne
/// répond pas serait absurde.
Future<void> _initFirebase() async {
  try {
    await Firebase.initializeApp();

    // En debug, on ne pollue pas la console Crashlytics avec les plantages du
    // développeur : les rapports ne partent qu'en release.
    await FirebaseCrashlytics.instance.setCrashlyticsCollectionEnabled(kReleaseMode);

    // Erreurs du framework Flutter (build, layout, paint).
    FlutterError.onError = FirebaseCrashlytics.instance.recordFlutterFatalError;

    // Erreurs de la plateforme (canaux natifs, isolats).
    PlatformDispatcher.instance.onError = (error, stack) {
      FirebaseCrashlytics.instance.recordError(error, stack, fatal: true);
      return true;
    };
  } catch (e) {
    debugPrint('Firebase indisponible : $e');
  }
}

class _AppErrorScreen extends StatelessWidget {
  const _AppErrorScreen({required this.details});
  final FlutterErrorDetails details;

  @override
  Widget build(BuildContext context) {
    // Rendu autonome : cet écran doit pouvoir s'afficher même hors MaterialApp.
    return Directionality(
      textDirection: TextDirection.ltr,
      child: Container(
        color: Colors.white,
        alignment: Alignment.center,
        padding: const EdgeInsets.all(28),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            const Icon(Icons.error_outline, color: Color(0xFFE5484D), size: 52),
            const SizedBox(height: 16),
            const Text(
              'Oups, un souci est survenu',
              textAlign: TextAlign.center,
              style: TextStyle(fontSize: 18, fontWeight: FontWeight.w800, color: Color(0xFF18211C)),
            ),
            const SizedBox(height: 8),
            const Text(
              'Revenez en arrière et réessayez.',
              textAlign: TextAlign.center,
              style: TextStyle(color: Color(0xFF7A8580)),
            ),
            const SizedBox(height: 16),
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
    // En release, une seule ligne : la stack complète n'aiderait pas le vendeur
    // et pourrait exposer des détails internes.
    return kReleaseMode ? s.split('\n').first : s;
  }
}


/// Écran affiché quand un build de RELEASE a été produit sans URL d'API explicite.
///
/// L'application ne démarre pas. C'est délibéré : elle FONCTIONNERAIT, mais contre le
/// serveur de test — et une app vendeur qui affiche zéro commande sans rien expliquer
/// est bien pire qu'une app qui refuse de se lancer en disant pourquoi.
class _MisconfiguredBuildApp extends StatelessWidget {
  const _MisconfiguredBuildApp();

  @override
  Widget build(BuildContext context) {
    // Arbre entièrement constant : cet écran n'a aucun état et ne change jamais.
    // Le `const` sur la racine rend tout le sous-arbre constant, ce qui rend
    // superflus ceux qui étaient posés plus bas.
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
                  'Cette version pointe sur un serveur de TEST (staging) — soit '
                  'API_BASE_URL est absente, soit elle vise le staging.\n\n'
                  'Elle ne doit pas être distribuée aux vendeurs.',
                  textAlign: TextAlign.center,
                  style: TextStyle(color: Colors.white70, fontSize: 14, height: 1.5),
                ),
                SizedBox(height: 22),
                Text(
                  'PROD :\n'
                  'flutter build … --dart-define=API_BASE_URL=https://<prod>\n\n'
                  'TEST staging (appareil réel) : ajouter\n'
                  '--dart-define=ALLOW_STAGING_RELEASE=true',
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
