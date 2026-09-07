namespace HBA.Shared.Domain.Primitives;

/// <summary>Value Object : objet sans identité, défini par ses valeurs (Money, Slug…).</summary>
public abstract class ValueObject : IEquatable<ValueObject>
{
    protected abstract IEnumerable<object?> GetAtomicValues();

    /// <summary>SANS CES DEUX OPÉRATEURS, `==` COMPARAIT LES RÉFÉRENCES.</summary>
    public static bool operator ==(ValueObject? left, ValueObject? right)
        => left is null ? right is null : left.Equals(right);

    public static bool operator !=(ValueObject? left, ValueObject? right) => !(left == right);

    public bool Equals(ValueObject? other)
        => other is not null && GetType() == other.GetType() && ValuesAreEqual(other);

    public override bool Equals(object? obj) => obj is ValueObject other && Equals(other);

    public override int GetHashCode()
        => GetAtomicValues().Aggregate(default(int), (hash, value) => HashCode.Combine(hash, value));

    private bool ValuesAreEqual(ValueObject other)
        => GetAtomicValues().SequenceEqual(other.GetAtomicValues());
}
