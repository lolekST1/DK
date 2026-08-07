using UnityEngine;

namespace DK
{
    public enum HeroKind
    {
        Knight = 0,
        Thief,
        Lord,
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

            /// <summary>
            /// Ignores the vault and marches on the dungeon heart instead. This is what makes
            /// the last wave a different problem rather than a bigger one.
            /// </summary>
            public bool GoesForTheHeart;

            /// <summary>
            /// Whether being hit makes it turn and fight. False for something here to steal
            /// and leave, which has to be intercepted rather than merely met.
            /// </summary>
            public bool FightsBack;

            public Color Colour;
        }

        public static Entry Get(HeroKind kind)
        {
            switch (kind)
            {
                case HeroKind.Thief:
                    return new Entry
                    {
                        Name = "Thief",

                        // Not a fight, a race. It is quicker than anything the dungeon houses
                        // except a fly, carries far more, and will not stop to trade blows even
                        // when hit — so it has to be cut off, not answered.
                        Health = 70,
                        Damage = 6,
                        AttackInterval = 1.2f,
                        MoveSpeed = 3.6f,

                        LootCapacity = 250,
                        AlertRange = 0,
                        GoesForTheHeart = false,
                        FightsBack = false,

                        Colour = new Color(0.42f, 0.36f, 0.54f),
                    };

                case HeroKind.Lord:
                    return new Entry
                    {
                        Name = "Lord of the Land",

                        // Defenders do not queue: every creature on the map answers a raid, so
                        // he fights a crowd that grows its damage with every member. What a
                        // garrison of N can land before he kills them all goes up with N
                        // squared, which is why this number is nothing like the 280 that a
                        // one-at-a-time reading of the fight suggested. Measured on a full map,
                        // with lairs round the heart: five defenders lose the heart, six or
                        // seven win it at the cost of two to four of them, and a full roster of
                        // ten walks away with nobody lost. That is the fight the economy pays
                        // for — hopeless for a keeper with three, comfortable for one who
                        // housed and paid a garrison.
                        Health = 1100,
                        Damage = 12,
                        AttackInterval = 1.3f,
                        MoveSpeed = 2.2f,

                        // Not here for the money.
                        LootCapacity = 0,
                        AlertRange = 8,
                        GoesForTheHeart = true,
                        FightsBack = true,

                        Colour = new Color(0.94f, 0.86f, 0.42f),
                    };

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
                        FightsBack = true,

                        Colour = new Color(0.72f, 0.76f, 0.86f),
                    };
            }
        }

        public static string NameOf(HeroKind kind) => Get(kind).Name;
    }
}
