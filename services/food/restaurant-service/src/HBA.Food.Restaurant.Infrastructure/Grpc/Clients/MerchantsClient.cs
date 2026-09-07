using Contracts = HBA.Merchants.Contracts;
using Grpc.Core;
using HBA.Merchants.Contracts;
using HBA.Shared.Hosting.Grpc;
using HBA.Shared.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Proto = HBA.Merchants.Grpc.V1;

using System.Globalization;
using System.Runtime.CompilerServices;

using ContratsMerchants = HBA.Merchants.Contracts;  // alias non masquable : voir tools/migration-grpc/lot_d_resolution.py
// COPIE DEPUIS `HBA.Merchants.Contracts.Grpc` (lot D — dissolution des assemblages
// de contrats).

namespace HBA.Food.Infrastructure.Grpc.Clients;

internal sealed class MerchantsGrpcClient : ContratsMerchants.ISellerModuleApi, ContratsMerchants.IMerchantAccessApi
{
    private readonly Proto.MerchantApi.MerchantApiClient _client;

    public MerchantsGrpcClient(Proto.MerchantApi.MerchantApiClient client) => _client = client;

    public async Task<ContratsMerchants.SellerSummary?> GetSellerAsync(
        Guid sellerId, CancellationToken cancellationToken = default)
    {
        var response = await _client.GetSellerAsync(
            new Proto.GetSellerRequest { SellerId = sellerId.ToString() },
            cancellationToken: cancellationToken);

        return response.Found ? ToContract(response.Seller) : null;
    }

    public async Task<ContratsMerchants.SellerSummary?> GetSellerByUserIdAsync(
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

    public async Task<ContratsMerchants.StoreSummary?> GetStoreAsync(
        Guid storeId, CancellationToken cancellationToken = default)
    {
        var response = await _client.GetStoreAsync(
            new Proto.GetStoreRequest { StoreId = storeId.ToString() },
            cancellationToken: cancellationToken);

        return response.Found ? ToContract(response.Store) : null;
    }

    public async Task<IReadOnlyList<ContratsMerchants.StoreSummary>> ListStoresBySellerAsync(
        Guid sellerId, CancellationToken cancellationToken = default)
    {
        var response = await _client.ListSellerStoresAsync(
            new Proto.ListSellerStoresRequest { SellerId = sellerId.ToString() },
            cancellationToken: cancellationToken);

        return response.Stores.Select(ToContract).ToList();
    }

    /// <summary>
    /// LE COMPTE DE REVERSEMENT — LA SEULE LECTURE QUI DISE LA VÉRITÉ À DISTANCE.
    /// </summary>
    public async Task<ContratsMerchants.SellerPayout> GetSellerPayoutAsync(
        Guid sellerId, CancellationToken cancellationToken = default)
    {
        var response = await _client.GetSellerPayoutAsync(
            new Proto.GetSellerPayoutRequest { SellerId = sellerId.ToString() },
            cancellationToken: cancellationToken);

        if (!response.Found)
        {
            return ContratsMerchants.SellerPayout.Unknown;
        }

        if (!response.Configured || response.Payout is null)
        {
            return ContratsMerchants.SellerPayout.NotConfigured;
        }

        return ContratsMerchants.SellerPayout.Of(new ContratsMerchants.PayoutAccountSummary(
            response.Payout.Provider,
            response.Payout.AccountNumber,
            response.Payout.AccountName));
    }

    /// <summary>
    /// HUIT CHAMPS AU PROTO, HUIT CHAMPS AU CONTRAT. PLUS RIEN N'EST INVENTÉ ICI.
    /// </summary>
    private static ContratsMerchants.SellerSummary ToContract(Proto.SellerSummary seller)
        => new(
            Id: ParseGuid(seller.SellerId),
            UserId: ParseGuid(seller.UserId),
            ShopName: seller.ShopName,
            LogoUrl: seller.HasLogoUrl ? seller.LogoUrl : null,
            Description: seller.HasDescription ? seller.Description : null,
            Status: seller.Status,
            KybStatus: seller.KybStatus,
            CommissionRate: ParseDecimal(seller.CommissionRate));

    private static ContratsMerchants.StoreSummary ToContract(Proto.StoreSummary store)
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
            // distance.
            IsSelling: store.IsSelling,
            FulfillmentLocationId: store.HasFulfillmentLocationId ? ParseGuid(store.FulfillmentLocationId) : null,
            StatusReason: null,
            OpeningHours: [],
            CreatedOnUtc: DateTime.MinValue);

    /// <summary>AUCUN CACHE CÔTÉ CLIENT, ET C'EST UNE DÉCISION.</summary>
    public async Task<ContratsMerchants.MerchantAccess?> GetAccessAsync(
        Guid userId, CancellationToken cancellationToken = default)
    {
        var response = await _client.GetMemberAccessAsync(
            new Proto.GetMemberAccessRequest { UserId = userId.ToString() },
            cancellationToken: cancellationToken);

        if (!response.Found)
        {
            return null;
        }

        return new ContratsMerchants.MerchantAccess(
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
            // d'un serveur qu'il ne compile pas avec lui.
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

    /// <summary>Un montant venu du fil.</summary>
    private static decimal ParseDecimal(
        string? value, [CallerArgumentExpression(nameof(value))] string champ = "")
        => MontantSurLeFil.Lire(value, champ);
}

internal static class MerchantsGrpcRegistration
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

        services.AddScoped<ContratsMerchants.ISellerModuleApi, MerchantsGrpcClient>();

        // DEUX INTERFACES, UNE SEULE INSTANCE — ET NON DEUX ENREGISTREMENTS
        // INDÉPENDANTS DE LA MÊME CLASSE.
        services.AddScoped<ContratsMerchants.IMerchantAccessApi>(sp =>
            (MerchantsGrpcClient)sp.GetRequiredService<ContratsMerchants.ISellerModuleApi>());

        return services;
    }
}
