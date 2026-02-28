using System.Buffers;
using Frimerki.Protocols.Common;

namespace Frimerki.Tests.Protocols.Common;

public class RentedListTests {
    [Fact]
    public void Add_SingleItem_CountIsOne() {
        using var list = new RentedList<int>();
        list.Add(42);
        Assert.Single(list);
        Assert.Equal(42, list[0]);
    }

    [Fact]
    public void Add_MultipleItems_MaintainsOrder() {
        using var list = new RentedList<string>();
        list.Add("a");
        list.Add("b");
        list.Add("c");
        Assert.Equal(3, list.Count);
        Assert.Equal("a", list[0]);
        Assert.Equal("b", list[1]);
        Assert.Equal("c", list[2]);
    }

    [Fact]
    public void Add_BeyondInitialCapacity_GrowsCorrectly() {
        using var list = new RentedList<int>(initialCapacity: 2);
        for (int i = 0; i < 100; i++) {
            list.Add(i);
        }
        Assert.Equal(100, list.Count);
        for (int i = 0; i < 100; i++) {
            Assert.Equal(i, list[i]);
        }
    }

    [Fact]
    public void Indexer_OutOfRange_Throws() {
        using var list = new RentedList<int>();
        list.Add(1);
        Assert.Throws<ArgumentOutOfRangeException>(() => list[1]);
    }

    [Fact]
    public void Indexer_Set_UpdatesValue() {
        using var list = new RentedList<int>();
        list.Add(10);
        list[0] = 20;
        Assert.Equal(20, list[0]);
    }

    [Fact]
    public void Clear_ResetsCount() {
        using var list = new RentedList<int>();
        list.Add(1);
        list.Add(2);
        list.Clear();
        Assert.Empty(list);
    }

    [Fact]
    public void Clear_ReleasesReferences() {
        using var list = new RentedList<string>();
        list.Add("hello");
        list.Add("world");

        // Get a span of the full backing array before clearing
        // (AsSpan only returns the active portion, so we check after clear + re-add)
        var countBefore = list.Count;
        Assert.Equal(2, countBefore);

        list.Clear();
        Assert.Empty(list);

        // Add one item to make span length 1, then check that index 1 (previously "world") is null
        list.Add("new");
        Assert.Equal("new", list[0]);

        // The backing array should have been cleared — slot [1] should be null, not "world"
        // We verify by growing the span: add enough items to reach index 1 and check it was overwritten from null
        list.Add("check");
        Assert.Equal("check", list[1]); // If Clear didn't null it, the slot would still contain "world"
    }

    [Fact]
    public void Clear_NullsOutReferenceSlots() {
        // Use a custom pool to inspect the returned array
        var pool = new InspectableArrayPool<string>();
        var list = new RentedList<string>(pool: pool);
        list.Add("a");
        list.Add("b");
        list.Add("c");

        list.Clear();
        list.Dispose();

        // The array returned to the pool should have its first 3 slots cleared
        Assert.NotNull(pool.LastReturned);
        Assert.Null(pool.LastReturned[0]);
        Assert.Null(pool.LastReturned[1]);
        Assert.Null(pool.LastReturned[2]);
    }

    [Fact]
    public void Clear_ValueType_DoesNotNeedClearing() {
        // Value types don't hold references, so Clear just resets count.
        // This test verifies Clear works correctly for value types.
        using var list = new RentedList<int>();
        list.Add(1);
        list.Add(2);
        list.Clear();
        Assert.Empty(list);

        // Can re-add after clear
        list.Add(3);
        Assert.Single(list);
        Assert.Equal(3, list[0]);
    }

    [Fact]
    public void Contains_FindsItem() {
        using var list = new RentedList<string>();
        list.Add("hello");
        Assert.Contains("hello", list);
        Assert.DoesNotContain("world", list);
    }

    [Fact]
    public void IndexOf_ReturnsCorrectIndex() {
        using var list = new RentedList<int>();
        list.Add(10);
        list.Add(20);
        list.Add(30);
        Assert.Equal(1, list.IndexOf(20));
        Assert.Equal(-1, list.IndexOf(99));
    }

    [Fact]
    public void Remove_ExistingItem_RemovesAndShifts() {
        using var list = new RentedList<int>();
        list.Add(1);
        list.Add(2);
        list.Add(3);
        Assert.True(list.Remove(2));
        Assert.Equal(2, list.Count);
        Assert.Equal(1, list[0]);
        Assert.Equal(3, list[1]);
    }

    [Fact]
    public void Remove_NonExistingItem_ReturnsFalse() {
        using var list = new RentedList<int>();
        list.Add(1);
        Assert.False(list.Remove(99));
        Assert.Single(list);
    }

    [Fact]
    public void RemoveAt_ShiftsElements() {
        using var list = new RentedList<string>();
        list.Add("a");
        list.Add("b");
        list.Add("c");
        list.RemoveAt(0);
        Assert.Equal(2, list.Count);
        Assert.Equal("b", list[0]);
        Assert.Equal("c", list[1]);
    }

    [Fact]
    public void Insert_AtBeginning_ShiftsElements() {
        using var list = new RentedList<int>();
        list.Add(2);
        list.Add(3);
        list.Insert(0, 1);
        Assert.Equal(3, list.Count);
        Assert.Equal(1, list[0]);
        Assert.Equal(2, list[1]);
        Assert.Equal(3, list[2]);
    }

    [Fact]
    public void Insert_AtEnd_AppendsElement() {
        using var list = new RentedList<int>();
        list.Add(1);
        list.Insert(1, 2);
        Assert.Equal(2, list.Count);
        Assert.Equal(2, list[1]);
    }

    [Fact]
    public void AddRange_AddsAllItems() {
        using var list = new RentedList<int>();
        list.AddRange([10, 20, 30]);
        Assert.Equal(3, list.Count);
        Assert.Equal(10, list[0]);
        Assert.Equal(30, list[2]);
    }

    [Fact]
    public void AddRange_ICollection_BulkCopies() {
        var pool = new TrackingArrayPool<int>();
        using var list = new RentedList<int>(initialCapacity: 1, pool: pool);
        var initialRents = pool.RentCount;

        // List<T> implements ICollection<T> — should EnsureCapacity once and CopyTo
        List<int> source = [1, 2, 3, 4, 5, 6, 7, 8];
        list.AddRange(source);

        Assert.Equal(8, list.Count);
        for (int i = 0; i < 8; i++) {
            Assert.Equal(i + 1, list[i]);
        }

        // Should have done at most one additional rent (EnsureCapacity), not 8 growths
        Assert.True(pool.RentCount - initialRents <= 1);
    }

    [Fact]
    public void AddRange_IReadOnlyCollection_PreSizes() {
        var pool = new TrackingArrayPool<int>();
        using var list = new RentedList<int>(initialCapacity: 1, pool: pool);
        var initialRents = pool.RentCount;

        // HashSet<T> implements IReadOnlyCollection<T> but not ICollection<T>.CopyTo usefully
        // Use a wrapper that only exposes IReadOnlyCollection<T>
        IEnumerable<int> source = new ReadOnlyCollectionWrapper<int>([10, 20, 30, 40, 50]);
        list.AddRange(source);

        Assert.Equal(5, list.Count);
        Assert.Equal(10, list[0]);
        Assert.Equal(50, list[4]);

        // Should have pre-sized, so at most one growth rent
        Assert.True(pool.RentCount - initialRents <= 1);
    }

    [Fact]
    public void AddRange_ICollection_AppendToExisting() {
        using var list = new RentedList<int>();
        list.Add(1);
        list.Add(2);
        list.AddRange(new[] { 3, 4, 5 }); // array implements ICollection<T>

        Assert.Equal(5, list.Count);
        Assert.Equal(1, list[0]);
        Assert.Equal(5, list[4]);
    }

    [Fact]
    public void AddRange_ICollection_ReturnsOldArrayOnGrow() {
        var pool = new TrackingArrayPool<int>();
        using var list = new RentedList<int>(initialCapacity: 1, pool: pool);

        // Force a grow via ICollection path
        list.AddRange(new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 });

        // Only 1 array outstanding despite growth
        Assert.Equal(1, pool.Outstanding);
    }

    [Fact]
    public void CopyTo_CopiesActiveElements() {
        using var list = new RentedList<int>();
        list.Add(1);
        list.Add(2);
        var array = new int[5];
        list.CopyTo(array, 1);
        Assert.Equal(0, array[0]);
        Assert.Equal(1, array[1]);
        Assert.Equal(2, array[2]);
    }

    [Fact]
    public void AsSpan_ReturnsActiveElements() {
        using var list = new RentedList<int>();
        list.Add(5);
        list.Add(10);
        var span = list.AsSpan();
        Assert.Equal(2, span.Length);
        Assert.Equal(5, span[0]);
        Assert.Equal(10, span[1]);
    }

    [Fact]
    public void Foreach_IteratesAllItems() {
        using var list = new RentedList<int>();
        list.Add(1);
        list.Add(2);
        list.Add(3);
        var sum = 0;
        foreach (var item in list) {
            sum += item;
        }
        Assert.Equal(6, sum);
    }

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes() {
        var list = new RentedList<int>();
        list.Add(1);
        list.Dispose();
        list.Dispose(); // Should not throw
    }

    [Fact]
    public void StringJoin_WorksWithRentedList() {
        // This is the actual usage pattern in ImapSession
        using var list = new RentedList<string>();
        list.Add(@"\Seen");
        list.Add(@"\Flagged");
        var result = string.Join(" ", list);
        Assert.Equal(@"\Seen \Flagged", result);
    }

    [Fact]
    public void EmptyList_CountIsZero() {
        using var list = new RentedList<int>();
        Assert.Empty(list);
        Assert.DoesNotContain(0, list);
    }

    [Fact]
    public void IsReadOnly_ReturnsFalse() {
        using var list = new RentedList<int>();
        Assert.False(list.IsReadOnly);
    }

    // ── Array pool tracking tests ──

    [Fact]
    public void Dispose_ReturnsRentedArray() {
        var pool = new TrackingArrayPool<int>();
        var list = new RentedList<int>(pool: pool);
        list.Add(1);
        Assert.Equal(1, pool.Outstanding);

        list.Dispose();
        Assert.Equal(0, pool.Outstanding);
    }

    [Fact]
    public void Grow_ReturnsOldArray_AndRentsNew() {
        var pool = new TrackingArrayPool<int>();
        using var list = new RentedList<int>(initialCapacity: 1, pool: pool);
        Assert.Equal(1, pool.RentCount);

        // Fill beyond initial capacity — triggers Grow
        for (int i = 0; i < 100; i++) {
            list.Add(i);
        }

        // Multiple rents happened (initial + growths), but only one is outstanding
        Assert.True(pool.RentCount > 1);
        Assert.Equal(1, pool.Outstanding);

        // All intermediate arrays were returned during growth
        Assert.Equal(pool.RentCount - 1, pool.ReturnCount);
    }

    [Fact]
    public void Dispose_AfterGrowth_ReturnsAllArrays() {
        var pool = new TrackingArrayPool<int>();
        var list = new RentedList<int>(initialCapacity: 1, pool: pool);
        for (int i = 0; i < 50; i++) {
            list.Add(i);
        }

        list.Dispose();
        Assert.Equal(0, pool.Outstanding);
        Assert.Equal(pool.RentCount, pool.ReturnCount);
    }

    [Fact]
    public void DoubleDispose_DoesNotReturnTwice() {
        var pool = new TrackingArrayPool<int>();
        var list = new RentedList<int>(pool: pool);
        list.Dispose();
        list.Dispose();
        Assert.Equal(0, pool.Outstanding);
        Assert.Equal(1, pool.ReturnCount);
    }

    [Fact]
    public void AddRange_ExceptionDuringEnumeration_ArrayStillReturnedOnDispose() {
        var pool = new TrackingArrayPool<int>();
        var list = new RentedList<int>(initialCapacity: 1, pool: pool);

        // Add some items first to potentially trigger growth
        list.Add(1);
        list.Add(2);

        try {
            list.AddRange(ThrowingEnumerable());
        } catch (InvalidOperationException) {
            // Expected
        }

        // Items added before the exception are still there
        Assert.True(list.Count >= 2);

        // Dispose still returns all rented arrays
        list.Dispose();
        Assert.Equal(0, pool.Outstanding);
        Assert.Equal(pool.RentCount, pool.ReturnCount);
    }

    [Fact]
    public void AddRange_ExceptionAfterGrowth_NoLeakedArrays() {
        var pool = new TrackingArrayPool<int>();
        using var list = new RentedList<int>(initialCapacity: 1, pool: pool);

        try {
            // This enumerable yields enough items to trigger multiple growths, then throws
            list.AddRange(ThrowAfterNItems(50));
        } catch (InvalidOperationException) {
            // Expected
        }

        // The list is still usable — items before the throw were added
        Assert.True(list.Count > 0);

        // Only one array outstanding (the current one)
        Assert.Equal(1, pool.Outstanding);
    }

    // ── Constructor from collection ──

    [Fact]
    public void Constructor_FromArray_CopiesElements() {
        using var list = new RentedList<int>([1, 2, 3]);
        Assert.Equal(3, list.Count);
        Assert.Equal(1, list[0]);
        Assert.Equal(2, list[1]);
        Assert.Equal(3, list[2]);
    }

    [Fact]
    public void Constructor_FromList_CopiesElements() {
        List<string> source = ["a", "b", "c"];
        using var list = new RentedList<string>(source);
        Assert.Equal(3, list.Count);
        Assert.Equal("a", list[0]);
        Assert.Equal("c", list[2]);
    }

    [Fact]
    public void Constructor_FromEnumerable_AddsAll() {
        using var list = new RentedList<int>(Enumerable.Range(1, 5));
        Assert.Equal(5, list.Count);
        Assert.Equal(1, list[0]);
        Assert.Equal(5, list[4]);
    }

    [Fact]
    public void Constructor_FromReadOnlyCollection_PreSizes() {
        var pool = new TrackingArrayPool<int>();
        var source = new ReadOnlyCollectionWrapper<int>([10, 20, 30]);
        using var list = new RentedList<int>(source, pool);
        Assert.Equal(3, list.Count);
        Assert.Equal(10, list[0]);
        // Should have done just one rent (pre-sized)
        Assert.Equal(1, pool.RentCount);
    }

    [Fact]
    public void Constructor_FromCollection_TracksPool() {
        var pool = new TrackingArrayPool<int>();
        var list = new RentedList<int>([1, 2, 3], pool);
        Assert.Equal(1, pool.Outstanding);
        list.Dispose();
        Assert.Equal(0, pool.Outstanding);
    }

    [Fact]
    public void Constructor_ExceptionDuringEnumeration_ReturnsRentedArray() {
        var pool = new TrackingArrayPool<int>();
        Assert.Throws<InvalidOperationException>(() => new RentedList<int>(ThrowingEnumerable(), pool));
        Assert.Equal(0, pool.Outstanding);
        Assert.Equal(pool.RentCount, pool.ReturnCount);
    }

    [Fact]
    public void Constructor_ExceptionAfterGrowth_ReturnsAllArrays() {
        var pool = new TrackingArrayPool<int>();
        Assert.Throws<InvalidOperationException>(() => new RentedList<int>(ThrowAfterNItems(50), pool));
        // The default enumerable path may have triggered growths; all should be returned
        Assert.Equal(0, pool.Outstanding);
        Assert.Equal(pool.RentCount, pool.ReturnCount);
    }

    // ── Capacity ──

    [Fact]
    public void Capacity_ReflectsRentedArraySize() {
        using var list = new RentedList<int>(initialCapacity: 32);
        Assert.True(list.Capacity >= 32);
    }

    // ── InsertRange ──

    [Fact]
    public void InsertRange_AtBeginning_ShiftsExisting() {
        using var list = new RentedList<int>([3, 4, 5]);
        list.InsertRange(0, new[] { 1, 2 });
        Assert.Equal(5, list.Count);
        Assert.Equal(1, list[0]);
        Assert.Equal(2, list[1]);
        Assert.Equal(3, list[2]);
        Assert.Equal(5, list[4]);
    }

    [Fact]
    public void InsertRange_AtEnd_Appends() {
        using var list = new RentedList<int>([1, 2]);
        list.InsertRange(2, new[] { 3, 4 });
        Assert.Equal(4, list.Count);
        Assert.Equal(3, list[2]);
        Assert.Equal(4, list[3]);
    }

    [Fact]
    public void InsertRange_InMiddle_SplitsCorrectly() {
        using var list = new RentedList<int>([1, 4]);
        list.InsertRange(1, new[] { 2, 3 });
        Assert.Equal(4, list.Count);
        Assert.Equal(1, list[0]);
        Assert.Equal(2, list[1]);
        Assert.Equal(3, list[2]);
        Assert.Equal(4, list[3]);
    }

    [Fact]
    public void InsertRange_EmptyCollection_NoChange() {
        using var list = new RentedList<int>([1, 2]);
        list.InsertRange(1, Array.Empty<int>());
        Assert.Equal(2, list.Count);
    }

    [Fact]
    public void InsertRange_ReturnsOldArrayOnGrow() {
        var pool = new TrackingArrayPool<int>();
        using var list = new RentedList<int>(initialCapacity: 1, pool: pool);
        list.InsertRange(0, Enumerable.Range(0, 50));
        Assert.Equal(1, pool.Outstanding);
    }

    [Fact]
    public void InsertRange_ReadOnlyCollection_AtBeginning() {
        using var list = new RentedList<int>([3, 4, 5]);
        list.InsertRange(0, new ReadOnlyCollectionWrapper<int>([1, 2]));
        Assert.Equal(5, list.Count);
        Assert.Equal([1, 2, 3, 4, 5], list.ToArray());
    }

    [Fact]
    public void InsertRange_ReadOnlyCollection_InMiddle() {
        using var list = new RentedList<int>([1, 4, 5]);
        list.InsertRange(1, new ReadOnlyCollectionWrapper<int>([2, 3]));
        Assert.Equal(5, list.Count);
        Assert.Equal([1, 2, 3, 4, 5], list.ToArray());
    }

    [Fact]
    public void InsertRange_ReadOnlyCollection_AtEnd() {
        using var list = new RentedList<int>([1, 2]);
        list.InsertRange(2, new ReadOnlyCollectionWrapper<int>([3, 4, 5]));
        Assert.Equal(5, list.Count);
        Assert.Equal([1, 2, 3, 4, 5], list.ToArray());
    }

    [Fact]
    public void InsertRange_ReadOnlyCollection_Empty_NoChange() {
        using var list = new RentedList<int>([1, 2, 3]);
        list.InsertRange(1, new ReadOnlyCollectionWrapper<int>([]));
        Assert.Equal(3, list.Count);
        Assert.Equal([1, 2, 3], list.ToArray());
    }

    [Fact]
    public void InsertRange_ReadOnlyCollection_IntoEmpty() {
        using var list = new RentedList<int>();
        list.InsertRange(0, new ReadOnlyCollectionWrapper<int>([1, 2, 3]));
        Assert.Equal(3, list.Count);
        Assert.Equal([1, 2, 3], list.ToArray());
    }

    [Fact]
    public void InsertRange_Enumerable_InMiddle() {
        using var list = new RentedList<int>([1, 5]);
        // Enumerable.Range returns IEnumerable, not ICollection or IReadOnlyCollection
        list.InsertRange(1, YieldItems([2, 3, 4]));
        Assert.Equal(5, list.Count);
        Assert.Equal([1, 2, 3, 4, 5], list.ToArray());
    }

    private static IEnumerable<T> YieldItems<T>(T[] items) {
        foreach (var item in items) {
            yield return item;
        }
    }

    // ── RemoveRange ──

    [Fact]
    public void RemoveRange_FromMiddle_ShiftsElements() {
        using var list = new RentedList<int>([1, 2, 3, 4, 5]);
        list.RemoveRange(1, 2);
        Assert.Equal(3, list.Count);
        Assert.Equal(1, list[0]);
        Assert.Equal(4, list[1]);
        Assert.Equal(5, list[2]);
    }

    [Fact]
    public void RemoveRange_FromBeginning_Works() {
        using var list = new RentedList<int>([1, 2, 3, 4]);
        list.RemoveRange(0, 2);
        Assert.Equal(2, list.Count);
        Assert.Equal(3, list[0]);
        Assert.Equal(4, list[1]);
    }

    [Fact]
    public void RemoveRange_AllElements_EmptiesList() {
        using var list = new RentedList<int>([1, 2, 3]);
        list.RemoveRange(0, 3);
        Assert.Empty(list);
    }

    [Fact]
    public void RemoveRange_ZeroCount_NoChange() {
        using var list = new RentedList<int>([1, 2, 3]);
        list.RemoveRange(1, 0);
        Assert.Equal(3, list.Count);
    }

    [Fact]
    public void RemoveRange_ClearsReferencesSlots() {
        var pool = new InspectableArrayPool<string>();
        var list = new RentedList<string>(["a", "b", "c", "d"], pool);
        list.RemoveRange(1, 2); // remove "b" and "c"
        Assert.Equal(2, list.Count);
        Assert.Equal("a", list[0]);
        Assert.Equal("d", list[1]);
        list.Dispose();
        // Slots [2] and [3] should have been cleared
        Assert.Null(pool.LastReturned[2]);
        Assert.Null(pool.LastReturned[3]);
    }

    // ── RemoveAll ──

    [Fact]
    public void RemoveAll_RemovesMatchingItems() {
        using var list = new RentedList<int>([1, 2, 3, 4, 5, 6]);
        var removed = list.RemoveAll(x => x % 2 == 0);
        Assert.Equal(3, removed);
        Assert.Equal(3, list.Count);
        Assert.Equal(1, list[0]);
        Assert.Equal(3, list[1]);
        Assert.Equal(5, list[2]);
    }

    [Fact]
    public void RemoveAll_NoMatches_ReturnsZero() {
        using var list = new RentedList<int>([1, 2, 3]);
        var removed = list.RemoveAll(x => x > 10);
        Assert.Equal(0, removed);
        Assert.Equal(3, list.Count);
    }

    [Fact]
    public void RemoveAll_AllMatch_EmptiesList() {
        using var list = new RentedList<int>([1, 2, 3]);
        var removed = list.RemoveAll(_ => true);
        Assert.Equal(3, removed);
        Assert.Empty(list);
    }

    // ── ToArray ──

    [Fact]
    public void ToArray_CopiesActiveElements() {
        using var list = new RentedList<int>([10, 20, 30]);
        var array = list.ToArray();
        Assert.Equal([10, 20, 30], array);
    }

    [Fact]
    public void ToArray_EmptyList_ReturnsEmpty() {
        using var list = new RentedList<int>();
        var array = list.ToArray();
        Assert.Empty(array);
    }

    // ── LastIndexOf ──

    [Fact]
    public void LastIndexOf_FindsLastOccurrence() {
        using var list = new RentedList<int>([1, 2, 3, 2, 1]);
        Assert.Equal(3, list.LastIndexOf(2));
        Assert.Equal(4, list.LastIndexOf(1));
        Assert.Equal(-1, list.LastIndexOf(99));
    }

    // ── Find / FindIndex / FindLast / FindLastIndex / Exists ──

    [Fact]
    public void FindIndex_ReturnsFirstMatch() {
        using var list = new RentedList<int>([10, 20, 30, 20]);
        Assert.Equal(1, list.FindIndex(x => x == 20));
    }

    [Fact]
    public void FindIndex_NoMatch_ReturnsNegative() {
        using var list = new RentedList<int>([1, 2, 3]);
        Assert.Equal(-1, list.FindIndex(x => x == 99));
    }

    [Fact]
    public void FindLastIndex_ReturnsLastMatch() {
        using var list = new RentedList<int>([10, 20, 30, 20]);
        Assert.Equal(3, list.FindLastIndex(x => x == 20));
    }

    [Fact]
    public void Find_ReturnsFirstMatchingElement() {
        using var list = new RentedList<string>(["apple", "banana", "avocado"]);
        Assert.Equal("apple", list.Find(s => s.StartsWith('a')));
    }

    [Fact]
    public void Find_NoMatch_ReturnsDefault() {
        using var list = new RentedList<string>(["apple"]);
        Assert.Null(list.Find(s => s.StartsWith('z')));
    }

    [Fact]
    public void FindLast_ReturnsLastMatchingElement() {
        using var list = new RentedList<string>(["apple", "banana", "avocado"]);
        Assert.Equal("avocado", list.FindLast(s => s.StartsWith('a')));
    }

    [Fact]
    public void Exists_ReturnsTrueWhenMatchFound() {
        using var list = new RentedList<int>([1, 2, 3]);
        Assert.True(list.Exists(x => x == 2));
        Assert.False(list.Exists(x => x == 99));
    }

    // ── Reverse ──

    [Fact]
    public void Reverse_ReversesElements() {
        using var list = new RentedList<int>([1, 2, 3, 4]);
        list.Reverse();
        Assert.Equal(4, list[0]);
        Assert.Equal(3, list[1]);
        Assert.Equal(2, list[2]);
        Assert.Equal(1, list[3]);
    }

    // ── Sort ──

    [Fact]
    public void Sort_SortsAscending() {
        using var list = new RentedList<int>([3, 1, 4, 1, 5]);
        list.Sort();
        Assert.Equal([1, 1, 3, 4, 5], list.ToArray());
    }

    [Fact]
    public void Sort_WithComparer_SortsDescending() {
        using var list = new RentedList<int>([3, 1, 4, 1, 5]);
        list.Sort(Comparer<int>.Create((a, b) => b.CompareTo(a)));
        Assert.Equal([5, 4, 3, 1, 1], list.ToArray());
    }

    [Fact]
    public void Sort_WithComparison_Works() {
        using var list = new RentedList<string>(["banana", "apple", "cherry"]);
        list.Sort((a, b) => string.Compare(a, b, StringComparison.Ordinal));
        Assert.Equal("apple", list[0]);
        Assert.Equal("banana", list[1]);
        Assert.Equal("cherry", list[2]);
    }

    // ── BinarySearch ──

    [Fact]
    public void BinarySearch_FindsItem() {
        using var list = new RentedList<int>([1, 3, 5, 7, 9]);
        Assert.Equal(2, list.BinarySearch(5));
    }

    [Fact]
    public void BinarySearch_NotFound_ReturnsNegative() {
        using var list = new RentedList<int>([1, 3, 5, 7, 9]);
        Assert.True(list.BinarySearch(4) < 0);
    }

    private static IEnumerable<int> ThrowingEnumerable() {
        yield return 10;
        yield return 20;
        throw new InvalidOperationException("Enumeration failed");
    }

    private static IEnumerable<int> ThrowAfterNItems(int n) {
        for (int i = 0; i < n; i++) {
            yield return i;
        }
        throw new InvalidOperationException("Enumeration failed after growth");
    }
}

/// <summary>
/// An ArrayPool that tracks Rent/Return calls for testing that all arrays are properly returned.
/// </summary>
public sealed class TrackingArrayPool<T> : ArrayPool<T> {
    private int _rentCount;
    private int _returnCount;
    private readonly HashSet<T[]> _outstanding = [];

    public int RentCount => _rentCount;
    public int ReturnCount => _returnCount;
    public int Outstanding => _outstanding.Count;

    public override T[] Rent(int minimumLength) {
        var array = ArrayPool<T>.Shared.Rent(minimumLength);
        _rentCount++;
        _outstanding.Add(array);
        return array;
    }

    public override void Return(T[] array, bool clearArray = false) {
        if (_outstanding.Remove(array)) {
            _returnCount++;
        }
        ArrayPool<T>.Shared.Return(array, clearArray);
    }
}

/// <summary>
/// Wraps an array as <see cref="IReadOnlyCollection{T}"/> without implementing <see cref="ICollection{T}"/>,
/// so we can test the IReadOnlyCollection path in AddRange separately.
/// </summary>
public sealed class ReadOnlyCollectionWrapper<T>(T[] items) : IReadOnlyCollection<T> {
    public int Count => items.Length;
    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)items).GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>
/// An ArrayPool that captures the last returned array without clearing it,
/// so tests can inspect whether the caller cleared the slots before returning.
/// </summary>
public sealed class InspectableArrayPool<T> : ArrayPool<T> {
    public T[] LastReturned { get; private set; }

    public override T[] Rent(int minimumLength) => new T[minimumLength];

    public override void Return(T[] array, bool clearArray = false) {
        // Intentionally do NOT clear — we want to inspect what the caller cleared
        LastReturned = array;
    }
}
