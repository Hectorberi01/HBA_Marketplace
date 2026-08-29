import 'package:flutter/material.dart';

import '../features/auth/login_screen.dart';
import '../features/home/driver_home_screen.dart';
import '../features/onboarding/onboarding_screen.dart';
import 'app_theme.dart';

enum AppStage { onboarding, login, home }

class DriverApp extends StatefulWidget {
  const DriverApp({super.key});

  @override
  State<DriverApp> createState() => _DriverAppState();
}

class _DriverAppState extends State<DriverApp> {
  AppStage _stage = AppStage.onboarding;

  @override
  Widget build(BuildContext context) {
    return MaterialApp(
      title: 'HBA Driver',
      debugShowCheckedModeBanner: false,
      theme: AppTheme.light(),
      home: switch (_stage) {
        AppStage.onboarding => OnboardingScreen(
          onFinished: () => setState(() => _stage = AppStage.login),
        ),
        AppStage.login => LoginScreen(
          onSignedIn: () => setState(() => _stage = AppStage.home),
        ),
        AppStage.home => const DriverHomeScreen(),
      },
    );
  }
}
