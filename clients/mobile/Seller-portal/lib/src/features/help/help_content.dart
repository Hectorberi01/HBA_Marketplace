import 'package:flutter/material.dart';

import '../legal/legal_content.dart';

/// Contenu du centre d'aide (FAQ). Statique dans un premier temps : ces réponses
/// décrivent le comportement RÉEL de l'app (déclinaisons, mises en vente, SKU auto,
/// lieux d'expédition, retraits…). Toute évolution du produit doit être répercutée
/// ici, sinon l'aide ment.
class FaqItem {
  const FaqItem(this.question, this.answer);
  final String question;
  final String answer;
}

class FaqCategory {
  const FaqCategory(this.title, this.icon, this.items);
  final String title;
  final IconData icon;
  final List<FaqItem> items;
}

class HelpContent {
  const HelpContent._();

  /// Adresse unique du support (partagée avec les textes légaux).
  static const supportEmail = Legal.supportEmail;

  /// FAQ dans la langue active. Le contenu d'aide n'est pas contractuel : il
  /// décrit le fonctionnement de l'app, donc traduire ne pose pas de problème
  /// juridique (contrairement aux textes légaux).
  static List<FaqCategory> faqFor(bool english) => english ? _faqEn : faq;

  static const List<FaqCategory> faq = [
    FaqCategory('Démarrer', Icons.rocket_launch_outlined, [
      FaqItem(
        'Par où commencer pour vendre ?',
        "Créez d'abord un produit (photo, catégorie, description). Ajoutez-lui au moins une "
            "déclinaison (l'article concret qui porte le stock), enregistrez du stock, puis "
            "mettez-le en vente en fixant votre prix. Un lieu d'expédition est aussi requis.",
      ),
      FaqItem(
        'Pourquoi mon compte est « en attente » ?',
        "Un nouveau vendeur doit être validé : vos pièces KYB sont vérifiées et vos coordonnées "
            "de reversement enregistrées avant l'activation. Vous êtes prévenu par notification "
            "et e-mail dès que la boutique est validée.",
      ),
    ]),
    FaqCategory('Produits & déclinaisons', Icons.inventory_2_outlined, [
      FaqItem(
        "Quelle différence entre un produit et une déclinaison ?",
        "Le produit est la fiche générale (ex. « Lampe UV »). La déclinaison est la version "
            "précise que vous vendez et stockez (taille, couleur, état). C'est elle qui porte le "
            "SKU et le stock.",
      ),
      FaqItem(
        'Comment vendre le même produit en neuf ET en occasion ?',
        "Créez deux déclinaisons : une « Neuf » et une « Occasion ». Chacune a son propre stock "
            "et sa propre mise en vente. Une même déclinaison ne peut pas être à la fois neuve et "
            "d'occasion, car son stock est unique.",
      ),
      FaqItem(
        "Dois-je saisir le SKU moi-même ?",
        "Non : à la création d'une déclinaison, un SKU est généré automatiquement (préfixe de "
            "votre identifiant vendeur + code aléatoire). Vous pouvez le remplacer par le vôtre si "
            "vous en avez un.",
      ),
    ]),
    FaqCategory('Mise en vente & prix', Icons.sell_outlined, [
      FaqItem(
        "Pourquoi mon produit n'est pas achetable ?",
        "Un produit n'est achetable que s'il a une mise en vente ACTIVE. Vérifiez qu'une "
            "déclinaison existe, qu'elle a du stock, et que vous l'avez mise en vente avec un prix.",
      ),
      FaqItem(
        'Comment est calculé le prix affiché au client ?',
        "Vous saisissez votre prix NET (ce que vous percevez). La plateforme ajoute sa commission "
            "et les frais de paiement pour obtenir le prix affiché à l'acheteur. Le détail s'affiche "
            "en direct pendant la saisie.",
      ),
    ]),
    FaqCategory('Stock & expédition', Icons.local_shipping_outlined, [
      FaqItem(
        "À quoi sert un lieu d'expédition ?",
        "C'est l'adresse d'où partent vos colis. Il est obligatoire pour créer une mise en vente. "
            "Gérez vos lieux depuis « Mon compte › Lieux d'expédition ».",
      ),
      FaqItem(
        'Comment marquer une commande expédiée ?',
        "Depuis « Expéditions », ouvrez le colis à traiter et renseignez le transporteur et le "
            "numéro de suivi. Le suivi est obligatoire : une expédition sans suivi n'est pas acceptée.",
      ),
    ]),
    FaqCategory('Commandes, retours & litiges', Icons.assignment_return_outlined, [
      FaqItem(
        "Que faire d'une demande de retour ?",
        "Dans « Retours », examinez la demande et validez ou refusez le remboursement. Un "
            "remboursement validé est déduit de votre solde. Un retour non traité peut se "
            "transformer en litige.",
      ),
    ]),
    FaqCategory('Paiements & retraits', Icons.account_balance_wallet_outlined, [
      FaqItem(
        'Comment retirer mon argent ?',
        "Depuis « Portefeuille », touchez « Demander le retrait » et indiquez le montant (dans la "
            "limite du solde disponible). Le versement se fait vers vos coordonnées de reversement, "
            "après validation.",
      ),
      FaqItem(
        'Pourquoi une partie de mon argent est « en attente » ?',
        "Les gains d'une commande restent en attente tant que la livraison n'est pas confirmée "
            "(séquestre). Ils deviennent retirables une fois la commande finalisée.",
      ),
    ]),
    FaqCategory('Compte & sécurité', Icons.lock_outline, [
      FaqItem(
        'Comment sécuriser mon compte ?',
        "Activez la double authentification (MFA) et le déverrouillage biométrique depuis "
            "« Profil et sécurité ». Ne partagez jamais vos codes.",
      ),
      FaqItem(
        'Que se passe-t-il si je ferme mon compte ?',
        "La fermeture retire immédiatement vos produits de la vente, mais conserve votre compte et "
            "son historique. Vous gardez un accès restreint et pouvez demander la réactivation à "
            "tout moment ; la suppression définitive est décidée par l'équipe.",
      ),
    ]),
  ];

  static const List<FaqCategory> _faqEn = [
    FaqCategory('Getting started', Icons.rocket_launch_outlined, [
      FaqItem(
        'Where do I start to sell?',
        "First create a product (photo, category, description). Add at least one variant (the "
            "concrete item that carries the stock), record some stock, then list it by setting your "
            "price. A ship-from location is also required.",
      ),
      FaqItem(
        'Why is my account “pending”?',
        "A new seller must be approved: your KYB documents are verified and your payout details "
            "registered before activation. You'll be notified by push and email as soon as your shop "
            "is approved.",
      ),
    ]),
    FaqCategory('Products & variants', Icons.inventory_2_outlined, [
      FaqItem(
        "What's the difference between a product and a variant?",
        "The product is the general listing page (e.g. “UV lamp”). The variant is the specific "
            "version you sell and stock (size, color, condition). It carries the SKU and the stock.",
      ),
      FaqItem(
        'How do I sell the same product both new AND used?',
        "Create two variants: one “New” and one “Used”. Each has its own stock and its own listing. "
            "A single variant can't be both new and used, because its stock is unique.",
      ),
      FaqItem(
        "Do I have to enter the SKU myself?",
        "No: when you create a variant, a SKU is generated automatically (your seller-id prefix + a "
            "random code). You can replace it with your own if you have one.",
      ),
    ]),
    FaqCategory('Listing & pricing', Icons.sell_outlined, [
      FaqItem(
        "Why isn't my product buyable?",
        "A product is buyable only if it has an ACTIVE listing. Check that a variant exists, that it "
            "has stock, and that you've listed it with a price.",
      ),
      FaqItem(
        'How is the price shown to the customer calculated?',
        "You enter your NET price (what you receive). The platform adds its commission and the "
            "payment fees to get the price shown to the buyer. The breakdown updates live as you type.",
      ),
    ]),
    FaqCategory('Stock & shipping', Icons.local_shipping_outlined, [
      FaqItem(
        "What is a ship-from location for?",
        "It's the address your parcels ship from. It's required to create a listing. Manage your "
            "locations from “My account › Ship-from locations”.",
      ),
      FaqItem(
        'How do I mark an order as shipped?',
        "From “Shipments”, open the parcel to process and enter the carrier and tracking number. "
            "Tracking is mandatory: a shipment without tracking isn't accepted.",
      ),
    ]),
    FaqCategory('Orders, returns & disputes', Icons.assignment_return_outlined, [
      FaqItem(
        "What do I do with a return request?",
        "In “Returns”, review the request and approve or reject the refund. An approved refund is "
            "deducted from your balance. An unhandled return can turn into a dispute.",
      ),
    ]),
    FaqCategory('Payments & withdrawals', Icons.account_balance_wallet_outlined, [
      FaqItem(
        'How do I withdraw my money?',
        "From “Wallet”, tap “Request withdrawal” and enter the amount (up to your available "
            "balance). The payout goes to your registered payout details, after validation.",
      ),
      FaqItem(
        'Why is part of my money “pending”?',
        "An order's earnings stay pending until delivery is confirmed (escrow). They become "
            "withdrawable once the order is finalized.",
      ),
    ]),
    FaqCategory('Account & security', Icons.lock_outline, [
      FaqItem(
        'How do I secure my account?',
        "Enable two-factor authentication (2FA) and biometric unlock from “Profile & security”. "
            "Never share your codes.",
      ),
      FaqItem(
        'What happens if I close my account?',
        "Closing immediately removes your products from sale, but keeps your account and its "
            "history. You keep restricted access and can request reactivation at any time; permanent "
            "deletion is decided by the team.",
      ),
    ]),
  ];
}
