using HBA.Users.Domain.Addresses;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;

namespace HBA.Users.Application.Addresses;

/// <summary>Liste les adresses d'un utilisateur (défaut en tête).</summary>
public sealed record ListAddressesQuery(Guid UserId) : IQuery<IReadOnlyList<AddressDto>>;

internal sealed class ListAddressesQueryHandler : IQueryHandler<ListAddressesQuery, IReadOnlyList<AddressDto>>
{
    private readonly IAddressRepository _repository;

    public ListAddressesQueryHandler(IAddressRepository repository) => _repository = repository;

    public async Task<Result<IReadOnlyList<AddressDto>>> Handle(ListAddressesQuery query, CancellationToken cancellationToken)
    {
        var addresses = await _repository.ListByUserAsync(query.UserId, cancellationToken);
        IReadOnlyList<AddressDto> result = addresses
            .Select(AddressDto.From)
            .ToList();
        return Result.Success(result);
    }
}
