using System.Text;
using System.Text.RegularExpressions;

namespace HBA.Controls.Controles;

/// <summary>
/// Équilibre des accolades, parenthèses et crochets dans les fichiers C#.
/// </summary>
/// <remarks>
/// ═══════════════════════════════════════════════════════════════════════════
/// POURQUOI CE CONTRÔLE EXISTE ALORS QUE LE COMPILATEUR LE FAIT MIEUX.
///
/// Il ne remplace pas `dotnet build` : il le PRÉCÈDE. Une accolade manquante
/// coûte un aller-retour complet — restauration, compilation des projets en
/// amont, échec sur `CS1513 } attendue` — pour une faute qu'une lecture de trois
/// secondes attrape. Sur ce dépôt, où l'on édite souvent plusieurs dizaines de
/// fichiers avant de compiler, cet aller-retour est le poste de temps perdu le
/// plus régulier.
///
/// CE N'EST PAS UN COMPTAGE NAÏF, ET IL NE PEUT PAS L'ÊTRE.
///
/// Compter les caractères sur le texte brut donne des résultats faux dans les
/// deux sens sur ce dépôt, pour quatre raisons :
///
///   · les commentaires sont en français et pleins d'apostrophes — « l'hôte »,
///     « d'un » — que tout traitement des littéraux de caractère apparie deux à
///     deux, avalant le code situé entre les deux ;
///   · les commentaires contiennent des parenthèses dépareillées, parce qu'ils
///     citent du code : « (voir `realGateways.Count == 0` plus haut) » ;
///   · les chaînes interpolées imbriquent des chaînes :
///     `$"… {string.Join(…, noms)} …"` ;
///   · les migrations portent du SQL en littéral BRUT — délimité par trois
///     guillemets — dont le contenu déborde d'accolades, de parenthèses et de
///     `$$` PostgreSQL.
///
/// Le lecteur de <see cref="CodeSeul"/> est donc un vrai automate : commentaires
/// de ligne et de bloc, chaînes normales, verbatim, interpolées avec leurs trous
/// — dont le contenu redevient du code, y compris s'il contient une chaîne.
///
/// ET L'ÉQUILIBRE NE SUFFIT PAS. C'EST LA LEÇON DU 21 AOÛT 2026.
///
/// Ce jour-là, une accolade fermante de méthode a disparu et une accolade
/// orpheline est apparue en fin de fichier. Le compte était donc JUSTE, et un
/// contrôle d'équilibre seul serait resté muet. Ce que le fichier était devenu,
/// en revanche, se voyait d'un coup d'œil : la méthode privée suivante s'était
/// retrouvée à l'intérieur du corps de la précédente — une fonction locale, à
/// laquelle C# refuse le modificateur `private`. D'où `CS1513 } attendue`, à
/// quinze lignes de la vraie faute.
///
/// D'où le second contrôle, <see cref="MembresMalPlaces"/> : dans ce dépôt,
/// l'indentation est de quatre espaces par niveau et une déclaration de membre
/// commence toujours par son modificateur d'accès. Si le nombre d'espaces ne
/// correspond pas à la profondeur d'accolades réelle, c'est qu'une accolade
/// manque ou est en trop — même quand le compte tombe juste.
///
/// POURQUOI CE CONTRÔLE NE PASSE PAS PAR <see cref="SourceCsharp"/>.
///
/// `SansCommentaires` CONSERVE les chaînes — ce sont elles que les autres
/// contrôles cherchent — et ne préserve pas les numéros de ligne. Ici il faut
/// exactement l'inverse : les littéraux doivent DISPARAÎTRE, puisque c'est là
/// que vivent les accolades dépareillées, et chaque caractère retiré doit être
/// remplacé par une espace pour que la ligne fautive reste désignable. Deux
/// besoins opposés ; les confondre casserait les deux.
/// ═══════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed class AccoladesControle : IControle
{
    /// <inheritdoc/>
    public string Nom => "accolades";

    /// <inheritdoc/>
    public string Resume => "accolades, parenthèses et crochets équilibrés dans les fichiers C#";

    private static readonly Dictionary<char, char> Paires = new()
    {
        ['{'] = '}',
        ['('] = ')',
        ['['] = ']',
    };

    private static readonly Dictionary<char, char> Fermants =
        Paires.ToDictionary(p => p.Value, p => p.Key);

    private static readonly Regex Declaration = new(
        @"^(\s*)(public|private|protected|internal)\s", RegexOptions.Compiled);

    // LECTURE STRICTE, ET C'EST DÉLIBÉRÉ. Par défaut .NET remplace les octets
    // invalides par un caractère de remplacement, en silence : un fichier au
    // mauvais encodage serait analysé comme s'il était sain. Ici il LÈVE, et le
    // fichier est nommé dans « ce qui n'est pas couvert » plutôt qu'oublié.
    private static readonly UTF8Encoding Utf8Strict = new(false, true);

    /// <summary>L'état d'un trou d'interpolation ouvert.</summary>
    private sealed class Trou
    {
        /// <summary>Profondeur d'accolades du CODE contenu dans le trou.</summary>
        public int Profondeur;

        /// <summary>La chaîne porteuse était-elle verbatim ?</summary>
        public bool Verbatim;
    }

    /// <inheritdoc/>
    public Verdict Executer()
    {
        var fichiers = Depot.Fichiers(Depot.Racine, ".cs")
            .OrderBy(f => Depot.Relatif(f), StringComparer.Ordinal)
            .ToList();

        var fautes = new List<string>();
        var illisibles = new List<string>();
        var total = 0;

        foreach (var chemin in fichiers)
        {
            string source;
            try
            {
                source = File.ReadAllText(chemin, Utf8Strict);
            }
            catch (Exception erreur)
                when (erreur is IOException or DecoderFallbackException
                          or UnauthorizedAccessException)
            {
                illisibles.Add($"{Depot.Relatif(chemin)} — illisible ({erreur.GetType().Name}) : "
                               + "ce fichier n'a PAS été analysé");
                continue;
            }

            var relatif = Depot.Relatif(chemin);
            foreach (var (ligne, message) in Analyser(source))
            {
                total++;
                fautes.Add($"{relatif} ligne {ligne} : {message}");
            }
        }

        var nonCouvert = new List<string>
        {
            "les littéraux BRUTS (trois guillemets) sont traités comme opaques, trous "
            + "d'interpolation compris : du code écrit dans un tel trou n'est pas lu. "
            + "Le dépôt ne les emploie que pour du SQL, sans trou",
            "les lignes qui ne commencent pas par un modificateur d'accès, et celles "
            + "dont l'indentation n'est pas un multiple de quatre espaces, échappent au "
            + "contrôle d'indentation : mieux vaut ne rien dire que dire n'importe quoi",
            "ce contrôle PRÉCÈDE le compilateur, il ne le remplace pas — un fichier "
            + "équilibré et bien indenté peut rester parfaitement incompilable",
        };

        nonCouvert.AddRange(illisibles);

        return new Verdict(
            fautes,
            [$"{fichiers.Count} fichier(s) C# analysé(s), {total} anomalie(s) de structure."],
            nonCouvert);
    }

    /// <summary>Les anomalies d'un source : (ligne, message).</summary>
    private static List<(int Ligne, string Message)> Analyser(string source)
    {
        // COLLECTÉES PENDANT LE DÉCOUPAGE, RAPPORTÉES EN PREMIER.
        //
        // Une chaîne coupée par un saut de ligne n'a AUCUN effet sur l'équilibre
        // des accolades — le contenu est retiré dans les deux cas. Elle ne peut
        // donc pas être trouvée par le comptage : il faut la signaler au moment
        // où on la voit.
        var coupees = new List<(int Ligne, string Extrait)>();

        var nu = CodeSeul(source, position =>
        {
            var numero = 1;
            for (var k = 0; k < position; k++)
            {
                if (source[k] == '\n')
                {
                    numero++;
                }
            }

            var debut = Math.Max(0, position - 60);
            var avant = source.Substring(debut, position - debut);
            var dernier = avant.LastIndexOf('"');
            coupees.Add((numero, dernier >= 0 ? avant[(dernier + 1)..] : avant));
        });

        var pile = new List<(char Ouvrant, int Ligne)>();
        var anomalies = new List<(int Ligne, string Message)>();
        var ligne = 1;

        foreach (var c in nu)
        {
            if (c == '\n')
            {
                ligne++;
            }
            else if (Paires.ContainsKey(c))
            {
                pile.Add((c, ligne));
            }
            else if (Fermants.TryGetValue(c, out var attendu))
            {
                if (pile.Count == 0)
                {
                    anomalies.Add((ligne, $"« {c} » sans ouvrant"));
                }
                else if (pile[^1].Ouvrant != attendu)
                {
                    var (ouvrant, ouverte) = pile[^1];
                    pile.RemoveAt(pile.Count - 1);
                    anomalies.Add((ligne, $"« {c} » ferme un « {ouvrant} » ouvert ligne {ouverte}"));
                }
                else
                {
                    pile.RemoveAt(pile.Count - 1);
                }
            }
        }

        foreach (var (ouvrant, ouverte) in pile)
        {
            anomalies.Add((ouverte, $"« {ouvrant} » jamais fermé"));
        }

        // SEULEMENT SI L'ÉQUILIBRE EST BON. Sur un fichier déjà déséquilibré, la
        // profondeur dérive après la première anomalie et CHAQUE déclaration
        // suivante serait signalée : cinquante lignes de bruit pour une faute.
        if (anomalies.Count == 0)
        {
            anomalies.AddRange(MembresMalPlaces(nu));
        }

        // En tête : c'est la cause, et le reste en découle souvent.
        var entete = new List<(int Ligne, string Message)>();
        foreach (var (numero, extrait) in coupees)
        {
            var bout = extrait.Trim();
            if (bout.Length > 40)
            {
                bout = bout[..40];
            }

            entete.Add((numero,
                $"saut de ligne dans une chaîne (CS1010) — « {bout}… » ; écrire un "
                + "échappement, ou passer la chaîne en verbatim"));
        }

        entete.AddRange(anomalies);
        return entete;
    }

    /// <summary>
    /// Rend le source privé de ses commentaires et de ses littéraux.
    /// </summary>
    /// <remarks>
    /// Les caractères retirés sont remplacés par des espaces afin que les numéros
    /// de ligne restent EXACTS : c'est ce qui permet de désigner la ligne fautive.
    /// </remarks>
    /// <param name="source">Le source C# entier.</param>
    /// <param name="signaler">
    /// Appelé avec la position d'un saut de ligne trouvé DANS une chaîne non
    /// verbatim. Voir <see cref="LireChaine"/> : ce cas était vu et tu.
    /// </param>
    private static string CodeSeul(string source, Action<int>? signaler)
    {
        var sortie = new StringBuilder(source.Length);
        var interpolees = new List<Trou>();
        var i = 0;
        var n = source.Length;

        while (i < n)
        {
            var c = source[i];
            var suivant = i + 1 < n ? source[i + 1] : '\0';

            // ── Commentaires ────────────────────────────────────────────────
            if (c == '/' && suivant == '/')
            {
                while (i < n && source[i] != '\n')
                {
                    Pousser(sortie, source[i]);
                    i++;
                }

                continue;
            }

            if (c == '/' && suivant == '*')
            {
                while (i < n && !(source[i] == '*' && i + 1 < n && source[i + 1] == '/'))
                {
                    Pousser(sortie, source[i]);
                    i++;
                }

                var reste = Math.Min(2, n - i);
                for (var k = 0; k < reste; k++)
                {
                    Pousser(sortie, source[i]);
                    i++;
                }

                continue;
            }

            // ── Littéral de caractère ───────────────────────────────────────
            if (c == '\'')
            {
                Pousser(sortie, c);
                i++;
                while (i < n && source[i] != '\'')
                {
                    if (source[i] == '\\')
                    {
                        Pousser(sortie, source[i]);
                        i++;
                        if (i < n)
                        {
                            Pousser(sortie, source[i]);
                            i++;
                        }

                        continue;
                    }

                    Pousser(sortie, source[i]);
                    i++;
                }

                if (i < n)
                {
                    Pousser(sortie, source[i]);
                    i++;
                }

                continue;
            }

            // ── Préfixes de chaîne ──────────────────────────────────────────
            var j = i;
            var verbatim = false;
            var interpole = false;
            while (j < n && (source[j] == '@' || source[j] == '$'))
            {
                verbatim |= source[j] == '@';
                interpole |= source[j] == '$';
                j++;
            }

            // ── Littéral BRUT ───────────────────────────────────────────────
            //
            // TRAITÉ COMME OPAQUE, TROUS D'INTERPOLATION COMPRIS.
            //
            // Un littéral brut interpolé peut contenir du code dans ses trous ;
            // ne pas le lire revient à ignorer ce code. C'est un choix : manquer
            // une anomalie coûte un aller-retour de compilation, en inventer une
            // fait perdre confiance dans le contrôle — et un contrôle en qui on
            // ne croit plus, personne ne le lance. Le dépôt n'utilise les
            // littéraux bruts que pour du SQL, sans trou.
            if (j + 3 <= n && source[j] == '"' && source[j + 1] == '"' && source[j + 2] == '"')
            {
                var longueur = 0;
                while (j + longueur < n && source[j + longueur] == '"')
                {
                    longueur++;
                }

                var entete = j - i + longueur;
                for (var k = 0; k < entete; k++)
                {
                    Pousser(sortie, source[i]);
                    i++;
                }

                while (i < n)
                {
                    if (source[i] == '"')
                    {
                        var fin = 0;
                        while (i + fin < n && source[i + fin] == '"')
                        {
                            fin++;
                        }

                        var termine = fin >= longueur;
                        for (var k = 0; k < fin; k++)
                        {
                            Pousser(sortie, source[i]);
                            i++;
                        }

                        if (termine)
                        {
                            break;
                        }

                        continue;
                    }

                    Pousser(sortie, source[i]);
                    i++;
                }

                continue;
            }

            // ── Chaînes préfixées ───────────────────────────────────────────
            if (j > i && j < n && source[j] == '"')
            {
                var entete = j - i + 1;
                for (var k = 0; k < entete; k++)
                {
                    Pousser(sortie, source[i]);
                    i++;
                }

                i = LireChaine(source, i, n, verbatim, interpole, sortie, interpolees, signaler);
                continue;
            }

            if (c == '"')
            {
                Pousser(sortie, c);
                i++;
                i = LireChaine(source, i, n, false, false, sortie, interpolees, signaler);
                continue;
            }

            // ── Sortie d'un trou d'interpolation ────────────────────────────
            if (c == '}' && interpolees.Count > 0 && interpolees[^1].Profondeur == 0)
            {
                var contexte = interpolees[^1];
                interpolees.RemoveAt(interpolees.Count - 1);
                Pousser(sortie, c);
                i++;
                i = LireChaine(
                    source, i, n, contexte.Verbatim, true, sortie, interpolees, signaler);
                continue;
            }

            if (interpolees.Count > 0)
            {
                if (c == '{')
                {
                    interpolees[^1].Profondeur++;
                }
                else if (c == '}')
                {
                    interpolees[^1].Profondeur--;
                }
            }

            sortie.Append(c);
            i++;
        }

        return sortie.ToString();
    }

    /// <summary>
    /// Consomme le corps d'une chaîne. Rend l'indice APRÈS le guillemet fermant.
    /// </summary>
    private static int LireChaine(
        string source,
        int i,
        int n,
        bool verbatim,
        bool interpole,
        StringBuilder sortie,
        List<Trou> interpolees,
        Action<int>? signaler)
    {
        while (i < n)
        {
            var c = source[i];

            if (!verbatim && c == '\\')
            {
                Pousser(sortie, c);
                i++;
                if (i < n)
                {
                    Pousser(sortie, source[i]);
                    i++;
                }

                continue;
            }

            if (c == '"')
            {
                // En verbatim, deux guillemets forment un guillemet littéral,
                // pas la fin de la chaîne.
                if (verbatim && i + 1 < n && source[i + 1] == '"')
                {
                    Pousser(sortie, c);
                    Pousser(sortie, source[i + 1]);
                    i += 2;
                    continue;
                }

                Pousser(sortie, c);
                return i + 1;
            }

            if (interpole && c == '{')
            {
                // Deux accolades forment une accolade littérale.
                if (i + 1 < n && source[i + 1] == '{')
                {
                    Pousser(sortie, c);
                    Pousser(sortie, source[i + 1]);
                    i += 2;
                    continue;
                }

                // Ouverture d'un trou : ce qui suit est du CODE.
                Pousser(sortie, c);
                interpolees.Add(new Trou { Profondeur = 0, Verbatim = verbatim });
                return i + 1;
            }

            if (!verbatim && c == '\n')
            {
                // ═════════════════════════════════════════════════════════════
                // UN SAUT DE LIGNE DANS UNE CHAÎNE NON VERBATIM — C'EST CS1010.
                //
                // Ce cas était DÉTECTÉ et TU. On rendait la main pour ne pas
                // avaler le reste du fichier, et on ne disait rien : les
                // accolades restaient équilibrées, le contrôle rendait
                // « 0 anomalie », et le compilateur crachait dix-neuf erreurs
                // sur le même fichier.
                //
                // C'est arrivé pour de vrai (lot 4.1, `FoodCartModuleInstaller`) :
                // un `\n` écrit comme un vrai retour à la ligne au lieu de
                // l'échappement. Le contrôle qui existait pour attraper
                // exactement ce genre de faute a répondu vert.
                //
                // ET C'EST PRÉCISÉMENT LE PIRE MODE DE DÉFAILLANCE D'UN
                // GARDE-FOU : il ne manque pas l'anomalie par ignorance, il la
                // VOIT et se tait.
                // ═════════════════════════════════════════════════════════════
                signaler?.Invoke(i);
                return i;
            }

            Pousser(sortie, c);
            i++;
        }

        return i;
    }

    /// <summary>Un caractère retiré : une espace, sauf le saut de ligne.</summary>
    private static void Pousser(StringBuilder sortie, char c)
    {
        sortie.Append(c == '\n' ? c : ' ');
    }

    /// <summary>Profondeur d'accolades AU DÉBUT de chaque ligne du source dépouillé.</summary>
    private static int[] ProfondeursParLigne(string[] lignes)
    {
        var profondeurs = new int[lignes.Length];
        var courante = 0;

        for (var index = 0; index < lignes.Length; index++)
        {
            profondeurs[index] = courante;
            courante += lignes[index].Count(c => c == '{') - lignes[index].Count(c => c == '}');
        }

        return profondeurs;
    }

    /// <summary>
    /// Déclarations de membre dont l'indentation contredit la profondeur réelle.
    /// </summary>
    /// <remarks>
    /// ON NE REGARDE QUE LES LIGNES COMMENÇANT PAR UN MODIFICATEUR D'ACCÈS.
    ///
    /// Elles sont, dans ce dépôt, toujours des déclarations de membre — jamais
    /// des continuations d'expression, jamais des `case`, jamais des
    /// initialiseurs. C'est ce qui rend la comparaison indentation/profondeur
    /// fiable ici alors qu'elle serait bruyante sur n'importe quelle ligne.
    ///
    /// Les lignes indentées avec autre chose que des multiples de quatre espaces
    /// sont ignorées : mieux vaut ne rien dire que dire n'importe quoi.
    /// </remarks>
    private static List<(int Ligne, string Message)> MembresMalPlaces(string nu)
    {
        var lignes = nu.Split('\n');
        var profondeurs = ProfondeursParLigne(lignes);
        var anomalies = new List<(int Ligne, string Message)>();

        for (var index = 0; index < lignes.Length; index++)
        {
            var correspondance = Declaration.Match(lignes[index]);
            if (!correspondance.Success)
            {
                continue;
            }

            var indentation = correspondance.Groups[1].Value;
            if (indentation.Contains('\t') || indentation.Length % 4 != 0)
            {
                continue;
            }

            var attendue = indentation.Length / 4;
            var reelle = profondeurs[index];

            if (attendue != reelle)
            {
                anomalies.Add((
                    index + 1,
                    $"déclaration indentée pour la profondeur {attendue}, mais la profondeur "
                    + $"réelle est {reelle} — une accolade manque ou est en trop plus haut"));
            }
        }

        return anomalies;
    }
}
