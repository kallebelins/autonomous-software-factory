namespace AutonomousSoftwareFactory.State;

using System.Text.Json;

public class InMemoryStateStore : IStateStore
{
    private readonly Dictionary<string, object> _store = new();

    public void Set(string key, object value)
    {
        _store[key] = value;
    }

    public T? Get<T>(string key)
    {
        if (!_store.TryGetValue(key, out var value))
            return default;

        if (value is T typed)
            return typed;

        // Handle JsonElement from checkpoint restoration
        if (value is JsonElement element)
            return element.Deserialize<T>();

        return default;
    }

    public bool Has(string key) => _store.ContainsKey(key);

    public Dictionary<string, object> GetAll() => new(_store);
}
