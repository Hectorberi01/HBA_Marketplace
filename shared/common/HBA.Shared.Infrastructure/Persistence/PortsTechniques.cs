namespace HBA.Shared.Infrastructure.Persistence;

/// <summary>Marque la table d'outbox d'un service.</summary>
public interface IMessageDOutbox
{
}

/// <summary>Marque la table d'inbox d'un service.</summary>
public interface IEntreeDInbox
{
}

/// <summary>Marque la table d'idempotence d'un service.</summary>
public interface IEnregistrementDIdempotence
{
}
