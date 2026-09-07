using Contracts = HBA.Merchants.Contracts;
using Grpc.Core;
using HBA.Shared.Hosting.Grpc;
using HBA.Shared.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Proto = HBA.Merchants.Grpc.V1;

using System.Globalization;
using System.Runtime.CompilerServices;

using HBA.Merchants.Contracts;
using ContratsMerchants = HBA.Merchants.Contracts;  // alias non masquable : voir tools/migration-grpc/lot_d_resolution.py
// DEPLACE DEPUIS `HBA.Merchants.Contracts.Grpc` (lot B de la migration gRPC).

namespace HBA.Merchants.Api.Grpc.Services;

internal sealed class MerchantsGrpcService : Proto.MerchantApi.MerchantApiBase
{
    private readonly ContratsMerchants.ISellerModuleApi _sellers;
    private readonly ContratsMerchants.IMerchantAccessApi _access;

    public MerchantsGrpcService(
        ContratsMerchants.ISellerModuleApi sellers, ContratsMerchants.IMerchantAccessApi access)
    {
        _sellers = sellers;
        _access = access;
    }

    public override async Task<Proto.GetSellerResponse> GetSeller(Proto.GetSellerRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.SellerId, out var sellerId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "seller_id n'est pas un GUID."));
        }

        var seller = await _sellers.GetSellerAsync(sellerId, context.CancellationToken);
        return seller is null
            ? new Proto.GetSellerResponse { Found = false }
            : new Proto.GetSellerResponse { Found = true, Seller = ToProto(seller) };
    }

    public override async Task<Proto.GetSellerResponse> GetSellerByUser(
        Proto.GetSellerByUserRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.UserId, out var userId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "user_id n'est pas un GUID."));
        }

        var seller = await _sellers.GetSellerByUserIdAsync(userId, context.CancellationToken);
        return seller is null
            ? new Proto.GetSellerResponse { Found = false }
            : new Proto.GetSellerResponse { Found = true, Seller = ToProto(seller) };
    }

    public override async Task<Proto.GetStoreResponse> GetStore(Proto.GetStoreRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.StoreId, out var storeId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "store_id n'est pas un GUID."));
        }

        var store = await _sellers.GetStoreAsync(storeId, context.CancellationToken);
        return store is null
            ? new Proto.GetStoreResponse { Found = false }
            : new Proto.GetStoreResponse { Found = true, Store = ToProto(store) };
    }

    public override async Task<Proto.ListSellerStoresResponse> ListSellerStores(
        Proto.ListSellerStoresRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.SellerId, out var sellerId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "seller_id n'est pas un GUID."));
        }

        var stores = await _sellers.ListStoresBySellerAsync(sellerId, context.CancellationToken);
        var response = new Proto.ListSellerStoresResponse();
        response.Stores.AddRange(stores.Select(ToProto));
        return response;
    }

    public override async Task<Proto.ValidateSellerResponse> ValidateSeller(
        Proto.ValidateSellerRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.SellerId, out var sellerId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "seller_id n'est pas un GUID."));
        }

        var active = await _sellers.IsActiveSellerAsync(sellerId, context.CancellationToken);
        return new Proto.ValidateSellerResponse { Valid = active, Status = active ? "Active" : "Inactive" };
    }

    /// <summary>LE COMPTE DE REVERSEMENT. UN SEUL APPELANT LÉGITIME : wallet-service.</summary>
    public override async Task<Proto.GetSellerPayoutResponse> GetSellerPayout(
        Proto.GetSellerPayoutRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.SellerId, out var sellerId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "seller_id n'est pas un GUID."));
        }

        var payout = await _sellers.GetSellerPayoutAsync(sellerId, context.CancellationToken);

        var response = new Proto.GetSellerPayoutResponse
        {
            Found = payout.SellerExists,
            Configured = payout.Account is not null
        };

        if (payout.Account is { } compte)
        {
            response.Payout = new Proto.PayoutAccount
            {
                Provider = compte.Provider,
                AccountNumber = compte.AccountNumber,
                AccountName = compte.AccountName
            };
        }

        return response;
    }

    /// <summary>LE CONTEXTE D'AUTORISATION D'UN COMPTE.</summary>
    public override async Task<Proto.GetMemberAccessResponse> GetMemberAccess(
        Proto.GetMemberAccessRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.UserId, out var userId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "user_id n'est pas un GUID."));
        }

        var acces = await _access.GetAccessAsync(userId, context.CancellationToken);

        if (acces is null)
        {
            return new Proto.GetMemberAccessResponse { Found = false };
        }

        var response = new Proto.GetMemberAccessResponse
        {
            Found = true,
            SellerId = acces.SellerId.ToString(),
            MemberId = acces.MemberId.ToString(),
            IsOwner = acces.IsOwner
        };

        response.Permissions.AddRange(acces.Permissions);
        response.StoreIds.AddRange(acces.StoreIds.Select(id => id.ToString()));
        response.SellerLevelPermissions.AddRange(acces.SellerLevelPermissions);

        // ORDONNÉ PAR IDENTIFIANT DE BOUTIQUE, comme les permissions le sont par
        // code : la réponse est mise en cache sérialisée, et un ordre instable
        // produirait une entrée différente à chaque calcul — invisible, mais
        // suffisant pour rendre incomparables deux traces d'un même incident.
        foreach (var (storeId, permissions) in acces.PermissionsByStore.OrderBy(e => e.Key))
        {
            var bloc = new Proto.StorePermissions { StoreId = storeId.ToString() };
            bloc.Permissions.AddRange(permissions);
            response.StorePermissions.Add(bloc);
        }

        return response;
    }

    /// <summary>
    /// Vérification explicite, quand le vendeur vient de la RESSOURCE et non du
    /// jeton.
    /// </summary>
    public override async Task<Proto.CheckMerchantCapabilityResponse> CheckMerchantCapability(
        Proto.CheckMerchantCapabilityRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.UserId, out var userId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "user_id n'est pas un GUID."));
        }

        if (!Guid.TryParse(request.SellerId, out var sellerId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "seller_id n'est pas un GUID."));
        }

        if (string.IsNullOrWhiteSpace(request.Permission))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "permission est obligatoire."));
        }

        var acces = await _access.GetAccessAsync(userId, context.CancellationToken);
        var memeVendeur = acces is not null && acces.SellerId == sellerId;

        // `store_id` EST ENFIN LU (lot F), ET UN CHAMP VIDE N'EST PAS UNE ERREUR.
        Guid? boutique = null;

        if (!string.IsNullOrWhiteSpace(request.StoreId))
        {
            if (!Guid.TryParse(request.StoreId, out var lue))
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument, "store_id n'est pas un GUID."));
            }

            boutique = lue;
        }

        return new Proto.CheckMerchantCapabilityResponse
        {
            Allowed = memeVendeur && acces!.CanInStore(boutique, request.Permission),

            // RENDU MÊME EN CAS DE REFUS, TANT QUE L'APPARTENANCE EXISTE.
            MemberId = memeVendeur ? acces!.MemberId.ToString() : string.Empty
        };
    }

    private static Proto.SellerSummary ToProto(ContratsMerchants.SellerSummary seller)
    {
        var message = new Proto.SellerSummary
        {
            SellerId = seller.Id.ToString(),
            UserId = seller.UserId.ToString(),
            ShopName = seller.ShopName,
            Status = seller.Status,
            KybStatus = seller.KybStatus,
            CommissionRate = seller.CommissionRate.ToString(CultureInfo.InvariantCulture)
        };

        if (seller.LogoUrl is not null)
        {
            message.LogoUrl = seller.LogoUrl;
        }

        if (seller.Description is not null)
        {
            message.Description = seller.Description;
        }

        return message;
    }

    private static Proto.StoreSummary ToProto(ContratsMerchants.StoreSummary store)
    {
        var message = new Proto.StoreSummary
        {
            StoreId = store.Id.ToString(),
            SellerId = store.SellerId.ToString(),
            Name = store.Name,

            // LES DEUX, ET PAS SEULEMENT LE STATUT. Le client déduisait `IsSelling`
            // de cette chaîne et se trompait de vocabulaire — voir l'encadré du
            // champ `is_selling` dans le proto.
            Status = store.Status,
            IsSelling = store.IsSelling
        };

        if (!string.IsNullOrWhiteSpace(store.ContactPhone))
        {
            message.ContactPhone = store.ContactPhone;
        }

        if (store.ContactEmail is not null)
        {
            message.ContactEmail = store.ContactEmail;
        }

        if (store.FulfillmentLocationId is { } locationId)
        {
            message.FulfillmentLocationId = locationId.ToString();
        }

        return message;
    }
}
