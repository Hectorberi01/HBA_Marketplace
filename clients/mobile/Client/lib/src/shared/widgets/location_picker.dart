import 'package:flutter/material.dart';
import 'package:flutter_map/flutter_map.dart';
import 'package:geolocator/geolocator.dart';
import 'package:latlong2/latlong.dart';
import 'package:url_launcher/url_launcher.dart';

/// Un point géographique, ou son absence.
class GeoPoint {
  const GeoPoint(this.latitude, this.longitude);

  final double latitude;
  final double longitude;

  /// Cotonou. Sert de vue initiale quand aucun point n'existe encore — pas de
  /// valeur par défaut enregistrée : afficher une carte centrée quelque part
  /// n'est pas la même chose que prétendre connaître la position de quelqu'un.
  static const cotonou = GeoPoint(6.3703, 2.3912);

  LatLng get latLng => LatLng(latitude, longitude);

  @override
  String toString() => '${latitude.toStringAsFixed(6)}, ${longitude.toStringAsFixed(6)}';
}

/// ─────────────────────────────────────────────────────────────────────────────
/// OUVRIR UN POINT DANS LA CARTO DU TÉLÉPHONE.
///
/// LE LIVREUR N'A PAS NOTRE APPLICATION. Il n'existe pas d'application coursier,
/// et il n'y en aura pas de sitôt. Afficher une belle carte chez nous ne l'aide
/// donc en rien : ce qu'il lui faut, c'est un point qui s'ouvre dans SON outil —
/// Google Maps, Waze, ou ce qu'il a installé.
///
/// Le schéma `geo:` est celui d'Android et laisse l'utilisateur choisir son
/// application. iOS ne le connaît pas : on y bascule sur une URL Google Maps, qui
/// s'ouvre dans l'application native si elle est installée, dans le navigateur
/// sinon. Aucune clé d'API n'est nécessaire pour un simple lien.
/// ─────────────────────────────────────────────────────────────────────────────
Future<bool> openInMaps(GeoPoint point, {String? label}) async {
  final query = '${point.latitude},${point.longitude}';
  final name = label == null ? '' : '(${Uri.encodeComponent(label)})';

  for (final uri in [
    Uri.parse('geo:$query?q=$query$name'),
    Uri.parse('https://www.google.com/maps/search/?api=1&query=$query'),
  ]) {
    if (await canLaunchUrl(uri)) {
      return launchUrl(uri, mode: LaunchMode.externalApplication);
    }
  }
  return false;
}

/// ─────────────────────────────────────────────────────────────────────────────
/// CHAMP DE POSITION — FACULTATIF, ET IL DOIT LE RESTER.
///
/// Deux gestes, dans cet ordre d'usage :
///   • « Utiliser ma position » — le cas courant. Aucune carte, aucune tuile
///     téléchargée : un appel GPS et c'est fini.
///   • « Ajuster sur la carte » — le cas rare mais réel : on commande depuis le
///     bureau pour une livraison à la maison, ou le GPS est imprécis.
///
/// C'est cette séparation qui rend la solution gratuite tenable. Les tuiles
/// OpenStreetMap ne sont chargées que par le second geste, qu'une minorité
/// d'utilisateurs déclenchera.
///
/// LE REFUS DE PERMISSION N'EST PAS UNE ERREUR. Un acheteur qui refuse, dont le
/// GPS est coupé, ou qui est dans un bâtiment sans signal doit pouvoir enregistrer
/// son adresse exactement comme les autres. Le message le dit, et rien ne bloque.
/// ─────────────────────────────────────────────────────────────────────────────
class LocationPicker extends StatefulWidget {
  const LocationPicker({
    super.key,
    required this.value,
    required this.onChanged,
    this.label = 'Position (facultatif)',
  });

  final GeoPoint? value;
  final ValueChanged<GeoPoint?> onChanged;
  final String label;

  @override
  State<LocationPicker> createState() => _LocationPickerState();
}

class _LocationPickerState extends State<LocationPicker> {
  bool _locating = false;
  String? _notice;

  Future<void> _useCurrentPosition() async {
    setState(() {
      _locating = true;
      _notice = null;
    });

    try {
      if (!await Geolocator.isLocationServiceEnabled()) {
        _say('Activez la localisation du téléphone, puis réessayez.');
        return;
      }

      var permission = await Geolocator.checkPermission();
      if (permission == LocationPermission.denied) {
        permission = await Geolocator.requestPermission();
      }

      if (permission == LocationPermission.deniedForever) {
        // On ne renvoie PAS vers les réglages de force : le champ est facultatif,
        // insister serait déplacé.
        _say('Permission refusée. Vous pouvez continuer sans position.');
        return;
      }
      if (permission == LocationPermission.denied) {
        _say('Permission refusée. Vous pouvez continuer sans position.');
        return;
      }

      final p = await Geolocator.getCurrentPosition(
        locationSettings: const LocationSettings(
          accuracy: LocationAccuracy.high,
          // Sur un réseau mobile béninois, un premier point GPS peut prendre du
          // temps. Au-delà, on rend la main plutôt que de laisser tourner.
          timeLimit: Duration(seconds: 20),
        ),
      );
      widget.onChanged(GeoPoint(p.latitude, p.longitude));
      _say(null);
    } catch (_) {
      _say('Position introuvable pour le moment. Réessayez ou continuez sans.');
    } finally {
      if (mounted) setState(() => _locating = false);
    }
  }

  void _say(String? message) {
    if (mounted) setState(() => _notice = message);
  }

  Future<void> _adjustOnMap() async {
    final picked = await Navigator.of(context).push<GeoPoint>(
      MaterialPageRoute(
        fullscreenDialog: true,
        builder: (_) => _MapAdjustScreen(initial: widget.value ?? GeoPoint.cotonou),
      ),
    );
    if (picked != null) widget.onChanged(picked);
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final value = widget.value;

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Text(widget.label, style: theme.textTheme.bodySmall),
        const SizedBox(height: 6),

        if (value != null)
          Row(children: [
            Icon(Icons.place, size: 18, color: theme.colorScheme.primary),
            const SizedBox(width: 6),
            Expanded(child: Text(value.toString(), style: theme.textTheme.bodyMedium)),
            IconButton(
              onPressed: () => widget.onChanged(null),
              icon: const Icon(Icons.close, size: 18),
              tooltip: 'Retirer la position',
              visualDensity: VisualDensity.compact,
            ),
          ])
        else
          Text(
            'Aucune position. Le point de repère suffit pour être livré ; '
            'la position aide simplement le coursier à vous trouver plus vite.',
            style: theme.textTheme.bodySmall,
          ),

        const SizedBox(height: 8),
        Row(children: [
          Expanded(
            child: OutlinedButton.icon(
              onPressed: _locating ? null : _useCurrentPosition,
              icon: _locating
                  ? const SizedBox(height: 16, width: 16, child: CircularProgressIndicator(strokeWidth: 2))
                  : const Icon(Icons.my_location, size: 18),
              label: const Text('Ma position'),
            ),
          ),
          const SizedBox(width: 8),
          Expanded(
            child: OutlinedButton.icon(
              onPressed: _adjustOnMap,
              icon: const Icon(Icons.map_outlined, size: 18),
              label: const Text('Ajuster'),
            ),
          ),
        ]),

        if (_notice != null) ...[
          const SizedBox(height: 6),
          Text(_notice!, style: theme.textTheme.bodySmall?.copyWith(color: theme.colorScheme.error)),
        ],
      ],
    );
  }
}

/// Écran d'ajustement : la carte reste FIXE et le repère est au centre.
///
/// Déplacer une épingle au doigt cache le point sous le doigt lui-même, et rate
/// une fois sur deux. Déplacer la carte sous un repère fixe est le geste qu'ont
/// adopté toutes les applications de livraison, pour cette raison précise.
class _MapAdjustScreen extends StatefulWidget {
  const _MapAdjustScreen({required this.initial});

  final GeoPoint initial;

  @override
  State<_MapAdjustScreen> createState() => _MapAdjustScreenState();
}

class _MapAdjustScreenState extends State<_MapAdjustScreen> {
  late final MapController _controller = MapController();
  late GeoPoint _center = widget.initial;

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('Ajuster la position')),
      body: Stack(children: [
        FlutterMap(
          mapController: _controller,
          options: MapOptions(
            initialCenter: widget.initial.latLng,
            initialZoom: 16,
            onPositionChanged: (pos, _) =>
                setState(() => _center = GeoPoint(pos.center.latitude, pos.center.longitude)),
          ),
          children: [
            TileLayer(
              urlTemplate: 'https://tile.openstreetmap.org/{z}/{x}/{y}.png',
              // `userAgentPackageName` est EXIGÉ par la politique d'usage
              // d'OpenStreetMap : un client anonyme se fait bloquer.
              userAgentPackageName: 'com.hbaexpress.client',
              maxZoom: 19,
            ),
            const RichAttributionWidget(
              attributions: [TextSourceAttribution('OpenStreetMap contributors')],
            ),
          ],
        ),

        // Repère fixe, au centre exact de la carte.
        IgnorePointer(
          child: Center(
            child: Padding(
              // Décalé d'une demi-hauteur : la pointe de l'épingle doit tomber sur
              // le centre, pas son milieu.
              padding: const EdgeInsets.only(bottom: 40),
              child: Icon(Icons.place, size: 40, color: Theme.of(context).colorScheme.primary),
            ),
          ),
        ),
      ]),
      bottomNavigationBar: SafeArea(
        child: Padding(
          padding: const EdgeInsets.all(16),
          child: Column(mainAxisSize: MainAxisSize.min, children: [
            Text(_center.toString(), style: Theme.of(context).textTheme.bodySmall),
            const SizedBox(height: 8),
            FilledButton(
              onPressed: () => Navigator.pop(context, _center),
              child: const Text('Utiliser ce point'),
            ),
          ]),
        ),
      ),
    );
  }
}
