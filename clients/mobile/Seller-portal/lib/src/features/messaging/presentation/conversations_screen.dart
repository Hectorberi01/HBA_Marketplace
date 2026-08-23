import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import 'package:hba_express_pro/l10n/app_localizations.dart';

import '../../../core/theme/app_theme.dart';
import '../../../shared/utils/formatters.dart';
import '../../../shared/widgets/async_views.dart';
import '../messaging_data.dart';

/// Liste des fils, à la WhatsApp : on arrive TOUJOURS sur la liste, jamais
/// directement dans une conversation — sinon on ne sait pas d'où l'on vient,
/// ni comment revenir.
class ConversationsScreen extends ConsumerWidget {
  const ConversationsScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final l = AppLocalizations.of(context);
    final conversations = ref.watch(conversationsProvider);
    final colors = AppColors.of(context);

    return Scaffold(
      appBar: AppBar(title: Text(l.navMessages)),
      body: conversations.when(
        loading: () => const LoadingView(),
        error: (e, _) => ErrorView(
          message: e.toString(),
          onRetry: () => ref.invalidate(conversationsProvider),
        ),
        data: (list) => RefreshIndicator(
          onRefresh: () async => ref.invalidate(conversationsProvider),
          child: list.isEmpty
              ? EmptyView(
                  message: l.msgNoConversations,
                  icon: Icons.forum_outlined,
                )
              : ListView.separated(
                  itemCount: list.length,
                  separatorBuilder: (_, __) => Divider(height: 1, indent: 76, color: colors.line),
                  itemBuilder: (_, i) => _ConversationTile(conversation: list[i]),
                ),
        ),
      ),
    );
  }
}

class _ConversationTile extends StatelessWidget {
  const _ConversationTile({required this.conversation});
  final Conversation conversation;

  @override
  Widget build(BuildContext context) {
    final l = AppLocalizations.of(context);
    final unread = conversation.unread > 0;
    final colors = AppColors.of(context);

    // ═════════════════════════════════════════════════════════════════════════
    // IL N'Y A AUCUN NOM D'INTERLOCUTEUR, ET LA TUILE LE DIT MAINTENANT.
    //
    // `conversation.customer` lisait un champ que `ConversationSummary` ne rend
    // pas : la valeur retombait sur « Client » pour TOUS les fils, sans que rien
    // ne l'indique. Le contrat ne porte que des `participantIds` (GUID) — voir
    // `Conversation.participantIds`. Résoudre le nom demanderait un appel à
    // user-service par participant, sur des profils qu'un vendeur n'a pas à
    // consulter en bloc.
    //
    // L'initiale de l'avatar cède donc la place à une icône : une lettre tirée
    // d'un libellé fixe donnerait à chaque fil l'air de porter un vrai nom.
    // ═════════════════════════════════════════════════════════════════════════
    return ListTile(
      onTap: () => context.push('/chat/${conversation.id}'),
      contentPadding: const EdgeInsets.symmetric(horizontal: 16, vertical: 6),
      leading: CircleAvatar(
        radius: 24,
        backgroundColor: colors.softGreen,
        child: const Icon(Icons.person_outline, color: AppTheme.brandGreen, size: 24),
      ),
      title: Text(
        l.ordClient,
        maxLines: 1,
        overflow: TextOverflow.ellipsis,
        style: TextStyle(
          fontWeight: unread ? FontWeight.w800 : FontWeight.w600,
          fontSize: 15,
          color: colors.ink,
        ),
      ),
      subtitle: Text(
        conversation.lastMessage.isEmpty ? l.msgNewThread : conversation.lastMessage,
        maxLines: 1,
        overflow: TextOverflow.ellipsis,
        style: TextStyle(
          fontSize: 13,
          color: unread ? colors.ink : colors.subtle,
          fontWeight: unread ? FontWeight.w600 : FontWeight.w400,
        ),
      ),
      trailing: Column(
        mainAxisAlignment: MainAxisAlignment.center,
        crossAxisAlignment: CrossAxisAlignment.end,
        children: [
          Text(Format.shortWhen(conversation.lastAt),
              style: TextStyle(
                fontSize: 11,
                color: unread ? AppTheme.brandGreen : colors.subtle,
                fontWeight: unread ? FontWeight.w700 : FontWeight.w500,
              )),
          const SizedBox(height: 6),
          if (unread)
            Container(
              padding: const EdgeInsets.symmetric(horizontal: 7, vertical: 2),
              constraints: const BoxConstraints(minWidth: 20),
              decoration: const BoxDecoration(color: AppTheme.brandGreen, shape: BoxShape.circle),
              child: Text('${conversation.unread}',
                  textAlign: TextAlign.center,
                  style: const TextStyle(color: Colors.white, fontSize: 11, fontWeight: FontWeight.w700)),
            )
          else
            const SizedBox(height: 20),
        ],
      ),
    );
  }
}
