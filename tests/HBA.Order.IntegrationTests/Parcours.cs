using System.Net.Http.Json;
using System.Text.Json;
using HBA.Tests.Authorization;

namespace HBA.Order.IntegrationTests;

/// <summary>
/// Une commande passée par le parcours réel, avec de quoi vérifier ce qu'elle
/// devient.
/// </summary>
/// <param name="LieuExpedition">
/// Le lieu d'expédition unique de toutes ses lignes : c'est le couple
/// (SKU, lieu, commande) qu'`InventaireDeTest` enregistre.
/// </param>
internal sealed record CommandePassee(
    Guid AcheteurId,
    Guid CommandeId,
    Guid LieuExpedition,
    IReadOnlyList<string> Skus);

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LES GESTES DU PARCOURS, PASSÉS PAR LA VRAIE SURFACE HTTP.
///
/// AUCUN RACCOURCI PAR LE `DbContext` OU PAR L'AGRÉGAT.
///
/// Il serait plus rapide d'insérer une commande en base et de lui injecter son
/// paiement. Ce serait aussi renoncer à la moitié de ce que ce niveau existe pour
/// éprouver : la liaison du corps de requête, le contrôle d'identité de
/// l'acheteur, la lecture du panier valorisé, la RÉSERVATION DE STOCK ligne par
/// ligne — sans laquelle il n'y aurait rien à libérer et ISSUE-003 n'aurait pas
/// d'objet — et le fait que la commande arrive bien en `AwaitingPayment`, l'état
/// d'où part tout le reste.
///
/// Une commande posée directement en base n'aurait jamais rien réservé, et le
/// test d'ISSUE-003 aurait compté zéro libération attendue contre zéro obtenue.
///
/// CE QUE CE PARCOURS NE PEUT PAS FAIRE PAR HTTP, ET IL FAUT LE SAVOIR.
///
/// Le panier vient de cart-service et le stock d'inventory-service : ni l'un ni
/// l'autre n'a de surface HTTP dans ce processus, et les faire tourner ferait de
/// chaque test un test de trois services. Le panier est donc DÉPOSÉ dans
/// `PanierDeTest` avant l'appel, et le lieu d'expédition dans
/// `InventaireDeTest` — c'est-à-dire à la place exacte où le vrai voisin
/// répondrait. Tout ce qui suit le `POST /api/orders` est réel.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
internal static class Parcours
{
    /// <summary>
    /// Passe une commande de marchandise et rend de quoi la suivre.
    /// </summary>
    /// <param name="skus">
    /// Une ligne par SKU. Deux par défaut : c'est le minimum pour que « une
    /// libération PAR LIGNE » se distingue de « au moins une libération ».
    /// </param>
    public static async Task<CommandePassee> PasserCommandeAsync(
        OrderIntegrationFixture fixture, params string[] skus)
    {
        if (skus.Length == 0)
        {
            skus = [$"SKU-A-{Guid.NewGuid():N}", $"SKU-B-{Guid.NewGuid():N}"];
        }

        var acheteurId = Guid.NewGuid();

        // AUCUN RÔLE : `/api/orders` passe par `MapAuthenticatedGroup`, et
        // c'est correct — la propriété de la commande se vérifie DANS le
        // gestionnaire, pas au niveau du groupe. Exiger un rôle ici ferait passer
        // ce test sur une politique que la production n'applique pas.
        var client = fixture.CreateClientWithToken(TestTokens.Create(acheteurId));

        var lieu = fixture.Inventaire.DeposerLieu();
        fixture.Panier.Deposer(acheteurId, lieu, skus);

        var reponse = await client.PostAsJsonAsync(
            "/api/orders",
            new
            {
                // COMMUNE ET POINT DE REPÈRE, PAS DE RUE.
                //
                // `PlaceOrderCommandHandler` en fait un INVARIANT de la commande :
                // sans eux, le checkout est refusé en `ordering.shipping_address_required`.
                // La rue reste facultative — au Bénin elle est souvent inexistante,
                // et l'exiger reviendrait à faire inventer une adresse.
                shippingAddress = new
                {
                    label = "Maison",
                    recipient = "Kossi Adjovi",
                    phone = "+22997000001",
                    communeCode = "cotonou",
                    quartier = "Fidjrosse",
                    landmark = "Après le carrefour de la SBEE",
                    line1 = (string?)null,
                    countryCode = "BJ",
                    latitude = (double?)null,
                    longitude = (double?)null
                },

                // AUCUN DEVIS DE COURSE, ET C'EST UN CHOIX EXPLIQUÉ DANS
                // `CourseDeTest`. Une commande de MARCHANDISE sans devis part à
                // zéro franc de frais, ce que le service journalise lui-même
                // comme un trou de recette connu. Un repas, lui, serait refusé.
                deliveryQuoteId = (string?)null
            });

        reponse.EnsureSuccessStatusCode();

        return new CommandePassee(acheteurId, await LireIdAsync(reponse), lieu, skus);
    }

    /// <summary>
    /// Prépare une commande et rend de quoi la déclencher, succès ou refus.
    /// </summary>
    /// <remarks>
    /// SÉPARÉE DE <see cref="PasserCommandeAsync"/> À DESSEIN.
    ///
    /// Celle-là appelle `EnsureSuccessStatusCode` : un refus y devient une
    /// exception, ce qui est exactement ce qu'il faut quand la commande n'est que
    /// le point de départ. Pour éprouver la revalidation du prix (lot 7.4), c'est
    /// le refus LUI-MÊME qui est le sujet — et son code d'erreur, pas seulement
    /// son statut HTTP : `ordering.offer_unavailable`,
    /// `ordering.offer_not_purchasable` et `ordering.price_changed` sont trois
    /// causes distinctes que l'acheteur doit pouvoir distinguer dans son panier.
    ///
    /// Le panier est déposé AVANT l'appel et ses identifiants d'offre rendus, pour
    /// que le test puisse désigner celle qu'il veut voir refuser.
    /// <para>
    /// ELLE N'EST PAS `async`, ET N'A PAS DE SUFFIXE `Async` : elle n'attend
    /// rien. Le dépôt du panier et la construction du client sont synchrones ;
    /// c'est la fonction RENDUE qui est asynchrone. Un `async` sans `await`
    /// compilerait — avec un CS1998 — et mentirait sur ce que la méthode fait.
    /// </para>
    /// </remarks>
    public static (Guid AcheteurId, IReadOnlyList<Guid> Offres, Func<Task<HttpResponseMessage>> Commander)
        PreparerCommande(OrderIntegrationFixture fixture, params string[] skus)
    {
        if (skus.Length == 0)
        {
            skus = [$"SKU-A-{Guid.NewGuid():N}"];
        }

        var acheteurId = Guid.NewGuid();
        var client = fixture.CreateClientWithToken(TestTokens.Create(acheteurId));

        var lieu = fixture.Inventaire.DeposerLieu();
        fixture.Panier.Deposer(acheteurId, lieu, skus);

        return (acheteurId, fixture.Panier.Offres(acheteurId), () => client.PostAsJsonAsync(
            "/api/orders",
            new
            {
                shippingAddress = new
                {
                    label = "Maison",
                    recipient = "Kossi Adjovi",
                    phone = "+22997000001",
                    communeCode = "cotonou",
                    quartier = "Fidjrosse",
                    landmark = "Après le carrefour de la SBEE",
                    line1 = (string?)null,
                    countryCode = "BJ",
                    latitude = (double?)null,
                    longitude = (double?)null
                },
                deliveryQuoteId = (string?)null
            }));
    }

    /// <summary>Le code métier porté par une réponse de refus.</summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LE CODE MÉTIER N'EST PAS DANS `error.code`. IL EST DANS LES DÉTAILS.
    ///
    /// `ApiResults.Problem` NORMALISE : `error.code` porte un code de FAMILLE
    /// (`…_CONFLICT`, `…_BUSINESS_RULE_VIOLATION`), et le code du domaine —
    /// `ordering.price_changed` — est rangé dans `error.details[]` sous le champ
    /// `reason`. Lire `error.code` ferait donc passer un test censé distinguer
    /// `offer_unavailable` de `price_changed` : les deux sont des conflits, les
    /// deux rendent le même `error.code`, et l'assertion serait verte sur la
    /// mauvaise cause.
    ///
    /// La première rédaction de cette méthode essayait cinq formes de corps au
    /// hasard, en espérant que l'une réponde. C'est la même faute que celles que
    /// ce chantier passe son temps à retirer : supposer la forme au lieu de la
    /// lire. Elle lit maintenant la seule qui existe, et LÈVE sur toute autre.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
    public static async Task<string> LireLeCodeMetierAsync(HttpResponseMessage reponse)
    {
        var corps = await reponse.Content.ReadFromJsonAsync<JsonElement>();

        if (corps.ValueKind != JsonValueKind.Object
            || !corps.TryGetProperty("error", out var erreur)
            || erreur.ValueKind != JsonValueKind.Object
            || !erreur.TryGetProperty("details", out var details)
            || details.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException(
                "Réponse de refus qui ne suit pas l'enveloppe du §5 (`error.details[]`) : "
                + $"{corps}");
        }

        foreach (var detail in details.EnumerateArray())
        {
            if (detail.ValueKind == JsonValueKind.Object
                && detail.TryGetProperty("field", out var champ)
                && champ.ValueKind == JsonValueKind.String
                && champ.GetString() == "reason"
                && detail.TryGetProperty("message", out var code)
                && code.ValueKind == JsonValueKind.String)
            {
                return code.GetString()!;
            }
        }

        // ON LÈVE PLUTÔT QUE DE RENDRE `null`. Un `null` remonterait jusqu'à
        // l'assertion et donnerait « attendu ordering.price_changed, obtenu null » —
        // vrai, et muet sur la cause. Ici le corps reçu est dans le message.
        throw new InvalidOperationException(
            $"Aucun détail `reason` dans la réponse de refus : {corps}");
    }

    /// <summary>
    /// CETTE RÉPONSE-CI N'EST PAS ENVELOPPÉE, CONTRAIREMENT À LA PLUPART.
    ///
    /// `OrderEndpoints.PlaceAsync` rend `Results.Created(…, new { id })` et non
    /// `ApiResults.Created(…)` : l'identifiant est donc à la RACINE, pas sous
    /// `data`. C'est un écart au §25 qui appartient à order-service, pas à ce
    /// test — on le lit tel qu'il est, et l'on accepte les DEUX formes pour que
    /// la mise en conformité du bord HTTP ne casse pas cette suite au passage.
    ///
    /// Lire aveuglément la racine serait le mode de panne trouvé dans
    /// `CatalogClient` : cela ne lève pas, cela rend un GUID vide, et le test
    /// échoue bien plus loin — sur une commande introuvable — sans dire pourquoi.
    /// D'où le refus explicite ci-dessous.
    /// </summary>
    private static async Task<Guid> LireIdAsync(HttpResponseMessage reponse)
    {
        var corps = await reponse.Content.ReadFromJsonAsync<JsonElement>();

        // `TryGetProperty` LÈVE sur autre chose qu'un objet JSON : on ne veut pas
        // d'une `InvalidOperationException` nue là où le corps reçu doit être dit.
        var porteur = corps.ValueKind == JsonValueKind.Object
                      && corps.TryGetProperty("data", out var data)
            ? data
            : corps;

        if (porteur.ValueKind != JsonValueKind.Object
            || !porteur.TryGetProperty("id", out var id)
            || id.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException(
                $"Réponse de POST /api/orders sans identifiant lisible : {corps}");
        }

        return id.GetGuid();
    }
}
