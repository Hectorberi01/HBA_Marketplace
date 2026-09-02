using System.Text.RegularExpressions;

namespace HBA.Controls.Controles;

/// <summary>
/// Clients gRPC : quelles méthodes ne contactent jamais le serveur ?
/// </summary>
/// <remarks>
/// ═══════════════════════════════════════════════════════════════════════════
/// UN BOUCHON COMPILE, NE LÈVE PAS, ET MENT.
///
///     public Task&lt;OfferSummary?&gt; GetOfferAsync(Guid offerId, CancellationToken ct = default)
///         =&gt; Task.FromResult&lt;OfferSummary?&gt;(null);
///
/// Cette méthode satisfait l'interface. Le conteneur la résout. L'appelant reçoit
/// « cette offre n'existe pas » — et conclut que l'offre n'existe pas, alors
/// qu'elle n'a jamais été demandée à personne.
///
/// C'est pire qu'une `NotImplementedException` : celle-là se voit au premier
/// appel.
///
/// CE QUE CE CONTRÔLE A TROUVÉ LA PREMIÈRE FOIS.
///
/// Sept méthodes bouchonnées dans deux clients. L'une d'elles, `GetOfferAsync`,
/// est appelée par le gestionnaire d'ajout au panier — c'est-à-dire qu'AUCUN
/// article ne pouvait entrer dans un panier. Le premier geste du parcours client,
/// avant même le checkout et le paiement.
///
/// Un audit précédent avait conclu « la couche synchrone est saine » parce que
/// chaque client déclaré avait un serveur en face. Il vérifiait l'existence du
/// serveur, pas le fait que le client lui parle.
///
/// ET ENSUITE, CE CONTRÔLE A MENTI À SON TOUR, PENDANT TOUTE SA VIE.
///
/// Il balayait `&lt;dépôt&gt;/src` — le chemin du monolithe. Ce dossier n'existe
/// pas ici : le code vit sous `services/`, `shared/` et `apps/`. Un parcours de
/// dossier absent ne lève pas, il n'itère pas. Le contrôle imprimait donc
/// « 0 client concerné, 0 méthode bouchonnée » à chaque exécution depuis le
/// premier jour, et ce zéro se lisait comme « tout va bien ».
///
/// Ce qu'il taisait : la remise en stock des retours rendait un résultat de
/// succès sans rien appeler — la marchandise retournée n'entrait JAMAIS en stock
/// — et la création d'une course de retour fabriquait une chaîne
/// `RET-DELIVERY-{guid}` : aucun enlèvement n'était jamais créé, et le client
/// recevait un numéro qui ne correspondait à rien.
///
/// Les racines de balayage viennent désormais de
/// <see cref="SourceCsharp.Fichiers"/>, qui passe par <see cref="Depot.Dossier"/>
/// et LÈVE quand un dossier déclaré manque. Un contrôle qui ne trouve pas ses
/// fichiers s'interrompt au lieu de rendre un zéro rassurant.
///
/// LE PIRE BOUCHON N'A PAS DE MÉTHODE BOUCHONNÉE : IL N'A PAS DE CLIENT DU TOUT.
///
/// Le critère historique — « le corps ne mentionne pas le champ client » —
/// suppose qu'un champ client existe. Les deux classes ci-dessus n'en avaient
/// AUCUN : pas de champ, pas de constructeur, rien que des expressions-corps.
/// C'est le bouchon le plus complet, donc le plus dangereux, et c'était
/// précisément celui qui passait entre les mailles.
///
/// Une classe `*GrpcClient` sans un seul collaborateur injecté est donc signalée
/// EN TANT QUE TELLE, avant même l'examen de ses méthodes, et TOUTES ses méthodes
/// sont listées : sans interlocuteur, aucune ne peut contacter quoi que ce soit.
///
/// ON NE CHERCHE PLUS LE SEUL NOM `_client`. Un client peut appeler `_orders.` ou
/// `_payments.` : figer le nom rendait le critère faux dès qu'un client était
/// nommé d'après son interlocuteur, et un vrai appel passait alors pour un
/// bouchon. Les collaborateurs sont donc TOUS les champs privés et TOUS les
/// paramètres du constructeur primaire (C# 12) — ces derniers ne sont pas des
/// champs déclarés mais se référencent exactement pareil.
///
/// POURQUOI CE CONTRÔLE NE FAIT PAS ÉCHOUER LA BARRIÈRE.
///
/// Un bouchon peut être délibéré tant que personne ne l'appelle depuis un autre
/// service, et ce contrôle ne sait pas qui appelle quoi. Le faire échouer d'office
/// rendrait la barrière rouge en permanence, ce qui est la meilleure façon de
/// faire ignorer les autres contrôles. C'est la LISTE qui compte, et elle est
/// rendue en CONSTATS. Le garde-fou qui, lui, refuse de démarrer, se pose dans
/// l'installeur du module — voir `ReturnRefundModuleInstaller` et
/// `PaymentsModuleInstaller`.
///
/// Ce qui N'EST PAS informatif, en revanche : une racine de code introuvable. Elle
/// LÈVE, et le lanceur compte l'interruption comme une faute. C'est exactement le
/// silence qui a laissé passer les deux bouchons de return-refund, et il ne doit
/// plus jamais ressembler à un succès.
///
/// CE QU'IL REGARDE, ET SA LIMITE.
///
/// Une méthode publique d'une classe `*GrpcClient` dont le corps ne mentionne
/// aucun collaborateur de la classe. Il ne juge pas la justesse de l'appel,
/// seulement sa présence. Une méthode qui délègue à une autre méthode du même
/// client est signalée à tort — c'est un faux positif assumé, préférable au
/// silence. Il ne dit RIEN de ce qui appelle le bouchon : c'est une liste à
/// trier, et le tri demande de savoir qui dépend de la méthode.
/// ═══════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed class GrpcStubsControle : IControle
{
    /// <inheritdoc/>
    public string Nom => "grpc-stubs";

    /// <inheritdoc/>
    public string Resume => "aucun client gRPC ne rend une réponse sans avoir appelé personne";

    // Corps qui trahissent un bouchon plutôt qu'un appel réseau.
    private static readonly string[] Bouchons =
        ["Task.FromResult", "NotImplementedException", "Array.Empty", "return null;"];

    private static readonly Regex ClasseCliente = new(
        @"class\s+(\w*GrpcClient)\b", RegexOptions.Compiled);

    // L'indentation à quatre espaces est le repère de « méthode de premier
    // niveau ». Grossier, et suffisant : elle sépare les méthodes du client des
    // fonctions locales et des lambdas de leurs corps.
    private static readonly Regex Methode = new(
        "\n    public (?:async )?(?:override )?Task<?[^\n(]*?>?\\s+(\\w+)\\s*\\(",
        RegexOptions.Compiled);

    // Ce dont un client a besoin pour parler à quelqu'un : un champ injecté…
    private static readonly Regex Champ = new(
        @"private\s+(?:readonly\s+)?[^;=(){}\n]+?\s+(\w+)\s*[;=]", RegexOptions.Compiled);

    // …ou un paramètre de constructeur primaire.
    private static readonly Regex ConstructeurPrimaire = new(
        @"class\s+\w*GrpcClient\s*\(([^)]*)\)", RegexOptions.Compiled);

    private static readonly Regex Mot = new(@"\w+", RegexOptions.Compiled);

    /// <inheritdoc/>
    public Verdict Executer()
    {
        var details = new List<string>();
        var clients = 0;
        var integraux = 0;
        var methodes = 0;

        var fichiers = SourceCsharp.Fichiers()
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        foreach (var fichier in fichiers)
        {
            var (classeNue, bouchonnees) = Analyser(fichier);
            if (classeNue is null && bouchonnees.Count == 0)
            {
                continue;
            }

            clients++;
            var relatif = Depot.Relatif(fichier);

            if (classeNue is not null)
            {
                integraux++;
                details.Add($"{relatif} : {classeNue} — BOUCHON INTÉGRAL : aucun champ "
                            + "client, aucun interlocuteur possible.");
            }

            foreach (var methode in bouchonnees)
            {
                methodes++;
                details.Add($"{relatif} : {methode} — ne contacte jamais le serveur");
            }
        }

        var constats = new List<string>
        {
            $"{clients} client(s) concerné(s), {integraux} bouchon(s) intégral(aux), "
            + $"{methodes} méthode(s) bouchonnée(s) — {fichiers.Count} fichier(s) .cs lus",
        };
        constats.AddRange(details);

        // FAUTES VIDES, DÉLIBÉRÉMENT : voir l'encadré de la classe. Ce contrôle
        // rend une liste, pas un verdict d'échec.
        return new Verdict([], constats, NonCouvert());
    }

    /// <summary>
    /// Rend le nom de la classe quand elle n'a AUCUN collaborateur — le bouchon
    /// intégral — et la liste des méthodes bouchonnées.
    /// </summary>
    private static (string? ClasseNue, IReadOnlyList<string> Methodes) Analyser(string chemin)
    {
        string source;
        try
        {
            source = File.ReadAllText(chemin);
        }
        catch (IOException)
        {
            return (null, []);
        }

        var premiere = ClasseCliente.Match(source);
        if (!premiere.Success)
        {
            return (null, []);
        }

        // On ne regarde QUE la portion à partir de la première classe cliente :
        // le serveur, dans le même fichier, n'a évidemment pas de champ client.
        var portion = source[premiere.Index..];
        var noms = Collaborateurs(portion);
        var trouvees = Methode.Matches(portion);

        if (noms.Count == 0)
        {
            return (premiere.Groups[1].Value,
                trouvees.Select(m => m.Groups[1].Value).ToList());
        }

        var bouchonnees = new List<string>();
        foreach (Match m in trouvees)
        {
            var corps = CorpsDe(portion, m.Index);
            if (noms.Any(nom => corps.Contains(nom + ".", StringComparison.Ordinal)))
            {
                continue;
            }

            if (Bouchons.Any(b => corps.Contains(b, StringComparison.Ordinal)))
            {
                bouchonnees.Add(m.Groups[1].Value);
            }
        }

        return (null, bouchonnees);
    }

    /// <summary>
    /// Texte jusqu'à la prochaine méthode publique — approximation suffisante.
    /// </summary>
    private static string CorpsDe(string portion, int depart)
    {
        var suivant = portion.IndexOf("\n    public ", depart + 1, StringComparison.Ordinal);
        return suivant > 0 ? portion[depart..suivant] : portion[depart..];
    }

    /// <summary>
    /// Noms référençables depuis les méthodes : champs privés et paramètres de
    /// constructeur primaire.
    /// </summary>
    private static HashSet<string> Collaborateurs(string portion)
    {
        var noms = Champ.Matches(portion)
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

        var primaire = ConstructeurPrimaire.Match(portion);
        if (primaire.Success && primaire.Groups[1].Value.Trim().Length > 0)
        {
            foreach (var morceau in primaire.Groups[1].Value.Split(','))
            {
                var mots = Mot.Matches(morceau).Select(m => m.Value).ToList();
                if (mots.Count > 0)
                {
                    noms.Add(mots[^1]);
                }
            }
        }

        return noms;
    }

    private static List<string> NonCouvert()
        =>
        [
            "ce contrôle NE FAIT PAS ÉCHOUER la barrière : un bouchon peut être "
            + "délibéré, et il ne sait pas qui l'appelle. La liste est en constats, "
            + "le refus de démarrer se pose dans les installeurs de modules",
            "la JUSTESSE de l'appel : seule sa présence est vue. Une méthode qui "
            + "appelle le mauvais serveur passe",
            "une méthode qui délègue à une autre méthode du même client est "
            + "signalée à tort — faux positif assumé, préférable au silence",
            "les méthodes qui ne sont pas indentées de quatre espaces, ni écrites "
            + "`public [async] [override] Task…` : la lecture est textuelle, elle "
            + "ne comprend pas le C#",
            "les clients dont la classe ne s'appelle pas `*GrpcClient`, et tout "
            + "bouchon hors gRPC",
        ];
}
