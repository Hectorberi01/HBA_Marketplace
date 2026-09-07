using MediatR;
using Microsoft.Extensions.DependencyInjection;
using HBA.Shared.Application.Behaviors;

namespace HBA.Shared.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Enregistre les behaviors transverses du pipeline MediatR (ordre voulu :
    /// logging englobe la validation).
    /// </summary>
    public static IServiceCollection AddBuildingBlocksPipeline(this IServiceCollection services)
    {
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        return services;
    }
}
