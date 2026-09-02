using System.Text.RegularExpressions;

namespace HBA.Controls.Controles;

/// <summary>
/// Une interface qui change laisse ses doubles de test derrière elle.
/// </summary>
/// <remarks>
/// ═══════════════════════════════════════════════════════════════════════════
/// TROIS ALLERS-RETOURS DE BUILD POUR LE MÊME DÉFAUT, EN UNE SEULE SÉANCE.
///
/// Ajouter un paramètre à une méthode de dépôt — une BORNE, au lot 8.4 — casse
/// toutes ses implémentations. Celles du code de production se voient : on vient
/// de les écrire. Celles des TESTS, non : ce sont des classes qu'on ne relit
/// jamais, souvent enfouies au bas d'un fichier de test, et dont les méthodes
/// lèvent `NotSupportedException` parce qu'aucun test ne les appelle.
///
/// Le compilateur les attrape — mais seulement au build, c'est-à-dire après
/// avoir rendu la main. Chaque oubli coûte un cycle complet.
///
/// CE QU'IL VÉRIFIE
///
/// Pour chaque interface déclarée dans le dépôt, il relève ses méthodes (nom +
/// nombre de paramètres). Pour chaque classe qui déclare implémenter cette
/// interface, il vérifie qu'une méthode de même NOM et de même ARITÉ existe.
///
/// NOM ET ARITÉ, PAS SIGNATURE COMPLÈTE. Comparer les types demanderait de
/// résoudre les alias, les génériques et les `using` — c'est-à-dire d'écrire un
/// compilateur. L'arité suffit à attraper le défaut visé : un paramètre AJOUTÉ
/// ou RETIRÉ. Elle ne verra pas un type changé à arité constante, et c'est
/// assumé.
///
/// IL BALAIE TOUT LE DÉPÔT, PAS SEULEMENT `services/`, `shared/` ET `apps/`.
///
/// C'est la seule différence de périmètre avec les autres contrôles, et elle est
/// la raison d'être de celui-ci : le défaut vit dans `tests/`. Passer par
/// <see cref="SourceCsharp.Fichiers"/>, qui s'arrête aux trois racines de
/// production, reviendrait à écrire un contrôle qui ne peut PAS voir la panne
/// qu'il existe pour voir. Le parcours part donc de <see cref="Depot.Racine"/>,
/// qui est vérifiée à la construction — un dépôt sans `HBA.sln` lève.
///
/// CE QU'IL NE VÉRIFIE PAS, ET POURQUOI IL LE DIT AU LIEU DE SE TAIRE
///
///   • Une classe qui hérite d'une base peut tenir le contrat par héritage. Elle
///     est rendue en CONSTAT, jamais en FAUTE : le contrôle ne sait pas lire la
///     base.
///   • Les membres d'interface à CORPS (méthodes par défaut, C# 8) ne sont pas
///     exigés — ils sont donc écartés du relevé.
///   • Une méthode déclarée `abstract` COMPTE comme implémentée : elle satisfait
///     le compilateur et reporte l'écriture sur les dérivées. Le contrôle ne va
///     pas vérifier que les dérivées, elles, l'écrivent — le compilateur le
///     fait.
///   • Les propriétés, indexeurs et événements ne sont pas relevés : le défaut
///     visé est le paramètre ajouté, qui n'existe que sur les méthodes.
///   • Une classe partielle dont les méthodes vivent dans un autre fichier
///     serait un faux positif. Le dépôt n'en a pas ; s'il en gagne une, ce texte
///     est l'endroit où le dire.
///   • Les interfaces sont rapprochées par leur NOM COURT, sans espace de noms.
///     Deux `IDepot` dans deux espaces différents fusionneraient leurs contrats.
///
/// CE CONTRÔLE NE REMPLACE PAS LE COMPILATEUR. Il l'ANTICIPE, sur la seule
/// famille d'erreurs qui se répète — et il tourne en quelques secondes là où un
/// build en prend deux cents.
/// ═══════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed class ImplementationsControle : IControle
{
    /// <inheritdoc/>
    public string Nom => "implementations";

    /// <inheritdoc/>
    public string Resume => "toute classe tient le nom et l'arité des méthodes de ses interfaces";

    private static readonly Regex DeclarationInterface = new(
        @"^\s*(?:public|internal|private|protected)?\s*(?:partial\s+)?interface\s+(I\w+)",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex DeclarationClasse = new(
        @"^\s*(?:public|internal|private|protected)?\s*"
        + @"(?:sealed\s+|abstract\s+|static\s+|partial\s+)*"
        + @"class\s+(\w+)\s*(?:<[^>]*>)?\s*:\s*([^\{\r\n]+)",
        RegexOptions.Compiled | RegexOptions.Multiline);

    // Une déclaration de méthode d'interface : se termine par « ; », pas de corps.
    private static readonly Regex Membre = new(
        @"^\s*(?!//|/\*|\*)([\w<>\[\],\?\. ]+?)\s+(\w+)\s*\(([^;{]*)\)\s*;",
        RegexOptions.Compiled | RegexOptions.Multiline);

    // Une méthode de classe : corps en accolade OU en expression.
    private static readonly Regex MethodeClasse = new(
        @"\b(\w+)\s*(?:<[^>()]*>)?\s*\(([^)]*)\)\s*(?:=>|\{)",
        RegexOptions.Compiled);

    // ═══════════════════════════════════════════════════════════════════════
    // UNE MÉTHODE PEUT TENIR LE CONTRAT SANS AVOIR DE CORPS.
    //
    // `public abstract Task<X> FaireAsync(Y y, CancellationToken ct);` implémente
    // l'interface : elle reporte l'écriture sur les classes dérivées, mais elle
    // SATISFAIT le compilateur. Reconnue par sa seule terminaison en « ; », donc
    // invisible pour MethodeClasse, qui exige `=>` ou `{`.
    //
    // C'est ce qui a valu trois faux positifs à HttpPaymentGatewayBase, une base
    // abstraite dont les quatre méthodes de passerelle sont déclarées abstraites.
    //
    // On EXIGE le modificateur (abstract / extern / partial) plutôt que d'accepter
    // toute ligne finissant par « ; » : sans lui, un simple appel de méthode dans
    // un corps — `await Publier(evenement);` — passerait pour une déclaration et
    // ferait taire le contrôle sur une vraie absence.
    // ═══════════════════════════════════════════════════════════════════════
    private static readonly Regex MembreSansCorps = new(
        @"^\s*(?:public|protected|internal|private)?\s*(?:public|protected|internal|private)?\s*"
        + @"(?:abstract|extern|partial)\s+[^;{()]*?\b(\w+)\s*(?:<[^>()]*>)?\s*\(([^;{]*)\)\s*;",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly string[] AccesseursDePropriete = ["get", "set", "init"];

    /// <summary>Compte les paramètres, en ignorant les virgules imbriquées.</summary>
    private static int Arite(string parametres)
    {
        var texte = parametres.Trim();
        if (texte.Length == 0)
        {
            return 0;
        }

        var profondeur = 0;
        var compte = 1;
        foreach (var caractere in texte)
        {
            if (caractere is '<' or '(' or '[')
            {
                profondeur++;
            }
            else if (caractere is '>' or ')' or ']')
            {
                profondeur--;
            }
            else if (caractere == ',' && profondeur == 0)
            {
                compte++;
            }
        }

        return compte;
    }

    /// <summary>Le bloc { … } qui suit `depart`, accolades équilibrées.</summary>
    private static string Corps(string source, int depart)
    {
        var ouverture = source.IndexOf('{', depart);
        if (ouverture == -1)
        {
            return string.Empty;
        }

        var profondeur = 0;
        for (var i = ouverture; i < source.Length; i++)
        {
            if (source[i] == '{')
            {
                profondeur++;
            }
            else if (source[i] == '}')
            {
                profondeur--;
                if (profondeur == 0)
                {
                    return source[ouverture..i];
                }
            }
        }

        return source[ouverture..];
    }

    /// <inheritdoc/>
    public Verdict Executer()
    {
        // ═══════════════════════════════════════════════════════════════════
        // LES COMMENTAIRES SONT RETIRÉS AVANT TOUTE LECTURE.
        //
        // Sans cela, une méthode dont la signature est séparée de son corps par
        // un commentaire — forme courante dans ce dépôt, où les encadrés
        // expliquent la requête juste avant le `=>` — passe pour non
        // implémentée. C'est ce qui a valu DIX-NEUF faux positifs à la première
        // exécution de ce contrôle, sur du code qui compilait. Un contrôle qui
        // crie au loup dix-neuf fois est pire que pas de contrôle du tout : on
        // cesse de le lire.
        // ═══════════════════════════════════════════════════════════════════
        var sources = new List<(string Chemin, string Source)>();
        foreach (var chemin in Depot.Fichiers(Depot.Racine, ".cs"))
        {
            if (chemin.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            sources.Add((chemin, SourceCsharp.SansCommentaires(File.ReadAllText(chemin))));
        }

        // ── Le contrat de chaque interface : nom -> {(methode, arite)}
        var contrats = new Dictionary<string, HashSet<(string Methode, int Arite)>>(
            StringComparer.Ordinal);

        foreach (var (_, source) in sources)
        {
            foreach (Match declaration in DeclarationInterface.Matches(source))
            {
                var bloc = Corps(source, declaration.Index + declaration.Length);
                var membres = new HashSet<(string Methode, int Arite)>();
                foreach (Match m in Membre.Matches(bloc))
                {
                    // Un `get;`/`set;` de propriété n'est pas une méthode.
                    if (AccesseursDePropriete.Contains(m.Groups[2].Value))
                    {
                        continue;
                    }

                    membres.Add((m.Groups[2].Value, Arite(m.Groups[3].Value)));
                }

                if (membres.Count == 0)
                {
                    continue;
                }

                if (!contrats.TryGetValue(declaration.Groups[1].Value, out var deja))
                {
                    deja = [];
                    contrats[declaration.Groups[1].Value] = deja;
                }

                deja.UnionWith(membres);
            }
        }

        var fautes = new List<string>();
        var incertains = new List<string>();
        var classesExaminees = 0;

        foreach (var (chemin, source) in sources)
        {
            foreach (Match declaration in DeclarationClasse.Matches(source))
            {
                var classe = declaration.Groups[1].Value;
                var heritages = declaration.Groups[2].Value
                    .Split(',')
                    .Select(h => h.Trim().Split('<')[0])
                    .ToList();

                var attendus = new HashSet<(string Methode, int Arite)>();
                var interfaces = new List<string>();
                foreach (var h in heritages)
                {
                    if (contrats.TryGetValue(h, out var contrat))
                    {
                        attendus.UnionWith(contrat);
                        interfaces.Add(h);
                    }
                }

                if (attendus.Count == 0)
                {
                    continue;
                }

                classesExaminees++;

                var bloc = Corps(source, declaration.Index + declaration.Length);
                var presentes = new HashSet<(string Methode, int Arite)>();
                foreach (Match m in MethodeClasse.Matches(bloc))
                {
                    presentes.Add((m.Groups[1].Value, Arite(m.Groups[2].Value)));
                }

                foreach (Match m in MembreSansCorps.Matches(bloc))
                {
                    presentes.Add((m.Groups[1].Value, Arite(m.Groups[2].Value)));
                }

                var manquants = attendus
                    .Where(a => !presentes.Contains(a))
                    .OrderBy(a => a.Methode, StringComparer.Ordinal)
                    .ThenBy(a => a.Arite)
                    .ToList();

                if (manquants.Count == 0)
                {
                    continue;
                }

                // Une base non-interface peut tenir le contrat : on ne tranche pas.
                var avecBase = heritages.Any(
                    h => !contrats.ContainsKey(h) && !h.StartsWith('I'));
                var relatif = Depot.Relatif(chemin);

                foreach (var (methode, n) in manquants)
                {
                    if (avecBase)
                    {
                        incertains.Add(
                            $"peut-être tenu par une classe de base — non tranché : "
                            + $"{relatif} : {classe}.{methode}/{n}");
                        continue;
                    }

                    fautes.Add(
                        $"{relatif} : « {classe} » n'implémente pas {methode}/{n}, exigée "
                        + $"par {string.Join(", ", interfaces)}. Un paramètre a probablement "
                        + "été ajouté ou retiré côté interface.");
                }
            }
        }

        var constats = new List<string>
        {
            $"{sources.Count} fichier(s) .cs lu(s), {contrats.Count} interface(s) au contrat "
            + $"relevé, {classesExaminees} classe(s) implémentant une interface du dépôt.",
        };

        // ON N'EN MONTRE QUE VINGT, COMME LE SCRIPT D'ORIGINE — ET ON DIT
        // COMBIEN RESTENT. Une liste tronquée en silence laisserait croire que
        // l'incertitude est bornée alors qu'elle ne l'est pas.
        constats.AddRange(incertains.Take(20));
        if (incertains.Count > 20)
        {
            constats.Add($"… et {incertains.Count - 20} autre(s) cas non tranché(s).");
        }

        return new Verdict(
            fautes,
            constats,
            [
                "les propriétés, indexeurs et événements — seules les méthodes sont relevées",
                "les types des paramètres : seule leur QUANTITÉ est comparée, un type "
                + "changé à arité constante passe",
                "les classes partielles dont les méthodes vivent dans un autre fichier",
                "les contrats tenus par une classe de base : rendus en constat, jamais "
                + "en faute, le contrôle ne sait pas lire la base",
                "les interfaces homonymes de deux espaces de noms différents, rapprochées "
                + "par leur seul nom court",
            ]);
    }
}
