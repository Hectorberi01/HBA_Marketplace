import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/config/app_config.dart';
import '../../core/providers/core_providers.dart';
import '../utils/formatters.dart';

/// Une des 77 communes du Bénin.
class Commune {
  const Commune({required this.code, required this.name, required this.departmentName});

  /// Identifiant STABLE (« abomey-calavi »). C'est lui qu'on envoie au serveur.
  final String code;

  /// Libellé accentué, pour l'affichage seulement.
  final String name;

  final String departmentName;

  factory Commune.fromJson(Map d) => Commune(
        code: Json.str(d['code']),
        name: Json.str(d['name']),
        departmentName: Json.str(d['departmentName']),
      );

  String get searchKey => fold('$name $code $departmentName');

  /// Replie une chaîne pour la recherche : minuscules, sans accent, ponctuation
  /// aplatie. Permet de trouver « Sèmè-Podji » en tapant « seme podji », ce que
  /// fait n'importe qui sur un clavier de téléphone.
  static String fold(String v) {
    const from = 'àáâãäåçèéêëìíîïñòóôõöùúûüýÿ';
    const to = 'aaaaaaceeeeiiiinooooouuuuyy';
    final buffer = StringBuffer();
    for (final ch in v.toLowerCase().split('')) {
      final i = from.indexOf(ch);
      buffer.write(i >= 0 ? to[i] : ch);
    }
    return buffer.toString().replaceAll(RegExp(r'[^a-z0-9]+'), ' ').trim();
  }
}

/// ═════════════════════════════════════════════════════════════════════════════
/// LES 77 COMMUNES — `GET /api/geo/benin`, ANONYME.
///
/// LA LISTE N'EST PAS EMBARQUÉE DANS L'APPLICATION, ET NE DOIT PAS L'ÊTRE.
///
/// La tentation est forte : 77 entrées, ça tient dans un fichier, et ça marche
/// hors ligne. Mais le serveur VALIDE le code envoyé
/// (`BeninGeography.ResolveCommuneCode`). Une liste recopiée diverge à la
/// première correction d'orthographe, et le vendeur se voit alors refuser une
/// commune que l'application vient de lui proposer — sans qu'aucun message ne
/// relie les deux faits.
///
/// La route rend la MÊME classe que celle qui valide. C'est cette identité, et
/// elle seule, qui garantit qu'un choix proposé sera accepté.
///
/// CE N'EST PAS UNE TABLE EN BASE, ET C'EST DÉLIBÉRÉ. Le découpage
/// administratif n'a pas bougé depuis 1999 ; une table créerait une seconde
/// autorité face au domaine, et une TROISIÈME face aux migrations SQL qui
/// replient les accents avec la même table de caractères. Le raisonnement
/// complet est dans `UserEndpoints.MapUserEndpoints`.
///
/// `keepAlive` : le référentiel est une constante. Le relire à chaque
/// ouverture de formulaire serait un appel réseau pour rien — le service pose
/// d'ailleurs `Cache-Control: public, max-age=86400`.
///
/// LA DÉGRADATION RESTE CHOISIE, ET ELLE NE BLOQUE RIEN.
///
/// Si l'appel échoue — hors ligne, service indisponible — le sélecteur devient
/// inerte et le reste du formulaire s'envoie normalement. La commune est
/// FACULTATIVE partout où ce champ apparaît (informations société du KYB,
/// adresse d'entrepôt) : refuser l'enregistrement parce qu'on n'a pas su charger
/// une liste serait retenir un vendeur pour un champ dont personne n'a besoin
/// tout de suite.
/// ═════════════════════════════════════════════════════════════════════════════
final communesProvider = FutureProvider<List<Commune>>((ref) async {
  ref.keepAlive();

  final resp = await ref.watch(dioProvider).get('${AppConfig.geo}/benin');
  final data = Json.map(resp.data);

  // Le service rend DÉJÀ les communes triées par code désaccentué, et chacune
  // porte le nom de son département : rien à joindre ni à retrier ici. Un tri
  // côté application sur des libellés accentués donnerait d'ailleurs un ordre
  // différent de celui du serveur, selon la locale de l'appareil.
  return Json.list(data['communes']).map(Commune.fromJson).toList();
});

/// Champ de sélection de commune : liste fermée avec recherche.
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

    Commune? selected;
    for (final c in communes.valueOrNull ?? const <Commune>[]) {
      if (c.code == selectedCode) {
        selected = c;
        break;
      }
    }

    // Repli honnête : référentiel pas encore chargé mais commune déjà choisie →
    // on affiche son code plutôt qu'un vide qui ferait croire à une saisie perdue.
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
          // CE N'EST PAS UNE PANNE PASSAGÈRE, ET LE TEXTE NE DOIT PAS LE
          //    LAISSER CROIRE. Il n'y a aucune route de communes à joindre (voir
          //    `communesProvider`). « Réessayez plus tard » ferait revenir le
          //    vendeur dix fois sur un champ qui ne s'ouvrira pas.
          errorText: communes.hasError
              ? 'La liste des communes n\'est pas encore disponible. '
                  'Ce champ est facultatif : vous pouvez continuer sans lui.'
              : null,
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
    final q = Commune.fold(raw);
    setState(() {
      _shown = q.isEmpty ? widget.all : widget.all.where((c) => c.searchKey.contains(q)).toList();
    });
  }

  @override
  Widget build(BuildContext context) {
    return DraggableScrollableSheet(
      initialChildSize: 0.85,
      maxChildSize: 0.95,
      expand: false,
      builder: (_, controller) => Column(
        children: [
          Padding(
            padding: const EdgeInsets.fromLTRB(20, 16, 20, 8),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                const Text('Choisir la commune',
                    style: TextStyle(fontSize: 18, fontWeight: FontWeight.w800)),
                const SizedBox(height: 12),
                TextField(
                  controller: _search,
                  autofocus: true,
                  onChanged: _filter,
                  decoration: const InputDecoration(
                    hintText: 'Rechercher (Cotonou, Parakou…)',
                    prefixIcon: Icon(Icons.search),
                  ),
                ),
              ],
            ),
          ),
          Expanded(
            child: _shown.isEmpty
                ? const Center(
                    child: Padding(
                      padding: EdgeInsets.all(24),
                      child: Text('Aucune commune ne correspond.', textAlign: TextAlign.center),
                    ),
                  )
                : ListView.builder(
                    controller: controller,
                    itemCount: _shown.length,
                    itemBuilder: (_, i) {
                      final c = _shown[i];
                      final isSelected = c.code == widget.selectedCode;
                      return ListTile(
                        title: Text(c.name,
                            style: TextStyle(fontWeight: isSelected ? FontWeight.w800 : null)),
                        subtitle: Text(c.departmentName),
                        trailing: isSelected ? const Icon(Icons.check) : null,
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
