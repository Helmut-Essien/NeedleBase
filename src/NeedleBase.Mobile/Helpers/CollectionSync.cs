using System.Collections.ObjectModel;

namespace NeedleBase.Mobile.Helpers;

/// <summary>
/// Updates a bound list in place: unchanged rows are kept, changed positions replaced, the tail added or
/// removed. No Reset notification, so the CollectionView keeps its scroll position and doesn't flicker.
/// </summary>
public static class CollectionSync
{
    public static void Apply<TItem, TSource>(
        ObservableCollection<TItem> target,
        IReadOnlyList<TSource> source,
        Func<TItem, TSource, bool> same,
        Func<TSource, TItem> create)
    {
        for (var i = 0; i < source.Count; i++)
        {
            if (i < target.Count)
            {
                if (!same(target[i], source[i]))
                {
                    target[i] = create(source[i]);
                }
            }
            else
            {
                target.Add(create(source[i]));
            }
        }

        for (var i = target.Count - 1; i >= source.Count; i--)
        {
            target.RemoveAt(i);
        }
    }
}
