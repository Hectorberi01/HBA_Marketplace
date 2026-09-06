using HBA.Shared.Infrastructure.Idempotency;
using HBA.Shared.Infrastructure.Persistence;
using HBA.Communication.Notifications.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using HBA.Communication.Notifications.Infrastructure.Persistence.Outbox;
using HBA.Communication.Notifications.Infrastructure.Persistence.Inbox;
using HBA.Communication.Notifications.Infrastructure.Messaging.Kafka.Retry;
using HBA.Communication.Notifications.Infrastructure.Messaging.Kafka.Processors;
// ═════════════════════════════════════════════════════════════════════════════
// COPIE DEPUIS `HBA.Shared.Infrastructure.Idempotency`.
//
// La table `idempotency_records` de CE service est creee par SES migrations :
// l'entite qui la decrit lui appartient. Le socle n'en garde que le port,
// `IIdempotencyStore`, que `IdempotencyEndpointFilter` resout sur chaque route
// annotee `AllowIdempotency()`.
//
// A REGENERER : l'instantane de modele de ce service reference encore le type du
// socle sous forme de chaine. Il compile et les migrations s'appliquent — mais
// modele et instantane divergent jusqu'a un `dotnet ef migrations add`, au diff
// de schema vide.
// ═════════════════════════════════════════════════════════════════════════════

namespace HBA.Communication.Notifications.Infrastructure.Idempotency;

/// <summary>
/// Pose le magasin d'idempotence ET son purgeur, en un seul geste.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// POURQUOI LES DEUX ENSEMBLE, ET PAS DEUX LIGNES À CÔTÉ.
///
/// Sept installeurs écrivaient chacun, à la main :
///
///     services.AddScoped&lt;IIdempotencyStore, EfIdempotencyStore&lt;XDbContext&gt;&gt;();
///
/// Ajouter le purgeur en seconde ligne dans chacun des sept aurait reconduit
/// exactement le mécanisme qui a produit le défaut d'origine : une capacité qui
/// dépend de N copies restant d'accord. Il aurait suffi qu'un huitième service
/// arrive en ne copiant que la première ligne pour que sa table ne soit jamais
/// purgée — sans rien casser, sans rien signaler.
///
/// Une seule porte d'entrée rend l'oubli impossible : on ne peut pas enregistrer
/// le magasin sans enregistrer sa purge.
///
/// LE PURGEUR EST UN `HostedService`, LE MAGASIN EST `Scoped`. Le premier vit
/// aussi longtemps que l'hôte et prend ses propres portées ; le second suit la
/// requête. C'est précisément pour cela que le purgeur reçoit un
/// `IServiceScopeFactory` plutôt qu'un DbContext.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public static class IdempotencyRegistration
{
    public static IServiceCollection AjouterIdempotenceCommunicationNotifications(this IServiceCollection services)
    {
        services.AddScoped<IIdempotencyStore, EfIdempotencyStore>();
        services.AddHostedService<IdempotencyPurger>();

        return services;
    }
}
