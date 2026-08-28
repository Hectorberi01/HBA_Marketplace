using System.Collections.Frozen;
using Microsoft.Extensions.Configuration;

namespace HBA.Shared.Infrastructure.Hosting;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// SOMMES-NOUS EN PRODUCTION — UNE SEULE RÉPONSE, ET ELLE EST FERMÉE PAR DÉFAUT.
///
/// CE QUI ÉTAIT CASSÉ (audit du 27 août, constats 1.4 et 2.1).
///
/// Six copies de la même méthode `IsProduction` vivaient dans six installeurs :
/// payment, notification, media, food-cart, return-refund, et le socle
/// d'infrastructure lui-même. Toutes écrivaient exactement ceci :
///
///     var env = configuration["ASPNETCORE_ENVIRONMENT"]
///               ?? configuration["DOTNET_ENVIRONMENT"] ?? string.Empty;
///     return string.Equals(env, "Production", StringComparison.OrdinalIgnoreCase);
///
/// Autrement dit : TOUT CE QUI N'EST PAS LITTÉRALEMENT « Production » EST TRAITÉ
/// COMME DU DÉVELOPPEMENT — y compris la chaîne VIDE, y compris une variable
/// ABSENTE, y compris une faute de frappe.
///
/// ET C'EST L'INVERSE DE CE QUE FAIT ASP.NET CORE LUI-MÊME. Quand
/// `ASPNETCORE_ENVIRONMENT` n'est pas posée, `IHostEnvironment.EnvironmentName`
/// vaut « Production ». Le socle et le framework se contredisaient donc sur le
/// cas exact où ça compte le plus : la variable oubliée.
///
/// CE QUE CES SIX COPIES GARDAIENT, CONCRÈTEMENT :
///
///   • la CLÉ DE CHIFFREMENT des codes de réinitialisation et de vérification.
///     Sans clé configurée et hors production, `AesGcmSecretProtector` retombe
///     sur une clé dérivée d'une phrase FIXE ET PUBLIQUE, présente dans ce dépôt.
///     Les codes traversent alors l'outbox et Kafka « chiffrés » avec une clé que
///     quiconque lit le code peut recalculer ;
///   • le refus de démarrer avec des ADAPTATEURS gRPC SIMULÉS — stock jamais
///     remis en rayon, course d'enlèvement inexistante, preuve photo non vérifiée ;
///   • le refus de démarrer avec des FOURNISSEURS DE PAIEMENT simulés.
///
/// Dans les trois cas, un `ASPNETCORE_ENVIRONMENT` oublié sur un vrai serveur
/// donnait un démarrage NORMAL, sans erreur, avec des effets métier fictifs et
/// une cryptographie décorative.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// LA RÈGLE, ET CE QU'ELLE RETOURNE.
///
///   1. VARIABLE ABSENTE OU VIDE → PRODUCTION. C'est le défaut d'ASP.NET Core,
///      et c'est le seul défaut sûr : se tromper dans ce sens fait échouer un
///      démarrage avec un message explicite ; se tromper dans l'autre sens
///      produit des données fausses en silence.
///
///   2. NOM EXPLICITEMENT HORS PRODUCTION → pas la production. La liste est
///      <see cref="NomsHorsProduction"/>, elle est courte, et elle est ici.
///
///   3. TOUT AUTRE NOM → PRODUCTION, avec un avertissement qui NOMME les valeurs
///      acceptées.
///
/// LE POINT 3 CHANGE UN COMPORTEMENT, ET C'EST LE CŒUR DE LA CORRECTION.
///
/// L'ancien commentaire justifiait le repli permissif ainsi : « un nom mal
/// orthographié empêcherait de travailler ». C'est vrai, et ce n'est pas
/// comparable aux deux issues. Une faute de frappe en développement produit
/// désormais un refus de démarrer, avec un message qui donne la liste des noms
/// valides : une minute de correction, immédiatement visible. La même faute sur
/// un serveur de production produisait une clé de chiffrement publique et des
/// remboursements fictifs, invisibles jusqu'à ce que quelqu'un compare des
/// données. On accepte le premier coût pour supprimer le second.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// CE QUE CETTE CLASSE NE COUVRE PAS, ET IL FAUT LE SAVOIR.
///
///   • « Staging » EST TRAITÉ COMME HORS PRODUCTION. C'est le comportement
///     existant, conservé sciemment : un préproduction ne doit pas encaisser de
///     vrai argent. CONSÉQUENCE : un environnement de préproduction sans
///     `Secrets:Key` utilise la clé de développement publique. Le remède n'est
///     pas ici — c'est de poser la clé, ce que le runbook de staging demande.
///
///   • ELLE NE VÉRIFIE RIEN D'AUTRE QUE LE NOM. Un serveur qui se déclare
///     « Development » est cru sur parole. Il n'existe aucun moyen, depuis la
///     configuration seule, de distinguer un poste de développement d'une
///     machine de production mal étiquetée.
///
///   • AJOUTER UN NOM À LA LISTE EST UNE DÉCISION, PAS UN RÉGLAGE. Il n'y a
///     délibérément AUCUNE façon d'étendre <see cref="NomsHorsProduction"/> par
///     configuration : une porte de sortie configurable est exactement le
///     mécanisme par lequel ce genre de garde finit désactivé en production, par
///     une variable posée « le temps d'un test ».
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public static class EnvironnementDeploiement
{
    /// <summary>
    /// Les seuls noms qui dispensent des gardes de production. Volontairement
    /// courte, volontairement en dur.
    /// </summary>
    public static readonly FrozenSet<string> NomsHorsProduction =
        new[] { "Development", "Local", "Test", "Testing", "CI", "Staging" }
        .ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Le nom brut lu dans la configuration, ou la chaîne vide s'il est absent.
    /// Rendu séparément pour que les messages d'erreur des appelants puissent
    /// dire CE QU'ILS ONT LU, et pas seulement leur conclusion.
    /// </summary>
    public static string Nom(IConfiguration configuration)
        => configuration["ASPNETCORE_ENVIRONMENT"]
           ?? configuration["DOTNET_ENVIRONMENT"]
           ?? string.Empty;

    /// <summary>
    /// Vrai si les gardes de production doivent mordre. Voir l'encadré de classe.
    /// </summary>
    public static bool EstProduction(IConfiguration configuration)
    {
        var nom = Nom(configuration);

        if (string.IsNullOrWhiteSpace(nom))
        {
            return true;
        }

        if (NomsHorsProduction.Contains(nom))
        {
            return false;
        }

        // UN NOM INCONNU N'EST PAS UNE ERREUR SILENCIEUSE. On rend « production »,
        // ce qui fait mordre les gardes de l'appelant avec SON message métier ;
        // cette ligne dit pourquoi, sans quoi le refus paraîtrait sans rapport
        // avec la coquille qui l'a causé.
        Console.WriteLine(
            $"[Environnement]  « {nom} » n'est pas un nom d'environnement connu. "
            + "Les gardes de production s'appliquent donc, par sécurité. "
            + $"Noms acceptés hors production : {string.Join(", ", NomsHorsProduction.Order(StringComparer.Ordinal))}.");

        return true;
    }
}
