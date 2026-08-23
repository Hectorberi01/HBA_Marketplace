namespace HBA.Shared.Infrastructure.Audit;

/// <summary>Ce qui est arrivé à une ligne.</summary>
public enum AuditOperation
{
    Created = 0,
    Updated = 1,
    Deleted = 2
}

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LE JOURNAL DE QUI A FAIT QUOI — UNE LIGNE PAR ENTITÉ MUTÉE, PAR REQUÊTE.
///
/// POURQUOI UN JOURNAL, ET NON UNE COLONNE `LastModifiedBy` SUR CHAQUE TABLE.
///
/// Une colonne ne retient que le DERNIER geste : le membre qui a corrigé une
/// faute de frappe efface la trace de celui qui a divisé le prix par dix la
/// veille. Or la question qu'on pose après coup n'est jamais « qui a touché ça en
/// dernier », c'est « qui a fait CE changement-là ». Une colonne ne peut pas y
/// répondre, quel que soit le soin qu'on met à la remplir.
///
/// ET POURQUOI IL N'EST PAS ALIMENTÉ PAR LES COMMANDES.
///
/// L'autre forme possible était un `CallerUserId` ajouté à chacune des trente-huit
/// commandes de mutation de ces trois services, recopié à la main dans chaque
/// handler. Cela aurait marché le jour de la livraison. La trente-neuvième
/// commande, écrite six mois plus tard par quelqu'un qui n'a pas lu ce texte,
/// n'aurait rien journalisé — et son absence du journal serait indiscernable d'un
/// geste qui n'a pas eu lieu.
///
/// Ici, la source est le `ChangeTracker` : une entité modifiée est journalisée
/// qu'on y ait pensé ou non. C'est le même raisonnement que l'outbox, quelques
/// lignes plus haut dans le même `SaveChangesAsync`.
///
/// IL EST ÉCRIT DANS LA MÊME TRANSACTION QUE LA MUTATION.
///
/// Un journal écrit après coup — par un événement, par un consommateur — peut
/// manquer précisément les lignes qui comptent : celles d'une transaction qui a
/// échoué à mi-parcours, ou celles qu'un incident a interrompues. Ici, ou les deux
/// sont écrits, ou aucun ne l'est.
///
/// CE N'EST PAS UN JOURNAL DE VALEURS.
///
/// Il retient QUI, QUOI, QUAND — pas l'avant/après. Enregistrer les valeurs
/// ferait entrer dans cette table des adresses, des numéros de téléphone et des
/// prix, c'est-à-dire recopier la base dans une table que personne ne purge et que
/// `AUDIT_VIEW` rend lisible à un rôle de gestion. Le besoin du §37 est la
/// RESPONSABILITÉ, pas la restauration.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class AuditEntry
{
    public long Id { get; set; }

    /// <summary>Nom court de l'entité mutée, ex. <c>Product</c>, <c>InventoryItem</c>.</summary>
    public string EntityType { get; set; } = string.Empty;

    /// <summary>
    /// Clé primaire de la ligne, sous forme textuelle.
    /// </summary>
    /// <remarks>
    /// TEXTE ET NON `Guid` : les clés de ce dépôt ne sont pas toutes des GUID —
    /// `IdempotencyRecord` a une clé composite, `ConsumerInboxEntry` aussi. Une
    /// colonne `uuid` obligerait à ignorer ces entités, c'est-à-dire à créer des
    /// trous silencieux dans le journal.
    /// </remarks>
    public string EntityId { get; set; } = string.Empty;

    public AuditOperation Operation { get; set; }

    /// <summary>
    /// L'acteur, repris de <c>HbaRequestContext.Current.Actor</c>.
    /// </summary>
    /// <remarks>
    /// NULL EST UNE VALEUR LÉGITIME, ET ELLE VEUT DIRE QUELQUE CHOSE.
    ///
    /// Un consommateur Kafka, un appel gRPC interne, un travail de fond : la
    /// mutation n'a PAS de personne derrière elle. Écrire « SYSTEM » dans la
    /// colonne utilisateur ferait passer un traitement automatique pour un compte,
    /// et le jour où l'on chercherait qui a annulé mille commandes, on trouverait
    /// un utilisateur qui n'existe pas. <see cref="ActorType"/> porte la nuance.
    /// </remarks>
    public Guid? ActorUserId { get; set; }

    /// <summary><c>CUSTOMER</c>, <c>SELLER</c>, <c>ADMIN</c>, <c>SYSTEM</c>… (§19.1).</summary>
    public string ActorType { get; set; } = "SYSTEM";

    /// <summary>
    /// Le fil du geste, repris du contexte de requête.
    /// </summary>
    /// <remarks>
    /// C'EST LUI QUI RELIE LE JOURNAL AUX TRACES ET AUX ÉVÉNEMENTS.
    ///
    /// Sans corrélation, une ligne de journal dit « ce membre a modifié cette
    /// offre à 14 h 03 » et s'arrête là. Avec, on remonte à la requête HTTP, à la
    /// trace, et aux événements qu'elle a publiés — c'est-à-dire à ce qui a suivi.
    /// </remarks>
    public string? CorrelationId { get; set; }

    public DateTime OccurredOnUtc { get; set; }
}
