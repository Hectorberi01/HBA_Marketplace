using Google.Protobuf.WellKnownTypes;
using HBA.Promotion.Grpc.V1;

namespace HBA.Promotions.Contracts.Grpc;

/// <summary>
/// Traduction entre les enregistrements de <c>HBA.Promotions.Contracts</c> et les
/// messages protobuf.
///
/// PROTOBUF N'A PAS DE `null`, ET C'EST TOUTE LA DIFFICULTÉ DE CE FICHIER.
///
/// Une `string` absente vaut la chaîne vide, un `int64` absent vaut zéro. Traduire
/// naïvement rendrait `PromotionId = Guid.Empty` là où le contrat dit « aucune
/// campagne », et `Discount = 0` là où il dit « aucune remise » — deux valeurs que
/// l'appelant ne peut plus distinguer d'un vrai zéro. Chaque conversion ci-dessous
/// dit explicitement ce qu'elle fait du vide.
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
            //
            // « 00000000-0000-… » est un GUID parfaitement valide : l'appelant le
            // parserait sans erreur et croirait tenir une campagne. Le vide, lui,
            // échoue au parsing, ce qui est exactement le signal recherché.
            PromotionId = result.PromotionId?.ToString() ?? string.Empty,
            Discount = result.Discount,
            Currency = result.Currency ?? string.Empty,
            Message = result.Message ?? string.Empty,
            Reason = result.Reason ?? string.Empty,

            // LA DÉCOMPOSITION PAR FINANCEUR (D28). Même règle de vide que
            // `PromotionId` : chaîne vide et non `Guid.Empty.ToString()`, parce
            // que « 00000000-… » est un GUID valide que l'appelant parserait sans
            // erreur en croyant tenir un vendeur.
            SellerFundedDiscount = result.SellerFundedDiscount,
            PlatformFundedDiscount = result.PlatformFundedDiscount,
            OwnerSellerId = result.OwnerSellerId?.ToString() ?? string.Empty
        };

    /// <summary>
    /// UN SERVEUR D'AVANT D28 REND `discount` SANS SES DEUX PARTS.
    ///
    /// Protobuf ne distingue pas « champ absent » de « zéro ». Un total non nul
    /// avec deux parts à zéro décrit donc une remise que personne ne paie, ce qui
    /// n'existe pas : c'est un serveur antérieur. On la rattache alors à la
    /// PLATEFORME — le même défaut que la migration pose aux campagnes existantes,
    /// et pour la même raison : imputer au vendeur prélèverait sur des marchands
    /// que rien ne désigne, par le calcul des gains, sans laisser de trace.
    /// </summary>
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
                //
                // `Timestamp.FromDateTime` LÈVE si le `DateTime` n'est pas en
                // `Kind.Utc`. Une date relue d'EF revient en `Kind.Unspecified` :
                // sans cette conversion, la première réservation renvoyée par gRPC
                // ferait tomber l'appel avec une exception d'argument, et le
                // checkout échouerait sur une retenue pourtant accordée.
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
