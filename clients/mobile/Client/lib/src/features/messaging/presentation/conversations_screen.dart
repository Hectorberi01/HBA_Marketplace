import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../../core/theme/app_theme.dart';
import '../../../shared/widgets/async_views.dart';
import '../messaging_data.dart';

class ConversationsScreen extends ConsumerStatefulWidget {
  const ConversationsScreen({super.key});

  @override
  ConsumerState<ConversationsScreen> createState() => _ConversationsScreenState();
}

class _ConversationsScreenState extends ConsumerState<ConversationsScreen> {
  String _query = '';
  Timer? _poll;

  @override
  void initState() {
    super.initState();
    // Quasi temps réel : rafraîchit la liste toutes les 5 s.
    _poll = Timer.periodic(const Duration(seconds: 5), (_) {
      if (mounted) ref.invalidate(conversationsProvider);
    });
  }

  @override
  void dispose() {
    _poll?.cancel();
    super.dispose();
  }

  /// Nouvelle discussion : on écrit à un vendeur depuis sa boutique. On guide
  /// donc le client vers la recherche/les boutiques (pas de destinataire libre).
  void _showNewConversation() {
    showModalBottomSheet(
      context: context,
      backgroundColor: AppTheme.surface,
      shape: const RoundedRectangleBorder(borderRadius: BorderRadius.vertical(top: Radius.circular(20))),
      builder: (sheetContext) => SafeArea(
        child: Padding(
          padding: const EdgeInsets.fromLTRB(20, 20, 20, 24),
          child: Column(mainAxisSize: MainAxisSize.min, crossAxisAlignment: CrossAxisAlignment.stretch, children: [
            const Icon(Icons.forum_outlined, size: 40, color: AppTheme.brandGreen),
            const SizedBox(height: 12),
            const Text('Nouvelle discussion',
                textAlign: TextAlign.center, style: TextStyle(fontSize: 18, fontWeight: FontWeight.w800)),
            const SizedBox(height: 8),
            Text(
              'Pour contacter un vendeur, ouvre sa boutique depuis un produit et touche « Contacter le vendeur ».',
              textAlign: TextAlign.center,
              style: TextStyle(color: AppTheme.subtle, height: 1.4),
            ),
            const SizedBox(height: 18),
            FilledButton.icon(
              onPressed: () {
                Navigator.pop(sheetContext);
                context.push('/search');
              },
              style: FilledButton.styleFrom(minimumSize: const Size.fromHeight(50)),
              icon: const Icon(Icons.search, size: 18),
              label: const Text('Explorer les produits'),
            ),
          ]),
        ),
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    final conversations = ref.watch(conversationsProvider);
    return Scaffold(
      backgroundColor: AppTheme.bg,
      appBar: AppBar(
        title: const Text('Messages'),
        actions: [
          IconButton(
            icon: const Icon(Icons.edit_square),
            tooltip: 'Nouvelle discussion',
            onPressed: _showNewConversation,
          ),
          const SizedBox(width: 4),
        ],
      ),
      body: RefreshIndicator(
        onRefresh: () async => ref.refresh(conversationsProvider.future),
        child: conversations.when(
          loading: () => const LoadingView(),
          error: (e, _) => ErrorView(message: e.toString(), onRetry: () => ref.invalidate(conversationsProvider)),
          data: (all) {
            final list = _query.isEmpty
                ? all
                : all.where((c) => c.title.toLowerCase().contains(_query) || c.lastMessage.toLowerCase().contains(_query)).toList();
            return ListView(
              children: [
                Padding(
                  padding: const EdgeInsets.fromLTRB(16, 8, 16, 8),
                  child: TextField(
                    onChanged: (v) => setState(() => _query = v.toLowerCase()),
                    decoration: InputDecoration(
                      hintText: 'Rechercher une discussion…',
                      prefixIcon: Icon(Icons.search, color: AppTheme.subtle),
                      filled: true,
                      fillColor: AppTheme.bg,
                      border: OutlineInputBorder(borderRadius: BorderRadius.circular(12), borderSide: BorderSide.none),
                      enabledBorder: OutlineInputBorder(borderRadius: BorderRadius.circular(12), borderSide: BorderSide.none),
                    ),
                  ),
                ),
                if (list.isEmpty)
                  const Padding(padding: EdgeInsets.only(top: 80), child: EmptyView(message: 'Aucune conversation.', icon: Icons.chat_bubble_outline))
                else
                  for (final c in list) _ConversationRow(c: c),
                if (list.isNotEmpty) ...[
                  const SizedBox(height: 20),
                  Center(
                    child: Row(mainAxisAlignment: MainAxisAlignment.center, children: [
                      Icon(Icons.archive_outlined, size: 16, color: AppTheme.subtle),
                      const SizedBox(width: 6),
                      Text('VOIR LES DISCUSSIONS ARCHIVÉES', style: TextStyle(color: AppTheme.subtle, fontSize: 12, fontWeight: FontWeight.w700, letterSpacing: 0.3)),
                    ]),
                  ),
                ],
                const SizedBox(height: 24),
              ],
            );
          },
        ),
      ),
    );
  }
}

class _ConversationRow extends StatelessWidget {
  const _ConversationRow({required this.c});
  final Conversation c;

  @override
  Widget build(BuildContext context) {
    final unread = c.unread > 0;
    return InkWell(
      onTap: () => context.push('/chat/${c.id}'),
      child: Container(
        color: unread ? AppTheme.softGreen : AppTheme.surface,
        padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 12),
        child: Row(children: [
          CircleAvatar(
            radius: 26,
            backgroundColor: AppTheme.softGreen,
            child: Text(c.title.isNotEmpty ? c.title.characters.first.toUpperCase() : '?',
                style: const TextStyle(color: AppTheme.brandGreen, fontWeight: FontWeight.w800)),
          ),
          const SizedBox(width: 12),
          Expanded(
            child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
              Text(c.title, maxLines: 1, overflow: TextOverflow.ellipsis,
                  style: TextStyle(fontWeight: unread ? FontWeight.w800 : FontWeight.w700, color: AppTheme.ink)),
              const SizedBox(height: 2),
              Text(c.lastMessage, maxLines: 1, overflow: TextOverflow.ellipsis,
                  style: TextStyle(color: unread ? AppTheme.ink : AppTheme.subtle, fontSize: 13, fontWeight: unread ? FontWeight.w600 : FontWeight.w400)),
            ]),
          ),
          const SizedBox(width: 8),
          Column(crossAxisAlignment: CrossAxisAlignment.end, children: [
            Text(_time(c.updatedAt), style: TextStyle(fontSize: 12, color: unread ? AppTheme.brandGreen : AppTheme.subtle, fontWeight: unread ? FontWeight.w700 : FontWeight.w400)),
            const SizedBox(height: 6),
            if (unread)
              Container(
                padding: const EdgeInsets.all(6),
                decoration: const BoxDecoration(color: AppTheme.brandGreen, shape: BoxShape.circle),
                constraints: const BoxConstraints(minWidth: 20, minHeight: 20),
                alignment: Alignment.center,
                child: Text('${c.unread}', style: const TextStyle(color: Colors.white, fontSize: 11, fontWeight: FontWeight.w700)),
              )
            else
              Icon(Icons.done_all, size: 16, color: AppTheme.subtle),
          ]),
        ]),
      ),
    );
  }

  String _time(DateTime? d) {
    if (d == null) return '';
    final local = d.toLocal();
    final now = DateTime.now();
    final sameDay = local.year == now.year && local.month == now.month && local.day == now.day;
    if (sameDay) return '${local.hour.toString().padLeft(2, '0')}:${local.minute.toString().padLeft(2, '0')}';
    final diff = now.difference(local).inDays;
    if (diff == 1) return 'Hier';
    if (diff < 7) {
      const days = ['Lun.', 'Mar.', 'Mer.', 'Jeu.', 'Ven.', 'Sam.', 'Dim.'];
      return days[local.weekday - 1];
    }
    return '${local.day}/${local.month}';
  }
}
