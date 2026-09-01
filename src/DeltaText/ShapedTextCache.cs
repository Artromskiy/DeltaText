using System.Diagnostics.CodeAnalysis;
using Delta.Text.Contract;

namespace Delta.Text;

/// <summary>Small bounded cache for immutable, frequently repeated shape results.</summary>
internal sealed class ShapedTextCache
{
    private const int Capacity = 256;
    private readonly Dictionary<Key, ShapedText> _items = new();
    private readonly Queue<Key> _order = new();

    internal bool TryGet(in Key key, [NotNullWhen(true)] out ShapedText? shaped)
        => _items.TryGetValue(key, out shaped);

    internal void Add(in Key key, ShapedText shaped)
    {
        if (_items.ContainsKey(key))
        {
            return;
        }

        _items.Add(key, shaped);
        _order.Enqueue(key);
        while (_items.Count > Capacity && _order.TryDequeue(out var oldest))
        {
            _items.Remove(oldest);
        }
    }

    internal readonly record struct Key(
        string Text,
        FontInstanceId Font,
        float PixelsPerEm,
        TextDirection Direction,
        uint ScriptValue);
}
