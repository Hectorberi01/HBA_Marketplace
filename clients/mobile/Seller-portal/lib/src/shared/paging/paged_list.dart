import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:hba_express_pro/l10n/app_localizations.dart';

import '../../core/theme/app_theme.dart';
import '../widgets/async_views.dart';

/// État d'une liste paginée + recherchable.
@immutable
class PagedState<T> {
  const PagedState({
    this.items = const [],
    this.search = '',
    this.loading = false,
    this.loadingMore = false,
    this.hasMore = true,
    this.error,
  });

  final List<T> items;
  final String search;
  final bool loading;
  final bool loadingMore;

  /// Faux dès qu'une page revient incomplète : c'est le signal de fin.
  final bool hasMore;

  final String? error;

  PagedState<T> copyWith({
    List<T>? items,
    String? search,
    bool? loading,
    bool? loadingMore,
    bool? hasMore,
    String? error,
    bool clearError = false,
  }) =>
      PagedState<T>(
        items: items ?? this.items,
        search: search ?? this.search,
        loading: loading ?? this.loading,
        loadingMore: loadingMore ?? this.loadingMore,
        hasMore: hasMore ?? this.hasMore,
        error: clearError ? null : (error ?? this.error),
      );
}

/// Contrôleur de liste paginée, partagé par Commandes et Produits.
///
/// Le serveur renvoie un tableau simple, sans total ni curseur : on en déduit la
/// fin quand une page revient avec MOINS d'éléments que demandé. C'est fiable et
/// n'exige aucune enveloppe côté API.
///
/// La recherche est DÉBOUNCÉE : sans cela, chaque frappe déclencherait un appel
/// réseau, et les réponses pourraient revenir dans le désordre — le vendeur
/// verrait alors les résultats d'une recherche qu'il a déjà corrigée.
abstract class PagedNotifier<T> extends AutoDisposeNotifier<PagedState<T>> {
  static const pageSize = 30;

  Timer? _debounce;
  int _page = 1;

  /// Jeton de requête : seule la réponse de la DERNIÈRE demande est appliquée.
  int _requestId = 0;

  /// À implémenter : un appel réseau paginé.
  Future<List<T>> fetch({required int page, required int pageSize, required String search});

  @override
  PagedState<T> build() {
    ref.onDispose(() => _debounce?.cancel());
    Future.microtask(refresh);

    // PAS de `const PagedState()` : dans une expression constante, `T` n'existe
    // pas encore, et Dart infère `PagedState<Never>`. Le type ne se voit qu'à
    // l'exécution — la première page revient, et l'affectation explose :
    // « List<SellerOrder> is not a subtype of List<Never> ». On instancie donc
    // le type EXPLICITEMENT.
    return PagedState<T>();
  }

  /// Recharge depuis la première page (pull-to-refresh, changement de recherche).
  Future<void> refresh() async {
    final id = ++_requestId;
    _page = 1;
    state = state.copyWith(loading: true, clearError: true);

    try {
      final items = await fetch(page: 1, pageSize: pageSize, search: state.search);
      if (id != _requestId) return; // une demande plus récente a pris la main

      state = state.copyWith(
        items: items,
        loading: false,
        hasMore: items.length >= pageSize,
      );
    } catch (e) {
      if (id != _requestId) return;
      state = state.copyWith(loading: false, error: e.toString());
    }
  }

  /// Page suivante (fin de liste atteinte au scroll).
  Future<void> loadMore() async {
    if (state.loading || state.loadingMore || !state.hasMore) return;

    final id = ++_requestId;
    state = state.copyWith(loadingMore: true);

    try {
      final next = await fetch(page: _page + 1, pageSize: pageSize, search: state.search);
      if (id != _requestId) return;

      _page++;
      state = state.copyWith(
        items: [...state.items, ...next],
        loadingMore: false,
        hasMore: next.length >= pageSize,
      );
    } catch (e) {
      if (id != _requestId) return;
      // Un échec de page suivante ne doit PAS effacer ce qui est déjà affiché.
      state = state.copyWith(loadingMore: false, error: e.toString());
    }
  }

  /// Recherche : on attend que la frappe se calme avant d'appeler le serveur.
  void search(String value) {
    state = state.copyWith(search: value);

    _debounce?.cancel();
    _debounce = Timer(const Duration(milliseconds: 350), refresh);
  }
}

/// Barre de recherche.
class SearchField extends StatelessWidget {
  const SearchField({super.key, required this.hint, required this.onChanged, this.value = ''});

  final String hint;
  final String value;
  final ValueChanged<String> onChanged;

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);
    return Padding(
      padding: const EdgeInsets.fromLTRB(16, 12, 16, 4),
      child: TextField(
        onChanged: onChanged,
        textInputAction: TextInputAction.search,
        decoration: InputDecoration(
          hintText: hint,
          prefixIcon: Icon(Icons.search, color: colors.subtle),
          isDense: true,
          contentPadding: const EdgeInsets.symmetric(horizontal: 14, vertical: 12),
        ),
      ),
    );
  }
}

/// Liste paginée : charge la page suivante à l'approche du bas.
///
/// Le déclenchement se fait AVANT d'atteindre le bord (200 px) : attendre le
/// dernier pixel imposerait au vendeur une attente visible à chaque page.
class PagedListView<T> extends StatefulWidget {
  const PagedListView({
    super.key,
    required this.state,
    required this.itemBuilder,
    required this.onLoadMore,
    required this.onRefresh,
    required this.emptyMessage,
    this.emptyIcon = Icons.inbox_outlined,
    this.header,
    this.padding = const EdgeInsets.fromLTRB(16, 4, 16, 24),
  });

  final PagedState<T> state;
  final Widget Function(BuildContext, T) itemBuilder;
  final VoidCallback onLoadMore;
  final Future<void> Function() onRefresh;
  final String emptyMessage;
  final IconData emptyIcon;
  final Widget? header;
  final EdgeInsets padding;

  @override
  State<PagedListView<T>> createState() => _PagedListViewState<T>();
}

class _PagedListViewState<T> extends State<PagedListView<T>> {
  final _scroll = ScrollController();

  @override
  void initState() {
    super.initState();
    _scroll.addListener(_onScroll);
  }

  @override
  void dispose() {
    _scroll.dispose();
    super.dispose();
  }

  void _onScroll() {
    if (!_scroll.hasClients) return;
    if (_scroll.position.pixels >= _scroll.position.maxScrollExtent - 200) {
      widget.onLoadMore();
    }
  }

  @override
  Widget build(BuildContext context) {
    final s = widget.state;
    final l = AppLocalizations.of(context);

    if (s.loading && s.items.isEmpty) return const LoadingView();

    // Une erreur n'efface pas ce qui est déjà affiché : on ne montre l'écran
    // d'erreur que si le vendeur n'a rien sous les yeux.
    if (s.error != null && s.items.isEmpty) {
      return ErrorView(message: s.error!, onRetry: widget.onRefresh);
    }

    if (s.items.isEmpty) {
      return RefreshIndicator(
        onRefresh: widget.onRefresh,
        child: ListView(
          children: [
            if (widget.header != null) widget.header!,
            SizedBox(
              height: MediaQuery.of(context).size.height * 0.45,
              child: EmptyView(
                message: s.search.isEmpty ? widget.emptyMessage : l.commonNoResultsFor(s.search),
                icon: widget.emptyIcon,
              ),
            ),
          ],
        ),
      );
    }

    return RefreshIndicator(
      onRefresh: widget.onRefresh,
      child: ListView.separated(
        controller: _scroll,
        padding: widget.padding,
        // +1 pour l'indicateur de chargement de la page suivante.
        itemCount: s.items.length + (s.loadingMore ? 1 : 0),
        separatorBuilder: (_, __) => const SizedBox(height: 10),
        itemBuilder: (context, i) {
          if (i >= s.items.length) {
            return const Padding(
              padding: EdgeInsets.symmetric(vertical: 16),
              child: Center(child: CircularProgressIndicator(strokeWidth: 2.4)),
            );
          }
          return widget.itemBuilder(context, s.items[i]);
        },
      ),
    );
  }
}
