using System.Text.Json;
using FluentAssertions;
using HBA.Gateway.Api.Middlewares;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HBA.Gateway.IntegrationTests;

/// <summary>
/// Teste <see cref="ExceptionMiddleware"/> directement.
/// </summary>
/// <remarks>
/// Il n'existe volontairement aucune route qui lève : en ajouter une pour les
/// besoins du test l'exposerait aussi en production. Le middleware est testable
/// isolément avec un <see cref="DefaultHttpContext"/>, ce qui est à la fois plus
/// sûr et plus rapide.
/// </remarks>
public sealed class ProblemDetailsTests
{
    private static async Task<(int Status, JsonElement Body)> Invoke(RequestDelegate next)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/orders/mine";
        context.Request.Method = "GET";
        context.Items[CorrelationIdMiddleware.HeaderName] = "correlation-de-test";

        using var body = new MemoryStream();
        context.Response.Body = body;

        var middleware = new ExceptionMiddleware(
            next, NullLogger<ExceptionMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        body.Position = 0;
        using var document = await JsonDocument.ParseAsync(body);

        return (context.Response.StatusCode, document.RootElement.Clone());
    }

    [Fact]
    public async Task Une_exception_produit_un_ProblemDetails_500()
    {
        var (status, body) = await Invoke(_ => throw new InvalidOperationException("peu importe"));

        status.Should().Be(StatusCodes.Status500InternalServerError);
        body.GetProperty("title").GetString().Should().Be("Internal Server Error");
        body.GetProperty("status").GetInt32().Should().Be(500);
        body.GetProperty("correlationId").GetString().Should().Be("correlation-de-test");
    }

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LE TEST QUI EMPÊCHE UNE FUITE PAR LE MESSAGE D'EXCEPTION.
    ///
    /// Interpoler `exception.Message` dans `Detail` est la modification la plus
    /// tentante du monde : elle rend le débogage tellement plus commode. Elle
    /// ferait aussi sortir, selon l'exception rencontrée, une chaîne de connexion
    /// PostgreSQL, un nom d'hôte interne ou un fragment de requête SQL — vers
    /// Internet, dans un corps de réponse HTTP 500.
    ///
    /// Le message est simulé ici avec ce qu'une vraie exception de connexion
    /// contiendrait.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    [Fact]
    public async Task Aucun_detail_interne_ne_sort_dans_la_reponse()
    {
        const string secret =
            "Host=postgres;Database=hba_financial;Username=hba;Password=SuperSecret123";

        var (_, body) = await Invoke(_ => throw new InvalidOperationException(secret));

        var serialized = body.GetRawText();

        serialized.Should().NotContain("Password");
        serialized.Should().NotContain("postgres");
        serialized.Should().NotContain("SuperSecret123");
        serialized.Should().NotContain("InvalidOperationException");
    }

    /// <summary>
    /// Un client mobile qui bascule du Wi-Fi vers la 4G abandonne sa requête en
    /// pleine course. Compter cela en 500 noie les vraies pannes dans les
    /// tableaux de bord et déclenche des alertes sur un réseau qui a vacillé.
    /// </summary>
    [Fact]
    public async Task Un_client_qui_raccroche_ne_produit_pas_un_500()
    {
        var context = new DefaultHttpContext();
        var aborted = new CancellationTokenSource();
        await aborted.CancelAsync();
        context.RequestAborted = aborted.Token;
        context.Response.Body = new MemoryStream();

        var middleware = new ExceptionMiddleware(
            _ => throw new OperationCanceledException(), NullLogger<ExceptionMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(499);
    }
}
