using System.Text;
using System.Text.Json;

namespace HBA.Admin.Desktop.Services;

/// <summary>La session de l'administrateur — en mémoire, et nulle part ailleurs.</summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// AUCUN JETON N'EST ÉCRIT SUR LE DISQUE. C'EST UNE DÉCISION, PAS UN MANQUE.
///
/// Les consoles web et mobiles persistent leur jeton de rafraîchissement : c'est
/// le bon arbitrage quand se reconnecter dix fois par jour ferait fuir
/// l'utilisateur. Ici l'arbitrage s'inverse, pour trois raisons :
///
///   1. CE QUE LE JETON OUVRE. Une centaine de points d'entrée
///      d'administration, dont les versements et la suspension de comptes. Le
///      vol d'un jeton vendeur coûte une boutique ; celui d'un jeton admin coûte
///      la plateforme.
///
///   2. IL N'Y A PAS DE COFFRE PORTABLE EN .NET. `ProtectedData` (DPAPI) est
///      Windows seulement. Sur Linux et macOS, il faudrait un trousseau système
///      atteint par P/Invoke — ou bien « chiffrer » avec une clé rangée à côté
///      du fichier chiffré, ce qui ne protège de rien et en donne l'apparence.
///      Cette apparence-là est pire que l'absence.
///
///   3. LE COÛT EST FAIBLE. Une session dure tant que l'application est ouverte.
///      Un administrateur la lance le matin et la ferme le soir.
///
/// Le jour où un trousseau portable sera câblé, la persistance redeviendra
/// discutable. Elle ne l'est pas avant.
///
/// CETTE CLASSE N'EST PAS SÛRE VIS-À-VIS DES FILS D'EXÉCUTION.
///
/// Elle est lue et écrite depuis le fil d'interface uniquement. Le client HTTP
/// la modifie après un rafraîchissement — donc depuis une continuation, qui
/// revient sur le contexte d'Avalonia. Si un jour un travail de fond y touche,
/// il faudra un verrou : deux rafraîchissements concurrents remplaceraient le
/// jeton l'un de l'autre, et le perdant serait déconnecté sans raison visible.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed class SessionAdmin
{
    private string? _jeton;
    private string? _rafraichissement;

    /// <summary>Une session est-elle ouverte ?</summary>
    public bool EstOuverte => !string.IsNullOrEmpty(_jeton);

    /// <summary>Nom affiché de l'administrateur, tiré des revendications du jeton.</summary>
    public string Nom { get; private set; } = string.Empty;

    /// <summary>Adresse de l'administrateur, affichée sous son nom.</summary>
    /// <remarks>
    /// DEUX COMPTES PEUVENT PORTER LE MÊME NOM, PAS LA MÊME ADRESSE.
    ///
    /// Sur un poste partagé — ce qu'est souvent un poste d'administration — le
    /// nom seul ne dit pas avec quel compte on est en train de suspendre une
    /// boutique.
    /// </remarks>
    public string Courriel { get; private set; } = string.Empty;

    /// <summary>Jeton d'accès courant, ou <c>null</c>.</summary>
    public string? Jeton => _jeton;

    /// <summary>Jeton de rafraîchissement courant, ou <c>null</c>.</summary>
    public string? Rafraichissement => _rafraichissement;

    /// <summary>Fin de validité du jeton d'accès.</summary>
    public DateTimeOffset ExpireLe { get; private set; } = DateTimeOffset.MinValue;

    /// <summary>Instant de la dernière saisie du MOT DE PASSE (revendication `auth_time`).</summary>
    /// <remarks>
    /// CE N'EST PAS L'HEURE DE CONNEXION, ET LA NUANCE EST TOUT L'INTÉRÊT.
    ///
    /// `auth_time` ne bouge PAS lors d'un rafraîchissement : c'est ce qui permet
    /// au serveur de distinguer « connecté depuis ce matin » de « a saisi son mot
    /// de passe il y a moins de cinq minutes ». Voir `StepUpAuthentication` côté
    /// serveur, dont la fenêtre est de cinq minutes.
    /// </remarks>
    public DateTimeOffset? MotDePasseSaisiLe { get; private set; }

    /// <summary>Les revendications `amr` du jeton — méthodes d'authentification.</summary>
    public IReadOnlyList<string> Methodes { get; private set; } = [];

    /// <summary>
    /// Le jeton courant satisfait-il l'élévation exigée par un geste sensible ?
    /// </summary>
    /// <remarks>
    /// LA MARGE DE TRENTE SECONDES EST DÉLIBÉRÉE.
    ///
    /// Le serveur accepte cinq minutes. Un client qui accepterait exactement cinq
    /// minutes laisserait passer une requête partie à 4 min 59 s et arrivée à
    /// 5 min 01 s : l'administrateur verrait son geste refusé APRÈS avoir cliqué,
    /// avec un message d'autorisation, alors qu'il venait de saisir son mot de
    /// passe. On redemande un peu trop tôt plutôt qu'un peu trop tard.
    /// </remarks>
    public bool ElevationValide
        => MotDePasseSaisiLe is { } instant
           && Methodes.Contains("pwd", StringComparer.Ordinal)
           && DateTimeOffset.UtcNow - instant < TimeSpan.FromMinutes(5) - TimeSpan.FromSeconds(30);

    /// <summary>Enregistre une paire de jetons fraîchement obtenue.</summary>
    public void Poser(string jeton, string rafraichissement, DateTimeOffset expireLe)
    {
        _jeton = jeton;
        _rafraichissement = rafraichissement;
        ExpireLe = expireLe;

        var revendications = LireRevendications(jeton);

        Nom = Chaine(revendications, "name")
              ?? $"{Chaine(revendications, "given_name")} {Chaine(revendications, "family_name")}".Trim();

        Courriel = Chaine(revendications, "email") ?? string.Empty;

        if (string.IsNullOrWhiteSpace(Nom))
        {
            Nom = string.IsNullOrWhiteSpace(Courriel) ? "Administrateur" : Courriel;
        }

        MotDePasseSaisiLe = Entier(revendications, "auth_time") is { } secondes
            ? DateTimeOffset.FromUnixTimeSeconds(secondes)
            : null;

        Methodes = Liste(revendications, "amr");
    }

    /// <summary>Efface tout. Appelée à la déconnexion ET à la fermeture.</summary>
    public void Oublier()
    {
        _jeton = null;
        _rafraichissement = null;
        ExpireLe = DateTimeOffset.MinValue;
        MotDePasseSaisiLe = null;
        Methodes = [];
        Nom = string.Empty;
        Courriel = string.Empty;
    }

    /// <summary>
    /// Décode la charge utile d'un JWT — SANS en vérifier la signature.
    /// </summary>
    /// <remarks>
    /// ET C'EST SANS DANGER, PARCE QU'ON NE DÉCIDE RIEN AVEC.
    ///
    /// Ce jeton vient d'être reçu de NOTRE passerelle, sur TLS. Ce qu'on en lit
    /// sert à afficher un nom et à savoir quand redemander le mot de passe.
    /// Aucune autorisation ne se décide ici : c'est le serveur qui refuse, et il
    /// vérifie la signature. Un décodeur qui déciderait d'un droit serait un
    /// contrôle d'accès dans le client, c'est-à-dire aucun contrôle.
    /// </remarks>
    private static Dictionary<string, JsonElement> LireRevendications(string jeton)
    {
        try
        {
            var parties = jeton.Split('.');

            if (parties.Length < 2)
            {
                return [];
            }

            var brut = parties[1].Replace('-', '+').Replace('_', '/');
            var octets = Convert.FromBase64String(brut.PadRight((brut.Length + 3) / 4 * 4, '='));

            return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
                Encoding.UTF8.GetString(octets)) ?? [];
        }
        catch (Exception exception) when (exception is FormatException or JsonException)
        {
            // Un jeton illisible n'est pas une raison de faire tomber
            // l'application : la session reste ouverte, le nom affiché est le
            // repli, et l'élévation sera simplement redemandée.
            return [];
        }
    }

    private static string? Chaine(Dictionary<string, JsonElement> revendications, string cle)
        => revendications.TryGetValue(cle, out var valeur) && valeur.ValueKind == JsonValueKind.String
            ? valeur.GetString()
            : null;

    private static long? Entier(Dictionary<string, JsonElement> revendications, string cle)
    {
        if (!revendications.TryGetValue(cle, out var valeur))
        {
            return null;
        }

        // `auth_time` ARRIVE PARFOIS EN CHAÎNE.
        //
        // La RFC le décrit comme un nombre, et certaines implémentations le
        // sérialisent entre guillemets. Ne traiter que le cas numérique ferait
        // silencieusement disparaître l'élévation : l'application redemanderait
        // le mot de passe à CHAQUE geste sensible, sans que rien n'explique
        // pourquoi.
        return valeur.ValueKind switch
        {
            JsonValueKind.Number when valeur.TryGetInt64(out var nombre) => nombre,
            JsonValueKind.String when long.TryParse(valeur.GetString(), out var texte) => texte,
            _ => null,
        };
    }

    private static IReadOnlyList<string> Liste(Dictionary<string, JsonElement> revendications, string cle)
    {
        if (!revendications.TryGetValue(cle, out var valeur))
        {
            return [];
        }

        // `amr` est un tableau selon la RFC 8176, mais une revendication à valeur
        // unique se sérialise couramment en chaîne nue. Les deux sont acceptées.
        return valeur.ValueKind switch
        {
            JsonValueKind.Array => valeur.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.String)
                .Select(e => e.GetString()!)
                .ToArray(),
            JsonValueKind.String => [valeur.GetString()!],
            _ => [],
        };
    }
}
