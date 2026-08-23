using Microsoft.Extensions.DependencyInjection;

namespace HBA.Shared.Hosting.Grpc;

/// <summary>
/// Les interceptions que TOUT client gRPC interne doit porter.
/// </summary>
public static class ClientGrpcInterneExtensions
{
    /// <summary>
    /// Pose, dans le bon ordre, le disjoncteur puis la clé interne, la
    /// corrélation et l'échéance.
    /// </summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// UNE SEULE LIGNE À RECOPIER, PARCE QU'IL Y EN AVAIT VINGT.
    ///
    /// Les vingt enregistrements de clients gRPC du dépôt écrivaient
    /// <c>.AddInterceptor&lt;InternalCallClientInterceptor&gt;()</c> à la main. Y
    /// ajouter le disjoncteur aurait fait vingt-et-un endroits à tenir d'accord,
    /// et le vingt-deuxième client aurait été écrit sans — comme le vingt-et-unième
    /// aurait pu l'être sans la clé.
    ///
    /// CE QUI REND CET OUBLI IMPOSSIBLE À EXPÉDIER EN SILENCE.
    ///
    /// Un client qui oublie cet appel n'envoie pas la clé interne. Le serveur le
    /// refuse au PREMIER appel réel — pas dans six mois sous charge. C'est ce qui
    /// distingue cette duplication-là des autres : elle se punit d'elle-même. Le
    /// disjoncteur, lui, ne se punit pas — il se contente de manquer, et c'est
    /// précisément pourquoi il fallait l'attacher à quelque chose qui, si.
    ///
    /// L'ORDRE COMPTE.
    ///
    /// Le disjoncteur est posé EN PREMIER, donc le plus à l'extérieur : quand il
    /// est ouvert, il refuse avant que quiconque compose l'adresse ou l'échéance.
    /// L'inverse ferait le travail de préparation d'un appel qui n'aura pas lieu —
    /// sans conséquence mesurable, mais l'ordre inverse ne se justifie par rien.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
    public static IHttpClientBuilder AjouterLesInterceptionsInternes(this IHttpClientBuilder builder)
        => builder
            .AddInterceptor<DisjoncteurClientInterceptor>()
            .AddInterceptor<InternalCallClientInterceptor>();
}
