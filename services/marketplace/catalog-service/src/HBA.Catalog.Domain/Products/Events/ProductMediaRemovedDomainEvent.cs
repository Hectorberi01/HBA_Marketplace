using HBA.Shared.Domain.Events;

namespace HBA.Catalog.Domain.Products.Events;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// UNE IMAGE A ÉTÉ DÉTACHÉE D'UN PRODUIT — SON FICHIER RESTE À EFFACER.
///
/// SANS CET ÉVÉNEMENT, DÉTACHER UNE IMAGE EST UN MENSONGE.
///
/// La ligne part, les octets restent dans le stockage, et plus rien ne les
/// désigne : ni le produit (qui ne connaît plus le média), ni l'exploitation (qui
/// n'a aucune liste des orphelins). Un catalogue actif fabrique des milliers de
/// ces fichiers par an — c'est du stockage facturé pour des images que plus
/// personne ne peut ni voir ni retrouver.
///
/// CE N'EST PLUS UNE SUPPRESSION SYNCHRONE, ET LA GARANTIE A CHANGÉ.
///
/// Avant, le gestionnaire appelait le stockage AVANT d'enregistrer : si
/// l'effacement échouait, le détachement échouait avec lui, et les deux systèmes
/// restaient d'accord. Désormais Catalog ne fait que NOMMER le fichier ; un
/// adaptateur du composition root l'efface plus tard, via l'outbox.
///
/// On y gagne l'indépendance des modules et la reprise automatique en cas de
/// panne du service média. On y perd la simultanéité : entre le détachement et
/// l'effacement, le fichier existe encore. Pour une image produit publique, c'est
/// acceptable — la fenêtre se compte en secondes et le contenu n'est pas
/// sensible. Ce raisonnement ne vaudrait PAS pour une pièce d'identité.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed record ProductMediaRemovedDomainEvent(
    Guid ProductId,
    Guid MediaId) : DomainEvent;
