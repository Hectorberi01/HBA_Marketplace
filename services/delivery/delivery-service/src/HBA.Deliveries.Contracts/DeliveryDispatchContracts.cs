namespace HBA.Deliveries.Contracts;

/// <summary>Un arrêt de course, décrit par le donneur d'ordre.</summary>
/// <remarks>
/// TROIS CHAMPS LOCALISENT, ET AUCUN N'EST UNE RUE.
///
/// Au Bénin, on se repère par commune, quartier et point de repère — « en face
/// de la pharmacie Ste Rita ». Le domaine refuse un arrêt sans repère : c'est
/// lui qui permet au livreur de trouver la porte.
///
/// Les coordonnées sont facultatives et servent au dispatch, pas à l'humain.
/// </remarks>
public sealed record DeliveryStopRequest(
    string? ContactName,
    string? Phone,
    string? Commune,
    string? Quartier,
    string? Landmark,
    string? Instructions = null,
    double? Latitude = null,
    double? Longitude = null);

/// <summary>Le colis, tel que l'appelant le décrit.</summary>
public sealed record DeliveryPackageRequest(
    string? Description,
    decimal? WeightKg = null,
    bool IsFragile = false,
    bool IsPerishable = false);

/// <summary>Demande de création d'une course.</summary>
/// <param name="Reference">
/// La référence du DONNEUR D'ORDRE — « ORDER-&lt;guid&gt; », « FOOD-&lt;guid&gt; ».
/// Delivery ne l'interprète jamais ; il la rend telle quelle dans ses
/// événements, et c'est l'émetteur qui sait la relire.
/// </param>
/// <param name="Source">« HbaExpress », « HbaFood » ou « ExternalPartner ».</param>
/// <param name="Type">« Express », « Standard » ou « Scheduled ».</param>
public sealed record CreateDeliveryRequest(
    string Reference,
    string Source,
    string Type,
    DeliveryStopRequest Pickup,
    DeliveryStopRequest Dropoff,
    DeliveryPackageRequest Package,

    // « RequiredProof » A DISPARU DE CE CONTRAT — ISSUE-057.
    //
    // Il valait « None » PAR DÉFAUT, et les deux seuls appelants réels ne le
    // renseignaient jamais. Le défaut n'était donc pas une négligence
    // ponctuelle : il était la valeur par défaut du contrat lui-même, et
    // n'importe quel troisième appelant l'aurait reproduit.
    //
    // Le donneur d'ordre DÉCRIT désormais sa course ; `ProofPolicy`, dans le
    // domaine de delivery-service, en déduit la preuve exigée.
    decimal? DeclaredValue = null,
    bool IsCashOnDelivery = false,
    Guid? PartnerId = null,
    string? QuoteId = null,
    DateTime? ScheduledForUtc = null);

/// <summary>
/// Résultat d'une création.
/// </summary>
/// <remarks>
/// UN REFUS N'EST PAS UNE EXCEPTION.
///
/// Une commune inconnue, un téléphone invalide, un quota partenaire atteint :
/// ce sont des réponses métier, fréquentes et attendues. Les faire remonter en
/// `RpcException` obligerait chaque appelant à distinguer « refusé » de « le
/// service est tombé » en lisant un code de statut. Le motif voyage donc dans
/// la réponse.
/// </remarks>
/// <param name="ReasonCode">
/// LE CODE NORMALISÉ, ET IL EST EN QUEUE AVEC UNE VALEUR PAR DÉFAUT.
///
/// Ajouté en dernier, optionnel : les constructions positionnelles existantes —
/// dont les doubles de test — compilent sans retouche. L'insérer au milieu
/// aurait décalé `Reason` sur `DeliveryId` dans tout appel positionnel, ce qui
/// COMPILE quand les types s'y prêtent et se découvre à l'exécution.
///
/// Nul quand l'appelé ne le renseigne pas encore (règle additive D32).
/// </param>
public sealed record DeliveryCreationResult(
    bool Succeeded, Guid DeliveryId, string? Reason, string? ReasonCode = null);

/// <summary>
/// Résultat d'une annulation demandée par le donneur d'ordre.
/// </summary>
/// <remarks>
/// TROIS ISSUES, ET IL FAUT LES TROIS.
///
///   • <c>Found = false</c> — aucune course sous cette référence. C'est le cas
///     LE PLUS FRÉQUENT et il est normal : la plupart des commandes sont
///     annulées avant confirmation, donc avant qu'une course n'existe. Le
///     traiter en erreur ferait sonner une alerte à chaque annulation ordinaire.
///   • <c>Found = true, Cancelled = false</c> — le domaine a refusé : colis déjà
///     collecté, course déjà terminée. Il y a alors un colis en circulation pour
///     une commande annulée, et cela relève de l'exploitation.
///   • <c>Cancelled = true</c> — le livreur ne se déplacera pas pour rien.
/// </remarks>
public sealed record DeliveryCancellationResult(
    bool Found, bool Cancelled, string? Reason, string? ReasonCode = null);

// `DeliveryQuoteDetails` A ÉTÉ DÉPLACÉ dans `HBA.DeliveryPricing.Contracts`,
// avec la relecture qu'il décrivait. Le laisser ici en aurait fait un type sans
// producteur — et deux types de même nom le jour où quelqu'un aurait référencé
// les deux contrats.

/// <summary>
/// La surface d'ÉCRITURE du moteur logistique, ouverte aux donneurs d'ordre.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// POURQUOI UNE SECONDE INTERFACE PLUTÔT QUE D'ÉLARGIR LA PREMIÈRE.
///
/// <c>IDeliveryModuleApi</c> déclare, noir sur blanc : « LECTURE SEULE, et c'est
/// délibéré. Créer une course passe par une commande MediatR — un autre module
/// ne doit pas pouvoir déclencher une livraison par un simple appel de méthode,
/// sans validation ni événement. »
///
/// Y ajouter <c>CreateAsync</c> aurait effacé cette intention d'une ligne, et
/// tous les appelants existants — qui ne lisent que le suivi — auraient hérité
/// du droit de créer.
///
/// L'invariant est PRÉSERVÉ, pas contourné : l'implémentation gRPC de cette
/// interface envoie un <c>CreateDeliveryCommand</c> par MediatR côté serveur.
/// Validation, règles du domaine et publication d'événements ont lieu
/// exactement comme par la route REST. Ce qui change, c'est le transport.
///
/// DEUX APPELANTS, ET C'EST LE CHOIX D'ARCHITECTURE.
///
/// order-service à la confirmation d'une commande, food-service quand un repas
/// est prêt. L'alternative — delivery-service consommant leurs événements —
/// l'aurait obligé à référencer les contrats de Food et d'Ordering, exactement
/// le couplage que son indépendance interdit.
///
/// Le sens de la dépendance est donc : les donneurs d'ordre connaissent le
/// transporteur ; le transporteur ne connaît personne.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public interface IDeliveryDispatchApi
{
    // ═════════════════════════════════════════════════════════════════════════
    // `RequestQuoteAsync` ET `LookupQuoteAsync` ONT QUITTÉ CE CONTRAT.
    //
    // Les deux s'appuyaient sur `DeliveryApi.GetQuote` et `DeliveryApi.LookupQuote`,
    // QUI N'ONT JAMAIS EU DE CORPS DE SERVEUR. `LookupQuoteAsync` était appelé par
    // les deux checkouts — marchandise et repas — et rendait `UNIMPLEMENTED` :
    // le devis étant obligatoire pour un repas, aucune commande de repas ne
    // pouvait aboutir.
    //
    // ET LES IMPLÉMENTER ICI N'AURAIT RIEN RÉGLÉ. delivery-service n'a plus de
    // domaine de tarification depuis la séparation des deux services : ses
    // entités `DeliveryQuote`, `DeliveryZone` et `PricingRule` n'existent plus
    // dans son code, et sa table `delivery_quotes` n'était écrite par personne.
    //
    // Le devis se demande et se relit chez delivery-pricing —
    // `HBA.DeliveryPricing.Contracts.IDeliveryQuoteLookup`, qui porte le
    // raisonnement complet sur « relire n'est pas redemander ». Ce contrat-ci
    // garde ce que delivery-service fait vraiment : créer, annuler, suivre.
    //
    // CE SERVICE CONSOMME TOUJOURS LE DEVIS, et c'est cohérent :
    // `CreateDeliveryCommand` appelle `ConsumeQuote` chez delivery-pricing, à
    // usage unique. Demander le prix et le dépenser sont deux gestes distincts.
    // ═════════════════════════════════════════════════════════════════════════

    Task<DeliveryCreationResult> CreateAsync(
        CreateDeliveryRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LE DONNEUR D'ORDRE DÉFAIT CE QU'IL A DEMANDÉ.
    ///
    /// ELLE MANQUAIT, ET L'ASYMÉTRIE ENVOYAIT DES LIVREURS DANS LE VIDE.
    ///
    /// order-service savait CRÉER une course à la confirmation, et rien pour
    /// l'annuler : `IDeliveryDispatchApi` n'apparaissait dans tout order-service
    /// qu'à la création. Une commande annulée laissait donc sa course VIVANTE —
    /// un livreur partait chercher un colis que le vendeur ne remettrait pas.
    /// Trajet à vide, livreur immobilisé sur une mission morte, et personne sur
    /// place pour le lui expliquer.
    ///
    /// PAR RÉFÉRENCE, PAS PAR IDENTIFIANT DE COURSE.
    ///
    /// Le donneur d'ordre ne retient pas l'identifiant rendu à la création — rien
    /// dans la commande ne le stocke. Il ne connaît que SA référence, et c'est
    /// tout ce que cette signature doit lui demander.
    ///
    /// AUCUN `RequiredPartnerId` ICI, ET C'EST VOULU.
    ///
    /// `CancelDeliveryCommand` en porte un pour protéger les partenaires
    /// externes les uns des autres. Ce contrat-ci n'est joignable que par la clé
    /// interne de service à service, et un donneur d'ordre HBA ne peut désigner
    /// que ses propres références — le préfixe le lui garantit.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    Task<DeliveryCancellationResult> CancelByReferenceAsync(
        string reference, string source, string? reason, CancellationToken cancellationToken = default);
}
