using FluentAssertions;
using Xunit;

namespace HBA.Gateway.IntegrationTests;

public sealed class CorrelationIdTests : IClassFixture<GatewayFactory>
{
    private const string Header = "X-Correlation-ID";

    private readonly GatewayFactory _factory;

    public CorrelationIdTests(GatewayFactory factory) => _factory = factory;

    [Fact]
    public async Task Un_identifiant_est_genere_quand_le_client_n_en_fournit_pas()
    {
        var response = await _factory.CreateClient().GetAsync("/health/live");

        response.Headers.TryGetValues(Header, out var values).Should().BeTrue();
        values!.Single().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task L_identifiant_fourni_par_le_client_est_conserve()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(Header, "abc-123");

        var response = await client.GetAsync("/health/live");

        response.Headers.GetValues(Header).Single().Should().Be("abc-123");
    }

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LE TEST QUI EMPÊCHE LA FABRICATION DE FAUSSES LIGNES DE JOURNAL.
    ///
    /// L'identifiant est recopié tel quel dans les journaux et propagé à treize
    /// services. Un saut de ligne suivi d'un texte crédible permet d'INSÉRER des
    /// entrées arbitraires dans Loki — jusqu'à simuler une trace d'audit. Une
    /// valeur de 100 Ko, elle, serait recopiée sur chaque appel sortant.
    ///
    /// Dans les deux cas, la passerelle doit ignorer la valeur du client et en
    /// générer une propre — sans rejeter la requête, qui n'a rien d'illégitime.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    [Theory]
    [InlineData("valeur avec espaces")]
    [InlineData("chemin/avec/slashes")]
    [InlineData("point-virgule;injection")]
    public async Task Un_identifiant_malforme_est_remplace_et_non_recopie(string malformed)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation(Header, malformed);

        var response = await client.GetAsync("/health/live");

        var returned = response.Headers.GetValues(Header).Single();

        returned.Should().NotBe(malformed);
        returned.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Un_identifiant_trop_long_est_remplace()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation(Header, new string('a', 500));

        var response = await client.GetAsync("/health/live");

        response.Headers.GetValues(Header).Single().Length.Should().BeLessThan(200);
    }
}
