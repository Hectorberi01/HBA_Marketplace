/// Profil, véhicule, documents et notifications.
library;

import 'driver_mock_data.dart';
import 'earnings_mock_data.dart';

/// Une note détaillée du profil.
class RatingFacet {
  const RatingFacet(this.label, this.score);

  final String label;

  /// Entre 0 et 1.
  final double score;
}

enum VehicleKind {
  moto('Moto'),
  voiture('Voiture'),
  velo('Vélo'),
  tricycle('Tricycle');

  const VehicleKind(this.label);

  final String label;
}

/// Catégorie d'une notification. Détermine la couleur de la pastille.
enum NotificationKind { mission, orderReady, payment, document, bonus }

class DriverNotification {
  const DriverNotification({
    required this.title,
    required this.detail,
    required this.when,
    required this.kind,
  });

  final String title;
  final String detail;
  final String when;
  final NotificationKind kind;
}

class AccountMockData {
  const AccountMockData._();

  static const String fullName = 'Hector Adjovi';
  static const String email = 'hector.a@mail.com';
  static const String phone = DriverMockData.phone;

  static const String rating = DriverMockData.rating;
  static const int ratingCount = 124;
  static const String memberSince = 'Mars 2025';
  static const int totalDeliveries = 1284;

  static const List<RatingFacet> facets = [
    RatingFacet('Rapidité', 0.96),
    RatingFacet('Communication', 0.98),
    RatingFacet('Respect du colis', 0.99),
  ];

  static const List<String> accountLinks = [
    'Mon véhicule',
    'Mes documents',
    'Mes revenus',
    'Mes évaluations',
    'Sécurité',
    'Notifications',
    'Aide',
  ];

  // ── Véhicule ──────────────────────────────────────────────────────────────

  static const VehicleKind vehicleKind = VehicleKind.moto;
  static const String vehicleModel = 'Honda PCX';

  /// FORMAT BÉNINOIS : « AB-1234-BJ ». Le suffixe BJ est le code pays.
  static const String plate = 'AB-1234-BJ';
  static const String vehicleColor = 'Noir';
  static const String vehicleYear = '2021';
  static const bool vehicleVerified = true;

  // ── Documents ─────────────────────────────────────────────────────────────

  /// Jours restants avant expiration de l'assurance.
  static const int insuranceDaysLeft = 12;

  /// LA DATE LIMITE EST CALCULÉE, PAS ÉCRITE.
  ///
  /// Vos deux écrans concordent : le 19 date la journée au « 12 mai », le 26
  /// annonce « avant le 24 mai ». 12 + 12 jours = 24. Écrire « 24 mai » en dur
  /// aurait fait mentir l'un des deux au premier changement de l'autre.
  static String get insuranceDeadline =>
      '${EarningsMockData.todayDayOfMonth + insuranceDaysLeft} '
      '${EarningsMockData.monthLabel}';

  static String get documentsWarning =>
      'Mettez à jour votre assurance avant le $insuranceDeadline '
      'pour continuer à recevoir des missions.';

  // ── Notifications ─────────────────────────────────────────────────────────

  static const List<DriverNotification> notifications = [
    DriverNotification(
      title: 'Nouvelle mission disponible',
      detail: 'Chez Mama → Akpakpa · 1 500 F',
      when: 'À l\'instant',
      kind: NotificationKind.mission,
    ),
    DriverNotification(
      title: 'Commande prête au restaurant',
      detail: '#FOOD-2058 · Chez Mama',
      when: 'Il y a 5 min',
      kind: NotificationKind.orderReady,
    ),
    DriverNotification(
      title: 'Votre paiement a été effectué',
      detail: '20 000 F CFA vers MTN MoMo',
      when: 'Hier',
      kind: NotificationKind.payment,
    ),
    DriverNotification(
      title: 'Votre document expire bientôt',
      detail: 'Assurance · dans $insuranceDaysLeft jours',
      when: 'Hier',
      kind: NotificationKind.document,
    ),
    DriverNotification(
      title: 'Bonus disponible aujourd\'hui',
      detail: '10 livraisons = +2 000 F',
      when: 'Lundi',
      kind: NotificationKind.bonus,
    ),
  ];
}
