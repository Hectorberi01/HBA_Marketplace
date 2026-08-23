import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../core/theme/app_theme.dart';
import '../commune_data.dart';

/// ─────────────────────────────────────────────────────────────────────────────
/// SÉLECTEUR DE COMMUNE — LISTE FERMÉE, PAS UN CHAMP LIBRE.
///
/// C'est le seul champ structuré de l'adresse, donc le seul qui puisse un jour
/// servir à calculer des frais par zone. Le laisser en texte libre a produit
/// « Cotonou », « cotonou » et « COTONOU » dans la même colonne, et rendu la
/// donnée inexploitable.
///
/// 77 entrées : trop pour un menu déroulant, assez peu pour tout charger. D'où
/// une feuille plein écran avec recherche, où l'on peut taper « seme podji »
/// sans accent ni tiret — ce que fait n'importe qui sur un clavier de téléphone.
/// ─────────────────────────────────────────────────────────────────────────────
class CommuneField extends ConsumerWidget {
  const CommuneField({
    super.key,
    required this.selectedCode,
    required this.onSelected,
    this.label = 'Commune',
  });

  final String? selectedCode;
  final ValueChanged<String> onSelected;
  final String label;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final communes = ref.watch(communesProvider);

    // Boucle explicite plutôt que `firstOrNull` : celui-ci vient de
    // package:collection, dont on ne suppose pas la présence dans ce projet.
    Commune? selected;
    for (final c in communes.valueOrNull ?? const <Commune>[]) {
      if (c.code == selectedCode) {
        selected = c;
        break;
      }
    }

    // Repli honnête : si le référentiel n'est pas encore chargé mais qu'une
    // commune est déjà choisie, on affiche son code plutôt qu'un champ vide qui
    // donnerait l'impression que la saisie a été perdue.
    final display = selected?.name ?? (selectedCode?.isNotEmpty == true ? selectedCode! : null);

    return InkWell(
      onTap: communes.hasValue ? () => _open(context, communes.value!) : null,
      borderRadius: BorderRadius.circular(8),
      child: InputDecorator(
        decoration: InputDecoration(
          labelText: label,
          suffixIcon: communes.isLoading
              ? const Padding(
                  padding: EdgeInsets.all(12),
                  child: SizedBox(height: 16, width: 16, child: CircularProgressIndicator(strokeWidth: 2)),
                )
              : const Icon(Icons.arrow_drop_down),
          errorText: communes.hasError ? 'Liste des communes indisponible.' : null,
        ),
        child: Text(
          display ?? 'Choisir une commune',
          style: TextStyle(
            color: display == null ? Theme.of(context).hintColor : null,
            fontWeight: display == null ? null : FontWeight.w600,
          ),
        ),
      ),
    );
  }

  Future<void> _open(BuildContext context, List<Commune> all) async {
    final code = await showModalBottomSheet<String>(
      context: context,
      isScrollControlled: true,
      useSafeArea: true,
      builder: (_) => _CommuneSheet(all: all, selectedCode: selectedCode),
    );
    if (code != null) onSelected(code);
  }
}

class _CommuneSheet extends StatefulWidget {
  const _CommuneSheet({required this.all, this.selectedCode});

  final List<Commune> all;
  final String? selectedCode;

  @override
  State<_CommuneSheet> createState() => _CommuneSheetState();
}

class _CommuneSheetState extends State<_CommuneSheet> {
  final _search = TextEditingController();
  late List<Commune> _shown = widget.all;

  @override
  void dispose() {
    _search.dispose();
    super.dispose();
  }

  void _filter(String raw) {
    // On replie la requête comme les clés de recherche : taper « seme » trouve
    // « Sèmè-Podji », et « calavi » trouve « Abomey-Calavi ».
    final q = Commune.fold(raw);
    setState(() {
      _shown = q.isEmpty ? widget.all : widget.all.where((c) => c.searchKey.contains(q)).toList();
    });
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    // Hauteur de la barre de navigation Android. Depuis que l'application cible
    // l'API 36, l'affichage est bord à bord et le système ne réserve plus cet
    // espace : sans ce rembourrage, la dernière commune de la liste passe SOUS
    // les boutons du téléphone et devient intouchable.
    final bottomInset = MediaQuery.viewPaddingOf(context).bottom;

    return DraggableScrollableSheet(
      initialChildSize: 0.85,
      maxChildSize: 0.95,
      expand: false,
      builder: (_, controller) => Column(
        children: [
          // ─────────────────────────────────────────────────────────────────
          // EN-TÊTE FIXE, SÉPARÉ DU CONTENU PAR UN TRAIT.
          //
          // Sans lui, la liste défilait DERRIÈRE le champ de recherche : on
          // voyait une commune à demi masquée sous le titre, sans comprendre si
          // elle était sélectionnable. Le trait rend la limite explicite, et le
          // fond opaque garantit que rien ne transparaît.
          // ─────────────────────────────────────────────────────────────────
          Container(
            decoration: BoxDecoration(
              color: theme.bottomSheetTheme.backgroundColor ?? theme.colorScheme.surface,
              border: Border(bottom: BorderSide(color: theme.dividerColor)),
            ),
            padding: const EdgeInsets.fromLTRB(20, 4, 20, 14),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                Row(
                  children: [
                    const Expanded(
                      child: Text('Choisir la commune',
                          style: TextStyle(fontSize: 18, fontWeight: FontWeight.w800)),
                    ),
                    // Sortie explicite : sur iOS, le glissement vers le bas est
                    // le seul moyen de fermer, et il n'est pas découvrable.
                    IconButton(
                      onPressed: () => Navigator.pop(context),
                      icon: const Icon(Icons.close),
                      tooltip: 'Fermer',
                      visualDensity: VisualDensity.compact,
                    ),
                  ],
                ),
                const SizedBox(height: 8),
                TextField(
                  controller: _search,
                  autofocus: true,
                  onChanged: _filter,
                  textInputAction: TextInputAction.search,
                  decoration: InputDecoration(
                    hintText: 'Rechercher (Cotonou, Parakou…)',
                    prefixIcon: const Icon(Icons.search),
                    isDense: true,
                    // Effacer d'un geste : sur 77 entrées, on affine puis on
                    // revient en arrière plus souvent qu'on ne le croit.
                    suffixIcon: _search.text.isEmpty
                        ? null
                        : IconButton(
                            icon: const Icon(Icons.clear),
                            onPressed: () {
                              _search.clear();
                              _filter('');
                            },
                          ),
                  ),
                ),
                const SizedBox(height: 8),
                Text(
                  _shown.length == widget.all.length
                      ? '${widget.all.length} communes'
                      : '${_shown.length} sur ${widget.all.length}',
                  style: TextStyle(fontSize: 12, color: theme.hintColor),
                ),
              ],
            ),
          ),
          Expanded(
            child: _shown.isEmpty
                ? Center(
                    child: Padding(
                      padding: const EdgeInsets.all(24),
                      child: Column(
                        mainAxisSize: MainAxisSize.min,
                        children: [
                          Icon(Icons.search_off, size: 40, color: theme.hintColor),
                          const SizedBox(height: 12),
                          const Text(
                            'Aucune commune ne correspond.\nLa livraison n\'est possible qu\'au Bénin.',
                            textAlign: TextAlign.center,
                          ),
                        ],
                      ),
                    ),
                  )
                : ListView.separated(
                    controller: controller,
                    // Le rembourrage bas ÉCARTE la dernière ligne de la barre
                    // système, au lieu de la laisser dessous.
                    padding: EdgeInsets.only(bottom: bottomInset + 12),
                    itemCount: _shown.length,
                    separatorBuilder: (_, __) =>
                        Divider(height: 1, indent: 20, endIndent: 20, color: theme.dividerColor),
                    itemBuilder: (_, i) {
                      final c = _shown[i];
                      final isSelected = c.code == widget.selectedCode;
                      return ListTile(
                        // La sélection courante se voit d'un coup d'œil, sans
                        // avoir à comparer le libellé au champ resté derrière.
                        tileColor: isSelected ? AppTheme.softGreen : null,
                        title: Text(c.name,
                            style: TextStyle(
                              fontWeight: isSelected ? FontWeight.w800 : FontWeight.w600,
                            )),
                        subtitle: Text(c.departmentName, style: const TextStyle(fontSize: 12)),
                        trailing: isSelected
                            ? const Icon(Icons.check_circle, color: AppTheme.brandGreen)
                            : null,
                        onTap: () => Navigator.pop(context, c.code),
                      );
                    },
                  ),
          ),
        ],
      ),
    );
  }
}
