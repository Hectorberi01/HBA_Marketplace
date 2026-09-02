using System.Text.RegularExpressions;

namespace HBA.Controls.Controles;

/// <summary>
/// Le sous-ensemble de HCL que le contrôle Terraform doit savoir lire.
/// </summary>
/// <remarks>
/// ═══════════════════════════════════════════════════════════════════════════
/// CE TYPE REMPLACE `python-hcl2`, ET C'EST LUI QUI REND LA PARTIE TERRAFORM
/// ENFIN EXÉCUTABLE.
///
/// `check-infra.py` commençait par `import hcl2` dans un `try`, et sur échec
/// imprimait « python-hcl2 absent — partie Terraform ignorée » puis rendait une
/// liste vide. Sur le poste où ce portage a été fait, le paquet MANQUAIT : la
/// partie Terraform entière — câblage des modules, variables non déclarées,
/// backends partagés, secrets en clair, `.tfvars` commités — n'a jamais tourné,
/// et le script rendait 0 en annonçant deux sections vertes sur trois. C'est
/// exactement le vert silencieux que ce dépôt combat.
///
/// L'outil .NET n'a AUCUNE référence de paquet et ne doit pas en acquérir. La
/// lecture est donc écrite ici, et elle tourne TOUJOURS.
///
/// CE N'EST PAS UN ANALYSEUR HCL. Il suit la profondeur des accolades et
/// reconnaît quatre choses, les seules dont le contrôle a besoin :
///
///   · les blocs étiquetés — `variable "x" {`, `module "y" {`, `backend "s3" {` ;
///   · les blocs nus — `terraform {`, `required_providers {` ;
///   · les blocs d'affectation — `endpoints = {` ;
///   · les affectations simples `cle = valeur`, guillemets extérieurs retirés.
///
/// CE QU'IL NE SAIT PAS LIRE, ET QUI PASSERAIT DONC EN SILENCE :
///
///   · un heredoc (`&lt;&lt;EOT`) : son contenu serait lu comme du code, et une ligne
///     du texte qui ressemble à `cle = valeur` deviendrait un argument fantôme ;
///   · une valeur de liste ou d'objet écrite sur PLUSIEURS lignes autrement que
///     par un bloc `{` — un `[` ouvert en fin de ligne n'est pas suivi ;
///   · une accolade ouvrante qui n'est pas en fin de ligne, ou deux blocs sur la
///     même ligne ;
///   · les expressions : `var.x` est trouvé par expression régulière sur le
///     texte, exactement comme le faisait le Python, jamais par évaluation ;
///   · et il NE VALIDE RIEN. Un fichier HCL cassé est lu sans broncher. Là où le
///     Python disait « HCL invalide », ce lecteur dira « argument inconnu » ou
///     ne dira rien. `terraform validate` est la vraie réponse, et il n'est pas
///     ici.
///
/// L'ÉCART A ÉTÉ MESURÉ, ET DANS LES DEUX SENS.
///
/// Ce lecteur a été prototypé en Python et confronté à `python-hcl2` — installé
/// pour l'occasion — sur les 6 fichiers `.tf` du dépôt, pour les trois seules
/// choses que le contrôle en tire :
///
///   · 24 blocs `variable`, noms et présence d'un `default` : IDENTIQUES ;
///   · 8 blocs `module`, `source` et jeu d'arguments : IDENTIQUES ;
///   · 2 blocs `backend`, `bucket` et `key` : IDENTIQUES ;
///   · et sur une copie MUTÉE du dossier — argument inconnu, variable requise
///     non fournie, `var.X` non déclarée, deux environnements sur la même clé
///     d'état — les six fautes attendues sont trouvées par les deux lecteurs,
///     mot pour mot.
/// ═══════════════════════════════════════════════════════════════════════════
/// </remarks>
internal static class LectureHcl
{
    /// <summary>Un bloc lu : son genre, ses étiquettes, ses clés de premier niveau.</summary>
    internal sealed record Bloc(
        string Genre,
        IReadOnlyList<string> Etiquettes,
        Dictionary<string, string?> Champs);

    private static readonly Regex Entete = new(
        @"^([A-Za-z_][A-Za-z0-9_\-]*)((?:\s+""[^""]*"")*)\s*\{\s*$", RegexOptions.Compiled);

    private static readonly Regex AffectationBloc = new(
        @"^([A-Za-z_][A-Za-z0-9_\-]*)\s*=\s*\{\s*$", RegexOptions.Compiled);

    private static readonly Regex Affectation = new(
        @"^([A-Za-z_][A-Za-z0-9_\-]*)\s*=\s*(.*?)\s*$", RegexOptions.Compiled);

    private static readonly Regex Etiquette = new(@"""([^""]*)""", RegexOptions.Compiled);

    /// <summary>Le contenu d'un fichier `.tf`, dépouillé de ce qui n'est pas du code.</summary>
    internal sealed record Fichier(
        Dictionary<string, Bloc> Variables,
        Dictionary<string, Bloc> Modules,
        List<Bloc> Backends);

    /// <summary>Lit un fichier `.tf`.</summary>
    public static Fichier Lire(string texte)
    {
        var variables = new Dictionary<string, Bloc>(StringComparer.Ordinal);
        var modules = new Dictionary<string, Bloc>(StringComparer.Ordinal);
        var backends = new List<Bloc>();
        var pile = new List<Bloc>();

        foreach (var brut in SansCommentaires(texte).Split('\n'))
        {
            var ligne = brut.Trim();
            if (ligne.Length == 0)
            {
                continue;
            }

            if (ligne.StartsWith('}'))
            {
                if (pile.Count == 0)
                {
                    continue;
                }

                var ferme = pile[^1];
                pile.RemoveAt(pile.Count - 1);

                if (ferme.Genre == "variable" && pile.Count == 0 && ferme.Etiquettes.Count > 0)
                {
                    variables[ferme.Etiquettes[0]] = ferme;
                }
                else if (ferme.Genre == "module" && pile.Count == 0
                         && ferme.Etiquettes.Count > 0)
                {
                    modules[ferme.Etiquettes[0]] = ferme;
                }
                else if (ferme.Genre == "backend" && pile.Count == 1
                         && pile[0].Genre == "terraform")
                {
                    backends.Add(ferme);
                }

                continue;
            }

            var bloc = AffectationBloc.Match(ligne);
            if (bloc.Success)
            {
                // `endpoints = { … }` : la clé existe au niveau du parent, et son
                // contenu ne nous intéresse pas — seule sa PRÉSENCE compte, pour
                // qu'un argument de module écrit sous cette forme soit compté.
                if (pile.Count > 0)
                {
                    pile[^1].Champs[bloc.Groups[1].Value] = null;
                }

                pile.Add(new Bloc("=", [bloc.Groups[1].Value],
                    new Dictionary<string, string?>(StringComparer.Ordinal)));
                continue;
            }

            var entete = Entete.Match(ligne);
            if (entete.Success)
            {
                var etiquettes = Etiquette.Matches(entete.Groups[2].Value)
                    .Select(m => m.Groups[1].Value)
                    .ToList();

                if (pile.Count > 0)
                {
                    pile[^1].Champs.TryAdd(entete.Groups[1].Value, null);
                }

                pile.Add(new Bloc(entete.Groups[1].Value, etiquettes,
                    new Dictionary<string, string?>(StringComparer.Ordinal)));
                continue;
            }

            var champ = Affectation.Match(ligne);
            if (champ.Success && pile.Count > 0)
            {
                pile[^1].Champs[champ.Groups[1].Value] = Denude(champ.Groups[2].Value);
            }
        }

        return new Fichier(variables, modules, backends);
    }

    /// <summary>Retire `#`, `//` et `/* */` — sans toucher aux chaînes.</summary>
    /// <remarks>
    /// ON RETIRE LES COMMENTAIRES AVANT DE CHERCHER UN DRAPEAU INTERDIT. Le rôle
    /// `k3s-serveur` porte un encadré qui explique pourquoi
    /// `--disable-network-policy` ne doit PAS être posé. Chercher dans le texte
    /// brut ferait échouer le contrôle sur sa propre documentation — et la
    /// correction évidente serait de supprimer l'encadré, c'est-à-dire exactement
    /// la mauvaise.
    ///
    /// LES CHAÎNES SONT SUIVIES CARACTÈRE PAR CARACTÈRE, là où le Python passait
    /// par une expression régulière : un `#` dans une valeur — une couleur, un
    /// fragment d'URL — n'y ouvre plus de commentaire.
    /// </remarks>
    public static string SansCommentaires(string texte)
    {
        var sortie = new System.Text.StringBuilder(texte.Length);
        var i = 0;
        var n = texte.Length;

        while (i < n)
        {
            var c = texte[i];

            if (c == '"')
            {
                sortie.Append(c);
                i++;
                while (i < n)
                {
                    if (texte[i] == '\\' && i + 1 < n)
                    {
                        sortie.Append(texte[i]).Append(texte[i + 1]);
                        i += 2;
                        continue;
                    }

                    sortie.Append(texte[i]);
                    var fin = texte[i] == '"' || texte[i] == '\n';
                    i++;
                    if (fin)
                    {
                        break;
                    }
                }

                continue;
            }

            if (c == '#' || (c == '/' && i + 1 < n && texte[i + 1] == '/'))
            {
                while (i < n && texte[i] != '\n')
                {
                    i++;
                }

                continue;
            }

            if (c == '/' && i + 1 < n && texte[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < n && !(texte[i] == '*' && texte[i + 1] == '/'))
                {
                    i++;
                }

                i += 2;
                continue;
            }

            sortie.Append(c);
            i++;
        }

        return sortie.ToString();
    }

    /// <summary>Retire les guillemets extérieurs d'une valeur.</summary>
    private static string Denude(string valeur)
    {
        var v = valeur.Trim();
        return v.Length >= 2 && v[0] == '"' && v[^1] == '"' ? v[1..^1] : v;
    }
}

/// <summary>
/// L'infrastructure est le seul code du dépôt que personne n'exécute en boucle.
/// </summary>
/// <remarks>
/// ═══════════════════════════════════════════════════════════════════════════
/// ET C'EST CE QUI LA REND DANGEREUSE.
///
/// Un service cassé se voit au premier `dotnet build`. Un module Terraform cassé
/// se voit le jour où l'on provisionne — c'est-à-dire au pire moment possible,
/// souvent sous pression, souvent par quelqu'un qui ne l'a pas écrit. Entre-temps
/// le fichier a l'air correct, et personne n'a de raison d'en douter.
///
/// Ce contrôle est le substitut du `terraform plan` que ce dépôt ne peut pas
/// lancer (pas d'identifiants OVH), et du `ansible-playbook --check` qu'il ne
/// peut pas lancer non plus (pas de machines).
///
/// CE QU'IL VÉRIFIE — chacun correspond à une panne qui NE SE SIGNALE PAS :
///
///   Terraform
///     • chaque `module { source = … }` désigne un dossier qui existe ;
///     • chaque argument passé à un module correspond à une `variable` déclarée
///       — un nom mal orthographié fait échouer `init`, mais bien plus tard ;
///     • chaque variable SANS défaut du module est effectivement fournie ;
///     • chaque `var.X` d'un fichier est déclaré dans SON dossier ;
///     • chaque environnement a un `backend` distant, et une `key` qui lui est
///       propre — deux environnements partageant la même clé d'état s'écrasent
///       l'un l'autre, et le second `apply` DÉTRUIT les ressources du premier ;
///     • aucun secret littéral, aucun `.tfvars` réel commité.
///
///   Ansible
///     • chaque rôle nommé par un playbook existe ;
///     • chaque `hosts:` désigne un groupe présent dans les inventaires
///       d'exemple — un groupe inexistant ne produit PAS d'erreur : Ansible
///       affiche « skipping: no hosts matched » et sort en 0. Le rôle n'a jamais
///       tourné ;
///     • chaque `notify:` désigne un handler qui existe — même défaut, même
///       silence : le handler n'est simplement jamais appelé, et sshd n'est
///       jamais rechargé ;
///     • chaque `template: src:` existe dans `templates/` du rôle ;
///     • `stdout_callback` ne nomme pas un plugin RETIRÉ. `stdout_callback =
///       yaml` a fonctionné des années ; le nom court se résolvait en
///       `community.general.yaml`, supprimé en community.general 12.0.0 — et le
///       jour où la collection se met à jour, `ansible-playbook` s'arrête AVANT
///       la première tâche, alors que rien dans le dépôt n'a changé ce jour-là ;
///     • toute collection employée est déclarée dans `requirements.yml`.
///       `roles/commun` emploie `ansible.posix.sysctl` et `ansible.posix.mount`,
///       qui ne sont pas dans `ansible-core` : le playbook s'arrêtait sur
///       « couldn't resolve module/action », un message qui DÉSIGNE UNE LIGNE DU
///       RÔLE alors que le rôle est correct — ce qui manque est sur la machine.
///       Et le défaut est intermittent : `pip install ansible` embarque la
///       collection, `ansible-core` non. D'où un « ça marche chez moi » sincère ;
///     • aucun inventaire RÉEL n'est commité. Ce contrôle signalait autrefois la
///       PRÉSENCE du fichier, et c'était faux : le `.example` dit lui-même « copier
///       en `staging.yml` », donc un déploiement SUPPOSE ce fichier sur le poste.
///       Un contrôle qui passe au rouge quand on fait ce qu'il faut finit ignoré,
///       et il emmène les vrais défauts avec lui. On interroge donc Git, pas le
///       disque.
///
///   Compose — LA PILE RÉELLEMENT LANCÉE
///     Ce contrôle a changé de cible ET de question. Il vérifiait que chaque
///     service de `infra/docker/compose.*.yml` portait `OpenTelemetry__Endpoint`.
///     Deux défauts : ce fichier n'était lancé par personne — vert sur du code
///     mort, aveugle sur la pile vivante — et la question ne vaut pas pour la
///     pile de développement, qui n'embarque AUCUN collecteur OTLP. Exiger une
///     adresse partout y produirait vingt-trois services qui échouent à se
///     connecter toutes les quelques secondes.
///
///     On vérifie donc la COHÉRENCE :
///       A. un collecteur est présent, ou aucun service n'a d'adresse. L'état
///          intermédiaire est celui qui remplit les journaux sans que rien ne le
///          désigne ;
///       B. chaque base `Database=hba_xxx` injectée existe dans
///          `infra/postgres/init/001-create-databases.sql`. `hba_promotion` était
///          injectée et jamais créée, et cela ne se voyait pas parce que
///          `Database.Migrate()` crée la base absente en développement — ce qui
///          masque l'oubli jusqu'à la production, où `MigrateOnStartup=false`.
///
/// ═══ CE QUE LE PORTAGE A CHANGÉ, ET IL FAUT LE LIRE
///
/// LE PYTHON S'AUTORISAIT À NE RIEN VÉRIFIER, DANS DEUX SECTIONS SUR TROIS.
///
///   · `import hcl2` échoue → « partie Terraform ignorée », liste vide. SUR LE
///     POSTE OÙ CE PORTAGE A ÉTÉ FAIT, C'ÉTAIT LE CAS : le script annonçait
///     « Ansible : 13 fichiers » et « Compose : 21 services », rendait 0, et
///     n'avait JAMAIS regardé un seul fichier `.tf`. Deux sections vertes sur
///     trois se lisent comme trois.
///   · `import yaml` échoue → « partie Ansible ignorée », liste vide, et pour le
///     compose une faute honnête (« PyYAML absent : contrôle compose
///     impossible »), qui reste une faute que personne ne peut corriger sans
///     installer un paquet.
///
/// Les trois tournent désormais TOUJOURS : <see cref="LectureHcl"/> remplace
/// `python-hcl2`, <see cref="LectureYaml"/> remplace PyYAML, et le compose passe
/// par <c>ComposeDev</c>, la lecture que `AdressesServiceControle` et
/// `KafkaTopicsControle` partagent déjà. Ce que ces trois lectures ne
/// garantissent plus est écrit dans <see cref="Verdict.NonCouvert"/>, à voix
/// haute, à chaque exécution.
/// ═══════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed class InfraControle : IControle
{
    /// <inheritdoc/>
    public string Nom => "infra";

    /// <inheritdoc/>
    public string Resume =>
        "terraform, ansible et le compose disent ce que le déploiement exige";

    // Les clés d'un bloc `module` qui ne sont pas des variables du module cible.
    private static readonly string[] MetaModule =
        ["source", "version", "count", "for_each", "providers", "depends_on"];

    private static readonly Regex ReferenceVariable = new(
        @"\bvar\.([A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled);

    // Un littéral qui ressemble à un secret. On cherche l'AFFECTATION, pas le
    // mot : un commentaire qui parle de mots de passe est légitime, une valeur
    // ne l'est pas.
    private static readonly Regex SecretLitteral = new(
        @"^\s*(password|secret|token|access_key|secret_key|private_key)\s*=\s*""[^""$]{6,}""",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex Commentaire = new(
        @"(?:(?<=^)|(?<=\s))(?:#|//).*$", RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex ModuleQualifie = new(
        @"\b([a-z][a-z0-9_]*\.[a-z][a-z0-9_]*)\.[a-z][a-z0-9_]*\s*:", RegexOptions.Compiled);

    private static readonly Regex BaseCreee = new(
        @"CREATE\s+DATABASE\s+(hba_[a-z_]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex BaseInjectee = new(
        @"Database=(hba_[a-z_]+)", RegexOptions.Compiled);

    private static readonly Regex ImageDeCollecteur = new(
        @"otel|opentelemetry|jaeger|tempo", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex NomDeService = new(
        @"^  ([A-Za-z0-9_.\-]+):\s*$", RegexOptions.Compiled);

    private static readonly Regex LigneDImage = new(
        @"^    image:\s*(.*?)\s*$", RegexOptions.Compiled);

    // CES PLUGINS D'AFFICHAGE ONT ÉTÉ RETIRÉS, ET LA LISTE EST TENUE À LA MAIN.
    // Un nom inconnu de cette liste passe : ce contrôle réduit la surprise, il ne
    // la supprime pas.
    private static readonly Dictionary<string, string> PluginsRetires = new(StringComparer.Ordinal)
    {
        ["yaml"] = "community.general 12.0.0 — remplacer par `stdout_callback = default` "
                   + "+ `result_format = yaml` (ansible-core >= 2.13, aucune collection requise)",
        ["community.general.yaml"] = "community.general 12.0.0 — remplacer par "
                                     + "`stdout_callback = default` + `result_format = yaml`",
    };

    /// <inheritdoc/>
    public Verdict Executer()
    {
        var fautes = new List<string>();
        var constats = new List<string>();
        var nonCouvert = new List<string>
        {
            "ni `terraform plan`, ni `terraform validate`, ni `ansible-playbook --check`, "
            + "ni `docker compose config -q` n'ont tourné : ce contrôle ne lit que la "
            + "SYNTAXE et le CÂBLAGE des fichiers du dépôt. Il ne joint aucun fournisseur, "
            + "aucune machine, aucun démon",
            "la VALIDITÉ des fichiers : le HCL est lu par un compteur d'accolades et le "
            + "YAML par un lecteur de sous-ensemble, tous deux écrits ici (aucune "
            + "dépendance de paquet). Un fichier cassé est lu sans broncher et rend une "
            + "image partielle. Là où le Python disait « HCL invalide » ou « YAML "
            + "invalide », ces lectures diront « argument inconnu », « rôle introuvable », "
            + "ou ne diront rien",
            "côté HCL : les heredocs, les listes et objets écrits sur plusieurs lignes "
            + "hors bloc `{`, et toute expression — `var.X` est trouvé par expression "
            + "régulière sur le texte, jamais par évaluation. Un module distant "
            + "(`source` qui ne commence pas par un point) reste hors de portée, comme "
            + "dans le Python",
            "côté Ansible : les ancres et alias YAML, `import_tasks`/`include_tasks` (les "
            + "tâches d'un fichier inclus ne sont PAS relues, donc leurs `notify` et leurs "
            + "`template:` non plus), les variables de groupe, et tout ce qu'un plugin "
            + "d'inventaire dynamique produirait",
            "côté Compose : la forme LISTE de `environment:` (`- CLÉ=valeur`), que Compose "
            + "accepte et que `ComposeDev` lit comme vide — toutes les clés du service "
            + "paraîtraient alors absentes ; `extends:`, `include:` et les fichiers de "
            + "surcharge ne sont pas suivis, et aucune substitution `${VAR}` n'est résolue",
            "que le `stdout_callback` déclaré soit valide : seuls DEUX noms retirés sont "
            + "connus, et la liste est tenue à la main. Les autres plugins retirés, "
            + "présents et à venir, passent",
        };

        fautes.AddRange(Terraform(constats, nonCouvert));
        fautes.AddRange(Ansible(constats, nonCouvert));
        fautes.AddRange(Compose(constats));

        constats.Add($"{fautes.Count} défaut(s) d'infrastructure.");

        return new Verdict(fautes, constats, nonCouvert);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // TERRAFORM
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>Le câblage des modules, l'état distant, et ce qui ne doit pas y être.</summary>
    private static List<string> Terraform(List<string> constats, List<string> nonCouvert)
    {
        var fautes = new List<string>();
        var racine = Depot.Dossier("infra", "terraform");
        var cache = new Dictionary<string, Dictionary<string, LectureHcl.Bloc>>(
            StringComparer.Ordinal);
        var clesDEtat = new Dictionary<string, string>(StringComparer.Ordinal);

        var fichiers = Depot.Fichiers(racine, ".tf")
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

        if (fichiers.Count == 0)
        {
            fautes.Add(
                "infra/terraform : aucun fichier .tf — ce contrôle ne peut RIEN regarder.");
            return fautes;
        }

        foreach (var chemin in fichiers)
        {
            var dossier = Path.GetDirectoryName(chemin)!;
            var brut = File.ReadAllText(chemin);
            var document = LectureHcl.Lire(brut);
            var code = LectureHcl.SansCommentaires(brut);
            var relatif = Depot.Relatif(chemin);

            if (SecretLitteral.IsMatch(code))
            {
                fautes.Add(
                    $"{relatif} : une valeur ressemble à un secret en clair — les "
                    + "identifiants passent par l'environnement, jamais par le dépôt");
            }

            if (code.Contains("--disable-network-policy", StringComparison.Ordinal))
            {
                fautes.Add(
                    $"{relatif} : « --disable-network-policy » rendrait k8s/base/policies/ "
                    + "inerte SANS rien supprimer");
            }

            // ── les `var.X` référencés sont-ils déclarés ici ? ────────────────
            var declarees = VariablesDuDossier(dossier, cache);
            foreach (var nom in ReferenceVariable.Matches(code)
                         .Select(m => m.Groups[1].Value)
                         .Distinct(StringComparer.Ordinal)
                         .OrderBy(x => x, StringComparer.Ordinal))
            {
                if (!declarees.ContainsKey(nom))
                {
                    fautes.Add(
                        $"{relatif} : `var.{nom}` n'est déclaré par aucun fichier de "
                        + $"{Depot.Relatif(dossier)}/");
                }
            }

            // ── l'état est-il distant, et propre à cet environnement ? ───────
            foreach (var backend in document.Backends)
            {
                var genre = backend.Etiquettes.Count > 0 ? backend.Etiquettes[0] : string.Empty;
                if (genre == "local")
                {
                    fautes.Add(
                        $"{relatif} : backend « local » — l'état contient des secrets en "
                        + "clair et deux opérateurs divergeraient sans le voir");
                    continue;
                }

                var cle = backend.Champs.GetValueOrDefault("bucket")
                          + "/" + backend.Champs.GetValueOrDefault("key");

                if (clesDEtat.TryGetValue(cle, out var premier))
                {
                    fautes.Add(
                        $"{relatif} : même clé d'état que {premier} ({cle}) — le second "
                        + "`apply` DÉTRUIRAIT les ressources du premier");
                }

                clesDEtat[cle] = relatif;
            }

            // ── le câblage des modules ───────────────────────────────────────
            foreach (var (nom, configuration) in document.Modules
                         .OrderBy(m => m.Key, StringComparer.Ordinal))
            {
                var source = configuration.Champs.GetValueOrDefault("source");
                if (string.IsNullOrEmpty(source))
                {
                    fautes.Add($"{relatif} : module « {nom} » sans `source`");
                    continue;
                }

                if (!source.StartsWith('.'))
                {
                    continue;   // module distant : hors de portée
                }

                var cible = Path.GetFullPath(Path.Combine(
                    dossier, source.Replace('/', Path.DirectorySeparatorChar)));

                if (!Directory.Exists(cible))
                {
                    fautes.Add(
                        $"{relatif} : module « {nom} » pointe {source}, qui n'existe pas");
                    continue;
                }

                var attendues = VariablesDuDossier(cible, cache);
                var fournis = configuration.Champs.Keys
                    .Where(k => !MetaModule.Contains(k))
                    .ToHashSet(StringComparer.Ordinal);

                foreach (var argument in fournis
                             .Where(a => !attendues.ContainsKey(a))
                             .OrderBy(a => a, StringComparer.Ordinal))
                {
                    fautes.Add(
                        $"{relatif} : module « {nom} » passe « {argument} », que "
                        + $"{Depot.Relatif(cible)} ne déclare pas");
                }

                foreach (var (variable, definition) in attendues
                             .OrderBy(v => v.Key, StringComparer.Ordinal))
                {
                    if (fournis.Contains(variable) || definition.Champs.ContainsKey("default"))
                    {
                        continue;
                    }

                    fautes.Add(
                        $"{relatif} : module « {nom} » ne fournit pas « {variable} », qui "
                        + "n'a pas de valeur par défaut");
                }
            }
        }

        // ── les environnements ont-ils tous un backend ? ─────────────────────
        var environnements = Path.Combine(racine, "environments");
        if (Directory.Exists(environnements))
        {
            foreach (var environnement in Directory.EnumerateDirectories(environnements)
                         .OrderBy(d => d, StringComparer.Ordinal))
            {
                var porte = Directory.EnumerateFiles(environnement, "*.tf")
                    .Any(f => LectureHcl.SansCommentaires(File.ReadAllText(f))
                        .Contains("backend \"", StringComparison.Ordinal));

                if (!porte)
                {
                    fautes.Add(
                        $"{Depot.Relatif(environnement)} : aucun `backend` — l'état "
                        + "finirait sur le poste de qui applique");
                }
            }
        }
        else
        {
            nonCouvert.Add(
                "infra/terraform/environments n'existe pas : aucun environnement n'a pu "
                + "être vérifié pour son backend");
        }

        // ── un .tfvars réel commité ? ────────────────────────────────────────
        foreach (var fichier in Depot.Fichiers(racine, ".tfvars")
                     .OrderBy(f => f, StringComparer.Ordinal))
        {
            fautes.Add(
                $"{Depot.Relatif(fichier)} : un .tfvars réel n'a rien à faire dans le "
                + "dépôt (seuls les .tfvars.example)");
        }

        constats.Add(
            $"Terraform : {fichiers.Count} fichier(s), {clesDEtat.Count} clé(s) d'état "
            + "distinctes.");

        return fautes;
    }

    /// <summary>Les `variable` déclarées par TOUS les `.tf` d'un dossier.</summary>
    private static Dictionary<string, LectureHcl.Bloc> VariablesDuDossier(string dossier, Dictionary<string, Dictionary<string, LectureHcl.Bloc>> cache)
    {
        if (cache.TryGetValue(dossier, out var connues))
        {
            return connues;
        }

        var declarees = new Dictionary<string, LectureHcl.Bloc>(StringComparer.Ordinal);
        foreach (var fichier in Directory.EnumerateFiles(dossier, "*.tf")
                     .OrderBy(f => f, StringComparer.Ordinal))
        {
            try
            {
                foreach (var (nom, bloc) in LectureHcl.Lire(File.ReadAllText(fichier)).Variables)
                {
                    declarees[nom] = bloc;
                }
            }
            catch (IOException)
            {
                // Un fichier illisible ne doit pas emporter le contrôle entier :
                // les autres dossiers restent vérifiables. Le Python faisait de
                // même, en avalant TOUTE exception ; ici on n'avale que l'accès.
            }
        }

        cache[dossier] = declarees;
        return declarees;
    }

    // ═════════════════════════════════════════════════════════════════════════
    // ANSIBLE
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>Rôles, groupes, handlers, modèles, collections et inventaires.</summary>
    private static List<string> Ansible(List<string> constats, List<string> nonCouvert)
    {
        var fautes = new List<string>();
        var racine = Depot.Dossier("infra", "ansible");

        // ── ansible.cfg : des plugins retirés y survivent en silence ─────────
        var configuration = Path.Combine(racine, "ansible.cfg");
        if (File.Exists(configuration))
        {
            var callback = CallbackDeSortie(File.ReadAllText(configuration));
            if (callback is not null && PluginsRetires.TryGetValue(callback, out var raison))
            {
                fautes.Add(
                    $"infra/ansible/ansible.cfg : `stdout_callback = {callback}` est un "
                    + $"plugin RETIRÉ ({raison}). `ansible-playbook` refuse de démarrer, "
                    + "avant la première tâche.");
            }
        }

        // ── tous les documents, lus une seule fois ───────────────────────────
        var charges = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var chemin in Depot.Fichiers(racine, ".yml", ".yml.example")
                     .OrderBy(f => f, StringComparer.Ordinal))
        {
            var brut = File.ReadAllText(chemin);
            charges[chemin] = LectureYaml.Charge(brut);

            if (Commentaire.Replace(brut, string.Empty)
                .Contains("--disable-network-policy", StringComparison.Ordinal))
            {
                fautes.Add(
                    $"{Depot.Relatif(chemin)} : « --disable-network-policy » rendrait "
                    + "k8s/base/policies/ inerte SANS rien supprimer");
            }
        }

        var dossierRoles = Path.Combine(racine, "roles");
        var rolesConnus = new List<string>();
        if (Directory.Exists(dossierRoles))
        {
            rolesConnus.AddRange(Directory.EnumerateDirectories(dossierRoles)
                .Select(d => Path.GetFileName(d))
                .Where(d => !string.IsNullOrEmpty(d))
                .Select(d => d!)
                .OrderBy(d => d, StringComparer.Ordinal));
        }

        if (rolesConnus.Count == 0)
        {
            fautes.Add(
                "infra/ansible/roles ne contient aucun rôle : le playbook ne peut rien "
                + "appliquer, et ce contrôle n'a rien à comparer.");
        }

        // ── les groupes définis par les inventaires d'exemple ────────────────
        var groupes = new HashSet<string>(StringComparer.Ordinal) { "all", "localhost" };

        foreach (var (chemin, document) in charges.OrderBy(c => c.Key, StringComparer.Ordinal))
        {
            var separateur = Path.DirectorySeparatorChar;
            if (!chemin.Contains($"{separateur}inventory{separateur}", StringComparison.Ordinal))
            {
                continue;
            }

            var pile = new List<object?> { document };
            while (pile.Count > 0)
            {
                var noeud = LectureYaml.Table(pile[^1]);
                pile.RemoveAt(pile.Count - 1);
                if (noeud is null)
                {
                    continue;
                }

                foreach (var (cle, valeur) in noeud)
                {
                    if (cle is "hosts" or "vars")
                    {
                        continue;
                    }

                    var table = LectureYaml.Table(valeur);
                    if (table is null)
                    {
                        continue;
                    }

                    if (cle == "children")
                    {
                        groupes.UnionWith(table.Keys);
                        pile.AddRange(table.Values);
                    }
                    else
                    {
                        groupes.Add(cle);
                        pile.Add(table);
                    }
                }
            }

            // ── la production ne doit plus documenter un faux cluster HA ─────
            //
            // Trois VMs avec un seul `serveur` et deux `agents` donnent de la
            // capacité, pas un quorum. Perdre le serveur coupe l'API k3s et la
            // replanification, exactement le scénario §24. Tant qu'aucun endpoint
            // stable 6443 n'existe, on garde les trois nœuds dans `serveurs`.
            if (Path.GetFileName(chemin) != "production.yml.example")
            {
                continue;
            }

            var enfants = LectureYaml.Chemin(document, "all", "children");
            var serveurs = LectureYaml.Table(LectureYaml.Chemin(enfants, "serveurs", "hosts"));
            var agents = LectureYaml.Table(LectureYaml.Chemin(enfants, "agents", "hosts"));
            var nombre = serveurs?.Count ?? 0;

            if (nombre < 3 || nombre % 2 == 0)
            {
                fautes.Add(
                    $"{Depot.Relatif(chemin)} : la production doit déclarer un nombre "
                    + "impair d'au moins 3 serveurs k3s pour former le quorum etcd");
            }

            if (agents is not null && agents.Count > 0)
            {
                fautes.Add(
                    $"{Depot.Relatif(chemin)} : des agents sont déclarés alors qu'aucun "
                    + "endpoint stable 6443 n'est provisionné ; les trois nœuds de "
                    + "production doivent rester dans `serveurs` pour l'instant");
            }
        }

        // ── les playbooks ────────────────────────────────────────────────────
        var dossierPlaybooks = Path.Combine(racine, "playbooks");
        if (!Directory.Exists(dossierPlaybooks))
        {
            fautes.Add(
                "infra/ansible/playbooks n'existe pas : aucun jeu n'a pu être vérifié.");
        }
        else
        {
            foreach (var chemin in Directory.EnumerateFiles(dossierPlaybooks, "*.yml")
                         .OrderBy(f => f, StringComparer.Ordinal))
            {
                var document = charges.GetValueOrDefault(chemin);
                foreach (var jeu in LectureYaml.Liste(document))
                {
                    var cible = LectureYaml.Texte(LectureYaml.Chemin(jeu, "hosts"));
                    if (cible is not null && !groupes.Contains(cible))
                    {
                        fautes.Add(
                            $"{Depot.Relatif(chemin)} : `hosts: {cible}` ne correspond à "
                            + "aucun groupe des inventaires — Ansible dirait « no hosts "
                            + "matched » et sortirait en 0, le jeu n'ayant jamais tourné");
                    }

                    foreach (var role in LectureYaml.Liste(LectureYaml.Chemin(jeu, "roles")))
                    {
                        var nom = LectureYaml.Texte(role)
                                  ?? LectureYaml.Texte(LectureYaml.Chemin(role, "role"));
                        if (!string.IsNullOrEmpty(nom) && !rolesConnus.Contains(nom))
                        {
                            fautes.Add(
                                $"{Depot.Relatif(chemin)} : rôle « {nom} » introuvable "
                                + "dans roles/");
                        }
                    }
                }
            }
        }

        // ── les rôles ────────────────────────────────────────────────────────
        foreach (var role in rolesConnus)
        {
            var basse = Path.Combine(dossierRoles, role);
            var fichierTaches = Path.Combine(basse, "tasks", "main.yml");

            if (!File.Exists(fichierTaches))
            {
                fautes.Add($"infra/ansible/roles/{role} : pas de tasks/main.yml");
                continue;
            }

            var handlers = new HashSet<string>(StringComparer.Ordinal);
            var fichierHandlers = Path.Combine(basse, "handlers", "main.yml");
            foreach (var handler in TachesAPlat(charges.GetValueOrDefault(fichierHandlers)))
            {
                var nom = LectureYaml.Texte(LectureYaml.Chemin(handler, "name"));
                if (!string.IsNullOrEmpty(nom))
                {
                    handlers.Add(nom);
                }
            }

            foreach (var tache in TachesAPlat(charges.GetValueOrDefault(fichierTaches)))
            {
                // `notify:` S'ÉCRIT AU SINGULIER OU EN LISTE, et les deux doivent
                // être lus. `LectureYaml.Texte` rend `null` sur une liste — c'est
                // ce qui distingue les deux formes sans avoir à les deviner.
                var avertis = LectureYaml.Chemin(tache, "notify");
                var noms = new List<string>();
                var seul = LectureYaml.Texte(avertis);
                if (seul is not null)
                {
                    noms.Add(seul);
                }
                else
                {
                    foreach (var element in LectureYaml.Liste(avertis))
                    {
                        var nom = LectureYaml.Texte(element);
                        if (!string.IsNullOrEmpty(nom))
                        {
                            noms.Add(nom);
                        }
                    }
                }

                foreach (var avertissement in noms)
                {
                    if (!handlers.Contains(avertissement))
                    {
                        fautes.Add(
                            $"infra/ansible/roles/{role} : `notify: {avertissement}` ne "
                            + "désigne aucun handler — il ne serait JAMAIS appelé, sans "
                            + "erreur");
                    }
                }

                var modele = LectureYaml.Chemin(tache, "ansible.builtin.template")
                             ?? LectureYaml.Chemin(tache, "template");
                var source = LectureYaml.Texte(LectureYaml.Chemin(modele, "src"));

                if (!string.IsNullOrEmpty(source)
                    && !File.Exists(Path.Combine(basse, "templates", source)))
                {
                    fautes.Add(
                        $"infra/ansible/roles/{role} : template « {source} » absent de "
                        + "templates/");
                }
            }
        }

        fautes.AddRange(InventairesCommites(racine));
        fautes.AddRange(CollectionsDeclarees(racine));

        constats.Add(
            $"Ansible : {charges.Count} fichier(s), {rolesConnus.Count} rôle(s), "
            + $"{groupes.Count} groupe(s) connus.");

        return fautes;
    }

    /// <summary>La valeur de `stdout_callback` dans la section `[defaults]`.</summary>
    /// <remarks>
    /// LECTURE TEXTUELLE D'UN INI, PAS UN ANALYSEUR. Le Python passait par
    /// `configparser` ; ici on suit les en-têtes de section et on lit la première
    /// affectation. Une valeur posée par interpolation, ou une section incluse
    /// depuis un autre fichier, ne serait pas vue — `ansible.cfg` de ce dépôt
    /// n'en emploie aucune.
    /// </remarks>
    private static string? CallbackDeSortie(string texte)
    {
        var section = string.Empty;

        foreach (var brut in texte.Replace("\r\n", "\n").Split('\n'))
        {
            var ligne = brut.Trim();
            if (ligne.Length == 0 || ligne.StartsWith('#') || ligne.StartsWith(';'))
            {
                continue;
            }

            if (ligne.StartsWith('[') && ligne.EndsWith(']'))
            {
                section = ligne[1..^1].Trim();
                continue;
            }

            if (section != "defaults")
            {
                continue;
            }

            var separateur = ligne.IndexOf('=');
            if (separateur < 0)
            {
                continue;
            }

            if (ligne[..separateur].Trim() == "stdout_callback")
            {
                return ligne[(separateur + 1)..].Split('#')[0].Trim();
            }
        }

        return null;
    }

    /// <summary>Les tâches d'un document, `block`/`rescue` compris.</summary>
    private static List<object?> TachesAPlat(object? document)
    {
        var rendu = new List<object?>();
        var pile = new List<object?>(LectureYaml.Liste(document));

        while (pile.Count > 0)
        {
            var element = pile[^1];
            pile.RemoveAt(pile.Count - 1);

            var table = LectureYaml.Table(element);
            if (table is null)
            {
                continue;
            }

            rendu.Add(element);

            foreach (var imbrique in new[]
                     {
                         "block", "rescue", "always", "tasks", "pre_tasks", "post_tasks",
                     })
            {
                pile.AddRange(LectureYaml.Liste(LectureYaml.Chemin(element, imbrique)));
            }
        }

        return rendu;
    }

    /// <summary>Aucun inventaire RÉEL ne doit être suivi par Git.</summary>
    /// <remarks>
    /// CE CONTRÔLE SIGNALAIT LA PRÉSENCE DU FICHIER, ET C'ÉTAIT FAUX. Le
    /// `.example` dit lui-même « copier en `staging.yml` » : un déploiement
    /// SUPPOSE donc ce fichier sur le poste, et le signaler faisait échouer
    /// `make infra` dès qu'on suivait la procédure du dépôt. Un contrôle qui
    /// passe au rouge quand on fait ce qu'il faut finit par être ignoré — et il
    /// emmène les vrais défauts avec lui.
    ///
    /// Le danger n'est pas d'AVOIR le fichier, c'est de le COMMITTER. On
    /// interroge donc Git, pas le disque.
    ///
    /// UN `git` INTROUVABLE EST UNE FAUTE, PAS UN SILENCE. Le Python le disait
    /// déjà, et c'est la bonne réponse : sans Git, ce contrôle ne peut pas savoir
    /// si un inventaire de production dort dans l'historique.
    /// </remarks>
    private static List<string> InventairesCommites(string racine)
    {
        var fautes = new List<string>();
        var dossier = Path.Combine(racine, "inventory");

        if (!Directory.Exists(dossier)
            || !Directory.EnumerateFiles(dossier, "*.yml").Any())
        {
            return fautes;
        }

        var (code, sortie, _) = Programme.Lancer(
            "git", "-C", Depot.Racine, "ls-files", "--", "infra/ansible/inventory");

        if (code != 0)
        {
            fautes.Add(
                "infra/ansible/inventory : `git` indisponible — impossible de vérifier "
                + "qu'aucun inventaire réel n'est commité");
            return fautes;
        }

        foreach (var chemin in sortie
                     .Replace("\r\n", "\n")
                     .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                     .Select(x => x.Trim())
                     .OrderBy(x => x, StringComparer.Ordinal))
        {
            if (chemin.EndsWith(".yml", StringComparison.Ordinal)
                && !chemin.EndsWith(".yml.example", StringComparison.Ordinal))
            {
                fautes.Add(
                    $"{chemin} : inventaire réel COMMITÉ — il porte des IP et des noms "
                    + "d'hôtes de production ; seuls les `.yml.example` doivent être "
                    + "suivis");
            }
        }

        return fautes;
    }

    /// <summary>Toute collection employée doit être déclarée.</summary>
    /// <remarks>
    /// `roles/commun` emploie `ansible.posix.sysctl` et `ansible.posix.mount`,
    /// qui ne font pas partie d'`ansible-core`. Aucun `requirements.yml` ne les
    /// déclarait : sur un poste où Ansible vient de `pip install ansible-core`,
    /// le playbook s'arrêtait sur
    ///
    ///     couldn't resolve module/action 'ansible.posix.mount'
    ///     Origin: roles/commun/tasks/main.yml:51:3
    ///
    /// LE MESSAGE DÉSIGNE UNE LIGNE DU RÔLE, ET LE RÔLE EST CORRECT — ce qui
    /// manque est sur la machine, pas dans le dépôt. Et le défaut est
    /// INTERMITTENT selon l'installation : `pip install ansible` embarque la
    /// collection, `ansible-core` non. D'où un « ça marche chez moi » sincère.
    ///
    /// `ansible.builtin` est exclue : elle est dans le cœur, par définition.
    /// </remarks>
    private static List<string> CollectionsDeclarees(string racine)
    {
        var fautes = new List<string>();
        var utilisees = new SortedSet<string>(StringComparer.Ordinal);

        var sources = new List<string>();
        var dossierRoles = Path.Combine(racine, "roles");
        if (Directory.Exists(dossierRoles))
        {
            sources.AddRange(Depot.Fichiers(dossierRoles, ".yml"));
        }

        var dossierPlaybooks = Path.Combine(racine, "playbooks");
        if (Directory.Exists(dossierPlaybooks))
        {
            sources.AddRange(Directory.EnumerateFiles(dossierPlaybooks, "*.yml"));
        }

        foreach (var chemin in sources.OrderBy(f => f, StringComparer.Ordinal))
        {
            foreach (Match trouve in ModuleQualifie.Matches(File.ReadAllText(chemin)))
            {
                var collection = trouve.Groups[1].Value;
                if (!collection.StartsWith("ansible.builtin", StringComparison.Ordinal))
                {
                    utilisees.Add(collection);
                }
            }
        }

        var fichierRequis = Path.Combine(racine, "requirements.yml");
        var declarees = new HashSet<string>(StringComparer.Ordinal);

        if (!File.Exists(fichierRequis))
        {
            if (utilisees.Count > 0)
            {
                fautes.Add(
                    "infra/ansible/requirements.yml absent alors que les rôles emploient "
                    + string.Join(", ", utilisees));
            }

            return fautes;
        }

        var document = LectureYaml.Charge(File.ReadAllText(fichierRequis));
        foreach (var entree in LectureYaml.Liste(LectureYaml.Chemin(document, "collections")))
        {
            var nom = LectureYaml.Texte(LectureYaml.Chemin(entree, "name"))
                      ?? LectureYaml.Texte(entree);
            if (!string.IsNullOrEmpty(nom))
            {
                declarees.Add(nom);
            }
        }

        foreach (var collection in utilisees.Where(c => !declarees.Contains(c)))
        {
            fautes.Add(
                $"infra/ansible : la collection « {collection} » est employée par un rôle "
                + "mais absente de requirements.yml");
        }

        return fautes;
    }

    // ═════════════════════════════════════════════════════════════════════════
    // COMPOSE — LA PILE RÉELLEMENT LANCÉE
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>Cohérence OTLP, et bases injectées contre bases créées.</summary>
    /// <remarks>
    /// APPLICATIF = CONSTRUIT DEPUIS UN DOCKERFILE .NET DU DÉPÔT. `postgres`,
    /// `redis`, `kafka` et les interfaces d'appoint ne portent ni télémétrie ni
    /// chaîne de connexion applicative. `rembg` construit bien une image, mais
    /// c'est un service Python sans socle .NET : le retenir ferait une faute par
    /// exécution, pour un service qui n'a rien à déclarer.
    ///
    /// UN COLLECTEUR SE RECONNAÎT À SON IMAGE, PAS À SON NOM : le renommer ne
    /// doit pas rendre ce contrôle silencieux.
    /// </remarks>
    private static List<string> Compose(List<string> constats)
    {
        var fautes = new List<string>();

        if (!File.Exists(ComposeDev.Fichier))
        {
            fautes.Add(
                "docker-compose.dev.yml introuvable — la pile de développement a été "
                + "déplacée sans mettre ce contrôle à jour.");
            return fautes;
        }

        var services = ComposeDev.Services();
        var images = ImagesDuCompose();

        var metier = services
            .Where(s => (ComposeDev.Dockerfile(s) ?? string.Empty)
                .StartsWith("services/", StringComparison.Ordinal)
                || (ComposeDev.Dockerfile(s) ?? string.Empty)
                    .StartsWith("apps/", StringComparison.Ordinal))
            .ToList();

        // ── A. Collecteur et adresses OTLP ──────────────────────────────────
        var collecteurs = services
            .Where(s => ImageDeCollecteur.IsMatch(images.GetValueOrDefault(s.Nom, string.Empty)))
            .Select(s => s.Nom)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        var avecAdresse = services
            .Where(s => s.Environnement.Any(
                e => string.Equals(e.Key, "OPENTELEMETRY__ENDPOINT",
                         StringComparison.OrdinalIgnoreCase)
                     && e.Value.Trim().Length > 0))
            .Select(s => s.Nom)
            .ToHashSet(StringComparer.Ordinal);

        if (collecteurs.Count == 0 && avecAdresse.Count > 0)
        {
            fautes.Add(
                "docker-compose.dev.yml : aucun collecteur OTLP dans la pile, mais "
                + string.Join(", ", avecAdresse.OrderBy(n => n, StringComparer.Ordinal))
                + " pose(nt) une adresse `OPENTELEMETRY__ENDPOINT` non vide — le service "
                + "journalisera un échec de connexion toutes les quelques secondes, sans "
                + "que rien ne désigne la cause.");
        }

        if (collecteurs.Count > 0)
        {
            var muets = metier
                .Where(s => !avecAdresse.Contains(s.Nom))
                .Select(s => s.Nom)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToList();

            if (muets.Count > 0)
            {
                fautes.Add(
                    $"docker-compose.dev.yml : un collecteur ({string.Join(", ", collecteurs)}) "
                    + $"tourne, mais {muets.Count} service(s) n'ont pas d'adresse OTLP — "
                    + string.Join(", ", muets.Take(5))
                    + (muets.Count > 5 ? "…" : string.Empty)
                    + ". Ils démarrent muets, sans erreur.");
            }
        }

        // ── B. Bases injectées contre bases créées ──────────────────────────
        var initialisation = Depot.Chemin(
            "infra", "postgres", "init", "001-create-databases.sql");

        if (!File.Exists(initialisation))
        {
            fautes.Add(
                "infra/postgres/init/001-create-databases.sql introuvable — le montage "
                + "`/docker-entrypoint-initdb.d` de la pile pointe dans le vide.");
        }
        else
        {
            var creees = BaseCreee.Matches(File.ReadAllText(initialisation))
                .Select(m => m.Groups[1].Value)
                .ToHashSet(StringComparer.Ordinal);

            var injectees = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var service in metier)
            {
                foreach (var valeur in service.Environnement.Values)
                {
                    foreach (Match trouve in BaseInjectee.Matches(valeur))
                    {
                        injectees.TryAdd(trouve.Groups[1].Value, service.Nom);
                    }
                }
            }

            foreach (var (base_, service) in injectees
                         .Where(b => !creees.Contains(b.Key))
                         .OrderBy(b => b.Key, StringComparer.Ordinal))
            {
                fautes.Add(
                    $"docker-compose.dev.yml : `{base_}` est injectée à `{service}` mais "
                    + "absente de infra/postgres/init/001-create-databases.sql — sur un "
                    + "volume neuf, ce service est le seul à échouer. `Database.Migrate()` "
                    + "masque l'oubli en développement, pas en production où "
                    + "`MigrateOnStartup=false`.");
            }
        }

        var etat = collecteurs.Count > 0
            ? $"collecteur {collecteurs[0]}"
            : "sans collecteur (adresses vides)";

        constats.Add(
            $"Compose : {metier.Count} service(s) applicatif(s), {etat}, "
            + $"{services.Count} service(s) au total.");

        return fautes;
    }

    /// <summary>L'`image:` de chaque service du compose.</summary>
    /// <remarks>
    /// CETTE LECTURE DOUBLE `ComposeDev`, ET C'EST UNE DETTE, PAS UN CHOIX.
    ///
    /// `ServiceCompose` porte le nom, l'environnement et le `build:` — pas
    /// l'`image:`, dont ce contrôle a besoin pour reconnaître un collecteur OTLP
    /// à ce qu'il EST plutôt qu'à son nom. Ajouter le champ obligerait à toucher
    /// `AdressesServiceControle.cs`, qui héberge `ComposeDev` « faute de mieux » :
    /// c'est là que ces quinze lignes doivent aller le jour où ce type prendra
    /// son propre fichier.
    ///
    /// Elle reprend donc exactement la forme que `ComposeDev` reconnaît — nom de
    /// service à deux espaces, clés à quatre — et rien de plus. Un service dont
    /// l'image est posée autrement (une ancre, une surcharge) serait vu sans
    /// image, donc jamais comme un collecteur.
    /// </remarks>
    private static Dictionary<string, string> ImagesDuCompose()
    {
        var images = new Dictionary<string, string>(StringComparer.Ordinal);
        string? service = null;

        foreach (var ligne in File.ReadAllText(ComposeDev.Fichier)
                     .Replace("\r\n", "\n")
                     .Split('\n'))
        {
            var entete = NomDeService.Match(ligne);
            if (entete.Success)
            {
                service = entete.Groups[1].Value;
                continue;
            }

            if (ligne.Length > 0 && !ligne.StartsWith(' '))
            {
                service = null;
                continue;
            }

            if (service is null)
            {
                continue;
            }

            var image = LigneDImage.Match(ligne);
            if (image.Success)
            {
                images[service] = image.Groups[1].Value.Trim('"', '\'');
            }
        }

        return images;
    }
}
