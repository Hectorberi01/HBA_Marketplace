namespace HBA.Gateway.Application.Bff.Admin;

/// <summary>
/// Les files d'attente d'administration, en un seul appel.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// L'ÉCRAN D'ACCUEIL DE L'ADMINISTRATEUR, ET LA SEULE CHOSE QU'AUCUN SERVICE NE
/// PEUT RENDRE SEUL.
///
/// Chaque service sait compter SA file : merchant sait combien de dossiers KYB
/// attendent, catalog combien de fiches sont à valider, food combien de
/// restaurants demandent leur ouverture. Aucun ne sait ce que les autres ont sur
/// les bras — et c'est précisément la question que se pose un administrateur en
/// ouvrant l'application : PAR QUOI COMMENCER.
///
/// C'est donc de l'agrégation au sens strict, pas un relais déguisé : les cinq
/// autres écrans d'administration parlent aux services directement, celui-ci ne
/// le peut pas.
///
/// CE QUE CES NOMBRES NE SONT PAS.
///
/// Ce ne sont pas des indicateurs d'activité, et ils ne doivent pas le devenir.
/// Un compteur de files sert à ORIENTER un geste ; y ajouter le chiffre
/// d'affaires ou le nombre de commandes du jour ferait de cet appel — passé à
/// chaque ouverture, par chaque administrateur — une requête d'analyse sur cinq
/// bases. La mesure a sa place ailleurs, et elle n'a pas la même fraîcheur
/// requise.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
/// <param name="Files">Une entrée par file, dans l'ordre où l'écran les présente.</param>
public sealed record AdminQueuesDto(IReadOnlyList<AdminQueueDto> Files);

/// <summary>Une file d'attente d'administration.</summary>
/// <param name="Cle">
/// Identifiant STABLE et fermé — `kyb`, `produits`, `marques`, `restaurants`,
/// `livreurs`.
/// </param>
/// <remarks>
/// LE CLIENT SE BRANCHE SUR `Cle`, JAMAIS SUR `Libelle`.
///
/// C'est la clé qui décide de l'icône, de la couleur et de l'écran vers lequel
/// la tuile navigue. Traduire ou reformuler `Libelle` ne doit rien casser — et
/// ce sera fait, puisque l'application admin est en français et que ces libellés
/// finiront dans des fichiers de traduction.
/// </remarks>
/// <param name="Libelle">Ce que l'administrateur lit.</param>
/// <param name="Total">
/// Nombre d'éléments en attente, ou <c>null</c> si le service n'a pas répondu.
/// </param>
/// <remarks>
/// `null` N'EST PAS `0`, ET LA DISTINCTION EST TOUT L'INTÉRÊT DE CE CHAMP.
///
/// Rendre `0` quand catalog-service est à terre affiche « rien à valider » à un
/// administrateur qui a deux cents fiches en attente. Il ferme l'application et
/// passe à autre chose. `null` fait afficher « indisponible », et l'avertissement
/// correspondant est porté par l'enveloppe.
///
/// C'est le contre-exemple que `DependencyCriticality` cite lui-même : rendre un
/// stock à zéro quand inventory est tombé.
/// </remarks>
/// <param name="Approximatif">
/// Le total est-il plafonné plutôt qu'exact ?
/// </param>
/// <remarks>
/// VRAI POUR LES FILES QUE L'AMONT NE SAIT PAS COMPTER.
///
/// Trois des cinq amonts rendent une LISTE, pas une page : `brands/requests`
/// rend tout, `restaurants/pending` rend tout, et `admin/drivers` rend au plus
/// `take` éléments. On compte donc les éléments reçus — ce qui est exact tant
/// qu'on n'atteint pas la borne, et un plancher au-delà.
///
/// Le dire est indispensable : un « 100 » affiché comme un compte exact quand il
/// y en a mille trois cents décide mal. L'écran affiche « 100+ ».
/// </remarks>
public sealed record AdminQueueDto(
    string Cle,
    string Libelle,
    int? Total,
    bool Approximatif);
