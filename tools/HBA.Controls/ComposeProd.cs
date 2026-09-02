namespace HBA.Controls;

/// <summary>
/// Engendre <c>docker-compose.prod.yml</c> depuis <c>docker-compose.dev.yml</c>.
/// </summary>
/// <remarks>
/// ═══════════════════════════════════════════════════════════════════════════
/// POURQUOI UN GÉNÉRATEUR PLUTÔT QU'UN SECOND FICHIER TENU À LA MAIN.
///
/// La source est `docker-compose.dev.yml`, seule description complète des vingt
/// services. Un second compose écrit à la main divergerait au premier
/// changement de variable, et la divergence ne se verrait qu'en production.
///
/// LES VALEURS NE SONT JAMAIS ICI. Chaque secret devient une référence
/// `${VAR:?...}` : Compose REFUSE de démarrer si la variable est absente,
/// plutôt que de lancer un service avec une chaîne vide.
///
/// LE TRAVAIL EST LIGNE À LIGNE, ET C'EST DÉLIBÉRÉ. Charger le compose dans un
/// modèle objet puis le réécrire perdrait les commentaires — or ce dépôt met la
/// raison d'un réglage dans le commentaire qui le précède. Un compose de
/// production sans ses raisons est un compose que personne n'ose corriger.
/// ═══════════════════════════════════════════════════════════════════════════
/// </remarks>
public static class ComposeProd
{
    /// <summary>Le verbe qui déclenche la génération.</summary>
    public const string Verbe = "compose-prod";

    private const string Proprietaire = "hectorberi01";
    private const string ServicePublic = "gateway";
    private const string PortPublic = "8080";
    private const string DomainePublic = "${HBA_DOMAINE:?le domaine public est obligatoire}";
    private const string CourrielAcme = "${HBA_ACME_EMAIL:?l'adresse pour Let's Encrypt est obligatoire}";
    private const string VersionTraefik = "traefik:v3.3";

    // Le service `gateway` est publié sous le nom `api-gateway` : le compose le
    // demandait sous son nom de dossier, et le `pull` cherchait une image qui
    // n'a jamais existé.
    private static readonly Dictionary<string, string> NomsImages = new()
    {
        ["gateway"] = "api-gateway",
    };

    // Construits sur le VPS, publiés nulle part.
    private static readonly HashSet<string> ConstruitsSurPlace = ["rembg"];

    private static readonly Dictionary<string, string> HorsProduction = new()
    {
        ["postgres"] = "la base vit sur un second VPS, jointe par le tunnel",
        ["redis-ui"] = "console d'exploration — jamais en production",
        ["minio-init"] = "amorçage de développement ; en production, voir le runbook",
    };

    private static readonly Dictionary<string, string> Bloques = new()
    {
        ["notification-service"] =
            "aucun adaptateur ISmsSender de production n'existe ; le SMS est le "
            + "canal OTP par défaut, et NotificationsModuleInstaller lève dans les "
            + "deux branches. CONSÉQUENCE : aucun courriel ni SMS ne part.",
        ["return-refund-service"] =
            "deux adaptateurs gRPC restent des bouchons — la marchandise retournée "
            + "n'est jamais remise en stock, et aucune course d'enlèvement n'est "
            + "créée alors qu'un numéro est rendu au client.",
    };

    private static readonly Dictionary<string, string> Bases = new()
    {
        ["identity-service"] = "hba_identity",
        ["user-service"] = "hba_user",
        ["media-service"] = "hba_media",
        ["notification-service"] = "hba_communication",
        ["payment-service"] = "hba_financial",
        ["promotion-service"] = "hba_promotion",
        ["review-service"] = "hba_engagement",
        ["catalog-service"] = "hba_catalog",
        ["cart-service"] = "hba_commerce",
        ["inventory-service"] = "hba_inventory",
        ["order-service"] = "hba_order",
        ["seller-service"] = "hba_merchant",
        ["return-refund-service"] = "hba_commerce",
        ["delivery-service"] = "hba_delivery",
        ["driver-service"] = "hba_delivery",
        ["delivery-pricing-service"] = "hba_delivery",
        ["route-service"] = "hba_delivery",
        ["food-cart-service"] = "hba_food",
        ["food-order-service"] = "hba_food",
        ["restaurant-service"] = "hba_food",
    };

    private static readonly HashSet<string> Secrets =
    [
        "AUTHENTICATION__SIGNINGKEY", "JWT__SIGNINGKEY", "INTERNAL__APIKEY",
        "INTERNAL__PRIVATEKEY", "INTERNAL__PUBLICKEYS",
        "SECURITY__SECRETPROTECTION__KEY", "ADMIN__PASSWORD",
        "NOTIFICATIONS__EMAIL__APIKEY", "MEDIA__STORAGE__ACCESSKEYID",
        "MEDIA__STORAGE__SECRETACCESSKEY", "MINIO_ROOT_USER", "MINIO_ROOT_PASSWORD",
    ];

    private static readonly Dictionary<string, string> Remplacements = new()
    {
        ["ADMIN__EMAIL"] = "hector.adjakpa@hbatechettrade.com",
    };

    private static readonly Dictionary<string, (string Cle, string Valeur)[]> AjoutsEnvironnement = new()
    {
        ["payment-service"] =
        [
            ("PAYMENTS__FEDAPAY__APIKEY",
             "${PAYMENTS__FEDAPAY__APIKEY:?la cle FedaPay sk_live_... est obligatoire}"),
            ("PAYMENTS__FEDAPAY__WEBHOOKSECRET",
             "${PAYMENTS__FEDAPAY__WEBHOOKSECRET:?sans lui les notifications FedaPay sont rejetees}"),
            ("PAYMENTS__FEDAPAY__BASEURL", "https://api.fedapay.com/v1"),
            ("PAYMENTS__FEDAPAY__ENABLEPAYOUTS", "\"true\""),
            ("PAYMENTS__FEDAPAY__CURRENCY", "XOF"),
            ("PAYMENTS__FEDAPAY__CALLBACKURL",
             "https://api.hba-express.com/api/payments/webhooks/fedapay"),
        ],
    };

    // Aucun service ne publie de port sur le VPS : publier, c'est publier sur
    // Internet. Les consoles passent par la boucle locale et un tunnel SSH.
    private static readonly HashSet<string> PortsAutorises = [];

    private static readonly Dictionary<string, string> PortsExposes = new()
    {
        ["gateway"] = "8080",
    };

    private static readonly Dictionary<string, (string Hote, string Conteneur)[]> PortsLoopback = new()
    {
        ["kafka-ui"] = [("8090", "8080")],
        // Console MinIO SEULE — l'API S3 (9000) reste interne.
        ["minio"] = [("9001", "9001")],
    };

    private static readonly string[] ImagesTierces =
        ["redis", "confluentinc", "minio", "danielgatis", "provectuslabs", "traefik"];

    private static readonly (string Motif, string Quoi)[] MotifsSuspects =
    [
        (@"Password=hba\b", "mot de passe de développement « hba »"),
        ("hba-development-signing-key", "clé de signature de développement"),
        ("Admin123!", "mot de passe administrateur de développement"),
        (@"\bminioadmin\b", "identifiants MinIO de développement"),
        ("cle-interne-de-test", "clé interne de test"),
    ];

    private static readonly Dictionary<string, string> DevSeulement = new()
    {
        ["INTERNAL__IDENTITESNONSIGNEES"] =
            "identites gRPC non signees : `AddHbaGrpc` leve hors Development",
    };

    // service compose -> nom de variable de sa clé privée, dérivé du Dockerfile.
    private static readonly Dictionary<string, string> ClesInternes = new();

    // Les services qui fusionnent l'ancre partagée. Une ancre définie que
    // personne ne fusionne, ou l'inverse, ne doit pas passer inaperçu.
    private static readonly List<string> Fusions = [];

    // ── Lecture du compose source ────────────────────────────────────────────

    /// <summary>
    /// Découpe en lignes EN CONSERVANT le saut de ligne de chacune.
    /// </summary>
    /// <remarks>
    /// Tout ce générateur assemble des lignes qui portent déjà leur `\n`, comme
    /// le faisait `readlines()`. Découper sans les fins obligerait à décider, à
    /// chaque concaténation, s'il faut en rajouter un — et un oubli ne se verrait
    /// qu'à la lecture du compose engendré.
    /// </remarks>
    private static string[] LignesAvecFin(string texte)
        => System.Text.RegularExpressions.Regex.Split(texte, "(?<=\n)")
            .Where(l => l.Length > 0)
            .ToArray();

    private static readonly System.Text.RegularExpressions.Regex EntrypointDotnet =
        new(@"ENTRYPOINT\s*\[\s*""dotnet""\s*,\s*""([\w.]+)\.dll""");

    /// <summary>`build:` d'un service -> nom du projet .NET, lu dans son Dockerfile.</summary>
    private static string? ProjetDeService(Dictionary<string, string>? build)
    {
        if (build is null)
        {
            return null;
        }

        var chemin = build.TryGetValue("dockerfile", out var d) && d.Length > 0
            ? d
            : Path.Combine(build.GetValueOrDefault("context", ""), "Dockerfile");

        string texte;
        try
        {
            texte = File.ReadAllText(Depot.Chemin(chemin.Replace('/', Path.DirectorySeparatorChar)));
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }

        var trouve = EntrypointDotnet.Match(texte);
        return trouve.Success ? trouve.Groups[1].Value : null;
    }

    private static string VariableDeCle(string projet)
        => "INTERNAL_KEY_" + projet.ToUpperInvariant().Replace(".", "_");

    /// <summary>Le `build:` d'un bloc de service, sous forme de dictionnaire.</summary>
    /// <remarks>
    /// CE QUE CETTE FONCTION NE COUVRE PAS : la forme courte `build: ./chemin`,
    /// et les ancres YAML à l'intérieur d'un `build:`. Le compose source n'en
    /// utilise pas ; si cela changeait, la clé privée du service concerné
    /// manquerait — et le contrôle des clés, plus bas, le dirait.
    /// </remarks>
    private static Dictionary<string, string>? BuildDeBloc(IReadOnlyList<string> corps)
    {
        for (var i = 0; i < corps.Count; i++)
        {
            if (corps[i].TrimEnd() != "    build:")
            {
                continue;
            }

            var champs = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var suite in corps.Skip(i + 1))
            {
                if (suite.Trim().Length == 0)
                {
                    continue;
                }

                var indentation = suite.Length - suite.TrimStart().Length;
                if (indentation <= 4)
                {
                    break;
                }

                var paire = System.Text.RegularExpressions.Regex.Match(
                    suite, @"^\s*([a-z_]+)\s*:\s*(.*?)\s*$");
                if (paire.Success)
                {
                    champs[paire.Groups[1].Value] = paire.Groups[2].Value.Trim('"', '\'');
                }
            }

            return champs.Count > 0 ? champs : null;
        }

        return null;
    }

    /// <summary>Rend (nom, lignes du bloc). `debut` est l'index de la ligne `  nom:`.</summary>
    private static (string Nom, List<string> Corps) BlocDeService(
        string[] lignes, int debut, int fin)
    {
        var nom = lignes[debut].Trim().TrimEnd(':');
        var corps = new List<string>();
        for (var i = debut + 1; i < fin; i++)
        {
            if (System.Text.RegularExpressions.Regex.IsMatch(lignes[i], @"^  \S")
                || System.Text.RegularExpressions.Regex.IsMatch(lignes[i], @"^\S"))
            {
                break;
            }

            corps.Add(lignes[i]);
        }

        return (nom, corps);
    }

    /// <summary>Rejoue l'ancre `x-dev-auth` en version production.</summary>
    /// <remarks>
    /// Les clés sont les mêmes — c'est le point : ce que vingt et un services
    /// attendent ne se décide pas ici. Seules les VALEURS changent, et seulement
    /// pour celles que <see cref="Secrets"/> désigne.
    /// </remarks>
    private static List<string> AncreDeProduction(string[] lignes)
    {
        var debut = Array.FindIndex(lignes, l => l.StartsWith("x-dev-auth:", StringComparison.Ordinal));
        if (debut < 0)
        {
            return [];
        }

        var corps = new List<string>();
        for (var i = debut + 1; i < lignes.Length; i++)
        {
            var l = lignes[i];
            if (l.Trim().Length > 0 && !l.StartsWith(' '))
            {
                break;
            }

            var m = System.Text.RegularExpressions.Regex.Match(l, @"^  ([A-Z][A-Z0-9_]*):\s*(.*)$");
            if (!m.Success)
            {
                continue;
            }

            var cle = m.Groups[1].Value;
            var valeur = m.Groups[2].Value.Trim();

            if (DevSeulement.TryGetValue(cle, out var pourquoi))
            {
                corps.Add($"  # {cle} : retire — {pourquoi}.\n");
                continue;
            }

            if (Secrets.Contains(cle))
            {
                corps.Add($"  {cle}: ${{{cle}:?{cle} est obligatoire en production}}\n");
                continue;
            }

            if (Remplacements.TryGetValue(cle, out var remplacee))
            {
                corps.Add($"  {cle}: {remplacee}\n");
                continue;
            }

            corps.Add($"  {cle}: {valeur}\n");
        }

        if (corps.Count == 0)
        {
            return [];
        }

        corps.Add("  INTERNAL__PUBLICKEYS: "
                  + "${INTERNAL_PUBLIC_KEYS:?le registre des cles publiques gRPC "
                  + "est obligatoire — scripts/generer-identites-internes.sh}\n");

        var entete = new List<string>
        {
            "# ═════════════════════════════════════════════════════════════════════════════\n",
            "# LES CLES PARTAGEES PAR TOUS LES SERVICES.\n",
            "#\n",
            "# Meme forme que `x-dev-auth` dans le compose de developpement, memes cles —\n",
            "# et c'est voulu : ce que les services attendent ne se decide pas ici. Seules\n",
            "# les valeurs changent, remplacees par des references obligatoires.\n",
            "#\n",
            "# AUTHENTICATION__SIGNINGKEY et JWT__SIGNINGKEY doivent porter la MEME valeur :\n",
            "# identity-service signe avec l'une, les autres verifient avec l'autre.\n",
            "#\n",
            "# INTERNAL__APIKEY doit etre IDENTIQUE partout — l'appelant la presente,\n",
            "# l'appele la compare. Une divergence rend `NotFound`, muet sur la cause.\n",
            "#\n",
            "# SECURITY__SECRETPROTECTION__KEY ne se regenere PAS : ce qu'elle a chiffre ne\n",
            "# se dechiffre pas avec la suivante.\n",
            "# ═════════════════════════════════════════════════════════════════════════════\n",
            "x-prod-auth: &prod-auth\n",
        };
        entete.AddRange(corps);
        entete.Add("\n");
        return entete;
    }

    // ── Les sept transformations ─────────────────────────────────────────────

    /// <summary>Applique les sept transformations à un bloc de service.</summary>
    private static List<string> Transformer(string nom, IReadOnlyList<string> corps)
    {
        static bool Re(string entree, string motif)
            => System.Text.RegularExpressions.Regex.IsMatch(entree, motif);

        var sortie = new List<string>();
        var i = 0;

        while (i < corps.Count)
        {
            var l = corps[i];

            // 1. `build:` devient `image:` — sauf pour ce qui se construit sur place.
            if (Re(l, @"^    build:\s*$"))
            {
                var image = NomsImages.GetValueOrDefault(nom, nom);
                sortie.Add($"    image: ghcr.io/{Proprietaire}/{image}:"
                           + "${HBA_TAG:?le tag d'image est obligatoire}\n");
                i++;
                var blocBuild = new List<string>();
                while (i < corps.Count && Re(corps[i], "^      "))
                {
                    blocBuild.Add(corps[i]);
                    i++;
                }

                if (ConstruitsSurPlace.Contains(nom))
                {
                    sortie.Add(l);
                    sortie.AddRange(blocBuild);
                }

                continue;
            }

            // 2. Variables que le compose de developpement ne porte pas.
            if (Re(l, @"^    environment:\s*$")
                && (AjoutsEnvironnement.ContainsKey(nom) || ClesInternes.ContainsKey(nom)))
            {
                sortie.Add(l);
                sortie.Add("      # Ajoutees pour la production : le compose de "
                           + "developpement ne les porte pas.\n");

                if (ClesInternes.TryGetValue(nom, out var variable))
                {
                    sortie.Add($"      INTERNAL__PRIVATEKEY: ${{{variable}:?identite gRPC de "
                               + $"{nom} absente — scripts/generer-identites-internes.sh}}\n");
                }

                foreach (var (cle, valeur) in AjoutsEnvironnement.GetValueOrDefault(nom, []))
                {
                    sortie.Add($"      {cle}: {valeur}\n");
                }

                i++;
                continue;
            }

            // 3. `depends_on:` — une cible ecartee devient un commentaire.
            if (Re(l, @"^    depends_on:\s*$"))
            {
                i++;
                var gardees = new List<string>();
                while (i < corps.Count && Re(corps[i], @"^      \S"))
                {
                    var cible = System.Text.RegularExpressions.Regex.Match(
                        corps[i], @"^      ([\w.-]+):\s*$");
                    if (!cible.Success)
                    {
                        gardees.Add(corps[i]);
                        i++;
                        continue;
                    }

                    var bloc = new List<string> { corps[i] };
                    i++;
                    while (i < corps.Count && Re(corps[i], "^        "))
                    {
                        bloc.Add(corps[i]);
                        i++;
                    }

                    var nomCible = cible.Groups[1].Value;
                    if (Ecartes.TryGetValue(nomCible, out var raison))
                    {
                        gardees.Add($"      # {nomCible} : retire — {raison}.\n");
                    }
                    else
                    {
                        gardees.AddRange(bloc);
                    }
                }

                // UN `depends_on:` VIDE FAIT REFUSER LE FICHIER ENTIER par Compose.
                if (gardees.Any(g => Re(g, @"^      [\w.-]+:")))
                {
                    sortie.Add(l);
                    sortie.AddRange(gardees);
                }
                else
                {
                    sortie.Add("    # `depends_on:` retire : toutes ses cibles "
                               + "sont ecartees de la production.\n");
                    sortie.AddRange(gardees.Where(g => g.TrimStart().StartsWith('#')));
                }

                continue;
            }

            // 4. L'ancre partagee bascule sur sa version de production.
            if (l.Contains("<<: *dev-auth", StringComparison.Ordinal))
            {
                sortie.Add("      <<: *prod-auth\n");
                Fusions.Add(nom);
                i++;
                continue;
            }

            // 5. `ports:` — publier sur le VPS, c'est publier sur Internet.
            if (Re(l, @"^    ports:\s*$") && !PortsAutorises.Contains(nom))
            {
                i++;
                while (i < corps.Count && Re(corps[i], "^      [-#]"))
                {
                    i++;
                }

                if (!PortsLoopback.ContainsKey(nom))
                {
                    sortie.Add("    # `ports:` retiré : publier sur le VPS, c'est publier sur Internet.\n");
                    sortie.Add("    # Les services se joignent par le réseau `hba-backend`.\n");
                }

                if (PortsLoopback.TryGetValue(nom, out var boucles))
                {
                    sortie.Add("    # Console : publiee sur la BOUCLE LOCALE, jamais sur 0.0.0.0.\n");
                    sortie.Add("    # Acces par tunnel : ssh -L <port>:127.0.0.1:<port> <hote>\n");
                    sortie.Add("    ports:\n");
                    foreach (var (hote, conteneur) in boucles)
                    {
                        sortie.Add($"      - \"127.0.0.1:{hote}:{conteneur}\"\n");
                    }
                }

                if (PortsExposes.TryGetValue(nom, out var expose))
                {
                    sortie.Add("    # `expose:` ne publie rien — il DECLARE le port, pour que le\n");
                    sortie.Add("    # proxy de Coolify sache ou router le domaine.\n");
                    sortie.Add($"    expose: [\"{expose}\"]\n");
                }

                continue;
            }

            // 6 et 7. Les variables d'environnement, une par une.
            var m = System.Text.RegularExpressions.Regex.Match(
                l, @"^      ([A-Z][A-Z0-9_]*):\s*(.*)$");
            if (m.Success)
            {
                var cle = m.Groups[1].Value;

                if (cle == "ASPNETCORE_ENVIRONMENT")
                {
                    sortie.Add("      ASPNETCORE_ENVIRONMENT: Production\n");
                    i++;
                    continue;
                }

                if (cle == "CONNECTIONSTRINGS__DEFAULT")
                {
                    if (!Bases.TryGetValue(nom, out var basePg))
                    {
                        sortie.Add("      # PAS DE BASE CONNUE POUR CE SERVICE — à compléter dans BASES\n");
                        sortie.Add(l);
                    }
                    else
                    {
                        sortie.Add(
                            "      CONNECTIONSTRINGS__DEFAULT: "
                            + "Host=${HBA_PGHOST:-10.20.0.2};Port=5432;"
                            + $"Database={basePg};Username={basePg};"
                            + $"Password=${{{basePg.ToUpperInvariant()}_PASSWORD:?mot de passe de {basePg} absent}}\n");
                    }

                    i++;
                    continue;
                }

                if (Remplacements.TryGetValue(cle, out var valeurRemplacee))
                {
                    sortie.Add($"      {cle}: {valeurRemplacee}\n");
                    i++;
                    continue;
                }

                if (Secrets.Contains(cle))
                {
                    sortie.Add($"      {cle}: ${{{cle}:?{cle} est obligatoire en production}}\n");
                    i++;
                    continue;
                }
            }

            sortie.Add(l);
            i++;
        }

        if (!sortie.Any(x => x.Contains("restart:", StringComparison.Ordinal)))
        {
            sortie.Add("    restart: unless-stopped\n");
        }

        if (!sortie.Any(x => x.Contains("container_name:", StringComparison.Ordinal)))
        {
            sortie.Insert(0, $"    container_name: hba-{nom}\n");
        }

        return sortie;
    }

    /// <summary>Les étiquettes de routage, posées sur le service public.</summary>
    private static List<string> EtiquettesTraefik() =>
    [
        "    # Routage public — lu par Traefik. Ce service est le SEUL a porter\n",
        "    # `traefik.enable` : voir l'encadre du bloc traefik plus bas.\n",
        "    labels:\n",
        "      traefik.enable: \"true\"\n",
        "      traefik.docker.network: hba-backend\n",
        $"      traefik.http.routers.hba.rule: Host(`{DomainePublic}`)\n",
        "      traefik.http.routers.hba.entrypoints: websecure\n",
        "      traefik.http.routers.hba.tls.certresolver: lets\n",
        $"      traefik.http.services.hba.loadbalancer.server.port: \"{PortPublic}\"\n",
    ];

    /// <summary>Le bloc de service Traefik, ajouté au rendu.</summary>
    private static List<string> TraefikService() =>
    [
        "\n  traefik:\n",
        "    container_name: hba-traefik\n",
        $"    image: {VersionTraefik}\n",
        "    restart: unless-stopped\n",
        "    command:\n",
        "      # Decouverte par etiquettes, et RIEN par defaut.\n",
        "      - --providers.docker=true\n",
        "      - --providers.docker.exposedByDefault=false\n",
        "      - --providers.docker.network=hba-backend\n",
        "      # 80 redirige vers 443 : aucun trafic applicatif en clair.\n",
        "      - --entryPoints.web.address=:80\n",
        "      - --entryPoints.websecure.address=:443\n",
        "      - --entryPoints.web.http.redirections.entryPoint.to=websecure\n",
        "      - --entryPoints.web.http.redirections.entryPoint.scheme=https\n",
        "      # Let's Encrypt, defi HTTP-01 sur l'entree 80 laissee ouverte\n",
        "      # pour ca — la redirection ci-dessus epargne `/.well-known/`.\n",
        $"      - --certificatesResolvers.lets.acme.email={CourrielAcme}\n",
        "      - --certificatesResolvers.lets.acme.storage=/acme/acme.json\n",
        "      - --certificatesResolvers.lets.acme.httpChallenge.entryPoint=web\n",
        "      # Aucun tableau de bord : il exposerait tout le routage.\n",
        "      - --api=false\n",
        "      - --log.level=INFO\n",
        "      - --accesslog=true\n",
        "    ports:\n",
        "      - \"80:80\"\n",
        "      - \"443:443\"\n",
        "    volumes:\n",
        "      # Lecture seule — mais l'API Docker rend quand meme les variables\n",
        "      # d'environnement de tous les conteneurs. Voir l'encadre.\n",
        "      - /var/run/docker.sock:/var/run/docker.sock:ro\n",
        "      # Les certificats survivent aux redemarrages. Sans ce volume,\n",
        "      # chaque `up` redemanderait un certificat, et Let's Encrypt\n",
        "      # limite a cinq echecs par heure et par domaine.\n",
        "      - traefik-acme:/acme\n",
        "    networks:\n",
        "      - hba-backend\n",
    ];

    private static Dictionary<string, string> Ecartes
    {
        get
        {
            var tout = new Dictionary<string, string>(HorsProduction, StringComparer.Ordinal);
            foreach (var (cle, valeur) in Bloques)
            {
                tout[cle] = valeur;
            }

            return tout;
        }
    }

    // ── Le modele textuel du rendu, pour les controles d'apres-coup ──────────

    /// <summary>Un service tel qu'il apparaît dans le compose engendré.</summary>
    private sealed record ServiceRendu(
        string Nom, string Image, bool PorteBuild, string? NomConteneur,
        IReadOnlyList<string> DependDe);

    /// <summary>
    /// Relit le rendu pour en extraire les services.
    /// </summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════
    /// CE QUI EST PERDU PAR RAPPORT A LA VERSION PYTHON, ET IL FAUT LE SAVOIR.
    ///
    /// La version Python chargeait le rendu avec PyYAML. Cela donnait DEUX
    /// choses : le modèle lu ci-dessous, et la garantie que le rendu est du YAML
    /// VALIDE. Cette seconde garantie n'existe plus : cet outil n'a aucune
    /// dépendance, et écrire un analyseur YAML complet pour la retrouver serait
    /// pire que le mal.
    ///
    /// Un rendu syntaxiquement cassé passerait donc ces contrôles et échouerait
    /// au `docker compose up`, sur le VPS. Ce qui limite la portée du trou :
    /// le rendu est produit par ce fichier, à partir d'un gabarit fixe, et non
    /// par une main humaine.
    ///
    /// A RETROUVER : un `docker compose config -q` dans la CI est la vraie
    /// réponse — il valide le fichier avec l'outil qui le consommera, pas avec
    /// une seconde implémentation qui pourrait diverger.
    /// ═════════════════════════════════════════════════════════════════════
    /// </remarks>
    private static List<ServiceRendu> ServicesDuRendu(string rendu)
    {
        var services = new List<ServiceRendu>();
        var lignes = rendu.Split('\n');

        var dansServices = false;
        string? nom = null;
        var image = "";
        var porteBuild = false;
        string? nomConteneur = null;
        var dependDe = new List<string>();
        var dansDepends = false;

        void Fermer()
        {
            if (nom is not null)
            {
                services.Add(new ServiceRendu(nom, image, porteBuild, nomConteneur, dependDe));
            }

            nom = null;
            image = "";
            porteBuild = false;
            nomConteneur = null;
            dependDe = [];
            dansDepends = false;
        }

        foreach (var ligne in lignes)
        {
            if (ligne.TrimEnd() == "services:")
            {
                dansServices = true;
                continue;
            }

            if (!dansServices)
            {
                continue;
            }

            // Une clé de premier niveau clôt la section des services.
            if (ligne.Length > 0 && !ligne.StartsWith(' ') && !ligne.StartsWith('#'))
            {
                Fermer();
                dansServices = false;
                continue;
            }

            var debutService = System.Text.RegularExpressions.Regex.Match(
                ligne, @"^  ([a-z][a-z0-9._-]*):\s*$");
            if (debutService.Success)
            {
                Fermer();
                nom = debutService.Groups[1].Value;
                continue;
            }

            if (nom is null)
            {
                continue;
            }

            if (dansDepends)
            {
                var cible = System.Text.RegularExpressions.Regex.Match(
                    ligne, @"^      ([\w.-]+):\s*$");
                if (cible.Success)
                {
                    dependDe.Add(cible.Groups[1].Value);
                    continue;
                }

                if (!ligne.StartsWith("        ", StringComparison.Ordinal)
                    && ligne.Trim().Length > 0
                    && !ligne.TrimStart().StartsWith('#'))
                {
                    dansDepends = false;
                }
            }

            if (System.Text.RegularExpressions.Regex.IsMatch(ligne, @"^    depends_on:\s*$"))
            {
                dansDepends = true;
                continue;
            }

            var m = System.Text.RegularExpressions.Regex.Match(ligne, @"^    image:\s*(.*?)\s*$");
            if (m.Success)
            {
                image = m.Groups[1].Value;
                continue;
            }

            if (System.Text.RegularExpressions.Regex.IsMatch(ligne, @"^    build:\s*$"))
            {
                porteBuild = true;
                continue;
            }

            m = System.Text.RegularExpressions.Regex.Match(ligne, @"^    container_name:\s*(.*?)\s*$");
            if (m.Success)
            {
                nomConteneur = m.Groups[1].Value;
            }
        }

        Fermer();
        return services;
    }

    // ── Le generateur, et ses refus ──────────────────────────────────────────

    /// <summary>Engendre le compose de production. Rend le code de sortie.</summary>
    /// <remarks>
    /// CHAQUE CONTRÔLE D'APRÈS-COUP REFUSE D'ÉCRIRE, il ne se contente pas
    /// d'avertir. Un compose de production presque juste s'applique quand même,
    /// et la faute se découvre sur le VPS.
    /// </remarks>
    public static int Executer()
    {
        var source = Depot.Chemin("docker-compose.dev.yml");
        var sortieFichier = Depot.Chemin("docker-compose.prod.yml");

        if (!File.Exists(source))
        {
            Console.Error.WriteLine($"introuvable : {Depot.Relatif(source)}");
            return 1;
        }

        Fusions.Clear();
        ClesInternes.Clear();

        var lignes = LignesAvecFin(File.ReadAllText(source));

        var debutServices = Array.FindIndex(lignes, l => l.TrimEnd() == "services:");
        if (debutServices < 0)
        {
            Console.Error.WriteLine("le compose source n'a pas de section `services:` — "
                                    + "format inattendu");
            return 1;
        }

        var finServices = lignes.Length;
        for (var i = debutServices + 1; i < lignes.Length; i++)
        {
            if (lignes[i].Trim().Length > 0 && !lignes[i].StartsWith(' ') && !lignes[i].StartsWith('\t'))
            {
                finServices = i;
                break;
            }
        }

        var debuts = new List<int>();
        for (var i = debutServices + 1; i < finServices; i++)
        {
            if (System.Text.RegularExpressions.Regex.IsMatch(lignes[i], @"^  [a-z][a-z0-9-]*:\s*$"))
            {
                debuts.Add(i);
            }
        }

        // Les cles privees, derivees des Dockerfile — avant toute transformation,
        // car `Transformer` les consulte.
        foreach (var d in debuts)
        {
            var (nomService, corpsService) = BlocDeService(lignes, d, finServices);
            var projet = ProjetDeService(BuildDeBloc(corpsService));
            if (projet is not null)
            {
                ClesInternes[nomService] = VariableDeCle(projet);
            }
        }

        var retenus = new List<(string Nom, List<string> Corps)>();
        var ecartes = new List<(string Nom, string Raison)>();

        foreach (var d in debuts)
        {
            var (nom, corps) = BlocDeService(lignes, d, finServices);
            if (HorsProduction.TryGetValue(nom, out var horsProd))
            {
                ecartes.Add((nom, horsProd));
                continue;
            }

            if (Bloques.TryGetValue(nom, out var bloque))
            {
                ecartes.Add((nom, bloque));
                continue;
            }

            retenus.Add((nom, Transformer(nom, corps)));
        }

        if (retenus.Count == 0)
        {
            Console.Error.WriteLine("aucun service retenu — le découpage a échoué");
            return 1;
        }

        var entete = new List<string>
        {
            "# ═══════════════════════════════════════════════════════════════════════════════\n",
            "# ENGENDRÉ PAR `dotnet run --project tools/HBA.Controls -- compose-prod`.\n",
            "# NE PAS ÉDITER À LA MAIN.\n",
            "#\n",
            "# La source est `docker-compose.dev.yml`, seule description complète des vingt\n",
            "# services. Modifier un service là-bas, puis relancer : un second compose écrit\n",
            "# à la main divergerait au premier changement de variable, et la divergence ne\n",
            "# se verrait qu'en production.\n",
            "#\n",
            "# LES VALEURS NE SONT PAS ICI. Chaque secret est une référence `${VAR:?...}` :\n",
            "# Compose REFUSE de démarrer si la variable est absente, plutôt que de lancer un\n",
            "# service avec une chaîne vide. Les valeurs vivent dans un fichier d'environnement\n",
            "# hors du dépôt — voir docs/RUNBOOK-TRAEFIK.md.\n",
            "#\n",
            "# CE QUE CE FICHIER NE PORTE PAS : la création des buckets MinIO et les\n",
            "# migrations de base. Tout est dans le runbook.\n",
        };

        if (ecartes.Count > 0)
        {
            entete.Add("#\n");
            entete.Add("# SERVICES ÉCARTÉS, ET POURQUOI :\n");
            foreach (var (nom, raison) in ecartes)
            {
                entete.Add("#\n");
                entete.Add($"#   {nom}\n");
                for (var j = 0; j < raison.Length; j += 68)
                {
                    entete.Add($"#     {raison.Substring(j, Math.Min(68, raison.Length - j))}\n");
                }
            }
        }

        entete.Add("# ═══════════════════════════════════════════════════════════════════════════════\n");
        entete.Add("\n");

        // L'ANCRE DOIT PRECEDER SES ALIAS : YAML resout dans l'ordre du document.
        var ancre = AncreDeProduction(lignes);
        if (ancre.Count == 0)
        {
            Console.Error.WriteLine("aucune ancre `x-dev-auth` dans la source — les cles "
                                    + "partagees seraient perdues pour tous les services");
            return 1;
        }

        entete.AddRange(ancre);
        entete.Add("services:\n");

        var corpsTotal = new List<string>();
        foreach (var (nom, corps) in retenus)
        {
            corpsTotal.Add($"\n  {nom}:\n");
            corpsTotal.AddRange(corps);
            if (nom == ServicePublic)
            {
                corpsTotal.AddRange(EtiquettesTraefik());
            }
        }

        corpsTotal.AddRange(TraefikService());

        var queue = new List<string>
        {
            "\nvolumes:\n", "  kafka-data:\n", "  minio-data:\n",
            "  rembg-models:\n", "  traefik-acme:\n",
            "\nnetworks:\n", "  hba-backend:\n",
        };

        var rendu = string.Concat(entete.Concat(corpsTotal).Concat(queue));

        return Controler(rendu, sortieFichier);
    }

    private static int Controler(string rendu, string sortieFichier)
    {
        var lignesRendu = rendu.Split('\n');

        // Un secret de developpement qui survit a la transformation.
        var fuites = new List<string>();
        for (var i = 0; i < lignesRendu.Length; i++)
        {
            if (lignesRendu[i].TrimStart().StartsWith('#'))
            {
                continue;
            }

            foreach (var (motif, quoi) in MotifsSuspects)
            {
                if (System.Text.RegularExpressions.Regex.IsMatch(lignesRendu[i], motif))
                {
                    var cle = lignesRendu[i].Split(':')[0].Trim();
                    fuites.Add($"ligne {i + 1} : {cle} — {quoi}");
                }
            }
        }

        HashSet<string> publiables;
        try
        {
            publiables = ImagesAffectees.NomsPubliables();
        }
        catch (Exception erreur)
        {
            Console.Error.WriteLine("REFUS : impossible d'etablir la liste des images "
                                    + $"publiables ({erreur.GetType().Name}) — le controle "
                                    + "des noms d'images ne peut pas s'executer.");
            return 1;
        }

        var services = ServicesDuRendu(rendu);

        // Une image ghcr que la CI ne publie pas, et qu'aucun `build:` ne produit.
        var orphelins = services
            .Where(s => s.Image.Length > 0
                        && !ImagesTierces.Any(t => s.Image.StartsWith(t, StringComparison.Ordinal))
                        && !s.PorteBuild
                        && !publiables.Contains(s.Image.Split('/')[^1].Split(':')[0]))
            .Select(s => s.Nom)
            .ToList();

        if (orphelins.Count > 0)
        {
            Console.Error.WriteLine($"REFUS : {string.Join(", ", orphelins)} portent une image "
                                    + "que la CI ne publie pas, et aucun `build:` ne pourrait "
                                    + "la produire.");
            Console.Error.WriteLine("    Ajouter la traduction a NomsImages, ou le service a "
                                    + "ConstruitsSurPlace.");
            return 1;
        }

        // UNE DEPENDANCE VERS UN SERVICE ABSENT FAIT REFUSER LE FICHIER ENTIER.
        var definis = services.Select(s => s.Nom).ToHashSet(StringComparer.Ordinal);
        var orphelines = services
            .SelectMany(s => s.DependDe.Where(c => !definis.Contains(c)).Select(c => $"{s.Nom} -> {c}"))
            .ToList();

        if (orphelines.Count > 0)
        {
            Console.Error.WriteLine($"REFUS : {orphelines.Count} dépendance(s) vers un service "
                                    + "absent du rendu.");
            foreach (var o in orphelines)
            {
                Console.Error.WriteLine("    " + o);
            }

            Console.Error.WriteLine("    Compose refuserait le fichier entier. Ajouter le service "
                                    + "à HorsProduction ou Bloques, ou le réintégrer.");
            return 1;
        }

        var noms = services.Select(s => s.NomConteneur).ToList();
        if (noms.Any(n => n is null) || noms.Distinct().Count() != noms.Count)
        {
            var fautifs = noms.Where(n => n is null || noms.Count(x => x == n) > 1)
                .Select(n => n ?? "(absent)")
                .Distinct()
                .OrderBy(x => x, StringComparer.Ordinal);
            Console.Error.WriteLine("REFUS : noms de conteneur manquants ou en double — "
                                    + string.Join(", ", fautifs));
            return 1;
        }

        foreach (var section in new[] { "volumes:", "networks:" })
        {
            var combien = lignesRendu.Count(l => l.TrimEnd() == section);
            if (combien != 1)
            {
                Console.Error.WriteLine($"REFUS : {combien} section(s) `{section}` dans le rendu, "
                                        + "une seule attendue.");
                return 1;
            }
        }

        // UNE ANCRE DEFINIE QUE PERSONNE NE FUSIONNE NE SERT A RIEN, ET
        // L'INVERSE EST PIRE : ni cle de signature, ni cle interne, ni cle de
        // chiffrement pour le service concerne.
        var attendues = Fusions.Count;
        var obtenues = lignesRendu.Count(l => l.Trim() == "<<: *prod-auth");
        if (attendues == 0 || obtenues != attendues)
        {
            Console.Error.WriteLine($"REFUS : {attendues} service(s) fusionnaient l'ancre "
                                    + $"partagée, {obtenues} la fusionnent dans le rendu.");
            Console.Error.WriteLine("    Sans elle : ni clé de signature, ni clé interne, ni clé "
                                    + "de chiffrement.");
            return 1;
        }

        foreach (var cle in DevSeulement.Keys)
        {
            if (lignesRendu.Any(l => l.Trim().StartsWith(cle + ":", StringComparison.Ordinal)))
            {
                Console.Error.WriteLine($"REFUS : {cle} survit au rendu — ce réglage empêche le "
                                        + "démarrage hors Development.");
                return 1;
            }
        }

        var exemptes = ConstruitsSurPlace.Select(n => NomsImages.GetValueOrDefault(n, n))
            .ToHashSet(StringComparer.Ordinal);
        var prefixe = $"    image: ghcr.io/{Proprietaire}/";
        var inconnues = lignesRendu
            .Where(l => l.StartsWith(prefixe, StringComparison.Ordinal))
            .Select(l => l[prefixe.Length..].Split(':')[0])
            .Where(image => !publiables.Contains(image) && !exemptes.Contains(image))
            .ToList();

        if (inconnues.Count > 0)
        {
            Console.Error.WriteLine($"REFUS : {inconnues.Count} image(s) du rendu ne sont publiees "
                                    + "par aucun Dockerfile connu de images-affectees :");
            foreach (var image in inconnues.Distinct().OrderBy(x => x, StringComparer.Ordinal))
            {
                Console.Error.WriteLine("    " + image);
            }

            Console.Error.WriteLine("    Ajouter la traduction a NomsImages, ou le service a "
                                    + "ConstruitsSurPlace.");
            return 1;
        }

        if (fuites.Count > 0)
        {
            Console.Error.WriteLine($"REFUS : {fuites.Count} secret(s) de développement "
                                    + "survivraient à la transformation.");
            foreach (var f in fuites)
            {
                Console.Error.WriteLine("    " + f);
            }

            Console.Error.WriteLine("Ajouter la clé concernée à Secrets, puis relancer.");
            return 1;
        }

        File.WriteAllText(sortieFichier, rendu);

        Console.WriteLine($"{services.Count} service(s) dans le rendu.");
        Console.WriteLine($"ecrit : {Depot.Relatif(sortieFichier)}");
        Console.WriteLine("aucun secret de développement n'a survécu au contrôle.");
        Console.WriteLine();
        Console.WriteLine("Ce qui n'est PAS couvert : la VALIDITÉ YAML du rendu. La version "
                          + "Python la tenait via PyYAML ;");
        Console.WriteLine("cet outil n'a aucune dépendance. `docker compose config -q` sur le "
                          + "fichier engendré est la vraie réponse.");
        return 0;
    }
}
