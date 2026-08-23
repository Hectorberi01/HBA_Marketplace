import 'package:flutter/material.dart';
import 'package:hba_express_pro/l10n/app_localizations.dart';

import '../../../core/theme/app_theme.dart';
import '../catalog_data.dart';

/// Reconstruit le chemin LISIBLE d'une catégorie (« Beauté et soins › Ongles ›
/// Accessoires de manucure ») à partir de son `path` en slugs et des noms des
/// ancêtres. Repli sur le slug si un ancêtre manque.
/// Segments NON vides d'un chemin (robuste au slash de tête « /a/b »).
List<String> _segments(String path) => path.split('/').where((s) => s.isNotEmpty).toList();

String categoryReadablePath(Category c, List<Category> all) {
  final byPath = {for (final x in all) x.path: x.name};
  final raw = c.path.split('/');
  final names = <String>[];
  var acc = '';
  for (var i = 0; i < raw.length; i++) {
    acc = i == 0 ? raw[i] : '$acc/${raw[i]}';
    if (raw[i].isEmpty) continue; // ignore le vide du slash de tête
    names.add(byPath[acc] ?? raw[i]);
  }
  return names.isEmpty ? c.name : names.join(' › ');
}

List<Category> _childrenOf(String? parentPath, List<Category> all) {
  final result = all.where((c) {
    final depth = _segments(c.path).length;
    if (parentPath == null) return depth == 1; // racines
    final parentDepth = _segments(parentPath).length;
    return depth == parentDepth + 1 && c.path.startsWith('$parentPath/');
  }).toList()
    ..sort((a, b) => a.name.toLowerCase().compareTo(b.name.toLowerCase()));
  return result;
}

bool _hasChildren(Category c, List<Category> all) {
  final depth = _segments(c.path).length;
  return all.any((x) => x.path.startsWith('${c.path}/') && _segments(x.path).length == depth + 1);
}

/// Ouvre le sélecteur en cascade et renvoie la catégorie choisie (ou null).
Future<Category?> showCategoryPicker(BuildContext context, List<Category> all) {
  return showModalBottomSheet<Category>(
    context: context,
    isScrollControlled: true,
    shape: const RoundedRectangleBorder(borderRadius: BorderRadius.vertical(top: Radius.circular(20))),
    builder: (_) => _CategoryPickerSheet(all: all),
  );
}

class _CategoryPickerSheet extends StatefulWidget {
  const _CategoryPickerSheet({required this.all});
  final List<Category> all;

  @override
  State<_CategoryPickerSheet> createState() => _CategoryPickerSheetState();
}

class _CategoryPickerSheetState extends State<_CategoryPickerSheet> {
  /// Path du niveau courant (null = racines). On y descend via le chevron.
  String? _parentPath;

  Category? get _parent =>
      _parentPath == null ? null : widget.all.firstWhere((c) => c.path == _parentPath, orElse: () => widget.all.first);

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);
    final l = AppLocalizations.of(context);
    final items = _childrenOf(_parentPath, widget.all);
    final parent = _parent;

    return SafeArea(
      top: false,
      child: SizedBox(
        height: MediaQuery.of(context).size.height * 0.72,
        child: Column(
          children: [
            const SizedBox(height: 10),
            Center(
              child: Container(
                width: 40,
                height: 4,
                decoration: BoxDecoration(color: colors.line, borderRadius: BorderRadius.circular(2)),
              ),
            ),
            // En-tête : retour + emplacement courant.
            Padding(
              padding: const EdgeInsets.fromLTRB(8, 8, 12, 8),
              child: Row(
                children: [
                  if (_parentPath != null)
                    IconButton(
                      icon: const Icon(Icons.arrow_back),
                      onPressed: () {
                        // Remonte d'un niveau (robuste au slash de tête).
                        final idx = _parentPath!.lastIndexOf('/');
                        setState(() => _parentPath = idx <= 0 ? null : _parentPath!.substring(0, idx));
                      },
                    )
                  else
                    const SizedBox(width: 12),
                  Expanded(
                    child: Text(
                      parent == null ? l.cpChooseCategory : categoryReadablePath(parent, widget.all),
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                      style: TextStyle(fontWeight: FontWeight.w800, fontSize: 16, color: colors.ink),
                    ),
                  ),
                  IconButton(icon: const Icon(Icons.close), onPressed: () => Navigator.pop(context)),
                ],
              ),
            ),
            // Sélectionner le niveau courant lui-même (produit rangé dans un parent).
            if (parent != null)
              ListTile(
                leading: const Icon(Icons.check_circle_outline, color: AppTheme.brandGreen),
                title: Text(l.cpChooseThis(parent.name),
                    style: const TextStyle(fontWeight: FontWeight.w700, color: AppTheme.brandGreen)),
                onTap: () => Navigator.pop(context, parent),
              ),
            Divider(height: 1, color: colors.line),
            Expanded(
              child: ListView.separated(
                itemCount: items.length,
                separatorBuilder: (_, __) => Divider(height: 1, color: colors.line),
                itemBuilder: (_, i) {
                  final c = items[i];
                  final hasKids = _hasChildren(c, widget.all);
                  return ListTile(
                    // Corps de la ligne = SÉLECTIONNER cette catégorie.
                    title: Text(c.name, style: TextStyle(fontSize: 15, color: colors.ink)),
                    onTap: () => Navigator.pop(context, c),
                    // Chevron = DESCENDRE dans les sous-catégories.
                    trailing: hasKids
                        ? IconButton(
                            icon: Icon(Icons.chevron_right, color: colors.subtle),
                            tooltip: l.cpSubcategories,
                            onPressed: () => setState(() => _parentPath = c.path),
                          )
                        : null,
                  );
                },
              ),
            ),
          ],
        ),
      ),
    );
  }
}

/// Champ « Catégorie » façon input : affiche le nom lisible sélectionné et ouvre
/// le sélecteur en cascade au tap.
class CategoryField extends StatelessWidget {
  const CategoryField({
    super.key,
    required this.categories,
    required this.selectedId,
    required this.onSelected,
  });

  final List<Category> categories;
  final String? selectedId;
  final ValueChanged<Category> onSelected;

  @override
  Widget build(BuildContext context) {
    final colors = AppColors.of(context);
    final l = AppLocalizations.of(context);
    final selected = selectedId == null
        ? null
        : categories.where((c) => c.id == selectedId).cast<Category?>().firstWhere((_) => true, orElse: () => null);

    return InkWell(
      borderRadius: BorderRadius.circular(12),
      onTap: () async {
        final picked = await showCategoryPicker(context, categories);
        if (picked != null) onSelected(picked);
      },
      child: InputDecorator(
        decoration: InputDecoration(labelText: l.cpCategory),
        child: Row(
          children: [
            Expanded(
              child: Text(
                selected == null ? l.cpChooseCategory : categoryReadablePath(selected, categories),
                maxLines: 1,
                overflow: TextOverflow.ellipsis,
                style: TextStyle(
                  fontSize: 15,
                  fontWeight: FontWeight.w600,
                  color: selected == null ? colors.subtle : colors.ink,
                ),
              ),
            ),
            Icon(Icons.expand_more_rounded, color: colors.subtle),
          ],
        ),
      ),
    );
  }
}
