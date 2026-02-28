using System.Buffers;
using System.Collections;
using System.Runtime.CompilerServices;

namespace Frimerki.Protocols.Common;

/// <summary>
/// A list backed by an <see cref="ArrayPool{T}"/>-rented array. Implements <see cref="IDisposable"/> to return the array.
/// Use in hot paths where short-lived <see cref="List{T}"/> allocations cause GC pressure.
/// </summary>
public sealed class RentedList<T> : IList<T>, IReadOnlyList<T>, IDisposable {
    private readonly ArrayPool<T> _pool;
    private T[] _items;
    private int _count;
    private bool _disposed;

    public RentedList(int initialCapacity = 16, ArrayPool<T> pool = null) {
        _pool = pool ?? ArrayPool<T>.Shared;
        _items = _pool.Rent(initialCapacity);
    }

    public RentedList(IEnumerable<T> collection, ArrayPool<T> pool = null) {
        _pool = pool ?? ArrayPool<T>.Shared;
        try {
            if (collection is ICollection<T> c) {
                _items = _pool.Rent(Math.Max(c.Count, 16));
                c.CopyTo(_items, 0);
                _count = c.Count;
                return;
            }
            _items = _pool.Rent(collection is IReadOnlyCollection<T> roc ? Math.Max(roc.Count, 16) : 16);
            foreach (var item in collection) {
                Add(item);
            }
        } catch {
            _pool.Return(_items, clearArray: RuntimeHelpers.IsReferenceOrContainsReferences<T>());
            throw;
        }
    }

    public int Count => _count;
    public bool IsReadOnly => false;

    public int Capacity => _items.Length;

    public T this[int index] {
        get {
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _count);
            return _items[index];
        }
        set {
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _count);
            _items[index] = value;
        }
    }

    public void Add(T item) {
        if (_count == _items.Length) {
            Grow();
        }
        _items[_count++] = item;
    }

    public void AddRange(IEnumerable<T> items) {
        switch (items) {
            case ICollection<T> collection: {
                    EnsureCapacity(_count + collection.Count);
                    collection.CopyTo(_items, _count);
                    _count += collection.Count;
                    return;
                }
            case IReadOnlyCollection<T> roc:
                EnsureCapacity(_count + roc.Count);
                break;
        }
        foreach (var item in items) {
            Add(item);
        }
    }

    private void EnsureCapacity(int capacity) {
        if (capacity < 0) {
            throw new OutOfMemoryException("RentedList capacity overflow.");
        }
        if (capacity <= _items.Length) {
            return;
        }

        var newSize = _items.Length;
        while (newSize < capacity) {
            newSize = GrowSize(newSize);
        }
        var newArray = _pool.Rent(newSize);
        Array.Copy(_items, newArray, _count);
        _pool.Return(_items, clearArray: RuntimeHelpers.IsReferenceOrContainsReferences<T>());
        _items = newArray;
    }

    public void Clear() {
        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>()) {
            Array.Clear(_items, 0, _count);
        }
        _count = 0;
    }

    public bool Contains(T item) => IndexOf(item) >= 0;

    public int IndexOf(T item) {
        var comparer = EqualityComparer<T>.Default;
        for (int i = 0; i < _count; i++) {
            if (comparer.Equals(_items[i], item)) {
                return i;
            }
        }
        return -1;
    }

    public void Insert(int index, T item) {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(index, _count);
        if (_count == _items.Length) {
            Grow();
        }
        if (index < _count) {
            Array.Copy(_items, index, _items, index + 1, _count - index);
        }
        _items[index] = item;
        _count++;
    }

    public void InsertRange(int index, IEnumerable<T> items) {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(index, _count);
        if (items is ICollection<T> c) {
            var newCount = _count + c.Count;
            EnsureCapacity(newCount);
            if (index < _count) {
                Array.Copy(_items, index, _items, index + newCount - _count, _count - index);
            }
            c.CopyTo(_items, index);
            _count = newCount;
        } else if (items is IReadOnlyCollection<T> roc) {
            EnsureCapacity(_count + roc.Count);
            if (index < _count) {
                Array.Copy(_items, index, _items, index + roc.Count, _count - index);
            }
            foreach (var item in roc) {
                _items[index++] = item;
            }
            _count += roc.Count;
        } else {
            foreach (var item in items) {
                Insert(index++, item);
            }
        }
    }

    public bool Remove(T item) {
        var index = IndexOf(item);
        if (index < 0) {
            return false;
        }

        RemoveAt(index);
        return true;
    }

    public void RemoveAt(int index) {
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _count);
        if (index < --_count) {
            Array.Copy(_items, index + 1, _items, index, _count - index);
        }
        _items[_count] = default;
    }

    public void RemoveRange(int index, int count) {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(index + count, _count);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (count == 0) {
            return;
        }

        var tail = _count - index - count;
        if (tail > 0) {
            Array.Copy(_items, index + count, _items, index, tail);
        }
        var oldCount = _count;
        _count -= count;
        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>()) {
            Array.Clear(_items, _count, oldCount - _count);
        }
    }

    public int RemoveAll(Predicate<T> match) {
        int freeIndex = 0;
        while (freeIndex < _count && !match(_items[freeIndex])) {
            freeIndex++;
        }
        if (freeIndex >= _count) {
            return 0;
        }

        int current = freeIndex + 1;
        while (current < _count) {
            if (!match(_items[current])) {
                _items[freeIndex++] = _items[current];
            }
            current++;
        }

        var removed = _count - freeIndex;
        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>()) {
            Array.Clear(_items, freeIndex, removed);
        }
        _count = freeIndex;
        return removed;
    }

    public void CopyTo(T[] array, int arrayIndex) =>
        Array.Copy(_items, 0, array, arrayIndex, _count);

    /// <summary>Returns a Span over the active portion of the list.</summary>
    public Span<T> AsSpan() => _items.AsSpan(0, _count);

    public T[] ToArray() {
        if (_count == 0) {
            return [];
        }

        var array = new T[_count];
        Array.Copy(_items, array, _count);
        return array;
    }

    public int LastIndexOf(T item) {
        var comparer = EqualityComparer<T>.Default;
        for (int i = _count - 1; i >= 0; i--) {
            if (comparer.Equals(_items[i], item)) {
                return i;
            }
        }
        return -1;
    }

    public int FindIndex(Predicate<T> match) {
        for (int i = 0; i < _count; i++) {
            if (match(_items[i])) {
                return i;
            }
        }
        return -1;
    }

    public int FindLastIndex(Predicate<T> match) {
        for (int i = _count - 1; i >= 0; i--) {
            if (match(_items[i])) {
                return i;
            }
        }
        return -1;
    }

    public T Find(Predicate<T> match) {
        var index = FindIndex(match);
        return index >= 0 ? _items[index] : default;
    }

    public T FindLast(Predicate<T> match) {
        var index = FindLastIndex(match);
        return index >= 0 ? _items[index] : default;
    }

    public bool Exists(Predicate<T> match) => FindIndex(match) >= 0;

    public void Reverse() => AsSpan().Reverse();

    public void Sort() => AsSpan().Sort();

    public void Sort(IComparer<T> comparer) => AsSpan().Sort(comparer);

    public void Sort(Comparison<T> comparison) => AsSpan().Sort(comparison);

    public int BinarySearch(T item) => BinarySearch(item, null);

    public int BinarySearch(T item, IComparer<T> comparer) =>
        Array.BinarySearch(_items, 0, _count, item, comparer);

    public Enumerator GetEnumerator() => new(this);
    IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private void Grow() {
        var newSize = GrowSize(_items.Length);
        var newArray = _pool.Rent(newSize);
        Array.Copy(_items, newArray, _count);
        _pool.Return(_items, clearArray: RuntimeHelpers.IsReferenceOrContainsReferences<T>());
        _items = newArray;
    }

    private static int GrowSize(int currentSize) {
        if (currentSize >= Array.MaxLength) {
            throw new OutOfMemoryException("RentedList capacity overflow.");
        }
        var newSize = currentSize * 2;
        return (uint)newSize > Array.MaxLength ? Array.MaxLength : newSize;
    }

    public void Dispose() {
        if (!_disposed) {
            _disposed = true;
            _pool.Return(_items, clearArray: RuntimeHelpers.IsReferenceOrContainsReferences<T>());
            _items = null;
        }
    }

    /// <summary>Struct enumerator to avoid allocations during foreach.</summary>
    public struct Enumerator(RentedList<T> list) : IEnumerator<T> {
        private int _index = -1;

        public T Current => list._items[_index];
        object IEnumerator.Current => Current;
        public bool MoveNext() => ++_index < list._count;
        public void Reset() => _index = -1;
        public void Dispose() { }
    }
}
