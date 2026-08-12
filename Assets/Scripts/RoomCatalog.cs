using UnityEngine;

namespace DK
{
    /// <summary>
    /// Every number and colour that distinguishes one room type from another, in one switch.
    /// Balancing the economy means editing this file and nothing else.
    /// </summary>
    public static class RoomCatalog
    {
        public struct Entry
        {
            public string Name;
            public string Hotkey;

            /// <summary>Gold charged per tile when the player paints this room.</summary>
            public int CostPerTile;

            /// <summary>How much gold one tile of this room can hold. 0 = not storage.</summary>
            public int GoldCapacityPerTile;

            /// <summary>False for rooms only the game places, i.e. the dungeon heart.</summary>
            public bool PlayerBuildable;

            public Color FloorColor;
        }

        public static Entry Get(RoomType type)
        {
            switch (type)
            {
                case RoomType.DungeonHeart:
                    return new Entry
                    {
                        Name = "Dungeon Heart",
                        Hotkey = "-",
                        CostPerTile = 0,
                        // Deliberately shallow: nine heart tiles hold four and a half gold seams,
                        // so the player runs out of vault before they run out of map and has to
                        // build a treasury. That pressure is the point of the room.
                        GoldCapacityPerTile = 25,
                        PlayerBuildable = false,
                        FloorColor = new Color(0.42f, 0.10f, 0.16f),
                    };

                case RoomType.Treasury:
                    return new Entry
                    {
                        Name = "Treasury",
                        Hotkey = "2",
                        CostPerTile = 50,
                        GoldCapacityPerTile = 250,
                        PlayerBuildable = true,
                        FloorColor = new Color(0.48f, 0.38f, 0.12f),
                    };

                case RoomType.Lair:
                    return new Entry
                    {
                        Name = "Lair",
                        Hotkey = "3",
                        CostPerTile = 100,
                        GoldCapacityPerTile = 0,
                        PlayerBuildable = true,
                        FloorColor = new Color(0.26f, 0.16f, 0.34f),
                    };

                case RoomType.TrainingRoom:
                    return new Entry
                    {
                        Name = "Training Room",
                        Hotkey = "4",
                        // Dearer per tile than a lair, and it has to be paid for twice: once to
                        // build and again for every level a creature takes in it.
                        CostPerTile = 120,
                        GoldCapacityPerTile = 0,
                        PlayerBuildable = true,
                        FloorColor = new Color(0.20f, 0.30f, 0.36f),
                    };

                case RoomType.Portal:
                    return new Entry
                    {
                        Name = "Portal",
                        Hotkey = "-",
                        CostPerTile = 0,
                        GoldCapacityPerTile = 0,
                        // Placed by the map, not the player, and never sellable: it is the only
                        // way creatures reach the dungeon, so losing it by misclick is not a
                        // decision worth offering.
                        PlayerBuildable = false,
                        FloorColor = new Color(0.16f, 0.32f, 0.52f),
                    };

                case RoomType.HeroGate:
                    return new Entry
                    {
                        Name = "Hero Gate",
                        Hotkey = "-",
                        CostPerTile = 0,
                        GoldCapacityPerTile = 0,
                        PlayerBuildable = false,
                        FloorColor = new Color(0.58f, 0.58f, 0.62f),
                    };

                default:
                    return new Entry
                    {
                        Name = "Floor",
                        Hotkey = "-",
                        CostPerTile = 0,
                        GoldCapacityPerTile = 0,
                        PlayerBuildable = false,
                        FloorColor = new Color(0.19f, 0.17f, 0.16f),
                    };
            }
        }

        public static int CostOf(RoomType type) => Get(type).CostPerTile;

        public static int CapacityOf(RoomType type) => Get(type).GoldCapacityPerTile;

        public static string NameOf(RoomType type) => Get(type).Name;

        /// <summary>What selling a tile of this room pays back. Half price, rounded down.</summary>
        public static int RefundOf(RoomType type) => Get(type).CostPerTile / 2;
    }
}
