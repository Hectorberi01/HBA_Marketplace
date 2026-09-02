using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace HBA.Controls.Controles;

/// <summary>Lancer un outil externe, et savoir dire qu'il est absent.</summary>
/// <remarks>
/// ═══════════════════════════════════════════════════════════════════════════
/// UN OUTIL ABSENT N'EST PAS UN CONTRÔLE QUI PASSE.
///
/// <see cref="K8sControle"/> lance `kustomize`, <see cref="InfraControle"/>
/// lance `git`. Les deux scripts Python d'origine traitaient l'absence de leur
/// outil comme un non-événement : « kustomize absent — le reste du contrôle est
/// ignoré », puis code de sortie 0. Ici, <see cref="Lancer"/> rend un code -1
/// que l'appelant DOIT traiter, et les deux le portent en
/// <see cref="Verdict.NonCouvert"/>.
///
/// LES DEUX FLUX SONT LUS AVANT D'ATTENDRE LA FIN. `kustomize build` rend des
/// centaines de kilo-octets : attendre la sortie du processus avant de lire
/// remplirait le tampon du tube et bloquerait les deux côtés pour toujours.
///
/// CE QUE CE TYPE NE FAIT PAS : il ne pose aucun délai maximal. Un outil qui se
/// bloque bloque le contrôle, et rien ici ne le dira. Aucun des deux appels
/// n'est réseau, ce qui rend le risque acceptable et non nul.
/// ═══════════════════════════════════════════════════════════════════════════
/// </remarks>
internal static class Programme
{
    /// <summary>Lance un programme externe. Code -1 s'il est introuvable.</summary>
    public static (int Code, string Sortie, string Erreur) Lancer(string programme, params string[] arguments)
    {
        var demarrage = new ProcessStartInfo(programme)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var argument in arguments)
        {
            demarrage.ArgumentList.Add(argument);
        }

        try
        {
            using var processus = Process.Start(demarrage);
            if (processus is null)
            {
                return (-1, string.Empty, $"{programme} n'a pas pu être démarré");
            }

            var sortie = processus.StandardOutput.ReadToEndAsync();
            var erreur = processus.StandardError.ReadToEndAsync();
            processus.WaitForExit();
            return (processus.ExitCode, sortie.Result, erreur.Result);
        }
        catch (Exception souci) when (souci is System.ComponentModel.Win32Exception
                                          or InvalidOperationException
                                          or IOException)
        {
            return (-1, string.Empty, souci.Message);
        }
    }
}

/// <summary>
/// Le sous-ensemble de YAML que ces contrôles doivent savoir lire.
/// </summary>
/// <remarks>
/// ═══════════════════════════════════════════════════════════════════════════
/// CE TYPE EXISTE PARCE QUE LE CONTRÔLE PYTHON S'AUTORISAIT À NE RIEN VÉRIFIER.
///
/// `check-k8s.py` et `check-infra.py` chargeaient tout par PyYAML, et quand le
/// paquet manquait ils imprimaient « PyYAML absent — contrôle ignoré » puis
/// rendaient 0. Une barrière verte qui n'a rien regardé : le défaut que ce dépôt
/// a déjà corrigé quatre fois ailleurs. Cet outil n'a AUCUNE référence de paquet
/// et ne doit pas en acquérir (voir l'encadré de `HBA.Controls.csproj`) : la
/// lecture est donc écrite ici.
///
/// CE N'EST PAS UN ANALYSEUR YAML, ET IL NE FAUT PAS LE PRENDRE POUR TEL.
///
/// CE QU'IL SAIT LIRE — et c'est la forme que ce dépôt écrit, dans les 105
/// fichiers de `k8s/` et les 13 d'`infra/ansible/` :
///
///   · les tables et les listes en style de BLOC, à indentation par espaces ;
///   · une liste écrite À LA MÊME COLONNE que sa clé (`clusters:` puis `- …` en
///     colonne 0) — la forme qu'écrit `kubectl`, et qu'une première version
///     rendait nulle ;
///   · les scalaires nus, entre guillemets simples ou doubles, avec leurs
///     échappements respectifs ;
///   · les scalaires de bloc `|`, `|-`, `|+`, `&gt;`, `&gt;-`, `&gt;+` — y compris
///     comme ÉLÉMENT de liste (`- |`), dont l'indentation minimale est celle du
///     contenu et non celle du tiret plus un ;
///   · les collections en style de FLUX, `[a, b]` et `{ cle: valeur }`, vides ou
///     non ;
///   · les documents séparés par `---`, et les commentaires de fin de ligne.
///
/// CE QU'IL NE SAIT PAS LIRE, ET QUI PASSERAIT DONC EN SILENCE :
///
///   · les ancres, les alias et la clé de fusion — un document qui en emploie
///     serait lu amputé ;
///   · les étiquettes de type (`!!str`, `!Ref`) et les clés complexes ;
///   · un scalaire nu qui court sur plusieurs lignes ;
///   · les dates : `2024-01-01` reste du TEXTE ici, là où PyYAML rend un objet
///     date. C'est le seul écart mesuré sur `infra/` (voir plus bas) ;
///   · et surtout : IL NE VALIDE RIEN. Un fichier syntaxiquement cassé est lu
///     sans broncher, et rend une image partielle au lieu d'une erreur. Là où
///     `check-k8s.py` disait « YAML invalide », ce lecteur dira « clé absente ».
///     La faute sera criée au bon endroit, avec le mauvais motif.
///
/// L'ÉCART A ÉTÉ MESURÉ, ET IL FAUT LE DIRE EN CHIFFRES.
///
/// Ce lecteur a été prototypé en Python et confronté à `yaml.safe_load_all` sur
/// les fichiers que ces deux contrôles lisent réellement :
///
///   · `k8s/**/*.yaml`        : 105 fichiers, 105 structures IDENTIQUES ;
///   · `infra/ansible/**`     :  13 fichiers,  13 structures IDENTIQUES ;
///   · les 66 corps de patch `patch: |-` en ligne, relus une seconde fois comme
///     documents YAML : 66 identiques ;
///   · `infra/**` en entier   :  20 fichiers,  19 identiques — le seul écart est
///     `infra/observability/loki/loki.yml`, où `from: 2024-01-01` est une DATE
///     pour PyYAML et une chaîne ici. Aucun contrôle ne lit ce fichier.
///
/// CE CHIFFRE VAUT POUR LES FICHIERS D'AUJOURD'HUI, PAS POUR CEUX DE DEMAIN. Le
/// jour où quelqu'un écrit une ancre dans un manifeste, ce lecteur la lira mal
/// et personne ne le dira. La forme de ces fichiers est tenue par ce dépôt, pas
/// par un tiers — c'est la seule raison pour laquelle ce recul est acceptable.
///
/// CE TYPE VIT DANS CE FICHIER FAUTE DE MIEUX. <see cref="K8sControle"/> et
/// <see cref="InfraControle"/> en ont tous deux besoin ; deux copies
/// divergeraient, et c'est celle qui se tait qu'on croirait. Sa place est un
/// `LectureYaml.cs` à lui, le jour où le portage rouvrira ce dossier — comme
/// <c>ComposeDev</c>, qui vit chez `AdressesServiceControle` pour la même raison.
/// ═══════════════════════════════════════════════════════════════════════════
/// </remarks>
internal static class LectureYaml
{
    private static readonly string[] Indicateurs = ["|", "|-", "|+", ">", ">-", ">+"];

    private static readonly Regex Cle = new(
        @"^(?:""([^""]*)""|'([^']*)'|([^:#]+?))\s*:(?:\s+(.*))?$", RegexOptions.Compiled);

    private static readonly Regex Entier = new(@"^[-+]?[0-9]+$", RegexOptions.Compiled);

    private static readonly Regex Reel = new(@"^[-+]?[0-9]*\.[0-9]+$", RegexOptions.Compiled);

    /// <summary>Tous les documents d'un texte YAML.</summary>
    public static List<object?> Documents(string texte)
    {
        var lignes = texte.Replace("\r\n", "\n").Split('\n');
        var documents = new List<object?>();
        var i = 0;

        while (i < lignes.Length)
        {
            var j = Saut(lignes, i);
            if (j >= lignes.Length)
            {
                break;
            }

            var s = lignes[j].Trim();
            if (s == "---" || s.StartsWith("--- ", StringComparison.Ordinal))
            {
                var (noeud, apres) = LireValeur(lignes, j + 1, 0);
                documents.Add(noeud);
                i = apres <= j ? j + 1 : apres;
                continue;
            }

            if (s == "...")
            {
                i = j + 1;
                continue;
            }

            var (racine, suite) = LireValeur(lignes, j, 0);
            documents.Add(racine);
            i = suite <= j ? j + 1 : suite;
        }

        return documents;
    }

    /// <summary>Le premier document, ou <c>null</c>.</summary>
    public static object? Charge(string texte)
    {
        foreach (var document in Documents(texte))
        {
            return document;
        }

        return null;
    }

    /// <summary>Le nœud vu comme une table, ou <c>null</c>.</summary>
    public static Dictionary<string, object?>? Table(object? noeud)
        => noeud as Dictionary<string, object?>;

    /// <summary>Le nœud vu comme une liste — VIDE s'il n'en est pas une.</summary>
    /// <remarks>
    /// UNE LISTE ABSENTE ET UNE LISTE VIDE SE LISENT PAREIL ICI, et c'est un
    /// choix : les appelants itèrent, ils ne comptent pas. Celui qui doit
    /// distinguer les deux interroge <see cref="Table"/> lui-même.
    /// </remarks>
    public static List<object?> Liste(object? noeud)
        => noeud as List<object?> ?? [];

    /// <summary>Le nœud vu comme un texte, ou <c>null</c>.</summary>
    /// <remarks>
    /// UNE COLLECTION N'EST PAS UN TEXTE, ET RENDRE SON NOM DE TYPE SERAIT PIRE
    /// QUE RENDRE NULL. `Convert.ToString` sur une liste rend
    /// « System.Collections.Generic.List`1[System.Object] » : un `notify:` écrit
    /// sous forme de liste serait alors cherché parmi les handlers sous CE
    /// nom-là, et le contrôle crierait une faute inventée en passant à côté des
    /// vraies. Les appelants qui acceptent les deux formes interrogent
    /// <see cref="Liste"/> quand ceci rend <c>null</c>.
    /// </remarks>
    public static string? Texte(object? noeud)
        => noeud switch
        {
            null => null,
            string s => s,
            bool b => b ? "true" : "false",
            List<object?> => null,
            Dictionary<string, object?> => null,
            _ => Convert.ToString(noeud, System.Globalization.CultureInfo.InvariantCulture),
        };

    /// <summary>Descend une suite de clés, en s'arrêtant au premier trou.</summary>
    public static object? Chemin(object? noeud, params string[] cles)
    {
        foreach (var cle in cles)
        {
            var table = Table(noeud);
            if (table is null || !table.TryGetValue(cle, out noeud))
            {
                return null;
            }
        }

        return noeud;
    }

    private static bool Vide(string ligne)
    {
        var s = ligne.Trim();
        return s.Length == 0 || s.StartsWith('#');
    }

    private static int Indentation(string ligne)
        => ligne.Length - ligne.TrimStart(' ').Length;

    private static int Saut(string[] lignes, int i)
    {
        while (i < lignes.Length && Vide(lignes[i]))
        {
            i++;
        }

        return i;
    }

    private static (object? Noeud, int Suivant) LireValeur(string[] lignes, int i, int minimum)
    {
        i = Saut(lignes, i);
        if (i >= lignes.Length)
        {
            return (null, i);
        }

        var ligne = lignes[i];
        if (Indentation(ligne) < minimum)
        {
            return (null, i);
        }

        var s = ligne.Trim();
        if (s == "---" || s == "..." || s.StartsWith("--- ", StringComparison.Ordinal))
        {
            return (null, i);
        }

        if (s == "-" || s.StartsWith("- ", StringComparison.Ordinal))
        {
            return LireSequence(lignes, i, Indentation(ligne));
        }

        if (Indicateurs.Contains(s))
        {
            // L'INDENTATION MINIMALE EST CELLE DU CONTENU DE L'ÉLÉMENT, PAS CELLE
            // DU TIRET PLUS UN : `- |` posé à 12 espaces porte son corps à 14.
            // Avec « tiret + 1 », le corps entier était sauté et la valeur rendue
            // valait un simple saut de ligne.
            var (texte, apres) = LireBlocScalaire(lignes, i + 1, minimum, s);
            return (texte, apres);
        }

        if (s.StartsWith('{') || s.StartsWith('['))
        {
            var (flux, _) = LireFlux(s, 0);
            return (flux, i + 1);
        }

        if (!Cle.IsMatch(s))
        {
            // Un scalaire nu : élément de liste, ou valeur sur sa propre ligne.
            return (Scalaire(s), i + 1);
        }

        return LireMapping(lignes, i, Indentation(ligne));
    }

    private static (object? Noeud, int Suivant) LireSequence(string[] lignes, int i, int indent)
    {
        var items = new List<object?>();

        while (true)
        {
            i = Saut(lignes, i);
            if (i >= lignes.Length)
            {
                break;
            }

            var ligne = lignes[i];
            if (Indentation(ligne) != indent)
            {
                break;
            }

            var s = ligne.Trim();
            if (s == "---" || s == "...")
            {
                break;
            }

            if (s == "-")
            {
                var (seul, apres) = LireValeur(lignes, i + 1, indent + 1);
                items.Add(seul);
                i = apres;
                continue;
            }

            if (!s.StartsWith("- ", StringComparison.Ordinal))
            {
                break;
            }

            var reste = ligne[(indent + 1)..];
            var supplement = reste.Length - reste.TrimStart(' ').Length;
            var position = indent + 1 + supplement;

            // ON RÉÉCRIT LA LIGNE PLUTÔT QUE DE PORTER UN DÉCALAGE PARTOUT : le
            // tiret devient de l'espace, et le contenu de l'élément se lit comme
            // n'importe quel nœud commençant à sa colonne.
            lignes[i] = new string(' ', position) + ligne[position..];
            var (valeur, suivant) = LireValeur(lignes, i, position);
            items.Add(valeur);
            i = suivant;
        }

        return (items, i);
    }

    private static (object? Noeud, int Suivant) LireMapping(string[] lignes, int i, int indent)
    {
        var table = new Dictionary<string, object?>(StringComparer.Ordinal);

        while (true)
        {
            i = Saut(lignes, i);
            if (i >= lignes.Length)
            {
                break;
            }

            var ligne = lignes[i];
            if (Indentation(ligne) != indent)
            {
                break;
            }

            var s = ligne.Trim();
            if (s == "---" || s == "..." || s.StartsWith("- ", StringComparison.Ordinal))
            {
                break;
            }

            var trouve = Cle.Match(s);
            if (!trouve.Success)
            {
                break;
            }

            var cle = trouve.Groups[1].Success
                ? trouve.Groups[1].Value
                : trouve.Groups[2].Success
                    ? trouve.Groups[2].Value
                    : trouve.Groups[3].Value.Trim();

            var reste = trouve.Groups[4].Value.Trim();
            if (reste.StartsWith('#'))
            {
                reste = string.Empty;
            }

            if (Indicateurs.Contains(reste))
            {
                var (texte, apres) = LireBlocScalaire(lignes, i + 1, indent + 1, reste);
                table[cle] = texte;
                i = apres;
                continue;
            }

            if (reste.Length > 0)
            {
                table[cle] = Scalaire(reste);
                i++;
                continue;
            }

            i++;

            // UNE LISTE PEUT ÊTRE ÉCRITE À LA MÊME COLONNE QUE SA CLÉ. `clusters:`
            // puis `- cluster:` en colonne 0 est du YAML valide, et c'est ce
            // qu'écrit `kubectl`. Sans ce cas, la valeur était nulle et le
            // kubeconfig se lisait comme un document vide.
            var j = Saut(lignes, i);
            if (j < lignes.Length
                && Indentation(lignes[j]) == indent
                && (lignes[j].Trim() == "-"
                    || lignes[j].Trim().StartsWith("- ", StringComparison.Ordinal)))
            {
                var (liste, apres) = LireSequence(lignes, j, indent);
                table[cle] = liste;
                i = apres;
                continue;
            }

            var (valeur, suite) = LireValeur(lignes, i, indent + 1);
            table[cle] = valeur;
            i = suite;
        }

        return (table, i);
    }

    private static (string Texte, int Suivant) LireBlocScalaire(string[] lignes, int i, int minimum, string style)
    {
        var corps = new List<string>();
        var baseIndentation = -1;

        while (i < lignes.Length)
        {
            var ligne = lignes[i];
            if (ligne.Trim().Length == 0)
            {
                corps.Add(string.Empty);
                i++;
                continue;
            }

            if (Indentation(ligne) < minimum)
            {
                break;
            }

            if (baseIndentation < 0)
            {
                baseIndentation = Indentation(ligne);
            }

            corps.Add(ligne.Length >= baseIndentation ? ligne[baseIndentation..] : string.Empty);
            i++;
        }

        while (corps.Count > 0 && corps[^1].Length == 0)
        {
            corps.RemoveAt(corps.Count - 1);
        }

        string texte;
        if (style[0] == '|')
        {
            texte = string.Join("\n", corps);
        }
        else
        {
            // LE PLIAGE EST APPROXIMATIF, ET C'EST ASSUMÉ. Aucun contrôle ne
            // compare un scalaire plié à autre chose qu'à lui-même : ce sont des
            // messages Ansible. Un `|` littéral, lui, est relu comme du YAML — il
            // est donc rendu au caractère près.
            var morceaux = new StringBuilder();
            foreach (var c in corps)
            {
                if (c.Length == 0)
                {
                    morceaux.Append('\n');
                }
                else if (morceaux.Length > 0 && morceaux[^1] != '\n' && !c.StartsWith(' '))
                {
                    morceaux.Append(' ').Append(c);
                }
                else
                {
                    morceaux.Append(c);
                }
            }

            texte = morceaux.ToString();
        }

        if (style.Length == 1 || style[^1] == '+')
        {
            texte += "\n";
        }

        return (texte, i);
    }

    private static object? Scalaire(string brut)
    {
        var t = brut.Trim();
        if (t.Length == 0)
        {
            return null;
        }

        if (t[0] == '"')
        {
            return EntreGuillemetsDoubles(t, 1).Valeur;
        }

        if (t[0] == '\'')
        {
            return EntreGuillemetsSimples(t, 1).Valeur;
        }

        // Un commentaire de fin de ligne commence par un blanc puis un dièse ; un
        // dièse collé à la valeur en fait partie.
        var diese = t.IndexOf(" #", StringComparison.Ordinal);
        if (diese >= 0)
        {
            t = t[..diese].Trim();
        }

        if (t.Length == 0 || t is "null" or "~" or "Null" or "NULL")
        {
            return null;
        }

        if (t is "true" or "True" or "TRUE" or "yes" or "Yes" or "YES" or "on" or "On" or "ON")
        {
            return true;
        }

        if (t is "false" or "False" or "FALSE" or "no" or "No" or "NO" or "off" or "Off" or "OFF")
        {
            return false;
        }

        if (t.StartsWith('{') || t.StartsWith('['))
        {
            return LireFlux(t, 0).Noeud;
        }

        if (Entier.IsMatch(t) && long.TryParse(t, out var entier))
        {
            return entier;
        }

        if (Reel.IsMatch(t)
            && double.TryParse(t, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var reel))
        {
            return reel;
        }

        return t;
    }

    private static (string Valeur, int Suivant) EntreGuillemetsDoubles(string t, int j)
    {
        var sortie = new StringBuilder();
        while (j < t.Length)
        {
            if (t[j] == '\\' && j + 1 < t.Length)
            {
                var c = t[j + 1];
                sortie.Append(c switch
                {
                    'n' => '\n',
                    't' => '\t',
                    '"' => '"',
                    '\\' => '\\',
                    _ => c,
                });
                j += 2;
                continue;
            }

            if (t[j] == '"')
            {
                j++;
                break;
            }

            sortie.Append(t[j]);
            j++;
        }

        return (sortie.ToString(), j);
    }

    private static (string Valeur, int Suivant) EntreGuillemetsSimples(string t, int j)
    {
        var sortie = new StringBuilder();
        while (j < t.Length)
        {
            if (t[j] == '\'' && j + 1 < t.Length && t[j + 1] == '\'')
            {
                sortie.Append('\'');
                j += 2;
                continue;
            }

            if (t[j] == '\'')
            {
                j++;
                break;
            }

            sortie.Append(t[j]);
            j++;
        }

        return (sortie.ToString(), j);
    }

    private static int SautDeFlux(string t, int i)
    {
        while (i < t.Length && (t[i] == ' ' || t[i] == '\t'))
        {
            i++;
        }

        return i;
    }

    private static (object? Noeud, int Suivant) ScalaireDeFlux(string t, int i)
    {
        if (i < t.Length && (t[i] == '"' || t[i] == '\''))
        {
            var (texte, apres) = t[i] == '"'
                ? EntreGuillemetsDoubles(t, i + 1)
                : EntreGuillemetsSimples(t, i + 1);
            return (texte, apres);
        }

        var j = i;
        while (j < t.Length)
        {
            if (t[j] == ',' || t[j] == ']' || t[j] == '}')
            {
                break;
            }

            // UN « : » SUIVI D'UN BLANC TERMINE UNE CLÉ EN STYLE DE FLUX. Sans cet
            // arrêt, `{ nom: x, valeur: "1" }` rendait UNE seule clé « nom: x » de
            // valeur nulle — six boucles Ansible lues à l'envers.
            if (t[j] == ':'
                && (j + 1 >= t.Length || t[j + 1] == ' ' || t[j + 1] == '\t'
                    || t[j + 1] == ',' || t[j + 1] == ']' || t[j + 1] == '}'))
            {
                break;
            }

            j++;
        }

        return (Scalaire(t[i..j]), j);
    }

    private static (object? Noeud, int Suivant) LireFlux(string t, int i)
    {
        i = SautDeFlux(t, i);
        if (i >= t.Length)
        {
            return (null, i);
        }

        if (t[i] == '[')
        {
            var items = new List<object?>();
            i++;
            while (true)
            {
                i = SautDeFlux(t, i);
                if (i >= t.Length || t[i] == ']')
                {
                    return (items, i + 1);
                }

                var (valeur, apres) = LireFlux(t, i);
                items.Add(valeur);
                i = SautDeFlux(t, apres);
                if (i < t.Length && t[i] == ',')
                {
                    i++;
                }
            }
        }

        if (t[i] == '{')
        {
            var table = new Dictionary<string, object?>(StringComparer.Ordinal);
            i++;
            while (true)
            {
                i = SautDeFlux(t, i);
                if (i >= t.Length || t[i] == '}')
                {
                    return (table, i + 1);
                }

                var (cle, apresCle) = ScalaireDeFlux(t, i);
                i = SautDeFlux(t, apresCle);
                object? valeur = null;
                if (i < t.Length && t[i] == ':')
                {
                    var (lue, apresValeur) = LireFlux(t, i + 1);
                    valeur = lue;
                    i = apresValeur;
                }

                table[Texte(cle) ?? string.Empty] = valeur;
                i = SautDeFlux(t, i);
                if (i < t.Length && t[i] == ',')
                {
                    i++;
                }
            }
        }

        return ScalaireDeFlux(t, i);
    }
}

/// <summary>
/// Les manifestes disent-ils encore ce que le cahier infrastructure exige ?
/// </summary>
/// <remarks>
/// ═══════════════════════════════════════════════════════════════════════════
/// CE CONTRÔLE CONSTRUIT VRAIMENT LES OVERLAYS, IL NE LIT PAS LES FICHIERS.
///
/// Lire `base/` ne prouve rien : c'est l'overlay qui décide, et un patch peut
/// défaire silencieusement ce que la base garantissait. Un `op: replace` sur
/// `/spec/template/spec/containers/0/securityContext` passe la revue de code
/// sans qu'on le remarque, et le résultat ne se voit qu'en construisant.
///
/// Ce que ce contrôle vérifie, et pourquoi chaque règle existe :
///
///   • non-root (§19)         — huit images du dépôt tournaient en root sans que
///                              rien ne le signale ; le SecurityContext est ce
///                              qui l'empêche de revenir. LES StatefulSet AUSSI,
///                              et c'était un oubli DÉJÀ EN PLACE : Redis et
///                              MinIO échappaient entièrement à la vérification,
///                              alors que ce sont les deux pods qui tiennent des
///                              données ;
///   • trois sondes (§7)      — une readiness manquante fait recevoir du trafic
///                              à un pod qui n'a pas fini de démarrer, pendant
///                              chaque déploiement ;
///   • requests + limits (§7) — sans requests le scheduler place à l'aveugle ;
///                              sans limits un pod affame ses voisins ;
///   • pas de `latest` en prod (§13) — un `kubectl apply` rejoué doit redéployer
///                              la MÊME image, sinon le rollback ne veut rien
///                              dire ;
///   • deny-all présent (§5)  — sans lui, tout pod parle à tout pod. Et couper
///                              l'egress sans rouvrir le DNS casse tout, avec un
///                              symptôme qui MENT : l'erreur désigne le service
///                              visé, pas la résolution ;
///   • aucun secret en clair (§12) — le pire des défauts silencieux : ça marche.
///
/// ═══ CE QUE LE PORTAGE A CHANGÉ, ET IL FAUT LE LIRE AVANT DE FAIRE CONFIANCE
///
/// LE PYTHON RENDAIT 0 DANS TROIS CAS, SANS AVOIR RIEN CONSTRUIT.
///
///   1. PyYAML absent : « contrôle des cibles de patch ignoré », puis 0.
///   2. `kustomize` absent : « le reste du contrôle est ignoré », puis 0. C'est
///      LE CAS COURANT sur un poste, et c'est ce qui s'est produit en établissant
///      la référence chiffrée de ce portage : le script a imprimé cette ligne et
///      rendu 0 SANS UNE SEULE LIGNE DE BILAN.
///   3. `kustomize` en version 4 : « trop ancien — contrôle ignoré », puis 0.
///
/// Les trois deviennent ici des entrées de <see cref="Verdict.NonCouvert"/> :
/// le lanceur les imprime à la fin, sous « Ce qui n'est PAS couvert ». Un
/// contrôle qui n'a pas pu regarder ne rend plus le même vert qu'un contrôle qui
/// a tout vu. Le premier cas disparaît complètement : <see cref="LectureYaml"/>
/// remplace PyYAML, et l'écart mesuré est NUL sur les 105 manifestes de `k8s/`.
///
/// `verifier_chaines_de_connexion` NE VÉRIFIAIT DÉJÀ PLUS RIEN, ET LE DISAIT.
///
/// Il confrontait la table `CLES` de `scripts/db/secret-depuis-motsdepasse.py`
/// au gabarit versionné : une clé déclarée d'un côté et absente de l'autre était
/// poussée vide, et le service partait avec un mot de passe vide vers une base
/// qui, elle, en attendait un. Le générateur a été supprimé avec l'outillage
/// Python le 2 septembre 2026, et un fichier seul ne se compare à rien. Sa
/// fonction rendait une liste vide, avec un encadré disant « À REPRENDRE DANS
/// L'OUTIL DE CONTRÔLES .NET ». Ce n'est PAS repris ici — il n'y a toujours
/// qu'un seul terme — mais c'est écrit dans `NonCouvert`, où cela se lit à
/// chaque exécution au lieu de dormir dans une docstring.
///
/// LE GÉNÉRATEUR DE TOPICS N'EXISTE PLUS NON PLUS. Le Python lançait
/// `scripts/k8s-kafka-topics.py --verifie` s'il le trouvait, et sautait l'étape
/// EN SILENCE sinon. Le fichier a disparu ; `KafkaTopicsControle` tient
/// désormais cette question, et c'est dit dans `NonCouvert` plutôt que sauté.
/// ═══════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed class K8sControle : IControle
{
    /// <inheritdoc/>
    public string Nom => "k8s";

    /// <inheritdoc/>
    public string Resume => "les overlays construits disent ce que le cahier exige";

    private static readonly string[] Overlays = ["dev", "staging", "prod"];

    // Placeholders attendus : ce sont des NOMS de clés, jamais des valeurs.
    private static readonly string[] ClesSecretes =
        ["SIGNINGKEY", "APIKEY", "PASSWORD", "SECRET", "CONNECTIONSTRINGS"];

    private static readonly Regex CleDeSecret = new(
        @"^\s{2,}([A-Za-z0-9_.\-]+)\s*:\s*(.*)$", RegexOptions.Compiled);

    private static readonly Regex ValeurCitee = new(@"^""([^""]*)""", RegexOptions.Compiled);

    private static readonly Regex ValeurDePatch = new(
        @"^\s*value:\s*(\S+)\s*$", RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex NomDHote = new(
        @"^[a-z0-9.\-]+\.[a-z]{2,}$", RegexOptions.Compiled);

    private static readonly Regex ImageDOverlay = new(
        "^  - name: (hba/[a-z0-9-]+)\n    newName: (\\S+)\n"
        + "    newTag:[ \t]*\"?([^\"\n]*?)\"?[ \t]*$",
        RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex ServiceDeploye = new(
        @"^\s*-\s+([a-z0-9\-]+-service)\s*$", RegexOptions.Compiled);

    private static readonly Regex VersionLue = new(@"v(\d+)\.(\d+)\.(\d+)", RegexOptions.Compiled);

    /// <inheritdoc/>
    public Verdict Executer()
    {
        var fautes = new List<string>();
        var constats = new List<string>();
        var nonCouvert = new List<string>
        {
            "la VALIDITÉ YAML des manifestes : ils sont lus par un lecteur de "
            + "sous-ensemble écrit dans ce fichier (aucune dépendance de paquet), qui ne "
            + "connaît ni les ancres, ni les alias, ni la clé de fusion, ni les étiquettes "
            + "de type. Un fichier cassé est lu sans broncher et rend une image partielle : "
            + "la faute sera criée, avec le mauvais motif. `kubectl apply --dry-run` et "
            + "`kustomize build` sont les vraies réponses",
            "que la clé `CONNECTIONSTRINGS__*` déclarée dans `secret.yaml` soit "
            + "effectivement posée dans le Secret du cluster, que l'utilisateur Postgres "
            + "soit dérivé du nom de base plutôt qu'écrit en dur, et que le namespace visé "
            + "soit celui de l'overlay de production et non `hba`. Le générateur qui "
            + "servait de second terme a été supprimé le 2 septembre 2026 : il n'y a plus "
            + "rien à comparer, et un Secret posé dans un namespace que personne ne lit ne "
            + "provoque aucune erreur à l'`apply`, seulement un CreateContainerConfigError "
            + "plus tard",
            "les secrets posés ailleurs que dans `k8s/base/common/secret*.yaml` : un "
            + "secret dans un ConfigMap, dans un overlay, ou dans n'importe quel autre "
            + "fichier passe au travers. L'historique Git n'est pas regardé non plus — ce "
            + "contrôle dit ce qui est là maintenant, pas ce qui y a été",
            "que les domaines des overlays RÉSOLVENT, qu'ils pointent la bonne machine ou "
            + "qu'un certificat existe : seul le caractère réservé (RFC 2606) est vérifié, "
            + "et seulement dans les valeurs de patch — un domaine posé dans le ConfigMap "
            + "(`NOTIFICATIONS__EMAIL__APPBASEURL`) n'est pas vu",
            "que les images des Jobs de migration existent dans le registre, ni qu'un tag "
            + "désigne encore le même contenu : deux fichiers du dépôt sont comparés, rien "
            + "de plus. Un tag mutable peut pointer deux images à deux jours d'intervalle",
            "les topics Kafka : `scripts/k8s-kafka-topics.py --verifie`, que le contrôle "
            + "Python lançait quand il le trouvait et sautait EN SILENCE sinon, n'existe "
            + "plus. `KafkaTopicsControle` tient cette question — mais lui non plus ne "
            + "construit pas les overlays",
        };

        // ═════════════════════════════════════════════════════════════════════
        // CES CONTRÔLES-CI TOURNENT MÊME SANS KUSTOMIZE, ET C'EST VOLONTAIRE.
        //
        // Ils sont placés AVANT le garde-fou : sur un poste sans kustomize — le
        // cas courant — tout le fichier Python rendait 0 sans rien vérifier. Une
        // cible de patch qui ne désigne rien se lit dans les fichiers, sans
        // construire quoi que ce soit.
        // ═════════════════════════════════════════════════════════════════════
        fautes.AddRange(SecretsVides());
        fautes.AddRange(HotesDesOverlays());
        fautes.AddRange(ImagesDeMigration());
        fautes.AddRange(CiblesDePatch());
        fautes.AddRange(MontagesDeSecret());

        constats.Add($"{fautes.Count} écart(s) relevé(s) sans construire les overlays.");

        // ═════════════════════════════════════════════════════════════════════
        // LE RENDU RÉEL — ET CE QUI SE PASSE QUAND L'OUTIL MANQUE.
        // ═════════════════════════════════════════════════════════════════════
        var version = VersionDeKustomize();

        if (version is null)
        {
            nonCouvert.Add(
                "le RENDU des overlays : `kustomize` est absent de ce poste, donc "
                + "non-root, sondes, requests/limits, tags immuables, NetworkPolicies, "
                + "secrets en clair et cohérence OTLP n'ont PAS été vérifiés — c'est la "
                + "plus grosse moitié de ce contrôle. Le Python rendait 0 ici sans autre "
                + "forme de procès. "
                + "https://kubectl.docs.kubernetes.io/installation/kustomize/");
            return new Verdict(fautes, constats, nonCouvert);
        }

        if (version[0] < 5)
        {
            // `k8s/base/services/*/kustomization.yaml` emploie le transformateur
            // `labels` avec `includeTemplates`, apparu en kustomize 5. Sur une
            // version 4, le build échoue sur « json: unknown field
            // "includeTemplates" » — un message qui envoie chercher une faute de
            // frappe dans le YAML, alors que le YAML est correct et que c'est
            // l'outil qui est trop ancien. `kubectl apply -k` embarque sa propre
            // copie (v5 depuis kubectl 1.28) et n'est PAS concerné : un poste peut
            // donc déployer correctement et voir ce contrôle refuser de regarder.
            var v = string.Join(".", version);
            nonCouvert.Add(
                $"le RENDU des overlays : `kustomize {v}` est trop ancien (il en faut 5 ou "
                + "plus, pour `labels` avec `includeTemplates`). `kubectl apply -k` "
                + "embarque sa propre copie et n'est PAS concerné — ce poste déploie "
                + "peut-être très bien.");
            return new Verdict(fautes, constats, nonCouvert);
        }

        var parOverlay = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        var reserves = new List<string>();

        foreach (var overlay in Overlays)
        {
            var chemin = Path.Combine(Depot.Dossier("k8s", "overlays"), overlay);
            if (!Directory.Exists(chemin))
            {
                fautes.Add($"k8s/overlays/{overlay} n'existe pas : rien n'a été construit.");
                continue;
            }

            var (code, sortie, erreur) = Programme.Lancer("kustomize", "build", chemin);
            if (code != 0)
            {
                fautes.Add($"{overlay} : kustomize build a échoué — {Premiere(erreur)}");
                continue;
            }

            var objets = LectureYaml.Documents(sortie)
                .Select(LectureYaml.Table)
                .Where(o => o is not null)
                .Select(o => o!)
                .ToList();

            var hotes = Hotes(objets);
            parOverlay[overlay] = hotes;
            reserves.AddRange(hotes
                .Where(h => h.EndsWith(".example", StringComparison.Ordinal))
                .Select(h => $"{overlay} : {h}"));

            var ecarts = Verifier(overlay, objets)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToList();

            var deploiements = objets.Count(o => Genre(o) == "Deployment");
            constats.Add(
                $"{overlay} — {objets.Count} objet(s), {deploiements} Deployment(s), "
                + $"{ecarts.Count} écart(s) au cahier.");
            fautes.AddRange(ecarts.Select(e => $"{overlay} : {e}"));
        }

        fautes.AddRange(IngressCroises(parOverlay));

        // INFORMATIF, PAS BLOQUANT — mais impossible à ne pas voir. `.example` est
        // réservé (RFC 2606) et ne résoudra jamais : c'est ce qui rend le
        // placeholder honnête. Le faire échouer bloquerait le lot de quiconque ne
        // déploie pas, alors que le vrai domaine n'est pas encore décidé.
        if (reserves.Count > 0)
        {
            constats.Add(
                $"{reserves.Count} hôte(s) encore en domaine réservé — à remplacer avant "
                + "tout déploiement :");
            constats.AddRange(reserves
                .OrderBy(x => x, StringComparer.Ordinal)
                .Select(x => "  " + x));
        }

        constats.Add($"{Overlays.Length} overlay(s) construit(s), {fautes.Count} écart(s).");

        return new Verdict(fautes, constats, nonCouvert);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // CE QUI SE LIT SANS KUSTOMIZE
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>Aucun `k8s/base/common/secret*.yaml` ne doit porter de valeur.</summary>
    /// <remarks>
    /// CE QUI EST ARRIVÉ : une clé d'API Resend en clair s'est retrouvée dans
    /// `secret.yaml` — posée le temps d'un essai, et restée. Elle n'a pas été
    /// commitée, mais elle était à un `git add -A` de l'historique. Un secret
    /// entre une fois dans l'historique se révoque ; il ne se retire pas.
    ///
    /// §12 dit depuis le début que ces fichiers restent vides dans Git. Rien ne
    /// l'imposait : la règle vivait dans un commentaire.
    ///
    /// LA VALEUR EST ENTRE GUILLEMETS, CE QUI SUIT EST UN COMMENTAIRE. C'est
    /// exactement le piège qui a fait rendre « quatorze valeurs non vides » à une
    /// première version, alors que le fichier ne portait que des commentaires de
    /// fin de ligne.
    /// </remarks>
    private static List<string> SecretsVides()
    {
        var fautes = new List<string>();
        var dossier = Depot.Dossier("k8s", "base", "common");
        var fichiers = Directory.EnumerateFiles(dossier, "secret*.yaml")
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

        if (fichiers.Count == 0)
        {
            fautes.Add(
                "aucun fichier ne correspond à k8s/base/common/secret*.yaml — ce contrôle "
                + "ne vérifie plus rien.");
            return fautes;
        }

        foreach (var chemin in fichiers)
        {
            var lignes = File.ReadAllText(chemin).Replace("\r\n", "\n").Split('\n');
            for (var numero = 1; numero <= lignes.Length; numero++)
            {
                var ligne = lignes[numero - 1];
                if (ligne.TrimStart().StartsWith('#'))
                {
                    continue;
                }

                var trouve = CleDeSecret.Match(ligne);
                if (!trouve.Success)
                {
                    continue;
                }

                var cle = trouve.Groups[1].Value;
                var reste = trouve.Groups[2].Value;
                if (cle is "name" or "namespace"
                    or "app.kubernetes.io/name" or "app.kubernetes.io/part-of")
                {
                    continue;
                }

                string valeur;
                var citee = ValeurCitee.Match(reste);
                if (citee.Success)
                {
                    valeur = citee.Groups[1].Value;
                }
                else if (reste.StartsWith('#') || reste.Trim().Length == 0)
                {
                    continue;
                }
                else
                {
                    valeur = reste.Split('#')[0].Trim();
                }

                if (valeur.Length > 0)
                {
                    fautes.Add(
                        $"{Depot.Relatif(chemin)}:{numero} : {cle} porte une valeur de "
                        + $"{valeur.Length} caractère(s) — ces fichiers restent vides dans "
                        + "Git (§12)");
                }
            }
        }

        return fautes;
    }

    /// <summary>Aucun overlay déployable ne doit garder un hôte en `.example`.</summary>
    /// <remarks>
    /// L'overlay de production a porté `api.hba-express.example` jusqu'au jour où
    /// l'enregistrement DNS a été posé — découvert par hasard, en relisant le
    /// fichier, pas par un contrôle. `.example` est réservé par la RFC 2606 : il
    /// ne résout nulle part. Déployé tel quel, l'Ingress existe, le cluster va
    /// bien, tous les pods sont verts, et AUCUNE requête n'arrive jamais. Rien
    /// dans l'état du cluster ne désigne la cause.
    ///
    /// Le placeholder était volontaire, et c'est un bon choix : un placeholder
    /// qui résout est un placeholder qu'on oublie. Ce qui manquait, c'est le
    /// contrôle qui refuse de le laisser passer en production. `dev` garde
    /// `.example` à dessein : rien n'y est publié.
    ///
    /// UNIQUEMENT LES VALEURS DE PATCH, JAMAIS LA PROSE : le commentaire qui
    /// explique la règle a le droit de nommer `.example`.
    /// </remarks>
    private static List<string> HotesDesOverlays()
    {
        var fautes = new List<string>();
        var vus = 0;

        foreach (var overlay in new[] { "prod", "staging" })
        {
            var chemin = Path.Combine(
                Depot.Dossier("k8s", "overlays"), overlay, "kustomization.yaml");

            if (!File.Exists(chemin))
            {
                fautes.Add($"k8s/overlays/{overlay}/kustomization.yaml est introuvable");
                continue;
            }

            var contenu = File.ReadAllText(chemin).Replace("\r\n", "\n");
            foreach (Match valeur in ValeurDePatch.Matches(contenu))
            {
                var hote = valeur.Groups[1].Value;
                if (!NomDHote.IsMatch(hote))
                {
                    continue;
                }

                vus++;
                if (hote.EndsWith(".example", StringComparison.Ordinal)
                    || hote.EndsWith(".invalid", StringComparison.Ordinal)
                    || hote.EndsWith(".test", StringComparison.Ordinal)
                    || hote.EndsWith(".localhost", StringComparison.Ordinal))
                {
                    fautes.Add(
                        $"overlay {overlay} : l'hôte {hote} est un domaine réservé "
                        + "(RFC 2606) — il ne résoudra jamais, et la panne se lira comme "
                        + "un cluster en bonne santé sans trafic");
                }
            }
        }

        if (vus == 0)
        {
            fautes.Add(
                "aucun hôte lu dans les overlays déployables — le format a changé, ce "
                + "contrôle ne vérifie plus rien.");
        }

        return fautes;
    }

    /// <summary>Les Jobs de migration emploient-ils l'image exacte du déploiement ?</summary>
    /// <remarks>
    /// Les Jobs de migration vivent dans leur propre overlay — il faut pouvoir
    /// les lancer sans redéployer. Le prix de cette séparation est une seconde
    /// liste d'images, et DEUX LISTES DIVERGENT TOUJOURS.
    ///
    /// Migrer avec une image PLUS ANCIENNE que celle qui servira applique un
    /// schéma en retard sur le code : le service démarre, puis échoue sur une
    /// colonne absente, à la première requête qui la touche — loin de la
    /// migration. Migrer avec une image PLUS RÉCENTE applique un schéma que le
    /// code déployé ne sait pas encore lire, ce qui peut passer inaperçu
    /// longtemps.
    ///
    /// UN SERVICE SANS MIGRATION N'A PAS DE JOB, ET C'EST CORRECT. La règle suit
    /// le code : un Job n'a de sens que si le `Program.cs` appelle
    /// `MigrateHbaDatabaseAsync`. Sinon le conteneur démarre un serveur web, le
    /// Job reste `Running`, et `kubectl wait` expire sur une étape qui n'avait
    /// rien à faire.
    /// </remarks>
    private static List<string> ImagesDeMigration()
    {
        var fautes = new List<string>();
        var overlays = Depot.Dossier("k8s", "overlays");

        var prod = ImagesDOverlay(Path.Combine(overlays, "prod", "kustomization.yaml"));
        var migr = ImagesDOverlay(
            Path.Combine(overlays, "migrations-prod", "kustomization.yaml"));

        // L'overlay de migration peut ne pas exister sur une branche ancienne : on
        // ne fabrique pas une faute à partir d'une absence.
        if (prod is null || migr is null)
        {
            return fautes;
        }

        if (prod.Count == 0 || migr.Count == 0)
        {
            fautes.Add(
                "aucune image lue dans les overlays prod ou migrations-prod — le format a "
                + "changé, ce contrôle ne vérifie plus rien.");
            return fautes;
        }

        var liste = Path.Combine(
            Depot.Dossier("k8s", "base", "services"), "kustomization.yaml");

        if (!File.Exists(liste))
        {
            fautes.Add(
                "k8s/base/services/kustomization.yaml est absent : impossible de savoir "
                + "quels services sont déployés, donc lesquels ont besoin d'un Job.");
            return fautes;
        }

        var deployes = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var ligne in File.ReadAllText(liste).Replace("\r\n", "\n").Split('\n'))
        {
            if (ligne.TrimStart().StartsWith('#'))
            {
                continue;
            }

            var trouve = ServiceDeploye.Match(ligne);
            if (trouve.Success)
            {
                deployes.Add("hba/" + trouve.Groups[1].Value);
            }
        }

        foreach (var nom in deployes)
        {
            if (!migr.TryGetValue(nom, out var image))
            {
                if (!ServiceADesMigrations(nom["hba/".Length..]))
                {
                    continue;
                }

                fautes.Add(
                    $"{nom} est déployé, migre, et n'a pas de Job — sa base resterait sans "
                    + "schéma, et le pod qui la vise échouerait au démarrage sans que rien "
                    + "ne désigne l'oubli");
                continue;
            }

            if (prod.TryGetValue(nom, out var deployee) && deployee != image)
            {
                fautes.Add(
                    $"{nom} : le Job de migration emploie {image.Nom}:{image.Tag} alors que "
                    + $"le déploiement emploie {deployee.Nom}:{deployee.Tag} — le schéma "
                    + "serait appliqué par une autre version du code");
            }
        }

        foreach (var nom in migr.Keys
                     .Where(n => !deployes.Contains(n))
                     .OrderBy(n => n, StringComparer.Ordinal))
        {
            fautes.Add(
                $"{nom} a un Job de migration mais n'est pas déployé — migration inutile "
                + "sur une base que personne ne lit");
        }

        return fautes;
    }

    /// <summary>Les images posées par un overlay, ou <c>null</c> si le fichier manque.</summary>
    private static Dictionary<string, (string Nom, string Tag)>? ImagesDOverlay(string chemin)
    {
        if (!File.Exists(chemin))
        {
            return null;
        }

        var images = new Dictionary<string, (string, string)>(StringComparer.Ordinal);
        var contenu = File.ReadAllText(chemin).Replace("\r\n", "\n");
        foreach (Match image in ImageDOverlay.Matches(contenu))
        {
            images[image.Groups[1].Value] = (image.Groups[2].Value, image.Groups[3].Value);
        }

        return images;
    }

    /// <summary>Le service appelle-t-il `MigrateHbaDatabaseAsync` ?</summary>
    /// <remarks>
    /// Même règle que le générateur de Jobs, et pour la même raison : un service
    /// sans migration ne doit pas avoir de Job.
    /// </remarks>
    private static bool ServiceADesMigrations(string service)
    {
        foreach (var domaine in new[] { "common", "marketplace", "delivery", "food" })
        {
            var dossier = Depot.Chemin("services", domaine, service);
            if (!Directory.Exists(dossier))
            {
                continue;
            }

            foreach (var fichier in Depot.Fichiers(dossier, ".cs"))
            {
                if (Path.GetFileName(fichier) != "Program.cs")
                {
                    continue;
                }

                if (File.ReadAllText(fichier)
                    .Contains("MigrateHbaDatabaseAsync", StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Aucune cible de patch ne doit désigner le vide.</summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// UNE CIBLE DE PATCH QUI NE DÉSIGNE RIEN NE FAIT PAS ÉCHOUER LE BUILD.
    ///
    /// Kustomize applique un patch aux objets qui correspondent à sa cible. Zéro
    /// correspondance n'est PAS une erreur : le build réussit, l'objet n'est pas
    /// modifié, et rien ne le dit. Un `name:` mal orthographié, un service
    /// renommé, un patch écrit pour un objet retiré du dépôt — les trois donnent
    /// la même sortie qu'un patch qui a mordu.
    ///
    /// C'est exactement le défaut qui a laissé dix HPA de production à
    /// `minReplicas: 1` pendant que le dépôt affichait `replicas: 2` : le patch du
    /// Deployment existait, celui du HPA n'existait pas. Ici on vérifie l'inverse
    /// — qu'aucun patch ne vise le vide — ce qui attrape le renommage et la
    /// coquille.
    ///
    /// NE REMPLACE PAS `kustomize build`, et ne prétend pas le faire : ce contrôle
    /// tourne SANS kustomize, c'est tout son intérêt sur un poste qui ne l'a pas.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
    private static List<string> CiblesDePatch()
    {
        var produits = ObjetsDeBase();
        var fautes = new List<string>();

        foreach (var overlay in Overlays)
        {
            var chemin = Path.Combine(
                Depot.Dossier("k8s", "overlays"), overlay, "kustomization.yaml");
            if (!File.Exists(chemin))
            {
                continue;
            }

            var kustomization = LectureYaml.Charge(File.ReadAllText(chemin));

            foreach (var patch in LectureYaml.Liste(
                         LectureYaml.Chemin(kustomization, "patches")))
            {
                var genre = LectureYaml.Texte(LectureYaml.Chemin(patch, "target", "kind"));
                var nom = LectureYaml.Texte(LectureYaml.Chemin(patch, "target", "name"));

                // Sans nom, la cible vise TOUS les objets de ce genre : rien à
                // résoudre, et c'est un usage légitime (le patch de `maxReplicas`).
                if (string.IsNullOrEmpty(genre) || string.IsNullOrEmpty(nom))
                {
                    continue;
                }

                // Le patch de Namespace renomme l'objet lui-même : il désigne le
                // nom d'AVANT, qui est bien celui de la base.
                if (!produits.Contains((genre, nom)))
                {
                    fautes.Add(
                        $"{overlay} : le patch vise {genre}/{nom}, qu'aucun objet de la "
                        + "base ne produit — kustomize l'appliquera à RIEN, sans erreur.");
                }
            }
        }

        return fautes;
    }

    /// <summary>Les (genre, nom) que la base produira, `namePrefix` appliqué à la main.</summary>
    /// <remarks>
    /// VOLONTAIREMENT APPROXIMATIF ET SUFFISANT : on ne réimplémente pas
    /// kustomize, on reconstitue la seule chose dont le contrôle a besoin — la
    /// liste des noms qu'une cible de patch peut légitimement désigner.
    ///
    /// ON CHERCHE QUI INCLUT LE GABARIT, ON NE SUPPOSE PAS OÙ IL EST INCLUS. Une
    /// première version ne regardait que `k8s/base/services/*`. Elle a signalé
    /// `gateway-service` comme introuvable — un FAUX POSITIF : la passerelle vit
    /// dans `k8s/base/apps/gateway/` et inclut le même gabarit avec son propre
    /// préfixe. Un contrôle qui invente des fautes se fait désactiver, et emporte
    /// avec lui les vraies.
    /// </remarks>
    private static HashSet<(string Genre, string Nom)> ObjetsDeBase()
    {
        var produits = new HashSet<(string, string)>();
        var racine = Depot.Dossier("k8s", "base");

        foreach (var chemin in Depot.Fichiers(racine, ".yaml")
                     .OrderBy(f => f, StringComparer.Ordinal))
        {
            var dossier = Path.GetDirectoryName(chemin)!;

            // Le gabarit `_service` n'est pas déployé tel quel : il est inclus par
            // chaque service, qui lui applique SON préfixe. On le traite donc
            // depuis les dossiers qui l'incluent, pas depuis lui-même.
            if (Path.GetFileName(dossier) == "_service")
            {
                continue;
            }

            var prefixe = string.Empty;
            var kustomization = Path.Combine(dossier, "kustomization.yaml");
            if (File.Exists(kustomization))
            {
                prefixe = LectureYaml.Texte(LectureYaml.Chemin(
                    LectureYaml.Charge(File.ReadAllText(kustomization)), "namePrefix"))
                    ?? string.Empty;
            }

            foreach (var objet in LectureYaml.Documents(File.ReadAllText(chemin)))
            {
                var genre = LectureYaml.Texte(LectureYaml.Chemin(objet, "kind"));
                var nom = LectureYaml.Texte(LectureYaml.Chemin(objet, "metadata", "name"));
                if (!string.IsNullOrEmpty(genre) && !string.IsNullOrEmpty(nom))
                {
                    produits.Add((genre, prefixe + nom));
                }
            }
        }

        // Les objets du gabarit, une fois préfixés par chaque service qui l'inclut.
        var noyaux = new HashSet<(string Genre, string Nom)>();
        var gabarit = Path.Combine(racine, "services", "_service");
        if (Directory.Exists(gabarit))
        {
            foreach (var chemin in Directory.EnumerateFiles(gabarit, "*.yaml")
                         .OrderBy(f => f, StringComparer.Ordinal))
            {
                foreach (var objet in LectureYaml.Documents(File.ReadAllText(chemin)))
                {
                    var genre = LectureYaml.Texte(LectureYaml.Chemin(objet, "kind"));
                    var nom = LectureYaml.Texte(LectureYaml.Chemin(objet, "metadata", "name"));
                    if (!string.IsNullOrEmpty(genre) && !string.IsNullOrEmpty(nom))
                    {
                        noyaux.Add((genre, nom));
                    }
                }
            }
        }

        foreach (var kustomization in Depot.Fichiers(racine, ".yaml")
                     .Where(f => Path.GetFileName(f) == "kustomization.yaml")
                     .OrderBy(f => f, StringComparer.Ordinal))
        {
            var dossier = Path.GetDirectoryName(kustomization)!;
            if (Path.GetFileName(dossier) == "_service")
            {
                continue;
            }

            var document = LectureYaml.Charge(File.ReadAllText(kustomization));
            var inclut = LectureYaml.Liste(LectureYaml.Chemin(document, "resources"))
                .Any(r => (LectureYaml.Texte(r) ?? string.Empty)
                    .Contains("_service", StringComparison.Ordinal));

            if (!inclut)
            {
                continue;
            }

            var prefixe = LectureYaml.Texte(LectureYaml.Chemin(document, "namePrefix"))
                ?? string.Empty;

            foreach (var (genre, nom) in noyaux)
            {
                produits.Add((genre, prefixe + nom));
            }
        }

        return produits;
    }

    /// <summary>Un fichier monté depuis un Secret s'accorde en quatre points.</summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// Quand un service lit un secret sous forme de FICHIER — le compte de service
    /// Firebase de notification-service est le premier — quatre valeurs doivent
    /// concorder, écrites dans DEUX fichiers différents :
    ///
    ///   1. le `secretName` du volume  ↔  le `metadata.name` du Secret ;
    ///   2. le `name` du volume        ↔  le `name` du volumeMount ;
    ///   3. le `mountPath`             ↔  le répertoire du chemin passé au service ;
    ///   4. le nom du fichier attendu  ↔  une CLÉ du Secret.
    ///
    /// AUCUN DES QUATRE NE CASSE AU DÉPLOIEMENT S'IL EST FAUX. Le pod démarre, le
    /// fichier n'est pas là où le service le cherche, `FcmOptions.ResolveJson()`
    /// rend `null`, et le refus de démarrage annonce « Notifications:Fcm n'est pas
    /// configuré » — alors qu'il l'est, et que seul un nom de fichier diffère. On
    /// cherche une variable manquante pendant que le secret est monté deux
    /// répertoires plus loin.
    ///
    /// Le quatrième est le plus traître : un volume de Secret projette chaque clé
    /// comme un fichier PORTANT LE NOM DE LA CLÉ. Renommer la clé dans le Secret
    /// déplace le fichier, en silence.
    ///
    /// `secretKeyRef` EST VÉRIFIÉ AUSSI, BIEN QU'IL ÉCHOUE BRUYAMMENT : le pod
    /// reste en `CreateContainerConfigError` avec la clé nommée dans l'événement.
    /// Le voir en revue coûte moins que de le voir sur un cluster à moitié
    /// déployé.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
    private static List<string> MontagesDeSecret()
    {
        var fautes = new List<string>();

        // Les Secrets déclarés quelque part sous `k8s/`, par nom → clés.
        var secrets = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        foreach (var chemin in Depot.Fichiers(Depot.Dossier("k8s"), ".yaml")
                     .OrderBy(f => f, StringComparer.Ordinal))
        {
            foreach (var objet in LectureYaml.Documents(File.ReadAllText(chemin)))
            {
                if (LectureYaml.Texte(LectureYaml.Chemin(objet, "kind")) != "Secret")
                {
                    continue;
                }

                var nom = LectureYaml.Texte(LectureYaml.Chemin(objet, "metadata", "name"));
                if (string.IsNullOrEmpty(nom))
                {
                    continue;
                }

                var cles = new SortedSet<string>(StringComparer.Ordinal);
                foreach (var section in new[] { "stringData", "data" })
                {
                    var table = LectureYaml.Table(LectureYaml.Chemin(objet, section));
                    if (table is not null)
                    {
                        cles.UnionWith(table.Keys);
                    }
                }

                secrets[nom] = cles;
            }
        }

        foreach (var kustomization in Depot.Fichiers(Depot.Dossier("k8s", "base"), ".yaml")
                     .Where(f => Path.GetFileName(f) == "kustomization.yaml")
                     .OrderBy(f => f, StringComparer.Ordinal))
        {
            var relatif = Depot.Relatif(kustomization);
            var document = LectureYaml.Charge(File.ReadAllText(kustomization));

            foreach (var patch in LectureYaml.Liste(LectureYaml.Chemin(document, "patches")))
            {
                var corps = LectureYaml.Texte(LectureYaml.Chemin(patch, "patch"));
                if (string.IsNullOrEmpty(corps)
                    || (!corps.Contains("volumeMounts", StringComparison.Ordinal)
                        && !corps.Contains("secretKeyRef", StringComparison.Ordinal)))
                {
                    continue;
                }

                var objet = LectureYaml.Charge(corps);
                var specification = LectureYaml.Chemin(objet, "spec", "template", "spec");
                if (specification is null)
                {
                    continue;
                }

                var volumes = new Dictionary<string, object?>(StringComparer.Ordinal);
                foreach (var volume in LectureYaml.Liste(
                             LectureYaml.Chemin(specification, "volumes")))
                {
                    var nom = LectureYaml.Texte(LectureYaml.Chemin(volume, "name"));
                    if (!string.IsNullOrEmpty(nom))
                    {
                        volumes[nom] = volume;
                    }
                }

                foreach (var conteneur in LectureYaml.Liste(
                             LectureYaml.Chemin(specification, "containers")))
                {
                    fautes.AddRange(MontagesDUnConteneur(relatif, conteneur, volumes, secrets));
                }
            }
        }

        return fautes;
    }

    /// <summary>Les quatre accords, pour un conteneur d'un patch en ligne.</summary>
    private static List<string> MontagesDUnConteneur(string relatif, object? conteneur, IReadOnlyDictionary<string, object?> volumes, IReadOnlyDictionary<string, SortedSet<string>> secrets)
    {
        var fautes = new List<string>();

        var environnement = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var variable in LectureYaml.Liste(LectureYaml.Chemin(conteneur, "env")))
        {
            var nom = LectureYaml.Texte(LectureYaml.Chemin(variable, "name"));
            var valeur = LectureYaml.Texte(LectureYaml.Chemin(variable, "value"));
            if (!string.IsNullOrEmpty(nom) && !string.IsNullOrEmpty(valeur))
            {
                environnement[nom] = valeur;
            }

            var nomSecret = LectureYaml.Texte(
                LectureYaml.Chemin(variable, "valueFrom", "secretKeyRef", "name"));
            var cle = LectureYaml.Texte(
                LectureYaml.Chemin(variable, "valueFrom", "secretKeyRef", "key"));

            if (string.IsNullOrEmpty(nomSecret) || string.IsNullOrEmpty(cle))
            {
                continue;
            }

            if (!secrets.TryGetValue(nomSecret, out var connues))
            {
                fautes.Add(
                    $"{relatif} : {nom} lit le Secret « {nomSecret} », qu'aucun manifeste "
                    + "de `k8s/` ne déclare.");
            }
            else if (!connues.Contains(cle))
            {
                fautes.Add(
                    $"{relatif} : {nom} lit la clé « {cle} » du Secret « {nomSecret} », "
                    + $"qui ne porte que {string.Join(", ", connues)}. Le pod restera en "
                    + "CreateContainerConfigError.");
            }
        }

        foreach (var montage in LectureYaml.Liste(
                     LectureYaml.Chemin(conteneur, "volumeMounts")))
        {
            var nomVolume = LectureYaml.Texte(LectureYaml.Chemin(montage, "name"));
            var cheminMontage = LectureYaml.Texte(LectureYaml.Chemin(montage, "mountPath"));

            if (string.IsNullOrEmpty(nomVolume)
                || !volumes.TryGetValue(nomVolume, out var volume))
            {
                // Un volume non déclaré : le pod ne démarre pas du tout.
                fautes.Add(
                    $"{relatif} : le montage « {nomVolume} » ne correspond à aucun volume "
                    + "déclaré.");
                continue;
            }

            var secret = LectureYaml.Texte(LectureYaml.Chemin(volume, "secret", "secretName"));
            if (string.IsNullOrEmpty(secret))
            {
                continue;   // emptyDir, configMap… hors sujet ici
            }

            if (!secrets.TryGetValue(secret, out var cles))
            {
                fautes.Add(
                    $"{relatif} : le volume « {nomVolume} » monte le Secret « {secret} », "
                    + "qu'aucun manifeste de `k8s/` ne déclare.");
                continue;
            }

            // Une variable qui pointe DANS ce montage doit nommer une clé.
            var prefixe = cheminMontage + "/";
            foreach (var (variable, valeur) in environnement
                         .OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                if (!valeur.StartsWith(prefixe, StringComparison.Ordinal))
                {
                    continue;
                }

                var fichier = valeur[prefixe.Length..];
                if (!cles.Contains(fichier))
                {
                    fautes.Add(
                        $"{relatif} : {variable} attend le fichier « {fichier} », mais le "
                        + $"Secret « {secret} » ne porte que {string.Join(", ", cles)}. Un "
                        + "volume de Secret nomme chaque fichier d'après SA CLÉ — le "
                        + "service ne trouvera rien, et annoncera une configuration "
                        + "absente.");
                }
            }
        }

        return fautes;
    }

    // ═════════════════════════════════════════════════════════════════════════
    // CE QUI DEMANDE LE RENDU RÉEL
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>Le genre d'un objet rendu.</summary>
    private static string Genre(Dictionary<string, object?> objet)
        => LectureYaml.Texte(LectureYaml.Chemin(objet, "kind")) ?? string.Empty;

    /// <summary>Le nom d'un objet rendu.</summary>
    private static string NomDe(Dictionary<string, object?> objet)
        => LectureYaml.Texte(LectureYaml.Chemin(objet, "metadata", "name")) ?? "(sans nom)";

    /// <summary>Les écarts au cahier d'un overlay construit.</summary>
    private static List<string> Verifier(string overlay, List<Dictionary<string, object?>> objets)
    {
        var fautes = new List<string>();

        var deploiements = objets.Where(o => Genre(o) == "Deployment").ToList();
        if (deploiements.Count == 0)
        {
            fautes.Add("aucun Deployment — l'overlay ne déploie rien");
        }

        // LES StatefulSet AUSSI, ET C'EST UN OUBLI QUI ÉTAIT DÉJÀ EN PLACE. La
        // première version ne regardait que les Deployments. Redis et MinIO sont
        // des StatefulSets : ils échappaient donc entièrement à la vérification
        // non-root, alors que ce sont précisément les deux pods qui tiennent des
        // données. Un datastore en root est un plus mauvais défaut qu'un service
        // web en root.
        var charges = deploiements
            .Concat(objets.Where(o => Genre(o) == "StatefulSet"))
            .ToList();

        foreach (var charge in charges)
        {
            var nom = NomDe(charge);
            var specification = LectureYaml.Chemin(charge, "spec", "template", "spec");

            if (LectureYaml.Chemin(specification, "securityContext", "runAsNonRoot")
                is not true)
            {
                fautes.Add($"{nom} : runAsNonRoot absent (§19)");
            }

            foreach (var conteneur in LectureYaml.Liste(
                         LectureYaml.Chemin(specification, "containers")))
            {
                fautes.AddRange(VerifierConteneur(overlay, nom, conteneur));
            }
        }

        var politiques = objets.Where(o => Genre(o) == "NetworkPolicy").ToList();
        var noms = politiques.Select(NomDe).ToHashSet(StringComparer.Ordinal);

        if (!politiques.Any(p => ToutePorte(p) && TypeDePolitique(p, "Ingress")))
        {
            fautes.Add("aucune NetworkPolicy deny-by-default sur l'ingress (§5)");
        }

        // Couper l'egress sans rouvrir le DNS casse tout, et le symptôme MENT :
        // l'erreur désigne le service visé, pas la résolution.
        if (politiques.Any(p => ToutePorte(p) && TypeDePolitique(p, "Egress"))
            && !noms.Contains("allow-dns"))
        {
            fautes.Add(
                "egress refusé sans règle DNS — les services échoueraient à résoudre "
                + "leurs dépendances, et l'erreur désignerait le service visé (§5)");
        }

        foreach (var secret in objets.Where(o => Genre(o) == "Secret"))
        {
            var donnees = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var section in new[] { "stringData", "data" })
            {
                var table = LectureYaml.Table(LectureYaml.Chemin(secret, section));
                if (table is null)
                {
                    continue;
                }

                foreach (var (cle, valeur) in table)
                {
                    donnees[cle] = valeur;
                }
            }

            foreach (var (cle, valeur) in donnees.OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                var texte = LectureYaml.Texte(valeur);
                if (string.IsNullOrEmpty(texte))
                {
                    continue;
                }

                var majuscule = cle.ToUpperInvariant();
                if (ClesSecretes.Any(m => majuscule.Contains(m, StringComparison.Ordinal)))
                {
                    fautes.Add(
                        $"Secret {NomDe(secret)} : « {cle} » porte une valeur en clair — "
                        + "le §12 l'interdit, et Git la garde après suppression");
                }
            }
        }

        var plateforme = objets.FirstOrDefault(
            o => Genre(o) == "ConfigMap" && NomDe(o) == "hba-platform");

        var adresseOtel = plateforme is null
            ? string.Empty
            : (LectureYaml.Texte(LectureYaml.Chemin(
                plateforme, "data", "OPENTELEMETRY__ENDPOINT")) ?? string.Empty).Trim();

        var nommes = objets
            .Where(o => LectureYaml.Chemin(o, "metadata", "name") is not null)
            .Select(o => (Genre(o), NomDe(o)))
            .ToHashSet();

        if (adresseOtel.Length > 0)
        {
            foreach (var attendu in new[]
                     {
                         ("Deployment", "otel-collector"),
                         ("Service", "otel-collector"),
                         ("NetworkPolicy", "allow-otel-collector-ingress"),
                     })
            {
                if (!nommes.Contains(attendu))
                {
                    fautes.Add(
                        $"OPENTELEMETRY__ENDPOINT est renseigné, mais {attendu.Item1}/"
                        + $"{attendu.Item2} manque — les services journaliseraient des "
                        + "échecs OTLP en boucle (§16)");
                }
            }
        }

        if ((overlay == "staging" || overlay == "prod")
            && adresseOtel != "http://otel-collector:4317")
        {
            var lue = adresseOtel.Length == 0 ? "<vide>" : adresseOtel;
            fautes.Add(
                $"OPENTELEMETRY__ENDPOINT vaut « {lue} » — staging/prod doivent exporter "
                + "vers le collecteur interne otel-collector:4317 (§16)");
        }

        return fautes;
    }

    /// <summary>La politique vise-t-elle TOUS les pods (`podSelector: {}`) ?</summary>
    private static bool ToutePorte(Dictionary<string, object?> politique)
    {
        var selecteur = LectureYaml.Table(LectureYaml.Chemin(politique, "spec", "podSelector"));
        return selecteur is not null && selecteur.Count == 0;
    }

    /// <summary>La politique déclare-t-elle ce type ?</summary>
    private static bool TypeDePolitique(Dictionary<string, object?> politique, string type)
        => LectureYaml.Liste(LectureYaml.Chemin(politique, "spec", "policyTypes"))
            .Any(t => LectureYaml.Texte(t) == type);

    /// <summary>Les écarts au cahier d'un conteneur.</summary>
    private static List<string> VerifierConteneur(string overlay, string nom, object? conteneur)
    {
        var fautes = new List<string>();
        var contexte = LectureYaml.Chemin(conteneur, "securityContext");

        if (LectureYaml.Chemin(contexte, "allowPrivilegeEscalation") is not false)
        {
            fautes.Add($"{nom} : allowPrivilegeEscalation n'est pas false (§19)");
        }

        var abandonnees = LectureYaml.Liste(LectureYaml.Chemin(contexte, "capabilities", "drop"));
        if (!abandonnees.Any(c => LectureYaml.Texte(c) == "ALL"))
        {
            fautes.Add($"{nom} : capacités Linux non abandonnées (§19)");
        }

        var table = LectureYaml.Table(conteneur);
        foreach (var sonde in new[] { "livenessProbe", "readinessProbe", "startupProbe" })
        {
            if (table is null || !table.ContainsKey(sonde))
            {
                fautes.Add($"{nom} : {sonde} absente (§7)");
            }
        }

        // LA CONFUSION QUI REDÉMARRE UN SERVICE EN BONNE SANTÉ.
        var vivant = LectureYaml.Texte(
            LectureYaml.Chemin(conteneur, "livenessProbe", "httpGet", "path"));
        var pret = LectureYaml.Texte(
            LectureYaml.Chemin(conteneur, "readinessProbe", "httpGet", "path"));

        if (!string.IsNullOrEmpty(vivant) && vivant.Contains("ready", StringComparison.Ordinal))
        {
            fautes.Add(
                $"{nom} : la liveness sonde « {vivant} » — un service qui a perdu sa base "
                + "serait redémarré en boucle pendant l'incident (§7)");
        }

        if (!string.IsNullOrEmpty(pret) && pret.EndsWith("/live", StringComparison.Ordinal))
        {
            fautes.Add(
                $"{nom} : la readiness sonde « {pret} » — un pod dont la base est absente "
                + "recevrait du trafic (§7)");
        }

        if (LectureYaml.Chemin(conteneur, "resources", "requests") is null)
        {
            fautes.Add($"{nom} : requests absentes (§7)");
        }

        if (LectureYaml.Chemin(conteneur, "resources", "limits") is null)
        {
            fautes.Add($"{nom} : limits absentes (§7)");
        }

        var image = LectureYaml.Texte(LectureYaml.Chemin(conteneur, "image")) ?? string.Empty;
        if (overlay == "prod"
            && (image.EndsWith(":latest", StringComparison.Ordinal) || !image.Contains(':')))
        {
            fautes.Add(
                $"{nom} : image « {image} » sans tag immuable — un apply rejoué ne "
                + "redéploierait pas la même version (§13)");
        }

        return fautes;
    }

    /// <summary>Tous les noms d'hôte servis par les Ingress d'un overlay.</summary>
    private static SortedSet<string> Hotes(List<Dictionary<string, object?>> objets)
    {
        var trouves = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var ingress in objets.Where(o => Genre(o) == "Ingress"))
        {
            foreach (var regle in LectureYaml.Liste(LectureYaml.Chemin(ingress, "spec", "rules")))
            {
                var hote = LectureYaml.Texte(LectureYaml.Chemin(regle, "host"));
                if (!string.IsNullOrEmpty(hote))
                {
                    trouves.Add(hote);
                }
            }

            foreach (var tls in LectureYaml.Liste(LectureYaml.Chemin(ingress, "spec", "tls")))
            {
                foreach (var hote in LectureYaml.Liste(LectureYaml.Chemin(tls, "hosts")))
                {
                    var texte = LectureYaml.Texte(hote);
                    if (!string.IsNullOrEmpty(texte))
                    {
                        trouves.Add(texte);
                    }
                }
            }
        }

        return trouves;
    }

    /// <summary>Deux environnements ne doivent jamais partager un nom d'hôte (§2).</summary>
    /// <remarks>
    /// Le §2 exige des namespaces, secrets, bases et buckets distincts par
    /// environnement. Un domaine partagé fait entrer du trafic de production dans
    /// un cluster de validation — et rien ne le signale avant les journaux, parce
    /// que les deux répondent 200.
    ///
    /// Un copier-coller d'overlay est le chemin le plus court vers ce défaut : on
    /// duplique `staging` pour créer `prod`, on change le namespace et les
    /// replicas, et on oublie l'hôte.
    /// </remarks>
    private static List<string> IngressCroises(IReadOnlyDictionary<string, SortedSet<string>> parOverlay)
    {
        var fautes = new List<string>();
        var noms = parOverlay.Keys.OrderBy(x => x, StringComparer.Ordinal).ToList();

        for (var i = 0; i < noms.Count; i++)
        {
            for (var j = i + 1; j < noms.Count; j++)
            {
                var partages = parOverlay[noms[i]]
                    .Intersect(parOverlay[noms[j]], StringComparer.Ordinal)
                    .OrderBy(x => x, StringComparer.Ordinal)
                    .ToList();

                if (partages.Count > 0)
                {
                    fautes.Add(
                        $"{noms[i]} et {noms[j]} servent le même hôte "
                        + $"[{string.Join(", ", partages)}] — le §2 exige des "
                        + "environnements séparés");
                }
            }
        }

        return fautes;
    }

    /// <summary>La version de kustomize, ou <c>null</c> s'il est absent.</summary>
    /// <remarks>
    /// LA VERSION SE VÉRIFIE ICI, SINON L'ÉCHEC DÉSIGNE LE MAUVAIS COUPABLE : sur
    /// une version 4, le build échoue sur « json: unknown field
    /// "includeTemplates" », un message qui envoie chercher une faute de frappe
    /// dans un YAML parfaitement correct.
    /// </remarks>
    private static int[]? VersionDeKustomize()
    {
        var (code, sortie, erreur) = Programme.Lancer("kustomize", "version");
        if (code < 0)
        {
            return null;
        }

        // v4 rend « {Version:kustomize/v4.5.4 GitCommit:… } », v5 rend « v5.4.3 ».
        var trouve = VersionLue.Match(sortie + erreur);
        if (!trouve.Success)
        {
            // L'OUTIL RÉPOND MAIS SA VERSION NE SE LIT PAS. On le traite comme
            // présent et récent : refuser ici priverait le contrôle de sa moitié
            // utile sur un binaire qui marche peut-être très bien.
            return [5, 0, 0];
        }

        return
        [
            int.Parse(trouve.Groups[1].Value),
            int.Parse(trouve.Groups[2].Value),
            int.Parse(trouve.Groups[3].Value),
        ];
    }

    /// <summary>La première ligne utile d'un message d'erreur.</summary>
    private static string Premiere(string texte)
    {
        foreach (var ligne in texte.Replace("\r\n", "\n").Split('\n'))
        {
            if (ligne.Trim().Length > 0)
            {
                return ligne.Trim();
            }
        }

        return "(aucun message)";
    }
}
