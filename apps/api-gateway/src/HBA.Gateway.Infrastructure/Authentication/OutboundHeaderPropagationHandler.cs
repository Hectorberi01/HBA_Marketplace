using Microsoft.AspNetCore.Http;

namespace HBA.Gateway.Infrastructure.Authentication;

/// <summary>
/// Recopie sur chaque appel sortant les seuls en-têtes de
/// <see cref="OutboundHeaderPolicy.Allowed"/> présents sur la requête entrante.
/// </summary>
public sealed class OutboundHeaderPropagationHandler : DelegatingHandler
{
    private readonly IHttpContextAccessor _accessor;

    public OutboundHeaderPropagationHandler(IHttpContextAccessor accessor)
        => _accessor = accessor;

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var incoming = _accessor.HttpContext?.Request;

        if (incoming is null)
        {
            // Appel hors requête HTTP : tâche de fond, sonde de démarrage, test.
            // Rien à propager, et surtout rien à inventer.
            return base.SendAsync(request, cancellationToken);
        }

        foreach (var name in OutboundHeaderPolicy.Allowed)
        {
            if (!incoming.Headers.TryGetValue(name, out var values))
            {
                continue;
            }

            // `Remove` AVANT `TryAddWithoutValidation`, sans quoi la valeur
            // s'AJOUTE à celle déjà posée. Deux en-têtes `Authorization` sur une
            // même requête : certains serveurs prennent le premier, d'autres le
            // dernier, d'autres rejettent. Le comportement dépendrait alors du
            // service appelé.
            request.Headers.Remove(name);
            request.Headers.TryAddWithoutValidation(name, (IEnumerable<string>)values!);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
