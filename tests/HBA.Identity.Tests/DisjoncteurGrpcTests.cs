using FluentAssertions;
using Grpc.Core;
using HBA.Shared.Hosting.Grpc;
using Xunit;

namespace HBA.Identity.Tests;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// CE QUI COMPTE COMME UNE PANNE POUR LE DISJONCTEUR gRPC (lot 8.8).
///
/// POURQUOI CE FICHIER EST DANS LE PROJET DE TESTS D'IDENTITY.
///
/// `HBA.Shared.Hosting` n'a pas de projet de tests propre. `StepUpTests` teste
/// déjà un type de ce même assemblage depuis ici — il y arrive par transitivité
/// via `HBA.Identity.Api`. On suit ce précédent plutôt que de créer un
/// vingt-quatrième projet à inscrire dans `HBA.sln`.
///
/// CE QUE CES TESTS PROTÈGENT.
///
/// Un disjoncteur qui compte les REFUS MÉTIER comme des pannes rend un service
/// parfaitement sain inaccessible : il suffit d'un appelant qui boucle sur un
/// identifiant invalide pour couper l'accès de tout le monde. Un disjoncteur qui
/// ne compte pas les vraies pannes ne coupe jamais — et donne toutes les
/// apparences d'être en place.
///
/// Les deux erreurs sont silencieuses, et aucune ne casse la compilation.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class DisjoncteurGrpcTests
{
    [Theory]
    [InlineData(StatusCode.Unavailable)]
    [InlineData(StatusCode.DeadlineExceeded)]
    [InlineData(StatusCode.ResourceExhausted)]
    [InlineData(StatusCode.Internal)]
    [InlineData(StatusCode.DataLoss)]
    [InlineData(StatusCode.Unknown)]
    public void Une_panne_du_service_appele_compte(StatusCode statut)
        => DisjoncteurClientInterceptor.EstUnePanne(Echec(statut)).Should().BeTrue(
            "{0} décrit un service qui ne rend pas le service attendu", statut);

    /// <summary>
    /// LE TEST QUI COMPTE LE PLUS.
    ///
    /// `NotFound`, `InvalidArgument`, `FailedPrecondition`, `PermissionDenied`,
    /// `AlreadyExists` : le service a répondu, vite et correctement. `Aborted` et
    /// `AlreadyExists` sont même des GARDES QUI ONT FONCTIONNÉ — conflit de
    /// concurrence optimiste et contrainte d'unicité, traduits par
    /// `TraductionDesErreursServerInterceptor`. Les compter ouvrirait le
    /// disjoncteur un jour de soldes, quand les écritures concurrentes sur une
    /// même commande se multiplient : la garde ferait tomber le service qu'elle
    /// protège.
    /// </summary>
    [Theory]
    [InlineData(StatusCode.NotFound)]
    [InlineData(StatusCode.InvalidArgument)]
    [InlineData(StatusCode.FailedPrecondition)]
    [InlineData(StatusCode.PermissionDenied)]
    [InlineData(StatusCode.AlreadyExists)]
    [InlineData(StatusCode.Aborted)]
    [InlineData(StatusCode.OutOfRange)]
    public void Un_refus_metier_ne_compte_pas(StatusCode statut)
        => DisjoncteurClientInterceptor.EstUnePanne(Echec(statut)).Should().BeFalse(
            "{0} est une réponse du service, pas une panne du service", statut);

    /// <summary>
    /// `Cancelled` VIENT DE L'APPELANT, PAS DE L'APPELÉ.
    ///
    /// Un utilisateur qui ferme son onglet, une passerelle qui raccroche : le
    /// compter ferait ouvrir le disjoncteur sur du trafic parfaitement normal, et
    /// d'autant plus vite que le service est populaire.
    /// </summary>
    [Fact]
    public void Une_annulation_par_l_appelant_ne_compte_pas()
        => DisjoncteurClientInterceptor.EstUnePanne(Echec(StatusCode.Cancelled)).Should().BeFalse();

    /// <summary>
    /// `Unauthenticated` EST UNE FAUTE DE CONFIGURATION, PAS UNE PANNE.
    ///
    /// C'est ce que rend `InternalCallServerInterceptor` quand la clé interne est
    /// absente ou fausse. La compter ouvrirait TOUS les disjoncteurs d'un coup au
    /// premier déploiement où la clé manque — en présentant une erreur de
    /// déploiement sous les traits d'une panne générale, ce qui enverrait
    /// l'exploitation chercher au mauvais endroit.
    /// </summary>
    [Fact]
    public void Une_cle_interne_refusee_ne_compte_pas()
        => DisjoncteurClientInterceptor.EstUnePanne(Echec(StatusCode.Unauthenticated)).Should().BeFalse();

    /// <summary>
    /// CE QUI N'EST PAS UNE `RpcException` N'EST PAS COMPTÉ.
    ///
    /// Une exception levée par le code d'appel lui-même — une conversion ratée
    /// dans le client, un `NullReferenceException` de mapping — ne dit RIEN sur
    /// l'état du service appelé. La compter ferait couper l'accès à un service
    /// sain à cause d'un bug local, et masquerait ce bug derrière un « service
    /// indisponible ».
    /// </summary>
    [Fact]
    public void Une_exception_locale_ne_compte_pas()
    {
        DisjoncteurClientInterceptor.EstUnePanne(new InvalidOperationException()).Should().BeFalse();
        DisjoncteurClientInterceptor.EstUnePanne(null).Should().BeFalse();
    }

    private static RpcException Echec(StatusCode statut) => new(new Status(statut, "test"));
}
