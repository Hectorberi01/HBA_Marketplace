namespace HBA.Shared.Application.Abstractions;

/// <summary>CHIFFREMENT DES SECRETS QUI TRAVERSENT LE BUS.</summary>
public interface ISecretProtector
{
    /// <summary>Chiffre un secret destiné à traverser l'outbox et Kafka.</summary>
    string Protect(string plaintext);

    /// <summary>Déchiffre.</summary>
    string Unprotect(string protectedValue);
}
