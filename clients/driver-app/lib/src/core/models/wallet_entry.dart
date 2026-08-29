enum WalletEntryType { earning, withdrawal, adjustment }

class WalletEntry {
  const WalletEntry({
    required this.label,
    required this.date,
    required this.amountXof,
    required this.type,
  });

  final String label;
  final String date;
  final int amountXof;
  final WalletEntryType type;
}
