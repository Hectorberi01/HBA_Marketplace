import 'dart:async';
import 'dart:io';

import 'package:cached_network_image/cached_network_image.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:image_picker/image_picker.dart';

import 'package:hba_express_pro/l10n/app_localizations.dart';

import '../../../core/network/api_exception.dart';
import '../../../core/providers/core_providers.dart';
import '../../../core/theme/app_theme.dart';
import '../../../shared/utils/formatters.dart';
import '../../../shared/widgets/app_notify.dart';
import '../../../shared/widgets/async_views.dart';
import '../chat_realtime.dart';
import '../messaging_data.dart';

class ChatScreen extends ConsumerStatefulWidget {
  const ChatScreen({super.key, required this.conversationId});

  final String conversationId;

  /// `customer` A DISPARU DE LA SIGNATURE, ET CE N'EST PAS UN NETTOYAGE.
  ///
  /// L'écran recevait le nom de l'interlocuteur en « extra » du routeur. Ce nom
  /// venait de `Conversation.customer`, un champ absent du contrat : le titre
  /// affichait donc toujours la même chose sans que rien ne le dise. Le fil est
  /// désormais intitulé par un libellé assumé — cf. `conversations_screen.dart`.
  @override
  ConsumerState<ChatScreen> createState() => _ChatScreenState();
}

class _ChatScreenState extends ConsumerState<ChatScreen> {
  final _input = TextEditingController();
  final _scroll = ScrollController();
  final _realtime = ChatRealtime();

  Timer? _poll;
  bool _sending = false;

  @override
  void initState() {
    super.initState();
    _connect();

    // Repli : si le WebSocket est bloqué, on rafraîchit périodiquement. Un chat
    // qui ne se met pas à jour est pire qu'un chat un peu lent.
    _poll = Timer.periodic(const Duration(seconds: 20), (_) => _refresh());
  }

  Future<void> _connect() async {
    final token = await ref.read(tokenStorageProvider).accessToken;
    if (!mounted) return;
    await _realtime.connect(
      conversationId: widget.conversationId,
      accessToken: token,
      onMessage: _refresh,
    );
  }

  void _refresh() {
    if (!mounted) return;
    ref.invalidate(messagesProvider(widget.conversationId));
    // Ouvrir/relire le fil le marque comme lu côté serveur : le badge doit suivre.
    ref.invalidate(conversationsProvider);
  }

  @override
  void dispose() {
    _poll?.cancel();
    _realtime.dispose();
    _input.dispose();
    _scroll.dispose();
    super.dispose();
  }

  /// Descend en bas du fil : le dernier message est celui qui compte.
  void _scrollToBottom() {
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (!_scroll.hasClients) return;
      _scroll.jumpTo(_scroll.position.maxScrollExtent);
    });
  }

  final _picker = ImagePicker();

  Future<void> _send() async {
    final body = _input.text.trim();
    if (body.isEmpty || _sending) return;

    setState(() => _sending = true);
    try {
      await ref.read(messagingApiProvider).send(widget.conversationId, body);
      _input.clear();
      _refresh();
    } catch (e) {
      if (mounted) AppNotify.error(context, e.toString());
    } finally {
      if (mounted) setState(() => _sending = false);
    }
  }

  /// Joint une image : galerie → dépôt sur media-service → envoi du message avec
  /// le `mediaId` obtenu (le texte saisi devient la légende, s'il y en a un).
  Future<void> _attach() async {
    if (_sending) return;
    final picked = await _picker.pickImage(
      source: ImageSource.gallery,
      imageQuality: 82,
      maxWidth: 1600,
    );
    if (picked == null) return;

    setState(() => _sending = true);
    try {
      // LE DÉPÔT EXIGE L'IDENTIFIANT DE L'UTILISATEUR, PAS CELUI DU VENDEUR.
      //
      // media-service range la pièce sous le propriétaire `User` : il n'existe
      // ni `MediaOwnerType.Conversation`, ni `MediaType.MessageAttachment`. Sans
      // cet identifiant, le fichier partirait sous un propriétaire vide — déposé,
      // mais rattaché à personne, donc impossible à faire signer ensuite.
      final myUserId = await ref.read(currentUserIdProvider.future);
      if (myUserId.isEmpty) {
        throw ApiException(
          "Votre compte n'est pas encore résolu : réessayez dans un instant.",
        );
      }
      final attachment = await ref
          .read(messagingApiProvider)
          .uploadAttachment(File(picked.path), myUserId: myUserId);
      await ref
          .read(messagingApiProvider)
          .send(widget.conversationId, _input.text.trim(), attachments: [attachment]);
      _input.clear();
      _refresh();
    } catch (e) {
      if (mounted) AppNotify.error(context, e.toString());
    } finally {
      if (mounted) setState(() => _sending = false);
    }
  }

  /// Toutes les actions sur un message passent par ici : une 404 signifie que le
  /// serveur n'a pas encore la fonctionnalité (backend à redéployer), et non que
  /// le message a disparu — le dire évite une fausse panique.
  Future<void> _action(Future<void> Function() action) async {
    try {
      await action();
      _refresh();
    } on ApiException catch (e) {
      if (!mounted) return;
      final l = AppLocalizations.of(context);
      AppNotify.error(
        context,
        e.isNotFound ? l.msgActionUnavailable : e.message,
      );
    } catch (e) {
      if (mounted) AppNotify.error(context, e.toString());
    }
  }

  @override
  Widget build(BuildContext context) {
    final l = AppLocalizations.of(context);
    final messages = ref.watch(messagesProvider(widget.conversationId));

    return Scaffold(
      appBar: AppBar(title: Text(l.ordClient)),
      body: Column(
        children: [
          Expanded(
            child: messages.when(
              loading: () => const LoadingView(),
              error: (e, _) => ErrorView(
                message: e is ApiException ? e.message : e.toString(),
                isNotFound: e is ApiException && e.isNotFound,
                onRetry: _refresh,
              ),
              data: (list) {
                _scrollToBottom();
                if (list.isEmpty) {
                  return EmptyView(
                    message: l.msgEmpty,
                    icon: Icons.chat_bubble_outline,
                  );
                }
                return ListView.builder(
                  controller: _scroll,
                  padding: const EdgeInsets.fromLTRB(12, 16, 12, 12),
                  itemCount: list.length,
                  itemBuilder: (_, i) => _Bubble(
                    message: list[i],
                    onReact: (emoji) => _action(() => ref
                        .read(messagingApiProvider)
                        .react(widget.conversationId, list[i].id, emoji)),
                    onDeleteForEveryone: () => _action(() => ref
                        .read(messagingApiProvider)
                        .deleteForEveryone(widget.conversationId, list[i].id)),
                    onHideForMe: () => _action(() =>
                        ref.read(messagingApiProvider).hideForMe(widget.conversationId, list[i].id)),
                  ),
                );
              },
            ),
          ),
          _Composer(controller: _input, sending: _sending, onSend: _send, onAttach: _attach),
        ],
      ),
    );
  }
}

class _Bubble extends StatelessWidget {
  const _Bubble({
    required this.message,
    required this.onReact,
    required this.onDeleteForEveryone,
    required this.onHideForMe,
  });

  final Message message;
  final void Function(String emoji) onReact;
  final VoidCallback onDeleteForEveryone;
  final VoidCallback onHideForMe;

  @override
  Widget build(BuildContext context) {
    final l = AppLocalizations.of(context);
    final mine = message.fromMe;
    final colors = AppColors.of(context);

    return Align(
      alignment: mine ? Alignment.centerRight : Alignment.centerLeft,
      child: GestureDetector(
        // Appui long : le geste standard, déjà connu de tous les utilisateurs
        // de messagerie. Pas de bouton visible qui encombrerait chaque bulle.
        onLongPress: message.isDeleted ? null : () => _openActions(context),
        child: Container(
          constraints: BoxConstraints(maxWidth: MediaQuery.of(context).size.width * 0.78),
          margin: const EdgeInsets.symmetric(vertical: 4),
          padding: const EdgeInsets.fromLTRB(12, 10, 12, 8),
          decoration: BoxDecoration(
            color: message.isDeleted
                ? colors.bg
                : (mine ? AppTheme.brandGreen : colors.surface),
            borderRadius: BorderRadius.only(
              topLeft: const Radius.circular(16),
              topRight: const Radius.circular(16),
              bottomLeft: Radius.circular(mine ? 16 : 4),
              bottomRight: Radius.circular(mine ? 4 : 16),
            ),
            border: mine ? null : Border.all(color: colors.line),
          ),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            mainAxisSize: MainAxisSize.min,
            children: [
              // Pièces jointes (au-dessus de la légende).
              for (final attachment
                  in (message.isDeleted ? const <MessageAttachment>[] : message.attachments))
                Padding(
                  padding: const EdgeInsets.only(bottom: 6),
                  child: _Attachment(attachment: attachment),
                ),
              // Corps du message : masqué si vide (message purement image).
              if (message.isDeleted || message.body.isNotEmpty)
                Text(
                  message.isDeleted ? l.msgDeleted : message.body,
                  style: TextStyle(
                    fontSize: 14,
                    height: 1.35,
                    fontStyle: message.isDeleted ? FontStyle.italic : FontStyle.normal,
                    color: message.isDeleted
                        ? colors.subtle
                        : (mine ? Colors.white : colors.ink),
                  ),
                ),
              const SizedBox(height: 4),
              // Ligne « heure + accusé de lecture ». L'accusé (✓ / ✓✓) n'apparaît que
              // sur MES messages (côté vendeur) et jamais sur un message supprimé.
              Row(
                mainAxisSize: MainAxisSize.min,
                children: [
                  Text(
                    Format.shortWhen(message.sentAt),
                    style: TextStyle(
                      fontSize: 10,
                      color: mine && !message.isDeleted ? Colors.white70 : colors.subtle,
                    ),
                  ),
                  if (mine && !message.isDeleted) ...[
                    const SizedBox(width: 3),
                    _ReadReceipt(read: message.isRead),
                  ],
                ],
              ),
              if (message.reactions.isNotEmpty) ...[
                const SizedBox(height: 6),
                Wrap(
                  spacing: 4,
                  children: [
                    for (final r in message.reactions)
                      Container(
                        padding: const EdgeInsets.symmetric(horizontal: 7, vertical: 3),
                        decoration: BoxDecoration(
                          color: mine ? Colors.white24 : colors.bg,
                          borderRadius: BorderRadius.circular(12),
                          // La réaction « la mienne » est cerclée : sans ce repère,
                          // impossible de savoir si un nouveau clic ajoute ou retire.
                          border: r.mine ? Border.all(color: AppTheme.brandGreen, width: 1.2) : null,
                        ),
                        child: Text(
                          r.count > 1 ? '${r.emoji} ${r.count}' : r.emoji,
                          style: TextStyle(
                            fontSize: 12,
                            color: mine ? Colors.white : colors.ink,
                          ),
                        ),
                      ),
                  ],
                ),
              ],
            ],
          ),
        ),
      ),
    );
  }

  void _openActions(BuildContext context) {
    final colors = AppColors.of(context);
    showModalBottomSheet<void>(
      context: context,
      shape: const RoundedRectangleBorder(borderRadius: BorderRadius.vertical(top: Radius.circular(20))),
      builder: (sheetContext) {
        final l = AppLocalizations.of(sheetContext);
        return SafeArea(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            const SizedBox(height: 12),
            Row(
              mainAxisAlignment: MainAxisAlignment.spaceEvenly,
              children: [
                for (final emoji in kReactionPalette)
                  InkWell(
                    borderRadius: BorderRadius.circular(24),
                    onTap: () {
                      Navigator.pop(sheetContext);
                      onReact(emoji);
                    },
                    child: Padding(
                      padding: const EdgeInsets.all(8),
                      child: Text(emoji, style: const TextStyle(fontSize: 26)),
                    ),
                  ),
              ],
            ),
            const Divider(height: 20),

            // Masquer pour soi : toujours possible, y compris sur un message reçu.
            ListTile(
              leading: Icon(Icons.visibility_off_outlined, color: colors.subtle),
              title: Text(l.msgHideForMe),
              subtitle: Text(l.msgHideForMeHint),
              onTap: () {
                Navigator.pop(sheetContext);
                onHideForMe();
              },
            ),

            // Supprimer pour tous : réservé à SES propres messages. Le serveur le
            // vérifie de toute façon ; on n'affiche pas une action vouée à échouer.
            if (message.fromMe)
              ListTile(
                leading: const Icon(Icons.delete_outline, color: AppTheme.danger),
                title: Text(l.msgDeleteForEveryone,
                    style: const TextStyle(color: AppTheme.danger)),
                subtitle: Text(l.msgDeleteForEveryoneHint),
                onTap: () {
                  Navigator.pop(sheetContext);
                  onDeleteForEveryone();
                },
              ),
            const SizedBox(height: 8),
          ],
        ),
      );
      },
    );
  }
}

/// Une pièce jointe reçue.
///
/// ═══════════════════════════════════════════════════════════════════════════
/// UNE PIÈCE JOINTE N'EST PLUS UNE URL : ELLE SE FAIT SIGNER À L'AFFICHAGE.
///
/// La bulle parcourait `message.attachments` comme une liste de chaînes et la
/// passait à `CachedNetworkImage`. Le contrat rend des OBJETS
/// (`mediaId`, `type`, `legacyUrl`) : l'`imageUrl` recevait « Instance of
/// '_Map' » et chaque image s'affichait cassée.
///
/// Le fichier est PRIVÉ côté media-service, et communication-service n'expose
/// aucune route de téléchargement : l'URL se demande à l'ouverture et n'est
/// valable qu'un temps court (voir `MessagingApi.attachmentUrl`).
///
/// D'où un état plutôt qu'un simple `ConsumerWidget` : une `Future` recréée à
/// chaque `build` redemanderait une signature à chaque reconstruction du fil —
/// c'est-à-dire toutes les vingt secondes, au rythme du sondage.
///
/// SEULES LES IMAGES SONT RENDUES. Les autres natures (`Document`, `Audio`…)
/// sont ANNONCÉES sans être ouvrables : l'application n'a pas de visualiseur, et
/// déléguer au navigateur une URL qui expire donnerait une page d'erreur.
/// ═══════════════════════════════════════════════════════════════════════════
class _Attachment extends ConsumerStatefulWidget {
  const _Attachment({required this.attachment});

  final MessageAttachment attachment;

  @override
  ConsumerState<_Attachment> createState() => _AttachmentState();
}

class _AttachmentState extends ConsumerState<_Attachment> {
  late final Future<String> _url;

  @override
  void initState() {
    super.initState();
    _url = ref.read(messagingApiProvider).attachmentUrl(widget.attachment);
  }

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);

    if (!widget.attachment.isImage) {
      return Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          Icon(Icons.attach_file, size: 15, color: colors.subtle),
          const SizedBox(width: 6),
          Text('Pièce jointe', style: TextStyle(fontSize: 13, color: colors.subtle)),
        ],
      );
    }

    return FutureBuilder<String>(
      future: _url,
      builder: (context, snapshot) {
        final url = snapshot.data;
        if (url == null) {
          return Container(
            height: snapshot.hasError ? 110 : 150,
            width: 200,
            alignment: Alignment.center,
            color: colors.bg,
            child: snapshot.hasError
                ? Icon(Icons.broken_image_outlined, color: colors.subtle)
                : const CircularProgressIndicator(strokeWidth: 2),
          );
        }
        return GestureDetector(
          // Tap = ouvrir l'image en plein écran DANS l'app (comme WhatsApp),
          // avec zoom, sans quitter la conversation.
          onTap: () => _openImageViewer(context, url),
          child: ClipRRect(
            borderRadius: BorderRadius.circular(10),
            // Même rendu que les photos produit : CachedNetworkImage gère le
            // cache et le chargement réseau là où Image.network échoue en silence.
            child: CachedNetworkImage(
              imageUrl: url,
              width: 200,
              fit: BoxFit.cover,
              placeholder: (_, __) => const SizedBox(
                height: 150,
                width: 200,
                child: Center(child: CircularProgressIndicator(strokeWidth: 2)),
              ),
              errorWidget: (_, __, ___) => Container(
                height: 110,
                width: 200,
                alignment: Alignment.center,
                color: colors.bg,
                child: Icon(Icons.broken_image_outlined, color: colors.subtle),
              ),
            ),
          ),
        );
      },
    );
  }
}

class _Composer extends StatelessWidget {
  const _Composer({
    required this.controller,
    required this.sending,
    required this.onSend,
    required this.onAttach,
  });

  final TextEditingController controller;
  final bool sending;
  final VoidCallback onSend;
  final VoidCallback onAttach;

  @override
  Widget build(BuildContext context) {
    final l = AppLocalizations.of(context);
    final colors = AppColors.of(context);
    return SafeArea(
      top: false,
      child: Container(
        padding: const EdgeInsets.fromLTRB(6, 8, 12, 8),
        decoration: BoxDecoration(
          color: colors.surface,
          border: Border(top: BorderSide(color: colors.line)),
        ),
        child: Row(
          children: [
            IconButton(
              icon: Icon(Icons.add_photo_alternate_outlined, color: colors.subtle),
              tooltip: l.msgAttachImage,
              onPressed: sending ? null : onAttach,
            ),
            Expanded(
              child: TextField(
                controller: controller,
                minLines: 1,
                maxLines: 4,
                textCapitalization: TextCapitalization.sentences,
                decoration: InputDecoration(
                  hintText: l.msgInputHint,
                  contentPadding: const EdgeInsets.symmetric(horizontal: 14, vertical: 10),
                ),
                onSubmitted: (_) => onSend(),
              ),
            ),
            const SizedBox(width: 8),
            Material(
              color: AppTheme.brandGreen,
              shape: const CircleBorder(),
              child: InkWell(
                customBorder: const CircleBorder(),
                onTap: sending ? null : onSend,
                child: Padding(
                  padding: const EdgeInsets.all(12),
                  child: sending
                      ? const SizedBox(
                          width: 20,
                          height: 20,
                          child: CircularProgressIndicator(strokeWidth: 2.2, color: Colors.white),
                        )
                      : const Icon(Icons.send, color: Colors.white, size: 20),
                ),
              ),
            ),
          ],
        ),
      ),
    );
  }
}

/// Ouvre une image en plein écran DANS l'app (fond sombre, zoom au pincement,
/// sans quitter la conversation) — comme WhatsApp.
void _openImageViewer(BuildContext context, String url) {
  Navigator.of(context).push(
    PageRouteBuilder(
      opaque: false,
      barrierColor: Colors.black.withValues(alpha: 0.92),
      pageBuilder: (_, __, ___) => _FullScreenImage(url: url),
    ),
  );
}

class _FullScreenImage extends StatelessWidget {
  const _FullScreenImage({required this.url});
  final String url;

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: Colors.transparent,
      body: Stack(
        children: [
          // Tap simple = fermer ; pincement = zoomer.
          GestureDetector(
            onTap: () => Navigator.of(context).pop(),
            child: InteractiveViewer(
              minScale: 1,
              maxScale: 5,
              child: Center(
                child: CachedNetworkImage(
                  imageUrl: url,
                  fit: BoxFit.contain,
                  placeholder: (_, __) =>
                      const Center(child: CircularProgressIndicator(color: Colors.white)),
                  errorWidget: (_, __, ___) =>
                      const Icon(Icons.broken_image_outlined, color: Colors.white54, size: 56),
                ),
              ),
            ),
          ),
          Positioned(
            top: MediaQuery.of(context).padding.top + 8,
            left: 4,
            child: IconButton(
              icon: const Icon(Icons.close, color: Colors.white, size: 28),
              onPressed: () => Navigator.of(context).pop(),
            ),
          ),
        ],
      ),
    );
  }
}

/// Accusé de lecture façon WhatsApp, affiché sous MES messages (côté vendeur).
///
/// Deux états seulement — on ne suit pas le « remis sur l'appareil » (il faudrait
/// un accusé de réception du terminal de l'acheteur, qu'on n'a pas) :
///   • [read] == false → un seul ✓ (`Icons.done`), gris : le message est parti et
///     persisté côté serveur, mais l'acheteur ne l'a pas encore ouvert.
///   • [read] == true  → double ✓✓ (`Icons.done_all`), bleu clair : l'acheteur a
///     ouvert la conversation (le serveur a posé `ReadAtUtc` via
///     MarkConversationReadCommand), donc il a vu le message.
///
/// Le bleu clair est choisi pour ressortir sur la bulle verte du vendeur, là où le
/// bleu vif de WhatsApp (pensé pour une bulle claire) passerait inaperçu.
class _ReadReceipt extends StatelessWidget {
  const _ReadReceipt({required this.read});

  final bool read;

  // Bleu clair lisible sur la bulle verte ; gris translucide pour « pas encore lu ».
  static const _readColor = Color(0xFF8FD3FF);
  static const _sentColor = Colors.white70;

  @override
  Widget build(BuildContext context) {
    return Icon(
      read ? Icons.done_all : Icons.done,
      size: 14,
      color: read ? _readColor : _sentColor,
      // Lecteurs d'écran : annonce l'état plutôt que « icône coche ».
      semanticLabel: read ? 'Lu' : 'Envoyé',
    );
  }
}
