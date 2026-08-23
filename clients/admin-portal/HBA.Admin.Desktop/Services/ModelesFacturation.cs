using System.Text.Json.Serialization;

namespace HBA.Admin.Desktop.Services;

/// <summary>Une règle de commission, telle que `CommissionRuleSummary` la rend.</summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// LA RÉSOLUTION EST SAINE, ET ELLE NE SE DEVINE PAS À L'ÉCRAN.
///
/// `CommissionResolver` retient la règle applicable la plus SPÉCIFIQUE :
/// `Priority => (int)Scope`, donc `Seller` (2) devant `Category` (1) devant
/// `Global` (0). À portée égale, la plus récente par `EffectiveFromUtc` gagne.
/// Et si rien ne correspond, le moteur applique un TAUX PAR DÉFAUT — pas zéro.
///
/// C'est pourquoi l'écran affiche les règles triées par portée et non par date :
/// l'ordre d'affichage est l'ordre d'application.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
/// <param name="TargetId">
/// Le vendeur ou la catégorie visé. Nul sur une règle `Global`.
/// </param>
/// <param name="Rate">Taux entre 0 et 1 — le domaine refuse hors de ces bornes.</param>
public sealed record RegleCommission(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("scope")] string Scope,
    [property: JsonPropertyName("targetId")] Guid? TargetId,
    [property: JsonPropertyName("rate")] decimal Rate,
    [property: JsonPropertyName("fixedFee")] decimal FixedFee,
    [property: JsonPropertyName("currency")] string Currency,
    [property: JsonPropertyName("minFee")] decimal? MinFee,
    [property: JsonPropertyName("maxFee")] decimal? MaxFee,
    [property: JsonPropertyName("effectiveFromUtc")] DateTime EffectiveFromUtc,
    [property: JsonPropertyName("isActive")] bool IsActive);

/// <summary>Le résultat d'un aperçu de commission, `CommissionResult`.</summary>
/// <remarks>
/// `AppliedRuleId` NUL SIGNIFIE « AUCUNE RÈGLE », PAS « ERREUR ».
///
/// Le moteur applique alors le taux par défaut de la plateforme. Un gestionnaire
/// antérieur recopiait le résolveur et rendait `0` dans ce cas — « l'écran
/// d'administration annonçait donc commission : 0 pendant que la comptabilisation
/// prélevait 10 % ». Cette copie a été supprimée ; l'aperçu délègue au moteur.
/// L'écran doit néanmoins DIRE qu'aucune règle ne s'est appliquée, sinon on croit
/// avoir configuré ce qu'on n'a pas configuré.
/// </remarks>
public sealed record ApercuCommission(
    [property: JsonPropertyName("grossAmount")] decimal GrossAmount,
    [property: JsonPropertyName("commissionAmount")] decimal CommissionAmount,
    [property: JsonPropertyName("netAmount")] decimal NetAmount,
    [property: JsonPropertyName("currency")] string Currency,
    [property: JsonPropertyName("appliedRuleId")] Guid? AppliedRuleId);

/// <summary>Une facture, telle que `InvoiceSummary` la rend.</summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// LES LIGNES NE SONT EXPOSÉES PAR AUCUNE ROUTE.
///
/// `Invoice` porte des `InvoiceLine`, le dépôt les charge (`Include(i => i.Lines)`)
/// — et `InvoiceMapper.ToSummary` les laisse tomber. `GetInvoiceQuery` rend le
/// même `InvoiceSummary` que la liste.
///
/// CONSÉQUENCE : on peut AJOUTER une ligne à une facture et ne jamais la relire.
/// Seul le total change. L'écran le dit plutôt que d'afficher un détail vide qui
/// ferait croire à une facture sans postes.
///
/// Corriger cela demande de toucher un contrat public que les clients vendeur
/// consomment déjà — ce n'est pas un geste de console.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed record FactureAdmin(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("sellerId")] Guid SellerId,
    [property: JsonPropertyName("periodStartUtc")] DateTime PeriodStartUtc,
    [property: JsonPropertyName("periodEndUtc")] DateTime PeriodEndUtc,
    [property: JsonPropertyName("currency")] string Currency,
    [property: JsonPropertyName("totalAmount")] decimal TotalAmount,
    [property: JsonPropertyName("status")] string Status);
