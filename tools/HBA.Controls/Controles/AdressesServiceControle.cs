using System.Text.RegularExpressions;

namespace HBA.Controls.Controles;

/// <summary>Un service de <c>docker-compose.dev.yml</c>, tel qu'un contrôle en a besoin.</summary>
/// <param name="Nom">Le nom du service dans la section <c>services:</c>.</param>
/// <param name="Environnement">
/// Ses variables d'environnement, ancre partagée FUSIONNÉE. Sans la fusion, les
/// clés de <c> x-dev-auth</c> manqueraient à vingt et un services.
/// </param>
/// <param name="Build">Les champs de son <c>build:</c>, ou <c>null</c> s'il n'en a pas.</param>
internal sealed record ServiceCompose(
    string Nom,
    IReadOnlyDictionary<string, string> Environnement,
    IReadOnlyDictionary<string, string>? Build);

/// <summary>La lecture de <c>docker-compose.dev.yml</c> dont les contrôles ont besoin.</summary>
internal static class ComposeDev
{
    private static readonly Regex DebutService = new(@"^  ([A-Za-z0-9_.\-]+):\s*$");
    private static readonly Regex Ancre = new(@"^([A-Za-z0-9_.\-]+):\s*&([A-Za-z0-9_.\-]+)\s*$");
    private static readonly Regex Fusion = new(@"^\s*<<:\s*\*([A-Za-z0-9_.\-]+)\s*$");
    private static readonly Regex Paire = new(@"^\s*([A-Za-z0-9_.\-]+):\s*(.*?)\s*$");

    /// <summary>Le chemin absolu du compose de développement.</summary>
    public static string Fichier => Depot.Chemin("docker-compose.dev.yml");

    /// <summary>Les services du compose, dans l'ordre du fichier.</summary>
    /// <exception cref="InvalidOperationException">
    /// Si la section <c> services:</c> est introuvable ou vide — un contrôle qui ne
    /// peut rien regarder doit s'arrêter, pas rendre zéro.
    /// </exception>
    public static IReadOnlyList<ServiceCompose> Services()
    {
        var lignes = File.ReadAllText(Fichier).Split('\n');
        var ancres = Ancres(lignes);

        var debut = Array.FindIndex(lignes, l => l.TrimEnd() == "services:");
        if (debut < 0)
        {
            throw new InvalidOperationException(
                "docker-compose.dev.yml n'a pas de section `services:` — la lecture "
                + "textuelle ne reconnaît plus ce fichier, et rien ne serait contrôlé.");
        }

        var fin = lignes.Length;
        for (var i = debut + 1; i < lignes.Length; i++)
        {
            if (lignes[i].Trim().Length > 0
                && !lignes[i].StartsWith(' ')
                && !lignes[i].StartsWith('\t'))
            {
                fin = i;
                break;
            }
        }

        var services = new List<ServiceCompose>();
        string? nom = null;
        var environnement = new Dictionary<string, string>(StringComparer.Ordinal);
        var build = new Dictionary<string, string>(StringComparer.Ordinal);
        var porteBuild = false;
        var dansEnvironnement = false;
        var dansBuild = false;

        void Fermer()
        {
            if (nom is not null)
            {
                IReadOnlyDictionary<string, string>? champs = porteBuild
                    ? new Dictionary<string, string>(build, StringComparer.Ordinal)
                    : null;

                services.Add(new ServiceCompose(
                    nom,
                    new Dictionary<string, string>(environnement, StringComparer.Ordinal),
                    champs));
            }

            nom = null;
            environnement = new Dictionary<string, string>(StringComparer.Ordinal);
            build = new Dictionary<string, string>(StringComparer.Ordinal);
            porteBuild = false;
            dansEnvironnement = false;
            dansBuild = false;
        }

        for (var i = debut + 1; i < fin; i++)
        {
            var ligne = lignes[i];
            if (ligne.Trim().Length == 0 || ligne.TrimStart().StartsWith('#'))
            {
                continue;
            }

            var entete = DebutService.Match(ligne);
            if (entete.Success)
            {
                Fermer();
                nom = entete.Groups[1].Value;
                continue;
            }

            if (nom is null)
            {
                continue;
            }

            var indentation = ligne.Length - ligne.TrimStart().Length;

            if (indentation == 4)
            {
                dansEnvironnement = ligne.TrimEnd() == "    environment:";
                dansBuild = ligne.TrimEnd() == "    build:";
                porteBuild = porteBuild || dansBuild;
                continue;
            }

            if (indentation < 6)
            {
                continue;
            }

            if (dansEnvironnement)
            {
                var fusion = Fusion.Match(ligne);
                if (fusion.Success)
                {
                    // L'ANCRE PARTAGÉE PORTE CE QUE VINGT ET UN SERVICES ATTENDENT.
                    // La sauter rendrait toutes ses clés absentes, partout.
                    if (ancres.TryGetValue(fusion.Groups[1].Value, out var partagees))
                    {
                        foreach (var (cle, valeur) in partagees)
                        {
                            environnement[cle] = valeur;
                        }
                    }

                    continue;
                }

                var paire = Paire.Match(ligne);
                if (paire.Success)
                {
                    environnement[paire.Groups[1].Value] = Nettoyer(paire.Groups[2].Value);
                }

                continue;
            }

            if (dansBuild)
            {
                var paire = Paire.Match(ligne);
                if (paire.Success)
                {
                    build[paire.Groups[1].Value] = Nettoyer(paire.Groups[2].Value);
                }
            }
        }

        Fermer();

        if (services.Count == 0)
        {
            throw new InvalidOperationException(
                "aucun service lu dans docker-compose.dev.yml : le découpage a échoué, "
                + "et un contrôle qui rendrait zéro ici mentirait.");
        }

        return services;
    }

    /// <summary>Le dossier du <c>build.dockerfile</c> d'un service, s'il en a un.</summary>
    public static string? DossierDeBuild(ServiceCompose service)
    {
        var dockerfile = Dockerfile(service);
        if (dockerfile is null)
        {
            return null;
        }

        var relatif = Path.GetDirectoryName(dockerfile.Replace('/', Path.DirectorySeparatorChar));
        return Depot.Chemin(relatif ?? string.Empty);
    }

    /// <summary>Le <c>build.dockerfile</c> d'un service, tel qu'écrit.</summary>
    public static string? Dockerfile(ServiceCompose service)
    {
        var dockerfile = service.Build?.GetValueOrDefault("dockerfile");
        return string.IsNullOrEmpty(dockerfile) ? null : dockerfile;
    }

    /// <summary>Les ancres de premier niveau, sous forme de tables de clés.</summary>
    private static Dictionary<string, Dictionary<string, string>> Ancres(string[] lignes)
    {
        var ancres = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);

        for (var i = 0; i < lignes.Length; i++)
        {
            var entete = Ancre.Match(lignes[i]);
            if (!entete.Success)
            {
                continue;
            }

            var contenu = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var j = i + 1; j < lignes.Length; j++)
            {
                var ligne = lignes[j];
                if (ligne.Trim().Length == 0 || ligne.TrimStart().StartsWith('#'))
                {
                    continue;
                }

                if (!ligne.StartsWith(' '))
                {
                    break;
                }

                if (ligne.Length - ligne.TrimStart().Length != 2)
                {
                    continue;
                }

                var paire = Paire.Match(ligne);
                if (paire.Success)
                {
                    contenu[paire.Groups[1].Value] = Nettoyer(paire.Groups[2].Value);
                }
            }

            ancres[entete.Groups[2].Value] = contenu;
        }

        return ancres;
    }

    /// <summary>Retire les guillemets extérieurs d'une valeur scalaire.</summary>
    private static string Nettoyer(string valeur)
        => valeur.Trim().Trim('"', '\'');
}

/// <summary>
/// Toute adresse <c> Services:&lt;X&gt;</c> qu'un client gRPC réclame est déclarée
/// là où l'hôte qui l'emploie démarre.
/// </summary>
public sealed class AdressesServiceControle : IControle
{
    /// <inheritdoc/>
    public string Nom => "adresses-service";

    /// <inheritdoc/>
    public string Resume =>
        "toute adresse Services:<X> réclamée par un client gRPC est déclarée là où l'hôte démarre";

    // `AddXGrpcClient(…) { … }` : la signature, puis le corps jusqu'à l'accolade
    // fermante posée à quatre espaces.
    //
    // CES EXTENSIONS ONT CHANGÉ D'ADRESSE. Elles vivaient dans `shared/contracts`
    // du temps des enveloppes `*.Contracts.Grpc` partagées ; le lot gRPC les a
    // descendues dans `Infrastructure/Grpc/Clients/` de CHAQUE service appelant.
    // Le contrôle a alors rendu une faute — « aucune extension trouvée » — au lieu
    // d'un vert vide, et c'était le bon comportement : c'est la règle de `Depot`
    // tenue jusqu'au bout. Un balayage qui ne trouve rien doit le CRIER.
    private static readonly Regex Enregistrement = new(
        @"Add(\w+)GrpcClient\s*\([^)]*\)\s*\{(.*?)\n    \}",
        RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex Cle = new(
        @"configuration\[""Services:(\w+)""\]", RegexOptions.Compiled);

    private static readonly Regex Appel = new(
        @"(Add\w+GrpcClient)\s*\(", RegexOptions.Compiled);

    private static readonly Regex Posee = new(
        @"""Services(?:__|:)(\w+)""", RegexOptions.Compiled);

    private static readonly string[] RacinesTests = ["tests", "bff"];

    /// <inheritdoc/>
    public Verdict Executer()
    {
        var fautes = new List<string>();
        var constats = new List<string>();
        var nonCouvert = new List<string>
        {
            "que l'adresse posée MÈNE quelque part : ces fabriques visent délibérément "
            + "des ports fermés. Un client que le code se met à APPELER a besoin, en plus "
            + "de son adresse, d'un double — et aucun contrôle statique ne le voit",
            "la VALIDITÉ YAML de docker-compose.dev.yml : la lecture est textuelle, ligne "
            + "à ligne (aucune dépendance de paquet). Un compose cassé serait lu sans "
            + "broncher, la forme liste de `environment:` serait lue comme vide, et ni "
            + "`extends:` ni les fichiers de surcharge ne sont suivis. "
            + "`docker compose config -q` est la vraie réponse",
            "les clés sont trouvées par expression régulière : une adresse construite "
            + "dynamiquement, ou une extension d'enregistrement dont l'accolade fermante "
            + "n'est pas posée à quatre espaces, resterait invisible",
            "les extensions posées ailleurs que sous `services/` et `bff/` : le "
            + "périmètre suit les APPELANTS, et un enregistrement écrit dans `shared/` "
            + "ne serait plus vu",
        };

        var table = ClientsConnus();
        if (table.Count == 0)
        {
            fautes.Add(
                "aucune extension `Add<X>GrpcClient` trouvée sous services/ ni bff/ : "
                + "le contrôle serait sans objet, et rendrait vert sans avoir rien comparé.");
            return new Verdict(fautes, constats, nonCouvert);
        }

        // ── 1. Le compose, service par service ───────────────────────────────
        var employees = new SortedSet<string>(StringComparer.Ordinal);
        var examines = 0;

        foreach (var service in ComposeDev.Services())
        {
            var dossier = ComposeDev.DossierDeBuild(service);
            if (dossier is null || !Directory.Exists(dossier))
            {
                continue;
            }

            var programmes = Programmes(dossier);
            if (programmes.Count == 0)
            {
                continue;
            }

            examines++;

            var environnement = service.Environnement.Keys
                .Select(c => c.ToUpperInvariant())
                .ToHashSet(StringComparer.Ordinal);

            var exigees = ExigeesPar(programmes, table);
            employees.UnionWith(exigees);

            var dockerfile = ComposeDev.Dockerfile(service) ?? "(sans dockerfile)";
            foreach (var clef in exigees)
            {
                var variable = "SERVICES__" + clef.ToUpperInvariant();
                if (environnement.Contains(variable))
                {
                    continue;
                }

                fautes.Add(
                    $"{service.Nom} : {variable} absent de docker-compose.dev.yml — le "
                    + $"service l'exige AU DÉMARRAGE, il ne partira pas ({dockerfile})");
            }
        }

        // ── 2. La fabrique des tests d'autorisation ─────────────────────────
        var fabrique = Path.Combine(
            Depot.Dossier("tests", "Shared"), "AuthorizationTestFactory.cs");

        if (!File.Exists(fabrique))
        {
            fautes.Add(
                "tests/Shared/AuthorizationTestFactory.cs est absent : le troisième endroit "
                + "où ces adresses doivent vivre n'a pas pu être vérifié.");
        }
        else
        {
            var texte = File.ReadAllText(fabrique);
            foreach (var clef in table.Values.Distinct().OrderBy(c => c, StringComparer.Ordinal))
            {
                if (texte.Contains($@"""Services__{clef}""", StringComparison.Ordinal))
                {
                    continue;
                }

                fautes.Add(
                    $"tests/Shared/AuthorizationTestFactory.cs : Services__{clef} absent — "
                    + "l'hôte lèvera À LA CONSTRUCTION dès qu'un service sous test "
                    + "d'autorisation emploiera ce client");
            }
        }

        // ── 4. Les quatre autres fabriques, chacune comparée à SON hôte ──────
        var fabriquesExaminees = 0;

        foreach (var projet in ProjetsDeTest(fabrique, fautes, nonCouvert))
        {
            if (projet.Hotes.Count == 0)
            {
                // Une suite qui pose des adresses sans démarrer d'API : rien à
                // comparer.
                continue;
            }

            fabriquesExaminees++;

            var exigees = ExigeesPar(projet.Hotes, table);
            foreach (var clef in exigees.Where(c => !projet.Posees.Contains(c)))
            {
                fautes.Add(
                    $"{Depot.Relatif(projet.Csproj)} : Services__{clef} absent — l'hôte LÈVE "
                    + "À LA CONSTRUCTION, donc toute la suite tombe avant la première "
                    + $"assertion ; réclamé par {Depot.Relatif(projet.Hotes[0])}");
            }
        }

        constats.Add(
            $"{examines} service(s) et {fabriquesExaminees} fabrique(s) de test examinés, "
            + $"{table.Count} extension(s) d'enregistrement au catalogue, "
            + $"{fautes.Count} adresse(s) manquante(s).");

        return new Verdict(fautes, constats, nonCouvert);
    }

    /// <summary>
    /// Chaque extension d'enregistrement, et la clé de configuration qu'elle exige.
    /// </summary>
    private static Dictionary<string, string> ClientsConnus()
    {
        var table = new Dictionary<string, string>(StringComparer.Ordinal);

        // ON BALAIE LES APPELANTS, PAS UN DOSSIER DE CONTRATS. Chaque service porte
        // desormais SES enregistrements de clients ; la passerelle aussi.
        var racines = new[] { Depot.Dossier("services"), Depot.Dossier("bff") };

        foreach (var fichier in racines.SelectMany(r => Depot.Fichiers(r, ".cs")))
        {
            var contenu = File.ReadAllText(fichier);
            foreach (Match methode in Enregistrement.Matches(contenu))
            {
                var cle = Cle.Match(methode.Groups[2].Value);
                if (cle.Success)
                {
                    table["Add" + methode.Groups[1].Value + "GrpcClient"] = cle.Groups[1].Value;
                }
            }
        }

        return table;
    }

    /// <summary>Tous les `Program.cs` sous un dossier — deux groupes sont co-hébergés.</summary>
    private static List<string> Programmes(string dossier)
        => Depot.Fichiers(dossier)
            .Where(f => Path.GetFileName(f) == "Program.cs")
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

    /// <summary>Les clés de configuration que ces hôtes réclament au démarrage.</summary>
    private static SortedSet<string> ExigeesPar(
        IEnumerable<string> hotes, Dictionary<string, string> table)
    {
        var exigees = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var hote in hotes)
        {
            foreach (Match appel in Appel.Matches(File.ReadAllText(hote)))
            {
                if (table.TryGetValue(appel.Groups[1].Value, out var clef))
                {
                    exigees.Add(clef);
                }
            }
        }

        return exigees;
    }

    /// <summary>Un projet de test qui pose des adresses, et les hôtes qu'il démarre.</summary>
    private sealed record ProjetDeTest(string Csproj, HashSet<string> Posees, List<string> Hotes);

    /// <summary>Les projets de test qui posent des adresses de service, et leur hôte.</summary>
    private static List<ProjetDeTest> ProjetsDeTest(
        string fabrique, List<string> fautes, List<string> nonCouvert)
    {
        var parDossier = new Dictionary<string, (List<string> Csproj, List<string> Sources)>(
            StringComparer.Ordinal);

        foreach (var racine in RacinesTests)
        {
            foreach (var fichier in Depot.Fichiers(Depot.Dossier(racine)))
            {
                var dossier = Path.GetDirectoryName(fichier)!;
                if (!parDossier.TryGetValue(dossier, out var contenu))
                {
                    contenu = (new List<string>(), new List<string>());
                    parDossier[dossier] = contenu;
                }

                if (fichier.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                {
                    contenu.Csproj.Add(fichier);
                }
                else if (fichier.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                {
                    contenu.Sources.Add(fichier);
                }
            }
        }

        var projets = new List<ProjetDeTest>();

        foreach (var (dossier, contenu) in parDossier.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            if (contenu.Csproj.Count == 0)
            {
                continue;
            }

            var posees = new HashSet<string>(StringComparer.Ordinal);
            foreach (var source in contenu.Sources)
            {
                // La fabrique d'autorisation est vérifiée à part, contre TOUT le
                // catalogue : la compter ici masquerait ses propres manques.
                if (string.Equals(source, fabrique, StringComparison.Ordinal))
                {
                    continue;
                }

                foreach (Match m in Posee.Matches(File.ReadAllText(source)))
                {
                    posees.Add(m.Groups[1].Value);
                }
            }

            if (posees.Count == 0)
            {
                continue;
            }

            var csproj = contenu.Csproj.OrderBy(c => c, StringComparer.Ordinal).First();

            IReadOnlyList<(string Brut, string Absolu)> references;
            try
            {
                references = Projets.References(csproj).ToList();
            }
            catch (IOException erreur)
            {
                fautes.Add($"{Depot.Relatif(csproj)} : illisible — {erreur.Message}");
                continue;
            }

            var hotes = new List<string>();
            foreach (var (brut, absolu) in references)
            {
                if (!brut.EndsWith(".Api.csproj", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var dossierHote = Path.GetDirectoryName(absolu)!;
                if (!Directory.Exists(dossierHote))
                {
                    // La référence morte est l'affaire du contrôle des références ;
                    // ici on dit seulement que cet hôte n'a PAS été comparé.
                    nonCouvert.Add(
                        $"{Depot.Relatif(csproj)} référence {brut}, dont le dossier n'existe "
                        + "pas : les adresses de cet hôte n'ont pas été vérifiées");
                    continue;
                }

                hotes.AddRange(Programmes(dossierHote));
            }

            projets.Add(new ProjetDeTest(csproj, posees, hotes));
        }

        return projets;
    }
}
