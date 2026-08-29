enum DeliveryType { food, package }

enum DeliveryStatus { proposed, accepted, pickupPending, pickedUp, delivered }

class DeliveryTask {
  const DeliveryTask({
    required this.id,
    required this.type,
    required this.status,
    required this.pickupName,
    required this.pickupAddress,
    required this.dropoffName,
    required this.dropoffAddress,
    required this.distanceKm,
    required this.estimatedMinutes,
    required this.payoutXof,
    required this.reference,
    required this.customerPhone,
    required this.pickupPhone,
    required this.instructions,
    required this.proofMethods,
  });

  final String id;
  final DeliveryType type;
  final DeliveryStatus status;
  final String pickupName;
  final String pickupAddress;
  final String dropoffName;
  final String dropoffAddress;
  final double distanceKm;
  final int estimatedMinutes;
  final int payoutXof;
  final String reference;
  final String customerPhone;
  final String pickupPhone;
  final String instructions;
  final List<ProofMethod> proofMethods;

  String get typeLabel => switch (type) {
    DeliveryType.food => 'Repas',
    DeliveryType.package => 'Colis',
  };

  String get statusLabel => switch (status) {
    DeliveryStatus.proposed => 'Proposée',
    DeliveryStatus.accepted => 'Acceptée',
    DeliveryStatus.pickupPending => 'À récupérer',
    DeliveryStatus.pickedUp => 'En route',
    DeliveryStatus.delivered => 'Livrée',
  };
}

enum ProofMethod { photo, signature, code }

extension ProofMethodLabel on ProofMethod {
  String get label => switch (this) {
    ProofMethod.photo => 'Photo',
    ProofMethod.signature => 'Signature',
    ProofMethod.code => 'Code client',
  };
}
