using System.IO;
using System.Windows.Ink;

namespace PaperNote.Desktop.Services;

public sealed class InkHistoryService
{
    private readonly Stack<byte[]> _undo = new();
    private readonly Stack<byte[]> _redo = new();
    private readonly int _capacity;

    public InkHistoryService(int capacity = 200)
    {
        _capacity = capacity;
    }

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public void Record(StrokeCollection strokes)
    {
        _undo.Push(Serialize(strokes));
        _redo.Clear();
        TrimToCapacity(_undo);
    }

    public StrokeCollection? Undo(StrokeCollection current)
    {
        if (_undo.Count == 0)
        {
            return null;
        }

        _redo.Push(Serialize(current));
        TrimToCapacity(_redo);
        return Deserialize(_undo.Pop());
    }

    public StrokeCollection? Redo(StrokeCollection current)
    {
        if (_redo.Count == 0)
        {
            return null;
        }

        _undo.Push(Serialize(current));
        TrimToCapacity(_undo);
        return Deserialize(_redo.Pop());
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }

    public static byte[] Serialize(StrokeCollection strokes)
    {
        using var stream = new MemoryStream();
        strokes.Save(stream, true);
        return stream.ToArray();
    }

    public static StrokeCollection Deserialize(byte[] data)
    {
        if (data.Length == 0)
        {
            return new StrokeCollection();
        }

        using var stream = new MemoryStream(data, writable: false);
        return new StrokeCollection(stream);
    }

    private void TrimToCapacity(Stack<byte[]> stack)
    {
        if (stack.Count <= _capacity)
        {
            return;
        }

        var newestFirst = stack.ToArray();
        stack.Clear();
        for (var index = Math.Min(_capacity, newestFirst.Length) - 1; index >= 0; index--)
        {
            stack.Push(newestFirst[index]);
        }
    }
}

