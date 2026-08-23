using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;
using HBA.Identity.Contracts;
using HBA.Identity.Domain.Users;

namespace HBA.Identity.Application.Users.Queries.GetSignupsSeries;

/// <summary>Évolution des inscriptions par jour sur l'intervalle [FromUtc, ToUtc[.</summary>
public sealed record GetSignupsSeriesQuery(DateTime FromUtc, DateTime ToUtc)
    : IQuery<IReadOnlyList<SignupPoint>>;

internal sealed class GetSignupsSeriesQueryHandler
    : IQueryHandler<GetSignupsSeriesQuery, IReadOnlyList<SignupPoint>>
{
    private readonly IUserRepository _userRepository;

    public GetSignupsSeriesQueryHandler(IUserRepository userRepository)
        => _userRepository = userRepository;

    public async Task<Result<IReadOnlyList<SignupPoint>>> Handle(
        GetSignupsSeriesQuery query, CancellationToken cancellationToken)
    {
        var rows = await _userRepository.SignupsByDayAsync(query.FromUtc, query.ToUtc, cancellationToken);

        IReadOnlyList<SignupPoint> series = rows
            .Select(r => new SignupPoint(r.Day, r.Count))
            .ToList();

        return Result.Success(series);
    }
}
