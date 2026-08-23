import 'package:flutter/material.dart';

import '../../../core/network/not_migrated.dart';

/// ═════════════════════════════════════════════════════════════════════════════
/// TABLEAU DE BORD CONSOLIDÉ — SANS AMONT.
///
/// SEULE LA VUE CONSOLIDÉE BASCULE. LES TABLEAUX DE BORD PAR ACTIVITÉ RESTENT.
///
/// `PartnerHomeScreen` aiguille `/home` sur trois branches : `null` mène ici,
/// une boutique mène à `ExpressDashboardScreen`, un restaurant à
/// `FoodDashboardScreen`. Ces deux-là ont un amont — `bffMerchant` et
/// `bffRestaurant` rendent le tableau de bord D'UNE activité — et ne sont pas
/// touchés. Seule la branche `null` est concernée.
///
/// PERSONNE N'ADDITIONNE LES ACTIVITÉS, CÔTÉ SERVEUR.
///
/// `GET /api/v1/bff/merchant/activities` rend la LISTE des boutiques et
/// restaurants du compte — pas leurs totaux. Il n'existe aucune agrégation
/// multi-activités : ni chiffre d'affaires cumulé, ni nombre de commandes toutes
/// activités confondues, ni compteur « à traiter ». Les deux façades sont même
/// gardées par des rôles distincts (`MerchantOnly` / `RestaurantOnly`), ce qui
/// interdit de les sommer naïvement côté client.
///
/// C'EST LA CARTE VERTE QUI RENDAIT CET ÉCRAN DANGEREUX.
///
/// Un bloc plein cadre annonçait « CHIFFRE D'AFFAIRES TOTAL » suivi d'un montant
/// et de trois compteurs. C'est le premier chiffre que lit un partenaire en
/// ouvrant l'application, et le seul élément pleinement coloré de toute
/// l'interface — donc celui qu'on croit sans réserve. Le laisser sur des données
/// de maquette pendant que les écrans voisins passent au vrai serveur, c'est
/// garantir qu'on n'en distinguera plus l'origine.
///
/// L'ISSUE RESTE OUVERTE : l'onglet « Activités » liste les commerces et permet
/// d'en choisir un, ce qui ramène `/home` sur un tableau de bord réel. La phrase
/// affichée l'indique — sans quoi cet onglet paraîtrait être un cul-de-sac.
///
/// POUR REBRANCHER : ajouter au BFF merchant une agrégation qui parcourt les
/// activités du compte et rende un total par univers, puis restaurer la mise en
/// page que git conserve (carte verte, sections par univers, cartes d'activité).
/// ═════════════════════════════════════════════════════════════════════════════
class GlobalDashboardScreen extends StatelessWidget {
  const GlobalDashboardScreen({super.key});

  @override
  Widget build(BuildContext context) => const NotMigratedScreen(
        inShell: true,
        title: 'Toutes mes activités',
        message:
            'Le récapitulatif de toutes vos activités arrive bientôt : il '
            'réunira le chiffre d\'affaires et les commandes de vos boutiques et '
            'de vos restaurants.',
        detail:
            'Ouvrez une activité depuis l\'onglet « Activités » pour retrouver '
            'son tableau de bord.',
      );
}
