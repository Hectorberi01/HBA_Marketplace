/// ═════════════════════════════════════════════════════════════════════════════
/// TEMPS RÉEL DE LA MESSAGERIE — NEUTRALISÉ : LE HUB N'EXISTE PAS CÔTÉ SERVEUR.
///
/// CE N'EST PAS UNE DÉSACTIVATION TEMPORAIRE PAR PRUDENCE, C'EST UN CONSTAT.
///
/// Ces deux services ouvraient un WebSocket SignalR sur
/// `${AppConfig.baseUrl}/seller/hubs/chat` — le hub du MONOLITHE. Sur la
/// plateforme HBA :
///
///   • aucun service n'appelle `MapHub` : le seul vestige du sujet est
///     `IMessagingModuleApi`, qui prépare l'autorisation d'un futur `ChatHub`
///     (« cet utilisateur a-t-il le droit d'écouter cette conversation ? ») ;
///   • la passerelle n'a AUCUNE route vers un hub, donc aucun passage WebSocket.
///
/// Ce que produisait le code conservé : à chaque ouverture de conversation, une
/// négociation SignalR partait vers un chemin inexistant, échouait, et le `catch`
/// remettait `_conn = null` — silencieusement. Le fil paraissait « connecté en
/// temps réel » et ne recevait jamais rien ; seul le sondage de repli le
/// rafraîchissait, toutes les trente secondes. Un vendeur qui répond à un client
/// voyait donc ses messages arriver avec une demi-minute de retard, sans que
/// rien n'explique pourquoi.
///
/// NE PAS « RÉTABLIR » CES CONNEXIONS EN DEVINANT UNE URL. Il n'existe
/// aujourd'hui aucune adresse correcte à écrire. Le temps réel demande, côté
/// serveur : un hub dans communication-service, sa politique d'autorisation
/// branchée sur `IMessagingModuleApi`, et une route WebSocket à la passerelle.
/// Tant que ces trois pièces manquent, le SONDAGE est le seul mécanisme réel —
/// et il fonctionne. Les appelants (`MainShell`, `ChatScreen`) n'ont donc rien à
/// changer : ces méthodes gardent leur signature et ne font rien.
/// ═════════════════════════════════════════════════════════════════════════════
library;

/// Connexion SignalR au fil d'une conversation. Sans effet : voir l'en-tête.
class ChatRealtime {
  Future<void> connect({
    required String conversationId,
    required String? accessToken,
    required void Function() onMessage,
  }) async {
    // Sans amont : le rafraîchissement périodique de `ChatScreen` porte seul la
    // mise à jour du fil.
  }

  Future<void> dispose() async {
    // Rien à fermer : aucune connexion n'a été ouverte.
  }
}

/// Connexion « boîte de réception », qui alimentait le badge de non-lus.
/// Sans effet : voir l'en-tête.
class InboxRealtime {
  Future<void> connect({
    required String? accessToken,
    required void Function() onInbox,
  }) async {
    // Sans amont : le badge dépend du sondage de `MainShell`.
  }

  Future<void> dispose() async {
    // Rien à fermer : aucune connexion n'a été ouverte.
  }
}
