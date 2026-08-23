import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../core/theme/app_theme.dart';
import '../../../shared/widgets/app_notify.dart';
import '../../../shared/widgets/ui_kit.dart';
import '../disputes_data.dart';

const _types = [
  ('NotReceived', 'Colis non reçu'),
  ('NotConforming', 'Produit non conforme'),
  ('DamagedItem', 'Article endommagé'),
  ('Other', 'Autre'),
];

/// Ouvre le formulaire de signalement d'un litige sur une commande.
Future<void> showDisputeSheet(BuildContext context, {required String orderId}) {
  return showModalBottomSheet<void>(
    context: context,
    isScrollControlled: true,
    backgroundColor: AppTheme.surface,
    shape: const RoundedRectangleBorder(borderRadius: BorderRadius.vertical(top: Radius.circular(20))),
    builder: (_) => _DisputeSheet(orderId: orderId),
  );
}

class _DisputeSheet extends ConsumerStatefulWidget {
  const _DisputeSheet({required this.orderId});
  final String orderId;

  @override
  ConsumerState<_DisputeSheet> createState() => _DisputeSheetState();
}

class _DisputeSheetState extends ConsumerState<_DisputeSheet> {
  String _type = _types.first.$1;
  final _message = TextEditingController();
  bool _saving = false;
  String? _error;

  @override
  void dispose() {
    _message.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    if (_message.text.trim().length < 5) {
      setState(() => _error = 'Décrivez le problème rencontré.');
      return;
    }
    setState(() {
      _saving = true;
      _error = null;
    });
    try {
      await ref.read(disputesApiProvider).open(orderId: widget.orderId, type: _type, message: _message.text.trim());
      ref.invalidate(disputesProvider);
      if (mounted) {
        Navigator.pop(context);
        AppNotify.success(context, 'Litige ouvert. Notre équipe va l\'examiner.');
      }
    } catch (e) {
      setState(() => _error = e.toString());
    } finally {
      if (mounted) setState(() => _saving = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: EdgeInsets.fromLTRB(20, 20, 20, sheetBottomInset(context)),
      child: SingleChildScrollView(
        child: Column(mainAxisSize: MainAxisSize.min, crossAxisAlignment: CrossAxisAlignment.stretch, children: [
          const Text('Signaler un problème', style: TextStyle(fontSize: 18, fontWeight: FontWeight.w800)),
          const SizedBox(height: 14),
          const Align(alignment: Alignment.centerLeft, child: Text('Type de problème', style: TextStyle(fontWeight: FontWeight.w600))),
          const SizedBox(height: 8),
          Wrap(spacing: 8, runSpacing: 8, children: [
            for (final t in _types)
              ChoiceChip(
                label: Text(t.$2),
                selected: _type == t.$1,
                selectedColor: AppTheme.softGreen,
                onSelected: (_) => setState(() => _type = t.$1),
              ),
          ]),
          const SizedBox(height: 14),
          TextField(
            controller: _message,
            minLines: 3,
            maxLines: 6,
            decoration: const InputDecoration(labelText: 'Décrivez le problème', alignLabelWithHint: true),
          ),
          if (_error != null) ...[
            const SizedBox(height: 8),
            Text(_error!, style: const TextStyle(color: AppTheme.danger, fontSize: 13)),
          ],
          const SizedBox(height: 16),
          FilledButton(
            onPressed: _saving ? null : _submit,
            child: _saving
                ? const SizedBox(height: 20, width: 20, child: CircularProgressIndicator(strokeWidth: 2, color: Colors.white))
                : const Text('Ouvrir le litige'),
          ),
        ]),
      ),
    );
  }
}
