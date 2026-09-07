using HBA.Gateway.Application.Abstractions.Services;
using HBA.Gateway.Application.Bff;
using HBA.Gateway.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace HBA.Gateway.Api.Extensions;

public static class BffOptionsExtensions
{
    /// <summary>Lie et valide la configuration des écrans agrégés.</summary>
    public static IServiceCollection AddGatewayBffOptions(
        this IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddOptions<BffAggregationOptions>()
            .Bind(configuration.GetSection(BffAggregationOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<BffAggregationOptions>, BffSectionsValidator>();

        return services;
    }
}

/// <summary>Vérifie que chaque section d'écran désigne un service connu et un chemin.</summary>
public sealed class BffSectionsValidator : IValidateOptions<BffAggregationOptions>
{
    public ValidateOptionsResult Validate(string? name, BffAggregationOptions options)
    {
        var failures = new List<string>();

        foreach (var (screenId, sections) in options.Screens)
        {
            foreach (var section in sections)
            {
                if (string.IsNullOrWhiteSpace(section.Key))
                {
                    failures.Add($"Bff:Screens:{screenId} — une section n'a pas de 'key'.");
                }

                if (string.IsNullOrWhiteSpace(section.Path))
                {
                    failures.Add($"Bff:Screens:{screenId}:{section.Key} — 'path' est vide.");
                }

                if (!ServiceKeys.All.Contains(section.Service, StringComparer.OrdinalIgnoreCase))
                {
                    failures.Add(
                        $"Bff:Screens:{screenId}:{section.Key} — service '{section.Service}' inconnu. "
                        + $"Valeurs acceptées : {string.Join(", ", ServiceKeys.All)}.");
                }
            }

            var duplicates = sections
                .GroupBy(section => section.Key, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key);

            foreach (var duplicate in duplicates)
            {
                // Deux sections de même clé : la réponse porterait deux entrées
                // homonymes et le client n'en lirait qu'une, sans savoir laquelle.
                failures.Add($"Bff:Screens:{screenId} — clé de section en double : '{duplicate}'.");
            }
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}
