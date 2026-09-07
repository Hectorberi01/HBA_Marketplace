using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace HBA.Gateway.IntegrationTests;

/// <summary>
/// Démarre la passerelle en mémoire, sans qu'aucun microservice n'existe.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// AUCUN SERVICE N'EST SIMULÉ, ET C'EST LE POINT DE CES TESTS.
///
/// Ils vérifient ce que la passerelle décide AVANT de router : authentification,
/// autorisation, limitation de débit, corrélation, forme des erreurs. Toutes ces
/// décisions se prennent sans jamais joindre un service — c'est précisément
/// pourquoi elles sont testables aujourd'hui, alors que les treize services
/// n'existent pas.
///
/// Une requête qui passe tous les contrôles finit en 502 (destination
/// injoignable). Un 502 est donc, ici, la preuve que les contrôles ont été
/// FRANCHIS — l'inverse d'un échec.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public class GatewayFactory : WebApplicationFactory<Program>
{
    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// CE QUI DOIT PASSER PAR L'ENVIRONNEMENT, ET NON PAR LA CONFIGURATION.
    ///
    /// La passerelle consomme desormais un evenement Kafka — `TokenRevoked`, pour
    /// evincer les verdicts de revocation mis en cache. Elle appelle donc
    /// `AddBuildingBlocksInfrastructure`, qui REFUSE DE DEMARRER quand le
    /// producteur est indisponible alors que le drainage d'outbox est actif.
    ///
    /// Sans les lignes ci-dessous, les 41 tests de ce projet echouaient d'un bloc
    /// a la construction de l'hote — pas sur une assertion, mais sur un refus de
    /// demarrage.
    ///
    /// CE PARAGRAPHE A DEJA ETE FAUX UNE FOIS, ET ÇA VAUT D'ETRE ECRIT ICI.
    /// `OUTBOX_ENABLED` seul avait ete annonce comme la correction ; le compte
    /// d'echecs est reste a 41, a l'unite pres, parce que le refus venait d'un
    /// SECOND garde du meme appel — la cle de protection des secrets. Un compte
    /// qui ne bouge pas apres une correction n'est pas une correction partielle,
    /// c'est un diagnostic a refaire.
    ///
    /// `OUTBOX_ENABLED` EST LU DANS L'ENVIRONNEMENT, PAS DANS LA CONFIGURATION.
    /// `DrainageDOutbox.Actif` appelle `Environment.GetEnvironmentVariable`
    /// directement : le poser dans `AddInMemoryCollection` n'aurait aucun effet,
    /// et l'echec serait identique et inexplicable.
    ///
    /// LE CONSTRUCTEUR STATIQUE, ET NON `ConfigureWebHost`. Les variables sont
    /// lues par `CreateBuilder` avant que la premiere ligne de `Program` ne
    /// s'execute ; les poser plus tard arriverait apres la lecture.
    ///
    /// CE QUE ÇA NE COUVRE PAS. Aucun test de ce projet n'eprouve le
    /// consommateur lui-meme : avec `Kafka:Enabled=false`, il ne demarre pas. La
    /// coupure immediate d'un jeton revoque n'est donc verifiee par rien.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    static GatewayFactory()
    {
        Environment.SetEnvironmentVariable("OUTBOX_ENABLED", "false");

        // ═════════════════════════════════════════════════════════════════════
        // L'HOTE DE TEST DECLARAIT « Development » PAR UN CANAL QUE LE SOCLE NE
        // LIT PAS. C'EST CE QUI FAISAIT ECHOUER LES 41 TESTS.
        //
        // `ConfigureWebHost` appelle `UseEnvironment(Development)` : cela pose
        // `IHostEnvironment.EnvironmentName`, et rien d'autre. Or
        // `EnvironnementDeploiement.EstProduction` — dont depend la fabrique
        // d'`ISecretProtector` enregistree par `AddBuildingBlocksInfrastructure` —
        // lit la CLE DE CONFIGURATION `ASPNETCORE_ENVIRONMENT`, pas
        // `IHostEnvironment`. Absente, elle vaut « production » par defaut, et
        // c'est le bon defaut : une variable oubliee sur un vrai serveur doit
        // faire mordre les gardes, pas les desactiver.
        //
        // Resultat : l'hote de test se croyait en developpement, le socle le
        // croyait en production, et `VerificationDesSecretsAuDemarrage` — qui
        // force la fabrique paresseuse au demarrage — arretait l'hote sur
        // « Security:SecretProtection:Key est absente en production ». Les 41
        // echecs etaient donc tous le MEME echec, avant la premiere assertion.
        //
        // CE QUI A ETE CHOISI : poser la variable d'environnement, avec LE MEME
        // NOM que `UseEnvironment` ci-dessous. Deux canaux, une seule valeur.
        // Fournir une fausse cle a la place aurait masque l'incoherence en la
        // laissant en place, et cette passerelle ne chiffre ni ne dechiffre rien.
        //
        // Meme raison que pour `OUTBOX_ENABLED` : le constructeur statique, et
        // non `ConfigureAppConfiguration`. `AddBuildingBlocksInfrastructure`
        // CAPTURE l'instance de configuration passee par `Program`, ligne 39 ;
        // une source ajoutee ensuite par la fabrique de test n'est pas garantie
        // d'etre visible depuis cette reference-la. La variable d'environnement,
        // elle, est lue par `CreateBuilder` avant la premiere ligne de `Program`.
        //
        // CE QUE ÇA NE COUVRE PAS. La passerelle utilise alors la CLE DE
        // DEVELOPPEMENT publique du depot. C'est sans effet ici — aucun test de
        // ce projet ne protege ni ne lit un secret — mais cela ne verifie rien du
        // chiffrement reel. Et la divergence de fond reste entiere : tout hote de
        // test qui appelle `UseEnvironment` sans poser cette variable retombera
        // dans le meme piege, sans que rien ne le signale.
        // ═════════════════════════════════════════════════════════════════════
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", Environments.Development);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Development);

        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Clé de test explicite : ne pas dépendre de celle
                // d'appsettings.Development.json, qu'un développeur peut changer
                // sans se douter qu'il casse la suite de tests.
                ["Authentication:SigningKey"] = TestTokens.SigningKey,
                ["Authentication:Issuer"] = TestTokens.Issuer,
                ["Authentication:Audience"] = TestTokens.Audience,

                // Kafka éteint : sinon le consommateur d'événements — ajouté avec
                // l'invalidation du cache de révocation — cherche un courtier
                // absent pendant toute la suite.
                ["Kafka:Enabled"] = "false",

                // Export désactivé : sans cela, chaque test tente d'atteindre le
                // collecteur et attend son délai de connexion.
                ["OpenTelemetry:Endpoint"] = string.Empty,

                // Les destinations doivent être des URL VALIDES — la validation
                // des options échouerait au démarrage — mais n'ont pas à répondre.
                ["Services:Identity"] = "http://127.0.0.1:59001",
                ["Services:Order"] = "http://127.0.0.1:59002",
                ["Services:Catalog"] = "http://127.0.0.1:59003"
            });
        });
    }
}
