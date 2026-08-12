namespace DK
{
    /// <summary>
    /// What a dug-out tile has been turned into. Rooms are a layer on top of terrain, not a
    /// terrain state — a tile is always <see cref="TileState.Dug"/> before it can hold one.
    /// </summary>
    public enum RoomType
    {
        None = 0,
        DungeonHeart,
        Treasury,
        Lair,
        TrainingRoom,
        Portal,
        HeroGate,
    }
}
