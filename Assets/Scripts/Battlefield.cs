using System.Collections.Generic;
using UnityEngine;

namespace DK
{
    public enum Side
    {
        Dungeon,
        Hero,
    }

    /// <summary>
    /// What it takes to be worth hitting. Keeping creatures and heroes behind one interface is
    /// what stops <see cref="CreatureAI"/> and <see cref="HeroAI"/> having to know about each
    /// other at all — they only ever ask the battlefield who the nearest enemy is.
    /// </summary>
    public interface ICombatant
    {
        Side Side { get; }
        Vector2Int Cell { get; }
        Vector3 Position { get; }
        bool IsAlive { get; }

        /// <summary>
        /// True for things that stand still and never swing back, like the dungeon heart.
        /// Enemy searches skip them by default: a knight is here for the gold and should not
        /// stop to hack at the masonry, and a creature has nothing to find in one at all.
        /// </summary>
        bool IsStructure { get; }

        void TakeDamage(int amount, ICombatant from);
    }

    /// <summary>
    /// The roster of everything that can fight, and the one place that answers "who is near
    /// enough to hit". Imps are not on it: they dig and run errands, and a keeper whose
    /// workforce could be killed would need a whole replacement policy the prototype does not
    /// have yet.
    /// </summary>
    public class Battlefield : MonoBehaviour
    {
        readonly List<ICombatant> _combatants = new List<ICombatant>();

        public IReadOnlyList<ICombatant> Combatants => _combatants;

        public void Register(ICombatant combatant)
        {
            if (!_combatants.Contains(combatant)) _combatants.Add(combatant);
        }

        public void Unregister(ICombatant combatant) => _combatants.Remove(combatant);

        public int CountOf(Side side)
        {
            int count = 0;
            for (int i = 0; i < _combatants.Count; i++)
                if (_combatants[i].IsAlive && _combatants[i].Side == side) count++;

            return count;
        }

        public bool TryFindNearestEnemy(ICombatant self, int range, out ICombatant enemy) =>
            TryFindNearestEnemy(self, range, false, out enemy);

        /// <summary>
        /// Nearest living enemy within range, by manhattan distance on the grid. Range is in
        /// tiles; pass <see cref="int.MaxValue"/> for "anywhere in the dungeon". Structures
        /// only turn up when asked for, so only what came to besiege one goes looking.
        /// </summary>
        public bool TryFindNearestEnemy(ICombatant self, int range, bool includeStructures,
                                        out ICombatant enemy)
        {
            enemy = null;
            int best = int.MaxValue;

            var from = self.Cell;

            for (int i = 0; i < _combatants.Count; i++)
            {
                var candidate = _combatants[i];
                if (candidate.Side == self.Side || !candidate.IsAlive) continue;
                if (candidate.IsStructure && !includeStructures) continue;

                var cell = candidate.Cell;
                int distance = Mathf.Abs(cell.x - from.x) + Mathf.Abs(cell.y - from.y);
                if (distance > range || distance >= best) continue;

                best = distance;
                enemy = candidate;
            }

            return enemy != null;
        }

        /// <summary>Adjacent, or on the same tile. Nothing here has reach beyond one step.</summary>
        public static bool InReach(ICombatant a, ICombatant b)
        {
            var one = a.Cell;
            var two = b.Cell;
            return Mathf.Abs(one.x - two.x) + Mathf.Abs(one.y - two.y) <= 1;
        }
    }
}
