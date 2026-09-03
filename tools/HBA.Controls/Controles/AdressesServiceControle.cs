using System.Text.RegularExpressions;

namespace HBA.Controls.Controles;

/// <summary>Un service de <c>docker-compose.dev.yml</c>, tel qu'un contrôle en a besoin.</summary>
/// <param name="Nom">Le nom du service dans la section <c>services:</c>.</param>
/// <param name="Environnement">
/// Ses variables d'environnement, ancre partagée FUSIONNÉE. Sans la fusion, les
/// clés de <c>x-dev-auth</c> manqueraient à vingt et un services.
/// </param>
/// <param name="Build">Les champs de son <c>build:</c>, ou <c>null</c> s'il n'en a pas.</param>
internal sealed record ServiceCompose(
    string Nom,
    IReadOnlyDictionary<string, string> Environnement,
    IReadOnlyDictionary<string, string>? Build);

/// <summary>
/// La lecture de <c>docker-compose.dev.yml</c> dont les contrôles ont besoin.
/// </summary>
/// <remarks>
/// ═══════════════════════════════════════════════════════════════════════════
/// CE N'EST PAS UN ANALYSEUR YAML, ET C'EST UN RECUL ASSUMÉ.
///
/// Les contrôles Python chargeaient le compose avec PyYAML. Ils s'AUTORISAIENT
/// alors à ne rien vérifier quand le paquet manquait — « PyYAML absent, contrôle
/// ignoré », puis code de sortie 0. Une barrière verte qui n'a rien regardé :
/// exactement le défaut que ce dépôt a corrigé quatre fois ailleurs.
///
/// Cet outil n'a AUCUNE dépendance de paquet et ne doit pas en acquérir. La
/// lecture ci-dessous est donc textuelle, ligne à ligne, taillée pour la seule
/// forme que ce fichier a : deux espaces par niveau, une section `services:` de
/// premier niveau, des variables d'environnement en `CLÉ: valeur`, et une unique
/// ancre `x-dev-auth` fusionnée par un `&lt;&lt;:` seul sur sa ligne.
///
/// CE QUE CETTE LECTURE NE GARANTIT PLUS, ET QUI DOIT ÊTRE DIT :
///
///   · elle ne VALIDE PAS le YAML. Un compose syntaxiquement cassé serait lu
///     sans broncher, et les contrôles rendraient un verdict sur une image
///     fausse du fichier. `docker compose config -q` est la vraie réponse, et
///     il n'est pas ici ;
///   · elle ne connaît que la forme MAPPING de `environment:` — la forme liste
///     `- CLÉ=valeur`, que Compose accepte aussi, serait lue comme vide, donc
///     toutes les adresses du service paraîtraient absentes. Ce fichier n'en
///     emploie aucune ;
///   · elle ne fusionne que les ancres définies AU PREMIER NIVEAU du document et
///     référencées par un `&lt;&lt;:` seul sur sa ligne. Une ancre imbriquée, une
///     liste d'ancres fusionnées, un alias hors `environment:` seraient ignorés ;
///   · elle ne suit ni `extends:`, ni `include:`, ni les fichiers de surcharge :
///     ce que Compose ajouterait au moment de l'exécution est invisible ici ;
///   · les valeurs sont rendues BRUTES, guillemets extérieurs retirés. Aucune
///     substitution `${VAR}` n'est résolue, aucun scalaire de bloc n'est plié.
///
/// La forme de ce fichier est tenue par ce dépôt, pas par un tiers : le jour où
/// elle change, ce sont les contrôles qui le diront, en rendant zéro service.
/// C'est pourquoi <see cref="Services"/> LÈVE quand la section est introuvable
/// ou vide, au lieu de rendre une liste vide qui se lirait « tout va bien ».
///
/// CE TYPE VIT DANS CE FICHIER FAUTE DE MIEUX. `AdressesServiceControle` et
/// `KafkaTopicsControle` en ont tous deux besoin ; deux copies divergeraient, et
/// c'est celle qui se tait qu'on croirait. Sa place est un `ComposeDev.cs` à lui,
/// le jour où le portage rouvrira ce dossier.
/// ═══════════════════════════════════════════════════════════════════════════
/// </remarks>
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
    /// Si la section <c>services:</c> est introuvable ou vide — un contrôle qui ne
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
/// Toute adresse <c>Services:&lt;X&gt;</c> qu'un client gRPC réclame est déclarée
/// là où l'hôte qui l'emploie démarre.
/// </summary>
/// <remarks>
/// ═══════════════════════════════════════════════════════════════════════════
/// UN CLIENT gRPC DONT L'ADRESSE N'EST DÉCLARÉE NULLE PART.
///
/// CE DÉFAUT NE SE TRADUIT PAS PAR UNE PANNE D'APPEL — LE SERVICE NE DÉMARRE PAS.
///
/// Chaque `Add&lt;X&gt;GrpcClient` LÈVE à la construction de l'hôte quand sa clé
/// `Services:&lt;Y&gt;` est absente. C'est le bon sens de l'erreur : un client sans
/// adresse rendrait sinon une panne au premier appel, sur un chemin
/// d'autorisation, des heures après le déploiement.
///
/// Mais l'échec arrive tard dans la boucle — après une construction d'image de
/// plusieurs minutes — et sous une forme qui ne désigne PAS le fichier fautif :
/// la pile montre l'extension d'enregistrement, puis `Program.cs`. Or le fichier
/// à corriger n'est ni celui de la pile, ni un `.cs` : c'est
/// `docker-compose.dev.yml`. C'est arrivé à engagement-service, le jour où
/// répondre à un avis a cessé d'être ouvert à tout compte inscrit — la garde
/// exigeait de savoir quel dossier vendeur porte le compte, donc un client vers
/// merchant-service, dont personne n'a pensé à ajouter l'adresse.
///
/// POURQUOI CE CONTRÔLE N'APPARTIENT PAS À CELUI DE LA CONFIGURATION.
///
/// Celui-là vérifie le sens INVERSE — une clé d'environnement dont aucune section
/// de configuration ne correspond (`OBJECTSTORAGE__*` qui ne liait rien, et
/// media-service tournant en mémoire tout un développement). Ici c'est une
/// section RÉCLAMÉE dont aucune variable ne correspond. Deux moitiés d'une même
/// symétrie, et aucune ne voit l'autre.
///
/// ET IL Y A UN TROISIÈME ENDROIT : LA FABRIQUE DES TESTS D'AUTORISATION.
///
/// `tests/Shared/AuthorizationTestFactory.cs` démarre les vrais `Program.cs` par
/// `WebApplicationFactory`. Les mêmes `Add&lt;X&gt;GrpcClient` y lèvent donc de la
/// même façon — sauf que l'échec ne ressemble à rien de connu : cinquante-neuf
/// tests d'autorisation tombent d'un coup, sur une exception levée AVANT la
/// première décision d'autorisation, et la pile désigne un fichier de contrats
/// partagés.
///
/// C'est arrivé au lot 6.1, le jour où payment-service a gagné un client vers
/// food-order-service. Sa liste de clés était tenue à la main, au rythme des
/// besoins, et RIEN ne la reliait à celle des clients qui la réclament. Ce
/// contrôle vérifiait le compose seul ; il ne regardait pas ce
/// fichier-là.
///
/// Il exige désormais que la fabrique porte TOUTE clé réclamée par une extension
/// d'enregistrement — y compris celles qu'aucun test n'emploie encore. Une clé
/// posée d'avance ne coûte rien ; son absence coûte une suite entière et une
/// demi-heure à comprendre pourquoi.
///
/// ET IL Y AVAIT QUATRE AUTRES FABRIQUES. LE CONTRÔLE N'EN VOYAIT QU'UNE.
///
/// Corriger la fabrique d'autorisation après les cinquante-neuf échecs n'a fermé
/// qu'un cinquième du trou. `OrderIntegrationFixture`, `CatalogIntegrationFixture`,
/// `MerchantsIntegrationFixture` et `GatewayFactory` démarrent elles aussi de
/// vrais `Program.cs`, chacune avec sa propre liste d'adresses tenue à la main.
///
/// Le lot 7.4 a branché `AddProductsGrpcClient` dans `HBA.Order.Api/Program.cs`.
/// Trois tests d'intégration sont tombés au démarrage sur « Services:Catalog est
/// absent » — et le contrôle, vert, regardait ailleurs. C'était la CINQUIÈME
/// occurrence du même motif dans ce dépôt, et la deuxième fois que la correction
/// d'une occurrence laissait les autres intactes.
///
/// CES QUATRE-LÀ NE SE VÉRIFIENT PAS COMME LA PREMIÈRE. La fabrique
/// d'autorisation démarre n'importe quel `Program.cs` : on lui demande TOUT le
/// catalogue. Celles-ci en démarrent UN, désigné par la `ProjectReference` vers
/// un `*.Api.csproj` de leur `.csproj`. Leur exiger tout le catalogue serait du
/// bruit — on leur demande exactement ce que LEUR hôte réclame, ni plus ni moins.
///
/// L'HÔTE EST DÉDUIT DE LA `ProjectReference`, pas d'un nom de fichier ni d'une
/// convention. Un projet de test ne peut démarrer par `WebApplicationFactory` que
/// ce qu'il référence : c'est la seule liaison que le compilateur garantit, donc
/// la seule sur laquelle un contrôle peut s'appuyer sans partager une hypothèse
/// avec le code.
///
/// `apps/` EST UNE RACINE DE TESTS AU MÊME TITRE QUE `tests/`.
/// `apps/api-gateway/tests` n'est pas sous `tests/` — l'oublier laisserait
/// `GatewayFactory` hors contrôle, ce qui est précisément la façon dont ce défaut
/// se reproduit.
///
/// CE QUE CE CONTRÔLE NE PEUT PAS DIRE : que l'adresse posée mène quelque part.
/// Ces fabriques visent délibérément des ports fermés pour les clients qu'aucun
/// chemin n'emprunte. Un client que le code se met à APPELER a donc besoin, en
/// plus de son adresse, d'un double — sans quoi l'erreur de construction devient
/// une erreur d'appel dans chaque test concerné. C'est exactement ce que le
/// catalogue a demandé au lot 7.4, et aucun contrôle statique ne l'aurait vu.
///
/// KUBERNETES N'EST PAS EXPOSÉ DE LA MÊME FAÇON.
///
/// Le compose distribue les adresses service par service : chacun ne reçoit que
/// les pods : un client oublié y trouve la sienne par accident. Le compose, lui,
/// énumère les variables service par service — plus sûr en production, plus
/// facile à oublier en développement. On vérifie donc le compose service par
/// service, sur les clés réellement employées quelque
/// part.
///
/// DEUX DIVERGENCES ASSUMÉES AVEC LE CONTRÔLE PYTHON D'ORIGINE.
///
/// Celui-ci sautait EN SILENCE la fabrique d'autorisation quand
/// le fichier n'existait pas, et s'ignorait tout entier quand PyYAML manquait. Un
/// fichier disparu est pourtant la panne la plus grave que ce contrôle puisse
/// rencontrer : ici, son absence est une FAUTE. Et le YAML n'est plus analysé —
/// voir <see cref="ComposeDev"/> pour ce que la lecture textuelle ne garantit
/// plus.
/// ═══════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed class AdressesServiceControle : IControle
{
    /// <inheritdoc/>
    public string Nom => "adresses-service";

    /// <inheritdoc/>
    public string Resume =>
        "toute adresse Services:<X> réclamée par un client gRPC est déclarée là où l'hôte démarre";

    // `AddXGrpcClient(…) { … }` : la signature, puis le corps jusqu'à l'accolade
    // fermante posée à quatre espaces. C'est la forme des extensions
    // d'enregistrement de `shared/contracts`.
    private static readonly Regex Enregistrement = new(
        @"Add(\w+)GrpcClient\s*\([^)]*\)\s*\{(.*?)\n    \}",
        RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex Cle = new(
        @"configuration\[""Services:(\w+)""\]", RegexOptions.Compiled);

    private static readonly Regex Appel = new(
        @"(Add\w+GrpcClient)\s*\(", RegexOptions.Compiled);

    private static readonly Regex Posee = new(
        @"""Services(?:__|:)(\w+)""", RegexOptions.Compiled);

    private static readonly string[] RacinesTests = ["tests", "apps"];

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
        };

        var table = ClientsConnus();
        if (table.Count == 0)
        {
            fautes.Add(
                "aucune extension d'enregistrement trouvée sous shared/contracts : le "
                + "contrôle serait sans objet, et rendrait vert sans avoir rien comparé.");
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
        //
        // ON EXIGE TOUTES LES CLÉS DU CATALOGUE, PAS SEULEMENT CELLES EMPLOYÉES.
        //
        // Le compose est vérifié service par service : chacun n'a besoin que de
        // ce qu'il appelle. Ici, non — la fabrique démarre N'IMPORTE LEQUEL des
        // `Program.cs` du dépôt, et la prochaine référence de projet peut faire
        // entrer une clé jusque-là inutile dans un hôte que la suite construit.
        // Une clé posée d'avance ne coûte rien ; son absence coûte une suite.
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
        //
        // EXIGENCE EXACTE, PAS LE CATALOGUE ENTIER. Ces fabriques démarrent un
        // `Program.cs` désigné : leur demander toutes les clés produirait du
        // bruit que personne ne corrigerait, et le bruit finit toujours par
        // masquer le vrai manque.
        var fabriquesExaminees = 0;

        foreach (var projet in ProjetsDeTest(fabrique, fautes, nonCouvert))
        {
            if (projet.Hotes.Count == 0)
            {
                // Une suite qui pose des adresses sans démarrer d'API : rien à
                // comparer. Ce n'est pas une anomalie — elle configure autre chose.
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

    /// <summary>Chaque extension d'enregistrement, et la clé de configuration qu'elle exige.</summary>
    private static Dictionary<string, string> ClientsConnus()
    {
        var table = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var fichier in Depot.Fichiers(Depot.Dossier("shared", "contracts"), ".cs"))
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
