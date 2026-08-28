using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HBA.Shared.Infrastructure.Idempotency;

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
    public static IServiceCollection AddIdempotence<TDbContext>(this IServiceCollection services)
        where TDbContext : DbContext
    {
        services.AddScoped<IIdempotencyStore, EfIdempotencyStore<TDbContext>>();
        services.AddHostedService<IdempotencyPurger<TDbContext>>();

        return services;
    }
}
