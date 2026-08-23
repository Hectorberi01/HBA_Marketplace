import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import 'src/app.dart';

void main() {
  WidgetsFlutterBinding.ensureInitialized();

  // Portrait seulement : on tient son téléphone d'une main, souvent sur un
  // support de guidon. Le paysage n'apporte rien et casse toutes les mises en
  // page verticales de la maquette.
  SystemChrome.setPreferredOrientations([
    DeviceOrientation.portraitUp,
    DeviceOrientation.portraitDown,
  ]);

  runApp(const ProviderScope(child: HbaDriverApp()));
}
