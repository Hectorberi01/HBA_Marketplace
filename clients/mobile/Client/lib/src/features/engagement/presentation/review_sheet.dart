import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../core/theme/app_theme.dart';
import '../../../shared/widgets/app_notify.dart';
import '../../../shared/widgets/ui_kit.dart';
import '../../catalog/catalog_data.dart';
import '../review_data.dart';

/// Ouvre le formulaire de dépôt d'avis pour un produit acheté.
Future<void> showReviewSheet(BuildContext context, {required String productId, required String orderId, required String productName}) {
  return showModalBottomSheet<void>(
    context: context,
    isScrollControlled: true,
    backgroundColor: AppTheme.surface,
    shape: const RoundedRectangleBorder(borderRadius: BorderRadius.vertical(top: Radius.circular(20))),
    builder: (_) => _ReviewSheet(productId: productId, orderId: orderId, productName: productName),
  );
}

class _ReviewSheet extends ConsumerStatefulWidget {
  const _ReviewSheet({required this.productId, required this.orderId, required this.productName});
  final String productId;
  final String orderId;
  final String productName;

  @override
  ConsumerState<_ReviewSheet> createState() => _ReviewSheetState();
}

class _ReviewSheetState extends ConsumerState<_ReviewSheet> {
  int _rating = 5;
  final _title = TextEditingController();
  final _body = TextEditingController();
  bool _saving = false;
  String? _error;

  @override
  void dispose() {
    _title.dispose();
    _body.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    if (_body.text.trim().isEmpty) {
      setState(() => _error = 'Écrivez quelques mots sur le produit.');
      return;
    }
    setState(() {
      _saving = true;
      _error = null;
    });
    try {
      await ref.read(reviewApiProvider).submit(
            productId: widget.productId,
            orderId: widget.orderId,
            rating: _rating,
            title: _title.text.trim().isEmpty ? null : _title.text.trim(),
            body: _body.text.trim(),
          );
      // Rafraîchit les avis du produit pour que le nouveau apparaisse aussitôt.
      ref.invalidate(productReviewsProvider(widget.productId));
      if (mounted) {
        AppNotify.success(context, 'Merci pour votre avis !');
        Navigator.pop(context);
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
          const Text('Noter le produit', style: TextStyle(fontSize: 18, fontWeight: FontWeight.w800)),
          const SizedBox(height: 2),
          Text(widget.productName, style: TextStyle(color: AppTheme.subtle, fontSize: 13)),
          const SizedBox(height: 16),
          Center(
            child: Row(mainAxisSize: MainAxisSize.min, children: [
              for (var i = 1; i <= 5; i++)
                IconButton(
                  onPressed: () => setState(() => _rating = i),
                  icon: Icon(i <= _rating ? Icons.star : Icons.star_border,
                      color: i <= _rating ? AppTheme.promoOrange : AppTheme.subtle, size: 34),
                ),
            ]),
          ),
          const SizedBox(height: 8),
          TextField(controller: _title, decoration: const InputDecoration(labelText: 'Titre (optionnel)')),
          const SizedBox(height: 12),
          TextField(
            controller: _body,
            minLines: 3,
            maxLines: 6,
            decoration: const InputDecoration(labelText: 'Votre avis', alignLabelWithHint: true),
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
                : const Text('Publier mon avis'),
          ),
        ]),
      ),
    );
  }
}
