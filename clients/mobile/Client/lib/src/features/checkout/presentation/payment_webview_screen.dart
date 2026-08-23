import 'dart:async';

import 'package:flutter/material.dart';
import 'package:webview_flutter/webview_flutter.dart';

import '../../../core/theme/app_theme.dart';

/// Ouvre la page de paiement hébergée (FedaPay) dans une WebView intégrée.
///
/// Se referme automatiquement de trois façons :
///  1. [onPoll] : sondage RÉGULIER du statut serveur — dès que le paiement est
///     terminé, on ferme SANS que l'utilisateur ait à toucher « Terminé ». C'est
///     le mécanisme principal : la page FedaPay est une SPA qui, une fois payée,
///     ne déclenche aucune navigation détectable.
///  2. Interception d'une URL de retour connue (ceinture et bretelles).
///  3. Bouton « Terminé » / croix (repli manuel).
Future<void> openPaymentWebView(
  BuildContext context,
  String url, {
  Future<bool> Function()? onPoll,
}) {
  return Navigator.of(context).push<void>(
    MaterialPageRoute(builder: (_) => _PaymentWebViewScreen(url: url, onPoll: onPoll)),
  );
}

class _PaymentWebViewScreen extends StatefulWidget {
  const _PaymentWebViewScreen({required this.url, this.onPoll});
  final String url;

  /// Renvoie `true` quand le paiement a atteint un état TERMINAL (payé, échoué
  /// ou annulé) — on ferme alors la WebView.
  final Future<bool> Function()? onPoll;

  @override
  State<_PaymentWebViewScreen> createState() => _PaymentWebViewScreenState();
}

class _PaymentWebViewScreenState extends State<_PaymentWebViewScreen> {
  late final WebViewController _controller;
  bool _loading = true;
  Timer? _poll;
  bool _checking = false;

  static const _returnMarkers = [
    'payment/return',
    'payment/cancel',
    'fedapay.com/success',
    'fedapay.com/checkout/return',
    'status=approved',
    'status=declined',
    'status=canceled',
  ];

  @override
  void initState() {
    super.initState();
    _controller = WebViewController()
      ..setJavaScriptMode(JavaScriptMode.unrestricted)
      ..setNavigationDelegate(NavigationDelegate(
        onPageStarted: (_) {
          if (mounted) setState(() => _loading = true);
        },
        onPageFinished: (_) {
          if (mounted) setState(() => _loading = false);
        },
        onNavigationRequest: (request) {
          final u = request.url.toLowerCase();
          if (_returnMarkers.any(u.contains)) {
            if (mounted) Navigator.of(context).maybePop();
            return NavigationDecision.prevent;
          }
          return NavigationDecision.navigate;
        },
      ))
      ..loadRequest(Uri.parse(widget.url));

    _startPolling();
  }

  /// Sonde le statut serveur toutes les 3 s. Dès qu'il est terminal, on ferme la
  /// WebView : l'utilisateur n'a plus à toucher « Terminé ».
  void _startPolling() {
    if (widget.onPoll == null) return;
    _poll = Timer.periodic(const Duration(seconds: 3), (_) async {
      if (_checking || !mounted) return;
      _checking = true;
      try {
        final done = await widget.onPoll!.call();
        if (done && mounted) {
          _poll?.cancel();
          Navigator.of(context).maybePop();
        }
      } catch (_) {
        // Réseau hésitant : on réessaie au prochain tick.
      } finally {
        _checking = false;
      }
    });
  }

  @override
  void dispose() {
    _poll?.cancel();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: AppTheme.bg,
      appBar: AppBar(
        title: const Text('Paiement FedaPay'),
        leading: IconButton(icon: const Icon(Icons.close), onPressed: () => Navigator.of(context).maybePop()),
        actions: [
          TextButton(
            onPressed: () => Navigator.of(context).maybePop(),
            child: const Text('Terminé', style: TextStyle(color: AppTheme.brandGreen, fontWeight: FontWeight.w800)),
          ),
        ],
      ),
      body: Stack(children: [
        WebViewWidget(controller: _controller),
        if (_loading) const LinearProgressIndicator(color: AppTheme.brandGreen, backgroundColor: Color(0xFFE7F3EC)),
      ]),
    );
  }
}
