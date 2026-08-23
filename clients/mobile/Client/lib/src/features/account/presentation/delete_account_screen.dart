import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../core/theme/app_theme.dart';
import '../../auth/application/auth_controller.dart';
import '../account_data.dart';

/// Suppression définitive du compte.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// POURQUOI CET ÉCRAN EXISTE
///
/// Apple l'EXIGE (Guideline 5.1.1(v)) dès qu'une application permet de créer un compte :
/// elle doit permettre de le supprimer, in-app, sans passer par le support. Sans ce
/// parcours, l'application est rejetée.
///
/// Et au-delà du store : notre propre politique de confidentialité promettait déjà ce
/// droit. Il n'existait aucun écran pour l'exercer. Nous promettions sans tenir.
///
/// CET ÉCRAN NE DOIT PAS ÊTRE « FLUIDE ».
///
/// La tentation, en conception, est de rendre chaque parcours rapide. Ici, ce serait une
/// faute : l'action est IRRÉVERSIBLE. On demande donc une confirmation explicite ET le
/// mot de passe. Une friction délibérée, qui protège l'utilisateur de lui-même — et du
/// téléphone déverrouillé qu'il a laissé sur une table.
/// ─────────────────────────────────────────────────────────────────────────────
class DeleteAccountScreen extends ConsumerStatefulWidget {
  const DeleteAccountScreen({super.key});

  @override
  ConsumerState<DeleteAccountScreen> createState() => _DeleteAccountScreenState();
}

class _DeleteAccountScreenState extends ConsumerState<DeleteAccountScreen> {
  final _password = TextEditingController();

  bool _understood = false;
  bool _busy = false;
  String? _error;

  @override
  void dispose() {
    _password.dispose();
    super.dispose();
  }

  Future<void> _delete() async {
    if (_password.text.isEmpty) {
      setState(() => _error = 'Saisissez votre mot de passe pour confirmer.');
      return;
    }

    // Dernière barrière. Un utilisateur qui coche et tape son mot de passe peut encore
    // se tromper d'écran ; il ne se trompera pas ici.
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (dialogContext) => AlertDialog(
        title: const Text('Supprimer définitivement ?'),
        content: const Text(
          'Votre compte et vos données personnelles seront effacés. '
          'Cette action est IRRÉVERSIBLE : vous ne pourrez pas récupérer votre historique.',
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(dialogContext, false),
            child: const Text('Annuler'),
          ),
          FilledButton(
            style: FilledButton.styleFrom(backgroundColor: AppTheme.danger),
            onPressed: () => Navigator.pop(dialogContext, true),
            child: const Text('Supprimer'),
          ),
        ],
      ),
    );

    if (confirmed != true || !mounted) return;

    setState(() {
      _busy = true;
      _error = null;
    });

    try {
      await ref.read(accountApiProvider).deleteAccount(_password.text);

      // Le compte n'existe plus : la session doit tomber. `logout()` retire au passage
      // le jeton push de cet appareil — sans quoi il continuerait de recevoir les
      // notifications d'un compte supprimé.
      await ref.read(authControllerProvider.notifier).logout();

      // Pas de navigation manuelle : la déconnexion fait basculer le routeur vers
      // l'écran de connexion. Naviguer en plus produirait un empilement d'écrans.
    } catch (e) {
      if (mounted) {
        setState(() {
          // Le serveur explique PRÉCISÉMENT ce qui bloque (« vous avez 2 commandes en
          // cours… »). On affiche son message tel quel : le reformuler en « une erreur
          // est survenue » perdrait la seule information utile.
          _error = e.toString().replaceFirst('ApiException: ', '');
          _busy = false;
        });
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('Supprimer mon compte')),
      body: ListView(
        padding: const EdgeInsets.all(20),
        children: [
          const _WarningCard(),
          const SizedBox(height: 24),

          const Text(
            'Ce qui sera effacé',
            style: TextStyle(fontSize: 15, fontWeight: FontWeight.w800),
          ),
          const SizedBox(height: 8),
          const _Bullet('Votre nom, votre e-mail et votre numéro de téléphone.'),
          const _Bullet('Vos adresses et vos moyens de paiement enregistrés.'),
          const _Bullet('Vos favoris et vos notifications.'),

          const SizedBox(height: 16),
          const Text(
            'Ce qui sera conservé',
            style: TextStyle(fontSize: 15, fontWeight: FontWeight.w800),
          ),
          const SizedBox(height: 8),

          // ON LE DIT. C'est une obligation légale, et le taire serait à la fois
          // malhonnête et contraire au RGPD : l'utilisateur a le droit de savoir ce qui
          // survit à sa demande, et pourquoi.
          const _Bullet(
            'Vos commandes passées, sans vos données personnelles. '
            'La loi nous impose de conserver ces documents comptables plusieurs années — '
            'ils ne vous désigneront plus.',
          ),

          const SizedBox(height: 24),
          CheckboxListTile(
            value: _understood,
            onChanged: _busy ? null : (v) => setState(() => _understood = v ?? false),
            contentPadding: EdgeInsets.zero,
            controlAffinity: ListTileControlAffinity.leading,
            title: const Text(
              "J'ai compris que cette action est définitive et que mon compte ne pourra pas être restauré.",
              style: TextStyle(fontSize: 13, height: 1.4),
            ),
          ),

          const SizedBox(height: 12),
          TextField(
            controller: _password,
            obscureText: true,
            enabled: _understood && !_busy,
            decoration: const InputDecoration(
              labelText: 'Votre mot de passe',
              helperText: 'Nous le demandons pour vérifier que c\'est bien vous.',
            ),
          ),

          if (_error != null) ...[
            const SizedBox(height: 16),
            Container(
              padding: const EdgeInsets.all(12),
              decoration: BoxDecoration(
                color: const Color(0xFFFDECEC),
                borderRadius: BorderRadius.circular(10),
              ),
              child: Text(
                _error!,
                style: const TextStyle(fontSize: 13, color: AppTheme.danger, height: 1.4),
              ),
            ),
          ],

          const SizedBox(height: 24),
          FilledButton(
            style: FilledButton.styleFrom(
              backgroundColor: AppTheme.danger,
              minimumSize: const Size.fromHeight(52),
            ),
            onPressed: (_understood && !_busy) ? _delete : null,
            child: _busy
                ? const SizedBox(
                    height: 20,
                    width: 20,
                    child: CircularProgressIndicator(strokeWidth: 2, color: Colors.white),
                  )
                : const Text('Supprimer mon compte'),
          ),

          const SizedBox(height: 12),
          Center(
            child: TextButton(
              onPressed: _busy ? null : () => Navigator.of(context).pop(),
              child: const Text('Annuler'),
            ),
          ),
        ],
      ),
    );
  }
}

class _WarningCard extends StatelessWidget {
  const _WarningCard();

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.all(16),
      decoration: BoxDecoration(
        color: const Color(0xFFFDECEC),
        borderRadius: BorderRadius.circular(12),
      ),
      child: const Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Icon(Icons.warning_amber_rounded, color: AppTheme.danger),
          SizedBox(width: 12),
          Expanded(
            child: Text(
              'La suppression de votre compte est définitive. '
              'Vous perdrez l\'accès à votre historique de commandes.',
              style: TextStyle(fontSize: 13, color: AppTheme.danger, height: 1.45),
            ),
          ),
        ],
      ),
    );
  }
}

class _Bullet extends StatelessWidget {
  const _Bullet(this.text);
  final String text;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.only(bottom: 6),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text('•  ', style: TextStyle(color: AppTheme.subtle)),
          Expanded(
            child: Text(
              text,
              style: TextStyle(fontSize: 13, color: AppTheme.subtle, height: 1.45),
            ),
          ),
        ],
      ),
    );
  }
}
