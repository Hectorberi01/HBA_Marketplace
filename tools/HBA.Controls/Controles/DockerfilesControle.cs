using System.Text.RegularExpressions;

namespace HBA.Controls.Controles;

/// <summary>
/// Fermeture transitive des <c> COPY</c> : ce que chaque image oublie d'embarquer.
/// </summary>
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
    /// Tout ce qui porte un Dockerfile : « univers/nom-du-service », puis «
    /// apps/nom ».
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

        // `bff/` EST LE PROJET, PAS UN DOSSIER QUI EN CONTIENT.
        yield return ("bff", Depot.Dossier("bff"));
    }

    /// <summary>
    /// Remplace les <c> ARG NOM=valeur</c> par leur valeur dans le reste du
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
            "les Dockerfiles hors `services/<univers>/<service>/` et `bff/`",
            "les `ProjectReference` écrites dans un commentaire XML ou sous un "
            + "`Condition` faux — la lecture est textuelle, elle ne comprend pas "
            + "MSBuild",
        ];
}
