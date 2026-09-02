using System.Text.RegularExpressions;

namespace HBA.Controls.Controles;

/// <summary>
/// La table d'autorisations gRPC est EXACTEMENT le graphe d'appel du dépôt.
/// </summary>
/// <remarks>
/// ═══════════════════════════════════════════════════════════════════════════
/// UNE TABLE D'AUTORISATIONS QUI NE SUIT PAS LE CODE REDEVIENT « TOUT LE MONDE
/// PEUT TOUT » — SANS QUE PERSONNE NE S'EN APERÇOIVE.
///
/// LES DEUX DÉRIVES SONT AUSSI GRAVES L'UNE QUE L'AUTRE, ET N'ONT PAS LE MÊME
/// SYMPTÔME.
///
///   • UN APPEL SANS AUTORISATION casse en production, au premier appel réel :
///     `PermissionDenied`. Bruyant, immédiat, réparable. Mais il casse un
///     parcours utilisateur, et personne n'aura relié la panne à un fichier de
///     sécurité.
///
///   • UNE AUTORISATION SANS APPEL ne casse RIEN, jamais. Elle reste, elle
///     s'accumule, et au bout de quelques lots la table autorise tout le monde à
///     tout — exactement l'état qu'elle avait été écrite pour quitter. C'est la
///     dérive silencieuse, donc la dangereuse.
///
/// D'où un contrôle qui refuse les deux : la table de `AutorisationsGrpc.cs`
/// doit être EXACTEMENT le graphe d'appel du dépôt, ni plus ni moins.
///
/// CE QU'IL VÉRIFIE
///
///   1. Pour chaque hôte (projet portant un `Program.cs`), l'ensemble des
///      méthodes gRPC atteignables par ses références de projet transitives,
///      comparé à l'entrée correspondante de la table.
///   2. Que tout `Internal__ServiceName` écrit dans un compose est une clé de la
///      table — un nom mal orthographié fermerait un service entier.
///
/// UN HÔTE EST UN PROJET QUI PORTE UN `Program.cs`, PAS UN NOM EN `.Api`. La
/// convention tient aujourd'hui, mais elle n'est écrite nulle part : un futur
/// hôte nommé autrement disparaîtrait silencieusement du contrôle, donc de la
/// table, donc de toute autorisation. La présence d'un point d'entrée, elle, est
/// une propriété du code.
///
/// CE QU'IL NE VÉRIFIE PAS, ET POURQUOI IL LE DIT
///
///   • LA GRANULARITÉ RESTE CELLE DU PAQUET DE CONTRATS. Une enveloppe
///     `*.Contracts.Grpc` est une seule classe qui appelle tous les RPC de son
///     service ; référencer le paquet donne donc droit à tous. Ce contrôle
///     reproduit fidèlement cette approximation — il ne la corrige pas. La
///     resserrer demande de découper les enveloppes par interface, ce qui est un
///     lot en soi.
///
///   • LES APPELS PAR RÉFLEXION OU PAR CANAL CONSTRUIT À LA MAIN. Seule la forme
///     `_client.X(` est reconnue, qui est celle de tout le dépôt.
///
///   • QUE L'HÔTE SOIT RÉELLEMENT DÉPLOYÉ. Un projet `*.Api` sans conteneur
///     figure quand même dans la table : l'y laisser ne coûte rien, l'en retirer
///     ferait échouer le jour où il est déployé.
///
/// CE QUE CE PORTAGE NE REPREND PAS : LA RÉGÉNÉRATION.
///
/// Le script d'origine sait réécrire le bloc engendré de `AutorisationsGrpc.cs`
/// avec `--ecrire`. Ce contrôle-ci ne fait que CONSTATER : un contrôle rend un
/// verdict, il ne modifie pas le dépôt qu'il contrôle. Tant que la régénération
/// n'a pas son propre verbe dans le lanceur, elle reste du côté de
/// `le contrôle `autorisations-grpc` --ecrire`, et le message de faute le
/// rappelle à qui le lit.
/// ═══════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed class AutorisationsGrpcControle : IControle
{
    /// <inheritdoc/>
    public string Nom => "autorisations-grpc";

    /// <inheritdoc/>
    public string Resume => "la table d'autorisations gRPC est exactement le graphe d'appel";

    // ═══════════════════════════════════════════════════════════════════════
    // `clients/` ET `tools/` SONT ÉCARTÉS, EN PLUS DES DOSSIERS IGNORÉS PAR
    // `Depot`.
    //
    // Le portail d'administration est un client lourd : il ne porte aucun hôte
    // gRPC, mais il porte un `.csproj` et un `Program`. L'inclure ajouterait un
    // « hôte » qui n'en est pas un, et donc une divergence permanente que
    // personne ne saurait corriger.
    //
    // `tools/` A ÉTÉ AJOUTÉ LE 2 SEPTEMBRE 2026, ET LE DÉFAUT ÉTAIT RÉEL : cet
    // outil de contrôles porte lui-même un `Program.cs`. Dès son arrivée dans
    // le dépôt, le contrôle a exigé une entrée dans la table d'autorisations
    // pour un projet qui n'appelle aucun service et ne se déploie nulle part —
    // « HBA.Controls : hôte absent de la table. Aucun de ses 0 appels ne
    // passerait. » La barrière est passée au rouge sur une divergence qui n'en
    // était pas une.
    // ═══════════════════════════════════════════════════════════════════════
    private static readonly string[] DossiersHorsGraphe = ["clients", "tools"];

    private static readonly Regex Paquet = new(
        @"^\s*package\s+([\w\.]+)\s*;", RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex Service = new(
        @"\A\s*service\s+(\w+)\s*\{", RegexOptions.Compiled);

    private static readonly Regex Rpc = new(
        @"\A\s*rpc\s+(\w+)\s*\(", RegexOptions.Compiled);

    // `IdentityApi.IdentityApiClient` — le service proto se lit dans le nom du
    // client engendré. On exige que les deux moitiés coïncident pour ne pas
    // confondre avec un type quelconque nommé `…Client`.
    private static readonly Regex DeclarationClient = new(
        @"\b(\w+)\.(\w+)Client\b", RegexOptions.Compiled);

    // `_client.NomAsync(` ET `_client.Nom(` : protoc engendre les deux formes.
    private static readonly Regex Appel = new(
        @"\b_client\.(\w+?)(?:Async)?\s*\(", RegexOptions.Compiled);

    private static readonly Regex NomDeService = new(
        @"^\s*Internal__ServiceName:\s*(\S+)\s*$",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex EntreeDeTable = new(
        @"\[""([\w\.]+)""\]\s*=\s*(FrozenSet<string>\.Empty|new\[\]\s*\{(.*?)\})",
        RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex MethodeCitee = new(
        @"""(/[^""]+)""", RegexOptions.Compiled);

    private static string CheminTable()
        => Depot.Chemin("shared", "common", "HBA.Shared.Hosting", "Grpc", "AutorisationsGrpc.cs");

    /// <summary>Vrai si le chemin passe par un dossier hors du graphe gRPC.</summary>
    private static bool SousClients(string absolu)
        => Depot.Relatif(absolu).Split('/').Any(DossiersHorsGraphe.Contains);

    /// <summary>service proto -> (paquet, {rpc})</summary>
    private static Dictionary<string, (string Paquet, HashSet<string> Rpc)> Protos()
    {
        var connus = new Dictionary<string, (string Paquet, HashSet<string> Rpc)>(
            StringComparer.Ordinal);

        // `Depot.Dossier` LÈVE si `shared/proto` manque : sans les protos, ce
        // contrôle ne verrait AUCUN appel et déclarerait la table parfaite.
        foreach (var chemin in Depot.Fichiers(Depot.Dossier("shared", "proto"), ".proto"))
        {
            var texte = File.ReadAllText(chemin);
            var trouve = Paquet.Match(texte);
            if (!trouve.Success)
            {
                continue;
            }

            var paquet = trouve.Groups[1].Value;
            string? courant = null;
            foreach (var ligne in texte.Split('\n'))
            {
                var debut = Service.Match(ligne);
                if (debut.Success)
                {
                    courant = debut.Groups[1].Value;
                    if (!connus.ContainsKey(courant))
                    {
                        connus[courant] = (paquet, []);
                    }
                }

                var methode = Rpc.Match(ligne);
                if (methode.Success && courant is not null)
                {
                    connus[courant].Rpc.Add(methode.Groups[1].Value);
                }
            }
        }

        return connus;
    }

    /// <summary>Le projet auquel appartient un fichier : le premier `.csproj` en remontant.</summary>
    private static string? ProjetDe(string fichier)
    {
        var racine = Depot.Racine;
        var dossier = Path.GetDirectoryName(fichier);
        while (dossier is not null && dossier.Length > racine.Length)
        {
            var csproj = Directory.EnumerateFiles(dossier, "*.csproj").FirstOrDefault();
            if (csproj is not null)
            {
                return Path.GetFileNameWithoutExtension(csproj);
            }

            dossier = Path.GetDirectoryName(dossier);
        }

        return null;
    }

    /// <summary>Les projets référencés transitivement, `depart` compris.</summary>
    private static HashSet<string> Transitives(string csproj, HashSet<string> vus)
    {
        IReadOnlyList<(string Brut, string Absolu)> references;
        try
        {
            references = Projets.References(csproj).ToList();
        }
        catch (IOException)
        {
            // Un csproj illisible ne doit pas interrompre le graphe entier : il
            // rend ses références invisibles, et c'est déjà dit par le contrôle
            // `references`.
            return vus;
        }

        foreach (var (_, absolu) in references)
        {
            var nom = Path.GetFileNameWithoutExtension(absolu);
            if (!vus.Add(nom))
            {
                continue;
            }

            Transitives(absolu, vus);
        }

        return vus;
    }

    /// <summary>Recalcule la table depuis le dépôt.</summary>
    private static SortedDictionary<string, SortedSet<string>> Graphe()
    {
        var connus = Protos();

        // projet -> {methodes appelees}
        var appels = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var chemin in Depot.Fichiers(Depot.Racine, ".cs"))
        {
            if (SousClients(chemin))
            {
                continue;
            }

            var texte = File.ReadAllText(chemin);
            var invoques = new HashSet<string>(StringComparer.Ordinal);
            foreach (Match m in Appel.Matches(texte))
            {
                invoques.Add(m.Groups[1].Value);
            }

            if (invoques.Count == 0)
            {
                continue;
            }

            var services = new HashSet<string>(StringComparer.Ordinal);
            foreach (Match m in DeclarationClient.Matches(texte))
            {
                var gauche = m.Groups[1].Value;
                if (gauche == m.Groups[2].Value && connus.ContainsKey(gauche))
                {
                    services.Add(gauche);
                }
            }

            var nomProjet = ProjetDe(chemin);
            if (nomProjet is null)
            {
                continue;
            }

            foreach (var service in services)
            {
                var (paquet, disponibles) = connus[service];
                foreach (var invoque in invoques)
                {
                    if (!disponibles.Contains(invoque))
                    {
                        continue;
                    }

                    if (!appels.TryGetValue(nomProjet, out var deja))
                    {
                        deja = new HashSet<string>(StringComparer.Ordinal);
                        appels[nomProjet] = deja;
                    }

                    deja.Add($"/{paquet}.{service}/{invoque}");
                }
            }
        }

        var projets = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var csproj in Projets.Tous())
        {
            if (!SousClients(csproj))
            {
                projets[Path.GetFileNameWithoutExtension(csproj)] = csproj;
            }
        }

        var table = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        foreach (var paire in projets)
        {
            var dossier = Path.GetDirectoryName(paire.Value)!;
            if (!File.Exists(Path.Combine(dossier, "Program.cs")))
            {
                continue;
            }

            var methodes = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var reference in Transitives(paire.Value, [paire.Key]))
            {
                if (appels.TryGetValue(reference, out var jointes))
                {
                    methodes.UnionWith(jointes);
                }
            }

            table[paire.Key] = methodes;
        }

        return table;
    }

    /// <summary>Relit la table telle qu'elle est engendrée dans le fichier C#.</summary>
    private static SortedDictionary<string, SortedSet<string>> TableEcrite(string texte)
    {
        var table = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        foreach (Match bloc in EntreeDeTable.Matches(texte))
        {
            var methodes = new SortedSet<string>(StringComparer.Ordinal);
            foreach (Match m in MethodeCitee.Matches(bloc.Groups[3].Value))
            {
                methodes.Add(m.Groups[1].Value);
            }

            table[bloc.Groups[1].Value] = methodes;
        }

        return table;
    }

    /// <summary>Les `Internal__ServiceName` posés dans les fichiers compose.</summary>
    private static SortedDictionary<string, List<string>> NomsDesCompose()
    {
        var poses = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var chemin in Depot.Fichiers(Depot.Racine, ".yml", ".yaml"))
        {
            if (SousClients(chemin))
            {
                continue;
            }

            foreach (Match m in NomDeService.Matches(File.ReadAllText(chemin)))
            {
                var valeur = m.Groups[1].Value;
                if (!poses.TryGetValue(valeur, out var ou))
                {
                    ou = [];
                    poses[valeur] = ou;
                }

                ou.Add(Depot.Relatif(chemin));
            }
        }

        return poses;
    }

    /// <inheritdoc/>
    public Verdict Executer()
    {
        var attendue = Graphe();
        var cheminTable = CheminTable();

        if (!File.Exists(cheminTable))
        {
            // SANS LA TABLE, IL N'Y A RIEN À COMPARER — et rendre « 0
            // divergence » ferait passer une absence totale d'autorisations pour
            // un dépôt cohérent.
            return new Verdict(
                [$"{Depot.Relatif(cheminTable)} est introuvable : la table d'autorisations "
                 + "gRPC n'existe pas, aucune comparaison n'a pu avoir lieu."],
                [$"{attendue.Count} hôte(s) trouvé(s) dans le dépôt."],
                ["tout : la table de référence manque"]);
        }

        var ecrite = TableEcrite(File.ReadAllText(cheminTable));
        var fautes = new List<string>();

        var appelants = new SortedSet<string>(StringComparer.Ordinal);
        appelants.UnionWith(attendue.Keys);
        appelants.UnionWith(ecrite.Keys);

        foreach (var appelant in appelants)
        {
            if (!ecrite.ContainsKey(appelant))
            {
                fautes.Add(
                    $"{appelant} : hôte absent de la table. Aucun de ses "
                    + $"{attendue[appelant].Count} appels ne passerait.");
                continue;
            }

            if (!attendue.ContainsKey(appelant))
            {
                fautes.Add(
                    $"{appelant} : dans la table, mais ce n'est plus un hôte. Entrée à retirer.");
                continue;
            }

            foreach (var methode in attendue[appelant].Where(m => !ecrite[appelant].Contains(m)))
            {
                fautes.Add($"{appelant} appelle {methode} sans y être autorisé.");
            }

            foreach (var methode in ecrite[appelant].Where(m => !attendue[appelant].Contains(m)))
            {
                fautes.Add($"{appelant} est autorisé à {methode} sans jamais l'appeler.");
            }
        }

        foreach (var pose in NomsDesCompose())
        {
            if (!attendue.ContainsKey(pose.Key))
            {
                fautes.Add(
                    $"Internal__ServiceName={pose.Key} ({string.Join(", ", pose.Value)}) "
                    + "ne correspond à aucun hôte connu.");
            }
        }

        if (fautes.Count > 0)
        {
            fautes.Add(
                "`dotnet run --project tools/HBA.Controls -- autorisations-grpc régénère la table. "
                + "Ce contrôle CONSTATE seulement : il ne réécrit pas le dépôt qu'il lit.");
        }

        return new Verdict(
            fautes,
            [$"{attendue.Count} appelant(s), "
             + $"{attendue.Values.Sum(m => m.Count)} autorisation(s) attendue(s)."],
            [
                "la granularité : elle reste celle du PAQUET de contrats. Référencer une "
                + "enveloppe `*.Contracts.Grpc` donne droit à TOUS les RPC de son service, "
                + "car l'enveloppe est une seule classe qui les appelle tous",
                "les appels par réflexion ou par canal construit à la main : seule la forme "
                + "`_client.X(` est reconnue",
                "que l'hôte soit réellement déployé : un projet portant un `Program.cs` sans "
                + "conteneur figure quand même dans la table",
                "la RÉGÉNÉRATION de la table, restée du côté de "
                + "`le contrôle `autorisations-grpc` --ecrire`",
                "les fichiers sous `clients/` et `tools/`, écartés du graphe : un client "
                + "lourd et l'outil de contrôles portent un "
                + "`Program.cs` sans être un hôte gRPC",
                "les YAML des dossiers cachés — `.github/workflows/` en particulier. "
                + "`Depot.Fichiers` écarte tout segment commençant par un point, là où le "
                + "script Python les lisait. Aucun n'y pose de `Internal__ServiceName` "
                + "aujourd'hui ; le jour où l'un le fera, une faute de frappe y passera",
            ]);
    }
}
