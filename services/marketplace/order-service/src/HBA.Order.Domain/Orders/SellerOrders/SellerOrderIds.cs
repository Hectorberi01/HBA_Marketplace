namespace HBA.Orders.Domain.Orders.SellerOrders;

/// <summary>Identité forte d'une commande vendeur. Même parti pris qu'<see cref="OrderId"/>.</summary>
public readonly record struct SellerOrderId(Guid Value)
{
    public static SellerOrderId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// CE QUE LE VENDEUR A À FAIRE DE SA PART DE COMMANDE.
///
/// CETTE ÉNUMÉRATION NE REMPLACE PAS <see cref="OrderStatus"/>, ELLE S'Y AJOUTE.
///
/// La commande garde son cycle GLOBAL — Pending → AwaitingPayment → Paid →
/// Confirmed → Delivered, plus Cancelled, Failed et UnderReview — parce que
/// c'est lui qui pilote le paiement, la libération du stock, la création de
/// course et le règlement des vendeurs. Rien ici ne le touche.
///
/// Ce qui manquait, c'est l'échelle où le VENDEUR agit. « Confirmée » à
/// l'échelle de la commande veut dire « le paiement est encaissé » ; à l'échelle
/// du vendeur cela devrait vouloir dire « j'accepte de préparer ces trois
/// articles-là ». Les deux phrases n'ont pas le même sujet, et une commande
/// multi-vendeurs n'avait aucun endroit pour porter la seconde : c'est ISSUE-027,
/// et c'est ce que cette énumération ouvre.
///
/// POURQUOI CET ENCHAÎNEMENT-LÀ, ET PAS UN AUTRE.
///
///   AwaitingConfirmation → le vendeur n'a rien dit. C'est l'état de naissance,
///                          et il ne naît qu'À LA CONFIRMATION de la commande :
///                          avant le paiement, il n'y a rien qu'un vendeur
///                          puisse faire, et lui montrer une commande non payée
///                          l'inviterait à préparer un colis pour un paiement
///                          qui échouera.
///   Confirmed            → « je l'honore ». C'est l'engagement, et il précède
///                          la préparation : un vendeur peut accepter à
///                          l'instant et n'emballer que le lendemain.
///   Preparing            → le colis se monte. Cet état existe pour l'ACHETEUR
///                          et pour l'exploitation : sans lui, une commande
///                          acceptée depuis trois jours est indiscernable d'une
///                          commande acceptée il y a dix minutes.
///   ReadyForPickup       → le colis attend le livreur. C'est l'état qui
///                          intéresse la course.
///   HandedOver           → le colis a quitté le vendeur.
///
/// Plus deux issues qui ne sont pas des étapes :
///
///   Rejected             → le vendeur n'honore pas, AVANT de s'être engagé.
///   Cancelled            → il n'honore plus, APRÈS s'être engagé (rupture
///                          découverte à l'emballage, casse).
///
/// CE QUE CES ÉTATS NE COUVRENT PAS, ET IL FAUT LE SAVOIR.
///
/// Il n'y a pas d'état « partiellement honoré ». Un vendeur qui peut livrer deux
/// articles sur trois doit aujourd'hui tout accepter ou tout refuser. Découper à
/// la ligne demanderait un état PAR LIGNE, donc un remboursement partiel par
/// ligne, que rien en aval ne sait faire — voir l'encadré de
/// <see cref="SellerOrder"/> sur ce qu'un refus déclenche réellement.
///
/// VALEURS EN QUEUE POUR LES AJOUTS FUTURS, ET COLONNE EN TEXTE.
///
/// Même précaution que sur <see cref="OrderStatus"/> : la colonne est stockée en
/// TEXTE (voir `SellerOrderConfiguration`), ce qui la protège déjà d'un
/// renumérotage — mais les deux précautions ne coûtent rien et la seconde ne
/// dépend pas d'un réglage de mapping.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public enum SellerOrderStatus
{
    AwaitingConfirmation = 0,
    Confirmed = 1,
    Preparing = 2,
    ReadyForPickup = 3,
    HandedOver = 4,
    Rejected = 5,
    Cancelled = 6
}
