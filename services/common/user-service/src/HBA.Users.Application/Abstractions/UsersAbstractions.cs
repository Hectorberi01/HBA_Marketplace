using HBA.Shared.Application.Abstractions;

namespace HBA.Users.Application.Abstractions;

/// <summary>
/// Frontière transactionnelle du module User.
///
/// Elle existe pour la même raison que <c>IIdentityUnitOfWork</c> ou
/// <c>IDeliveryUnitOfWork</c> : un handler de ce module enregistre SES changements,
/// et n'a aucun moyen d'en enregistrer d'autres. Le jour où le module est extrait,
/// cette interface devient la limite d'un pas de Saga sans qu'une seule ligne de
/// code métier change.
/// </summary>
public interface IUsersUnitOfWork : IUnitOfWork
{
}
