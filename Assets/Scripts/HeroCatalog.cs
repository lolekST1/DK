using UnityEngine;

namespace DK
{
    public enum HeroKind
    {
        Knight = 0,
    }

    /// <summary>
    /// What comes through the hero gate, in one switch, the same shape as
    /// <see cref="CreatureCatalog"/> and <see cref="RoomCatalog"/>.
    /// </summary>
    public static class HeroCatalog
    {
        public struct Entry
        {
            public string Name;

            public int Health;
            public int Damage;
            public float AttackInterval;
            public float MoveSpeed;

            /// <summary>How much gold it can carry out of a vault in one raid.</summary>
            public int LootCapacity;

            /// <summary>How far off it will notice a defender, in tiles.</summary>
            public int AlertRange;

            public Color Colour;
        }

        public static Entry Get(HeroKind kind)
        {
            switch (kind)
            {
                default:
                    return new Entry
                    {
                        Name = "Knight",

                        // Beats one beetle and loses to two, counting them arriving one after
                        // the other. The dungeon's answer to a raid is "house and pay more
                        // creatures", which is the loop the gold economy already runs on.
                        Health = 120,
                        Damage = 8,
                        AttackInterval = 1.2f,
                        MoveSpeed = 2.6f,

                        // Enough to hurt without emptying a developed vault in one visit.
                        LootCapacity = 150,
                        AlertRange = 6,

                        Colour = new Color(0.72f, 0.76f, 0.86f),
                    };
            }
        }

        public static string NameOf(HeroKind kind) => Get(kind).Name;
    }
}
