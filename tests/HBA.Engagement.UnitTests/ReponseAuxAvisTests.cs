using FluentAssertions;
using HBA.Engagement.Reviews.Application.Abstractions;
using HBA.Engagement.Reviews.Application.Reviews.Commands;
using HBA.Engagement.Reviews.Domain.Reviews;
using HBA.Merchants.Contracts;
using HBA.Shared.Domain.Results;
using Xunit;

namespace HBA.Engagement.UnitTests;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LA RÉPONSE DU VENDEUR À UN AVIS — UNE ROUTE OUVERTE À TOUT COMPTE INSCRIT.
///
/// LE HANDLER NE LISAIT AUCUNE IDENTITÉ. NI AUTEUR, NI VENDEUR, NI RÔLE.
///
/// La réponse s'affiche sous l'avis comme émanant du commerçant. Les
/// identifiants d'avis sont publics — la liste par produit les rend à qui la
/// demande, et le groupe de la route était `MapAuthenticatedGroup`. Un jeton
/// d'acheteur suffisait donc à faire dire n'importe quoi à n'importe quel
/// vendeur, sous l'avis de son choix : un aveu, une insulte à un client, un
/// renvoi vers un autre site.
///
/// CE QUE CES TESTS ÉPROUVENT VRAIMENT : QUE LA COMPARAISON PORTE SUR L'AVIS.
///
/// Un contrôle qui se contenterait de « l'appelant a-t-il un dossier vendeur »
/// passerait le premier test et échouerait au second — et c'est exactement le
/// contrôle qu'on écrit par réflexe. Le vendeur concerné est celui de L'AVIS,
/// pas celui du jeton.
///
/// ET CE N'EST PLUS « LE PROPRIÉTAIRE », C'EST « QUI A LA CAPACITÉ ».
///
/// La première version de cette garde comparait le vendeur résolu depuis le jeton
/// au `SellerId` de l'avis. Elle fermait le trou, et fermait aussi la route aux
/// MEMBRES — un chargé de clientèle porte `REVIEW_REPLY` par son rôle, et
/// répondre aux avis est précisément son métier.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class ReponseAuxAvisTests
{
    private static readonly Guid VendeurDeLAvis = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid CompteDuVendeur = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid CompteDunTiers = Guid.Parse("33333333-3333-4333-8333-333333333333");

    [Fact]
    public async Task Le_vendeur_de_l_avis_peut_repondre()
    {
        var avis = CreerAvis();
        var handler = Monter(avis, proprietaire: CompteDuVendeur, vendeur: VendeurDeLAvis);

        var resultat = await handler.Handle(
            new ReplyToReviewCommand(avis.Id.Value, CompteDuVendeur, "Merci, nous corrigeons l'emballage."),
            CancellationToken.None);

        resultat.IsSuccess.Should().BeTrue();
        avis.SellerReply.Should().Be("Merci, nous corrigeons l'emballage.");
    }

    /// <summary>
    /// LE TEST QUI DISTINGUE « EST VENDEUR » DE « EST LE VENDEUR DE CET AVIS ».
    ///
    /// L'appelant a un dossier vendeur parfaitement valide — le sien. Il n'a
    /// simplement rien à voir avec le produit noté. C'est le cas qu'un contrôle
    /// posé au niveau du groupe de routes ne peut pas voir, et c'est pour cela
    /// que la garde vit dans le handler.
    /// </summary>
    [Fact]
    public async Task Un_autre_vendeur_ne_repond_pas_a_l_avis_d_un_concurrent()
    {
        var avis = CreerAvis();
        var autreVendeur = Guid.Parse("44444444-4444-4444-8444-444444444444");
        var handler = Monter(avis, proprietaire: CompteDunTiers, vendeur: autreVendeur);

        var resultat = await handler.Handle(
            new ReplyToReviewCommand(avis.Id.Value, CompteDunTiers, "Achetez plutôt chez nous."),
            CancellationToken.None);

        resultat.IsFailure.Should().BeTrue();
        resultat.Error.Type.Should().Be(ErrorType.Forbidden);
        resultat.Error.Code.Should().Be("reviews.reply.not_seller");
        avis.SellerReply.Should().BeNull("rien ne doit être écrit sous l'avis d'un concurrent");
    }

    /// <summary>Le cas de départ : un acheteur, aucun dossier vendeur.</summary>
    [Fact]
    public async Task Un_compte_sans_dossier_vendeur_ne_repond_pas()
    {
        var avis = CreerAvis();
        var handler = Monter(avis, proprietaire: null, vendeur: null);

        var resultat = await handler.Handle(
            new ReplyToReviewCommand(avis.Id.Value, CompteDunTiers, "Ce produit est une contrefaçon."),
            CancellationToken.None);

        resultat.IsFailure.Should().BeTrue();
        resultat.Error.Type.Should().Be(ErrorType.Forbidden);
        avis.SellerReply.Should().BeNull();
    }

    /// <summary>
    /// RIEN N'EST ENREGISTRÉ QUAND LE REFUS TOMBE.
    ///
    /// `Review.Reply` mute l'agrégat AVANT tout enregistrement. Un handler qui
    /// contrôlerait après l'appel laisserait la réponse en mémoire — et le
    /// premier `SaveChanges` d'une autre commande dans le même contexte la
    /// persisterait. Le refus doit précéder la mutation, pas la suivre.
    /// </summary>
    [Fact]
    public async Task Un_refus_n_enregistre_rien()
    {
        var avis = CreerAvis();
        var unite = new UniteDeTest();
        var handler = new ReplyToReviewCommandHandler(
            new DepotDeTest(avis), unite, new CapacitesDeTest(null, null));

        await handler.Handle(
            new ReplyToReviewCommand(avis.Id.Value, CompteDunTiers, "…"),
            CancellationToken.None);

        unite.Enregistrements.Should().Be(0);
    }

    [Fact]
    public async Task Un_avis_inexistant_reste_introuvable()
    {
        var handler = new ReplyToReviewCommandHandler(
            new DepotDeTest(null), new UniteDeTest(), new CapacitesDeTest(null, null));

        var resultat = await handler.Handle(
            new ReplyToReviewCommand(Guid.NewGuid(), CompteDuVendeur, "Bonjour."),
            CancellationToken.None);

        resultat.IsFailure.Should().BeTrue();
        resultat.Error.Type.Should().Be(ErrorType.NotFound);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Outillage
    // ═════════════════════════════════════════════════════════════════════════

    private static Review CreerAvis()
    {
        var note = Rating.Create(2).Value;

        return Review.Create(
            productId: Guid.NewGuid(),
            sellerId: VendeurDeLAvis,
            buyerId: Guid.NewGuid(),
            orderId: Guid.NewGuid(),
            rating: note,
            title: "Colis abîmé",
            body: "L'emballage était ouvert à la livraison.",
            isVerifiedPurchase: true).Value;
    }

    private static ReplyToReviewCommandHandler Monter(Review avis, Guid? proprietaire, Guid? vendeur)
        => new(new DepotDeTest(avis), new UniteDeTest(), new CapacitesDeTest(proprietaire, vendeur));

    /// <summary>Rend l'avis fourni, quel que soit l'identifiant demandé.</summary>
    private sealed class DepotDeTest : IReviewRepository
    {
        private readonly Review? _avis;

        public DepotDeTest(Review? avis) => _avis = avis;

        public Task<Review?> GetByIdAsync(ReviewId id, CancellationToken cancellationToken = default)
            => Task.FromResult(_avis);

        // LE RESTE LÈVE. Ce handler ne lit ni la liste des avis d'un produit,
        // ni une note agrégée. Une valeur neutre rendue en silence ferait passer
        // un futur chemin de code sans qu'on l'ait éprouvé.

        public Task AddAsync(Review review, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Répondre à un avis n'en crée pas un.");

        public Task<IReadOnlyList<Review>> ListByProductAsync(
            Guid productId, int take = 100, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<Review>> ListBySellerAsync(
            Guid sellerId, int take = 100, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<(IReadOnlyList<Review> Items, int Total, IReadOnlyDictionary<string, int> StatusCounts)>
            ListForModerationAsync(int page, int pageSize, ReviewStatus? status, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Répondre à un avis ne consulte pas la file de modération.");

        public Task<bool> ExistsAsync(Guid buyerId, Guid productId, Guid orderId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<ProductRating> GetProductRatingAsync(Guid productId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<SellerRating> GetSellerRatingAsync(Guid sellerId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class UniteDeTest : IReviewsUnitOfWork
    {
        public int Enregistrements { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            Enregistrements++;
            return Task.FromResult(1);
        }
    }

    /// <summary>
    /// merchant-service en mémoire — une seule correspondance, ou aucune.
    /// </summary>
    /// <remarks>
    /// IL IMPLÉMENTE `IMerchantAccessApi`, PLUS `ISellerModuleApi`.
    ///
    /// Le handler ne demande plus « qui est le vendeur de ce compte » mais « ce
    /// compte a-t-il CETTE capacité sur CE vendeur ». Le double suit : il ne rend
    /// vrai que si les trois coïncident — le compte, le vendeur, et la permission.
    /// Un double qui répondrait vrai sur le seul couple compte/vendeur laisserait
    /// passer un test qui ne vérifierait plus rien de la capacité.
    /// </remarks>
    private sealed class CapacitesDeTest : IMerchantAccessApi
    {
        private readonly Guid? _compte;
        private readonly Guid? _vendeur;

        public CapacitesDeTest(Guid? compte, Guid? vendeur)
        {
            _compte = compte;
            _vendeur = vendeur;
        }

        public Task<bool> HasCapabilityAsync(
            Guid userId, Guid sellerId, Guid? storeId, string permission,
            CancellationToken cancellationToken = default)
            => Task.FromResult(
                _compte == userId
                && _vendeur == sellerId
                && permission == MerchantCapabilities.ReviewReply);

        // LÈVE. Ce handler ne résout pas un contexte d'accès : il pose une
        // question fermée. Une valeur neutre rendue en silence ferait passer un
        // futur chemin de code sans qu'on l'ait éprouvé.
        public Task<MerchantAccess?> GetAccessAsync(Guid userId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException(
                "La garde de la réponse aux avis demande une capacité, pas un contexte.");
    }
}
