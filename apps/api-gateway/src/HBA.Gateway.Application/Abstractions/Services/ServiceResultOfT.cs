namespace HBA.Gateway.Application.Abstractions.Services;

/// <summary>
/// Résultat TYPÉ d'un appel sortant.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// POURQUOI UN SECOND TYPE PLUTÔT QUE DE MODIFIER <see cref="ServiceResult"/>.
///
/// Le résultat non typé reste la voie du mécanisme configuré, qui rend du
/// <c>JsonElement</c> parce qu'il ignore la forme de ce qu'il appelle. Ce type-ci
/// sert aux appels dont le contrat amont EXISTE et a été lu — c'est-à-dire à
/// presque tout, désormais.
///
/// Les deux coexistent parce que les deux besoins coexistent, et non par
/// hésitation. Le jour où plus aucune section n'est configurée, celui-ci reste.
///
/// AUCUNE EXCEPTION N'EST LEVÉE POUR UN ÉCHEC ATTENDU.
///
/// C'est la propriété qui rend la dégradation partielle possible : un agrégateur
/// qui doit continuer malgré une panne ne peut pas s'appuyer sur des exceptions,
/// il lui faudrait un try/catch par dépendance et la moindre omission ferait
/// tomber l'écran entier.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed record ServiceResult<T>(
    bool IsSuccess,
    int StatusCode,
    T? Value,
    string? FailureReason)
{
    /// <summary>
    /// Le service a répondu 404 : la ressource n'existe pas.
    /// </summary>
    /// <remarks>
    /// UN 404 N'EST PAS UNE PANNE, ET LES CONFONDRE COÛTE CHER.
    ///
    /// Une fiche produit dont le produit n'existe pas doit rendre 404 au client.
    /// Une fiche produit dont le catalogue est injoignable doit rendre 503. Sans
    /// cette distinction, un catalogue à terre ferait croire à des milliers de
    /// clients que leurs produits ont été supprimés — et déclencherait des
    /// signalements que personne ne saurait interpréter.
    /// </remarks>
    public bool IsNotFound => StatusCode == 404;

    public static ServiceResult<T> Success(int statusCode, T value)
        => new(true, statusCode, value, null);

    public static ServiceResult<T> Failure(int statusCode, string reason)
        => new(false, statusCode, default, reason);
}
