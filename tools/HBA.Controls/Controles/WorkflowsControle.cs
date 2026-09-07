using System.Diagnostics;
using System.Text.RegularExpressions;

namespace HBA.Controls.Controles;

/// <summary>Les workflows GitHub Actions se chargent, ont des jobs, et chaque étape agit.</summary>
public sealed class WorkflowsControle : IControle
{
    /// <inheritdoc/>
    public string Nom => "workflows";

    /// <inheritdoc/>
    public string Resume =>
        "les workflows ont des jobs, des `needs` qui existent, et des étapes qui agissent";

    private static readonly Regex ScalaireBloc = new(@"^(\s*)(?:-\s+)?[^:#]+:\s*[|>][-+0-9]*\s*$");

    private static readonly Regex Mapping = new(@"^(\s*)(?:-\s+)?([A-Za-z0-9_.\-]+):\s*(.*)$");

    private static readonly Regex DebutJob = new(@"^  ([A-Za-z0-9_.\-]+):\s*$");

    private static readonly Regex Besoins = new(@"^    needs:\s*(.*)$");

    private static readonly Regex ElementDeListe = new(@"^      -\s*(\S+)\s*$");

    private static readonly Regex DebutEtape = new(@"^      -\s*([A-Za-z0-9_.\-]+):\s*(.*)$");

    private static readonly Regex SuiteEtape = new(@"^        ([A-Za-z0-9_.\-]+):\s*(.*)$");

    private static readonly Regex ScriptLance = new(
        @"^\s*(?:-\s*)?run:\s*(\./\S+)", RegexOptions.Multiline | RegexOptions.Compiled);

    /// <summary>Un job du workflow, tel que la lecture textuelle le voit.</summary>
    private sealed class Travail
    {
        /// <summary>Les jobs dont celui-ci dépend.</summary>
        public List<string> Requis = [];

        /// <summary>Ses étapes, dans l'ordre du fichier.</summary>
        public List<Etape> Etapes = [];

        /// <summary>Un `needs:` ouvert sur une liste en bloc, dont les éléments suivent.</summary>
        public bool RequisEnBloc;
    }

    /// <summary>Une étape : les clés qu'elle porte, et son nom s'il en a un.</summary>
    private sealed class Etape
    {
        /// <summary>Les clés de premier niveau de l'étape.</summary>
        public HashSet<string> Cles = new(StringComparer.Ordinal);

        /// <summary>La valeur de sa clé `name`, si elle en a une.</summary>
        public string? Intitule;
    }

    /// <inheritdoc/>
    public Verdict Executer()
    {
        var fautes = new List<string>();
        var constats = new List<string>();
        var nonCouvert = new List<string>
        {
            "le CHARGEMENT du YAML, qui était le premier point du contrôle Python : cet "
            + "outil n'a aucune dépendance de paquet. À la place, deux soupçons seulement "
            + "— une tabulation dans l'indentation, et un scalaire non cité portant un "
            + "deux-points. Clé dupliquée, guillemet non refermé, ancre cassée, style de "
            + "flux mal formé, indentation incohérente : rien de tout cela n'est vu, et un "
            + "workflow qui en souffre ne s'exécutera JAMAIS sans le dire nulle part. "
            + "`actionlint` en CI est la vraie réponse",
            "le bit d'exécution n'est vérifié que pour les `run:` commençant par `./` — un "
            + "script appelé par `bash script.sh` n'en a pas besoin. Ni l'existence du "
            + "script, ni son bon fonctionnement ne sont vérifiés",
            "la lecture de structure ne reconnaît que la forme canonique de ces fichiers "
            + "(jobs à deux espaces, leurs clés à quatre, les étapes à six) : un workflow "
            + "écrit autrement serait lu comme n'ayant aucun job — une faute au motif "
            + "trompeur, jamais un silence",
            "les expressions `${{ … }}`, les workflows réutilisables appelés par un `uses:` "
            + "de job, et les entrées `on:` ne sont pas examinés",
        };

        var dossier = Depot.Dossier(".github", "workflows");
        var fichiers = Depot.Fichiers(dossier, ".yml")
            .OrderBy(f => f, StringComparer.Ordinal)
            .Concat(Depot.Fichiers(dossier, ".yaml").OrderBy(f => f, StringComparer.Ordinal))
            .ToList();

        if (fichiers.Count == 0)
        {
            fautes.Add(
                ".github/workflows ne contient aucun workflow : la CI de ce dépôt n'existe "
                + "pas, et ce contrôle ne peut pas rendre vert là-dessus.");
            return new Verdict(fautes, constats, nonCouvert);
        }

        foreach (var chemin in fichiers)
        {
            var court = Depot.Relatif(chemin);
            var texte = File.ReadAllText(chemin);
            var lues = LignesUtiles(texte);

            fautes.AddRange(Soupcons(court, lues));

            var jobs = Jobs(lues);

            if (jobs.Count == 0)
            {
                fautes.Add(
                    $"{court} : aucun job — GitHub n'exécutera RIEN, et ne le dira nulle part.");
                continue;
            }

            foreach (var (nomJob, travail) in jobs)
            {
                foreach (var requis in travail.Requis.Where(r => !jobs.ContainsKey(r)))
                {
                    fautes.Add(
                        $"{court} : le job « {nomJob} » dépend de « {requis} », qui n'existe "
                        + "pas — il ne s'exécuterait jamais");
                }

                for (var i = 0; i < travail.Etapes.Count; i++)
                {
                    var etape = travail.Etapes[i];
                    if (etape.Cles.Contains("uses") || etape.Cles.Contains("run"))
                    {
                        continue;
                    }

                    var titre = etape.Intitule ?? $"étape {i}";
                    fautes.Add($"{court} : « {nomJob} / {titre} » n'a ni `uses` ni `run`");
                }
            }
        }

        fautes.AddRange(ScriptsExecutables(fichiers, constats));

        constats.Add($"{fichiers.Count} workflow(s), {fautes.Count} défaut(s).");

        return new Verdict(fautes, constats, nonCouvert);
    }

    /// <summary>
    /// Les lignes qui portent de la structure : ni vides, ni commentaires, ni corps
    /// de scalaire en bloc.
    /// </summary>
    private static List<(int Numero, string Ligne)> LignesUtiles(string texte)
    {
        var lignes = texte.Split('\n');
        var utiles = new List<(int Numero, string Ligne)>();
        var i = 0;

        while (i < lignes.Length)
        {
            var ligne = lignes[i];
            var nue = ligne.Trim();

            if (nue.Length == 0 || nue.StartsWith('#'))
            {
                i++;
                continue;
            }

            utiles.Add((i + 1, ligne));

            var bloc = ScalaireBloc.Match(ligne);
            if (!bloc.Success)
            {
                i++;
                continue;
            }

            var reference = bloc.Groups[1].Value.Length;
            i++;
            while (i < lignes.Length)
            {
                if (lignes[i].Trim().Length == 0)
                {
                    i++;
                    continue;
                }

                if (lignes[i].Length - lignes[i].TrimStart().Length <= reference)
                {
                    break;
                }

                i++;
            }
        }

        return utiles;
    }

    /// <summary>Les deux soupçons qui remplacent le chargement YAML.</summary>
    private static List<string> Soupcons(string court, List<(int Numero, string Ligne)> lues)
    {
        var soupcons = new List<string>();

        foreach (var (numero, ligne) in lues)
        {
            var indentation = ligne[..(ligne.Length - ligne.TrimStart().Length)];
            if (indentation.Contains('\t'))
            {
                soupcons.Add(
                    $"{court} ligne {numero} : tabulation dans l'indentation — YAML les "
                    + "interdit, et GitHub n'exécutera RIEN");
                continue;
            }

            var mapping = Mapping.Match(ligne);
            if (!mapping.Success)
            {
                continue;
            }

            var valeur = mapping.Groups[3].Value.Trim();

            // Un commentaire de fin de ligne ne fait pas partie du scalaire.
            var commentaire = valeur.IndexOf(" #", StringComparison.Ordinal);
            if (commentaire >= 0)
            {
                valeur = valeur[..commentaire].Trim();
            }

            if (valeur.Length == 0 || "\"'|>&*[{#".Contains(valeur[0]))
            {
                continue;
            }

            if (!valeur.Contains(": ", StringComparison.Ordinal) && !valeur.EndsWith(':'))
            {
                continue;
            }

            soupcons.Add(
                $"{court} ligne {numero} : « {mapping.Groups[2].Value} » porte un scalaire "
                + "NON CITÉ contenant un deux-points — YAML y lit un mapping imbriqué, et "
                + "GitHub n'exécutera RIEN sans le dire nulle part. Entourer la valeur de "
                + "guillemets.");
        }

        return soupcons;
    }

    /// <summary>Les jobs d'un workflow, lus ligne à ligne.</summary>
    private static Dictionary<string, Travail> Jobs(List<(int Numero, string Ligne)> lues)
    {
        var jobs = new Dictionary<string, Travail>(StringComparer.Ordinal);
        var dansJobs = false;
        var dansEtapes = false;
        string? job = null;
        Etape? etape = null;

        foreach (var (_, ligne) in lues)
        {
            var indentation = ligne.Length - ligne.TrimStart().Length;

            if (indentation == 0)
            {
                dansJobs = ligne.TrimEnd() == "jobs:";
                job = null;
                dansEtapes = false;
                etape = null;
                continue;
            }

            if (!dansJobs)
            {
                continue;
            }

            if (indentation == 2)
            {
                var entete = DebutJob.Match(ligne);
                job = entete.Success ? entete.Groups[1].Value : null;
                if (job is not null)
                {
                    jobs[job] = new Travail();
                }

                dansEtapes = false;
                etape = null;
                continue;
            }

            if (job is null)
            {
                continue;
            }

            var travail = jobs[job];

            if (indentation == 4)
            {
                dansEtapes = false;
                etape = null;

                var requis = Besoins.Match(ligne);
                if (requis.Success)
                {
                    LireRequis(travail, requis.Groups[1].Value.Trim());
                    continue;
                }

                if (ligne.TrimEnd() == "    steps:")
                {
                    dansEtapes = true;
                }

                continue;
            }

            if (indentation == 6 && !dansEtapes && travail.RequisEnBloc)
            {
                var element = ElementDeListe.Match(ligne);
                if (element.Success)
                {
                    travail.Requis.Add(element.Groups[1].Value.Trim('"', '\''));
                }

                continue;
            }

            if (dansEtapes && indentation == 6)
            {
                var debut = DebutEtape.Match(ligne);
                if (debut.Success)
                {
                    etape = new Etape();
                    etape.Cles.Add(debut.Groups[1].Value);
                    if (debut.Groups[1].Value == "name")
                    {
                        etape.Intitule = debut.Groups[2].Value.Trim();
                    }

                    travail.Etapes.Add(etape);
                    continue;
                }

                if (ligne.TrimEnd() == "      -")
                {
                    etape = new Etape();
                    travail.Etapes.Add(etape);
                }

                continue;
            }

            if (dansEtapes && etape is not null && indentation == 8)
            {
                var suite = SuiteEtape.Match(ligne);
                if (suite.Success)
                {
                    etape.Cles.Add(suite.Groups[1].Value);
                    if (suite.Groups[1].Value == "name")
                    {
                        etape.Intitule = suite.Groups[2].Value.Trim();
                    }
                }
            }
        }

        return jobs;
    }

    /// <summary>
    /// Un `needs:` sous ses trois formes : scalaire, liste en ligne, liste en bloc.
    /// </summary>
    private static void LireRequis(Travail travail, string valeur)
    {
        if (valeur.Length == 0)
        {
            travail.RequisEnBloc = true;
            return;
        }

        if (valeur.StartsWith('['))
        {
            foreach (var morceau in valeur.Trim('[', ']').Split(','))
            {
                var propre = morceau.Trim().Trim('"', '\'');
                if (propre.Length > 0)
                {
                    travail.Requis.Add(propre);
                }
            }

            return;
        }

        travail.Requis.Add(valeur.Trim('"', '\''));
    }

    /// <summary>
    /// Tout script lancé par <c> ./</c> dans un workflow doit être exécutable DANS
    /// GIT.
    /// </summary>
    private static List<string> ScriptsExecutables(List<string> fichiers, List<string> constats)
    {
        var fautes = new List<string>();
        var modes = ModesGit(fautes);

        if (modes.Count == 0)
        {
            return fautes;
        }

        var vus = 0;

        foreach (var chemin in fichiers)
        {
            var court = Depot.Relatif(chemin);
            foreach (Match appel in ScriptLance.Matches(File.ReadAllText(chemin)))
            {
                var lance = appel.Groups[1].Value;
                var cible = lance.TrimStart('.', '/');
                vus++;

                if (!modes.TryGetValue(cible, out var mode))
                {
                    fautes.Add($"{court} : lance `{lance}`, que Git ne suit pas");
                    continue;
                }

                if (mode.EndsWith("755", StringComparison.Ordinal))
                {
                    continue;
                }

                fautes.Add(
                    $"{court} : lance `{lance}`, enregistré en {mode} — le runner répondra "
                    + $"« Permission denied » (code 126). chmod +x {cible} && git add {cible}");
            }
        }

        if (vus == 0)
        {
            constats.Add("aucun script appelé par `./` dans les workflows");
        }

        return fautes;
    }

    /// <summary>
    /// Les modes de l'INDEX GIT, seul endroit où vit le bit d'exécution que le
    /// runner recevra.
    /// </summary>
    private static Dictionary<string, string> ModesGit(List<string> fautes)
    {
        var modes = new Dictionary<string, string>(StringComparer.Ordinal);

        try
        {
            var demarrage = new ProcessStartInfo("git")
            {
                WorkingDirectory = Depot.Racine,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            demarrage.ArgumentList.Add("ls-files");
            demarrage.ArgumentList.Add("-s");

            using var processus = Process.Start(demarrage);
            if (processus is null)
            {
                fautes.Add(
                    "`git ls-files -s` n'a pas pu être lancé — le bit d'exécution des "
                    + "scripts de CI n'est plus vérifié par personne.");
                return modes;
            }

            var sortie = processus.StandardOutput.ReadToEnd();
            processus.WaitForExit();

            foreach (var ligne in sortie.Split('\n'))
            {
                var champs = ligne.Split('\t', 2);
                if (champs.Length != 2)
                {
                    continue;
                }

                var entete = champs[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (entete.Length > 0)
                {
                    modes[champs[1].Trim()] = entete[0];
                }
            }
        }
        catch (Exception erreur) when (erreur is System.ComponentModel.Win32Exception
                                           or InvalidOperationException or IOException)
        {
            fautes.Add(
                $"`git ls-files -s` a échoué ({erreur.GetType().Name}) — le bit d'exécution "
                + "des scripts de CI n'est plus vérifié par personne.");
            return modes;
        }

        if (modes.Count == 0)
        {
            fautes.Add(
                "`git ls-files -s` n'a rien rendu — ce contrôle ne vérifie plus rien du bit "
                + "d'exécution, et un `Permission denied` en CI reviendrait sans prévenir.");
        }

        return modes;
    }
}
