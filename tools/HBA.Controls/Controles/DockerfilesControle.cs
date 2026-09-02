using System.Text.RegularExpressions;

namespace HBA.Controls.Controles;

/// <summary>
/// Fermeture transitive des <c>COPY</c> : ce que chaque image oublie
/// d'embarquer.
/// </summary>
/// <remarks>
/// ═══════════════════════════════════════════════════════════════════════════
/// LA RESTAURATION RÉUSSIT, LA COMPILATION TOMBE, ET LE MESSAGE MENT.
///
/// Chaque Dockerfile copie `shared` puis les quelques `*.Contracts` d'autres
/// services dont il a besoin. Le piège : un projet de `shared` peut lui-même
/// référencer un projet qui vit DANS un service — c'est le cas de tous les
/// clients gRPC, qui référencent les contrats du service qu'ils appellent.
///
/// Personne n'écrit cette dépendance : elle est transitive. Et son absence ne
/// produit pas d'erreur franche :
///
///     warning MSB9008: the referenced project does not exist
///     error CS0234: le namespace 'Contracts' n'existe pas dans 'HBA.Orders'
///
/// Le premier n'est qu'un AVERTISSEMENT, noyé dans la sortie. Le second envoie
/// chercher un problème d'espace de noms dans du code qui compile parfaitement en
/// local. On a perdu deux constructions là-dessus — commerce-service, puis
/// communication-service, la seconde causée par l'ajout d'un simple client gRPC.
///
/// CE CONTRÔLE COÛTE UNE SECONDE, UNE CONSTRUCTION D'IMAGE EN COÛTE CENT.
///
/// Il part du projet nommé par `dotnet restore`, suit toutes les
/// `ProjectReference` de proche en proche, et vérifie que chaque projet atteint
/// tombe bien dans l'un des chemins `COPY` du Dockerfile.
///
/// `apps/` A ÉTÉ AJOUTÉ APRÈS COUP, ET SON ABSENCE A LAISSÉ PASSER UNE PANNE.
///
/// Le script d'origine ne parcourait que `services/`. La passerelle n'ayant
/// longtemps dépendu d'aucun projet partagé, cela ne se voyait pas — jusqu'à ce
/// que le contrôle de révocation (ISSUE-022) lui donne sa première référence vers
/// `shared/`, que son Dockerfile ne copiait pas. `dotnet build` local ne pouvait
/// rien en dire : il voit tout le dépôt. Seul un `docker build` l'aurait montré.
///
/// Depuis, `apps/` est parcouru comme `services/`, et par
/// <see cref="Depot.Dossier"/> : sa disparition LÈVE au lieu de rendre « 21
/// Dockerfile(s) vérifié(s) » sur vingt.
///
/// LE MÊME DÉFAUT, UN CRAN PLUS TÔT : `src/services` n'existe plus depuis la
/// réorganisation, et les services sont rangés par univers. Le script levait un
/// `FileNotFoundError` que `check-all.sh` affichait comme un échec de contrôle
/// ordinaire.
///
/// LES `ARG` SONT SUBSTITUÉS AVANT LECTURE, ET C'EST INDISPENSABLE.
///
/// `apps/api-gateway/Dockerfile` écrit `COPY ${BFF}/src/...` et
/// `dotnet restore ${BFF}/src/...`. Comparer ces chaînes telles quelles à des
/// chemins du dépôt ne rend jamais vrai : le contrôle passerait en annonçant zéro
/// problème sur un fichier qu'il n'a pas compris. Un contrôle qui se tait à tort
/// est pire que pas de contrôle.
///
/// CE QU'IL NE VÉRIFIE PAS : il ne construit aucune image. Un `COPY` présent mais
/// annulé par `.dockerignore` reste invisible ici, et le contexte de construction
/// n'est pas évalué.
/// ═══════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed class DockerfilesControle : IControle
{
    /// <inheritdoc/>
    public string Nom => "dockerfiles";

    /// <inheritdoc/>
    public string Resume => "chaque image copie tous les projets qu'elle compile, transitivement";

    private static readonly Regex Argument = new(
        @"^ARG\s+(\w+)=(\S+)", RegexOptions.Compiled | RegexOptions.Multiline);

    // `COPY --from=…` recopie une étape précédente, pas le dépôt : hors sujet.
    private static readonly Regex Copie = new(
        @"^COPY\s+(\S+)\s+\S+\s*$", RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex Restauration = new(
        @"dotnet restore (\S+)", RegexOptions.Compiled);

    /// <inheritdoc/>
    public Verdict Executer()
    {
        var fautes = new List<string>();
        var verifies = 0;
        var manquants = 0;

        foreach (var (etiquette, dossier) in Images())
        {
            var dockerfile = Path.Combine(dossier, "Dockerfile");
            if (!File.Exists(dockerfile))
            {
                continue;
            }

            verifies++;

            var texte = SansArguments(File.ReadAllText(dockerfile));

            var copies = Copie.Matches(texte)
                .Select(m => m.Groups[1].Value)
                .Where(c => !c.StartsWith("--from", StringComparison.Ordinal))
                .ToList();

            var entree = Restauration.Match(texte);
            if (!entree.Success)
            {
                manquants++;
                fautes.Add($"{etiquette} — aucun `dotnet restore` trouvé dans le "
                           + "Dockerfile : rien à suivre, donc rien de vérifié.");
                continue;
            }

            foreach (var (relatif, raison) in Manquants(entree.Groups[1].Value, copies))
            {
                manquants++;
                var parent = Parent(relatif);
                fautes.Add($"{etiquette} : {relatif} — {raison}. "
                           + $"Ajouter `COPY {parent} ./{parent}` au Dockerfile. "
                           + "MSBuild rendra MSB9008 puis échouera en CS0234 sur un "
                           + "espace de noms, et le message désignera la mauvaise cause.");
            }
        }

        return new Verdict(
            fautes,
            [$"{verifies} Dockerfile(s) vérifié(s), {manquants} projet(s) manquant(s)"],
            NonCouvert());
    }

    /// <summary>
    /// Les projets atteints depuis le `dotnet restore` qui ne tombent dans aucun
    /// `COPY`, ou qui n'existent pas.
    /// </summary>
    /// <remarks>
    /// LE PARCOURS S'ARRÊTE AU PREMIER PROJET NON COPIÉ. Ses propres références
    /// ne sont pas suivies : elles manqueraient toutes, et la liste noierait la
    /// seule ligne `COPY` qu'il faut ajouter sous vingt conséquences.
    /// </remarks>
    private static SortedDictionary<string, string> Manquants(
        string restaure, IReadOnlyList<string> copies)
    {
        var problemes = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var vus = new HashSet<string>(StringComparer.Ordinal);

        var pile = new Stack<string>();
        pile.Push(Path.GetFullPath(Path.Combine(
            Depot.Racine, restaure.Replace('\\', Path.DirectorySeparatorChar))));

        while (pile.Count > 0)
        {
            var projet = pile.Pop();
            if (!vus.Add(projet))
            {
                continue;
            }

            var relatif = Depot.Relatif(projet);

            var dedans = copies.Any(c =>
                relatif == c
                || relatif.StartsWith(c.TrimEnd('/') + "/", StringComparison.Ordinal));

            if (!dedans)
            {
                problemes[relatif] = "non copié par le Dockerfile";
                continue;
            }

            if (!File.Exists(projet))
            {
                problemes[relatif] = "référencé mais absent du dépôt";
                continue;
            }

            try
            {
                foreach (var (_, absolu) in Projets.References(projet).ToList())
                {
                    pile.Push(absolu);
                }
            }
            catch (IOException erreur)
            {
                problemes[relatif] = "illisible — " + erreur.Message;
            }
        }

        return problemes;
    }

    /// <summary>
    /// Tout ce qui porte un Dockerfile : « univers/nom-du-service », puis
    /// « apps/nom ».
    /// </summary>
    private static IEnumerable<(string Etiquette, string Dossier)> Images()
    {
        var services = Depot.Dossier("services");
        foreach (var univers in Directory.EnumerateDirectories(services)
                     .OrderBy(x => x, StringComparer.Ordinal))
        {
            foreach (var service in Directory.EnumerateDirectories(univers)
                         .OrderBy(x => x, StringComparer.Ordinal))
            {
                yield return ($"{Path.GetFileName(univers)}/{Path.GetFileName(service)}", service);
            }
        }

        var apps = Depot.Dossier("apps");
        foreach (var app in Directory.EnumerateDirectories(apps)
                     .OrderBy(x => x, StringComparer.Ordinal))
        {
            yield return ($"apps/{Path.GetFileName(app)}", app);
        }
    }

    /// <summary>
    /// Remplace les <c>ARG NOM=valeur</c> par leur valeur dans le reste du
    /// fichier.
    /// </summary>
    private static string SansArguments(string texte)
    {
        foreach (Match m in Argument.Matches(texte))
        {
            var nom = m.Groups[1].Value;
            var valeur = m.Groups[2].Value;
            texte = texte.Replace("${" + nom + "}", valeur, StringComparison.Ordinal);
            texte = texte.Replace("$" + nom, valeur, StringComparison.Ordinal);
        }

        return texte;
    }

    /// <summary>Le dossier d'un chemin relatif, séparateurs `/`.</summary>
    private static string Parent(string relatif)
    {
        var coupe = relatif.LastIndexOf('/');
        return coupe <= 0 ? "." : relatif[..coupe];
    }

    private static List<string> NonCouvert()
        =>
        [
            "que l'image se construise : ce contrôle lit du texte, il ne lance "
            + "aucun `docker build`",
            "`.dockerignore` — un `COPY` présent dont le contenu est exclu du "
            + "contexte reste invisible ici",
            "les `COPY` à sources multiples et les `COPY --from=…` : le premier "
            + "motif n'en lit qu'une, le second est ignoré",
            "les `ARG` sans valeur par défaut, et ceux redéfinis à la "
            + "construction par `--build-arg` : seule la valeur écrite dans le "
            + "fichier est substituée",
            "les Dockerfiles hors `services/<univers>/<service>/` et `apps/<nom>/`",
            "les `ProjectReference` écrites dans un commentaire XML ou sous un "
            + "`Condition` faux — la lecture est textuelle, elle ne comprend pas "
            + "MSBuild",
        ];
}
