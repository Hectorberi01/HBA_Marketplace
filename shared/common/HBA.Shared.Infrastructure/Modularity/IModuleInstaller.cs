using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HBA.Shared.Infrastructure.Modularity;

/// <summary>Point d'entrée d'enregistrement d'un module dans le conteneur.</summary>
public interface IModuleInstaller
{
    string ModuleName { get; }

    /// <summary>Assembly Application du module, scannée par MediatR (handlers).</summary>
    Assembly ApplicationAssembly { get; }

    void Install(IServiceCollection services, IConfiguration configuration);
}
