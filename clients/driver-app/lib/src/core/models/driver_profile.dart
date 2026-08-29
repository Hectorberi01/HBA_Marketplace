class DriverProfile {
  const DriverProfile({
    required this.fullName,
    required this.city,
    required this.rating,
    required this.completedDeliveries,
    required this.vehicleLabel,
    required this.plateNumber,
    required this.verified,
  });

  final String fullName;
  final String city;
  final double rating;
  final int completedDeliveries;
  final String vehicleLabel;
  final String plateNumber;
  final bool verified;
}
