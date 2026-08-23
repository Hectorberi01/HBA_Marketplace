using MediatR;
using Microsoft.Extensions.DependencyInjection;
using HBA.Shared.Application.Behaviors;

namespace HBA.Shared.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Enregistre les behaviors transverses du pipeline MediatR (ordre voulu :
    /// logging englobe la validation). MediatR lui-même (scan des handlers) est
    /// configuré dans le Bootstrap avec l'ensemble des assemblies de modules.
    /// </summary>
    public static IServiceCollection AddBuildingBlocksPipeline(this IServiceCollection services)
    {
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        return services;
    }
}
