using System.Text.RegularExpressions;

namespace HBA.Controls.Controles;

/// <summary>
/// Une permission que personne n'interroge est un droit sans effet.
/// </summary>
/// <remarks>
/// ═══════════════════════════════════════════════════════════════════════════
/// IL Y EN AVAIT SEPT, ET RIEN NE PERMETTAIT DE LE SAVOIR.
///
/// Le catalogue déclare cinquante-sept permissions. Chacune est attribuée à des
/// rôles, affichée au vendeur, cochable dans un rôle personnalisé. Sept
/// n'étaient exigées par AUCUNE route ni AUCUN handler :
///
///   • le transfert d'inventaire et la lecture des mouvements de stock — le rôle
///     gestionnaire de stock promet « Stocks, ajustements, transferts », et le
///     mot « transfert » n'apparaissait nulle part dans inventory-service
///     (fermées au lot 7.3) ;
///   • le litige de retour — aucune notion de litige n'existe ;
///   • la lecture des avis — ouverte à tout compte authentifié ;
///   • l'attribution de rôle — doublon de l'attribution de rôle à un membre ;
///   • la mise à jour du compte bancaire — doublon de la configuration des
///     versements, tous deux critiques et réservés au propriétaire ;
///   • la mise à jour de la politique de sécurité — sans objet, et c'était déjà
///     écrit.
///
/// Le constat demandait de croiser cinquante-sept déclarations avec tous les
/// appels du dépôt. Personne ne le fait deux fois.
///
/// CE CONTRÔLE A MENTI À SA PREMIÈRE EXÉCUTION, ET C'EST INSTRUCTIF.
///
/// Il cherchait les usages sous la seule forme symbolique
/// <c>MerchantPermission.X</c> / <c>MerchantCapabilities.X</c>. Or les routes de
/// retours vendeur recopiaient les codes en chaînes littérales dans leurs propres
/// <c>private const string</c>. Le contrôle a donc annoncé cinq permissions de
/// retour « sans garde » alors qu'elles gardaient DIX routes — et la correction
/// évidente aurait été de les inscrire dans la liste des permissions assumées
/// sans garde, donc de graver dans le dépôt le contraire de la vérité.
///
/// C'est la quatrième fois dans ce chantier qu'un contrôle partage l'hypothèse
/// fausse du code qu'il contrôle. D'où la troisième anomalie ci-dessous : le
/// littéral n'est plus seulement VU, il est REFUSÉ. Un code de permission recopié
/// à la main compile même mal orthographié, et une garde mal orthographiée refuse
/// TOUT LE MONDE — ce qui est aussi cassé qu'une garde absente, et personne ne
/// s'en aperçoit avant le premier vendeur bloqué.
///
/// CE QU'IL VÉRIFIE
///
/// Il calcule l'ensemble des permissions RÉELLEMENT exigées — tout symbole
/// <c>MerchantPermission.X</c>, tout <c>MerchantCapabilities.X</c>, et tout code
/// littéral hors des trois fichiers de déclaration et hors des commentaires — et
/// le compare à la liste <c>SansGardeAssumee</c>. Trois anomalies :
///
///   • une permission sans garde ET absente de la liste : un droit sans effet que
///     personne n'a assumé. À brancher, ou à inscrire en disant pourquoi ;
///   • une permission inscrite dans la liste ET pourtant gardée : la liste ment,
///     et un lecteur planifiera un lot déjà fait — c'est exactement ce que les
///     requêtes d'audit faisaient pour les journaux d'audit ;
///   • un code de permission écrit en chaîne littérale hors des déclarations : la
///     garde existe peut-être, mais rien ne garantit qu'elle vise la bonne
///     permission. Le catalogue expose une constante par code — c'est le
///     compilateur qui doit tenir cette correspondance, pas la vigilance du
///     lecteur.
///
/// LES COMMENTAIRES SONT RETIRÉS AVANT LA RECHERCHE DU LITTÉRAL, via
/// <see cref="SourceCsharp.SansCommentaires"/>. Sans cela, un bandeau qui cite un
/// code pour EXPLIQUER le problème passerait pour la garde elle-même — le fichier
/// corrigé de return-refund fait exactement cela.
///
/// Le SYMBOLE, lui, se lit sur le source entier : le voir dans un commentaire ne
/// coûte rien, puisqu'il faudrait de toute façon que la constante existe.
///
/// CE QU'IL NE VÉRIFIE PAS : que la garde soit au BON endroit, ni qu'elle couvre
/// toutes les routes qu'elle devrait. Une permission exigée par une seule route
/// sur cinq passe ce contrôle.
/// ═══════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed class PermissionsControle : IControle
{
    /// <inheritdoc/>
    public string Nom => "permissions";

    /// <inheritdoc/>
    public string Resume => "aucune permission déclarée n'est un droit sans effet";

    // Les fichiers qui DÉCLARENT ou ATTRIBUENT : y voir une permission n'est pas
    // une garde, c'est sa définition.
    private static readonly string[] Declarations =
        ["MerchantPermission.cs", "MerchantCapabilities.cs", "SellerRole.cs"];

    private static readonly Regex Entree = new(
        @"new\(MerchantPermission\.(\w+),\s*[""]([A-Z_]+)[""]", RegexOptions.Compiled);

    private static readonly Regex Symbole = new(
        @"Merchant(?:Permission|Capabilities)\.(\w+)", RegexOptions.Compiled);

    private static readonly Regex Assumee = new(
        @"MerchantPermission\.(\w+)", RegexOptions.Compiled);

    /// <summary>
    /// LA DÉCLARATION de <c>SansGardeAssumee</c>, et non sa première MENTION.
    /// </summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// CE MOTIF EXISTE PARCE QUE LE CONTRÔLE A ACCUSÉ CINQUANTE-TROIS
    ///    PERMISSIONS D'UN COUP.
    ///
    /// La lecture partait d'`IndexOf("SansGardeAssumee")` — la première fois que
    /// ces seize lettres apparaissent dans le fichier. Un COMMENTAIRE qui nommait
    /// la liste, deux cent cinquante lignes plus haut, a suffi : l'ancre s'est
    /// posée dans l'énumération, l'accolade suivante était celle du catalogue, et
    /// le bloc lu a couvert QUINZE MILLE caractères — donc les cinquante-huit
    /// entrées `new(MerchantPermission.X, …)`. Toutes les permissions sont
    /// devenues « assumées », donc toutes les permissions GARDÉES sont devenues
    /// des fautes.
    ///
    /// Un témoin qui accuse tout le monde n'accuse personne : c'est le chiffre
    /// démesuré qui l'a trahi, pas une relecture.
    ///
    /// LA FAMILLE DE CE DÉFAUT EST CELLE QUE CE DÉPÔT FERME PARTOUT AILLEURS :
    /// chercher un NOM au lieu d'une DÉCLARATION. Un nom apparaît dans les
    /// commentaires, dans la documentation, dans le message d'erreur qui parle de
    /// la chose. Une déclaration n'apparaît qu'une fois.
    ///
    /// SI LA FORME DE LA DÉCLARATION CHANGE, ce motif ne trouve plus rien et le
    /// contrôle le DIT — voir l'appelant. Il ne rend pas un ensemble vide en
    /// silence, ce qui transformerait cinq constats en cinq fautes au motif faux.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
    private static readonly Regex DeclarationAssumees = new(
        @"SansGardeAssumee\s*\{\s*get;\s*\}\s*=\s*new\s+HashSet<MerchantPermission>",
        RegexOptions.Compiled);

    /// <inheritdoc/>
    public Verdict Executer()
    {
        var nonCouvert = NonCouvert();

        // Le catalogue est un FICHIER, pas un dossier : `Depot.Dossier` ne peut
        // rien pour lui. Son absence reste une FAUTE et non un contrôle « sauté »
        // — la version Python se taisait ici, et un contrôle qui se tait rend le
        // même vert qu'un contrôle qui a tout vu.
        var chemin = Depot.Chemin(
            "services", "marketplace", "seller-service", "src",
            "HBA.Merchants.Domain", "Members", "MerchantPermission.cs");

        if (!File.Exists(chemin))
        {
            return new Verdict(
                [$"{Depot.Relatif(chemin)} introuvable : ce contrôle ne peut RIEN "
                 + "comparer. Corriger le chemin plutôt que de laisser rendre zéro."],
                [],
                nonCouvert);
        }

        var source = File.ReadAllText(chemin);

        var catalogue = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match m in Entree.Matches(source))
        {
            catalogue[m.Groups[1].Value] = m.Groups[2].Value;
        }

        if (catalogue.Count == 0)
        {
            return new Verdict(
                [$"aucune entrée reconnue dans {Depot.Relatif(chemin)} : la forme des "
                 + "déclarations a changé et le motif de lecture ne la suit plus. Sans "
                 + "catalogue, ce contrôle comparerait le vide au vide."],
                [],
                nonCouvert);
        }

        // `null` VEUT DIRE « JE N'AI PAS TROUVÉ LA DÉCLARATION », ET C'EST
        // DIFFÉRENT DE « LA LISTE EST VIDE ». Confondre les deux ferait rendre
        // cinq fautes au motif « n'est pas inscrite dans SansGardeAssumee »
        // alors que le vrai défaut serait la lecture de ce contrôle.
        var assumees = Assumees(source);

        if (assumees is null)
        {
            return new Verdict(
                [$"la déclaration de `MerchantPermissions.SansGardeAssumee` est "
                 + $"introuvable dans {Depot.Relatif(chemin)} : sa forme a changé et le "
                 + "motif de lecture ne la suit plus. Ce contrôle ne peut PAS trancher "
                 + "sans elle — rendre un verdict ici accuserait les permissions "
                 + "légitimement assumées, ou en absoudrait d'autres."],
                [],
                nonCouvert);
        }

        // Le chemin inverse : du code au nom, pour rendre compte d'un littéral.
        var parCode = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var paire in catalogue)
        {
            parCode[paire.Value] = paire.Key;
        }

        var exigees = new HashSet<string>(StringComparer.Ordinal);
        var litteraux = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        var lus = 0;

        foreach (var fichier in Depot.Fichiers(Depot.Racine, ".cs"))
        {
            var relatif = Depot.Relatif(fichier);

            // Les tests sont écartés : un usage qui n'existerait que dans un test
            // n'est pas une garde de production.
            if (relatif.Split('/').Contains("tests"))
            {
                continue;
            }

            if (Declarations.Contains(Path.GetFileName(fichier)))
            {
                continue;
            }

            string brut;
            try
            {
                brut = File.ReadAllText(fichier);
            }
            catch (IOException)
            {
                continue;
            }

            lus++;

            foreach (Match m in Symbole.Matches(brut))
            {
                exigees.Add(m.Groups[1].Value);
            }

            var sansCommentaires = SourceCsharp.SansCommentaires(brut);
            var guillemet = "\"";
            foreach (var code in parCode.Keys)
            {
                if (!sansCommentaires.Contains(guillemet + code + guillemet, StringComparison.Ordinal))
                {
                    continue;
                }

                exigees.Add(parCode[code]);
                if (!litteraux.TryGetValue(code, out var ou))
                {
                    ou = new SortedSet<string>(StringComparer.Ordinal);
                    litteraux[code] = ou;
                }

                ou.Add(relatif);
            }
        }

        var sansGarde = catalogue.Keys
            .Where(nom => !exigees.Contains(nom))
            .ToHashSet(StringComparer.Ordinal);

        var fautes = new List<string>();

        foreach (var nom in sansGarde.Where(n => !assumees.Contains(n))
                     .OrderBy(x => x, StringComparer.Ordinal))
        {
            fautes.Add(
                $"« {catalogue[nom]} » n'est exigée par aucune route ni aucun handler, et "
                + "n'est pas inscrite dans `MerchantPermissions.SansGardeAssumee`. C'est un "
                + "droit affiché au vendeur, cochable dans un rôle, et sans le moindre effet "
                + "— à brancher, ou à assumer en écrivant pourquoi.");
        }

        foreach (var nom in assumees.Where(n => !sansGarde.Contains(n))
                     .OrderBy(x => x, StringComparer.Ordinal))
        {
            var code = catalogue.TryGetValue(nom, out var trouve) ? trouve : nom;
            fautes.Add(
                $"« {code} » est inscrite dans `SansGardeAssumee` alors qu'elle garde bien "
                + "quelque chose. La liste ment : à retirer.");
        }

        foreach (var paire in litteraux)
        {
            fautes.Add(
                $"« {paire.Key} » est écrite en chaîne littérale dans "
                + $"{string.Join(", ", paire.Value)}. Le code est recopié à la main : une "
                + "faute de frappe compile, et la garde refuse alors TOUT LE MONDE sans que "
                + $"rien ne le signale. `MerchantCapabilities.{parCode[paire.Key]}` dit la "
                + "même chose et le compilateur la tient.");
        }

        var assumeesSansGarde = sansGarde
            .Where(assumees.Contains)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        var constats = new List<string>
        {
            $"{catalogue.Count} permission(s) au catalogue, "
            + $"{catalogue.Keys.Count(exigees.Contains)} gardée(s), "
            + $"{assumeesSansGarde.Count} sans garde assumée "
            + $"({lus} fichier(s) .cs lus)",
        };

        foreach (var nom in assumeesSansGarde)
        {
            constats.Add($"sans garde, et assumée comme telle : {catalogue[nom]}");
        }

        return new Verdict(fautes, constats, nonCouvert);
    }

    /// <summary>
    /// Les permissions inscrites entre les accolades de <c>SansGardeAssumee</c>.
    /// </summary>
    /// <remarks>
    /// L'ANCRE EST LA DÉCLARATION, PAS UNE MENTION — voir l'encadré de
    /// <see cref="DeclarationAssumees"/>, qui raconte ce que coûtait l'inverse.
    ///
    /// LA LECTURE RESTE TEXTUELLE ET S'ARRÊTE AU PREMIER <c>};</c>. Un ensemble
    /// refermé autrement serait tronqué, et les permissions tombées hors du bloc
    /// deviendraient des fautes. C'est un défaut BRUYANT, pas silencieux : on le
    /// préfère à une analyse qui devine.
    /// </remarks>
    /// <returns>
    /// Les permissions inscrites, ou <c>null</c> si la DÉCLARATION est
    /// introuvable — deux situations que l'appelant ne doit pas confondre.
    /// </returns>
    private static HashSet<string>? Assumees(string source)
    {
        var declaration = DeclarationAssumees.Match(source);
        if (!declaration.Success)
        {
            return null;
        }

        var ouverture = source.IndexOf('{', declaration.Index + declaration.Length);
        if (ouverture < 0)
        {
            return null;
        }

        var fermeture = source.IndexOf("};", ouverture, StringComparison.Ordinal);
        if (fermeture < ouverture)
        {
            return null;
        }

        var assumees = new HashSet<string>(StringComparer.Ordinal);

        foreach (Match m in Assumee.Matches(source[ouverture..fermeture]))
        {
            assumees.Add(m.Groups[1].Value);
        }

        return assumees;
    }

    private static List<string> NonCouvert()
        =>
        [
            "que la garde soit au BON endroit, ni qu'elle couvre toutes les routes "
            + "qu'elle devrait : une permission exigée par une seule route sur cinq "
            + "passe ce contrôle",
            "les fichiers sous un dossier `tests/` — un usage qui n'existerait que "
            + "dans un test ne compte pas comme une garde",
            "les trois fichiers qui déclarent ou attribuent les permissions : les y "
            + "voir n'est pas une garde",
            "le SYMBOLE est cherché sur le source AVEC ses commentaires : une "
            + "constante seulement citée dans un encadré compte comme exigée",
            "la forme de `SansGardeAssumee` : l'ancre est sa DÉCLARATION, mais le "
            + "bloc est ensuite lu jusqu'au premier `};`. Un ensemble refermé "
            + "autrement serait tronqué — les permissions perdues deviendraient des "
            + "fautes, bruyamment",
        ];
}
