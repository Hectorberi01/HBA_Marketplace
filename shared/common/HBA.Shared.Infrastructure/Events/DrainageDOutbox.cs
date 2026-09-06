namespace HBA.Shared.Infrastructure.Events;

/// <summary>
/// Le drainage de l'outbox est-il actif sur cet hote ?
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// UN SEUL LECTEUR POUR UNE SEULE VARIABLE.
///
/// `OUTBOX_ENABLED` est lu par le socle — pour decider si un producteur Kafka
/// indisponible doit refuser le demarrage — et par l'enregistrement d'outbox de
/// chaque service, pour decider s'il monte la boucle de livraison. Vingt-cinq
/// lectures de la meme variable, c'est vingt-cinq occasions d'ecrire
/// « OUTBOX_DISABLED » ou de comparer autrement.
///
/// ELLE EST LUE DANS L'ENVIRONNEMENT, PAS DANS `IConfiguration`, et c'est le
/// comportement d'origine : la poser dans une source en memoire n'a aucun effet.
/// Les harnais de tests le savent — voir `GatewayFactory`, ou l'oubli de cette
/// nuance a coute quarante et un tests.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public static class DrainageDOutbox
{
    public static bool Actif =>
        !string.Equals(Environment.GetEnvironmentVariable("OUTBOX_ENABLED"), "false",
                       StringComparison.OrdinalIgnoreCase);
}
