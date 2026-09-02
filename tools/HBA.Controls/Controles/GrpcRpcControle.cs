using System.Text.RegularExpressions;

namespace HBA.Controls.Controles;

/// <summary>
/// Tout RPC appelé par un client a-t-il un corps de serveur ?
/// </summary>
/// <remarks>
/// ═══════════════════════════════════════════════════════════════════════════
/// UN RPC APPELÉ SANS CORPS DE SERVEUR REND `UNIMPLEMENTED` — ET RIEN NE LE DIT.
///
/// DEUX FOIS EN UNE JOURNÉE, DONT UNE QUI TUAIT TOUT LE PARCOURS REPAS.
///
///   • `DeliveryApi.LookupQuote` : appelé par les deux checkouts, aucun corps.
///     Le devis étant obligatoire pour un repas, AUCUNE COMMANDE DE REPAS NE
///     POUVAIT ÊTRE PASSÉE.
///   • `OrderApi.ListOrdersBySeller` : appelé par `SellerSalesCountHandler` à
///     CHAQUE commande confirmée, aucun corps. L'exception partait avant que
///     l'inbox ne soit marquée — donc rejeu du message jusqu'à épuisement — et
///     `SalesCount` restait à zéro pour tous les vendeurs, c'est-à-dire le
///     défaut même que ce handler avait été écrit pour fermer.
///
/// Les deux compilent. Les deux passent tous les autres contrôles du dépôt. Le
/// `.proto` déclare le RPC, `protoc` génère la méthode côté client ET une base
/// côté serveur dont les membres non surchargés lèvent `UNIMPLEMENTED` À
/// L'EXÉCUTION. Il n'y a aucun moment, entre l'éditeur et la production, où
/// quelque chose s'en aperçoit.
///
/// CE QU'IL VÉRIFIE
///
/// Pour chaque RPC déclaré dans un `.proto` compilé (`shared/proto/`) :
///
///   FAUTE  appelé par un client ET sans corps de serveur → panne à l'exécution
///   constat  sans corps de serveur et sans appelant      → surface latente
///   constat  avec corps de serveur et sans appelant      → RPC mort
///
/// Seule la première catégorie fait échouer : c'est la seule qui casse quelque
/// chose aujourd'hui. Les deux autres sont un inventaire — le lot 9.1 les
/// traite, et les compter ici évite de les recompter à la main.
///
/// LES COMMENTAIRES SONT RETIRÉS AVANT DE CHERCHER LES CORPS DE SERVEUR. Un
/// encadré qui cite « public override Task&lt;X&gt; Machin( » — il y en a dans ce
/// dépôt, ils expliquent justement ce qui manque — compterait sinon pour une
/// implémentation, et le contrôle se tairait sur le défaut qu'il vise. D'où
/// <see cref="SourceCsharp.SansCommentaires"/>, partagé avec les autres
/// contrôles pour que la même lecture naïve ne se trompe pas deux fois
/// différemment.
///
/// CE QU'IL NE VÉRIFIE PAS, ET POURQUOI IL LE DIT
///
///   • L'APPELANT APPLICATIF. Une enveloppe de `*.Contracts.Grpc` qui appelle un
///     RPC compte comme un appelant, même si PERSONNE n'appelle l'enveloppe. Le
///     RPC est alors « appelé » au sens de ce contrôle et « mort » en pratique.
///     Remonter jusqu'à l'appelant métier demanderait de suivre les interfaces à
///     travers l'injection de dépendances — c'est-à-dire d'écrire un
///     compilateur. Conséquence assumée : on peut signaler en faute un RPC que
///     personne n'appelle vraiment. C'est le bon sens de l'erreur : il RESTE à
///     brancher ou à retirer.
///
///   • LES FLUX. Seuls les RPC unaires sont reconnus, ce qui est tout le dépôt.
///
///   • LES `.proto` NON COMPILÉS. Le balayage prend tout `shared/proto/` sans
///     lire les `&lt;Protobuf Include=…&gt;` : un `.proto` posé là mais compilé par
///     aucun csproj gonflerait l'inventaire sans rien apprendre. Aucun ne s'y
///     trouve aujourd'hui — `return_refund.proto` n'est pas dans ce dossier.
///
///   • LE NOM DU CHAMP CLIENT. Un appel n'est reconnu que sous la forme
///     `_client.Machin(` ou `_client.MachinAsync(` : un client rangé dans un
///     champ nommé autrement est invisible à ce contrôle, et ses RPC
///     paraîtraient sans appelant.
/// ═══════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed class GrpcRpcControle : IControle
{
    /// <inheritdoc/>
    public string Nom => "grpc-rpc";

    /// <inheritdoc/>
    public string Resume => "aucun RPC appelé par un client sans corps de serveur";

    private static readonly Regex Service = new(
        @"^\s*service\s+(\w+)\s*\{", RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex Rpc = new(
        @"^\s*rpc\s+(\w+)\s*\(", RegexOptions.Compiled | RegexOptions.Multiline);

    /// <summary>
    /// `public sealed class X : Truc.MachinApiBase` — on retient « MachinApi ».
    /// </summary>
    private static readonly Regex Base = new(
        @"class\s+\w+\s*:\s*[\w\.]*?(\w+)\.(\w+)Base\b", RegexOptions.Compiled);

    private static readonly Regex Surcharge = new(
        @"public\s+override\s+(?:async\s+)?Task\s*<[^>]*>\s+(\w+)\s*\(",
        RegexOptions.Compiled);

    /// <summary>
    /// `_client.NomAsync(` ET `_client.Nom(` : protoc génère les deux formes.
    /// </summary>
    private static readonly Regex Appel = new(
        @"\b_client\.(\w+?)(?:Async)?\s*\(", RegexOptions.Compiled);

    /// <inheritdoc/>
    public Verdict Executer()
    {
        // ── 1. Ce que les contrats déclarent ────────────────────────────────
        var declares = new Dictionary<(string Service, string Rpc), string>();
        foreach (var chemin in Depot.Fichiers(Depot.Dossier("shared", "proto"), ".proto"))
        {
            var source = File.ReadAllText(chemin);
            foreach (Match service in Service.Matches(source))
            {
                var apres = service.Index + service.Length;

                // Le corps du service s'arrête à l'accolade en début de ligne.
                // Un `.proto` sans accolade fermante ne fait pas avaler le
                // fichier suivant : on s'arrête à sa fin.
                var accolade = source.IndexOf("\n}", apres, StringComparison.Ordinal);
                var corps = source[apres..(accolade >= 0 ? accolade : source.Length)];

                foreach (Match rpc in Rpc.Matches(corps))
                {
                    declares[(service.Groups[1].Value, rpc.Groups[1].Value)] =
                        Depot.Relatif(chemin);
                }
            }
        }

        // ── 2 et 3. Ce que les serveurs implémentent, ce que les clients
        // appellent. Une seule lecture du disque pour les deux : le fichier est
        // lu une fois, débarrassé de ses commentaires une fois.
        var servis = new HashSet<(string, string)>();
        var appeles = new HashSet<string>(StringComparer.Ordinal);
        var lus = 0;

        foreach (var chemin in Depot.Fichiers(Depot.Racine, ".cs"))
        {
            var brut = File.ReadAllText(chemin);
            var source = SourceCsharp.SansCommentaires(brut);
            lus++;

            foreach (Match appel in Appel.Matches(source))
            {
                appeles.Add(appel.Groups[1].Value);
            }

            // Filtre bon marché : sans le mot `Base`, aucune classe de service
            // gRPC générée n'est héritée ici.
            if (!brut.Contains("Base", StringComparison.Ordinal))
            {
                continue;
            }

            var services = Base.Matches(source)
                .Select(m => m.Groups[2].Value)
                .ToHashSet(StringComparer.Ordinal);

            if (services.Count == 0)
            {
                continue;
            }

            foreach (Match surcharge in Surcharge.Matches(source))
            {
                foreach (var service in services)
                {
                    servis.Add((service, surcharge.Groups[1].Value));
                }
            }
        }

        var fautes = new List<string>();
        var latents = 0;
        var morts = 0;

        var ordonnes = declares
            .OrderBy(d => d.Key.Service, StringComparer.Ordinal)
            .ThenBy(d => d.Key.Rpc, StringComparer.Ordinal);

        foreach (var ((service, rpc), proto) in ordonnes)
        {
            var aUnCorps = servis.Contains((service, rpc));
            var estAppele = appeles.Contains(rpc);

            if (estAppele && !aUnCorps)
            {
                fautes.Add(
                    $"{service}.{rpc} — déclaré dans {proto}, appelé par un client, "
                    + "AUCUN corps de serveur. À l'exécution : "
                    + "RpcException(Unimplemented), non rattrapée. Le brancher, ou "
                    + "retirer l'appel — pas laisser en l'état.");
            }
            else if (!aUnCorps)
            {
                latents++;
            }
            else if (!estAppele)
            {
                morts++;
            }
        }

        return new Verdict(
            fautes,
            [
                $"{declares.Count} RPC déclarés dans les contrats compilés, "
                + $"{lus} fichier(s) C# lus.",
                $"{latents} sans corps de serveur et sans appelant — surface latente.",
                $"{morts} implémentés et jamais appelés — RPC morts (lot 9.1).",
            ],
            [
                "l'appelant APPLICATIF : une enveloppe de `*.Contracts.Grpc` compte "
                + "comme appelant même si personne ne l'appelle",
                "les flux — seuls les RPC unaires sont reconnus",
                "les `.proto` que ne compile aucun `<Protobuf Include=…>` : tout "
                + "`shared/proto/` est pris, la liste de compilation n'est pas lue",
                "les appels passant par un champ nommé autrement que `_client`",
            ]);
    }
}
