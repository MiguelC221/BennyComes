using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;

namespace BenyComes.Helpers;

/// <summary>
/// ObservableCollection that can replace/append many items with a single change notification,
/// avoiding thousands of per-item UI updates (large speed-up on big libraries).
/// </summary>
public class BulkObservableCollection<T> : ObservableCollection<T>
{
    public void ReplaceRange(IEnumerable<T> items)
    {
        CheckReentrancy();
        Items.Clear();
        foreach (var item in items) Items.Add(item);
        RaiseReset();
    }

    public void AddRange(IEnumerable<T> items)
    {
        CheckReentrancy();
        var list = items as IList<T> ?? items.ToList();
        if (list.Count == 0) return;
        foreach (var item in list) Items.Add(item);
        RaiseReset();
    }

    private void RaiseReset()
    {
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
