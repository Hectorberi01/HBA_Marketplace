using HBA.Analytics.Domain.RollUps;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;

namespace HBA.Analytics.Application.RollUps.Queries;

/// <summary>Les annulations d'un vendeur, jour par jour.</summary>
public sealed record GetSellerCancellationSeriesQuery(
    Guid SellerId, DateOnly? From, DateOnly? To, string? Currency)
    : IQuery<SellerCancellationSeriesDto>;

/// <summary>Les paiements de la plateforme, jour par jour et par prestataire.</summary>
public sealed record GetPaymentSeriesQuery(DateOnly? From, DateOnly? To, string? Currency)
    : IQuery<PaymentSeriesDto>;

internal sealed class GetSellerCancellationSeriesQueryHandler
    : IQueryHandler<GetSellerCancellationSeriesQuery, SellerCancellationSeriesDto>
{
    private readonly IRegistreDesRollUps _registre;

    public GetSellerCancellationSeriesQueryHandler(IRegistreDesRollUps registre) => _registre = registre;

    public async Task<Result<SellerCancellationSeriesDto>> Handle(
        GetSellerCancellationSeriesQuery query, CancellationToken cancellationToken)
    {
        var periode = PeriodeDemandee.Construire(query.From, query.To);
        if (periode.IsFailure)
        {
            return periode.Error;
        }

        var devise = DeviseDemandee.Normaliser(query.Currency);

        var lignes = await _registre.LireAnnulationsAsync(
            query.SellerId, periode.Value.Du, periode.Value.Au, cancellationToken);

        var parJour = lignes
            .Where(ligne => string.Equals(ligne.Currency, devise, StringComparison.Ordinal))
            .ToDictionary(ligne => ligne.Day);

        var points = new List<SellerCancellationPointDto>(periode.Value.Jours);

        foreach (var jour in periode.Value.Journees())
        {
            points.Add(parJour.TryGetValue(jour, out var ligne)
                ? new SellerCancellationPointDto(jour, ligne.OrdersCount, ligne.Amount)
                : new SellerCancellationPointDto(jour, 0, 0m));
        }

        return new SellerCancellationSeriesDto(
            query.SellerId,
            periode.Value.Du,
            periode.Value.Au,
            devise,
            points,
            points.Sum(point => point.OrdersCount),
            points.Sum(point => point.Amount));
    }
}

/// <summary>Les paiements de la plateforme : la courbe, et le classement par prestataire.</summary>
internal sealed class GetPaymentSeriesQueryHandler
    : IQueryHandler<GetPaymentSeriesQuery, PaymentSeriesDto>
{
    private readonly IRegistreDesRollUps _registre;

    public GetPaymentSeriesQueryHandler(IRegistreDesRollUps registre) => _registre = registre;

    /// <summary>Le taux d'échec, ou <c>null</c> quand il n'y a eu aucune issue.</summary>
    private static decimal? Taux(int encaisses, int echoues)
    {
        var total = encaisses + echoues;
        return total == 0 ? null : decimal.Round((decimal)echoues / total, 4, MidpointRounding.AwayFromZero);
    }

    public async Task<Result<PaymentSeriesDto>> Handle(
        GetPaymentSeriesQuery query, CancellationToken cancellationToken)
    {
        var periode = PeriodeDemandee.Construire(query.From, query.To);
        if (periode.IsFailure)
        {
            return periode.Error;
        }

        var devise = DeviseDemandee.Normaliser(query.Currency);

        var lignes = (await _registre.LirePaiementsAsync(
                periode.Value.Du, periode.Value.Au, cancellationToken))
            .Where(ligne => string.Equals(ligne.Currency, devise, StringComparison.Ordinal))
            .ToArray();

        var parJour = lignes.GroupBy(ligne => ligne.Day)
            .ToDictionary(groupe => groupe.Key, groupe => groupe.ToArray());

        var points = new List<PaymentPointDto>(periode.Value.Jours);

        foreach (var jour in periode.Value.Journees())
        {
            if (!parJour.TryGetValue(jour, out var duJour))
            {
                points.Add(new PaymentPointDto(jour, 0, 0, 0m));
                continue;
            }

            points.Add(new PaymentPointDto(
                jour,
                duJour.Where(l => l.Outcome == IssueDePaiement.Encaisse).Sum(l => l.Count),
                duJour.Where(l => l.Outcome == IssueDePaiement.Echoue).Sum(l => l.Count),
                duJour.Where(l => l.Outcome == IssueDePaiement.Encaisse).Sum(l => l.Amount)));
        }

        var prestataires = lignes
            .GroupBy(ligne => ligne.Provider, StringComparer.Ordinal)
            .Select(groupe =>
            {
                var encaisses = groupe.Where(l => l.Outcome == IssueDePaiement.Encaisse).Sum(l => l.Count);
                var echoues = groupe.Where(l => l.Outcome == IssueDePaiement.Echoue).Sum(l => l.Count);

                return new PaymentProviderDto(
                    groupe.Key,
                    encaisses,
                    echoues,
                    groupe.Where(l => l.Outcome == IssueDePaiement.Encaisse).Sum(l => l.Amount),
                    Taux(encaisses, echoues));
            })
            // DU PLUS GROS VOLUME AU PLUS PETIT, et non par taux d'échec : un
            // prestataire à une seule tentative échouée afficherait 100 % et
            // trônerait en tête d'un classement qui ne décide de rien.
            .OrderByDescending(prestataire => prestataire.CapturedAmount)
            .ThenBy(prestataire => prestataire.Provider, StringComparer.Ordinal)
            .ToList();

        var totalEncaisse = points.Sum(point => point.Captured);
        var totalEchoue = points.Sum(point => point.Failed);

        return new PaymentSeriesDto(
            periode.Value.Du,
            periode.Value.Au,
            devise,
            points,
            prestataires,
            totalEncaisse,
            totalEchoue,
            Taux(totalEncaisse, totalEchoue));
    }
}
