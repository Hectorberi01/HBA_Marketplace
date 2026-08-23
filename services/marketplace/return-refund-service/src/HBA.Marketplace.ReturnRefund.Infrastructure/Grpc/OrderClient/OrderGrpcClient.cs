using Grpc.Core;
using HBA.Marketplace.ReturnRefund.Application.Abstractions;
using HBA.Shared.Domain.Results;
using OrderContracts = HBA.Orders.Contracts;
using ReturnContext = HBA.Marketplace.ReturnRefund.Application.Abstractions.OrderReturnContext;
using ReturnLineContext = HBA.Marketplace.ReturnRefund.Application.Abstractions.OrderReturnLineContext;

namespace HBA.Marketplace.ReturnRefund.Infrastructure.Grpc.OrderClient;

internal sealed class OrderGrpcClient : IOrderGrpcClient
{
    private readonly OrderContracts.IOrderingModuleApi _orders;

    public OrderGrpcClient(OrderContracts.IOrderingModuleApi orders) => _orders = orders;

    public async Task<Result<ReturnContext>> GetOrderReturnContextAsync(Guid orderId, CancellationToken cancellationToken)
    {
        try
        {
            var context = await _orders.GetOrderReturnContextAsync(orderId, cancellationToken);
            return context is null
                ? Error.NotFound("return_refund.order_not_returnable", "Commande introuvable ou non eligible au retour.")
                : ToApplication(context);
        }
        // ═════════════════════════════════════════════════════════════════════
        // `catch (Exception)` TRADUISAIT TOUT EN « LE SERVICE EST INDISPONIBLE ».
        //
        // Un `UNIMPLEMENTED` — RPC déclaré mais sans corps de serveur, il y en a
        // quarante dans le dépôt —, un `InvalidArgument` sur un GUID malformé, une
        // `NullReferenceException` de mapping DANS CE FICHIER : les trois
        // devenaient le même message. On cherchait une panne réseau devant un bug
        // de code, et le vrai défaut ne laissait aucune trace.
        //
        // ET LE `catch (OperationCanceledException)` ÉTAIT QUASI MORT.
        //
        // Un dépassement d'échéance gRPC lève `RpcException(DeadlineExceeded)`,
        // pas `OperationCanceledException`. Il est conservé — un jeton annulé en
        // amont peut encore passer par là — mais il ne rattrapait PAS le cas qu'il
        // prétendait couvrir, et son code d'erreur le disait pourtant.
        //
        // CE QUI N'EST PLUS RATTRAPÉ REMONTE, ET C'EST LE BUT.
        //
        // `Internal`, `Unimplemented`, `InvalidArgument`, `Unknown` et toute
        // exception non-gRPC traversent désormais. Elles produiront un 500 et une
        // trace — c'est-à-dire quelque chose à lire. Un `Result` d'échec bien
        // formé sur un bug de mapping ne se distingue de rien.
        // ═════════════════════════════════════════════════════════════════════
        catch (RpcException exception) when (exception.StatusCode is StatusCode.DeadlineExceeded)
        {
            return Error.DependencyUnavailable(
                "return_refund.order_grpc_timeout",
                "Le service Order n'a pas repondu dans le delai imparti.");
        }
        catch (RpcException exception) when (exception.StatusCode
            is StatusCode.Unavailable
            or StatusCode.Unauthenticated
            or StatusCode.FailedPrecondition)
        {
            return Error.DependencyUnavailable(
                $"return_refund.order_grpc_{exception.StatusCode.ToString().ToLowerInvariant()}",
                "Le service Order est indisponible.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Error.DependencyUnavailable(
                "return_refund.order_grpc_cancelled",
                "L'appel au service Order a ete interrompu.");
        }
    }

    private static ReturnContext ToApplication(OrderContracts.OrderReturnContext context)
        => new(
            context.OrderId,
            context.CustomerId,
            context.SellerId,
            context.StoreId,
            context.SellerOrderId,
            context.DeliveredAtUtc,
            context.PaymentId,
            context.Currency,
            context.CapturedAmount,
            context.AlreadyRefundedAmount,
            context.Lines.Select(ToApplication).ToList());

    private static ReturnLineContext ToApplication(OrderContracts.OrderReturnLineContext line)
        => new(
            line.OrderItemId,
            line.ProductId,
            line.VariantId,
            line.CategoryId,
            line.Sku,
            line.Name,
            line.OrderedQuantity,
            line.DeliveredQuantity,
            line.AlreadyReturnedQuantity,
            line.UnitPaidAmount);
}
