using System.Collections.Concurrent;
using System.Reflection;

namespace HBA.Shared.Infrastructure.Serialization;

/// <summary>
/// Nom de type stable pour la persistance outbox : « FullName, AssemblyName »
/// sans numéro de version, pour survivre aux montées de version d'assembly.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// CE NOM EST UNE IDENTITÉ PERSISTÉE, ET IL CONTIENT LE NAMESPACE.
///
/// `outbox_messages.Type` porte le nom COMPLET du type de l'événement. Tant que
/// `Resolve` exigeait une correspondance exacte, **déplacer un type d'événement
/// d'un namespace à un autre cassait tous les messages déjà en base** : ils
/// n'auraient plus jamais été publiés, et le contenu de l'outbox est du paiement,
/// du remboursement et de la libération de stock.
///
/// Le défaut a été trouvé en évaluant le renommage des 257 namespaces désalignés
/// (lot 9.5) : la substitution aurait été silencieuse à la compilation, verte à
/// tous les contrôles, et fatale au premier redémarrage sur une base qui a des
/// messages en attente.
///
/// ET LE FIL KAFKA, LUI, N'EST PAS CONCERNÉ.
///
/// `KafkaEventNaming.EventType` dérive le type d'événement du nom SIMPLE de la
/// classe. Un renommage de namespace y est invisible. L'asymétrie est réelle :
/// c'est l'outbox, pas le bus, qui tient le namespace pour une identité.
///
/// CE QUE LA TOLÉRANCE NE FAIT PAS : elle ne rend pas un RENOMMAGE DE CLASSE
/// sûr. Le repli se fait sur le nom simple ; renommer la classe elle-même laisse
/// les messages en attente irrésolubles, et cela reste interdit par la règle
/// additive (D32).
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public static class EventTypeName
{
    private static readonly ConcurrentDictionary<string, Type> Cache = new(StringComparer.Ordinal);

    public static string Of(Type type) => $"{type.FullName}, {type.Assembly.GetName().Name}";

    /// <summary>
    /// Le type désigné, par correspondance exacte puis par repli sur le nom simple.
    /// </summary>
    /// <remarks>
    /// LE REPLI EXIGE UNE CORRESPONDANCE UNIQUE, ET REFUSE SINON.
    ///
    /// Deux classes de même nom simple dans deux namespaces — le dépôt en compte
    /// 77 — rendraient le repli ambigu. Choisir la première serait le pire des
    /// comportements : un message de paiement désérialisé dans le type d'un autre
    /// module, sans une erreur. On refuse, et le message part en échec avec un
    /// motif qui nomme les candidats.
    ///
    /// ON NE CHERCHE QUE PARMI LES ASSEMBLAGES DÉJÀ CHARGÉS, ce qui suffit :
    /// l'événement est publié par le service qui l'a écrit, et son assemblage de
    /// contrats est chargé puisque le message y a été sérialisé. Balayer le disque
    /// coûterait cher au démarrage pour un cas qui ne se présente pas.
    /// </remarks>
    public static Type Resolve(string typeName)
        => Cache.GetOrAdd(typeName, Chercher);

    private static Type Chercher(string typeName)
    {
        if (Type.GetType(typeName) is { } exact)
        {
            return exact;
        }

        // « Namespace.Classe, Assemblage » → « Classe »
        var avantVirgule = typeName.Split(',')[0].Trim();
        var nomSimple = avantVirgule[(avantVirgule.LastIndexOf('.') + 1)..];

        if (nomSimple.Length == 0)
        {
            throw new InvalidOperationException($"Type d'event introuvable : {typeName}");
        }

        var candidats = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(TypesDe)
            .Where(t => string.Equals(t.Name, nomSimple, StringComparison.Ordinal))
            .Distinct()
            .ToList();

        if (candidats.Count == 1)
        {
            return candidats[0];
        }

        if (candidats.Count > 1)
        {
            throw new InvalidOperationException(
                $"Type d'event ambigu : « {typeName} » est introuvable tel quel, et « {nomSimple} » "
                + $"existe dans {candidats.Count} namespaces ({string.Join(", ", candidats.Select(c => c.FullName))}). "
                + "Le message n'est pas publié : désambiguïser avant de rejouer.");
        }

        throw new InvalidOperationException($"Type d'event introuvable : {typeName}");
    }

    /// <remarks>
    /// UN ASSEMBLAGE PEUT REFUSER DE RENDRE SES TYPES. `ReflectionTypeLoadException`
    /// survient dès qu'une dépendance optionnelle manque — et laisser l'exception
    /// remonter ferait échouer la résolution de TOUS les messages à cause d'un
    /// assemblage sans rapport. On garde ce qui a pu être chargé.
    /// </remarks>
    private static IEnumerable<Type> TypesDe(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t is not null)!;
        }
        catch (Exception)
        {
            return Array.Empty<Type>();
        }
    }
}
