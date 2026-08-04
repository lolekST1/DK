namespace DK
{
    /// <summary>Per-tile terrain state. Deliberately tiny — rooms/walls are out of scope.</summary>
    public enum TileState
    {
        Rock = 0,
        Dug = 1,
        GoldSeam = 2,
    }
}
