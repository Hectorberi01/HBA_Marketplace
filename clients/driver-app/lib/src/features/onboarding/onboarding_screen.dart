import 'package:flutter/material.dart';

class OnboardingScreen extends StatelessWidget {
  const OnboardingScreen({required this.onFinished, super.key});

  final VoidCallback onFinished;

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      body: SafeArea(
        child: Padding(
          padding: const EdgeInsets.all(24),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              const Spacer(),
              Container(
                width: 72,
                height: 72,
                decoration: BoxDecoration(
                  color: Theme.of(context).colorScheme.primaryContainer,
                  borderRadius: BorderRadius.circular(22),
                ),
                child: Icon(
                  Icons.two_wheeler,
                  size: 38,
                  color: Theme.of(context).colorScheme.primary,
                ),
              ),
              const SizedBox(height: 28),
              Text(
                'HBA Driver',
                style: Theme.of(context).textTheme.displaySmall
                    ?.copyWith(fontWeight: FontWeight.w900),
              ),
              const SizedBox(height: 12),
              Text(
                'Recevez vos courses, acceptez les missions, suivez vos gains et confirmez les livraisons depuis une seule application.',
                style: Theme.of(context).textTheme.bodyLarge,
              ),
              const Spacer(),
              _FeatureLine(
                icon: Icons.near_me_outlined,
                title: 'Courses proches',
                subtitle: 'Propositions en temps réel selon votre position.',
              ),
              const SizedBox(height: 14),
              _FeatureLine(
                icon: Icons.verified_outlined,
                title: 'Preuve de livraison',
                subtitle: 'Signature, photo ou code selon le type de course.',
              ),
              const SizedBox(height: 14),
              _FeatureLine(
                icon: Icons.account_balance_wallet_outlined,
                title: 'Gains visibles',
                subtitle: 'Solde, historique et demandes de retrait.',
              ),
              const SizedBox(height: 32),
              FilledButton(
                onPressed: onFinished,
                child: const Text('Commencer'),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

class _FeatureLine extends StatelessWidget {
  const _FeatureLine({
    required this.icon,
    required this.title,
    required this.subtitle,
  });

  final IconData icon;
  final String title;
  final String subtitle;

  @override
  Widget build(BuildContext context) {
    return Row(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Icon(icon, color: Theme.of(context).colorScheme.primary),
        const SizedBox(width: 14),
        Expanded(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(title, style: const TextStyle(fontWeight: FontWeight.w800)),
              const SizedBox(height: 2),
              Text(subtitle),
            ],
          ),
        ),
      ],
    );
  }
}
