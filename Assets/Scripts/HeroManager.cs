using System;
using System.Collections.Generic;
using UnityEngine;

namespace DK
{
    /// <summary>
    /// The hero gate and the raids that come out of it. Mirrors <see cref="CreatureManager"/>:
    /// a sealed cavern the map places, a connectivity gate, a timer, and a roster.
    ///
    /// Raids only start once the dungeon has dug its way through to the gate, so expanding is
    /// what invites them in. That is the point — the map is not a safe box you empty at your
    /// own pace, and the gold you dug up is the thing being come for.
    /// </summary>
    public class HeroManager : MonoBehaviour
    {
        /// <summary>Seconds between raids once the gate is reachable.</summary>
        public float RaidInterval = 90f;

        /// <summary>Grace period after the gate is first opened, so it is never a surprise.</summary>
        public float FirstRaidDelay = 45f;

        /// <summary>How many heroes are in one raid. Kept at one until it needs not to be.</summary>
        public int RaidSize = 1;

        public int MaxHeroes = 4;

        /// <summary>
        /// How many ordinary raids come before the Lord of the Land does. The waves in between
        /// are what the player is meant to learn on, and what pays for the creatures that meet
        /// him.
        /// </summary>
        public int WavesBeforeLord = 5;

        /// <summary>
        /// Ordinary raids alternate. A knight has to be beaten and a thief has to be caught,
        /// so a dungeon that only answers one of them loses to the other.
        /// </summary>
        static readonly HeroKind[] RaidOrder = { HeroKind.Knight, HeroKind.Thief };

        GridManager _grid;
        RoomManager _rooms;
        ResourceManager _economy;
        LooseGold _loose;
        Battlefield _battlefield;
        DungeonHeart _heart;
        Transform _root;

        readonly List<HeroAI> _heroes = new List<HeroAI>();
        readonly List<Vector2Int> _pathScratch = new List<Vector2Int>();

        float _raidTimer;
        int _spawnCounter;
        bool _gateOpened;

        public IReadOnlyList<HeroAI> Heroes => _heroes;

        public int HeroCount => _heroes.Count;

        /// <summary>Heroes killed inside the dungeon.</summary>
        public int Repelled { get; private set; }

        /// <summary>Heroes that got back out through the gate.</summary>
        public int Escaped { get; private set; }

        /// <summary>Gold carried out of the dungeon for good.</summary>
        public int GoldStolen { get; private set; }

        /// <summary>Gold dropped by heroes killed on the way out, and left for the imps.</summary>
        public int GoldRecovered { get; private set; }

        /// <summary>Raids sent so far, the Lord's included.</summary>
        public int WavesSent { get; private set; }

        /// <summary>True once the last wave has been sent.</summary>
        public bool LordSent { get; private set; }

        /// <summary>True once the Lord of the Land has been killed in the dungeon.</summary>
        public bool LordDefeated { get; private set; }

        /// <summary>How many ordinary raids are still to come before the Lord.</summary>
        public int WavesUntilLord => Mathf.Max(0, WavesBeforeLord - WavesSent);

        /// <summary>True once a dug route joins the gate to the heart.</summary>
        public bool GateReachable { get; private set; }

        public float SecondsToRaid => _gateOpened ? Mathf.Max(0f, _raidTimer) : -1f;

        public event Action<HeroAI> RaidArrived;

        /// <summary>Raised when a hero leaves the roster. Carries how it went and the loot.</summary>
        public event Action<HeroAI, bool, int> HeroGone;

        public void Configure(GridManager grid, RoomManager rooms, ResourceManager economy,
                              LooseGold loose, Battlefield battlefield, DungeonHeart heart)
        {
            _grid = grid;
            _rooms = rooms;
            _economy = economy;
            _loose = loose;
            _battlefield = battlefield;
            _heart = heart;

            _root = new GameObject("Heroes").transform;
            _root.SetParent(transform, false);

            _raidTimer = FirstRaidDelay;
        }

        void Update()
        {
            if (_grid == null) return;

            float dt = Time.deltaTime;

            PruneGone();
            TickRaids(dt);
        }

        void TickRaids(float dt)
        {
            GateReachable = IsGateReachable();

            if (!GateReachable)
            {
                // The clock does not run on a gate nobody has opened yet.
                _raidTimer = _gateOpened ? _raidTimer : FirstRaidDelay;
                return;
            }

            if (!_gateOpened)
            {
                _gateOpened = true;
                _raidTimer = FirstRaidDelay;
            }

            if (_heroes.Count >= MaxHeroes) return;

            _raidTimer -= dt;
            if (_raidTimer > 0f) return;

            _raidTimer = RaidInterval;
            Raid();
        }

        bool IsGateReachable()
        {
            if (_rooms == null || !_rooms.HasHeroGate) return false;
            if (!_grid.IsWalkable(_rooms.HeroGateCell) || !_grid.IsWalkable(_rooms.HeartCell)) return false;

            return Pathfinder.TryFindPath(_grid, _rooms.HeroGateCell, _rooms.HeartCell, _pathScratch);
        }

        /// <summary>Sends one raid through the gate. Public so tests do not have to wait.</summary>
        public void Raid()
        {
            // The last wave is one Lord, on his own. He is not a bigger knight and does not
            // need an escort — he walks past the vault and goes for the heart.
            bool lord = !LordSent && WavesSent >= WavesBeforeLord;
            var kind = lord ? HeroKind.Lord : RaidOrder[WavesSent % RaidOrder.Length];
            int count = lord ? 1 : RaidSize;

            WavesSent++;
            if (lord) LordSent = true;

            for (int i = 0; i < count && _heroes.Count < MaxHeroes; i++)
            {
                var hero = BuildHero(_spawnCounter++, kind);
                _heroes.Add(hero);
                RaidArrived?.Invoke(hero);
            }
        }

        HeroAI BuildHero(int index, HeroKind kind)
        {
            var root = new GameObject($"Hero_{index}");
            root.transform.SetParent(_root, false);

            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            Destroy(body.GetComponent<Collider>());
            body.transform.SetParent(root.transform, false);
            // Taller than anything else on the map, so a raid is obvious at a glance.
            body.transform.localScale = new Vector3(0.44f, 0.42f, 0.44f);
            body.transform.localPosition = new Vector3(0f, 0.42f, 0f);

            var stats = HeroCatalog.Get(kind);

            // The Lord stands a head above the knights, so the last wave is unmistakable, and
            // a thief is slighter than either.
            if (stats.GoesForTheHeart)
            {
                body.transform.localScale = new Vector3(0.52f, 0.52f, 0.52f);
                body.transform.localPosition = new Vector3(0f, 0.52f, 0f);
            }
            else if (!stats.FightsBack)
            {
                body.transform.localScale = new Vector3(0.36f, 0.38f, 0.36f);
                body.transform.localPosition = new Vector3(0f, 0.38f, 0f);
            }

            var renderer = body.GetComponent<Renderer>();
            renderer.sharedMaterial = MaterialLibrary.CreateLit(
                $"DK_Hero_{index}", stats.Colour, 0.4f, 0.6f);

            var crest = GameObject.CreatePrimitive(PrimitiveType.Cube);
            crest.name = "Crest";
            Destroy(crest.GetComponent<Collider>());
            crest.transform.SetParent(body.transform, false);
            crest.transform.localScale = new Vector3(0.25f, 0.5f, 0.7f);
            crest.transform.localPosition = new Vector3(0f, 0.62f, 0f);
            crest.GetComponent<Renderer>().sharedMaterial =
                MaterialLibrary.CreateLit("DK_HeroCrest", new Color(0.75f, 0.20f, 0.22f));

            var hero = root.AddComponent<HeroAI>();
            hero.Configure(_grid, _rooms, _economy, _loose, _battlefield, _heart, kind,
                           body.transform, renderer, _rooms.HeroGateCell);
            return hero;
        }

        void PruneGone()
        {
            for (int i = _heroes.Count - 1; i >= 0; i--)
            {
                var hero = _heroes[i];
                if (hero == null)
                {
                    _heroes.RemoveAt(i);
                    continue;
                }

                // Health, not IsAlive: an escaped hero also reports as no longer alive, and
                // walking out of the door is the opposite of being killed.
                bool killed = hero.Health <= 0;
                if (!killed && !hero.HasEscaped) continue;

                int loot = hero.CarriedGold;

                if (killed)
                {
                    Repelled++;
                    GoldRecovered += hero.DropLoot();
                    if (hero.Kind == HeroKind.Lord) LordDefeated = true;
                }
                else
                {
                    Escaped++;
                    GoldStolen += loot;
                }

                _heroes.RemoveAt(i);
                HeroGone?.Invoke(hero, killed, loot);

                Destroy(hero.gameObject);
            }
        }
    }
}
