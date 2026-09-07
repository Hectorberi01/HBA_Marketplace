namespace HBA.Pricing.Contracts;

/// <summary>API in-process publique du module Pricing.</summary>
public interface IPricingModuleApi
{
    Task<PriceBreakdownDto> CalculatePriceAsync(PriceRequest request, CancellationToken cancellationToken = default);

    /// <summary>Valide un code promo pour un acheteur, avant de l'attacher au panier.</summary>
    /// <param name="cartSubtotal">AJOUTÉ PAR LE LOT D28, ET CE N'EST PAS UN CONFORT.</param>
    Task<CouponValidation> ValidateCouponAsync(
        string code, Guid buyerId, decimal cartSubtotal = 0m, CancellationToken cancellationToken = default);
}
