namespace HBA.Shared.IntegrationEvents;

/// <summary>
/// L'IDENTITE D'UN CONSOMMATEUR DANS `consumer_inbox`, ECRITE PLUTOT QUE DEDUITE.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class NomDeConsommateurAttribute : Attribute
{
    public NomDeConsommateurAttribute(string nom)
    {
        if (string.IsNullOrWhiteSpace(nom))
        {
            throw new ArgumentException(
                "Le nom de consommateur est une cle d'idempotence en base : il ne peut pas etre vide.",
                nameof(nom));
        }

        Nom = nom;
    }

    /// <summary>La cle telle qu'elle est — ou sera — dans `consumer_inbox.consumer_name`.</summary>
    public string Nom { get; }
}
