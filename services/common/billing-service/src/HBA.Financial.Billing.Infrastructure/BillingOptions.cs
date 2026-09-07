namespace HBA.Financial.Billing.Infrastructure;

/// <summary>Options du module Billing (section de configuration « Billing »).</summary>
public sealed class BillingOptions
{
    /// <summary>Taux de commission plateforme par défaut, en fraction (0.10 = 10 %).</summary>
    public decimal DefaultCommissionRate { get; set; } = 0.10m;
}
