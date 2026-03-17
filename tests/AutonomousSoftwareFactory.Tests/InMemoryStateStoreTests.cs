namespace AutonomousSoftwareFactory.Tests;

using AutonomousSoftwareFactory.State;

public class InMemoryStateStoreTests
{
    private readonly InMemoryStateStore _store = new();

    [Fact]
    public void Set_And_Get_ReturnsStoredValue()
    {
        _store.Set("key1", "value1");

        var result = _store.Get<string>("key1");

        Assert.Equal("value1", result);
    }

    [Fact]
    public void Set_OverwritesExistingValue()
    {
        _store.Set("key1", "original");
        _store.Set("key1", "updated");

        Assert.Equal("updated", _store.Get<string>("key1"));
    }

    [Fact]
    public void Get_NonExistentKey_ReturnsDefault()
    {
        var result = _store.Get<string>("missing");

        Assert.Null(result);
    }

    [Fact]
    public void Get_TypeMismatch_ReturnsDefault()
    {
        _store.Set("key1", 42);

        var result = _store.Get<string>("key1");

        Assert.Null(result);
    }

    [Fact]
    public void Get_ValueType_ReturnsStoredValue()
    {
        _store.Set("count", 10);

        var result = _store.Get<int>("count");

        Assert.Equal(10, result);
    }

    [Fact]
    public void Get_ValueType_NonExistentKey_ReturnsDefaultValue()
    {
        var result = _store.Get<int>("missing");

        Assert.Equal(0, result);
    }

    [Fact]
    public void Has_ExistingKey_ReturnsTrue()
    {
        _store.Set("key1", "value1");

        Assert.True(_store.Has("key1"));
    }

    [Fact]
    public void Has_NonExistentKey_ReturnsFalse()
    {
        Assert.False(_store.Has("missing"));
    }

    [Fact]
    public void Get_ComplexObject_ReturnsCorrectType()
    {
        var data = new Dictionary<string, string> { ["a"] = "b" };
        _store.Set("dict", data);

        var result = _store.Get<Dictionary<string, string>>("dict");

        Assert.NotNull(result);
        Assert.Equal("b", result!["a"]);
    }
}
