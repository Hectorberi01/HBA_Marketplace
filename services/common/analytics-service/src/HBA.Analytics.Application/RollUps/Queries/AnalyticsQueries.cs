using HBA.Analytics.Domain.RollUps;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;

namespace HBA.Analytics.Application.RollUps.Queries;

/// <summary>Les ventes d'un vendeur, jour par jour.</summary>
public sealed record GetSellerSalesSeriesQuery(
    Guid SellerId, DateOnly? From, DateOnly? To, string? Currency)
    : IQuery<SellerSalesSeriesDto>;

/// <summary>L'activité de la plateforme, jour par jour.</summary>
public sealed record GetPlatformActivitySeriesQuery(
    DateOnly? From, DateOnly? To, string? Currency)
    : IQuery<PlatformActivitySeriesDto>;

/// <summary>Les inscriptions, jour par jour.</summary>
public sealed record GetSignupSeriesQuery(DateOnly? From, DateOnly? To)
    : IQuery<SignupSeriesDto>;

/// <summary>Ce que ces trois requêtes partagent : une devise, et le refus d'inventer.</summary>
internal static class DeviseDemandee
{
    public const string ParDefaut = "XOF";

    public static string Normaliser(string? demandee)
        => string.IsNullOrWhiteSpace(demandee) ? ParDefaut : demandee.Trim().ToUpperInvariant();
}

internal sealed class GetSellerSalesSeriesQueryHandler
    : IQueryHandler<GetSellerSalesSeriesQuery, SellerSalesSeriesDto>
{
    private readonly IRegistreDesRollUps _registre;

    public GetSellerSalesSeriesQueryHandler(IRegistreDesRollUps registre) => _registre = registre;

    public async Task<Result<SellerSalesSeriesDto>> Handle(
        GetSellerSalesSeriesQuery query, CancellationToken cancellationToken)
    {
        var periode = PeriodeDemandee.Construire(query.From, query.To);
        if (periode.IsFailure)
        {
            return periode.Error;
        }

        var devise = DeviseDemandee.Normaliser(query.Currency);

        var lignes = await _registre.LireVentesAsync(
            query.SellerId, periode.Value.Du, periode.Value.Au, cancellationToken);

        // LE FILTRE DE DEVISE EST ICI, PAS DANS LA REQUETE SQL, et c'est un choix
        // de peu de conséquence : la lecture est déjà bornée par (vendeur,
        // période), donc au plus 366 x natures x devises lignes.
        var parJour = lignes
            .Where(ligne => string.Equals(ligne.Currency, devise, StringComparison.Ordinal))
            .GroupBy(ligne => ligne.Day)
            .ToDictionary(groupe => groupe.Key, groupe => groupe.ToArray());

        var points = new List<SellerSalesPointDto>(periode.Value.Jours);

        foreach (var jour in periode.Value.Journees())
        {
            if (!parJour.TryGetValue(jour, out var duJour))
            {
                points.Add(new SellerSalesPointDto(jour, 0, 0, 0m, 0, 0));
                continue;
            }

            points.Add(new SellerSalesPointDto(
                jour,
                duJour.Sum(ligne => ligne.OrdersCount),
                duJour.Sum(ligne => ligne.ItemsCount),
                duJour.Sum(ligne => ligne.Revenue),
                duJour.Where(ligne => ligne.Kind == NatureDeCommande.Marchandise).Sum(ligne => ligne.OrdersCount),
                duJour.Where(ligne => ligne.Kind == NatureDeCommande.Repas).Sum(ligne => ligne.OrdersCount)));
        }

        var commandes = points.Sum(point => point.OrdersCount);
        var articles = points.Sum(point => point.ItemsCount);
        var chiffre = points.Sum(point => point.Revenue);

        return new SellerSalesSeriesDto(
            query.SellerId,
            periode.Value.Du,
            periode.Value.Au,
            devise,
            points,
            commandes,
            articles,
            chiffre,
            commandes == 0 ? 0m : decimal.Round(chiffre / commandes, 2, MidpointRounding.AwayFromZero));
    }
}

internal sealed class GetPlatformActivitySeriesQueryHandler
    : IQueryHandler<GetPlatformActivitySeriesQuery, PlatformActivitySeriesDto>
{
    private readonly IRegistreDesRollUps _registre;

    public GetPlatformActivitySeriesQueryHandler(IRegistreDesRollUps registre) => _registre = registre;

    public async Task<Result<PlatformActivitySeriesDto>> Handle(
        GetPlatformActivitySeriesQuery query, CancellationToken cancellationToken)
    {
        var periode = PeriodeDemandee.Construire(query.From, query.To);
        if (periode.IsFailure)
        {
            return periode.Error;
        }

        var devise = DeviseDemandee.Normaliser(query.Currency);

        var lignes = await _registre.LireActiviteAsync(
            periode.Value.Du, periode.Value.Au, cancellationToken);

        var parJour = lignes
            .Where(ligne => string.Equals(ligne.Currency, devise, StringComparison.Ordinal))
            .GroupBy(ligne => ligne.Day)
            .ToDictionary(groupe => groupe.Key, groupe => groupe.ToArray());

        var points = new List<PlatformActivityPointDto>(periode.Value.Jours);

        foreach (var jour in periode.Value.Journees())
        {
            if (!parJour.TryGetValue(jour, out var duJour))
            {
                points.Add(new PlatformActivityPointDto(jour, 0, 0, 0m, 0, 0));
                continue;
            }

            points.Add(new PlatformActivityPointDto(
                jour,
                duJour.Sum(ligne => ligne.OrdersCount),
                duJour.Sum(ligne => ligne.ItemsCount),
                duJour.Sum(ligne => ligne.Gmv),
                duJour.Where(ligne => ligne.Kind == NatureDeCommande.Marchandise).Sum(ligne => ligne.OrdersCount),
                duJour.Where(ligne => ligne.Kind == NatureDeCommande.Repas).Sum(ligne => ligne.OrdersCount)));
        }

        return new PlatformActivitySeriesDto(
            periode.Value.Du,
            periode.Value.Au,
            devise,
            points,
            points.Sum(point => point.OrdersCount),
            points.Sum(point => point.ItemsCount),
            points.Sum(point => point.Gmv));
    }
}

internal sealed class GetSignupSeriesQueryHandler
    : IQueryHandler<GetSignupSeriesQuery, SignupSeriesDto>
{
    private readonly IRegistreDesRollUps _registre;

    public GetSignupSeriesQueryHandler(IRegistreDesRollUps registre) => _registre = registre;

    public async Task<Result<SignupSeriesDto>> Handle(
        GetSignupSeriesQuery query, CancellationToken cancellationToken)
    {
        var periode = PeriodeDemandee.Construire(query.From, query.To);
        if (periode.IsFailure)
        {
            return periode.Error;
        }

        var lignes = await _registre.LireInscriptionsAsync(
            periode.Value.Du, periode.Value.Au, cancellationToken);

        var parJour = lignes.GroupBy(ligne => ligne.Day)
            .ToDictionary(groupe => groupe.Key, groupe => groupe.ToArray());

        var points = new List<SignupPointDto>(periode.Value.Jours);

        foreach (var jour in periode.Value.Journees())
        {
            if (!parJour.TryGetValue(jour, out var duJour))
            {
                points.Add(new SignupPointDto(jour, 0, 0));
                continue;
            }

            points.Add(new SignupPointDto(
                jour,
                duJour.Where(ligne => ligne.Kind == NatureDInscription.Acheteur).Sum(ligne => ligne.Count),
                duJour.Where(ligne => ligne.Kind == NatureDInscription.Vendeur).Sum(ligne => ligne.Count)));
        }

        return new SignupSeriesDto(
            periode.Value.Du,
            periode.Value.Au,
            points,
            points.Sum(point => point.Buyers),
            points.Sum(point => point.Sellers));
    }
}
