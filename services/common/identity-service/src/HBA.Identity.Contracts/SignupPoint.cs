namespace HBA.Identity.Contracts;

/// <summary>
/// Un point de la courbe d'évolution des inscriptions : un jour (UTC) et le nombre
/// de comptes créés ce jour-là.
/// </summary>
public sealed record SignupPoint(DateTime Date, int Count);
