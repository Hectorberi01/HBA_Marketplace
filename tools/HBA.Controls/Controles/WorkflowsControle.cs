using System.Diagnostics;
using System.Text.RegularExpressions;

namespace HBA.Controls.Controles;

/// <summary>
/// Les workflows GitHub Actions se chargent, ont des jobs, et chaque étape agit.
/// </summary>
/// <remarks>
/// ═══════════════════════════════════════════════════════════════════════════
/// UN WORKFLOW MAL FORMÉ NE SE PLAINT PAS — IL NE TOURNE PAS.
///
/// C'EST LA PANNE LA PLUS TRAÎTRE DE TOUTE LA CHAÎNE.
///
/// GitHub Actions n'exécute pas un workflow dont le YAML est invalide : aucune
/// exécution n'apparaît dans l'onglet Actions, aucune notification ne part, aucun
/// statut ne remonte sur la PR. On croit la CI verte alors qu'elle n'a jamais
/// démarré — et l'on s'en aperçoit au pire moment, en cherchant pourquoi une
/// régression est passée.
///
/// Le défaut rencontré en écrivant `ci.yml` : un `- name:` contenant un
/// deux-points sans guillemets, que YAML lit comme un mapping imbriqué. Le
/// fichier paraît parfaitement lisible.
///
/// Ce que le contrôle vérifie :
///   · il y a au moins un job ;
///   · chaque `needs` désigne un job qui existe — une faute de frappe y produit
///     un job qui n'est JAMAIS exécuté, sans erreur ;
///   · chaque étape a un `uses` ou un `run` ;
///   · tout script lancé par `./` est enregistré exécutable DANS GIT.
///
/// ─────────────────────────────────────────────────────────────────────────
/// CE QUE LE PORTAGE A PERDU, ET C'EST LA PREMIÈRE CHOSE À DIRE.
///
/// La version Python chargeait chaque fichier avec PyYAML : le premier point de
/// sa liste était « le YAML se charge », et c'est celui qui a motivé tout le
/// contrôle. Cet outil n'a AUCUNE dépendance de paquet, et écrire un analyseur
/// YAML complet pour retrouver cette garantie serait pire que le mal.
///
/// La lecture est donc textuelle, taillée pour la forme de ces quatre fichiers.
/// À la place de « le YAML se charge », deux soupçons SEULEMENT, qui couvrent le
/// défaut réellement rencontré :
///
///   · une indentation contenant une TABULATION — YAML les interdit ;
///   · un scalaire NON CITÉ qui porte un deux-points suivi d'une espace, ou qui
///     se termine par un deux-points : c'est exactement le `- name:` qui a
///     produit la panne.
///
/// TOUT LE RESTE DE LA VALIDITÉ YAML N'EST PLUS VÉRIFIÉ : clé dupliquée dans un
/// même mapping, guillemet non refermé, ancre ou alias cassé, style de flux mal
/// formé, indentation incohérente, caractère non imprimable. Un fichier atteint
/// de l'un de ces maux passerait ce contrôle et ne s'exécuterait jamais.
///
/// LA VRAIE RÉPONSE EST `actionlint`, ou tout simplement un chargement YAML dans
/// une étape de la CI elle-même. Tant qu'aucun des deux n'existe, cette liste est
/// le trou, et elle est écrite ici pour qu'on ne l'oublie pas.
///
/// EN REVANCHE, LA LECTURE DE STRUCTURE NE PEUT PAS SE TAIRE. Elle ne reconnaît
/// que la forme canonique de ces fichiers — `jobs:` en première colonne, les jobs
/// à deux espaces, leurs clés à quatre, les étapes à six. Un workflow écrit
/// autrement serait lu comme n'ayant AUCUN job, ce qui est déjà une faute de ce
/// contrôle. Le silence est fermé ; c'est le motif de la faute qui pourrait
/// tromper.
/// ─────────────────────────────────────────────────────────────────────────
/// LE BIT D'EXÉCUTION, ET POURQUOI CE CONTRÔLE LANCE `git`.
///
/// `scripts/check-all.sh` était enregistré en 100644. La CI l'appelle par
/// `./scripts/check-all.sh`, et le runner a répondu :
///
///     ./scripts/check-all.sh: Permission denied
///     Error: Process completed with exit code 126
///
/// Le message parle de permission, ce qui envoie regarder les droits du runner ou
/// ceux du dépôt — alors que la cause est un bit stocké dans l'INDEX GIT,
/// invisible dans un diff et absent de tout affichage habituel. C'est celui-là
/// que le runner reçoit après un checkout, et il peut différer de celui du
/// disque : lire le système de fichiers répondrait à côté de la question.
///
/// D'où le seul appel de processus de tout cet outillage : `git ls-files -s`. S'il
/// échoue ou ne rend rien, c'est une FAUTE — un contrôle qui ne peut plus
/// vérifier ne doit pas rendre vert.
///
/// Le mode se perd facilement : un fichier réécrit par un outil, une copie depuis
/// un système sans bit d'exécution, un `git add` après un `cp` maladroit. Rien ne
/// le signale avant que la CI ne tombe.
///
/// LE CONSEIL COMPTE AUTANT QUE LE CONSTAT, ET LE PREMIER ÉTAIT FRAGILE. Le
/// message recommandait `git update-index --chmod=+x`. Ça corrige l'index — et le
/// PROCHAIN `git add` de ce fichier le défait, parce que `git add` relit le mode
/// sur le DISQUE. Le défaut revient alors sans que personne ne comprenne pourquoi.
///
/// CE QUE LE CONTRÔLE DU BIT NE COUVRE PAS : il ne regarde que les `run:` qui
/// commencent par `./`. Un script appelé par `bash script.sh` n'a pas besoin du
/// bit, et n'est donc pas vérifié — c'est d'ailleurs la façon la plus robuste
/// d'écrire un workflow. Il ne vérifie pas non plus que le script EXISTE, ni
/// qu'il fonctionne.
/// ═══════════════════════════════════════════════════════════════════════════
/// </remarks>
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
    /// Les lignes qui portent de la structure : ni vides, ni commentaires, ni
    /// corps de scalaire en bloc.
    /// </summary>
    /// <remarks>
    /// LE CORPS D'UN `run: |` EST DU SHELL, PAS DU YAML. Le lire comme du YAML
    /// ferait crier les soupçons sur chaque ligne de commande contenant un
    /// deux-points, et le bruit finit toujours par masquer le vrai manque.
    /// </remarks>
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

    /// <summary>Un `needs:` sous ses trois formes : scalaire, liste en ligne, liste en bloc.</summary>
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
    /// Tout script lancé par <c>./</c> dans un workflow doit être exécutable DANS GIT.
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
    /// <remarks>
    /// LE SEUL APPEL DE PROCESSUS DE TOUT CET OUTILLAGE, et il est assumé : le
    /// mode que le runner obtient après un checkout est celui de l'index, pas
    /// celui du disque. Lire le système de fichiers répondrait à côté de la
    /// question. Un échec de `git` est une FAUTE, jamais un silence.
    /// </remarks>
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
