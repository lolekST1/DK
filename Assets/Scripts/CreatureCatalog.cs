using UnityEngine;

namespace DK
{
    public enum CreatureKind
    {
        Beetle = 0,
    }

    /// <summary>
    /// Every number that defines a creature, in one switch, the same way
    /// <see cref="RoomCatalog"/> holds the room economy. Tuning how demanding the dungeon's
    /// tenants are means editing this file and nothing else.
    /// </summary>
    public static class CreatureCatalog
    {
        public struct Entry
        {
            public string Name;

            /// <summary>Gold this creature takes out of the vault every payday.</summary>
            public int Wage;

            public float MoveSpeed;

            /// <summary>Fatigue gained per second awake. 1.0 means "needs a bed now".</summary>
            public float FatiguePerSecond;

            /// <summary>Fatigue shed per second asleep in a lair.</summary>
            public float RestPerSecond;

            /// <summary>Anger gained per second while it has nowhere to sleep.</summary>
            public float HomelessAngerPerSecond;

            /// <summary>Anger gained each time the dungeon cannot make payroll.</summary>
            public float AngerPerMissedWage;

            /// <summary>Anger shed per second once it is both housed and paid up.</summary>
            public float CalmPerSecond;

            public Color Skin;
        }

        public static Entry Get(CreatureKind kind)
        {
            switch (kind)
            {
                default:
                    return new Entry
                    {
                        Name = "Beetle",
                        Wage = 30,

                        MoveSpeed = 2.2f,

                        // Roughly a minute and a half awake, fifteen seconds to sleep it off.
                        // Short enough that a player watching for a minute sees the cycle,
                        // long enough that creatures are not permanently in bed.
                        FatiguePerSecond = 1f / 90f,
                        RestPerSecond = 1f / 15f,

                        // Two minutes of homelessness, or three missed paydays, and it walks.
                        // Both routes out are deliberately slow: the player needs time to
                        // notice the HUD warning and dig their way to more gold.
                        HomelessAngerPerSecond = 1f / 120f,
                        AngerPerMissedWage = 0.34f,
                        CalmPerSecond = 1f / 30f,

                        Skin = new Color(0.30f, 0.52f, 0.34f),
                    };
            }
        }

        public static string NameOf(CreatureKind kind) => Get(kind).Name;

        public static int WageOf(CreatureKind kind) => Get(kind).Wage;
    }
}
