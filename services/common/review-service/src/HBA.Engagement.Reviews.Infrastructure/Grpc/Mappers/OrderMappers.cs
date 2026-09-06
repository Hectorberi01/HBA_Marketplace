using Grpc.Core;
using HBA.Ordering.Contracts;
using HBA.Shared.Hosting.Grpc;
using HBA.Shared.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Proto = HBA.Orders.Grpc.V1;
using ServiceLine = HBA.Orders.Contracts.OrderLineSummary;
using ServiceOrder = HBA.Orders.Contracts.OrderSummary;

using SharedLine = HBA.Ordering.Contracts.OrderLineSummary;
using SharedOrder = HBA.Ordering.Contracts.OrderSummary;
using System.Globalization;
using System.Runtime.CompilerServices;

// ═════════════════════════════════════════════════════════════════════════════
// COPIE DEPUIS `HBA.Ordering.Contracts.Grpc` (lot D — dissolution des assemblages de contrats).
//
// `shared/` ne contient plus que les `.proto`. Ce service compile lui-meme le
// contrat dont il a besoin, et porte donc sa propre traduction.
//
// LES TYPES GENERES SONT `internal` A CET ASSEMBLAGE. Deux services qui
// compilent le meme proto obtiennent deux types CLR distincts ; les rendre
// publics ferait, dans un hote compose, deux types publics du meme nom complet —
// CS0433, a l'usage, loin de la cause. Les adaptateurs et mappings sont donc
// `internal` eux aussi : un type public dont la signature expose un type interne
// ne compile pas.
//
// CE QUE ÇA COUTE : cette traduction existe en 8 exemplaires dans le depot,
// un par service qui appelle ce domaine. Elles sont identiques aujourd'hui et
// rien n'empeche qu'elles divergent. C'est le prix de l'autonomie par service,
// paye ici en connaissance de cause.
// ═════════════════════════════════════════════════════════════════════════════

namespace HBA.Engagement.Reviews.Infrastructure.Grpc.Mappers;

internal static class OrderingGrpcParsing
{
    public static Guid ParseGuid(string? value)
        => Guid.TryParse(value, out var id) ? id : Guid.Empty;

    public static DateTime ParseDate(string? value)
        => DateTime.TryParse(
               value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var date)
            ? date
            : DateTime.MinValue;

    // Chaîne vide et null se confondent en protobuf3 : un champ absent arrive
    // comme "". Rendre "" là où le contrat attend `null` ferait afficher des
    // valeurs vides au lieu de « non renseigné ».
    public static string? Vide(string? value) => string.IsNullOrEmpty(value) ? null : value;

    /// <summary>
    /// Un montant venu du fil.
    /// </summary>
    /// <remarks>
    /// REFUSAIT DE RENDRE ZÉRO — voir <see cref="MontantSurLeFil"/>. Cette
    /// fonction s'écrivait « TryParse(…) ? valeur : 0m », comme six autres du
    /// dépôt : un champ non posé par l'émetteur — donc la chaîne VIDE, il n'y a
    /// pas de « non renseigné » pour un `string` protobuf 3 — se lisait « zéro
    /// franc ».
    ///
    /// `champ` EST REMPLI PAR LE COMPILATEUR, pas à la main. Il reçoit le TEXTE
    /// de l'expression passée — « order.AlreadyRefundedAmount » — donc un nom plus
    /// précis qu'aucun littéral recopié, et qui suit les renommages tout seul.
    /// </remarks>
    public static decimal ParseDecimal(
        string? value, [CallerArgumentExpression(nameof(value))] string champ = "")
        => MontantSurLeFil.Lire(value, champ);
}
