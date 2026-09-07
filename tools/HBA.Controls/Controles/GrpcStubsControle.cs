using System.Text.RegularExpressions;

namespace HBA.Controls.Controles;

/// <summary>Clients gRPC : quelles méthodes ne contactent jamais le serveur ?</summary>
public sealed class GrpcStubsControle : IControle
{
    /// <inheritdoc/>
    public string Nom => "grpc-stubs";

    /// <inheritdoc/>
    public string Resume => "aucun client gRPC ne rend une réponse sans avoir appelé personne";

    // Corps qui trahissent un bouchon plutôt qu'un appel réseau.
    private static readonly string[] Bouchons =
        ["Task.FromResult", "NotImplementedException", "Array.Empty", "return null;"];

    private static readonly Regex ClasseCliente = new(
        @"class\s+(\w*GrpcClient)\b", RegexOptions.Compiled);

    // L'indentation à quatre espaces est le repère de « méthode de premier niveau
    // ».
    private static readonly Regex Methode = new(
        "\n    public (?:async )?(?:override )?Task<?[^\n(]*?>?\\s+(\\w+)\\s*\\(",
        RegexOptions.Compiled);

    // Ce dont un client a besoin pour parler à quelqu'un : un champ injecté…
    private static readonly Regex Champ = new(
        @"private\s+(?:readonly\s+)?[^;=(){}\n]+?\s+(\w+)\s*[;=]", RegexOptions.Compiled);

    // …ou un paramètre de constructeur primaire.
    private static readonly Regex ConstructeurPrimaire = new(
        @"class\s+\w*GrpcClient\s*\(([^)]*)\)", RegexOptions.Compiled);

    private static readonly Regex Mot = new(@"\w+", RegexOptions.Compiled);

    /// <inheritdoc/>
    public Verdict Executer()
    {
        var details = new List<string>();
        var clients = 0;
        var integraux = 0;
        var methodes = 0;

        var fichiers = SourceCsharp.Fichiers()
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        foreach (var fichier in fichiers)
        {
            var (classeNue, bouchonnees) = Analyser(fichier);
            if (classeNue is null && bouchonnees.Count == 0)
            {
                continue;
            }

            clients++;
            var relatif = Depot.Relatif(fichier);

            if (classeNue is not null)
            {
                integraux++;
                details.Add($"{relatif} : {classeNue} — BOUCHON INTÉGRAL : aucun champ "
                            + "client, aucun interlocuteur possible.");
            }

            foreach (var methode in bouchonnees)
            {
                methodes++;
                details.Add($"{relatif} : {methode} — ne contacte jamais le serveur");
            }
        }

        var constats = new List<string>
        {
            $"{clients} client(s) concerné(s), {integraux} bouchon(s) intégral(aux), "
            + $"{methodes} méthode(s) bouchonnée(s) — {fichiers.Count} fichier(s) .cs lus",
        };
        constats.AddRange(details);

        // FAUTES VIDES, DÉLIBÉRÉMENT : voir l'encadré de la classe.
        return new Verdict([], constats, NonCouvert());
    }

    /// <summary>
    /// Rend le nom de la classe quand elle n'a AUCUN collaborateur — le bouchon
    /// intégral — et la liste des méthodes bouchonnées.
    /// </summary>
    private static (string? ClasseNue, IReadOnlyList<string> Methodes) Analyser(string chemin)
    {
        string source;
        try
        {
            source = File.ReadAllText(chemin);
        }
        catch (IOException)
        {
            return (null, []);
        }

        var premiere = ClasseCliente.Match(source);
        if (!premiere.Success)
        {
            return (null, []);
        }

        // On ne regarde QUE la portion à partir de la première classe cliente : le
        // serveur, dans le même fichier, n'a évidemment pas de champ client.
        var portion = source[premiere.Index..];
        var noms = Collaborateurs(portion);
        var trouvees = Methode.Matches(portion);

        if (noms.Count == 0)
        {
            return (premiere.Groups[1].Value,
                trouvees.Select(m => m.Groups[1].Value).ToList());
        }

        var bouchonnees = new List<string>();
        foreach (Match m in trouvees)
        {
            var corps = CorpsDe(portion, m.Index);
            if (noms.Any(nom => corps.Contains(nom + ".", StringComparison.Ordinal)))
            {
                continue;
            }

            if (Bouchons.Any(b => corps.Contains(b, StringComparison.Ordinal)))
            {
                bouchonnees.Add(m.Groups[1].Value);
            }
        }

        return (null, bouchonnees);
    }

    /// <summary>Texte jusqu'à la prochaine méthode publique — approximation suffisante.</summary>
    private static string CorpsDe(string portion, int depart)
    {
        var suivant = portion.IndexOf("\n    public ", depart + 1, StringComparison.Ordinal);
        return suivant > 0 ? portion[depart..suivant] : portion[depart..];
    }

    /// <summary>
    /// Noms référençables depuis les méthodes : champs privés et paramètres de
    /// constructeur primaire.
    /// </summary>
    private static HashSet<string> Collaborateurs(string portion)
    {
        var noms = Champ.Matches(portion)
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

        var primaire = ConstructeurPrimaire.Match(portion);
        if (primaire.Success && primaire.Groups[1].Value.Trim().Length > 0)
        {
            foreach (var morceau in primaire.Groups[1].Value.Split(','))
            {
                var mots = Mot.Matches(morceau).Select(m => m.Value).ToList();
                if (mots.Count > 0)
                {
                    noms.Add(mots[^1]);
                }
            }
        }

        return noms;
    }

    private static List<string> NonCouvert()
        =>
        [
            "ce contrôle NE FAIT PAS ÉCHOUER la barrière : un bouchon peut être "
            + "délibéré, et il ne sait pas qui l'appelle. La liste est en constats, "
            + "le refus de démarrer se pose dans les installeurs de modules",
            "la JUSTESSE de l'appel : seule sa présence est vue. Une méthode qui "
            + "appelle le mauvais serveur passe",
            "une méthode qui délègue à une autre méthode du même client est "
            + "signalée à tort — faux positif assumé, préférable au silence",
            "les méthodes qui ne sont pas indentées de quatre espaces, ni écrites "
            + "`public [async] [override] Task…` : la lecture est textuelle, elle "
            + "ne comprend pas le C#",
            "les clients dont la classe ne s'appelle pas `*GrpcClient`, et tout "
            + "bouchon hors gRPC",
        ];
}
