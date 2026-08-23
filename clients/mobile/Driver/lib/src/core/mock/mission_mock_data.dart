/// ═════════════════════════════════════════════════════════════════════════════
/// LE FLUX MISSION — données figées et machine à états.
/// ═════════════════════════════════════════════════════════════════════════════
library;

import 'package:flutter/material.dart';

import '../theme/app_theme.dart';

/// L'origine d'une course. Trois verticales HBA cohabitent dans la même file.
///
/// L'UNIVERS EST UN BADGE, JAMAIS UNE RÈGLE DE PARCOURS.
///
/// Il change la couleur de la pastille et le mot « restaurant » en « boutique ».
/// Il ne doit pas décider des étapes : un colis pharmacie et un repas suivent le
/// même chemin. Le seul écart réel est l'attente en cuisine, et elle dépend du
/// STATUT de la commande, pas de la verticale.
enum MissionUniverse {
  food('HBA FOOD', 'restaurant'),
  express('HBAEXPRESS', 'boutique'),
  delivery('HBA DELIVERY', 'point de retrait');

  const MissionUniverse(this.badge, this.pickupNoun);

  final String badge;

  /// Le mot du métier : « Appeler le restaurant » / « Appeler la boutique ».
  final String pickupNoun;

  Color get accent => switch (this) {
        MissionUniverse.food => AppTheme.amber,
        MissionUniverse.express => AppTheme.info,
        MissionUniverse.delivery => AppTheme.brandGreen,
      };

  Color get soft => switch (this) {
        MissionUniverse.food => AppTheme.amberSoft,
        MissionUniverse.express => AppTheme.infoSoft,
        MissionUniverse.delivery => AppTheme.brandGreenSoft,
      };
}

/// Où en est une mission, du point de vue du livreur.
///
/// DIX ÉTAPES, ET L'ORDRE EST CELUI DE VOS ÉCRANS 06 À 15.
///
///   offered → accepted → goingToPickup → arrivedAtPickup → waitingForPickup
///          → pickedUp → goingToDropoff → arrivedAtDropoff → verifying → delivered
///
/// `waitingForPickup` N'EST PAS TOUJOURS TRAVERSÉ.
///
/// Une commande déjà prête à l'arrivée passe directement à la confirmation du
/// retrait. Le forcer ferait attendre devant un comptoir où le sac est posé.
enum MissionStage {
  offered,
  accepted,
  goingToPickup,
  arrivedAtPickup,
  waitingForPickup,
  pickedUp,
  goingToDropoff,
  arrivedAtDropoff,
  verifying,
  delivered,
}

/// La preuve exigée à la livraison.
///
/// LE MODE EST IMPOSÉ PAR LE SERVICE, PAS CHOISI PAR LE LIVREUR.
///
/// Votre maquette l'écrit : « Mode imposé par Delivery Service selon la
/// commande. » Les trois onglets sont donc une DÉMONSTRATION des trois modes,
/// pas un choix offert sur le terrain — sans quoi un livreur pressé prendrait
/// toujours la photo, la moins contraignante des trois, et le code OTP ne
/// protégerait plus rien.
enum ProofMode {
  otp('Code OTP'),
  photo('Photo'),
  signature('Signature');

  const ProofMode(this.label);

  final String label;
}

/// Statut d'une mission dans les listes (écrans 16 et 17).
enum MissionListStatus {
  inProgress('En cours'),
  available('Disponible'),
  delivered('Livrée'),
  cancelled('Annulée');

  const MissionListStatus(this.label);

  final String label;
}

class MockMission {
  const MockMission({
    required this.reference,
    required this.universe,
    required this.pickupName,
    required this.dropoffArea,
    required this.distanceKm,
    required this.durationMin,
    required this.earning,
    required this.status,
  });

  final String reference;
  final MissionUniverse universe;
  final String pickupName;
  final String dropoffArea;
  final double distanceKm;
  final int durationMin;
  final int earning;
  final MissionListStatus status;

  /// « Chez Mama → Akpakpa »
  String get route => '$pickupName → $dropoffArea';
}

/// Une ligne de l'historique.
class MissionHistoryEntry {
  const MissionHistoryEntry({
    required this.reference,
    required this.universeLabel,
    required this.dayLabel,
    required this.time,
    required this.amount,
    required this.status,
  });

  final String reference;

  /// « HBAExpress », « HBA Food » — casse d'affichage de l'historique, qui n'est
  /// pas celle des badges en capitales des cartes.
  final String universeLabel;

  final String dayLabel;
  final String time;
  final int amount;
  final MissionListStatus status;

  bool get isToday => dayLabel == 'Aujourd\'hui';
}

class MissionMockData {
  const MissionMockData._();

  // ── La mission en cours, celle du flux 06 → 15 ───────────────────────────

  static const String reference = '#DEL-2058';

  /// La référence de la COMMANDE, distincte de celle de la MISSION.
  ///
  /// DEUX RÉFÉRENCES, ET C'EST VOULU PAR VOS MAQUETTES.
  ///
  /// L'écran 07 affiche « #DEL-2058 », l'écran 09 « #FOOD-2058 ». Ce n'est pas
  /// une coquille : la première désigne la course confiée au livreur, la seconde
  /// la commande passée par le client. Au comptoir, le restaurateur ne connaît
  /// que la sienne — présenter le numéro de course lui demanderait de chercher
  /// une référence qu'il n'a jamais vue.
  static const String orderReference = '#FOOD-2058';

  static const MissionUniverse universe = MissionUniverse.food;

  static const String pickupName = 'Chez Mama';
  static const String pickupArea = 'Fidjrossè, Cotonou';
  static const String pickupAddress =
      'Rue 12.045, Fidjrossè Plage — face à la station Oryx';
  static const double pickupDistanceKm = 1.8;
  static const int pickupEtaMin = 6;
  static const String pickupEtaClock = '9:47';

  static const String dropoffArea = 'Akpakpa';
  static const String dropoffAddress = 'Akpakpa, Rue 4.078 — Aïbatin 2';
  static const String dropoffInstruction =
      'Maison portail noir. Appeler en arrivant.';
  static const double dropoffDistanceKm = 6.2;
  static const int dropoffEtaMin = 18;
  static const String dropoffEtaClock = '10:12';

  static const String customerName = 'Sandrine A.';
  static const String customerPhoneNote = 'Numéro masqué · appel via HBA';

  static const int earning = 1500;
  static const int totalDurationMin = 32;
  static const String parcelNote = '2 sacs · maintenir vertical';
  static const String missionSummary = 'Livraison repas · 2 sacs · 32 min estimées';

  /// Délai d'acceptation, en secondes.
  ///
  /// 15 SECONDES : C'EST LA VALEUR DE LA MAQUETTE, PAS UN CHOIX.
  ///
  /// Court, et c'est le métier qui le veut — une mission non prise doit repartir
  /// vite vers un autre livreur. Mais rien n'indique ce qui se passe à zéro :
  /// refus automatique, ou simple retrait de l'offre ? Les deux ont des
  /// conséquences opposées sur le taux d'acceptation du livreur. À trancher
  /// avant la mise en service ; ici, zéro FERME l'offre sans la compter comme un
  /// refus, l'hypothèse la moins pénalisante.
  static const int acceptSeconds = 15;

  static const int preparationEstimateMin = 5;

  /// Code affiché sur le ticket, saisi si le QR ne passe pas.
  static const String pickupCode = '4821';

  /// MODE DE PREUVE IMPOSÉ PAR LE SERVICE — ici l'OTP.
  static const ProofMode proofMode = ProofMode.otp;

  /// Récapitulatif de fin de course.
  static const int completedDurationMin = 28;

  /// 9 COURSES : 8 AU TABLEAU DE BORD, PLUS CELLE-CI.
  ///
  /// Votre écran 15 affiche « COURSES 9 » et le tableau de bord « Livraisons 8 ».
  /// Les deux concordent, à condition que ce nombre soit CALCULÉ après la
  /// livraison. L'écrire en dur aurait figé le 9 quelle que soit la journée.
  static const int deliveriesBefore = 8;
  static int get deliveriesAfter => deliveriesBefore + 1;

  // ── Écran 16 · Missions ──────────────────────────────────────────────────

  static const List<MockMission> missions = [
    MockMission(
      reference: '#DEL-2058',
      universe: MissionUniverse.food,
      pickupName: 'Chez Mama',
      dropoffArea: 'Akpakpa',
      distanceKm: 6.2,
      durationMin: 32,
      earning: 1500,
      status: MissionListStatus.inProgress,
    ),
    MockMission(
      reference: '#DEL-2041',
      universe: MissionUniverse.express,
      pickupName: 'HBA Tech Store',
      dropoffArea: 'Cadjèhoun',
      distanceKm: 4.1,
      durationMin: 32,
      earning: 2000,
      status: MissionListStatus.delivered,
    ),
    MockMission(
      reference: '#DEL-2033',
      universe: MissionUniverse.delivery,
      pickupName: 'Pharmacie Jonquet',
      dropoffArea: 'Ganhi',
      distanceKm: 2.7,
      durationMin: 32,
      earning: 1200,
      status: MissionListStatus.delivered,
    ),
    MockMission(
      reference: '#DEL-2019',
      universe: MissionUniverse.food,
      pickupName: 'Le Berlin',
      dropoffArea: 'Fidjrossè',
      distanceKm: 3.4,
      durationMin: 26,
      earning: 0,
      status: MissionListStatus.cancelled,
    ),
  ];

  // ── Écran 17 · Historique ────────────────────────────────────────────────

  /// SIX LIGNES DU JOUR ONT ÉTÉ AJOUTÉES À VOS DEUX LIGNES DESSINÉES.
  ///
  /// Votre maquette montrait deux courses aujourd'hui (+2 000 et +1 200), un
  /// total de « 68 000 F CFA » et « 42 courses ». Les trois chiffres sont
  /// inconciliables entre eux, et aucun ne concorde avec le tableau de bord, qui
  /// annonce 15 500 F CFA pour 8 livraisons — chiffre corroboré par l'écran 15
  /// (« COURSES 9 » après celle-ci).
  ///
  /// J'ai retenu le tableau de bord : vos deux lignes sont conservées telles
  /// quelles, six autres les complètent pour atteindre 15 500 F et 8 courses. Le
  /// total affiché est CALCULÉ à partir des lignes, jamais écrit — un total qui
  /// ne correspond pas à la liste qu'il surmonte est la première chose qu'un
  /// livreur remarque, parce que c'est son argent.
  static const List<MissionHistoryEntry> history = [
    MissionHistoryEntry(
      reference: '#DEL-2041',
      universeLabel: 'HBAExpress',
      dayLabel: 'Aujourd\'hui',
      time: '14:20',
      amount: 2000,
      status: MissionListStatus.delivered,
    ),
    MissionHistoryEntry(
      reference: '#DEL-2038',
      universeLabel: 'HBA Food',
      dayLabel: 'Aujourd\'hui',
      time: '13:35',
      amount: 1500,
      status: MissionListStatus.delivered,
    ),
    MissionHistoryEntry(
      reference: '#DEL-2033',
      universeLabel: 'HBA Delivery',
      dayLabel: 'Aujourd\'hui',
      time: '12:05',
      amount: 1200,
      status: MissionListStatus.delivered,
    ),
    MissionHistoryEntry(
      reference: '#DEL-2030',
      universeLabel: 'HBAExpress',
      dayLabel: 'Aujourd\'hui',
      time: '11:40',
      amount: 2500,
      status: MissionListStatus.delivered,
    ),
    MissionHistoryEntry(
      reference: '#DEL-2027',
      universeLabel: 'HBA Food',
      dayLabel: 'Aujourd\'hui',
      time: '11:02',
      amount: 1200,
      status: MissionListStatus.delivered,
    ),
    MissionHistoryEntry(
      reference: '#DEL-2024',
      universeLabel: 'HBA Delivery',
      dayLabel: 'Aujourd\'hui',
      time: '10:18',
      amount: 2000,
      status: MissionListStatus.delivered,
    ),
    MissionHistoryEntry(
      reference: '#DEL-2021',
      universeLabel: 'HBAExpress',
      dayLabel: 'Aujourd\'hui',
      time: '09:44',
      amount: 2600,
      status: MissionListStatus.delivered,
    ),
    MissionHistoryEntry(
      reference: '#DEL-2018',
      universeLabel: 'HBA Food',
      dayLabel: 'Aujourd\'hui',
      time: '09:05',
      amount: 2500,
      status: MissionListStatus.delivered,
    ),
    MissionHistoryEntry(
      reference: '#DEL-2014',
      universeLabel: 'HBA Food',
      dayLabel: 'Hier',
      time: '19:48',
      amount: 1500,
      status: MissionListStatus.delivered,
    ),
    MissionHistoryEntry(
      reference: '#DEL-2009',
      universeLabel: 'HBAExpress',
      dayLabel: 'Hier',
      time: '17:12',
      amount: 2500,
      status: MissionListStatus.delivered,
    ),
    MissionHistoryEntry(
      reference: '#DEL-2001',
      universeLabel: 'HBA Food',
      dayLabel: 'Hier',
      time: '13:30',
      amount: 0,
      status: MissionListStatus.cancelled,
    ),
  ];

  /// UNE COURSE ANNULÉE COMPTE DANS LA LISTE, PAS DANS LE TOTAL.
  ///
  /// Elle a bien eu lieu du point de vue du livreur — il s'est déplacé — mais
  /// elle n'a rien rapporté. La masquer ferait disparaître un déplacement non
  /// payé ; la compter dans le nombre de courses gonflerait une statistique dont
  /// dépend le bonus du jour.
  static int totalFor(List<MissionHistoryEntry> entries) =>
      entries.fold(0, (sum, e) => sum + e.amount);

  static int countFor(List<MissionHistoryEntry> entries) =>
      entries.where((e) => e.status == MissionListStatus.delivered).length;
}
