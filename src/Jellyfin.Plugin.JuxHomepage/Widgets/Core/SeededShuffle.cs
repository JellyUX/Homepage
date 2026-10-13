namespace Jellyfin.Plugin.JuxHomepage.Widgets;

/// <summary>
/// Deterministic shuffling shared by the daily rotations ("Because You Watched", TODO_V3.md Phase 9.2,
/// and random-sorted sections, TODO_V4.md 8.1): the same seed always gives the same permutation of the
/// same input, across requests and restarts.
/// </summary>
internal static class SeededShuffle
{
    /// <summary>
    /// Shuffles <paramref name="items"/> in place using the Fisher-Yates algorithm.
    /// </summary>
    /// <typeparam name="T">The item type.</typeparam>
    /// <param name="items">The list to shuffle in place.</param>
    /// <param name="seed">The random seed.</param>
    public static void Shuffle<T>(IList<T> items, int seed)
    {
        var random = new Random(seed);
        for (var i = items.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
    }
}
