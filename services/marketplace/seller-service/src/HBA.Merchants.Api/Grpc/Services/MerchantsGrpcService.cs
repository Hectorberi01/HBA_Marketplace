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
// ═════════════════════════════════════════════════════════════════════════════
// DEPLACE DEPUIS `HBA.Merchants.Contracts.Grpc` (lot B de la migration gRPC).
//
// LE SERVEUR VIVAIT DANS L'ASSEMBLAGE DE CONTRATS, DONC CHEZ TOUS SES
// CONSOMMATEURS. Les dix services qui consomment merchant.proto liaient
// l'implementation de seller-service ; les huit qui consomment order.proto
// liaient celle d'order-service. Aucun ne s'en servait.
//
// Le serveur est la surface d'UN service : il vit desormais dans son `.Api`.
// L'assemblage de contrats ne porte plus que le stub genere, le client et son
// enregistrement — le lot C descendra ces deux-la chez les appelants.
//
// CE QUE ÇA NE CHANGE PAS : le cablage. `Program.cs` appelle toujours
// `MapInternalGrpcService<...>()`, avec la meme autorisation et les memes
// intercepteurs. Un deplacement de fichier ne rend rien plus sur.
// ═════════════════════════════════════════════════════════════════════════════

namespace HBA.Merchants.Api.Grpc.Services;

internal sealed class MerchantsGrpcService : Proto.MerchantApi.MerchantApiBase
{
    private readonly Contracts.ISellerModuleApi _sellers;
    private readonly Contracts.IMerchantAccessApi _access;

    public MerchantsGrpcService(
        Contracts.ISellerModuleApi sellers, Contracts.IMerchantAccessApi access)
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

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LE COMPTE DE REVERSEMENT. UN SEUL APPELANT LÉGITIME : wallet-service.
    ///
    /// CE RPC REND UN NUMÉRO MOBILE MONEY. Il n'est atteignable que sur le port
    /// gRPC INTERNE — `MapInternalGrpcService`, clé d'appel interne exigée par
    /// l'intercepteur — et jamais par la passerelle. C'est la seule raison pour
    /// laquelle une coordonnée de paiement peut voyager ici, et c'est aussi
    /// pourquoi elle ne voyage PAS dans `GetSeller`, dont la réponse est mise en
    /// cache et servie en boucle par la fiche produit mobile.
    ///
    /// « VENDEUR INCONNU » ET « VENDEUR SANS COMPTE » SONT DEUX RÉPONSES.
    ///
    /// Les confondre est précisément le défaut qu'on répare : l'appelant doit
    /// pouvoir dire au vendeur « configurez votre compte » plutôt que « aucun
    /// compte configuré » à quelqu'un qui en a un, ou « vendeur introuvable » à
    /// quelqu'un qui existe.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
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

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LE CONTEXTE D'AUTORISATION D'UN COMPTE.
    ///
    /// CE RPC EST APPELÉ SUR CHAQUE REQUÊTE VENDEUR DE LA PLATEFORME.
    ///
    /// C'est le chemin le plus chaud du service, et il est servi par un cache
    /// évincé transactionnellement (voir `MerchantAccessApi`). Y ajouter une
    /// lecture non mise en cache reviendrait à poser une requête SQL sur chaque
    /// appel autorisé des cinq services appelants.
    ///
    /// `found = false` N'EST PAS UN REFUS.
    ///
    /// C'est « ce compte n'appartient à aucune équipe vendeur » — le cas de
    /// l'immense majorité des comptes, qui sont des acheteurs. C'est l'appelant
    /// qui décide s'il en tire un 403 ou un 404, selon ce que sa route peut
    /// révéler sans permettre d'énumérer les vendeurs.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
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
    /// Vérification explicite, quand le vendeur vient de la RESSOURCE et non du jeton.
    /// </summary>
    /// <remarks>
    /// LE `seller_id` REÇU EST VÉRIFIÉ, JAMAIS ACCEPTÉ — c'est la règle du §36.
    /// Il désigne le vendeur visé ; c'est l'appartenance résolue depuis
    /// l'identifiant d'UTILISATEUR qui décide.
    /// </remarks>
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
        //
        // proto3 ne distingue pas « absent » de « chaîne vide » : un appelant qui
        // ne situe pas sa ressource n'envoie rien, et c'est le cas nominal — un avis
        // ne porte pas de boutique. Une chaîne présente mais illisible, en revanche,
        // est une faute d'appelant : la laisser passer pour un `null` appliquerait
        // la garde LARGE sur une requête qui demandait le cadrage, c'est-à-dire
        // exactement l'inverse de l'intention.
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
            //
            // C'est ce qui permet à l'appelant d'auditer « le membre X a tenté Y
            // et s'est vu refuser » plutôt que « un compte a tenté Y ». Un refus
            // sans acteur est une trace qui ne sert à personne.
            MemberId = memeVendeur ? acces!.MemberId.ToString() : string.Empty
        };
    }

    private static Proto.SellerSummary ToProto(Contracts.SellerSummary seller)
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

    private static Proto.StoreSummary ToProto(Contracts.StoreSummary store)
    {
        var message = new Proto.StoreSummary
        {
            StoreId = store.Id.ToString(),
            SellerId = store.SellerId.ToString(),
            Name = store.Name,

            // LES DEUX, ET PAS SEULEMENT LE STATUT. Le client déduisait
            // `IsSelling` de cette chaîne et se trompait de vocabulaire — voir
            // l'encadré du champ `is_selling` dans le proto.
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
