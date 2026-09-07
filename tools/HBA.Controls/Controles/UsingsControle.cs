using System.Text.RegularExpressions;

namespace HBA.Controls.Controles;

/// <summary>Un type référencé sans <c>using</c> accessible — la classe d'erreur CS0246.</summary>
public sealed class UsingsControle : IControle
{
    /// <inheritdoc/>
    public string Nom => "usings";

    /// <inheritdoc/>
    public string Resume => "un type référencé sans `using` accessible — la classe CS0246";

    private static readonly Regex Declaration = new(
        @"\b(?:public|internal)\s+(?:sealed\s+|static\s+|abstract\s+|partial\s+|readonly\s+)*"
        + @"(?:class|record|struct|interface|enum)\s+(\w+)",
        RegexOptions.Compiled);

    private static readonly Regex DeclarationStatique = new(
        @"\b(?:public|internal)\s+static\s+(?:partial\s+)*class\s+(\w+)",
        RegexOptions.Compiled);

    private static readonly Regex EspaceDeNoms = new(
        @"^\s*namespace\s+([\w.]+)", RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex Utilise = new(
        @"^\s*using\s+(?:static\s+)?([\w.]+);", RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex Chaine = new(
        @"@?\$?""(?:\\.|[^""\\])*""", RegexOptions.Compiled);

    private static readonly Regex CommentaireDeLigne = new(
        @"//[^\n]*", RegexOptions.Compiled);

    /// <summary>Les quatre positions où un identifiant DOIT désigner un type.</summary>
    private static readonly Regex[] Positions =
    [
        new(@"\bnew\s+([A-Z]\w+)\s*[({]", RegexOptions.Compiled),
        new(@"[<,]\s*([A-Z]\w+)\s*[,>]", RegexOptions.Compiled),
        new(@"[(,]\s*([A-Z]\w+\??)\s+[a-z]\w*\s*[,)=]", RegexOptions.Compiled),
        new(@"\b(?:public|private|internal|protected)\s+(?:static\s+|readonly\s+)*"
            + @"([A-Z]\w+\??)\s+\w+\s*\{\s*get", RegexOptions.Compiled),
    ];

    /// <summary>Le cinquième motif : l'accès de membre `Nom.Membre`.</summary>
    private static readonly Regex AccesStatique = new(
        @"(?<![.\w])([A-Z]\w+)\s*\.", RegexOptions.Compiled);

    /// <summary>Voir l'encadré `Program` de l'en-tête de classe.</summary>
    private static readonly string[] Invisibles = ["Program"];

    /// <inheritdoc/>
    public Verdict Executer()
    {
        var fichiers = Fichiers();

        // 1. index type → { namespaces }
        var index = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        var indexStatique = new HashSet<string>(StringComparer.Ordinal);

        foreach (var chemin in fichiers)
        {
            var texte = File.ReadAllText(chemin);
            var espace = EspaceDeNoms.Match(texte);
            if (!espace.Success)
            {
                continue;
            }

            foreach (Match m in Declaration.Matches(texte))
            {
                if (!index.TryGetValue(m.Groups[1].Value, out var espaces))
                {
                    espaces = new SortedSet<string>(StringComparer.Ordinal);
                    index[m.Groups[1].Value] = espaces;
                }

                espaces.Add(espace.Groups[1].Value);
            }

            foreach (Match m in DeclarationStatique.Matches(texte))
            {
                indexStatique.Add(m.Groups[1].Value);
            }
        }

        // 2. détection, fichier par fichier
        var signalements = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var chemin in fichiers)
        {
            var texte = File.ReadAllText(chemin);
            var espace = EspaceDeNoms.Match(texte);
            if (!espace.Success)
            {
                continue;
            }

            var espaceFichier = espace.Groups[1].Value;
            var usings = Utilise.Matches(texte)
                .Select(m => m.Groups[1].Value)
                .ToHashSet(StringComparer.Ordinal);

            // Les chaînes sont VIDÉES, les commentaires de ligne retirés — dans cet
            // ordre : un `//` dans une URL doit disparaître avec sa chaîne, pas
            // emporter la fin de la ligne de code.
            var corps = CommentaireDeLigne.Replace(Chaine.Replace(texte, "\"\""), "");

            var declares = Declaration.Matches(texte)
                .Select(m => m.Groups[1].Value)
                .ToHashSet(StringComparer.Ordinal);

            var positions = new HashSet<string>(StringComparer.Ordinal);
            foreach (var motif in Positions)
            {
                foreach (Match m in motif.Matches(corps))
                {
                    // LE `?` EST RETIRÉ ICI : voir l'encadré de l'en-tête.
                    positions.Add(m.Groups[1].Value.TrimEnd('?'));
                }
            }

            var statiques = AccesStatique.Matches(corps)
                .Select(m => m.Groups[1].Value)
                .ToHashSet(StringComparer.Ordinal);

            foreach (var identifiant in positions.Union(statiques))
            {
                if (declares.Contains(identifiant)
                    || !index.TryGetValue(identifiant, out var possibles))
                {
                    continue;
                }

                if (Invisibles.Contains(identifiant))
                {
                    continue;
                }

                // POUR UN ACCÈS DE MEMBRE, IL FAUT QUE LE NOM SOIT UNE CLASSE
                // STATIQUE. Sinon `resultat.Error.Code` ferait remonter le type
                // `Error`, et le contrôle crierait sur du code valide.
                if (!positions.Contains(identifiant) && !indexStatique.Contains(identifiant))
                {
                    continue;
                }

                // ET IL FAUT REGARDER TOUS LES HOMONYMES, PAS SEULEMENT LE STATIQUE
                // : voir `PageRequest` dans l'en-tête.
                if (possibles.Any(n => Accessible(espaceFichier, usings, n)))
                {
                    continue;
                }

                var cites = string.Join(", ", possibles.Take(2));
                signalements.Add($"{Depot.Relatif(chemin)} : {identifiant} déclaré "
                                 + $"dans {cites}");
            }
        }

        var constats = new List<string>
        {
            $"{fichiers.Count} fichier(s) indexé(s), {index.Count} type(s) connu(s).",
            signalements.Count == 0
                ? "aucun type inaccessible détecté."
                : $"{signalements.Count} type(s) référencé(s) sans `using` accessible — "
                  + "constats, PAS des fautes : voir l'en-tête du contrôle.",
        };

        constats.AddRange(signalements);

        return new Verdict(
            [],
            constats,
            [
                "ce contrôle ne rend AUCUNE faute : ses signalements restent "
                + "heuristiques et portent encore des homonymes sur un dépôt qui "
                + "compile — le jour où le compte tombe à zéro, ils doivent passer en "
                + "fautes",
                "les fichiers sans instruction `namespace` : ni indexés, ni examinés — "
                + "c'est le cas des `Program` des API",
                "les dossiers `Migrations`, écartés du balayage",
            "le dossier `tools/`, écarté depuis qu'il a produit un faux "
            + "rapprochement : ses types ne sont visibles d'aucun projet "
            + "applicatif, mais leurs noms courts entraient en collision",
                "les alias `using X = Y;`, lus comme un `using` du namespace `X`",
                "les `global using` d'un autre projet, invisibles au fichier qui en "
                + "profite",
                "un type cité dans un commentaire de BLOC : les chaînes sont vidées, "
                + "les commentaires de bloc sont gardés",
            ]);
    }

    /// <summary>Les `.cs` du dépôt, hors `Migrations`.</summary>
    private static IReadOnlyList<string> Fichiers()
        => Depot.Fichiers(Depot.Racine, ".cs")
            .Where(f =>
            {
                var segments = Depot.Relatif(f).Split('/');
                return !segments.Contains("Migrations") && segments[0] != "tools";
            })
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// Un namespace est-il joignable depuis un fichier — par un `using`, ou parce
    /// qu'il ENGLOBE le namespace du fichier ?
    /// </summary>
    private static bool Accessible(
        string espaceFichier, IReadOnlySet<string> usings, string espaceType)
    {
        if (usings.Contains(espaceType))
        {
            return true;
        }

        var parties = espaceFichier.Split('.');
        for (var i = parties.Length; i > 0; i--)
        {
            if (string.Join('.', parties.Take(i)) == espaceType)
            {
                return true;
            }
        }

        return false;
    }
}
