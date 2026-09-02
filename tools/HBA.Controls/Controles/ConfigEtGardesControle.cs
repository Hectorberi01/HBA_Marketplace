using System.Text.RegularExpressions;

namespace HBA.Controls.Controles;

/// <summary>
/// Trois défauts qui paraissent corrects et ne font pas ce qu'ils disent.
/// </summary>
/// <remarks>
/// ═══════════════════════════════════════════════════════════════════════════
/// TROIS CONTRÔLES NÉS DE LA MÊME SÉANCE, ET DE LA MÊME CAUSE : DU CODE QUI
/// PARAÎT CORRECT ET NE FAIT PAS CE QU'IL DIT.
///
/// Aucun des trois n'est visible du compilateur. Aucun ne fait échouer un
/// démarrage. Les trois ont coûté des heures de recherche au mauvais endroit.
///
/// ───────────────────────────────────────────────────────────────────────────
/// 1. LES CLÉS D'ENVIRONNEMENT QUI NE LIENT RIEN
///
///     # docker-compose.dev.yml
///     OBJECTSTORAGE__ENDPOINT: http://minio:9000
///     OBJECTSTORAGE__ACCESSKEY: hba-minio
///
///     // le code
///     public const string SectionName = "Media:Storage";
///     public string? AccessKeyId { get; set; }
///
/// Quatre clés, aucune correspondance : le préfixe attendu était
/// `MEDIA__STORAGE__` et la propriété s'appelle `AccessKeyId`. `IsConfigured`
/// rendait donc faux, et media-service basculait sur un stockage EN MÉMOIRE.
/// Toutes les photos produit vivaient dans un dictionnaire, perdues à chaque
/// redémarrage.
///
/// Le service AVERTISSAIT au démarrage. Personne ne lisait la ligne — parce
/// qu'un avertissement au démarrage se noie dans quarante lignes
/// d'initialisation.
///
/// CE CONTRÔLE NE VÉRIFIE PAS LES NOMS DE PROPRIÉTÉS, seulement les RACINES de
/// sections. Vérifier les propriétés demanderait de résoudre les classes
/// d'options et leur hiérarchie ; la racine suffit à attraper le cas réel, et ne
/// produit aucun faux positif.
///
/// ───────────────────────────────────────────────────────────────────────────
/// 2. LES GARDES CITÉES DANS UN COMMENTAIRE ET ABSENTES DU CODE
///
///     "_lire": "Les trois routes existaient sous MapAuthenticatedGroup, AVEC
///               EnsureSellerAsync QUI VÉRIFIE LA PROPRIÉTÉ"
///
/// `EnsureSellerAsync` n'existait NULLE PART. Les trois routes rendaient le
/// chiffre d'affaires brut, les commissions et le net de n'importe quel vendeur
/// à n'importe quel compte authentifié.
///
/// C'EST PIRE QU'UN SILENCE. Un commentaire qui certifie une garde absente fait
/// PASSER la relecture : on lit « c'est gardé », on passe à la suite. Deux cas
/// dans la même séance — celui-là, et le routeur Flutter qui affirmait qu'un
/// écran était « conservé hors routeur » alors que sa route était déclarée
/// trente lignes plus bas.
///
/// ───────────────────────────────────────────────────────────────────────────
/// 3. LE CORPS INFÉRÉ SUR UNE MÉTHODE QUI N'EN ACCEPTE PAS
///
///     seller.MapDelete("/me", DeleteAccountAsync);
///     ...
///     static Task&lt;IResult&gt; DeleteAccountAsync(DeleteAccountRequest request, ...)
///
/// ASP.NET refuse d'INFÉRER un corps sur GET, DELETE, HEAD et OPTIONS. La route
/// compile, et le service refuse de démarrer :
///
///     Body was inferred but the method does not allow inferred body parameters
///
/// Deux services l'ont violé — dont un depuis longtemps, sans que personne ne
/// s'en aperçoive parce qu'il ne démarrait pas pour une AUTRE raison.
///
/// RFC 9110 autorise un corps sur DELETE ; ASP.NET refuse de le DEVINER. La
/// nuance est exactement ce qui rend ce défaut si facile à écrire : le
/// raisonnement sur le protocole est juste, et l'implémentation ne suit pas.
/// `[FromBody]` le lève.
///
/// ───────────────────────────────────────────────────────────────────────────
/// ET LE CONTRÔLE LUI-MÊME LISAIT ZÉRO FICHIER.
///
/// Ce script venait du monolithe, où tout le C# tenait sous `src/`. Après la
/// réorganisation en monorepo, ce dossier n'existe plus : le code vit sous
/// `services/`, `shared/` et `apps/`.
///
/// Le contrôle ne tombait pas en erreur pour autant — il concluait qu'AUCUNE
/// section de configuration n'était déclarée, donc que TOUTES les clés
/// d'environnement du compose étaient orphelines. Plus de cent fautes à chaque
/// exécution, toutes fausses, et les deux autres règles ne vérifiaient plus rien
/// en silence.
///
/// Pire encore dans sa version réparée : le parcours SAUTAIT en silence une
/// racine absente. Ici, <see cref="SourceCsharp.Fichiers"/> s'appuie sur
/// <see cref="Depot.Dossier"/>, qui LÈVE. Un contrôle qui ne peut pas regarder
/// doit s'arrêter, pas rendre zéro.
///
/// ───────────────────────────────────────────────────────────────────────────
/// LE COMPOSE EST LU EN TEXTE, SANS ANALYSEUR YAML — ET C'EST UN RECUL ASSUMÉ.
///
/// Le script Python passait par PyYAML, et se SAUTAIT lui-même quand la
/// bibliothèque manquait : « PyYAML absent — contrôle des clés d'environnement
/// sauté ». Un contrôle dont une règle disparaît selon l'environnement de celui
/// qui le lance est un contrôle sur lequel on ne peut pas s'appuyer.
///
/// L'outil C# n'a AUCUNE référence de paquet, par contrainte (voir
/// `HBA.Controls.csproj`) : il n'y a donc pas d'analyseur YAML à appeler. La
/// lecture ci-dessous est une lecture par INDENTATION, taillée pour la seule
/// question posée : quelles CLÉS vivent sous `services.&lt;nom&gt;.environment`.
/// Elle résout les ancres `&amp;nom` et les fusions `&lt;&lt;: *nom`, parce que
/// le compose de ce dépôt en contient et que les ignorer masquerait vingt-deux
/// blocs d'authentification.
///
/// CE QUE CE RECUL COÛTE : elle ne comprend ni les scalaires multi-lignes
/// (`|`, `&gt;`), ni les mappages en accolades (`{a: 1}`), ni un
/// `environment:` écrit en LISTE (`- CLE=valeur`). Le compose de ce dépôt
/// n'utilise aucune de ces formes ; le jour où il en gagne une, ce texte est
/// l'endroit où le dire — et ces clés-là deviendraient invisibles, pas fausses.
/// ═══════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed class ConfigEtGardesControle : IControle
{
    /// <inheritdoc/>
    public string Nom => "config-et-gardes";

    /// <inheritdoc/>
    public string Resume =>
        "clés d'environnement liées, gardes citées existantes, aucun corps inféré sur GET/DELETE";

    // Racines fournies par le cadre applicatif ou l'hôte, jamais déclarées dans
    // le code. Les omettre produirait cinq fautes à chaque exécution — et un
    // contrôle qui crie pour rien finit ignoré, ce qui est le seul échec qui
    // compte.
    private static readonly string[] RacinesConnues =
    [
        "ASPNETCORE", "DOTNET", "LOGGING", "CONNECTIONSTRINGS",
        "ALLOWEDHOSTS", "URLS", "KESTREL",
    ];

    private static readonly string[] VerbesSansCorps = ["Get", "Delete", "Head", "Options"];

    private static readonly Regex[] MotifsDeSection =
    [
        new(@"SectionName\s*=\s*""([^""]+)""", RegexOptions.Compiled),
        new(@"GetSection\(""([^""]+)""\)", RegexOptions.Compiled),
        new(@"[Cc]onfiguration\[""([^""]+)""\]", RegexOptions.Compiled),
    ];

    private static readonly Regex Garde = new(
        @"\b(Ensure\w+Async|Deny\w+Async)\b", RegexOptions.Compiled);

    private static readonly Regex GardeDefinie = new(
        @"\b(Ensure\w+Async|Deny\w+Async)\s*\(", RegexOptions.Compiled);

    private static readonly Regex ParametreDeRequete = new(
        @"(\w+Request)\s+\w+", RegexOptions.Compiled);

    // ════════════════════════════════════════════════════════ lecture du compose
    private static string CleDeLigne(string nu)
    {
        var pos = nu.IndexOf(':');
        return pos < 0 ? string.Empty : nu[..pos].Trim().Trim('"').Trim('\'');
    }

    private static string ValeurDeLigne(string nu)
    {
        var pos = nu.IndexOf(':');
        return pos < 0 ? string.Empty : nu[(pos + 1)..].Trim();
    }

    /// <summary>Index de fin (exclu) du bloc indenté sous la ligne `i`.</summary>
    private static int FinDuBloc(List<(int Indent, string Texte)> lignes, int i)
    {
        var fin = i + 1;
        while (fin < lignes.Count && lignes[fin].Indent > lignes[i].Indent)
        {
            fin++;
        }

        return fin;
    }

    /// <summary>Les clés d'un bloc, fusions `&lt;&lt;: *ancre` résolues.</summary>
    private static List<string> ClesDuBloc(
        List<(int Indent, string Texte)> lignes,
        int debut,
        int fin,
        Dictionary<string, List<string>> ancres)
    {
        var cles = new List<string>();
        if (debut >= fin)
        {
            return cles;
        }

        var indent = lignes[debut].Indent;
        for (var i = debut; i < fin; i++)
        {
            if (lignes[i].Indent != indent || lignes[i].Texte.StartsWith('-'))
            {
                continue;
            }

            var cle = CleDeLigne(lignes[i].Texte);
            if (cle.Length == 0)
            {
                continue;
            }

            if (cle != "<<")
            {
                cles.Add(cle);
                continue;
            }

            // LA FUSION EST LA RAISON D'ÊTRE DE CETTE LECTURE. Vingt-deux
            // services héritent leurs clés d'authentification d'une seule ancre :
            // les ignorer ferait disparaître ces clés du contrôle sans que rien
            // ne le signale.
            var valeur = ValeurDeLigne(lignes[i].Texte);
            if (valeur.StartsWith('*')
                && ancres.TryGetValue(valeur[1..].Trim(), out var heritees))
            {
                cles.AddRange(heritees);
            }
        }

        return cles;
    }

    private static Dictionary<string, List<string>> Ancres(List<(int Indent, string Texte)> lignes)
    {
        var ancres = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        for (var i = 0; i < lignes.Count; i++)
        {
            var valeur = ValeurDeLigne(lignes[i].Texte);
            if (!valeur.StartsWith('&'))
            {
                continue;
            }

            ancres[valeur[1..].Trim()] =
                ClesDuBloc(lignes, i + 1, FinDuBloc(lignes, i), ancres);
        }

        return ancres;
    }

    private static List<(string Service, string Cle)> ClesEnvironnement(string compose)
    {
        var lignes = new List<(int Indent, string Texte)>();
        foreach (var brute in File.ReadAllText(compose).Split('\n'))
        {
            var sansFin = brute.TrimEnd('\r');
            var nu = sansFin.Trim();
            if (nu.Length == 0 || nu.StartsWith('#'))
            {
                continue;
            }

            lignes.Add((sansFin.Length - sansFin.TrimStart(' ').Length, nu));
        }

        var ancres = Ancres(lignes);
        var resultat = new List<(string Service, string Cle)>();

        for (var i = 0; i < lignes.Count; i++)
        {
            if (lignes[i].Indent != 0 || CleDeLigne(lignes[i].Texte) != "services")
            {
                continue;
            }

            var finServices = FinDuBloc(lignes, i);
            if (i + 1 >= finServices)
            {
                break;
            }

            var indentService = lignes[i + 1].Indent;
            for (var j = i + 1; j < finServices; j++)
            {
                if (lignes[j].Indent != indentService)
                {
                    continue;
                }

                var service = CleDeLigne(lignes[j].Texte);
                var finService = FinDuBloc(lignes, j);
                if (j + 1 >= finService)
                {
                    continue;
                }

                var indentEnfant = lignes[j + 1].Indent;
                for (var k = j + 1; k < finService; k++)
                {
                    if (lignes[k].Indent != indentEnfant
                        || CleDeLigne(lignes[k].Texte) != "environment")
                    {
                        continue;
                    }

                    foreach (var cle in ClesDuBloc(
                                 lignes, k + 1, FinDuBloc(lignes, k), ancres))
                    {
                        resultat.Add((service, cle));
                    }
                }
            }

            break;
        }

        return resultat;
    }

    // ═══════════════════════════════════════════════════ 1. clés d'environnement
    /// <summary>Toutes les racines de section que le code lit, sous les trois formes.</summary>
    private static HashSet<string> RacinesDeclarees(
        IReadOnlyList<(string Chemin, string Texte)> sources)
    {
        var racines = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (_, texte) in sources)
        {
            foreach (var motif in MotifsDeSection)
            {
                foreach (Match m in motif.Matches(texte))
                {
                    racines.Add(m.Groups[1].Value.Split(':')[0].ToUpperInvariant());
                }
            }
        }

        foreach (var connue in RacinesConnues)
        {
            racines.Add(connue);
        }

        return racines;
    }

    private static List<string> ControleEnvironnement(
        IReadOnlyList<(string Chemin, string Texte)> sources, string compose)
    {
        var declarees = RacinesDeclarees(sources);
        var fautes = new List<string>();

        foreach (var (service, cle) in ClesEnvironnement(compose))
        {
            if (!cle.Contains("__", StringComparison.Ordinal))
            {
                continue;
            }

            var racine = cle.Split("__")[0].ToUpperInvariant();
            if (declarees.Contains(racine))
            {
                continue;
            }

            fautes.Add(
                $"{Path.GetFileName(compose)} · {service} · {cle} → aucune section "
                + $"« {racine} » n'est lue par le code. La valeur est ignorée EN SILENCE.");
        }

        return fautes;
    }

    // ═══════════════════════════════════════════════════════════ 2. gardes citées
    /// <summary>Une garde nommée dans un commentaire ou une métadonnée doit exister.</summary>
    private static List<string> ControleGardes(
        IReadOnlyList<(string Chemin, string Texte)> sources, string? metadonnees)
    {
        // Les noms réellement définis, quelle que soit leur visibilité.
        var definis = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (_, texte) in sources)
        {
            foreach (Match m in GardeDefinie.Matches(texte))
            {
                definis.Add(m.Groups[1].Value);
            }
        }

        // ON NE CHERCHE QUE DANS LES COMMENTAIRES ET LES CHAÎNES DE MÉTADONNÉES.
        // Une mention dans du code exécutable est déjà vérifiée par le
        // compilateur ; c'est l'affirmation NON COMPILÉE qui peut mentir.
        var aScruter = sources.ToList();
        if (metadonnees is not null)
        {
            aScruter.Add((metadonnees, File.ReadAllText(metadonnees)));
        }

        var cites = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        foreach (var (chemin, texte) in aScruter)
        {
            var estMetadonnee = Path.GetExtension(chemin)
                .Equals(".json", StringComparison.OrdinalIgnoreCase);
            var lignes = texte.Split('\n');
            for (var i = 0; i < lignes.Length; i++)
            {
                var nu = lignes[i].Trim();
                var commentaire = nu.StartsWith("//", StringComparison.Ordinal)
                                  || nu.StartsWith('*');
                if (!commentaire && !estMetadonnee)
                {
                    continue;
                }

                foreach (Match m in Garde.Matches(lignes[i]))
                {
                    var nom = m.Groups[1].Value;
                    if (definis.Contains(nom))
                    {
                        continue;
                    }

                    if (!cites.TryGetValue(nom, out var lieux))
                    {
                        lieux = new SortedSet<string>(StringComparer.Ordinal);
                        cites[nom] = lieux;
                    }

                    lieux.Add($"{Depot.Relatif(chemin)}:{i + 1}");
                }
            }
        }

        return cites
            .Select(paire =>
                $"« {paire.Key} » est annoncée comme garde mais n'existe nulle part : "
                + string.Join(", ", paire.Value))
            .ToList();
    }

    // ═══════════════════════════════════════════════════════════ 3. corps inféré
    private static List<string> ControleCorpsInfere(
        IReadOnlyList<(string Chemin, string Texte)> sources)
    {
        var fautes = new List<string>();
        foreach (var (chemin, texte) in sources)
        {
            if (!Path.GetFileName(chemin).Contains("Endpoints", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var verbe in VerbesSansCorps)
            {
                var route = new Regex($@"\.Map{verbe}\(""([^""]*)"",\s*(\w+)\)");
                foreach (Match m in route.Matches(texte))
                {
                    var handler = m.Groups[2].Value;
                    var signature = Regex.Match(
                        texte,
                        $@"Task<IResult>\s+{Regex.Escape(handler)}\s*\(([^)]*)\)",
                        RegexOptions.Singleline);
                    if (!signature.Success)
                    {
                        continue;
                    }

                    var parametres = signature.Groups[1].Value;

                    // ON CHERCHE UN TYPE « …Request », pas n'importe quel type
                    // complexe : `ISender`, `ClaimsPrincipal` et les autres
                    // services sont résolus par injection, jamais depuis le
                    // corps. Élargir produirait un faux positif sur chaque route.
                    var premier = ParametreDeRequete.Match(parametres);
                    if (!premier.Success
                        || parametres.Contains("[FromBody]", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    fautes.Add(
                        $"{Depot.Relatif(chemin)} · Map{verbe}(\"{m.Groups[1].Value}\") → "
                        + $"{handler} : paramètre « {premier.Groups[1].Value} » sans "
                        + $"[FromBody]. ASP.NET refuse d'inférer un corps sur "
                        + $"{verbe.ToUpperInvariant()} : le service ne démarrera pas.");
                }
            }
        }

        return fautes;
    }

    /// <inheritdoc/>
    public Verdict Executer()
    {
        var sources = SourceCsharp.Fichiers()
            .Select(c => (Chemin: c, Texte: File.ReadAllText(c)))
            .ToList();

        var constats = new List<string> { $"{sources.Count} fichier(s) C# analysé(s)." };
        var nonCouvert = new List<string>
        {
            "les NOMS DE PROPRIÉTÉS des classes d'options : seule la RACINE de section "
            + "est rapprochée, `MEDIA__STORAGE__ACCESSKEY` passerait pour "
            + "`MEDIA__STORAGE__ACCESSKEYID`",
            "les scalaires multi-lignes, les mappages en accolades et un `environment:` "
            + "écrit en LISTE (`- CLE=valeur`) : la lecture du compose est une lecture "
            + "par indentation, pas un analyseur YAML",
            "les gardes citées ailleurs que dans un commentaire `//`, une ligne `*` ou un "
            + "fichier `.json`",
            "les routes dont le gestionnaire ne rend pas `Task<IResult>`, et celles "
            + "déclarées hors d'un fichier dont le nom contient « Endpoints »",
        };

        var fautes = new List<string>();

        var compose = Depot.Chemin("docker-compose.dev.yml");
        if (File.Exists(compose))
        {
            fautes.AddRange(ControleEnvironnement(sources, compose));
        }
        else
        {
            // L'ABSENCE DU COMPOSE NE DOIT PAS SE LIRE « RIEN À SIGNALER ».
            nonCouvert.Add(
                "les clés d'environnement : `docker-compose.dev.yml` est introuvable, "
                + "cette règle n'a RIEN examiné");
        }

        var metadonnees = Depot.Chemin(
            "apps", "api-gateway", "src", "HBA.Gateway.Api", "appsettings.json");
        if (!File.Exists(metadonnees))
        {
            nonCouvert.Add(
                $"les gardes citées dans {Depot.Relatif(metadonnees)} : le fichier est "
                + "introuvable, seuls les commentaires C# ont été scrutés");
            metadonnees = null;
        }

        fautes.AddRange(ControleGardes(sources, metadonnees));
        fautes.AddRange(ControleCorpsInfere(sources));

        return new Verdict(fautes, constats, nonCouvert);
    }
}
