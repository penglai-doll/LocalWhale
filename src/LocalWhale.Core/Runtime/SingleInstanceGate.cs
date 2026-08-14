namespace LocalWhale.Core.Runtime;

public sealed class SingleInstanceGate : IDisposable
{
    private readonly Mutex _mutex;

    public SingleInstanceGate(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _mutex = new Mutex(initiallyOwned: false, name, out var createdNew);
        IsPrimary = createdNew;
    }

    public bool IsPrimary { get; }

    public void Dispose() => _mutex.Dispose();
}
