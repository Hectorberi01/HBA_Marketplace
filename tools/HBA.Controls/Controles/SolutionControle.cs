using System.Text.RegularExpressions;

namespace HBA.Controls.Controles;

/// <summary>
/// La solution référence-t-elle encore ce qui existe ?
/// </summary>
/// <remarks>
/// ═══════════════════════════════════════════════════════════════════════════
/// ÉCRIT PARCE QUE `HBA.sln` A CASSÉ LE BUILD ET QU'AUCUN CONTRÔLE NE LE VOYAIT.
///
/// Le retrait de quatre squelettes avait supprimé vingt blocs `Project` et leurs
/// lignes de configuration — mais PAS leurs lignes d'imbrication, restées dans
/// `GlobalSection(NestedProjects)`. MSBuild s'arrête net :
///
///     HBA.sln(2849): error MSB5023: Un projet avec le GUID « {FBE95272-…} »
///     est répertorié comme étant imbriqué sous le projet « {0A6FC087-…} »,
///     mais il n'existe pas dans la solution.
///
/// Zéro fichier C# en cause, zéro erreur de compilation : les quinze contrôles
/// passaient tous, et rien ne se construisait.
///
/// CE QUI REND CE DÉFAUT PARTICULIER : LA VÉRIFICATION S'EST TROMPÉE COMME LE
/// RETRAIT. Le retrait cherchait les lignes d'imbrication avec `^\t\{` — UNE
/// tabulation. Elles en portent DEUX. La vérification écrite dans la foulée
/// utilisait le MÊME motif, a donc trouvé « zéro orphelin », et a confirmé une
/// suppression qui n'avait pas eu lieu. Un contrôle qui partage l'hypothèse
/// fausse du code qu'il contrôle ne contrôle rien.
///
/// D'OÙ LA RÈGLE TENUE ICI : aucune contrainte d'indentation nulle part
/// (`\s*` partout). Rien ne se décide à partir d'un motif de mise en forme.
///
/// CE QU'IL VÉRIFIE
///   1. tout GUID cité en tête de ligne — configuration ou imbrication — est
///      déclaré par un bloc `Project` ;
///   2. tout GUID PARENT d'une imbrication l'est aussi ;
///   3. `Project`/`EndProject` et `GlobalSection`/`EndGlobalSection`
///      s'équilibrent ;
///   4. chaque `.csproj` référencé existe sur le disque ;
///   5. aucun `.csproj` du dépôt n'est absent de la solution — l'oubli inverse,
///      qui ne casse pas le build mais laisse un projet jamais compilé.
///
/// CE QU'IL NE VÉRIFIE PAS : que la solution se construise. Il lit un fichier
/// texte ; il ne remplace pas `dotnet build`.
/// ═══════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed class SolutionControle : IControle
{
    /// <inheritdoc/>
    public string Nom => "solution";

    /// <inheritdoc/>
    public string Resume => "HBA.sln et le disque décrivent le même dépôt";

    private static readonly Regex Declaration = new(
        @"Project\(""\{[0-9A-Fa-f-]+\}""\)\s*=\s*""[^""]*"",\s*""([^""]*)"",\s*""\{([0-9A-Fa-f-]+)\}""",
        RegexOptions.Compiled);

    // Aucune contrainte d'indentation : c'est précisément l'hypothèse qui a échoué.
    private static readonly Regex AGauche = new(
        @"^\s*\{([0-9A-Fa-f-]+)\}\s*(?:\.|=)", RegexOptions.Compiled);

    private static readonly Regex Imbrication = new(
        @"^\s*\{([0-9A-Fa-f-]+)\}\s*=\s*\{([0-9A-Fa-f-]+)\}\s*$", RegexOptions.Compiled);

    /// <inheritdoc/>
    public Verdict Executer()
    {
        var solution = Depot.Chemin("HBA.sln");
        if (!File.Exists(solution))
        {
            return new Verdict(["HBA.sln introuvable"], [], []);
        }

        var texte = File.ReadAllText(solution);
        var lignes = texte.Split('\n');

        var declares = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Declaration.Matches(texte))
        {
            declares[m.Groups[2].Value.ToUpperInvariant()] = m.Groups[1].Value;
        }

        var fautes = new List<string>();

        for (var i = 0; i < lignes.Length; i++)
        {
            var numero = i + 1;

            var gauche = AGauche.Match(lignes[i]);
            if (gauche.Success && !declares.ContainsKey(gauche.Groups[1].Value.ToUpperInvariant()))
            {
                fautes.Add($"ligne {numero} — GUID {{{gauche.Groups[1].Value}}} cité "
                           + "mais déclaré par aucun bloc Project");
            }

            var nid = Imbrication.Match(lignes[i]);
            if (nid.Success && !declares.ContainsKey(nid.Groups[2].Value.ToUpperInvariant()))
            {
                fautes.Add($"ligne {numero} — imbriqué sous {{{nid.Groups[2].Value}}}, "
                           + "qui n'existe pas dans la solution");
            }
        }

        var ouverts = Regex.Matches(texte, @"^Project\(""", RegexOptions.Multiline).Count;
        var fermes = Regex.Matches(texte, @"^EndProject\s*$", RegexOptions.Multiline).Count;
        if (ouverts != fermes)
        {
            fautes.Add($"{ouverts} « Project » pour {fermes} « EndProject »");
        }

        if (Compter(texte, "GlobalSection(") != Compter(texte, "EndGlobalSection"))
        {
            fautes.Add("GlobalSection et EndGlobalSection ne s'équilibrent pas");
        }

        // 4 et 5 : la solution et le disque doivent décrire le même dépôt.
        var surDisque = Depot.Fichiers(Depot.Racine, ".csproj")
            .Select(Depot.Relatif)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var references = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (_, chemin) in declares)
        {
            if (!chemin.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            {
                continue; // dossier de solution
            }

            var relatif = chemin.Replace('\\', '/');
            references.Add(relatif);

            if (!File.Exists(Depot.Chemin(relatif.Replace('/', Path.DirectorySeparatorChar))))
            {
                fautes.Add("référencé par la solution mais absent du disque : " + chemin);
            }
        }

        // ═════════════════════════════════════════════════════════════════
        // UN PROJET ABSENT DE LA SOLUTION N'EST PAS FORCÉMENT UN PROJET PERDU.
        //
        // Le contrôle Python listait les orphelins pour information, sans jamais
        // faire échouer, alors que sa documentation annonçait qu'il vérifiait
        // « l'oubli inverse ». Porté tel quel en faute, il rendait SIX faux
        // positifs : les contrats gRPC de `shared/contracts` ne sont pas dans le
        // `.sln`, mais six projets de services les référencent — ils sont donc
        // compilés, transitivement, à chaque build.
        //
        // La distinction qui compte n'est pas « déclaré dans le .sln » mais
        // « atteignable par la compilation ». Un projet que PERSONNE ne
        // référence et que le .sln ignore n'est compilé par rien : son code
        // pourrit sans qu'aucune erreur ne le dise.
        // ═════════════════════════════════════════════════════════════════
        var reference = ProjetsReferences();
        var orphelins = surDisque.Except(references)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        var perdus = orphelins.Where(o => !reference.Contains(o)).ToList();
        var transitifs = orphelins.Count - perdus.Count;

        foreach (var chemin in perdus)
        {
            fautes.Add("absent de la solution ET référencé par aucun projet — "
                       + "compilé par rien : " + chemin);
        }

        var constats = new List<string>
        {
            $"{declares.Count} projet(s) et dossier(s) déclarés, "
            + $"{surDisque.Count} .csproj sur le disque",
        };

        if (transitifs > 0)
        {
            constats.Add($"{transitifs} projet(s) hors solution mais référencés par "
                         + "un projet de la solution — compilés transitivement");
        }

        return new Verdict(
            fautes,
            constats,
            ["que la solution se construise — ce contrôle lit un fichier texte, "
             + "il ne remplace pas `dotnet build`"]);
    }

    /// <summary>
    /// Tout projet cité par un <c>ProjectReference</c>, en chemin relatif à la
    /// racine du dépôt.
    /// </summary>
    private static HashSet<string> ProjetsReferences()
    {
        var cites = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var csproj in Projets.Tous())
        {
            foreach (var (_, absolu) in Projets.References(csproj))
            {
                cites.Add(Depot.Relatif(absolu));
            }
        }

        return cites;
    }

    /// <summary>Occurrences d'une sous-chaîne littérale.</summary>
    /// <remarks>
    /// Une expression rationnelle ferait l'affaire, mais `GlobalSection(`
    /// contient une parenthèse : il faudrait l'échapper, et un échappement
    /// oublié transformerait le compte en groupe de capture vide qui compte
    /// tout autre chose sans rien signaler.
    /// </remarks>
    private static int Compter(string texte, string aiguille)
    {
        var total = 0;
        var index = 0;
        while ((index = texte.IndexOf(aiguille, index, StringComparison.Ordinal)) >= 0)
        {
            total++;
            index += aiguille.Length;
        }

        return total;
    }
}
