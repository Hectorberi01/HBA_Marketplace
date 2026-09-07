using Google.Protobuf.WellKnownTypes;
using HBA.Promotion.Grpc.V1;
using HBA.Promotion.Grpc.V1;

using HBA.Promotions.Contracts;
using HBA.Shared.Hosting.Grpc;
using HBA.Shared.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;


// COPIE DEPUIS `HBA.Promotions.Contracts.Grpc` (lot D — dissolution des assemblages
// de contrats).

namespace HBA.Promotions.Api.Grpc.Mappers;

/// <summary>
/// Traduction entre les enregistrements de <c> HBA.Promotions.Contracts</c> et les
/// messages protobuf.
/// </summary>
internal static class PromotionGrpcMapping
{
    public static PromotionContext ToProto(this PromotionEvaluationContext context)
        => new()
        {
            Scope = context.Scope ?? string.Empty,
            Subtotal = context.Subtotal,
            DeliveryFee = context.DeliveryFee,
            Currency = context.Currency ?? string.Empty,
            UserId = context.UserId.ToString()
        };

    public static PromotionEvaluationContext ToContract(this PromotionContext? proto)
        => proto is null
            ? new PromotionEvaluationContext("GLOBAL", 0, 0, "XOF", Guid.Empty)
            : new PromotionEvaluationContext(
                proto.Scope,
                proto.Subtotal,
                proto.DeliveryFee,
                string.IsNullOrWhiteSpace(proto.Currency) ? "XOF" : proto.Currency,
                Guid.TryParse(proto.UserId, out var userId) ? userId : Guid.Empty);

    public static EvaluatePromotionResponse ToProto(this PromotionEvaluationResult result)
        => new()
        {
            Valid = result.Valid,

            // CHAÎNE VIDE ET NON `Guid.Empty.ToString()`.
            PromotionId = result.PromotionId?.ToString() ?? string.Empty,
            Discount = result.Discount,
            Currency = result.Currency ?? string.Empty,
            Message = result.Message ?? string.Empty,
            Reason = result.Reason ?? string.Empty,

            // LA DÉCOMPOSITION PAR FINANCEUR (D28).
            SellerFundedDiscount = result.SellerFundedDiscount,
            PlatformFundedDiscount = result.PlatformFundedDiscount,
            OwnerSellerId = result.OwnerSellerId?.ToString() ?? string.Empty
        };

    /// <summary>UN SERVEUR D'AVANT D28 REND `discount` SANS SES DEUX PARTS.</summary>
    public static PromotionEvaluationResult ToContract(this EvaluatePromotionResponse proto)
    {
        var vendeur = proto.SellerFundedDiscount;
        var plateforme = proto.PlatformFundedDiscount;

        if (proto.Valid && proto.Discount > 0 && vendeur == 0 && plateforme == 0)
        {
            plateforme = proto.Discount;
        }

        return new PromotionEvaluationResult(
            proto.Valid,
            Guid.TryParse(proto.PromotionId, out var promotionId) ? promotionId : null,
            proto.Discount,
            proto.Currency,
            proto.Message,
            string.IsNullOrWhiteSpace(proto.Reason) ? null : proto.Reason,
            vendeur,
            plateforme,
            Guid.TryParse(proto.OwnerSellerId, out var proprietaire) ? proprietaire : null);
    }

    public static ReserveCouponResponse ToProto(this CouponReservationResult? reservation, string? reason)
        => reservation is null
            ? new ReserveCouponResponse { Reserved = false, Reason = reason ?? string.Empty }
            : new ReserveCouponResponse
            {
                Reserved = true,
                ReservationId = reservation.ReservationId.ToString(),
                CouponId = reservation.CouponId.ToString(),
                PromotionId = reservation.PromotionId.ToString(),
                DiscountAmount = reservation.DiscountAmount,
                Currency = reservation.Currency,

                // `ToUniversalTime()` EST OBLIGATOIRE, PAS DÉFENSIF.
                ExpiresAt = Timestamp.FromDateTime(
                    DateTime.SpecifyKind(reservation.ExpiresAtUtc, DateTimeKind.Utc))
            };

    public static CouponReservationResult? ToContract(this ReserveCouponResponse proto)
        => !proto.Reserved
            ? null
            : new CouponReservationResult(
                Guid.TryParse(proto.ReservationId, out var reservationId) ? reservationId : Guid.Empty,
                Guid.TryParse(proto.CouponId, out var couponId) ? couponId : Guid.Empty,
                Guid.TryParse(proto.PromotionId, out var promotionId) ? promotionId : Guid.Empty,
                proto.DiscountAmount,
                proto.Currency,
                proto.ExpiresAt?.ToDateTime() ?? DateTime.UtcNow);
}
