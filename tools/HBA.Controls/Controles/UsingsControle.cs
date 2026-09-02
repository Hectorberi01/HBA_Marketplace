using System.Text.RegularExpressions;

namespace HBA.Controls.Controles;

/// <summary>
/// Un type référencé sans <c>using</c> accessible — la classe d'erreur CS0246.
/// </summary>
/// <remarks>
/// ═══════════════════════════════════════════════════════════════════════════
/// DÉTECTE LES TYPES UTILISÉS SANS `using` ACCESSIBLE — LA CLASSE D'ERREUR
/// CS0246 QUI A COÛTÉ TROIS ALLERS-RETOURS (`FindFirstValue`,
/// `NotificationChannel`…).
///
/// Heuristique, PAS un compilateur : on indexe « type → namespace » sur tout le
/// dépôt, puis pour chaque fichier on vérifie que chaque type référencé est soit
/// dans le namespace du fichier, soit dans un namespace ENGLOBANT, soit dans un
/// `using`. Un namespace FRÈRE ne compte pas — c'est précisément le piège.
///
/// LA RACINE VIENT DE `HBA.sln`, JAMAIS D'UN CHEMIN EN DUR. La première version
/// Python portait le chemin de la machine où elle avait été écrite. Elle
/// indexait donc 0 fichier partout ailleurs — et ANNONÇAIT « aucun type
/// inaccessible détecté ». Un contrôle qui ne trouve rien parce qu'il ne regarde
/// rien est pire qu'un contrôle absent : il rassure. <see cref="Depot.Racine"/>
/// ferme ce silence-là.
///
/// ── ON NE REGARDE QUE LES POSITIONS DE TYPE ─────────────────────────────────
///
/// Chercher tout identifiant capitalisé produisait 205 signalements sur un dépôt
/// qui compile : `resultat.Error.Code`, une propriété nommée `Message`, un
/// paramètre `email`… Tous des mots courants qui sont AUSSI des types ailleurs.
/// Un outil qui crie 205 fois à tort n'est pas consulté. Quatre formes seulement
/// sont retenues, celles où un identifiant DOIT désigner un type : déclaration
/// de paramètre, de propriété, `new X(`, argument générique.
///
/// ── LE `?` EST RETIRÉ, ET C'EST LA CORRECTION D'UN TROU RÉEL ────────────────
///
/// Deux des quatre motifs capturent le nom AVEC son point d'interrogation. La
/// boucle, elle, itérait sur les noms NORMALISÉS puis testait leur présence dans
/// l'ensemble BRUT : le nom nu n'y étant jamais, le test écartait en silence
/// tout type employé UNIQUEMENT en position nullable. CE QUE CE TROU A COÛTÉ :
/// `ReturnStatus? status` dans `IReturnRequestRepository`, un CS0246 découvert
/// par `docker compose build` après quatre-vingt-deux secondes de compilation —
/// exactement l'aller-retour que ce contrôle existe pour éviter. Il avait tourné
/// et annoncé « aucun type inaccessible détecté ». Normaliser une fois aligne
/// les deux ensembles : ZÉRO nouveau signalement sur le dépôt entier, et le
/// CS0246 retrouvé.
///
/// ── CINQUIÈME MOTIF : L'ACCÈS DE MEMBRE STATIQUE, `Nom.Membre` ──────────────
///
/// Ajouté après un CS0103 que les quatre motifs précédents ne pouvaient pas
/// voir : `PromotionConstantes.Convertir(…)` n'est ni une déclaration, ni un
/// `new`, ni un argument générique — c'est un accès de membre, et le fichier
/// n'avait pas le `using` du namespace FRÈRE.
///
/// Il est RESTREINT AUX CLASSES STATIQUES, et c'est ce qui le rend utilisable :
/// une classe statique ne peut apparaître QUE sous la forme `Nom.Membre`, jamais
/// en type de variable, de paramètre, de propriété ni d'argument générique. Sans
/// cette restriction, l'accès de membre produisait des CENTAINES de faux
/// positifs. Avec elle : un seul sur tout le dépôt, et c'était un homonyme réel
/// — d'où la règle qui suit, de consulter TOUS les homonymes et pas seulement
/// l'index statique. `PageRequest` existe en deux exemplaires, une classe
/// statique et un record ; ne consulter que l'index statique le signalait à
/// tort, et ce seul faux positif suffisait à disqualifier le motif.
///
/// ── `Program` EST INVISIBLE À CETTE HEURISTIQUE ────────────────────────────
///
/// Chaque API en déclare un sous la forme `public partial class Program`, mais
/// dans le namespace GLOBAL, sans instruction `namespace` — or l'indexation
/// saute tout fichier sans namespace. Les vingt `Program` du dépôt sont donc
/// invisibles à l'index, et le seul qui y figure est celui de
/// `HBA.Admin.Desktop`, qui a un namespace parce que c'est une application de
/// bureau. Conséquence observée : les treize `WebApplicationFactory&lt;Program&gt;`
/// des projets de test étaient signalés comme visant le `Program` de la console
/// admin — treize signalements faux sur vingt-sept, de quoi cesser de lire la
/// sortie. `Program` est donc écarté ; le vrai `using` manquant sur un type
/// nommé `Program` n'existe pas, il est résolu dans le namespace global.
///
/// ── POURQUOI CE CONTRÔLE NE REND AUCUNE FAUTE ──────────────────────────────
///
/// Ses signalements sont des CONSTATS, jamais des fautes, et c'est délibéré. Le
/// script Python d'origine n'a jamais rendu autre chose que 0 : `check-all.sh`
/// lit le code de sortie, et ce contrôle n'a donc jamais pu faire échouer la
/// barrière. Il rend aujourd'hui une quinzaine de signalements sur un dépôt qui
/// COMPILE — des homonymes, pas des CS0246. Les inscrire en fautes rendrait la
/// barrière rouge en permanence, et une barrière toujours rouge cesse d'être
/// lue : c'est le même défaut que les 205 signalements, une marche plus haut.
///
/// LE JOUR OÙ LE COMPTE TOMBE À ZÉRO, ILS DOIVENT PASSER EN FAUTES. Tant qu'il
/// ne l'est pas, ce contrôle sert d'inventaire — et le dire ici est le seul
/// moyen qu'il ne se fasse pas prendre pour une garantie.
///
/// ── CE QU'IL NE REGARDE PAS ─────────────────────────────────────────────────
///
/// Les fichiers SANS instruction `namespace` (ni indexés, ni examinés), les
/// dossiers `Migrations` (écartés : du code généré, bruyant et jamais écrit à la
/// main), les alias `using X = Y;` — lus comme un `using` du namespace `X`, ce
/// qui n'est pas ce qu'ils veulent dire — et les `global using` d'un autre
/// projet, invisibles au fichier qui en profite.
///
/// LA LECTURE N'EST PAS CELLE DE <see cref="SourceCsharp.SansCommentaires"/>, ET
/// C'EST VOULU. Ce contrôle VIDE les chaînes (un nom de type cité dans une
/// chaîne n'est pas une référence) et GARDE les commentaires de bloc, là où le
/// lecteur partagé fait l'inverse — il conserve les chaînes, parce que les
/// contrôles de permissions et de RPC cherchent justement dedans. Employer le
/// lecteur partagé ici changerait le jeu de signalements ; les deux lectures ont
/// des buts opposés, et la seconde est écrite en deux expressions rationnelles.
/// CE QUE CETTE LECTURE-CI LAISSE PASSER : un type cité dans un commentaire de
/// BLOC compte comme une référence réelle.
/// ═══════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed class UsingsControle : IControle
{
    /// <inheritdoc/>
    public string Nom => "usings";

    /// <inheritdoc/>
    public string Resume => "un type référencé sans `using` accessible — la classe CS0246";

    private static readonly Regex Declaration = new(
        @"\b(?:public|internal)\s+(?:sealed\s+|static\s+|abstract\s+|partial\s+|readonly\s+)*"
        + @"(?:class|record|struct|interface|enum)\s+(\w+)",
        RegexOptions.Compiled);

    private static readonly Regex DeclarationStatique = new(
        @"\b(?:public|internal)\s+static\s+(?:partial\s+)*class\s+(\w+)",
        RegexOptions.Compiled);

    private static readonly Regex EspaceDeNoms = new(
        @"^\s*namespace\s+([\w.]+)", RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex Utilise = new(
        @"^\s*using\s+(?:static\s+)?([\w.]+);", RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex Chaine = new(
        @"@?\$?""(?:\\.|[^""\\])*""", RegexOptions.Compiled);

    private static readonly Regex CommentaireDeLigne = new(
        @"//[^\n]*", RegexOptions.Compiled);

    /// <summary>Les quatre positions où un identifiant DOIT désigner un type.</summary>
    private static readonly Regex[] Positions =
    [
        new(@"\bnew\s+([A-Z]\w+)\s*[({]", RegexOptions.Compiled),
        new(@"[<,]\s*([A-Z]\w+)\s*[,>]", RegexOptions.Compiled),
        new(@"[(,]\s*([A-Z]\w+\??)\s+[a-z]\w*\s*[,)=]", RegexOptions.Compiled),
        new(@"\b(?:public|private|internal|protected)\s+(?:static\s+|readonly\s+)*"
            + @"([A-Z]\w+\??)\s+\w+\s*\{\s*get", RegexOptions.Compiled),
    ];

    /// <summary>Le cinquième motif : l'accès de membre `Nom.Membre`.</summary>
    private static readonly Regex AccesStatique = new(
        @"(?<![.\w])([A-Z]\w+)\s*\.", RegexOptions.Compiled);

    /// <summary>Voir l'encadré `Program` de l'en-tête de classe.</summary>
    private static readonly string[] Invisibles = ["Program"];

    /// <inheritdoc/>
    public Verdict Executer()
    {
        var fichiers = Fichiers();

        // 1. index type → { namespaces }
        var index = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        var indexStatique = new HashSet<string>(StringComparer.Ordinal);

        foreach (var chemin in fichiers)
        {
            var texte = File.ReadAllText(chemin);
            var espace = EspaceDeNoms.Match(texte);
            if (!espace.Success)
            {
                continue;
            }

            foreach (Match m in Declaration.Matches(texte))
            {
                if (!index.TryGetValue(m.Groups[1].Value, out var espaces))
                {
                    espaces = new SortedSet<string>(StringComparer.Ordinal);
                    index[m.Groups[1].Value] = espaces;
                }

                espaces.Add(espace.Groups[1].Value);
            }

            foreach (Match m in DeclarationStatique.Matches(texte))
            {
                indexStatique.Add(m.Groups[1].Value);
            }
        }

        // 2. détection, fichier par fichier
        var signalements = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var chemin in fichiers)
        {
            var texte = File.ReadAllText(chemin);
            var espace = EspaceDeNoms.Match(texte);
            if (!espace.Success)
            {
                continue;
            }

            var espaceFichier = espace.Groups[1].Value;
            var usings = Utilise.Matches(texte)
                .Select(m => m.Groups[1].Value)
                .ToHashSet(StringComparer.Ordinal);

            // Les chaînes sont VIDÉES, les commentaires de ligne retirés — dans
            // cet ordre : un `//` dans une URL doit disparaître avec sa chaîne,
            // pas emporter la fin de la ligne de code.
            var corps = CommentaireDeLigne.Replace(Chaine.Replace(texte, "\"\""), "");

            var declares = Declaration.Matches(texte)
                .Select(m => m.Groups[1].Value)
                .ToHashSet(StringComparer.Ordinal);

            var positions = new HashSet<string>(StringComparer.Ordinal);
            foreach (var motif in Positions)
            {
                foreach (Match m in motif.Matches(corps))
                {
                    // LE `?` EST RETIRÉ ICI : voir l'encadré de l'en-tête.
                    positions.Add(m.Groups[1].Value.TrimEnd('?'));
                }
            }

            var statiques = AccesStatique.Matches(corps)
                .Select(m => m.Groups[1].Value)
                .ToHashSet(StringComparer.Ordinal);

            foreach (var identifiant in positions.Union(statiques))
            {
                if (declares.Contains(identifiant)
                    || !index.TryGetValue(identifiant, out var possibles))
                {
                    continue;
                }

                if (Invisibles.Contains(identifiant))
                {
                    continue;
                }

                // POUR UN ACCÈS DE MEMBRE, IL FAUT QUE LE NOM SOIT UNE CLASSE
                // STATIQUE. Sinon `resultat.Error.Code` ferait remonter le type
                // `Error`, et le contrôle crierait sur du code valide.
                if (!positions.Contains(identifiant) && !indexStatique.Contains(identifiant))
                {
                    continue;
                }

                // ET IL FAUT REGARDER TOUS LES HOMONYMES, PAS SEULEMENT LE
                // STATIQUE : voir `PageRequest` dans l'en-tête.
                if (possibles.Any(n => Accessible(espaceFichier, usings, n)))
                {
                    continue;
                }

                var cites = string.Join(", ", possibles.Take(2));
                signalements.Add($"{Depot.Relatif(chemin)} : {identifiant} déclaré "
                                 + $"dans {cites}");
            }
        }

        var constats = new List<string>
        {
            $"{fichiers.Count} fichier(s) indexé(s), {index.Count} type(s) connu(s).",
            signalements.Count == 0
                ? "aucun type inaccessible détecté."
                : $"{signalements.Count} type(s) référencé(s) sans `using` accessible — "
                  + "constats, PAS des fautes : voir l'en-tête du contrôle.",
        };

        constats.AddRange(signalements);

        return new Verdict(
            [],
            constats,
            [
                "ce contrôle ne rend AUCUNE faute : ses signalements restent "
                + "heuristiques et portent encore des homonymes sur un dépôt qui "
                + "compile — le jour où le compte tombe à zéro, ils doivent passer en "
                + "fautes",
                "les fichiers sans instruction `namespace` : ni indexés, ni examinés — "
                + "c'est le cas des `Program` des API",
                "les dossiers `Migrations`, écartés du balayage",
            "le dossier `tools/`, écarté depuis qu'il a produit un faux "
            + "rapprochement : ses types ne sont visibles d'aucun projet "
            + "applicatif, mais leurs noms courts entraient en collision",
                "les alias `using X = Y;`, lus comme un `using` du namespace `X`",
                "les `global using` d'un autre projet, invisibles au fichier qui en "
                + "profite",
                "un type cité dans un commentaire de BLOC : les chaînes sont vidées, "
                + "les commentaires de bloc sont gardés",
            ]);
    }

    /// <summary>
    /// Les `.cs` du dépôt, hors `Migrations`.
    /// </summary>
    /// <remarks>
    /// LES MIGRATIONS SONT ÉCARTÉES PARCE QU'ELLES SONT ENGENDRÉES. Personne ne
    /// les écrit à la main, elles portent leurs `using` en propre, et elles
    /// pèsent le quart des fichiers du dépôt.
    ///
    /// `tools/` EST ÉCARTÉ DEPUIS LE 2 SEPTEMBRE 2026, ET LE DÉFAUT ÉTAIT RÉEL.
    ///
    /// Ce contrôle rapproche les types par leur NOM COURT sur tout le dépôt.
    /// Dès que cet outil de contrôles y est entré, son type `Verdict` est venu
    /// se poser en face du `Verdict` de la passerelle, et le contrôle a annoncé
    /// « TokenRevocationMiddleware.cs : Verdict déclaré dans HBA.Controls » —
    /// un rapprochement qui ne peut pas exister : `tools/` n'est référencé par
    /// aucun projet applicatif, ses types ne sont visibles de nulle part.
    ///
    /// La règle générale : tout projet ajouté au dépôt qui n'est pas dans le
    /// graphe de compilation des applications ajoute des HOMONYMES, donc du
    /// bruit. Un contrôle qui crie sur ce qu'il a lui-même introduit finit par
    /// être ignoré en entier.
    /// </remarks>
    private static IReadOnlyList<string> Fichiers()
        => Depot.Fichiers(Depot.Racine, ".cs")
            .Where(f =>
            {
                var segments = Depot.Relatif(f).Split('/');
                return !segments.Contains("Migrations") && segments[0] != "tools";
            })
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// Un namespace est-il joignable depuis un fichier — par un `using`, ou
    /// parce qu'il ENGLOBE le namespace du fichier ?
    /// </summary>
    /// <remarks>
    /// UN NAMESPACE FRÈRE NE COMPTE PAS, et c'est tout l'objet de ce contrôle :
    /// `A.B.C` voit `A`, `A.B` et `A.B.C`, jamais `A.B.D`.
    /// </remarks>
    private static bool Accessible(
        string espaceFichier, IReadOnlySet<string> usings, string espaceType)
    {
        if (usings.Contains(espaceType))
        {
            return true;
        }

        var parties = espaceFichier.Split('.');
        for (var i = parties.Length; i > 0; i--)
        {
            if (string.Join('.', parties.Take(i)) == espaceType)
            {
                return true;
            }
        }

        return false;
    }
}
