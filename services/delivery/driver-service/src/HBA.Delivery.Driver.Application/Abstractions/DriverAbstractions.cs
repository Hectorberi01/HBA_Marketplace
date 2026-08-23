using HBA.Shared.Application.Abstractions;

namespace HBA.Drivers.Application.Abstractions;

/// <summary>
/// Frontière transactionnelle du module Drivers.
///
/// ELLE N'EXISTAIT PAS. Ce service n'avait ni base, ni transaction, ni rien à
/// valider : son état tenait dans un `ConcurrentDictionary` de processus
/// (`DriverStore`), perdu au redémarrage et non partagé entre réplicas. Une
/// vérification de dossier faite sur un conteneur était invisible de l'autre.
/// </summary>
public interface IDriverUnitOfWork : IUnitOfWork
{
}
