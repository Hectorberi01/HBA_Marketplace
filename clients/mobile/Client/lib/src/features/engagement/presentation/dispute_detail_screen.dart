import 'dart:io';

import 'package:cached_network_image/cached_network_image.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:image_picker/image_picker.dart';

import '../../../core/theme/app_theme.dart';
import '../../../shared/utils/formatters.dart';
import '../../../shared/widgets/app_notify.dart';
import '../../../shared/widgets/async_views.dart';
import '../../messaging/messaging_data.dart';
import '../disputes_data.dart';

/// Détail d'un litige : le fil complet des échanges (mes messages vs réponses du
/// support) et un champ pour répondre.
class DisputeDetailScreen extends ConsumerStatefulWidget {
  const DisputeDetailScreen({super.key, required this.disputeId});
  final String disputeId;

  @override
  ConsumerState<DisputeDetailScreen> createState() => _DisputeDetailScreenState();
}

class _DisputeDetailScreenState extends ConsumerState<DisputeDetailScreen> {
  final _reply = TextEditingController();
  bool _sending = false;
  File? _pendingImage;

  @override
  void dispose() {
    _reply.dispose();
    super.dispose();
  }

  Future<void> _pickImage() async {
    try {
      final x = await ImagePicker().pickImage(source: ImageSource.gallery, maxWidth: 1600, imageQuality: 80);
      if (x != null && mounted) setState(() => _pendingImage = File(x.path));
    } catch (e) {
      if (mounted) AppNotify.error(context, "Impossible de sélectionner l'image : $e");
    }
  }

  Future<void> _send() async {
    if (_sending) return;
    final text = _reply.text.trim();
    // Rien à envoyer : ni texte, ni photo.
    if (text.isEmpty && _pendingImage == null) return;
    setState(() => _sending = true);
    try {
      String? photoUrl;
      if (_pendingImage != null) {
        photoUrl = await ref.read(messagingApiProvider).uploadAttachment(_pendingImage!);
      }
      // Le backend exige un message non vide : si l'utilisateur n'envoie qu'une
      // photo, on met un corps par défaut.
      final body = text.isEmpty ? 'Pièce jointe' : text;
      await ref.read(disputesApiProvider).reply(widget.disputeId, body, photoUrl: photoUrl);
      _reply.clear();
      setState(() => _pendingImage = null);
      ref.invalidate(disputeDetailProvider(widget.disputeId));
      ref.invalidate(disputesProvider);
    } catch (e) {
      if (mounted) AppNotify.error(context, e.toString());
    } finally {
      if (mounted) setState(() => _sending = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final dispute = ref.watch(disputeDetailProvider(widget.disputeId));
    return Scaffold(
      backgroundColor: AppTheme.bg,
      appBar: AppBar(title: const Text('Litige')),
      body: dispute.when(
        loading: () => const LoadingView(),
        error: (e, _) => ErrorView(
          message: e.toString(),
          onRetry: () => ref.invalidate(disputeDetailProvider(widget.disputeId)),
        ),
        data: (d) {
          final resolved = d.status.toLowerCase() == 'resolved';
          return Column(children: [
            Expanded(
              child: ListView(
                padding: const EdgeInsets.fromLTRB(16, 16, 16, 16),
                children: [
                  _Header(dispute: d),
                  const SizedBox(height: 16),
                  if (d.messages.isEmpty)
                    Padding(
                      padding: const EdgeInsets.only(top: 40),
                      child: Center(
                        child: Text('Aucun message pour l’instant.',
                            style: TextStyle(color: AppTheme.subtle)),
                      ),
                    ),
                  for (final m in d.messages)
                    _Bubble(message: m, mine: m.authorId == d.raisedBy),
                ],
              ),
            ),
            if (resolved)
              Container(
                width: double.infinity,
                padding: const EdgeInsets.all(16),
                color: AppTheme.surface,
                child: Text('Ce litige est résolu.',
                    textAlign: TextAlign.center, style: TextStyle(color: AppTheme.subtle)),
              )
            else
              _ReplyBar(
                controller: _reply,
                sending: _sending,
                onSend: _send,
                onAttach: _pickImage,
                pendingImage: _pendingImage,
                onRemoveImage: () => setState(() => _pendingImage = null),
              ),
          ]);
        },
      ),
    );
  }
}

class _Header extends StatelessWidget {
  const _Header({required this.dispute});
  final Dispute dispute;

  String get _orderRef {
    final id = dispute.orderId.replaceAll('-', '');
    return 'CMD-${id.substring(0, id.length >= 8 ? 8 : id.length).toUpperCase()}';
  }

  @override
  Widget build(BuildContext context) {
    final (label, color) = _status(dispute.status);
    return Container(
      padding: const EdgeInsets.all(14),
      decoration: BoxDecoration(
        color: AppTheme.surface,
        borderRadius: BorderRadius.circular(14),
        border: Border.all(color: AppTheme.line),
      ),
      child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
        Row(children: [
          Expanded(child: Text(_type(dispute.type), style: const TextStyle(fontWeight: FontWeight.w800, fontSize: 16))),
          Container(
            padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 4),
            decoration: BoxDecoration(color: color.withValues(alpha: 0.12), borderRadius: BorderRadius.circular(20)),
            child: Text(label, style: TextStyle(color: color, fontSize: 12, fontWeight: FontWeight.w700)),
          ),
        ]),
        const SizedBox(height: 6),
        GestureDetector(
          onTap: () => context.push('/order/${dispute.orderId}'),
          child: Text('Voir la commande $_orderRef',
              style: const TextStyle(color: AppTheme.brandGreen, fontWeight: FontWeight.w700, fontSize: 13)),
        ),
      ]),
    );
  }

  (String, Color) _status(String s) {
    switch (s.toLowerCase()) {
      case 'underreview':
        return ('En cours d\'examen', const Color(0xFF3B6FE0));
      case 'resolved':
        return ('Résolu', AppTheme.brandGreen);
      case 'escalated':
        return ('Escaladé', AppTheme.danger);
      default:
        return ('Ouvert', AppTheme.promoOrange);
    }
  }

  String _type(String t) {
    switch (t.toLowerCase()) {
      case 'notreceived':
        return 'Colis non reçu';
      case 'notconforming':
        return 'Produit non conforme';
      case 'damageditem':
        return 'Article endommagé';
      default:
        return 'Autre problème';
    }
  }
}

/// Bulle de message : à DROITE (vert) si c'est moi, à GAUCHE (support) sinon.
class _Bubble extends StatelessWidget {
  const _Bubble({required this.message, required this.mine});
  final DisputeMessage message;
  final bool mine;

  @override
  Widget build(BuildContext context) {
    final bg = mine ? AppTheme.brandGreen : AppTheme.surface;
    final fg = mine ? Colors.white : AppTheme.ink;
    return Padding(
      padding: const EdgeInsets.only(bottom: 10),
      child: Column(crossAxisAlignment: mine ? CrossAxisAlignment.end : CrossAxisAlignment.start, children: [
        if (!mine)
          Padding(
            padding: const EdgeInsets.only(left: 4, bottom: 2),
            child: Text('Support', style: TextStyle(fontSize: 11, fontWeight: FontWeight.w700, color: AppTheme.subtle)),
          ),
        Container(
          constraints: BoxConstraints(maxWidth: MediaQuery.of(context).size.width * 0.78),
          padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 10),
          decoration: BoxDecoration(
            color: bg,
            borderRadius: BorderRadius.only(
              topLeft: const Radius.circular(14),
              topRight: const Radius.circular(14),
              bottomLeft: Radius.circular(mine ? 14 : 4),
              bottomRight: Radius.circular(mine ? 4 : 14),
            ),
            border: mine ? null : Border.all(color: AppTheme.line),
          ),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            mainAxisSize: MainAxisSize.min,
            children: [
              if (message.photoUrl != null) ...[
                ClipRRect(
                  borderRadius: BorderRadius.circular(8),
                  child: CachedNetworkImage(
                    imageUrl: message.photoUrl!,
                    fit: BoxFit.cover,
                    placeholder: (_, __) => Container(height: 140, color: Colors.black12),
                    errorWidget: (_, __, ___) => Container(
                        height: 140,
                        color: Colors.black12,
                        child: const Icon(Icons.broken_image_outlined)),
                  ),
                ),
                const SizedBox(height: 6),
              ],
              Text(message.body, style: TextStyle(color: fg, height: 1.3)),
            ],
          ),
        ),
        Padding(
          padding: const EdgeInsets.only(top: 3, left: 4, right: 4),
          child: Text(Format.dateTime(message.createdAt),
              style: TextStyle(fontSize: 10, color: AppTheme.subtle)),
        ),
      ]),
    );
  }
}

class _ReplyBar extends StatelessWidget {
  const _ReplyBar({
    required this.controller,
    required this.sending,
    required this.onSend,
    required this.onAttach,
    required this.pendingImage,
    required this.onRemoveImage,
  });
  final TextEditingController controller;
  final bool sending;
  final VoidCallback onSend;
  final VoidCallback onAttach;
  final File? pendingImage;
  final VoidCallback onRemoveImage;

  @override
  Widget build(BuildContext context) {
    return SafeArea(
      top: false,
      child: Container(
        padding: const EdgeInsets.fromLTRB(8, 8, 8, 8),
        decoration: BoxDecoration(
          color: AppTheme.surface,
          border: Border(top: BorderSide(color: AppTheme.line)),
        ),
        child: Column(mainAxisSize: MainAxisSize.min, children: [
          // Aperçu de la pièce jointe sélectionnée (avant envoi).
          if (pendingImage != null)
            Align(
              alignment: Alignment.centerLeft,
              child: Padding(
                padding: const EdgeInsets.only(bottom: 8, left: 6, top: 2),
                child: Stack(clipBehavior: Clip.none, children: [
                  ClipRRect(
                    borderRadius: BorderRadius.circular(10),
                    child: Image.file(pendingImage!, width: 64, height: 64, fit: BoxFit.cover),
                  ),
                  Positioned(
                    right: -6,
                    top: -6,
                    child: GestureDetector(
                      onTap: onRemoveImage,
                      child: Container(
                        decoration: const BoxDecoration(color: AppTheme.danger, shape: BoxShape.circle),
                        padding: const EdgeInsets.all(2),
                        child: const Icon(Icons.close, size: 14, color: Colors.white),
                      ),
                    ),
                  ),
                ]),
              ),
            ),
          Row(children: [
            IconButton(
              onPressed: sending ? null : onAttach,
              icon: const Icon(Icons.attach_file),
              tooltip: 'Joindre une photo',
              color: AppTheme.subtle,
            ),
            Expanded(
              child: TextField(
                controller: controller,
                minLines: 1,
                maxLines: 4,
                textInputAction: TextInputAction.newline,
                decoration: const InputDecoration(hintText: 'Répondre au litige…'),
              ),
            ),
            const SizedBox(width: 6),
            IconButton.filled(
              onPressed: sending ? null : onSend,
              icon: sending
                  ? const SizedBox(width: 18, height: 18, child: CircularProgressIndicator(strokeWidth: 2, color: Colors.white))
                  : const Icon(Icons.send),
            ),
          ]),
        ]),
      ),
    );
  }
}
