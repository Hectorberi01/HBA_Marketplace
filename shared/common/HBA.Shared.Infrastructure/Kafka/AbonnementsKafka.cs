namespace HBA.Shared.Infrastructure.Kafka;

/// <summary>Les sujets Kafka qu'un service déclare écouter.</summary>
/// <param name="Sujets">Noms COMPLETS, préfixe et version compris — `service.identity.v1`.</param>
public sealed record AbonnementsKafka(params string[] Sujets);
