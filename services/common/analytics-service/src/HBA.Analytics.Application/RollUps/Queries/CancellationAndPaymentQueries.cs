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

/// <summary>
/// Les paiements de la plateforme : la courbe, et le classement par prestataire.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// LE SEAU « inconnu » N'EST PAS FILTRÉ, ET C'EST DÉLIBÉRÉ.
///
/// Le retirer rendrait un taux d'échec calculé sur un sous-ensemble présenté
/// comme un total. Il apparaît donc dans le classement, sous son nom, et il doit
/// décroître jusqu'à zéro après le déploiement du lot 2.
///
/// LA DEVISE INCONNUE, ELLE, TOMBE AVEC LE FILTRE DE DEVISE.
///
/// Les messages d'avant le lot 2 sont rangés sous `XXX` : ils sortent donc du
/// résultat dès qu'on demande une devise réelle, et le seau « inconnu » ne
/// contient plus alors que les messages qui portaient une devise SANS
/// prestataire. Les deux manques sont distincts et ne se recouvrent pas
/// forcément — c'est ce qui rend l'appel `?currency=XXX` utile pour compter ce
/// qui reste à rattraper.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
internal sealed class GetPaymentSeriesQueryHandler
    : IQueryHandler<GetPaymentSeriesQuery, PaymentSeriesDto>
{
    private readonly IRegistreDesRollUps _registre;

    public GetPaymentSeriesQueryHandler(IRegistreDesRollUps registre) => _registre = registre;

    /// <summary>
    /// Le taux d'échec, ou <c>null</c> quand il n'y a eu aucune issue.
    /// </summary>
    /// <remarks>
    /// `null` ET NON `0` : un taux sur zéro tentative n'est pas « aucun échec »,
    /// il n'existe pas. Rendre zéro afficherait une barre verte rassurante un
    /// jour où le prestataire n'a rien traité du tout.
    /// </remarks>
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
