using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HBA.Shared.Infrastructure.Persistence;

/// <summary>
/// Horodatage de la DERNIÈRE modification d'une ligne.
/// </summary>
public static class HorodatageExtensions
{
    /// <summary>
    /// Nom de la colonne, et seul endroit où il est écrit.
    ///
    /// L'extension la déclare, <c>ModuleDbContext</c> l'estampille : deux
    /// littéraux séparés finiraient par diverger, et la colonne cesserait d'être
    /// remplie sans que rien ne casse.
    /// </summary>
    public const string ColonneModification = "UpdatedAtUtc";

    /// <summary>
    /// Déclare <c>UpdatedAtUtc</c> sur l'entité, estampillée à chaque écriture.
    ///
    /// ═════════════════════════════════════════════════════════════════════════
    /// LA QUESTION QU'ON POSE EN INCIDENT EST « QUAND CETTE LIGNE A-T-ELLE ÉTÉ
    /// TOUCHÉE POUR LA DERNIÈRE FOIS ? », ET LE DÉPÔT NE SAVAIT PAS Y RÉPONDRE.
    ///
    /// Quarante et une tables portaient un <c>Created*Utc</c> sans jamais
    /// d'<c>Updated*Utc</c> — dont <c>orders</c>, <c>payments</c>,
    /// <c>deliveries</c>, <c>withdrawals</c> : précisément les agrégats dont
    /// l'état change plusieurs fois. Sur un paiement, on savait dire quand il
    /// avait été créé, et quand il avait été capturé (colonne dédiée) ; pas quand
    /// la ligne avait bougé la dernière fois. Un paiement resté en
    /// <c>Processing</c> ne disait donc pas s'il y était depuis deux minutes ou
    /// depuis trois jours — c'est-à-dire s'il fallait attendre ou intervenir.
    ///
    /// PROPRIÉTÉ FANTÔME, PAS PROPRIÉTÉ DE DOMAINE.
    ///
    /// La colonne n'existe QUE dans le modèle EF. Le domaine ne la voit pas, ne
    /// la lit pas, ne décide de rien avec. C'est délibéré : c'est une donnée
    /// d'EXPLOITATION, pas une donnée métier. Lui donner une propriété C# la
    /// rendrait disponible à une règle de gestion, et un invariant finirait par
    /// s'appuyer sur l'heure d'un UPDATE — le plus fragile des fondements.
    /// Elle se lit en SQL, ou par <c>EF.Property&lt;DateTime?&gt;(e, "UpdatedAtUtc")</c>.
    ///
    /// NULLABLE, ET LE NUL VEUT DIRE QUELQUE CHOSE.
    ///
    /// <c>NULL</c> = la ligne est ANTÉRIEURE à la colonne. On ne pose pas de
    /// valeur par défaut : <c>DEFAULT now()</c> ferait dire à chaque ligne
    /// ancienne qu'elle a été touchée à l'instant de la migration, ce qui est
    /// faux, et faux d'une manière qui ne se voit pas. Une ligne écrite après la
    /// migration a toujours une valeur — l'estampille court aussi à l'INSERT —
    /// donc <c>UpdatedAtUtc = CreatedAtUtc</c> se lit « jamais modifiée depuis sa
    /// création ».
    /// ═════════════════════════════════════════════════════════════════════════
    ///
    /// <para>
    /// <b>UNE LIGNE PARENTE QUE SEUL UN ENFANT FAIT CHANGER N'EST PAS
    /// ESTAMPILLÉE.</b> Si une opération n'écrit que des lignes ENFANTS, EF
    /// n'émet aucun UPDATE sur le parent, l'entrée reste <c>Unchanged</c> dans le
    /// ChangeTracker, et cette colonne ne bouge pas. C'est exactement l'angle
    /// mort déjà documenté sur <c>UsePostgresRowVersion</c> — même cause, même
    /// remède si on en a besoin (salir une colonne du parent, comme
    /// <c>InventoryItem.StockVersion</c>). Ajouter une ligne de commande ne
    /// change donc pas <c>orders.UpdatedAtUtc</c>.
    /// </para>
    ///
    /// <para>
    /// <b>CE N'EST PAS UN JOURNAL D'AUDIT.</b> La colonne dit QUAND, jamais
    /// QUI ni QUOI : elle est écrasée à chaque écriture et ne garde aucun
    /// historique. Pour l'acteur et le détail des champs, c'est
    /// <c>audit_entries</c> et <c>KeepsAuditTrail</c> — les deux mécanismes se
    /// complètent et ne se remplacent pas.
    /// </para>
    /// </summary>
    public static EntityTypeBuilder<TEntity> HorodateLesModifications<TEntity>(
        this EntityTypeBuilder<TEntity> builder)
        where TEntity : class
    {
        // REFUS EXPLICITE PLUTÔT QUE DOUBLON SILENCIEUX.
        //
        // Dix configurations du dépôt déclarent DÉJÀ un `UpdatedAtUtc` en dur, en
        // propriété C# réelle (catalog, sellers, food, préférences, portefeuille).
        // Poser l'ombre par-dessus donnerait deux propriétés de même nom sur la
        // même table : EF ne s'en plaint pas au démarrage, il choisit — et
        // l'estampille irait dans celle que le domaine ne lit pas.
        //
        // Sur ces entités-là, il n'y a rien à faire : la propriété existe, et le
        // code métier la remplit lui-même. L'extension le dit au lieu de le taire.
        if (typeof(TEntity).GetProperty(ColonneModification) is not null)
        {
            throw new InvalidOperationException(
                $"« {typeof(TEntity).Name} » déclare déjà une propriété « {ColonneModification} » : "
                + "n'appelez pas HorodateLesModifications() dessus, l'entité tient déjà son horodatage.");
        }

        builder.Property<DateTime?>(ColonneModification)
            .HasColumnName(ColonneModification);

        return builder;
    }
}
