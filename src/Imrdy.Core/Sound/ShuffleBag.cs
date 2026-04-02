namespace Imrdy.Core.Sound;

/// <summary>
/// Generic shuffle bag: draws items randomly without repeats until exhausted,
/// then refills ensuring no consecutive duplicate from the last draw.
/// </summary>
public sealed class ShuffleBag<T> where T : notnull
{
    private readonly List<T> _items;
    private readonly List<T> _remaining;
    private readonly Random _random;
    private T? _lastDrawn;

    public ShuffleBag(IEnumerable<T> items, Random? random = null)
    {
        _items = new List<T>(items);
        _remaining = new List<T>();
        _random = random ?? Random.Shared;
    }

    /// <summary>
    /// Number of original items in the bag.
    /// </summary>
    public int Count => _items.Count;

    /// <summary>
    /// Draws the next item. Returns default if the bag has no items.
    /// Automatically refills when exhausted, avoiding consecutive duplicates.
    /// </summary>
    public T? Draw()
    {
        if (_items.Count == 0)
        {
            return default;
        }

        if (_remaining.Count == 0)
        {
            _remaining.AddRange(_items);
        }

        var index = _random.Next(_remaining.Count);
        var item = _remaining[index];

        // Avoid consecutive duplicates when alternatives exist
        if (_remaining.Count > 1 && _lastDrawn is not null
            && EqualityComparer<T>.Default.Equals(item, _lastDrawn))
        {
            // Pick the first non-matching item instead
            for (var i = 0; i < _remaining.Count; i++)
            {
                if (!EqualityComparer<T>.Default.Equals(_remaining[i], _lastDrawn))
                {
                    index = i;
                    item = _remaining[i];
                    break;
                }
            }
        }

        _remaining.RemoveAt(index);
        _lastDrawn = item;
        return item;
    }
}
