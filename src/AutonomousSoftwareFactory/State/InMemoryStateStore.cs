namespace AutonomousSoftwareFactory.State;

public class InMemoryStateStore : IStateStore
{
    private readonly Dictionary<string, object> _store = new();

    public void Set(string key, object value)
    {
        _store[key] = value;
    }

    public T? Get<T>(string key)
    {
        if (_store.TryGetValue(key, out var value) && value is T typed)
            return typed;

        return default;
    }

    public bool Has(string key) => _store.ContainsKey(key);
}
