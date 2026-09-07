using FluentAssertions;
using Xunit;

namespace HBA.Order.IntegrationTests;

/// <summary>UN EVENEMENT QUI ECHOUE TROIS FOIS DOIT ETRE RETROUVABLE.</summary>
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

        // TROIS TENTATIVES, DEUX PAUSES DE 2 ET 4 SECONDES, PUIS LA RECOPIE. La
        // marge couvre le rééquilibrage initial du groupe de consommation.
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
