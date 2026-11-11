using EmberKnightsTweaks;
using Xunit;

public class FavoriteQueueTests
{
    private static readonly int[] Favorites = { 179, 177, 219, 131, 92, 152 };

    [Fact]
    public void Parse_TrimsSkipsJunkAndDuplicates() =>
        Assert.Equal(new[] { 179, 177, 92 }, FavoriteQueue.Parse(" 179, 177,,abc,92,179 "));

    [Fact]
    public void Take_FirstStockGetsFirstFavorites()
    {
        var (picks, cursor) = FavoriteQueue.Take(Favorites, 0, 4, _ => true);
        Assert.Equal(new[] { 179, 177, 219, 131 }, picks);
        Assert.Equal(4, cursor);
    }

    [Fact]
    public void Take_RerollContinuesFromCursor()
    {
        var (picks, cursor) = FavoriteQueue.Take(Favorites, 4, 3, _ => true);
        Assert.Equal(new[] { 92, 152 }, picks);
        Assert.Equal(6, cursor);
    }

    [Fact]
    public void Take_SkipsIneligibleForGood()
    {
        var (picks, cursor) = FavoriteQueue.Take(Favorites, 0, 2, id => id != 177);
        Assert.Equal(new[] { 179, 219 }, picks);
        Assert.Equal(3, cursor);
    }

    [Fact]
    public void Take_ExhaustedQueueReturnsNothing()
    {
        var (picks, cursor) = FavoriteQueue.Take(Favorites, 6, 4, _ => true);
        Assert.Empty(picks);
        Assert.Equal(6, cursor);
    }

    [Fact]
    public void Place_FullPicksReplaceEverything() =>
        Assert.Equal(new[] { 1, 2, 3 }, FavoriteQueue.Place(new[] { 7, 8, 9 }, new[] { 1, 2, 3 }));

    [Fact]
    public void Place_KeepsTrailingRandomIds() =>
        Assert.Equal(new[] { 1, 8, 9 }, FavoriteQueue.Place(new[] { 7, 8, 9 }, new[] { 1 }));

    [Fact]
    public void Place_SwapsTrailingDuplicateForDisplacedId() =>
        Assert.Equal(new[] { 1, 2, 7, 9 }, FavoriteQueue.Place(new[] { 7, 8, 1, 9 }, new[] { 1, 2 }));

    [Fact]
    public void Place_PickAlreadyInItsOwnSlotIsNotSpare() =>
        Assert.Equal(new[] { 1, 2, 8 }, FavoriteQueue.Place(new[] { 1, 7, 8 }, new[] { 1, 2 }));
}
