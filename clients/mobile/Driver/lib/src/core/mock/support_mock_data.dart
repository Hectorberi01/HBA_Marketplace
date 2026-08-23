/// Incidents, hors ligne et assistance.
library;

/// Un type d'incident, avec la marche à suivre qui lui répond.
///
/// CHAQUE INCIDENT PORTE SES ÉTAPES : IL N'Y A PAS DE CONSEIL GÉNÉRIQUE.
///
/// « Contactez le support » ne dit pas quoi faire pendant les cinq minutes où
/// l'on attend devant un portail. Votre maquette détaille « Client absent » en
/// trois étapes numérotées, et c'est le bon niveau : appeler, attendre un délai
/// précis, puis déclarer l'échec.
///
/// SEUL « CLIENT ABSENT » EST DÉTAILLÉ PAR LA MAQUETTE.
///
/// Les six autres marches à suivre sont DÉDUITES de la logique de terrain. Elles
/// sont plausibles, elles ne sont pas validées — et un conseil erroné sur un
/// colis endommagé ou un problème véhicule engage plus qu'un écran mal aligné.
/// À faire relire par l'exploitation avant mise en service.
class IncidentType {
  const IncidentType({
    required this.label,
    required this.steps,
    required this.failureAction,
    this.drawnByDesign = false,
  });

  final String label;
  final List<String> steps;

  /// Le libellé du bouton rouge, ou `null` si l'incident ne clôt pas la course.
  final String? failureAction;

  /// `true` uniquement pour la marche à suivre écrite par la maquette.
  final bool drawnByDesign;
}

class SupportMockData {
  const SupportMockData._();

  static const String missionContext =
      'Mission #DEL-2058 · en livraison. Choisissez le problème rencontré, '
      'nous vous indiquerons la marche à suivre.';

  static const String rulesNote = 'Soumis aux règles Delivery Service.';

  static const List<IncidentType> incidents = [
    IncidentType(
      label: 'Restaurant fermé',
      steps: [
        'Vérifiez l\'adresse et l\'entrée du commerce.',
        'Appelez l\'établissement (numéro masqué).',
        'Si personne ne répond, signalez la fermeture.',
      ],
      failureAction: 'Signaler l\'établissement fermé',
    ),
    IncidentType(
      label: 'Client absent',
      drawnByDesign: true,
      steps: [
        'Appelez le client (numéro masqué).',
        'Attendez 5 minutes sur place — un chronomètre démarre.',
        'Si aucune réponse, déclarez l\'échec de livraison.',
      ],
      failureAction: 'Je ne peux pas effectuer la livraison',
    ),
    IncidentType(
      label: 'Client injoignable',
      steps: [
        'Réessayez l\'appel une seconde fois.',
        'Envoyez un message via HBA.',
        'Attendez 5 minutes avant de déclarer l\'échec.',
      ],
      failureAction: 'Je ne peux pas effectuer la livraison',
    ),
    IncidentType(
      label: 'Adresse incorrecte',
      steps: [
        'Relisez l\'instruction laissée par le client.',
        'Appelez pour faire préciser le repère.',
        'Si l\'adresse reste introuvable, signalez-la.',
      ],
      failureAction: 'Signaler une adresse introuvable',
    ),
    IncidentType(
      label: 'Colis endommagé',
      steps: [
        'Photographiez le colis avant toute remise.',
        'Ne remettez pas un contenu alimentaire renversé.',
        'Signalez : HBA décidera du remboursement.',
      ],
      failureAction: 'Signaler un colis endommagé',
    ),
    IncidentType(
      label: 'Problème véhicule',
      steps: [
        'Mettez-vous en sécurité avant toute manipulation.',
        'Prévenez HBA : la mission sera réattribuée.',
        'Passez hors ligne le temps de la réparation.',
      ],
      failureAction: 'Je ne peux pas continuer',
    ),
    IncidentType(
      label: 'Autre',
      steps: [
        'Décrivez la situation au support.',
        'Restez sur place tant qu\'aucune consigne ne vous parvient.',
      ],
      // Pas de bouton d'échec : « Autre » ne doit pas devenir le raccourci
      // universel pour abandonner une course sans motif traçable.
      failureAction: null,
    ),
  ];

  // ── Hors ligne ────────────────────────────────────────────────────────────

  static const String offlineBanner =
      'Connexion perdue · tentative de reconnexion…';

  static const String offlineTitle = 'Votre mission reste disponible hors ligne';

  static const String offlineBody =
      'Adresses, instructions et contacts sont conservés sur l\'appareil. '
      'Les actions serveur seront envoyées à la reconnexion.';

  /// LA FILE EST NOMMÉE, PAS SEULEMENT COMPTÉE.
  ///
  /// « 1 action en attente » ne dit pas laquelle. « "Je suis arrivé" sera envoyé
  /// automatiquement » dit ce que le serveur ignore encore — et évite qu'on
  /// retouche le bouton trois fois en croyant que rien n'a été pris.
  static const List<String> queuedActions = ['Je suis arrivé'];

  // ── Assistance ────────────────────────────────────────────────────────────

  static const String emergencyBody =
      'Accident, agression, danger immédiat. Votre position est transmise à HBA.';

  static const List<String> supportTopics = [
    'Problème sur ma mission',
    'Problème de paiement',
    'Mon compte / mes documents',
  ];

  static const String supportHours = 'Support HBA · 7j/7 de 6h à 23h';
  static const String supportPhone = '+229 01 40 00 00';
}
