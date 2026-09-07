using System.Collections.Concurrent;
using System.Reflection;

namespace HBA.Shared.Infrastructure.Serialization;

/// <summary>
/// Nom de type stable pour la persistance outbox : « FullName, AssemblyName » sans
/// numéro de version, pour survivre aux montées de version d'assembly.
/// </summary>
public static class EventTypeName
{
    private static readonly ConcurrentDictionary<string, Type> Cache = new(StringComparer.Ordinal);

    public static string Of(Type type) => $"{type.FullName}, {type.Assembly.GetName().Name}";

    /// <summary>
    /// Le type désigné, par correspondance exacte puis par repli sur le nom simple.
    /// </summary>
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
