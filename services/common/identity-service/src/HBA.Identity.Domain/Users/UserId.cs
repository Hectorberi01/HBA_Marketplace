namespace HBA.Identity.Domain.Users;

/// <summary>Identité forte d'un compte utilisateur.</summary>
public readonly record struct UserId(Guid Value)
{
    public static UserId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}
