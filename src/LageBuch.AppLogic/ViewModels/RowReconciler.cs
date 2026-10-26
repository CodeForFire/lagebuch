using System.Collections.ObjectModel;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// Brings a bound row collection in line with the incident by id instead of Clear()+re-add (#294).
/// <c>Clear()</c> raises a Reset, which makes Avalonia tear down and re-realise every row container
/// and takes the selected row, the scroll offset and keyboard focus with it -- on every change made
/// anywhere in the incident. Here a row whose item is gone is removed, a new item is inserted where
/// it belongs, a row out of place is moved, and every kept row is updated in place.
/// </summary>
internal static class RowReconciler
{
    /// <summary>
    /// Makes <paramref name="rows"/> mirror <paramref name="items"/>, in order. Never raises a Reset
    /// and never replaces a row by index. O(n²) through <c>IndexOf</c>, which costs nothing at the
    /// tens of rows an Einsatz holds. The key is an id for every row the incident holds by id; a
    /// row derived from text (a free-text Abschnitt) is keyed by that text.
    /// </summary>
    public static void Reconcile<TRow, TItem, TKey>(
        ObservableCollection<TRow> rows,
        IReadOnlyList<TItem> items,
        Func<TRow, TKey> rowId,
        Func<TItem, TKey> itemId,
        Func<TItem, TRow> create,
        Action<TRow, TItem> update)
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(rowId);
        ArgumentNullException.ThrowIfNull(itemId);
        ArgumentNullException.ThrowIfNull(create);
        ArgumentNullException.ThrowIfNull(update);

        var wanted = items.Select(itemId).ToHashSet();
        for (var i = rows.Count - 1; i >= 0; i--)
        {
            if (!wanted.Contains(rowId(rows[i])))
            {
                rows.RemoveAt(i);
            }
        }

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var id = itemId(item);
            var at = IndexOf(rows, rowId, id, from: i);
            if (at < 0)
            {
                rows.Insert(i, create(item));
                continue;
            }

            if (at != i)
            {
                rows.Move(at, i);
            }

            update(rows[i], item);
        }
    }

    // Rows before 'from' are already placed, so the search starts there.
    private static int IndexOf<TRow, TKey>(ObservableCollection<TRow> rows, Func<TRow, TKey> rowId, TKey id, int from)
        where TKey : notnull
    {
        for (var i = from; i < rows.Count; i++)
        {
            if (EqualityComparer<TKey>.Default.Equals(rowId(rows[i]), id))
            {
                return i;
            }
        }

        return -1;
    }
}
