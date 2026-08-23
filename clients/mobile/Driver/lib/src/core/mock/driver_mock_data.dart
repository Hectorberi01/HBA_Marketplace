/// ═════════════════════════════════════════════════════════════════════════════
/// DONNÉES FIGÉES DES MAQUETTES HBA DRIVER.
///
/// LES VALEURS SONT CELLES DE LA MAQUETTE, AU FRANC PRÈS.
///
/// 15 500 F CFA, 8 livraisons, 4 h 20, 4,9 ★, Fidjrossè : reprendre les mêmes
/// chiffres permet de poser l'écran à côté de la capture et de voir ce qui
/// diverge — un alignement, une graisse. Avec des données aléatoires, on ne
/// valide plus que « ça ressemble ».
///
/// CE FICHIER DISPARAÎTRA. AUCUNE RÈGLE MÉTIER ICI.
///
/// Les totaux sont écrits, pas dérivés. Le jour où le service delivery répondra,
/// seule la source change.
///
/// LE CONTEXTE EST BÉNINOIS, ET C'EST DÉLIBÉRÉ.
///
/// +229, Cotonou, Fidjrossè, Carrefour Aïbatin. Les maquettes du portail vendeur
/// employaient des repères ivoiriens (+225, Abidjan, Cocody) — écart déjà
/// signalé. Ici la maquette est juste : on ne la corrige pas, on la suit.
/// ═════════════════════════════════════════════════════════════════════════════
library;

/// État d'une pièce du dossier livreur.
///
/// « EXPIRE BIENTÔT » EST UN ÉTAT À PART, PAS UN « VÉRIFIÉ » DÉCORÉ.
///
/// Une assurance qui expire dans douze jours est aujourd'hui valide et sera
/// bientôt bloquante. La ranger avec les pièces vérifiées ferait disparaître
/// l'échéance ; la ranger avec les refusées empêcherait de rouler alors que rien
/// ne l'interdit encore.
enum DriverDocStatus {
  verified('Vérifié'),
  expiring('Expire bientôt'),
  pending('En attente'),
  rejected('Refusé');

  const DriverDocStatus(this.label);

  final String label;
}

class DriverDocument {
  const DriverDocument({
    required this.name,
    required this.status,
    this.detail,
    this.feminine = false,
  });

  final String name;
  final DriverDocStatus status;

  /// Précision qui remplace le libellé générique : « Expire dans 12 jours ».
  final String? detail;

  /// L'ACCORD DU PARTICIPE SUIT LE GENRE DU DOCUMENT.
  ///
  /// « Permis vérifié », mais « Carte d'identité vérifiée ». La maquette écrit
  /// les deux formes. Une chaîne unique aurait donné « Carte d'identité
  /// vérifié » — la faute que tout le monde remarque et que personne ne corrige.
  final bool feminine;

  /// Le texte réellement affiché sous le nom.
  String get statusLabel {
    if (detail != null) return detail!;
    if (!feminine) return status.label;
    return switch (status) {
      DriverDocStatus.verified => 'Vérifiée',
      DriverDocStatus.expiring => 'Expire bientôt',
      DriverDocStatus.pending => 'En attente',
      DriverDocStatus.rejected => 'Refusée',
    };
  }
}

/// Une étape du parcours d'inscription.
class OnboardingStep {
  const OnboardingStep(this.label);

  final String label;
}

class DriverMockData {
  const DriverMockData._();

  static const String driverName = 'Hector';
  static const String phone = '+229 97 44 12 08';

  // ── Inscription ───────────────────────────────────────────────────────────

  /// SEPT ÉTAPES, ET LA MAQUETTE LES NOMME TOUTES.
  ///
  /// La liste « Étapes restantes » n'est pas décorative : elle répond à la seule
  /// question que se pose quelqu'un au milieu d'un formulaire long — combien
  /// encore. Sans elle, l'abandon se décide à la troisième.
  static const List<OnboardingStep> onboardingSteps = [
    OnboardingStep('Informations personnelles'),
    OnboardingStep('Téléphone'),
    OnboardingStep('Adresse'),
    OnboardingStep('Moyen de transport'),
    OnboardingStep('Documents'),
    OnboardingStep('Photo'),
    OnboardingStep('Vérification'),
  ];

  // ── Dossier ───────────────────────────────────────────────────────────────

  static const List<DriverDocument> documents = [
    DriverDocument(name: 'Permis de conduire', status: DriverDocStatus.verified),
    DriverDocument(
      name: 'Carte d\'identité',
      status: DriverDocStatus.verified,
      feminine: true,
    ),
    DriverDocument(
      name: 'Assurance',
      status: DriverDocStatus.expiring,
      detail: 'Expire dans 12 jours',
      feminine: true,
    ),
    DriverDocument(
      name: 'Carte grise',
      status: DriverDocStatus.pending,
      feminine: true,
    ),
  ];

  static const String verificationDelay =
      'Votre dossier est en cours d\'examen par l\'équipe HBA. '
      'Vous serez notifié dès validation, généralement sous 48 h.';

  // ── Journée en cours ──────────────────────────────────────────────────────

  static const int earningsToday = 15500;
  static const int deliveriesToday = 8;
  static const String onlineTime = '4 h 20';
  static const String rating = '4,9';

  static const String zoneName = 'Fidjrossè';
  static const String zoneDemand = 'forte demande';

  // ── Bonus du jour ─────────────────────────────────────────────────────────

  /// LES DEUX NOMBRES DOIVENT S'ACCORDER AVEC `deliveriesToday`.
  ///
  /// La maquette affiche « 8 sur 10 · plus que 2 courses » et « 8 » livraisons
  /// dans les statistiques. Le « plus que 2 » est donc CALCULÉ, jamais écrit :
  /// une valeur figée aurait promis deux courses restantes après la neuvième.
  static const int bonusTarget = 10;
  static const int bonusAmount = 2000;

  static int get bonusRemaining {
    final left = bonusTarget - deliveriesToday;
    return left < 0 ? 0 : left;
  }

  static double get bonusProgress =>
      (deliveriesToday / bonusTarget).clamp(0.0, 1.0);
}
