import '../models/delivery_task.dart';
import '../models/driver_profile.dart';
import '../models/wallet_entry.dart';

const driverProfile = DriverProfile(
  fullName: 'Koffi Adandé',
  city: 'Cotonou',
  rating: 4.8,
  completedDeliveries: 342,
  vehicleLabel: 'Moto Haojue DK 125',
  plateNumber: 'BJ-2048-RB',
  verified: true,
);

final activeDelivery = DeliveryTask(
  id: 'DLV-2408-0182',
  type: DeliveryType.food,
  status: DeliveryStatus.pickupPending,
  pickupName: 'Restaurant Akpakpa Grill',
  pickupAddress: 'Rue 12.229, Cotonou',
  dropoffName: 'Client A. Mensah',
  dropoffAddress: 'Haie Vive, Cotonou',
  distanceKm: 5.8,
  estimatedMinutes: 24,
  payoutXof: 1800,
  reference: 'FOOD-92741',
  customerPhone: '+229 01 67 42 19 88',
  pickupPhone: '+229 01 60 10 18 44',
  instructions: 'Remettre le repas au gardien si le client ne répond pas.',
  proofMethods: [ProofMethod.photo, ProofMethod.code],
);

final proposedDeliveries = <DeliveryTask>[
  DeliveryTask(
    id: 'DLV-2408-0183',
    type: DeliveryType.package,
    status: DeliveryStatus.proposed,
    pickupName: 'Boutique Ganhi',
    pickupAddress: 'Ganhi, Cotonou',
    dropoffName: 'M. Dossou',
    dropoffAddress: 'Fidjrosse, Cotonou',
    distanceKm: 8.4,
    estimatedMinutes: 32,
    payoutXof: 2500,
    reference: 'SHIP-44190',
    customerPhone: '+229 01 64 71 20 09',
    pickupPhone: '+229 01 61 33 45 20',
    instructions: 'Colis fragile. Ne pas incliner le paquet.',
    proofMethods: [ProofMethod.signature, ProofMethod.photo],
  ),
  DeliveryTask(
    id: 'DLV-2408-0184',
    type: DeliveryType.food,
    status: DeliveryStatus.proposed,
    pickupName: 'Bistro Cadjehoun',
    pickupAddress: 'Cadjehoun, Cotonou',
    dropoffName: 'Client S. Bio',
    dropoffAddress: 'Aibatin, Cotonou',
    distanceKm: 3.1,
    estimatedMinutes: 16,
    payoutXof: 1200,
    reference: 'FOOD-92752',
    customerPhone: '+229 01 90 22 14 73',
    pickupPhone: '+229 01 54 18 04 22',
    instructions: 'Appeler le client à l’arrivée.',
    proofMethods: [ProofMethod.code],
  ),
];

final completedDeliveries = <DeliveryTask>[
  DeliveryTask(
    id: 'DLV-2408-0178',
    type: DeliveryType.food,
    status: DeliveryStatus.delivered,
    pickupName: 'Chez Maman Clarisse',
    pickupAddress: 'Mènontin, Cotonou',
    dropoffName: 'Client B. Sossa',
    dropoffAddress: 'Gbégamey, Cotonou',
    distanceKm: 4.6,
    estimatedMinutes: 21,
    payoutXof: 1500,
    reference: 'FOOD-92680',
    customerPhone: '+229 01 97 15 48 20',
    pickupPhone: '+229 01 66 09 11 30',
    instructions: 'Livraison terminée.',
    proofMethods: [ProofMethod.code],
  ),
  DeliveryTask(
    id: 'DLV-2408-0172',
    type: DeliveryType.package,
    status: DeliveryStatus.delivered,
    pickupName: 'HBA Store Étoile Rouge',
    pickupAddress: 'Étoile Rouge, Cotonou',
    dropoffName: 'Mme Houénou',
    dropoffAddress: 'Calavi, Abomey-Calavi',
    distanceKm: 13.2,
    estimatedMinutes: 42,
    payoutXof: 3800,
    reference: 'SHIP-44031',
    customerPhone: '+229 01 62 11 29 00',
    pickupPhone: '+229 01 55 41 77 13',
    instructions: 'Livraison terminée.',
    proofMethods: [ProofMethod.signature],
  ),
];

final walletEntries = <WalletEntry>[
  const WalletEntry(
    label: 'Course FOOD-92741',
    date: 'Aujourd’hui, 12:24',
    amountXof: 1800,
    type: WalletEntryType.earning,
  ),
  const WalletEntry(
    label: 'Course SHIP-44031',
    date: 'Hier, 18:05',
    amountXof: 3800,
    type: WalletEntryType.earning,
  ),
  const WalletEntry(
    label: 'Retrait Mobile Money',
    date: '26 août, 09:10',
    amountXof: -12000,
    type: WalletEntryType.withdrawal,
  ),
];
