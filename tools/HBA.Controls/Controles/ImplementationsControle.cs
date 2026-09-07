using System.Text.RegularExpressions;

namespace HBA.Controls.Controles;

/// <summary>Une interface qui change laisse ses doubles de test derrière elle.</summary>
public sealed class ImplementationsControle : IControle
{
    /// <inheritdoc/>
    public string Nom => "implementations";

    /// <inheritdoc/>
    public string Resume => "toute classe tient le nom et l'arité des méthodes de ses interfaces";

    private static readonly Regex DeclarationInterface = new(
        @"^\s*(?:public|internal|private|protected)?\s*(?:partial\s+)?interface\s+(I\w+)",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex DeclarationClasse = new(
        @"^\s*(?:public|internal|private|protected)?\s*"
        + @"(?:sealed\s+|abstract\s+|static\s+|partial\s+)*"
        + @"class\s+(\w+)\s*(?:<[^>]*>)?\s*:\s*([^\{\r\n]+)",
        RegexOptions.Compiled | RegexOptions.Multiline);

    // Une déclaration de méthode d'interface : se termine par « ; », pas de corps.
    private static readonly Regex Membre = new(
        @"^\s*(?!//|/\*|\*)([\w<>\[\],\?\. ]+?)\s+(\w+)\s*\(([^;{]*)\)\s*;",
        RegexOptions.Compiled | RegexOptions.Multiline);

    // Une méthode de classe : corps en accolade OU en expression.
    private static readonly Regex MethodeClasse = new(
        @"\b(\w+)\s*(?:<[^>()]*>)?\s*\(([^)]*)\)\s*(?:=>|\{)",
        RegexOptions.Compiled);

    // UNE MÉTHODE PEUT TENIR LE CONTRAT SANS AVOIR DE CORPS.
    private static readonly Regex MembreSansCorps = new(
        @"^\s*(?:public|protected|internal|private)?\s*(?:public|protected|internal|private)?\s*"
        + @"(?:abstract|extern|partial)\s+[^;{()]*?\b(\w+)\s*(?:<[^>()]*>)?\s*\(([^;{]*)\)\s*;",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly string[] AccesseursDePropriete = ["get", "set", "init"];

    /// <summary>Compte les paramètres, en ignorant les virgules imbriquées.</summary>
    private static int Arite(string parametres)
    {
        var texte = parametres.Trim();
        if (texte.Length == 0)
        {
            return 0;
        }

        var profondeur = 0;
        var compte = 1;
        foreach (var caractere in texte)
        {
            if (caractere is '<' or '(' or '[')
            {
                profondeur++;
            }
            else if (caractere is '>' or ')' or ']')
            {
                profondeur--;
            }
            else if (caractere == ',' && profondeur == 0)
            {
                compte++;
            }
        }

        return compte;
    }

    /// <summary>Le bloc { … } qui suit `depart`, accolades équilibrées.</summary>
    private static string Corps(string source, int depart)
    {
        var ouverture = source.IndexOf('{', depart);
        if (ouverture == -1)
        {
            return string.Empty;
        }

        var profondeur = 0;
        for (var i = ouverture; i < source.Length; i++)
        {
            if (source[i] == '{')
            {
                profondeur++;
            }
            else if (source[i] == '}')
            {
                profondeur--;
                if (profondeur == 0)
                {
                    return source[ouverture..i];
                }
            }
        }

        return source[ouverture..];
    }

    /// <inheritdoc/>
    public Verdict Executer()
    {
        // LES COMMENTAIRES SONT RETIRÉS AVANT TOUTE LECTURE.
        var sources = new List<(string Chemin, string Source)>();
        foreach (var chemin in Depot.Fichiers(Depot.Racine, ".cs"))
        {
            if (chemin.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            sources.Add((chemin, SourceCsharp.SansCommentaires(File.ReadAllText(chemin))));
        }

        // ── Le contrat de chaque interface : nom -> {(methode, arite)}
        var contrats = new Dictionary<string, HashSet<(string Methode, int Arite)>>(
            StringComparer.Ordinal);

        foreach (var (_, source) in sources)
        {
            foreach (Match declaration in DeclarationInterface.Matches(source))
            {
                var bloc = Corps(source, declaration.Index + declaration.Length);
                var membres = new HashSet<(string Methode, int Arite)>();
                foreach (Match m in Membre.Matches(bloc))
                {
                    // Un `get;`/`set;` de propriété n'est pas une méthode.
                    if (AccesseursDePropriete.Contains(m.Groups[2].Value))
                    {
                        continue;
                    }

                    membres.Add((m.Groups[2].Value, Arite(m.Groups[3].Value)));
                }

                if (membres.Count == 0)
                {
                    continue;
                }

                if (!contrats.TryGetValue(declaration.Groups[1].Value, out var deja))
                {
                    deja = [];
                    contrats[declaration.Groups[1].Value] = deja;
                }

                deja.UnionWith(membres);
            }
        }

        var fautes = new List<string>();
        var incertains = new List<string>();
        var classesExaminees = 0;

        foreach (var (chemin, source) in sources)
        {
            foreach (Match declaration in DeclarationClasse.Matches(source))
            {
                var classe = declaration.Groups[1].Value;
                var heritages = declaration.Groups[2].Value
                    .Split(',')
                    .Select(h => h.Trim().Split('<')[0])
                    .ToList();

                var attendus = new HashSet<(string Methode, int Arite)>();
                var interfaces = new List<string>();
                foreach (var h in heritages)
                {
                    if (contrats.TryGetValue(h, out var contrat))
                    {
                        attendus.UnionWith(contrat);
                        interfaces.Add(h);
                    }
                }

                if (attendus.Count == 0)
                {
                    continue;
                }

                classesExaminees++;

                var bloc = Corps(source, declaration.Index + declaration.Length);
                var presentes = new HashSet<(string Methode, int Arite)>();
                foreach (Match m in MethodeClasse.Matches(bloc))
                {
                    presentes.Add((m.Groups[1].Value, Arite(m.Groups[2].Value)));
                }

                foreach (Match m in MembreSansCorps.Matches(bloc))
                {
                    presentes.Add((m.Groups[1].Value, Arite(m.Groups[2].Value)));
                }

                var manquants = attendus
                    .Where(a => !presentes.Contains(a))
                    .OrderBy(a => a.Methode, StringComparer.Ordinal)
                    .ThenBy(a => a.Arite)
                    .ToList();

                if (manquants.Count == 0)
                {
                    continue;
                }

                // Une base non-interface peut tenir le contrat : on ne tranche pas.
                var avecBase = heritages.Any(
                    h => !contrats.ContainsKey(h) && !h.StartsWith('I'));
                var relatif = Depot.Relatif(chemin);

                foreach (var (methode, n) in manquants)
                {
                    if (avecBase)
                    {
                        incertains.Add(
                            $"peut-être tenu par une classe de base — non tranché : "
                            + $"{relatif} : {classe}.{methode}/{n}");
                        continue;
                    }

                    fautes.Add(
                        $"{relatif} : « {classe} » n'implémente pas {methode}/{n}, exigée "
                        + $"par {string.Join(", ", interfaces)}. Un paramètre a probablement "
                        + "été ajouté ou retiré côté interface.");
                }
            }
        }

        var constats = new List<string>
        {
            $"{sources.Count} fichier(s) .cs lu(s), {contrats.Count} interface(s) au contrat "
            + $"relevé, {classesExaminees} classe(s) implémentant une interface du dépôt.",
        };

        // ON N'EN MONTRE QUE VINGT, COMME LE SCRIPT D'ORIGINE — ET ON DIT COMBIEN
        // RESTENT. Une liste tronquée en silence laisserait croire que
        // l'incertitude est bornée alors qu'elle ne l'est pas.
        constats.AddRange(incertains.Take(20));
        if (incertains.Count > 20)
        {
            constats.Add($"… et {incertains.Count - 20} autre(s) cas non tranché(s).");
        }

        return new Verdict(
            fautes,
            constats,
            [
                "les propriétés, indexeurs et événements — seules les méthodes sont relevées",
                "les types des paramètres : seule leur QUANTITÉ est comparée, un type "
                + "changé à arité constante passe",
                "les classes partielles dont les méthodes vivent dans un autre fichier",
                "les contrats tenus par une classe de base : rendus en constat, jamais "
                + "en faute, le contrôle ne sait pas lire la base",
                "les interfaces homonymes de deux espaces de noms différents, rapprochées "
                + "par leur seul nom court",
            ]);
    }
}
