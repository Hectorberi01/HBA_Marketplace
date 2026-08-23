namespace HBA.Marketplace.ReturnRefund.Infrastructure.Redis;

public sealed class ReturnPolicyCache
{
    private readonly Dictionary<string, object> _memory = new(StringComparer.OrdinalIgnoreCase);

    public T? Get<T>(string key) where T : class
        => _memory.TryGetValue(key, out var value) ? value as T : null;

    public void Set<T>(string key, T value) where T : class
        => _memory[key] = value;
}
