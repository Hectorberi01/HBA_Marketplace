/// ═════════════════════════════════════════════════════════════════════════════
/// REVENUS, MOUVEMENTS ET RETRAITS.
///
/// VOS ÉCRANS 18 ET 19 ONT TRANCHÉ UNE CONTRADICTION SIGNALÉE HIER.
///
/// L'historique (écran 17) annonçait « 68 000 F CFA · 42 courses » au-dessus de
/// lignes qui n'y menaient pas, alors que le tableau de bord donnait 15 500 F.
/// L'écran 18 lève l'ambiguïté : « Aujourd'hui 15 500 F · Cette semaine
/// 68 000 F ». Les deux nombres coexistent, sur deux périodes différentes.
///
/// Le total hebdomadaire est donc établi ici, et l'écran 17 le reprend pour ses
/// périodes longues au lieu de le recalculer.
/// ═════════════════════════════════════════════════════════════════════════════
library;

/// Nature d'un mouvement sur le solde.
///
/// QUATRE NATURES, PARCE QU'ELLES NE SE LISENT PAS PAREIL.
///
/// Une livraison rémunère ; un bonus récompense ; un ajustement RETIRE de
/// l'argent déjà crédité ; un retrait sort l'argent du compte sans être une
/// perte. Confondre les deux derniers ferait passer un virement demandé pour une
/// sanction — la confusion la plus coûteuse en confiance sur ce type d'écran.
enum MovementKind { delivery, bonus, adjustment, payout }

class DriverMovement {
  const DriverMovement({
    required this.title,
    required this.detail,
    required this.amount,
    required this.kind,
  });

  final String title;
  final String detail;

  /// Négatif pour un ajustement ou un retrait. Le signe porte le sens ;
  /// l'affichage n'ajoute jamais de « − » à la main.
  final int amount;

  final MovementKind kind;
}

/// Un moyen de paiement Mobile Money.
class PayoutMethod {
  const PayoutMethod({
    required this.id,
    required this.name,
    required this.maskedNumber,
    required this.initials,
  });

  final String id;
  final String name;

  /// STOCKÉ DÉJÀ MASQUÉ : « XX XX XX 12 ».
  ///
  /// Le numéro complet n'a rien à faire dans l'application. Le garder entier
  /// pour l'afficher tronqué le ferait fuiter au premier rapport de plantage.
  final String maskedNumber;

  final String initials;
}

class EarningsMockData {
  const EarningsMockData._();

  /// Solde retirable immédiatement.
  static const int available = 42500;

  /// « EN ATTENTE » N'EST PAS « DISPONIBLE ».
  ///
  /// Ce sont des courses livrées mais pas encore libérées — délai de
  /// réclamation, contrôle anti-fraude. Les additionner dans un solde unique
  /// ferait promettre un retrait que le service refuserait ensuite.
  static const int pending = 15000;

  static const int today = 15500;

  /// LES SEPT JOURS SOMMENT AUX 68 000 F DE VOTRE ÉCRAN 18.
  ///
  /// Dimanche vaut 15 500 : c'est aujourd'hui, et c'est le même nombre que le
  /// tableau de bord et que l'historique. Tout le reste en découle.
  ///
  /// LES HAUTEURS DE BARRES DE LA MAQUETTE NE SUIVENT PAS SES PROPRES CHIFFRES.
  ///
  /// Elle dessine dimanche comme la barre la plus COURTE alors que c'est le
  /// meilleur jour de la semaine. Le graphique étant construit à partir des
  /// valeurs, dimanche y sera la plus haute. C'est le dessin qui est à reprendre,
  /// pas les données.
  static const Map<String, int> weekByDay = {
    'Lun': 9000,
    'Mar': 10000,
    'Mer': 7500,
    'Jeu': 8000,
    'Ven': 9000,
    'Sam': 9000,
    'Dim': today,
  };

  static int get week => weekByDay.values.fold(0, (a, b) => a + b);

  /// Jour affiché sur l'écran de détail. Sert aussi à calculer l'échéance de
  /// l'assurance : « expire dans 12 jours » ⇒ le 24 mai.
  static const String todayLabel = 'AUJOURD\'HUI · 12 MAI';
  static const int todayDayOfMonth = 12;
  static const String monthLabel = 'mai';

  static const List<DriverMovement> movements = [
    DriverMovement(
      title: 'Livraison #DEL-2058',
      detail: 'HBA Food · 14:52',
      amount: 1500,
      kind: MovementKind.delivery,
    ),
    DriverMovement(
      title: 'Bonus heure de pointe',
      detail: '12 mai · 19:00-21:00',
      amount: 500,
      kind: MovementKind.bonus,
    ),
    DriverMovement(
      // LE MOTIF EST OBLIGATOIRE SUR UN AJUSTEMENT NÉGATIF.
      //
      // « Ajustement −500 F » sans explication est la ligne qui déclenche un
      // appel au support. « Annulation client #DEL-2001 » désigne la course et
      // permet de vérifier.
      title: 'Ajustement',
      detail: 'Annulation client #DEL-2001',
      amount: -500,
      kind: MovementKind.adjustment,
    ),
    DriverMovement(
      title: 'Payout MTN MoMo',
      detail: '11 mai · XX XX XX 12',
      amount: -20000,
      kind: MovementKind.payout,
    ),
  ];

  static const List<PayoutMethod> payoutMethods = [
    PayoutMethod(
      id: 'mtn',
      name: 'MTN MoMo',
      maskedNumber: 'XX XX XX 12',
      initials: 'MTN',
    ),
    PayoutMethod(
      id: 'moov',
      name: 'Moov Money',
      maskedNumber: 'XX XX XX 47',
      initials: 'MOOV',
    ),
  ];

  /// Montants proposés en un tap.
  ///
  /// « TOUT » N'EST PAS UN MONTANT, C'EST LE SOLDE.
  ///
  /// Le figer à 42 500 le rendrait faux dès la première course suivante. Il est
  /// donc calculé au moment du tap.
  static const List<int> quickAmounts = [10000, 25000];

  static const int defaultAmount = 25000;

  static const String payoutDelay = 'Traitement sous 24 h ouvrées.';
}
