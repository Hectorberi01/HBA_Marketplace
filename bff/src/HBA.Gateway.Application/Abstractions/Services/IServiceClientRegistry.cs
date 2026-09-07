namespace HBA.Gateway.Application.Abstractions.Services;

/// <summary>Résout un client sortant à partir de sa clé logique (« Catalog », « Food »…).</summary>
public interface IServiceClientRegistry
{
    /// <summary>
    /// Retourne le client correspondant, ou <c> null</c> si la clé est inconnue —
    /// cas d'une configuration erronée, qui doit produire une section en échec et
    /// non une exception au milieu d'une agrégation.
    /// </summary>
    IServiceClient? Find(string serviceKey);

    /// <summary>Clés connues, pour la validation de configuration au démarrage.</summary>
    IReadOnlyCollection<string> KnownKeys { get; }
}
