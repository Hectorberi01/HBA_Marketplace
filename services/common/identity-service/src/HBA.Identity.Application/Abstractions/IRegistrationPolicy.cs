namespace HBA.Identity.Application.Abstractions;

/// <summary>
/// Politique d'activation des comptes à l'inscription : un compte nouvellement créé
/// est-il utilisable tout de suite, ou attend-il l'aval d'un administrateur ?
/// </summary>
public interface IRegistrationPolicy
{
    /// <summary>
    /// Un compte issu de l'inscription publique (app acheteur, site) attend-il une
    /// validation ? Si faux, il est actif immédiatement.
    /// </summary>
    bool RequireApprovalForBuyers { get; }

    /// <summary>Un compte créé par la console d'administration attend-il une validation ?</summary>
    bool RequireApprovalForAdminCreated { get; }
}
