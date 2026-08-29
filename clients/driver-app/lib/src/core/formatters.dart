String formatXof(int amount) {
  final value = amount.toString();
  final buffer = StringBuffer();

  for (var index = 0; index < value.length; index++) {
    final remaining = value.length - index;
    buffer.write(value[index]);
    if (remaining > 1 && remaining % 3 == 1) {
      buffer.write(' ');
    }
  }

  return '${buffer.toString()} XOF';
}

String formatDistance(double kilometers) {
  return '${kilometers.toStringAsFixed(1)} km';
}
