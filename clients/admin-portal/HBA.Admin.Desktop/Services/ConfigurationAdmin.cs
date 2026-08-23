using System.Text.Json;

namespace HBA.Admin.Desktop.Services;

/// <summary>Ce dont l'application a besoin pour démarrer.</summary>
/// <param name="UrlPasserelle">Racine de la passerelle HBA, sans barre finale.</param>
public sealed record ConfigurationAdmin(string UrlPasserelle)
{
    private const string Fichier = "appsettings.json";
    private const string Variable = "HBA_ADMIN_GATEWAY_URL";

    /// <summary>
    /// Lit `appsettings.json`, surchargé par la variable d'environnement.
    /// </summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// AUCUNE ADRESSE PAR DÉFAUT, ET SURTOUT PAS CELLE DE LA PRODUCTION.
    ///
    /// Les deux applications Flutter du dépôt se replient sur une adresse de
    /// STAGING précisément pour qu'un déploiement mal configuré ne tombe jamais
    /// sur la production par accident. Le raisonnement ne se transpose pas ici,
    /// et s'inverse même : un back-office qui pointerait silencieusement vers la
    /// staging donnerait à un administrateur l'illusion d'avoir approuvé un
    /// dossier. Il l'apprendrait quand le vendeur rappellerait.
    ///
    /// Sans adresse, l'application refuse de démarrer. C'est bruyant, immédiat,
    /// et cela ne coûte qu'une variable d'environnement.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
    public static ConfigurationAdmin Charger()
    {
        var url = Environment.GetEnvironmentVariable(Variable);

        if (string.IsNullOrWhiteSpace(url))
        {
            url = LireDuFichier();
        }

        if (string.IsNullOrWhiteSpace(url))
        {
            throw new InvalidOperationException(
                $"Adresse de la passerelle absente. Renseigner « gateway » dans {Fichier}, "
                + $"ou la variable d'environnement {Variable}.");
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var adresse)
            || (adresse.Scheme != Uri.UriSchemeHttp && adresse.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException($"Adresse de passerelle invalide : « {url} ».");
        }

        // HTTP TOLÉRÉ SUR LA BOUCLE LOCALE UNIQUEMENT.
        //
        // Un back-office qui parle en clair à un hôte distant expose des jetons
        // d'administration sur le réseau. Sur `localhost`, il n'y a pas de
        // réseau à écouter, et l'exiger interdirait la pile de développement.
        if (adresse.Scheme == Uri.UriSchemeHttp && !adresse.IsLoopback)
        {
            throw new InvalidOperationException(
                $"HTTP en clair refusé vers un hôte distant : « {url} ». Utiliser HTTPS.");
        }

        return new ConfigurationAdmin(url.TrimEnd('/'));
    }

    private static string? LireDuFichier()
    {
        var chemin = Path.Combine(AppContext.BaseDirectory, Fichier);

        if (!File.Exists(chemin))
        {
            return null;
        }

        using var document = JsonDocument.Parse(File.ReadAllText(chemin));

        return document.RootElement.TryGetProperty("gateway", out var valeur)
               && valeur.ValueKind == JsonValueKind.String
            ? valeur.GetString()
            : null;
    }
}
