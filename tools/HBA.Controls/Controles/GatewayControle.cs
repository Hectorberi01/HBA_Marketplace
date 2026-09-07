using System.Text.Json;
using System.Text.RegularExpressions;

namespace HBA.Controls.Controles;

/// <summary>
/// Les cinq endroits qui rendent un service joignable portent-ils les mêmes clés ?
/// </summary>
public sealed class GatewayControle : IControle
{
    /// <inheritdoc/>
    public string Nom => "gateway";

    /// <inheritdoc/>
    public string Resume => "les cinq endroits qui rendent un service joignable portent les mêmes clés";

    /// <summary>`[Required, Url] public string Machin { get; init; }`.</summary>
    private static readonly Regex Propriete = new(
        @"public\s+string\s+(\w+)\s*\{\s*get;\s*init;", RegexOptions.Compiled);

    /// <summary>`ServiceKeys.Machin => Machin,` dans le corps de `Resolve`.</summary>
    private static readonly Regex Branche = new(
        @"ServiceKeys\.(\w+)\s*=>", RegexOptions.Compiled);

    /// <summary>Les noms cités entre les crochets de `ServiceKeys.All`.</summary>
    private static readonly Regex Cite = new(
        @"\b([A-Z]\w+)\b", RegexOptions.Compiled);

    /// <inheritdoc/>
    public Verdict Executer()
    {
        // Les dossiers de la passerelle : leur absence LÈVE, elle ne se solde pas
        // par un « contrôle sauté » qui rend zéro.
        var api = Depot.Dossier("bff", "src", "HBA.Gateway.Api");
        var infrastructure = Depot.Dossier(
            "bff", "src", "HBA.Gateway.Infrastructure");

        var appsettings = Path.Combine(api, "appsettings.json");
        var options = Path.Combine(infrastructure, "Configuration", "ServicesOptions.cs");

        var fautes = new List<string>();

        if (!File.Exists(appsettings))
        {
            fautes.Add($"{Depot.Relatif(appsettings)} est introuvable : "
                       + "les clusters, les routes et les adresses ne peuvent PAS être lus.");
        }

        if (!File.Exists(options))
        {
            fautes.Add($"{Depot.Relatif(options)} est introuvable : "
                       + "les propriétés, les branches de `Resolve` et `ServiceKeys.All` "
                       + "ne peuvent PAS être lues.");
        }

        if (fautes.Count > 0)
        {
            return new Verdict(fautes, [], []);
        }

        // LE JSON EST LU EN TOLÉRANT COMMENTAIRES ET VIRGULES TRAÎNANTES.
        // `json.load` du script Python refusait les deux et mourait sur une trace ;
        // un fichier que la passerelle lit sans broncher doit se lire ici aussi.
        using var document = JsonDocument.Parse(
            File.ReadAllText(appsettings),
            new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });

        var racine = document.RootElement;

        // Les clés commençant par `_` sont des encadrés de lecture, pas des
        // adresses.
        var adresses = Cles(racine, "Services")
            .Where(c => !c.StartsWith('_'))
            .ToHashSet(StringComparer.Ordinal);

        var proxy = Sous(racine, "ReverseProxy");
        var clusters = Cles(proxy, "Clusters").ToHashSet(StringComparer.Ordinal);
        var routes = Routes(proxy);

        var source = File.ReadAllText(options);

        // `Resolve` d'abord : ses branches nomment les clés réellement résolues.
        var branches = new HashSet<string>(StringComparer.Ordinal);
        var corpsResolve = Entre(source, "public string? Resolve(", "_ => null");
        if (corpsResolve is null)
        {
            fautes.Add($"{Depot.Relatif(options)} : `public string? Resolve(` ou son "
                       + "`_ => null` est introuvable — les branches ne peuvent PAS être "
                       + "lues, et ce contrôle refuse de conclure sans elles.");
        }
        else
        {
            foreach (Match m in Branche.Matches(corpsResolve))
            {
                branches.Add(m.Groups[1].Value);
            }
        }

        // La liste `All`, entre ses crochets.
        var liste = new HashSet<string>(StringComparer.Ordinal);
        var corpsListe = Entre(
            source, "public static readonly IReadOnlyList<string> All", "];", "[");
        if (corpsListe is null)
        {
            fautes.Add($"{Depot.Relatif(options)} : `ServiceKeys.All` est introuvable — "
                       + "la quatrième des cinq places ne peut PAS être vérifiée.");
        }
        else
        {
            foreach (Match m in Cite.Matches(corpsListe))
            {
                liste.Add(m.Groups[1].Value);
            }
        }

        var proprietes = Propriete.Matches(source)
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var cluster in Triees(clusters))
        {
            if (!adresses.Contains(cluster))
            {
                fautes.Add($"cluster « {cluster} » : aucune adresse dans `Services:` — "
                           + "il se chargera SANS destination, donc 503.");
            }

            if (!proprietes.Contains(cluster))
            {
                fautes.Add($"cluster « {cluster} » : aucune propriété dans `ServicesOptions` — "
                           + $"la variable SERVICES__{cluster.ToUpperInvariant()} est ingérée "
                           + "et jetée.");
            }

            if (corpsResolve is not null && !branches.Contains(cluster))
            {
                fautes.Add($"cluster « {cluster} » : aucune branche dans `Resolve` — "
                           + "il rendra null.");
            }

            if (corpsListe is not null && !liste.Contains(cluster))
            {
                fautes.Add($"cluster « {cluster} » : absent de `ServiceKeys.All`.");
            }
        }

        foreach (var adresse in Triees(adresses.Except(clusters)))
        {
            fautes.Add($"adresse « Services:{adresse} » : aucun cluster ne l'emploie.");
        }

        var designes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (nom, cible) in routes)
        {
            designes.Add(cible);
            if (!clusters.Contains(cible))
            {
                fautes.Add($"route « {nom} » : désigne le cluster « {cible} », "
                           + "qui n'existe pas.");
            }
        }

        foreach (var cluster in Triees(clusters.Except(designes)))
        {
            fautes.Add($"cluster « {cluster} » : aucune route ne le désigne — destination "
                       + "que rien n'emprunte.");
        }

        var nonCouvert = new List<string>
        {
            "que le service RÉPONDE, et que le gabarit d'une route corresponde à un "
            + "`MapGroup` réel — un préfixe mal orthographié passe ici et rend 404",
        };

        return new Verdict(
            fautes,
            [$"{clusters.Count} cluster(s), {routes.Count} route(s), {adresses.Count} adresse(s)."],
            nonCouvert);
    }


    /// <summary>Les noms des propriétés d'un objet fils, ou rien s'il manque.</summary>
    private static IEnumerable<string> Cles(JsonElement parent, string nom)
    {
        var fils = Sous(parent, nom);
        if (fils.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        return fils.EnumerateObject().Select(p => p.Name).ToList();
    }

    /// <summary>Un objet fils, ou un élément indéfini s'il manque.</summary>
    private static JsonElement Sous(JsonElement parent, string nom)
        => parent.ValueKind == JsonValueKind.Object
           && parent.TryGetProperty(nom, out var fils)
            ? fils
            : default;

    /// <summary>Les routes : leur nom, et le cluster qu'elles désignent.</summary>
    private static IReadOnlyList<(string Nom, string Cible)> Routes(JsonElement proxy)
    {
        var trouvees = new List<(string, string)>();
        var fils = Sous(proxy, "Routes");
        if (fils.ValueKind != JsonValueKind.Object)
        {
            return trouvees;
        }

        foreach (var route in fils.EnumerateObject())
        {
            var cible = route.Value.ValueKind == JsonValueKind.Object
                        && route.Value.TryGetProperty("ClusterId", out var id)
                        && id.ValueKind == JsonValueKind.String
                ? id.GetString() ?? string.Empty
                : string.Empty;
            trouvees.Add((route.Name, cible));
        }

        return trouvees;
    }

    /// <summary>Le texte entre deux repères, ou <c>null</c> si l'un manque.</summary>
    /// <param name="source">Le fichier entier.</param>
    /// <param name="debut">Le repère d'ouverture.</param>
    /// <param name="fin">Le repère de fermeture, cherché APRÈS le premier.</param>
    /// <param name="apres">
    /// Repère intermédiaire facultatif : la lecture commence après lui, pas après
    /// <paramref name="debut"/> .
    /// </param>
    private static string? Entre(string source, string debut, string fin, string? apres = null)
    {
        var i = source.IndexOf(debut, StringComparison.Ordinal);
        if (i < 0)
        {
            return null;
        }

        if (apres is not null)
        {
            i = source.IndexOf(apres, i, StringComparison.Ordinal);
            if (i < 0)
            {
                return null;
            }
        }

        var j = source.IndexOf(fin, i, StringComparison.Ordinal);
        return j < 0 ? null : source[i..j];
    }

    /// <summary>Tri ordinal — celui de `sorted()` en Python.</summary>
    private static IEnumerable<string> Triees(IEnumerable<string> valeurs)
        => valeurs.OrderBy(x => x, StringComparer.Ordinal);
}
