using System;
using System.Collections.Generic;
using UnityEngine;

namespace DK
{
    /// <summary>
    /// The portal's roster: who has walked in, who is owed wages, and who has walked out.
    ///
    /// Creatures only arrive when the dungeon has actually earned them — the portal has to be
    /// dug through to, and there has to be a free lair waiting. Payday then charges the vault
    /// for everyone on the books, so every creature is a standing cost against the gold the
    /// imps bring in rather than a free unit.
    /// </summary>
    public class CreatureManager : MonoBehaviour
    {
        /// <summary>Seconds between arrivals while the dungeon can take another creature.</summary>
        public float SpawnInterval = 20f;

        /// <summary>
        /// Seconds between wage bills. A full roster at 20 gold each is a third of a gold a
        /// second per creature, which a crew of imps can out-earn with room to spare — at 30
        /// every 45s, eight creatures cost more than the dungeon could dig.
        /// </summary>
        public float PaydayInterval = 60f;

        /// <summary>
        /// Hard ceiling, so a huge lair block cannot spawn an unbounded crowd. Kept above what
        /// the last stand needs, so lairs — which cost gold and floor — are the real limit.
        /// </summary>
        public int MaxCreatures = 10;

        /// <summary>
        /// What the portal sends next. It rotates through the catalog rather than picking at
        /// random, so a dungeon always gets the same mix in the same order — a garrison you
        /// can plan around, and a raid you can reproduce when something goes wrong in it.
        /// </summary>
        public CreatureKind NextKind => CreatureCatalog.All[_spawnCounter % CreatureCatalog.All.Length];

        GridManager _grid;
        RoomManager _rooms;
        ResourceManager _economy;
        Battlefield _battlefield;
        Transform _root;

        readonly List<CreatureAI> _creatures = new List<CreatureAI>();
        readonly List<Vector2Int> _pathScratch = new List<Vector2Int>();

        float _spawnTimer;
        float _paydayTimer;
        int _spawnCounter;

        public IReadOnlyList<CreatureAI> Creatures => _creatures;

        public int CreatureCount => _creatures.Count;

        /// <summary>Gold the dungeon has paid out in wages over the run.</summary>
        public int TotalWagesPaid { get; private set; }

        /// <summary>How many wage payments the vault has failed to cover.</summary>
        public int MissedPayments { get; private set; }

        /// <summary>Creatures that gave up and walked back out of the portal.</summary>
        public int Departed { get; private set; }

        /// <summary>Creatures killed defending the dungeon.</summary>
        public int Fallen { get; private set; }

        public float SecondsToPayday => Mathf.Max(0f, _paydayTimer);

        /// <summary>True once a dug route joins the portal to the heart.</summary>
        public bool PortalConnected { get; private set; }

        /// <summary>Why nothing is arriving right now, or null when a creature is on its way.</summary>
        public string ArrivalBlocker { get; private set; }

        /// <summary>Raised when a creature walks in.</summary>
        public event Action<CreatureAI> CreatureArrived;

        /// <summary>Raised when one walks out. Carries the reason it gave up.</summary>
        public event Action<CreatureAI, string> CreatureLeft;

        public void Configure(GridManager grid, RoomManager rooms, ResourceManager economy,
                              Battlefield battlefield)
        {
            _grid = grid;
            _rooms = rooms;
            _economy = economy;
            _battlefield = battlefield;

            _root = new GameObject("Creatures").transform;
            _root.SetParent(transform, false);

            _paydayTimer = PaydayInterval;
            _spawnTimer = SpawnInterval;
        }

        void Update()
        {
            if (_grid == null) return;

            float dt = Time.deltaTime;

            PruneDeparted();
            TickPayday(dt);
            TickSpawning(dt);
        }

        // ---------------------------------------------------------------- payroll

        void TickPayday(float dt)
        {
            if (_creatures.Count == 0)
            {
                // No mouths to feed, no clock running. Otherwise the first creature to arrive
                // would be billed a fraction of a second later, which reads as a bug.
                _paydayTimer = PaydayInterval;
                return;
            }

            _paydayTimer -= dt;
            if (_paydayTimer > 0f) return;

            _paydayTimer = PaydayInterval;
            RunPayday();
        }

        /// <summary>Bills the vault for every creature on the roster. Public so tests can force it.</summary>
        public void RunPayday()
        {
            for (int i = 0; i < _creatures.Count; i++)
            {
                var creature = _creatures[i];
                if (creature == null) continue;

                int wage = creature.Wage;

                if (_economy != null && _economy.TrySpend(wage))
                {
                    TotalWagesPaid += wage;
                    creature.OnPaid();
                    continue;
                }

                MissedPayments++;
                creature.OnUnpaid();
            }
        }

        // ---------------------------------------------------------------- arrivals

        void TickSpawning(float dt)
        {
            ArrivalBlocker = DescribeArrivalBlocker();

            if (ArrivalBlocker != null)
            {
                // Hold the timer at the full interval while blocked, so clearing the blocker
                // does not instantly dump a creature the player did not see coming.
                _spawnTimer = SpawnInterval;
                return;
            }

            _spawnTimer -= dt;
            if (_spawnTimer > 0f) return;

            _spawnTimer = SpawnInterval;
            Spawn();
        }

        string DescribeArrivalBlocker()
        {
            if (_rooms == null || !_rooms.HasPortal) return "no portal on this map";

            PortalConnected = IsPortalConnected();
            if (!PortalConnected) return "portal is still sealed in rock — dig your way to it";

            if (_creatures.Count >= MaxCreatures) return "the dungeon is full";

            if (_rooms.FreeLairCount <= 0) return "no free lair for anyone to move into";

            return null;
        }

        bool IsPortalConnected()
        {
            if (!_grid.IsWalkable(_rooms.PortalCell) || !_grid.IsWalkable(_rooms.HeartCell)) return false;

            return Pathfinder.TryFindPath(_grid, _rooms.PortalCell, _rooms.HeartCell, _pathScratch);
        }

        /// <summary>Puts one creature on the portal tile. Public so tests do not have to wait.</summary>
        public CreatureAI Spawn()
        {
            var creature = BuildCreature(_spawnCounter, NextKind, _rooms.PortalCell);
            _spawnCounter++;
            _creatures.Add(creature);

            CreatureArrived?.Invoke(creature);
            return creature;
        }

        CreatureAI BuildCreature(int index, CreatureKind kind, Vector2Int cell)
        {
            var stats = CreatureCatalog.Get(kind);

            var root = new GameObject($"Creature_{index}");
            root.transform.SetParent(_root, false);

            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            Destroy(body.GetComponent<Collider>());
            body.transform.SetParent(root.transform, false);
            // Squat and wide, so a creature never gets mistaken for an imp at a glance, and
            // sized off its health so the heavy ones read as heavy without a label.
            float bulk = Mathf.Lerp(0.5f, 0.78f, Mathf.InverseLerp(40f, 130f, stats.Health));
            body.transform.localScale = new Vector3(bulk, bulk * 0.42f, bulk);
            body.transform.localPosition = new Vector3(0f, bulk * 0.45f, 0f);

            var renderer = body.GetComponent<Renderer>();
            renderer.sharedMaterial = MaterialLibrary.CreateLit($"DK_Creature_{index}", stats.Skin);

            var shell = GameObject.CreatePrimitive(PrimitiveType.Cube);
            shell.name = "Shell";
            Destroy(shell.GetComponent<Collider>());
            shell.transform.SetParent(body.transform, false);
            shell.transform.localScale = new Vector3(0.55f, 0.6f, 0.9f);
            shell.transform.localPosition = new Vector3(0f, 0.55f, 0.1f);
            shell.GetComponent<Renderer>().sharedMaterial =
                MaterialLibrary.CreateLit("DK_CreatureShell", new Color(0.14f, 0.20f, 0.16f));

            var creature = root.AddComponent<CreatureAI>();
            creature.Configure(_grid, _rooms, _battlefield, kind, body.transform, renderer, cell);
            return creature;
        }

        // ---------------------------------------------------------------- departures

        void PruneDeparted()
        {
            for (int i = _creatures.Count - 1; i >= 0; i--)
            {
                var creature = _creatures[i];
                if (creature == null)
                {
                    _creatures.RemoveAt(i);
                    continue;
                }

                if (!creature.HasLeft && creature.IsAlive) continue;

                string reason = !creature.IsAlive ? "killed"
                              : creature.MissedPaydays > 0 ? "unpaid"
                              : "nowhere to sleep";

                if (!creature.IsAlive) Fallen++;
                else Departed++;

                _creatures.RemoveAt(i);

                // Give the lair back now rather than leaving it to OnDestroy: Unity does not
                // destroy the object until the end of the frame, and a lair that stays
                // occupied until then can block the very next arrival check.
                if (_rooms != null) _rooms.UnregisterWorker(creature);

                CreatureLeft?.Invoke(creature, reason);
                Destroy(creature.gameObject);
            }
        }
    }
}
