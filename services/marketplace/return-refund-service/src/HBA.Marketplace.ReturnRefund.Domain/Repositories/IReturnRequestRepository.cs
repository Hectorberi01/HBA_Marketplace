using HBA.Marketplace.ReturnRefund.Domain.Aggregates.ReturnRequest;
using HBA.Marketplace.ReturnRefund.Domain.Enums;

namespace HBA.Marketplace.ReturnRefund.Domain.Repositories;

/// <summary>
/// Un remboursement DÉCIDÉ dont le versement n'est pas encore acquis : de quoi
/// recharger l'agrégat et relancer l'exécution.
///
/// <para>
/// Volontairement réduit à deux identifiants. Le balayage ne DÉCIDE de rien —
/// il désigne. Faire remonter le montant ou le statut inviterait à trancher dans
/// le worker, hors de l'agrégat et hors de toute transaction, et c'est
/// exactement ainsi qu'on rembourse deux fois.
/// </para>
/// </summary>
public sealed record RefundExecutionTicket(Guid ReturnId, Guid RefundId);

public interface IReturnRequestRepository
{
    Task<ReturnRequest?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<ReturnRequest?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken);
    Task AddAsync(ReturnRequest request, string idempotencyKey, CancellationToken cancellationToken);
    Task<IReadOnlyList<ReturnRequest>> ListCustomerAsync(Guid customerId, int page, int pageSize, CancellationToken cancellationToken);
    Task<IReadOnlyList<ReturnRequest>> ListSellerAsync(Guid sellerId, int page, int pageSize, CancellationToken cancellationToken);

    /// <summary>
    /// Une page de dossiers, toutes boutiques confondues, pour l'administration.
    /// </summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LES DEUX LISTES VOISINES RENDENT UN TOTAL FAUX, ET CELLE-CI NON.
    ///
    /// `GetCustomerReturnsQueryHandler` et `GetSellerReturnsQueryHandler`
    /// construisent leur `PagedResult` avec `items.Count` en guise de total —
    /// c'est-à-dire la taille de la PAGE. Un client qui en déduit un nombre de
    /// pages en trouve toujours une seule, et la pagination s'arrête à la
    /// première. C'est corrigé dans le même lot ; cette méthode-ci rend le total
    /// réel depuis le début.
    ///
    /// LE COMPTE PAR STATUT EST CALCULÉ AVANT LE FILTRE.
    ///
    /// Sinon les facettes ne montreraient qu'un seul statut — celui qu'on vient
    /// de choisir — et l'écran perdrait la seule information qui dit où aller
    /// ensuite. Même choix que `UserRepository.ListPagedAsync`.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
    Task<(IReadOnlyList<ReturnRequest> Items, int Total, IReadOnlyDictionary<string, int> StatusCounts)>
        ListForAdminAsync(int page, int pageSize, ReturnStatus? status, CancellationToken cancellationToken);

    /// <summary>Le nombre de dossiers d'un client — pour un total de page exact.</summary>
    Task<int> CountCustomerAsync(Guid customerId, CancellationToken cancellationToken);

    /// <summary>Le nombre de dossiers d'une boutique — pour un total de page exact.</summary>
    Task<int> CountSellerAsync(Guid sellerId, CancellationToken cancellationToken);

    /// <summary>
    /// Les remboursements en attente de versement, du plus ancien au plus récent,
    /// et seulement ceux dont le DOSSIER attend encore son versement
    /// (<c>ReturnStatus.RefundPending</c>).
    /// </summary>
    /// <remarks>
    /// `Processing` EST INCLUS, ET C'EST LE POINT.
    ///
    /// Un remboursement resté `Processing` est une exécution interrompue : le
    /// processus est tombé entre la réservation et la réponse du prestataire. Sans
    /// lui dans cette liste, ce dossier n'est plus jamais repris par personne — le
    /// client attend indéfiniment un versement que rien ne relance.
    ///
    /// Le reprendre est sûr parce que la clé d'idempotence est déterministe : le
    /// service Payment reconnaît la tentative précédente et rend son issue au lieu
    /// de verser une seconde fois.
    ///
    /// ET LE DOSSIER DOIT ÊTRE EN `RefundPending`.
    ///
    /// Un remboursement définitivement en échec reste `Failed` alors que son
    /// dossier est passé en `ManualReview`. Sans cette restriction, le balayage le
    /// reprendrait indéfiniment et journaliserait une erreur toutes les vingt
    /// secondes sur un dossier déjà confié à un humain.
    /// </remarks>
    Task<IReadOnlyList<RefundExecutionTicket>> ListRefundsAwaitingExecutionAsync(int batchSize, CancellationToken cancellationToken);

    /// <summary>
    /// Les dossiers dont le délai est dépassé et qui attendent encore quelqu'un.
    /// </summary>
    /// <remarks>
    /// Restreint à `AwaitingApproval` et `AwaitingReturn` : ce sont les DEUX
    /// SEULS états depuis lesquels `ReturnStateMachine` mène à `Expired`. Élargir
    /// la sélection ferait remonter des dossiers que `Expire()` refuserait, et le
    /// balayage tournerait à vide en boucle sur les mêmes lignes.
    /// </remarks>
    Task<IReadOnlyList<ReturnRequest>> ListExpirableAsync(DateTime nowUtc, int batchSize, CancellationToken cancellationToken);

    /// <summary>
    /// Ce que les dossiers ENCORE OUVERTS de cette commande ont déjà engagé, ligne
    /// de commande par ligne de commande.
    /// </summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// C'EST LA MOITIÉ D'ISSUE-014 QU'ORDER-SERVICE NE PEUT PAS COUVRIR.
    ///
    /// Order-service n'apprend un retour qu'au moment où l'argent PART
    /// (`ReturnRefundedIntegrationEvent`). Entre l'ouverture d'un dossier et son
    /// versement — validation vendeur, transport retour, réception, inspection —
    /// il ne voit rien. Deux dossiers ouverts en parallèle sur la même ligne
    /// passeraient donc tous deux le contrôle de quantité, et le même exemplaire
    /// serait remboursé deux fois.
    ///
    /// Ces dossiers-là, return-refund les possède. Cette lecture les compte.
    ///
    /// LES ÉTATS TERMINAUX SONT EXCLUS, ET IL LE FAUT DANS LES DEUX SENS.
    ///
    /// `Refunded` et `Closed` sont déjà comptés par order-service : les inclure
    /// ici les compterait DEUX fois et interdirait un retour légitime. `Rejected`,
    /// `Cancelled` et `Expired` n'ont rien engagé du tout : les inclure
    /// consommerait définitivement le droit au retour d'un client dont la demande
    /// a été refusée.
    ///
    /// CE QUI RESTE OUVERT, ET C'EST BORNÉ.
    ///
    /// Entre le passage en `Refunded` et la consommation du message par
    /// order-service — le temps d'un outbox et d'un aller Kafka — la quantité
    /// n'est comptée par personne. La fenêtre se mesure en secondes ; la fermer
    /// exigerait de compter des deux côtés, donc de compter deux fois pendant tout
    /// le reste du temps.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
    /// <param name="exceptReturnId">
    /// Dossier à ne pas compter — le sien, quand l'appelant est en train de
    /// décider sur ce dossier-là et compte déjà ses propres engagements.
    /// </param>
    Task<IReadOnlyDictionary<Guid, int>> ListOpenQuantitiesByOrderAsync(
        Guid orderId, Guid? exceptReturnId, CancellationToken cancellationToken);

    /// <summary>
    /// Le résumé des retours d'une commande : ce qui a été REMBOURSÉ, et combien
    /// de dossiers sont ENCORE OUVERTS.
    /// </summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// CE QUI ÉTAIT CASSÉ (audit du 27 août, constat 1.5).
    ///
    /// `GetOrderReturnSummaryQueryHandler` ne lisait RIEN. Il injectait ce dépôt,
    /// ne l'appelait jamais, et rendait une constante :
    ///
    ///     return new OrderReturnSummaryDto(query.OrderId, 0m, "XOF", 0);
    ///
    /// L'écran affichait donc « 0 remboursé, 0 retour actif » sur toutes les
    /// commandes de la plateforme, y compris celles remboursées la veille. Le pire
    /// n'est pas l'absence d'information : c'est qu'un zéro se lit comme une
    /// réponse. Un conseiller qui voit « 0 remboursé » conclut qu'il n'y a rien eu.
    ///
    /// ═════════════════════════════════════════════════════════════════════════
    /// DEUX DÉCISIONS DE COMPTAGE, ET IL FAUT LES CONNAÎTRE.
    ///
    /// 1. LE MONTANT COMPTE L'ARGENT PARTI, PAS L'ARGENT PROMIS. On additionne les
    ///    `Refund` en `Succeeded`, et non les `ApprovedRefundAmount` des dossiers.
    ///    Un remboursement approuvé dont l'exécution échoue chez le fournisseur
    ///    doit compter pour ZÉRO : le client n'a rien reçu. Compter l'approuvé
    ///    afficherait un remboursement que la banque n'a jamais vu, et c'est
    ///    exactement le genre de chiffre qu'on oppose à un client mécontent.
    ///
    /// 2. « ACTIF » REPREND LA MÊME DÉFINITION QUE `ListOpenQuantitiesByOrderAsync`.
    ///    Six statuts terminaux — Refunded, Closed, Rejected, RejectedAfterInspection,
    ///    Cancelled, Expired — tout le reste est ouvert. Deux définitions du mot
    ///    « ouvert » dans le même dépôt divergeraient au premier statut ajouté.
    ///
    /// ═════════════════════════════════════════════════════════════════════════
    /// CE QUE CETTE LECTURE NE COUVRE PAS.
    ///
    ///   • LA DEVISE EST CELLE DU PREMIER REMBOURSEMENT TROUVÉ, et « XOF » quand
    ///     il n'y en a aucun. La plateforme n'opère qu'au Bénin ; le jour où une
    ///     commande mêle deux devises, cette somme sera FAUSSE SANS RIEN DIRE.
    ///     C'est le défaut le plus probable de cette méthode, écrit ici pour qu'on
    ///     le trouve. Le remède serait un montant par devise, donc un changement
    ///     de forme du DTO — il n'est pas fait.
    ///
    ///   • ELLE NE DIT PAS CE QUI EST EN COURS D'EXÉCUTION. Un remboursement en
    ///     `Processing` ne compte ni dans le montant, ni nulle part ailleurs dans
    ///     ce résumé. Il n'est donc pas visible qu'un versement est en vol.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
    Task<(decimal MontantRembourse, string Devise, int DossiersActifs)> GetOrderSummaryAsync(
        Guid orderId, CancellationToken cancellationToken);
}
