namespace AutonomousSoftwareFactory.State;

public interface IStateStore
{
    void Set(string key, object value);
    T? Get<T>(string key);
    bool Has(string key);
    Dictionary<string, object> GetAll();
}
