using System.Runtime.CompilerServices;
using System.Globalization;
using Grpc.Core;
using HBA.Shared.Hosting;
using HBA.Shared.Hosting.Grpc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Contracts = HBA.Merchants.Contracts;
using Proto = HBA.Merchants.Grpc.V1;

namespace HBA.Merchants.Contracts.Grpc;



public sealed class MerchantsGrpcClient : Contracts.ISellerModuleApi, Contracts.IMerchantAccessApi
{
    private readonly Proto.MerchantApi.MerchantApiClient _client;

    public MerchantsGrpcClient(Proto.MerchantApi.MerchantApiClient client) => _client = client;

    public async Task<Contracts.SellerSummary?> GetSellerAsync(
        Guid sellerId, CancellationToken cancellationToken = default)
    {
        var response = await _client.GetSellerAsync(
            new Proto.GetSellerRequest { SellerId = sellerId.ToString() },
            cancellationToken: cancellationToken);

        return response.Found ? ToContract(response.Seller) : null;
    }

    public async Task<Contracts.SellerSummary?> GetSellerByUserIdAsync(
        Guid userId, CancellationToken cancellationToken = default)
    {
        var response = await _client.GetSellerByUserAsync(
            new Proto.GetSellerByUserRequest { UserId = userId.ToString() },
            cancellationToken: cancellationToken);

        return response.Found ? ToContract(response.Seller) : null;
    }

    public async Task<bool> IsActiveSellerAsync(Guid sellerId, CancellationToken cancellationToken = default)
    {
        var response = await _client.ValidateSellerAsync(
            new Proto.ValidateSellerRequest { SellerId = sellerId.ToString() },
            cancellationToken: cancellationToken);

        return response.Valid;
    }

    public async Task<Contracts.StoreSummary?> GetStoreAsync(
        Guid storeId, CancellationToken cancellationToken = default)
    {
        var response = await _client.GetStoreAsync(
            new Proto.GetStoreRequest { StoreId = storeId.ToString() },
            cancellationToken: cancellationToken);

        return response.Found ? ToContract(response.Store) : null;
    }

    public async Task<IReadOnlyList<Contracts.StoreSummary>> ListStoresBySellerAsync(
        Guid sellerId, CancellationToken cancellationToken = default)
    {
        var response = await _client.ListSellerStoresAsync(
            new Proto.ListSellerStoresRequest { SellerId = sellerId.ToString() },
            cancellationToken: cancellationToken);

        return response.Stores.Select(ToContract).ToList();
    }

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LE COMPTE DE REVERSEMENT — LA SEULE LECTURE QUI DISE LA VÉRITÉ À DISTANCE.
    ///
    /// VOIR `ToContract` PLUS BAS AVANT DE LIRE `SellerSummary.Payout`.
    ///
    /// Ce champ-là vaut `null` pour tout le monde ici, faute d'être transporté.
    /// wallet-service l'a lu, et plus aucun vendeur de la plateforme ne pouvait
    /// sortir son argent.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    public async Task<Contracts.SellerPayout> GetSellerPayoutAsync(
        Guid sellerId, CancellationToken cancellationToken = default)
    {
        var response = await _client.GetSellerPayoutAsync(
            new Proto.GetSellerPayoutRequest { SellerId = sellerId.ToString() },
            cancellationToken: cancellationToken);

        if (!response.Found)
        {
            return Contracts.SellerPayout.Unknown;
        }

        if (!response.Configured || response.Payout is null)
        {
            return Contracts.SellerPayout.NotConfigured;
        }

        return Contracts.SellerPayout.Of(new Contracts.PayoutAccountSummary(
            response.Payout.Provider,
            response.Payout.AccountNumber,
            response.Payout.AccountName));
    }

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// HUIT CHAMPS AU PROTO, HUIT CHAMPS AU CONTRAT. PLUS RIEN N'EST INVENTÉ ICI.
    ///
    /// CE MAPPEUR EN FABRIQUAIT SIX, ET CE N'ÉTAIT PAS BÉNIN.
    ///
    /// Le record C# portait quatorze champs ; le proto en transporte huit. Les six
    /// autres — `Rating`, `SalesCount`, `Payout`, `KybDocuments`, `Metadata`,
    /// `KybRejectionReason` — recevaient ici une valeur neutre qu'aucun appelant ne
    /// pouvait distinguer d'une vraie. Une interface, deux sémantiques : dans
    /// seller-service, `ISellerModuleApi` rendait le vendeur ; ici, un objet EN
    /// FORME de vendeur, dont l'argent et les pièces d'identité avaient été
    /// remplacés par du plausible.
    ///
    /// `Payout: null` A RENDU IMPOSSIBLE TOUT RETRAIT VENDEUR DE LA PLATEFORME.
    ///
    /// wallet-service, hébergé par payment-service, résout `ISellerModuleApi` sur
    /// CE client. Il lisait `seller?.Payout`, obtenait `null` quel que soit le
    /// vendeur, et refusait chaque demande — pendant que la validation
    /// administrative d'une demande existante échouait AVEC remboursement, sur le
    /// même motif faux. Le vendeur lisait « Aucun compte de versement Mobile Money
    /// configuré » avec son numéro MTN sous les yeux (D21).
    ///
    /// La sortie n'était pas de mieux commenter ce mappeur : c'était de SÉPARER LES
    /// CONTRATS (D24). `SellerSummary` ne porte plus que ce qui voyage ; la fiche
    /// riche vit dans `SellerDetail`, côté Application, et ne sort jamais du
    /// service. Il n'y a donc plus de champ à remplir faute de mieux — le
    /// compilateur interdit ce que ce commentaire se contentait d'avertir.
    ///
    /// CE QUI RESTE À SAVOIR : ajouter un champ à `SellerSummary` sans l'ajouter
    /// au proto rouvrirait le trou à l'identique. Les deux se modifient ensemble.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    private static Contracts.SellerSummary ToContract(Proto.SellerSummary seller)
        => new(
            Id: ParseGuid(seller.SellerId),
            UserId: ParseGuid(seller.UserId),
            ShopName: seller.ShopName,
            LogoUrl: seller.HasLogoUrl ? seller.LogoUrl : null,
            Description: seller.HasDescription ? seller.Description : null,
            Status: seller.Status,
            KybStatus: seller.KybStatus,
            CommissionRate: ParseDecimal(seller.CommissionRate));

    private static Contracts.StoreSummary ToContract(Proto.StoreSummary store)
        => new(
            Id: ParseGuid(store.StoreId),
            SellerId: ParseGuid(store.SellerId),
            Name: store.Name,
            LogoUrl: null,
            Description: null,
            ContactPhone: store.HasContactPhone ? store.ContactPhone : string.Empty,
            ContactEmail: store.HasContactEmail ? store.ContactEmail : null,
            Status: store.Status,
            // LU, PLUS CALCULÉ. Cette ligne comparait le statut à « Active »,
            // valeur absente de `StoreStatus` : toute boutique était fermée à
            // distance. Voir l'encadré de `is_selling` dans le proto.
            IsSelling: store.IsSelling,
            FulfillmentLocationId: store.HasFulfillmentLocationId ? ParseGuid(store.FulfillmentLocationId) : null,
            StatusReason: null,
            OpeningHours: [],
            CreatedOnUtc: DateTime.MinValue);

    /// <summary>
    /// AUCUN CACHE CÔTÉ CLIENT, ET C'EST UNE DÉCISION.
    ///
    /// Le cahier (§50) voulait un cache chez chaque appelant, invalidé par un
    /// événement Kafka. Cinq services, cinq copies, cinq occasions d'en oublier
    /// une — et dans un groupe de consommateurs, une SEULE réplique reçoit le
    /// message d'invalidation. La suspension d'un membre n'aurait mordu que là.
    ///
    /// Le cache vit donc chez seller-service, où il est évincé dans la même
    /// transaction que la mutation qui le périme. Ce client fait un aller-retour
    /// gRPC par requête ; c'est le prix d'une révocation qui prend effet
    /// immédiatement partout, et c'est le bon.
    /// </summary>
    public async Task<Contracts.MerchantAccess?> GetAccessAsync(
        Guid userId, CancellationToken cancellationToken = default)
    {
        var response = await _client.GetMemberAccessAsync(
            new Proto.GetMemberAccessRequest { UserId = userId.ToString() },
            cancellationToken: cancellationToken);

        if (!response.Found)
        {
            return null;
        }

        return new Contracts.MerchantAccess(
            ParseGuid(response.SellerId),
            ParseGuid(response.MemberId),
            userId,
            response.IsOwner,
            [.. response.Permissions],
            [.. response.StoreIds.Select(ParseGuid)],
            [.. response.SellerLevelPermissions],

            // `ToDictionary` SUR UNE LISTE QUI POURRAIT PORTER DEUX FOIS LA MÊME
            // BOUTIQUE LÈVERAIT. Le serveur ne le fait pas — il itère un
            // dictionnaire — mais un client ne doit pas dépendre de la discipline
            // d'un serveur qu'il ne compile pas avec lui. Le regroupement absorbe
            // le doublon en réunissant, ce qui est la seule fusion correcte ici.
            response.StorePermissions
                .GroupBy(b => ParseGuid(b.StoreId))
                .ToDictionary(
                    g => g.Key,
                    g => (IReadOnlyList<string>)[.. g.SelectMany(b => b.Permissions).Distinct()]));
    }

    public async Task<bool> HasCapabilityAsync(
        Guid userId,
        Guid sellerId,
        Guid? storeId,
        string permission,
        CancellationToken cancellationToken = default)
    {
        var request = new Proto.CheckMerchantCapabilityRequest
        {
            UserId = userId.ToString(),
            SellerId = sellerId.ToString(),
            Permission = permission
        };

        if (storeId is { } boutique)
        {
            request.StoreId = boutique.ToString();
        }

        var response = await _client.CheckMerchantCapabilityAsync(
            request, cancellationToken: cancellationToken);

        return response.Allowed;
    }

    private static Guid ParseGuid(string? value)
        => Guid.TryParse(value, out var id) ? id : Guid.Empty;

    /// <summary>
    /// Un montant venu du fil.
    /// </summary>
    /// <remarks>
    /// REFUSAIT DE RENDRE ZÉRO — voir <see cref="MontantSurLeFil"/>. Cette
    /// fonction s'écrivait « TryParse(…) ? valeur : 0m », comme six autres du
    /// dépôt : un champ non posé par l'émetteur — donc la chaîne VIDE, il n'y a
    /// pas de « non renseigné » pour un `string` protobuf 3 — se lisait « zéro
    /// franc ».
    ///
    /// `champ` EST REMPLI PAR LE COMPILATEUR, pas à la main. Il reçoit le TEXTE
    /// de l'expression passée — « order.AlreadyRefundedAmount » — donc un nom plus
    /// précis qu'aucun littéral recopié, et qui suit les renommages tout seul.
    /// </remarks>
    private static decimal ParseDecimal(
        string? value, [CallerArgumentExpression(nameof(value))] string champ = "")
        => MontantSurLeFil.Lire(value, champ);
}

public static class MerchantsGrpcRegistration
{
    public static IServiceCollection AddMerchantsGrpcClient(
        this IServiceCollection services, IConfiguration configuration)
    {
        var address = configuration["Services:Merchant"]
            ?? throw new InvalidOperationException("Services:Merchant est absent.");

        var grpcPort = configuration.GetSection(HostingOptions.SectionName)
            .Get<HostingOptions>()?.GrpcPort ?? new HostingOptions().GrpcPort;

        services
            .AddGrpcClient<Proto.MerchantApi.MerchantApiClient>(options =>
                options.Address = new UriBuilder(address) { Port = grpcPort }.Uri)
            .AjouterLesInterceptionsInternes();

        services.AddScoped<Contracts.ISellerModuleApi, MerchantsGrpcClient>();

        // DEUX INTERFACES, UNE SEULE INSTANCE — ET NON DEUX ENREGISTREMENTS
        // INDÉPENDANTS DE LA MÊME CLASSE.
        //
        // `AddScoped<IMerchantAccessApi, MerchantsGrpcClient>()` construirait un
        // SECOND client dans la même portée. Sans conséquence fonctionnelle
        // aujourd'hui, mais c'est exactement le genre de duplication qui devient
        // un défaut le jour où le client portera un état — un jeton, un compteur,
        // un disjoncteur.
        services.AddScoped<Contracts.IMerchantAccessApi>(sp =>
            (MerchantsGrpcClient)sp.GetRequiredService<Contracts.ISellerModuleApi>());

        return services;
    }
}
