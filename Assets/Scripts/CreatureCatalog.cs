using UnityEngine;

namespace DK
{
    public enum CreatureKind
    {
        Beetle = 0,
        Fly,
        Troll,
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

            /// <summary>
            /// Gold this creature takes out of the vault every payday. Read it together with
            /// <see cref="CreatureManager.PaydayInterval"/>: what matters is the bill per
            /// second for a full roster against what a crew of imps can actually mine.
            /// </summary>
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

            /// <summary>Damage it can absorb before it dies.</summary>
            public int Health;

            /// <summary>
            /// Health recovered per second asleep in a lair, and nowhere else. Without this,
            /// every wound a creature takes is permanent: a garrison that met five raids came
            /// to the last one on two thirds health with no way back, so the Lord fought a
            /// worn-down dungeon however well the player had played up to then.
            /// </summary>
            public float HealPerSecond;

            /// <summary>Damage dealt per swing.</summary>
            public int Damage;

            /// <summary>Seconds between swings.</summary>
            public float AttackInterval;

            /// <summary>
            /// How far off it will notice a hero, in tiles. Bigger than any prototype map on
            /// purpose: with no way for the player to order creatures into battle, a defender
            /// that ignores a raid on the far side of the dungeon is a defender that does not
            /// defend. The cap stays per-kind for the day a lazier creature wants one.
            /// </summary>
            public int AlertRange;

            public Color Skin;
        }

        /// <summary>Everything the portal can send, in the order it sends them.</summary>
        public static readonly CreatureKind[] All =
        {
            CreatureKind.Fly,
            CreatureKind.Beetle,
            CreatureKind.Troll,
        };

        public static Entry Get(CreatureKind kind)
        {
            switch (kind)
            {
                case CreatureKind.Fly:
                    return new Entry
                    {
                        Name = "Fly",
                        Wage = 10,
                        MoveSpeed = 3.4f,

                        // Cheap and quick to the fight, which is most of what it is for: it
                        // reaches a raid while the heavier things are still crossing the map.
                        FatiguePerSecond = 1f / 70f,
                        RestPerSecond = 1f / 12f,

                        HomelessAngerPerSecond = 1f / 120f,
                        AngerPerMissedWage = 0.34f,
                        CalmPerSecond = 1f / 30f,

                        Health = 40,
                        Damage = 5,
                        AttackInterval = 0.8f,
                        AlertRange = 64,

                        Skin = new Color(0.62f, 0.68f, 0.78f),
                    };

                case CreatureKind.Troll:
                    return new Entry
                    {
                        Name = "Troll",

                        // Dear, but not so dear that a full house of them outruns the digging:
                        // PayrollBalanceChecks prices a roster on the portal's rotation, and
                        // 35 put the average past what six imps can mine.
                        Wage = 30,
                        MoveSpeed = 1.8f,

                        // Sleeps hard and often. The cost of the thing that actually holds a
                        // corridor is that it is not always awake in it.
                        FatiguePerSecond = 1f / 70f,
                        RestPerSecond = 1f / 20f,

                        HomelessAngerPerSecond = 1f / 120f,
                        AngerPerMissedWage = 0.34f,
                        CalmPerSecond = 1f / 30f,

                        Health = 130,
                        Damage = 12,
                        AttackInterval = 1.4f,
                        AlertRange = 64,

                        Skin = new Color(0.34f, 0.40f, 0.26f),
                    };

                default:
                    return new Entry
                    {
                        Name = "Beetle",
                        Wage = 20,

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

                        // Balanced for the worst case, which is also the normal one: creatures
                        // arrive from their lairs one at a time, so a raid is a run of duels
                        // rather than a brawl. One beetle loses to a knight and leaves it on
                        // about a third health, so the second one finishes it. Fighting
                        // together is strictly better than that, never worse.
                        Health = 70,
                        Damage = 8,
                        AttackInterval = 1.0f,

                        // A full night in a lair — about half a minute — mends a beetle from
                        // nearly dead to whole. Slower than the raid clock on purpose: back to
                        // back waves still find the garrison short of its best.
                        HealPerSecond = 70f / 30f,

                        // Effectively the whole dungeon: every beetle answers every raid.
                        AlertRange = 64,

                        Skin = new Color(0.30f, 0.52f, 0.34f),
                    };
            }
        }

        public static string NameOf(CreatureKind kind) => Get(kind).Name;

        public static int WageOf(CreatureKind kind) => Get(kind).Wage;
    }
}
