namespace ColdAudit.Shared.Contracts;

public sealed class EventBus
{
    private readonly List<object> _events = [];

    public void Publish<T>(T evt) where T : notnull
    {
        _events.Add(evt);
    }

    public IEnumerable<T> OfType<T>()
    {
        foreach (var evt in _events)
        {
            if (evt is T typed)
            {
                yield return typed;
            }
        }
    }

    public void Clear() => _events.Clear();
}
