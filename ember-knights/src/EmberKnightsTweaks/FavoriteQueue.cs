using System;
using System.Collections.Generic;
using System.Linq;

namespace EmberKnightsTweaks;

// Pure queue logic, kept free of game types so it can be unit tested.
public static class FavoriteQueue
{
    public static IReadOnlyList<int> Parse(string csv) =>
        csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => int.TryParse(s, out var id) ? id : -1)
            .Where(id => id >= 0)
            .Distinct()
            .ToList();

    // Walks the queue from cursor, taking up to count ids that pass isEligible.
    // Ineligible ids are skipped for good (the cursor moves past them).
    public static (IReadOnlyList<int> Picks, int Cursor) Take(
        IReadOnlyList<int> favorites, int cursor, int count, Func<int, bool> isEligible)
    {
        var picks = new List<int>();
        while (cursor < favorites.Count && picks.Count < count)
        {
            var id = favorites[cursor++];
            if (isEligible(id)) picks.Add(id);
        }
        return (picks, cursor);
    }

    // Lays picks over the generated relic ids, front first. When there are fewer picks
    // than slots, a leftover random id that repeats a pick is swapped for one of the
    // random ids the picks displaced, so the shop never shows the same relic twice.
    public static IReadOnlyList<int> Place(IReadOnlyList<int> generated, IReadOnlyList<int> picks)
    {
        var spare = new Queue<int>(generated.Take(picks.Count).Where(id => !picks.Contains(id)));
        var rest = generated.Skip(picks.Count)
            .Select(id => picks.Contains(id) && spare.Count > 0 ? spare.Dequeue() : id);
        return picks.Concat(rest).ToList();
    }
}
