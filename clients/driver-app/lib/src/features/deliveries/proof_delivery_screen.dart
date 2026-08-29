import 'package:flutter/material.dart';

import '../../core/models/delivery_task.dart';
import '../../core/widgets/app_card.dart';
import '../../core/widgets/section_header.dart';

class ProofDeliveryScreen extends StatefulWidget {
  const ProofDeliveryScreen({required this.task, super.key});

  final DeliveryTask task;

  @override
  State<ProofDeliveryScreen> createState() => _ProofDeliveryScreenState();
}

class _ProofDeliveryScreenState extends State<ProofDeliveryScreen> {
  ProofMethod? _selectedMethod;
  final _codeController = TextEditingController();
  final _noteController = TextEditingController();

  @override
  void dispose() {
    _codeController.dispose();
    _noteController.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final methods = widget.task.proofMethods;
    _selectedMethod ??= methods.firstOrNull;

    return Scaffold(
      appBar: AppBar(title: const Text('Preuve de livraison')),
      body: SafeArea(
        child: ListView(
          padding: const EdgeInsets.fromLTRB(16, 8, 16, 24),
          children: [
            AppCard(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    widget.task.reference,
                    style: Theme.of(context).textTheme.titleLarge
                        ?.copyWith(fontWeight: FontWeight.w900),
                  ),
                  const SizedBox(height: 6),
                  Text(widget.task.dropoffAddress),
                ],
              ),
            ),
            const SizedBox(height: 18),
            const SectionHeader(title: 'Méthode de preuve'),
            const SizedBox(height: 12),
            for (final method in methods) ...[
              InkWell(
                borderRadius: BorderRadius.circular(12),
                onTap: () => setState(() => _selectedMethod = method),
                child: AppCard(
                  padding: const EdgeInsets.symmetric(
                    horizontal: 14,
                    vertical: 12,
                  ),
                  child: Row(
                    children: [
                      Icon(
                        _selectedMethod == method
                            ? Icons.radio_button_checked
                            : Icons.radio_button_unchecked,
                        color: Theme.of(context).colorScheme.primary,
                      ),
                      const SizedBox(width: 12),
                      Expanded(child: Text(method.label)),
                    ],
                  ),
                ),
              ),
              const SizedBox(height: 8),
            ],
            const SizedBox(height: 10),
            if (_selectedMethod == ProofMethod.code)
              TextField(
                controller: _codeController,
                keyboardType: TextInputType.number,
                decoration: const InputDecoration(
                  labelText: 'Code client',
                  prefixIcon: Icon(Icons.pin_outlined),
                ),
              ),
            if (_selectedMethod == ProofMethod.photo)
              OutlinedButton.icon(
                onPressed: () {},
                icon: const Icon(Icons.camera_alt_outlined),
                label: const Text('Prendre une photo'),
              ),
            if (_selectedMethod == ProofMethod.signature)
              AppCard(
                padding: const EdgeInsets.all(24),
                child: Column(
                  children: [
                    Icon(
                      Icons.draw_outlined,
                      size: 40,
                      color: Theme.of(context).colorScheme.primary,
                    ),
                    const SizedBox(height: 8),
                    const Text('Zone de signature à brancher'),
                  ],
                ),
              ),
            const SizedBox(height: 12),
            TextField(
              controller: _noteController,
              minLines: 3,
              maxLines: 5,
              decoration: const InputDecoration(
                labelText: 'Note de livraison',
                alignLabelWithHint: true,
              ),
            ),
            const SizedBox(height: 20),
            FilledButton.icon(
              onPressed: () =>
                  Navigator.of(context).popUntil((route) => route.isFirst),
              icon: const Icon(Icons.check_circle_outline),
              label: const Text('Confirmer la livraison'),
            ),
          ],
        ),
      ),
    );
  }
}
