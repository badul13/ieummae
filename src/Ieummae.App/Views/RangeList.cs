using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;

namespace Ieummae.App.Views;

// 묶음 추가 목록 - 커밋 수천 개를 알림 한 번으로 붙임 (하나씩 알리면 목록이 매번 다시 계산)
public sealed class RangeList<T> : IList<T>, IReadOnlyList<T>, IList, INotifyCollectionChanged, INotifyPropertyChanged
{
    readonly List<T> _items = [];

    public event NotifyCollectionChangedEventHandler? CollectionChanged;
    public event PropertyChangedEventHandler? PropertyChanged;

    public void AddRange(IReadOnlyList<T> items)
    {
        if (items.Count == 0) return;
        int start = _items.Count;
        _items.AddRange(items);
        CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, (IList)items.ToList(), start));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Count)));
    }

    public void Reset(IEnumerable<T> items)
    {
        _items.Clear();
        _items.AddRange(items);
        CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Count)));
    }

    public void Clear() => Reset([]);

    public T this[int index] { get => _items[index]; set => throw new NotSupportedException(); }
    object? IList.this[int index] { get => _items[index]; set => throw new NotSupportedException(); }
    public int Count => _items.Count;
    public bool IsReadOnly => true;
    bool IList.IsFixedSize => false;
    bool ICollection.IsSynchronized => false;
    object ICollection.SyncRoot => this;
    public int IndexOf(T item) => _items.IndexOf(item);
    int IList.IndexOf(object? value) => value is T t ? _items.IndexOf(t) : -1;
    public bool Contains(T item) => _items.Contains(item);
    bool IList.Contains(object? value) => value is T t && _items.Contains(t);
    public void CopyTo(T[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);
    void ICollection.CopyTo(Array array, int index) => ((ICollection)_items).CopyTo(array, index);
    public IEnumerator<T> GetEnumerator() => _items.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();
    public void Add(T item) => AddRange([item]);
    int IList.Add(object? value) { Add((T)value!); return _items.Count - 1; }
    public void Insert(int index, T item) => throw new NotSupportedException();
    void IList.Insert(int index, object? value) => throw new NotSupportedException();
    public bool Remove(T item) => throw new NotSupportedException();
    void IList.Remove(object? value) => throw new NotSupportedException();
    public void RemoveAt(int index) => throw new NotSupportedException();
}
