using System.Text.RegularExpressions;

namespace HBA.Controls.Controles;

/// <summary>
/// Départ à froid : chaque migration tient-elle sur une base VIDE ?
/// </summary>
/// <remarks>
/// ═══════════════════════════════════════════════════════════════════════════
/// POURQUOI CE CONTRÔLE EXISTE.
///
/// Une migration écrite à la main l'est presque toujours en REGARDANT une base
/// existante. On y voit la colonne, on la renomme, la migration passe. Elle ne
/// passera plus jamais ailleurs si la colonne n'existe qu'à cet endroit-là.
///
/// C'est ce qui s'est produit sur food-service :
/// `20260814000000_RepriseImagesVersMedia` renommait `restaurants.LogoUrl` en
/// `LegacyLogoUrl` alors que `20260812121955_ImagesVersMedia` l'avait DÉJÀ fait
/// deux jours plus tôt. Sur la base de développement de l'époque, la première
/// n'avait pas encore tourné. Sur une base neuve, la seconde tombe sur
///
///     42703: column "LogoUrl" does not exist
///
/// et le service ne démarre pas — les migrations sont appliquées AVANT
/// l'ouverture du port, délibérément.
///
/// CE QUE LE COMPILATEUR NE VOIT PAS. Une migration est du C# valide quoi
/// qu'elle raconte : `RenameColumn("Inexistant")` compile. L'erreur n'apparaît
/// qu'à l'exécution, sur une base dans le bon état — c'est-à-dire tard, et sur
/// la machine de quelqu'un d'autre.
///
/// Ce contrôle rejoue donc les migrations À SEC, dans l'ordre, en tenant la
/// liste des colonnes existantes. Il ne remplace pas un vrai départ à froid ; il
/// attrape la classe d'erreurs qui ne se manifeste QUE là.
///
/// TROIS CHOSES ONT ÉTÉ AJOUTÉES AU LOT 9.4, ET LA PREMIÈRE EST UN AVEU.
///
///   • <see cref="CasseDesIdentifiantsSql"/> ne voyait que 19 blocs SQL sur
///     232 : il ne connaissait que la forme à triple guillemet, alors que le
///     dépôt écrit son SQL à 146 exemplaires en littéral VERBATIM. Il passait
///     donc à côté de 94 % de ce qu'il prétendait lire — un contrôle qui
///     rassure sans rien vérifier, exactement ce que cet encadré dénonce.
///
///   • <see cref="EntitesDuSnapshot"/> compare le ModelSnapshot au CODE. C'est
///     ce qui manquait quand `DeliveriesDbContextModelSnapshot` a gardé trois
///     agrégats dont le namespace n'existait plus : le prochain
///     `migrations add` aurait généré, TOUT SEUL, une migration supprimant
///     quatre tables — une suppression de données produite par un outil, pas
///     par une décision.
///
///   • Le rejeu à froid, lui, ne change pas : il compare les migrations ENTRE
///     ELLES. Ni l'un ni l'autre ne lit la configuration EF — pour cela il
///     faudrait démarrer l'application.
///
/// UNE CONFIGURATION PARTAGÉE CRÉE UNE TABLE TOUT AUSSI RÉELLE.
///
/// `ConsumerInboxConfiguration` et `IdempotencyConfiguration` vivent dans
/// `shared/common/HBA.Shared.Infrastructure`, et quatre DbContext les
/// appliquent. Ce contrôle ne regardait que `services/` : les huit tables
/// manquantes étaient donc invisibles, alors que leur absence est exactement la
/// panne qu'il existe pour attraper — le service démarre, le premier événement
/// consommé lève « relation "consumer_inbox" does not exist », et le message
/// part en boucle de rejeu.
///
/// Un dossier `Migrations` = un DbContext = un historique. Les services à
/// plusieurs contextes (financial, engagement, communication) sont donc traités
/// séparément, ce qui évite de confondre trois tables `outbox_messages`
/// distinctes.
///
/// CE QUE LE PORTAGE A CHANGÉ, ET C'EST PEU.
///
/// `check-migrations.py` ne dépendait d'AUCUNE bibliothèque tierce : il n'y a
/// donc rien à regretter ici, contrairement aux contrôles infra et k8s. Deux
/// écarts seulement :
///
///   · le mode `--services-en-defaut`, qui rendait la liste que
///     `db/add-missing-migrations.sh` tenait à la main, n'est PAS porté : un
///     contrôle rend un <see cref="Verdict"/>, il n'imprime rien. La même
///     information se lit dans les fautes « table configurée … aucune migration
///     ne la crée » ;
///   · le balayage des types du dépôt passe par <see cref="Depot.Fichiers"/>,
///     qui écarte aussi `.vs`, `TestResults` et tout dossier commençant par un
///     point. Le Python n'écartait que `.git`. Un type déclaré dans un tel
///     dossier serait donc vu comme introuvable — aucun n'existe ici.
/// ═══════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed class MigrationsControle : IControle
{
    /// <inheritdoc/>
    public string Nom => "migrations";

    /// <inheritdoc/>
    public string Resume => "chaque migration tient sur une base vide, dans l'ordre";

    private static readonly Regex Appel = new(
        @"migrationBuilder\.(CreateTable|AddColumn|RenameColumn|DropColumn"
        + @"|AlterColumn|DropTable|RenameTable)\b",
        RegexOptions.Compiled);

    private static readonly Regex ColonneDeTable = new(
        @"(\w+)\s*=\s*table\.Column<", RegexOptions.Compiled);

    private static readonly Regex TableCreee = new(
        @"CreateTable\(\s*name:\s*""([a-z_0-9]+)""", RegexOptions.Compiled);

    private static readonly Regex TableRenommee = new(
        @"RenameTable\([^)]*newName:\s*""([a-z_0-9]+)""", RegexOptions.Compiled);

    private static readonly Regex ColonneAjoutee = new(
        @"(AddColumn<[^>]*>|RenameColumn)\(([^;]*?)\)\s*;",
        RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex VersUneTable = new(
        @"\.ToTable\(\s*""([a-z_0-9]+)""", RegexOptions.Compiled);

    private static readonly Regex ConfigurationAppliquee = new(
        @"ApplyConfiguration\(\s*new\s+(\w+Configuration)\s*\(", RegexOptions.Compiled);

    private static readonly Regex IdentifiantCite = new(
        @"""([A-Za-z_][A-Za-z_0-9]*)""", RegexOptions.Compiled);

    private static readonly Regex EspaceDeNoms = new(
        @"^\s*namespace\s+([\w\.]+)", RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex DeclarationDeType = new(
        @"^\s*(?:public|internal|private|protected)?\s*"
        + @"(?:sealed\s+|abstract\s+|static\s+|partial\s+|readonly\s+|record\s+)*"
        + @"(?:class|record|struct|interface)\s+(\w+)",
        RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex EntiteDuSnapshot = new(
        @"(?:modelBuilder\.Entity|b\d*\.OwnsOne|b\d*\.OwnsMany)\(\s*""([\w\.]+)""",
        RegexOptions.Compiled);

    /// <inheritdoc/>
    public Verdict Executer()
    {
        var fautes = new List<string>();
        var constats = new List<string>();
        var nonCouvert = new List<string>
        {
            "le `migrationBuilder.Sql(...)` brut n'est PAS analysé — comprendre du SQL "
            + "arbitraire dépasse ce que ce contrôle prétend faire. Les migrations de "
            + "reprise de données passent souvent par là et restent à vérifier à la main. "
            + "Seuls leurs identifiants entre guillemets sont relus, et uniquement pour la "
            + "question de la CASSE",
            "les tables héritées d'un module extrait, dont le `CreateTable` n'est pas dans "
            + "CES migrations : les juger produirait un faux positif à chaque colonne, et "
            + "le bruit tuerait le contrôle. Une colonne absente y passe donc inaperçue",
            "que le snapshot décrive toutes les COLONNES du modèle, ni leurs types : seule "
            + "l'existence des TYPES est comparée. Vérifier les colonnes demanderait de "
            + "rejouer la configuration EF, c'est-à-dire de démarrer l'application",
            "la configuration EF elle-même : ni le rejeu à froid ni la comparaison au "
            + "snapshot ne la lisent. Un `HasColumnName` posé par convention, une "
            + "configuration appliquée autrement que par `ApplyConfiguration(new X())`, "
            + "restent invisibles",
            "la lecture est textuelle et par expression régulière, pas un analyseur C# : "
            + "un appel `migrationBuilder` construit dynamiquement, ou un argument nommé "
            + "dont la valeur n'est pas un littéral, n'est pas vu",
        };

        var racineServices = Depot.Dossier("services");
        var partagees = TablesDesConfigurationsPartagees();
        var typesConnus = TypesDeclares();
        var contextes = 0;

        foreach (var service in Services(racineServices))
        {
            var dossierService = Path.Combine(racineServices, service);

            // ── 1. Le rejeu à sec, un dossier `Migrations` à la fois ─────────
            foreach (var dossier in DossiersDeMigrations(dossierService))
            {
                var fichiers = Directory.EnumerateFiles(dossier, "*.cs")
                    .Select(Path.GetFileName)
                    .Where(n => n is not null && !n.Contains("Designer", StringComparison.Ordinal)
                                && !n.Contains("Snapshot", StringComparison.Ordinal))
                    .Select(n => n!)
                    .OrderBy(n => n, StringComparer.Ordinal)
                    .ToList();

                if (fichiers.Count == 0)
                {
                    continue;
                }

                contextes++;

                foreach (var (fichier, message) in Rejouer(dossier, fichiers))
                {
                    fautes.Add($"{service} : {fichier} — {message}");
                }
            }

            // ── 2. Une configuration sans migration ─────────────────────────
            var configurees = TablesConfigurees(dossierService, partagees);
            var creees = TablesCreees(dossierService);

            foreach (var (table, source) in configurees
                         .Where(t => !creees.Contains(t.Key))
                         .OrderBy(t => t.Key, StringComparer.Ordinal))
            {
                fautes.Add(
                    $"{service} : table « {table} » configurée ({source}) et AUCUNE "
                    + "migration ne la crée. Le code compile, les tests de domaine "
                    + "passent, le service démarre — jusqu'à la première requête.");
            }

            // ── 3. La casse des identifiants dans le SQL brut ────────────────
            foreach (var (fichier, message) in CasseDesIdentifiantsSql(dossierService))
            {
                fautes.Add($"{service} : {fichier} — {message}");
            }

            // ── 4. Le snapshot désigne-t-il des types qui existent ? ─────────
            foreach (var (fichier, message) in EntitesDuSnapshot(dossierService, typesConnus))
            {
                fautes.Add(
                    $"{service} : {fichier} — {message} ; le prochain `migrations add` "
                    + "generera une SUPPRESSION de table");
            }
        }

        if (contextes == 0)
        {
            // UN ZÉRO ICI NE VEUT PAS DIRE « TOUT VA BIEN ». C'est le défaut que
            // `Depot.Dossier` ferme pour les chemins ; ici c'est le CRITÈRE qui
            // pourrait ne plus rien trouver, et il faut le dire en faute.
            fautes.Add(
                "aucun dossier `Migrations` trouvé sous services/ : ce contrôle n'a RIEN "
                + "rejoué, et son zéro ne dit rien de l'état du dépôt.");
        }

        constats.Add(
            $"{contextes} contexte(s) rejoué(s), {fautes.Count} incohérence(s) de départ "
            + "à froid.");

        return new Verdict(fautes, constats, nonCouvert);
    }

    /// <summary>Les services, sous la forme « univers/nom-du-service ».</summary>
    /// <remarks>
    /// « src/services » N'A JAMAIS EXISTÉ ICI, et les services sont rangés par
    /// univers. Sans cette énumération à DEUX niveaux, le script d'origine levait
    /// un FileNotFoundError au lieu de vérifier quoi que ce soit.
    /// </remarks>
    private static List<string> Services(string racine)
    {
        var trouves = new List<string>();
        foreach (var univers in Directory.EnumerateDirectories(racine)
                     .OrderBy(d => d, StringComparer.Ordinal))
        {
            foreach (var service in Directory.EnumerateDirectories(univers)
                         .OrderBy(d => d, StringComparer.Ordinal))
            {
                trouves.Add(
                    Path.GetFileName(univers) + "/" + Path.GetFileName(service));
            }
        }

        return trouves;
    }

    /// <summary>Les dossiers nommés exactement `Migrations` sous un service.</summary>
    private static List<string> DossiersDeMigrations(string dossierService)
    {
        if (!Directory.Exists(dossierService))
        {
            return [];
        }

        return Directory
            .EnumerateDirectories(dossierService, "*", SearchOption.AllDirectories)
            .Where(d => Path.GetFileName(d) == "Migrations")
            .Where(d => !Ignore(d))
            .OrderBy(d => d, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Le chemin passe-t-il par un dossier qu'aucun contrôle ne lit ?</summary>
    private static bool Ignore(string chemin)
        => chemin.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(s => Depot.Ignores.Contains(s));

    /// <summary>
    /// Rejoue un historique de migrations à sec, dans l'ordre d'application.
    /// </summary>
    /// <remarks>
    /// LE TRI LEXICOGRAPHIQUE **EST** L'ORDRE D'APPLICATION : le préfixe des
    /// migrations EF est un horodatage à largeur fixe. Seul `Up()` compte —
    /// `Down()` s'exécute dans un autre état et n'est jamais joué au démarrage.
    /// </remarks>
    private static List<(string Fichier, string Message)> Rejouer(
        string dossier, IReadOnlyList<string> fichiers)
    {
        var problemes = new List<(string, string)>();
        var colonnes = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var tables = new HashSet<string>(StringComparer.Ordinal);

        HashSet<string> Colonnes(string table)
        {
            if (!colonnes.TryGetValue(table, out var jeu))
            {
                jeu = new HashSet<string>(StringComparer.Ordinal);
                colonnes[table] = jeu;
            }

            return jeu;
        }

        foreach (var nom in fichiers)
        {
            var source = File.ReadAllText(Path.Combine(dossier, nom));

            var debut = source.IndexOf("void Up(", StringComparison.Ordinal);
            var fin = source.IndexOf("void Down(", StringComparison.Ordinal);
            if (debut < 0)
            {
                continue;
            }

            var haut = fin > debut ? source[debut..fin] : source[debut..];

            var position = 0;
            while (true)
            {
                var appel = Appel.Match(haut, position);
                if (!appel.Success)
                {
                    break;
                }

                var genre = appel.Groups[1].Value;
                var (texte, apres) = Arguments(haut, appel.Index);
                position = apres;

                if (genre == "CreateTable")
                {
                    var table = Nomme(texte, "name");
                    if (table is not null)
                    {
                        tables.Add(table);
                        foreach (Match colonne in ColonneDeTable.Matches(texte))
                        {
                            Colonnes(table).Add(colonne.Groups[1].Value);
                        }
                    }
                }
                else if (genre == "AddColumn")
                {
                    var table = Nomme(texte, "table");
                    var colonne = Nomme(texte, "name");
                    if (table is not null && colonne is not null)
                    {
                        if (tables.Contains(table) && Colonnes(table).Contains(colonne))
                        {
                            problemes.Add((nom,
                                $"AddColumn {table}.« {colonne} » — colonne déjà présente"));
                        }

                        Colonnes(table).Add(colonne);
                    }
                }
                else if (genre == "RenameColumn")
                {
                    var table = Nomme(texte, "table");
                    var colonne = Nomme(texte, "name");
                    var neuf = Nomme(texte, "newName");
                    if (table is not null && colonne is not null)
                    {
                        // ON NE SIGNALE QUE LES TABLES CRÉÉES PAR CES MIGRATIONS.
                        // Une table héritée d'un module extrait n'a pas son
                        // `CreateTable` ici : la juger produirait un faux positif à
                        // chaque colonne, et le bruit tuerait le contrôle.
                        if (tables.Contains(table) && !Colonnes(table).Contains(colonne))
                        {
                            problemes.Add((nom,
                                $"RenameColumn {table}.« {colonne} » — colonne absente à "
                                + "ce stade ; sur une base neuve PostgreSQL rend 42703 et "
                                + "le service ne démarre pas"));
                        }

                        Colonnes(table).Remove(colonne);
                        if (neuf is not null)
                        {
                            Colonnes(table).Add(neuf);
                        }
                    }
                }
                else if (genre == "DropColumn" || genre == "AlterColumn")
                {
                    var table = Nomme(texte, "table");
                    var colonne = Nomme(texte, "name");
                    if (table is not null && colonne is not null)
                    {
                        if (tables.Contains(table) && !Colonnes(table).Contains(colonne))
                        {
                            problemes.Add((nom,
                                $"{genre} {table}.« {colonne} » — colonne absente à ce stade"));
                        }

                        if (genre == "DropColumn")
                        {
                            Colonnes(table).Remove(colonne);
                        }
                    }
                }
                else if (genre == "RenameTable")
                {
                    var table = Nomme(texte, "name");
                    var neuf = Nomme(texte, "newName");
                    if (table is not null && neuf is not null && tables.Contains(table))
                    {
                        tables.Remove(table);
                        tables.Add(neuf);
                        colonnes[neuf] = colonnes.TryGetValue(table, out var jeu)
                            ? jeu
                            : new HashSet<string>(StringComparer.Ordinal);
                        colonnes.Remove(table);
                    }
                }
                else if (genre == "DropTable")
                {
                    var table = Nomme(texte, "name");
                    if (table is not null)
                    {
                        tables.Remove(table);
                        colonnes.Remove(table);
                    }
                }
            }
        }

        return problemes;
    }

    /// <summary>Le texte entre les parenthèses de l'appel qui commence à `depart`.</summary>
    /// <remarks>
    /// UNE EXPRESSION RÉGULIÈRE NE SUFFIT PAS : `CreateTable` porte des
    /// parenthèses imbriquées sur plusieurs dizaines de lignes. On compte donc la
    /// profondeur.
    /// </remarks>
    private static (string Texte, int Apres) Arguments(string source, int depart)
    {
        var i = source.IndexOf('(', depart);
        if (i < 0)
        {
            return (string.Empty, source.Length);
        }

        var profondeur = 0;
        for (var j = i; j < source.Length; j++)
        {
            if (source[j] == '(')
            {
                profondeur++;
            }
            else if (source[j] == ')')
            {
                profondeur--;
                if (profondeur == 0)
                {
                    return (source[(i + 1)..j], j);
                }
            }
        }

        return (source[(i + 1)..], source.Length);
    }

    /// <summary>La valeur littérale d'un argument nommé.</summary>
    private static string? Nomme(string texte, string cle)
    {
        var trouve = Regex.Match(texte, @"\b" + Regex.Escape(cle) + @":\s*""([^""]*)""");
        return trouve.Success ? trouve.Groups[1].Value : null;
    }

    /// <summary>
    /// `{ NomDeClasseConfiguration → (table, fichier) }` pour tout `shared/`.
    /// </summary>
    private static Dictionary<string, (string Table, string Fichier)> TablesDesConfigurationsPartagees()
    {
        var trouvees = new Dictionary<string, (string, string)>(StringComparer.Ordinal);

        foreach (var fichier in Depot.Fichiers(Depot.Dossier("shared"), ".cs")
                     .OrderBy(f => f, StringComparer.Ordinal))
        {
            var nom = Path.GetFileName(fichier);
            if (!nom.EndsWith("Configuration.cs", StringComparison.Ordinal))
            {
                continue;
            }

            var premiere = VersUneTable.Match(File.ReadAllText(fichier));
            if (premiere.Success)
            {
                trouvees[nom[..^3]] = (premiere.Groups[1].Value, nom);
            }
        }

        return trouvees;
    }

    /// <summary>Tables déclarées par une configuration EF : `ToTable("nom")`.</summary>
    /// <remarks>
    /// LES CONFIGURATIONS QU'UN SERVICE APPLIQUE SANS LES HÉBERGER COMPTENT AUSSI.
    /// Quatre DbContext appliquent `ConsumerInboxConfiguration` et
    /// `IdempotencyConfiguration` de `shared/` : chacun a besoin des tables dans
    /// SON schéma, et ce contrôle ne regardait que `services/`.
    /// </remarks>
    private static Dictionary<string, string> TablesConfigurees(
        string dossierService,
        IReadOnlyDictionary<string, (string Table, string Fichier)> partagees)
    {
        var tables = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!Directory.Exists(dossierService))
        {
            return tables;
        }

        foreach (var fichier in Depot.Fichiers(dossierService, ".cs")
                     .OrderBy(f => f, StringComparer.Ordinal))
        {
            // Les migrations décrivent le schéma, elles ne le CONFIGURENT pas :
            // les lire ferait passer chaque table pour « configurée ».
            var chemin = "/" + Depot.Relatif(fichier).Replace('\\', '/') + "/";
            if (chemin.Contains("/Migrations/", StringComparison.Ordinal))
            {
                continue;
            }

            var nom = Path.GetFileName(fichier);
            var contenu = File.ReadAllText(fichier);

            foreach (Match table in VersUneTable.Matches(contenu))
            {
                tables.TryAdd(table.Groups[1].Value, nom);
            }

            foreach (Match classe in ConfigurationAppliquee.Matches(contenu))
            {
                if (partagees.TryGetValue(classe.Groups[1].Value, out var partagee))
                {
                    tables.TryAdd(
                        partagee.Table,
                        $"{partagee.Fichier} (partagée, appliquée par {nom})");
                }
            }
        }

        return tables;
    }

    /// <summary>Tables créées par une migration : `CreateTable(name: "nom"`.</summary>
    private static HashSet<string> TablesCreees(string dossierService)
    {
        var tables = new HashSet<string>(StringComparer.Ordinal);

        foreach (var fichier in FichiersDeMigration(dossierService))
        {
            var contenu = File.ReadAllText(fichier);
            foreach (Match table in TableCreee.Matches(contenu))
            {
                tables.Add(table.Groups[1].Value);
            }

            // Une table peut aussi apparaître par renommage.
            foreach (Match table in TableRenommee.Matches(contenu))
            {
                tables.Add(table.Groups[1].Value);
            }
        }

        return tables;
    }

    /// <summary>Les fichiers de migration d'un service, snapshots et designers exclus.</summary>
    private static IEnumerable<string> FichiersDeMigration(string dossierService)
    {
        foreach (var dossier in DossiersDeMigrations(dossierService))
        {
            foreach (var fichier in Directory.EnumerateFiles(dossier, "*.cs")
                         .OrderBy(f => f, StringComparer.Ordinal))
            {
                var nom = Path.GetFileName(fichier);
                if (nom.Contains("Designer", StringComparison.Ordinal)
                    || nom.Contains("Snapshot", StringComparison.Ordinal))
                {
                    continue;
                }

                yield return fichier;
            }
        }
    }

    /// <summary>Toutes les colonnes créées par les migrations d'un service.</summary>
    private static HashSet<string> ColonnesDeclarees(string dossierService)
    {
        var colonnes = new HashSet<string>(StringComparer.Ordinal);

        foreach (var fichier in FichiersDeMigration(dossierService))
        {
            var contenu = File.ReadAllText(fichier);
            foreach (Match colonne in ColonneDeTable.Matches(contenu))
            {
                colonnes.Add(colonne.Groups[1].Value);
            }

            foreach (Match appel in ColonneAjoutee.Matches(contenu))
            {
                foreach (var cle in new[] { "name", "newName" })
                {
                    var trouve = Nomme(appel.Groups[2].Value, cle);
                    if (trouve is not null)
                    {
                        colonnes.Add(trouve);
                    }
                }
            }
        }

        return colonnes;
    }

    /// <summary>
    /// Le corps de chaque `migrationBuilder.Sql(...)`, quelle que soit la forme
    /// du littéral.
    /// </summary>
    /// <remarks>
    /// ATTENTION : LE CONTRÔLE NE VOYAIT QUE 19 BLOCS SUR 232. Il cherchait
    /// uniquement la forme brute à triple guillemet. Or le dépôt écrit son SQL à
    /// 146 exemplaires en littéral VERBATIM et à 11 en littéral ordinaire.
    ///
    /// ET LES TROIS FORMES N'ÉCHAPPENT PAS LES GUILLEMETS PAREIL — ce qui compte
    /// précisément ici, puisqu'on cherche des IDENTIFIANTS ENTRE GUILLEMETS :
    /// dans un littéral brut rien n'est échappé, dans un verbatim un guillemet se
    /// DOUBLE, dans un ordinaire il s'échappe par une contre-oblique. Les rendre
    /// tous sous la même forme est la seule façon d'appliquer ensuite un seul
    /// motif de recherche.
    /// </remarks>
    private static List<string> BlocsSql(string contenu)
    {
        const string Marqueur = "migrationBuilder.Sql(";
        const string Triple = "\"\"\"";
        var resultats = new List<string>();
        var i = 0;

        while (true)
        {
            i = contenu.IndexOf(Marqueur, i, StringComparison.Ordinal);
            if (i < 0)
            {
                return resultats;
            }

            var j = i + Marqueur.Length;
            while (j < contenu.Length && (contenu[j] == ' ' || contenu[j] == '\t'
                                          || contenu[j] == '\r' || contenu[j] == '\n'))
            {
                j++;
            }

            if (string.CompareOrdinal(contenu, j, Triple, 0, 3) == 0)
            {
                var fin = contenu.IndexOf(Triple, j + 3, StringComparison.Ordinal);
                if (fin < 0)
                {
                    i = j;
                    continue;
                }

                resultats.Add(contenu[(j + 3)..fin]);
                i = fin + 3;
            }
            else if (string.CompareOrdinal(contenu, j, "@\"", 0, 2) == 0)
            {
                var k = j + 2;
                while (k < contenu.Length)
                {
                    if (contenu[k] == '"')
                    {
                        if (k + 1 < contenu.Length && contenu[k + 1] == '"')
                        {
                            k += 2;
                            continue;
                        }

                        break;
                    }

                    k++;
                }

                resultats.Add(contenu[(j + 2)..Math.Min(k, contenu.Length)]
                    .Replace("\"\"", "\""));
                i = k + 1;
            }
            else if (j < contenu.Length && contenu[j] == '"')
            {
                var k = j + 1;
                while (k < contenu.Length)
                {
                    if (contenu[k] == '\\')
                    {
                        k += 2;
                        continue;
                    }

                    if (contenu[k] == '"')
                    {
                        break;
                    }

                    k++;
                }

                resultats.Add(contenu[(j + 1)..Math.Min(k, contenu.Length)]
                    .Replace("\\\"", "\""));
                i = k + 1;
            }
            else
            {
                i = j;
            }
        }
    }

    /// <summary>
    /// POSTGRESQL DISTINGUE LA CASSE DÈS QU'ON MET DES GUILLEMETS.
    /// </summary>
    /// <remarks>
    /// `se."Metadata"` et `se."metadata"` sont deux colonnes différentes. Une
    /// migration de reprise écrite à la main a désigné la première alors que la
    /// configuration EF mappe la seconde (`HasColumnName("metadata")`) — et
    /// PostgreSQL l'a dit lui-même, à l'exécution, sur une base neuve :
    ///
    ///     42703: column se.Metadata does not exist
    ///     Hint: Perhaps you meant to reference the column "se.metadata".
    ///
    /// ON NE PRÉTEND PAS ANALYSER DU SQL. On cherche UNIQUEMENT les identifiants
    /// entre guillemets qui ne correspondent à aucune colonne connue mais qui en
    /// égalent une À LA CASSE PRÈS. Un identifiant totalement inconnu est ignoré :
    /// il vient d'un autre schéma, d'un alias ou d'une expression, et le signaler
    /// noierait le vrai constat.
    /// </remarks>
    private static List<(string Fichier, string Message)> CasseDesIdentifiantsSql(
        string dossierService)
    {
        var colonnes = ColonnesDeclarees(dossierService);
        var minuscules = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var colonne in colonnes)
        {
            minuscules[colonne.ToLowerInvariant()] = colonne;
        }

        var problemes = new List<(string, string)>();

        foreach (var fichier in FichiersDeMigration(dossierService))
        {
            var nom = Path.GetFileName(fichier);
            foreach (var bloc in BlocsSql(File.ReadAllText(fichier)))
            {
                var vus = new HashSet<string>(StringComparer.Ordinal);
                foreach (Match identifiant in IdentifiantCite.Matches(bloc))
                {
                    var texte = identifiant.Groups[1].Value;
                    if (!vus.Add(texte) || colonnes.Contains(texte))
                    {
                        continue;
                    }

                    if (minuscules.TryGetValue(texte.ToLowerInvariant(), out var attendue))
                    {
                        problemes.Add((nom,
                            $"« {texte} » dans du SQL brut — la colonne s'appelle "
                            + $"« {attendue} »"));
                    }
                }
            }
        }

        return problemes;
    }

    /// <summary>Tous les types du dépôt, sous la forme « Namespace.Type ».</summary>
    /// <remarks>
    /// ON ÉCARTE LES SNAPSHOTS ET LES DESIGNERS PAR LEUR SUFFIXE, PAS PAR
    /// « Snapshot dans le nom ». Le premier jet écartait tout fichier dont le NOM
    /// contenait « Snapshot » — donc `PolicySnapshot.cs`, un value object
    /// parfaitement vivant. Le contrôle a immédiatement annoncé que le snapshot de
    /// return-refund déclarait un type introuvable : un faux positif fabriqué par
    /// son propre filtre, sur le premier dépôt venu.
    /// </remarks>
    private static HashSet<string> TypesDeclares()
    {
        var trouves = new HashSet<string>(StringComparer.Ordinal);

        foreach (var fichier in Depot.Fichiers(Depot.Racine, ".cs"))
        {
            var nom = Path.GetFileName(fichier);
            if (nom.EndsWith("ModelSnapshot.cs", StringComparison.Ordinal)
                || nom.EndsWith(".Designer.cs", StringComparison.Ordinal))
            {
                continue;
            }

            var contenu = File.ReadAllText(fichier);
            var espaces = EspaceDeNoms.Matches(contenu)
                .Select(m => m.Groups[1].Value)
                .ToList();
            if (espaces.Count == 0)
            {
                continue;
            }

            foreach (Match type in DeclarationDeType.Matches(contenu))
            {
                foreach (var espace in espaces)
                {
                    trouves.Add(espace + "." + type.Groups[1].Value);
                }
            }
        }

        return trouves;
    }

    /// <summary>
    /// LE SNAPSHOT PEUT DÉCRIRE DES TYPES QUI N'EXISTENT PLUS.
    /// </summary>
    /// <remarks>
    /// `DeliveriesDbContextModelSnapshot` déclarait trois agrégats —
    /// DeliveryQuote, DeliveryZone et PricingRule — sous un namespace INTROUVABLE
    /// dans tout le dépôt. Le domaine de tarification avait été déplacé vers
    /// delivery-pricing-service ; le code était parti, le snapshot était resté.
    ///
    /// CE QUE CET ÉCART COÛTE : le prochain `dotnet ef migrations add` sur ce
    /// contexte génère, tout seul, une migration qui SUPPRIME les tables
    /// correspondantes — au milieu d'un diff portant sur autre chose, sans que
    /// personne l'ait demandé.
    ///
    /// Aucun autre contrôle ne le voit : le rejeu à froid compare les migrations
    /// ENTRE ELLES, jamais au modèle.
    /// </remarks>
    private static List<(string Fichier, string Message)> EntitesDuSnapshot(
        string dossierService, HashSet<string> connus)
    {
        var problemes = new List<(string, string)>();
        if (!Directory.Exists(dossierService))
        {
            return problemes;
        }

        foreach (var fichier in Depot.Fichiers(dossierService, ".cs")
                     .OrderBy(f => f, StringComparer.Ordinal))
        {
            var nom = Path.GetFileName(fichier);
            if (!nom.EndsWith("ModelSnapshot.cs", StringComparison.Ordinal))
            {
                continue;
            }

            var contenu = File.ReadAllText(fichier);
            var pleins = EntiteDuSnapshot.Matches(contenu)
                .Select(m => m.Groups[1].Value)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(x => x, StringComparer.Ordinal);

            foreach (var plein in pleins)
            {
                // Un type d'entité sans point est un sac de propriétés EF, sans
                // classe CLR : il n'y a rien à chercher dans le code.
                if (!plein.Contains('.') || connus.Contains(plein))
                {
                    continue;
                }

                problemes.Add((nom,
                    $"le snapshot déclare « {plein} », introuvable dans le dépôt"));
            }
        }

        return problemes;
    }
}
