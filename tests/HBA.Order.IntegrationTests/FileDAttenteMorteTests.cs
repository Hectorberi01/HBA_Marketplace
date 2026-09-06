using FluentAssertions;
using Xunit;

namespace HBA.Order.IntegrationTests;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// UN EVENEMENT QUI ECHOUE TROIS FOIS DOIT ETRE RETROUVABLE.
///
/// CE QUE CE TEST EPROUVE, ET QUE RIEN N'EPROUVAIT.
///
/// Avant la file d'attente morte, un evenement dont le traitement echouait trois
/// fois etait PERDU : l'offset avançait, le message disparaissait du sujet a
/// l'expiration de la retention, et il ne restait qu'une ligne de journal que
/// personne ne relit. C'est par ce chemin que le `user.registered` du 30 aout est
/// parti — il n'en reste rien a rejouer.
///
/// LE CAS CHOISI EST REEL, PAS SIMULE. La charge publiée ici porte un `orderId`
/// qui n'est pas un GUID. C'est exactement ce que produit un producteur qui
/// change le type d'un champ sans compatibilite ascendante : le type est reconnu,
/// la desserialisation leve, et l'erreur remonte jusqu'aux reprises. Aucun
/// gestionnaire de test n'est injecte — c'est le vrai chemin d'echec.
///
/// LE SUJET `.dlq` EST CREE EXPLICITEMENT, ET C'EST LE POINT DU TEST.
///
/// Le producteur de lettres mortes pose `AllowAutoCreateTopics = false`, comme en
/// production ou le courtier refuse la creation implicite. Si ce test creait le
/// sujet par accident — en comptant sur la creation automatique du courtier de
/// test — il passerait ici et mentirait sur la production. Le creer a la main
/// reproduit le provisionnement que `deploy.yml` fait pour de vrai.
///
/// CE QUE CE TEST NE COUVRE PAS. Il ne verifie pas qu'un message mis en lettre
/// morte peut etre REJOUE : rien ne relit ces sujets aujourd'hui, ni consommateur
/// d'archivage, ni commande de rejeu. Il prouve que le message survit, pas qu'on
/// sait s'en resservir.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
[Collection(OrderIntegrationCollection.Nom)]
public sealed class FileDAttenteMorteTests
{
    /// <summary>Le sujet des lettres mortes de `service.financial.v1`.</summary>
    private const string SujetMort = BusDeTest.SujetFinancial + ".dlq";

    private readonly OrderIntegrationFixture _fixture;

    public FileDAttenteMorteTests(OrderIntegrationFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Un_evenement_indeserialisable_finit_dans_la_file_d_attente_morte()
    {
        await BusDeTest.CreerSujetAsync(_fixture.BootstrapServers, SujetMort);

        var eventId = Guid.NewGuid();
        var marqueur = Guid.NewGuid().ToString();

        // `payment.captured` EST UN TYPE QUE CE SERVICE RECONNAIT.
        //
        // Un type inconnu serait journalise « NON RECONNU » et l'evenement serait
        // saute SANS exception : il n'atteindrait jamais les reprises, et ce test
        // passerait pour la mauvaise raison. Il faut donc un type reconnu dont la
        // CHARGE est illisible.
        await BusDeTest.PublierAsync(
            _fixture.BootstrapServers,
            BusDeTest.SujetFinancial,
            eventId,
            typeEvenement: "payment.captured",
            aggregateType: "Order",
            aggregateId: marqueur,
            charge: new
            {
                // Un GUID est attendu ici. Une chaîne libre fait lever la
                // desserialisation, a chacune des trois tentatives.
                orderId = "ceci-n-est-pas-un-guid",
                paymentId = Guid.NewGuid(),
                amount = 1000m,
                currency = "XOF"
            });

        // TROIS TENTATIVES, DEUX PAUSES DE 2 ET 4 SECONDES, PUIS LA RECOPIE.
        // La marge couvre le rééquilibrage initial du groupe de consommation.
        var limite = DateTime.UtcNow + TimeSpan.FromSeconds(90);
        IReadOnlyList<(string Valeur, IReadOnlyDictionary<string, string> Entetes)> morts = [];

        while (DateTime.UtcNow < limite)
        {
            morts = (await Task.Run(() => BusDeTest.DrainerBrut(_fixture.BootstrapServers, SujetMort)))
                .Where(m => m.Valeur.Contains(marqueur, StringComparison.Ordinal))
                .ToList();

            if (morts.Count > 0)
            {
                break;
            }
        }

        morts.Should().HaveCount(
            1,
            "un événement abandonné après trois tentatives doit être recopié sur son sujet de "
            + "lettres mortes — sans quoi il disparaît avec la rétention et rien ne reste à rejouer");

        var (valeur, entetes) = morts[0];

        valeur.Should().Contain(
            eventId.ToString(),
            "le message est recopié TEL QUEL : c'est ce qui le rend rejouable sans reconstitution");

        entetes.Should().ContainKey("dlq-sujet-origine")
            .WhoseValue.Should().Be(
                BusDeTest.SujetFinancial,
                "sans le sujet d'origine, on sait qu'un message est mort mais pas où le remettre");

        entetes.Should().ContainKey("dlq-offset-origine");
        entetes.Should().ContainKey("dlq-horodatage");

        entetes.Should().ContainKey("dlq-raison")
            .WhoseValue.Should().NotBeEmpty(
                "la raison distingue un message empoisonné d'une panne passagère de son "
                + "consommateur — les deux finissent ici, ils ne se traitent pas pareil");
    }
}
