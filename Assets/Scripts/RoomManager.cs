using System;
using System.Collections.Generic;
using UnityEngine;

namespace DK
{
    /// <summary>
    /// The dungeon layer sitting on top of dug-out floor: which tiles have been turned into
    /// rooms, how much gold each storage tile holds, and which imp sleeps where.
    ///
    /// Terrain stays <see cref="GridManager"/>'s job. A room only ever exists on a tile that
    /// is already <see cref="TileState.Dug"/>, and dug tiles never revert, so the two layers
    /// never have to reconcile.
    ///
    /// Gold does not teleport into a counter any more: an imp carries what it mined here and
    /// drops it on a storage tile, so the vault is a physical place with a physical limit.
    /// </summary>
    public class RoomManager : MonoBehaviour
    {
        /// <summary>
        /// Gold the dungeon heart starts stocked with. Exactly two treasury tiles' worth, so
        /// the opening move is obvious without being free.
        /// </summary>
        public const int StartingGold = 100;

        /// <summary>Heart half-extent in tiles: 1 gives the classic 3x3.</summary>
        public const int HeartRadius = 1;

        GridManager _grid;

        RoomType[,] _rooms;
        int[,] _stored;

        GameObject[,] _slabs;
        Transform[,] _piles;
        Renderer[,] _slabRenderers;

        readonly List<Vector2Int> _storageTiles = new List<Vector2Int>();
        readonly List<Vector2Int> _lairTiles = new List<Vector2Int>();

        // Lair ownership is two-way so selling a tile can evict exactly one imp.
        readonly Dictionary<Vector2Int, object> _lairOwner = new Dictionary<Vector2Int, object>();
        readonly Dictionary<object, Vector2Int> _workerLair = new Dictionary<object, Vector2Int>();
        readonly Dictionary<object, Vector2Int> _workerFallback = new Dictionary<object, Vector2Int>();

        Transform _roomsRoot;
        Transform _heartCore;
        float _heartPhase;

        Material _pileMaterial;
        Material _heartCoreMaterial;
        Material _heartPlinthMaterial;
        readonly Dictionary<RoomType, Material> _floorMaterials = new Dictionary<RoomType, Material>();

        int _gold;
        int _capacity;

        /// <summary>Gold sitting in storage right now.</summary>
        public int StoredGold => _gold;

        /// <summary>Total gold every storage tile could hold.</summary>
        public int StorageCapacity => _capacity;

        public int FreeCapacity => Mathf.Max(0, _capacity - _gold);

        public Vector2Int HeartCell => _grid != null ? _grid.BaseCell : default;

        /// <summary>Raised when gold or capacity moved. Carries (stored, capacity).</summary>
        public event Action<int, int> StorageChanged;

        /// <summary>Raised when a tile's room changed (x, z).</summary>
        public event Action<int, int> RoomChanged;

        // ---------------------------------------------------------------- setup

        public void Configure(GridManager grid)
        {
            _grid = grid;

            _rooms = new RoomType[grid.Width, grid.Depth];
            _stored = new int[grid.Width, grid.Depth];
            _slabs = new GameObject[grid.Width, grid.Depth];
            _piles = new Transform[grid.Width, grid.Depth];
            _slabRenderers = new Renderer[grid.Width, grid.Depth];

            _roomsRoot = new GameObject("Rooms").transform;
            _roomsRoot.SetParent(transform, false);

            _pileMaterial = MaterialLibrary.CreateLit("DK_GoldPile", new Color(0.95f, 0.78f, 0.20f), 0.55f, 0.75f);
            _heartCoreMaterial = MaterialLibrary.CreateLit("DK_HeartCore", new Color(0.90f, 0.15f, 0.22f), 0.6f);
            _heartPlinthMaterial = MaterialLibrary.CreateLit("DK_HeartPlinth", new Color(0.22f, 0.06f, 0.10f));

            BuildHeart();
        }

        void BuildHeart()
        {
            var centre = _grid.BaseCell;

            for (int x = centre.x - HeartRadius; x <= centre.x + HeartRadius; x++)
            for (int z = centre.y - HeartRadius; z <= centre.y + HeartRadius; z++)
            {
                if (!_grid.IsWalkable(x, z)) continue;
                SetRoom(x, z, RoomType.DungeonHeart);
            }

            BuildHeartCentrepiece(centre);

            // Seed capital lives in the heart like everything else, so the HUD reads honestly
            // from the very first frame.
            DepositAnywhere(StartingGold);
        }

        void BuildHeartCentrepiece(Vector2Int centre)
        {
            var root = new GameObject("HeartCore").transform;
            root.SetParent(_roomsRoot, false);
            root.localPosition = _grid.CellToWorld(centre);

            var plinth = GameObject.CreatePrimitive(PrimitiveType.Cube);
            plinth.name = "Plinth";
            Destroy(plinth.GetComponent<Collider>());
            plinth.transform.SetParent(root, false);
            plinth.transform.localScale = new Vector3(0.8f, 0.18f, 0.8f);
            plinth.transform.localPosition = new Vector3(0f, 0.09f, 0f);
            plinth.GetComponent<Renderer>().sharedMaterial = _heartPlinthMaterial;

            var core = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            core.name = "Core";
            Destroy(core.GetComponent<Collider>());
            core.transform.SetParent(root, false);
            core.transform.localScale = new Vector3(0.5f, 0.5f, 0.5f);
            core.transform.localPosition = new Vector3(0f, 0.42f, 0f);
            core.GetComponent<Renderer>().sharedMaterial = _heartCoreMaterial;

            _heartCore = core.transform;
        }

        void Update()
        {
            if (_heartCore == null) return;

            // A slow beat, so the middle of the dungeon reads as alive rather than as scenery.
            _heartPhase += Time.deltaTime * 2.2f;
            float scale = 0.5f + Mathf.Sin(_heartPhase) * 0.045f;
            _heartCore.localScale = new Vector3(scale, scale, scale);
        }

        // ---------------------------------------------------------------- queries

        public RoomType GetRoom(int x, int z) =>
            _rooms != null && _grid.InBounds(x, z) ? _rooms[x, z] : RoomType.None;

        public RoomType GetRoom(Vector2Int cell) => GetRoom(cell.x, cell.y);

        public int GetStoredGold(int x, int z) =>
            _stored != null && _grid.InBounds(x, z) ? _stored[x, z] : 0;

        /// <summary>Whether the player could paint this room here, and why not when they cannot.</summary>
        public bool CanBuild(int x, int z, RoomType type, out string reason)
        {
            var entry = RoomCatalog.Get(type);

            if (!entry.PlayerBuildable)
            {
                reason = $"{entry.Name} cannot be built by hand";
                return false;
            }

            if (!_grid.InBounds(x, z))
            {
                reason = "off the map";
                return false;
            }

            if (!_grid.IsWalkable(x, z))
            {
                reason = "needs dug-out floor";
                return false;
            }

            if (_rooms[x, z] != RoomType.None)
            {
                reason = $"already {RoomCatalog.NameOf(_rooms[x, z])}";
                return false;
            }

            if (_gold < entry.CostPerTile)
            {
                reason = $"needs {entry.CostPerTile} gold";
                return false;
            }

            reason = null;
            return true;
        }

        public bool CanBuild(int x, int z, RoomType type) => CanBuild(x, z, type, out _);

        /// <summary>Whether this tile holds a room the player is allowed to tear out.</summary>
        public bool CanSell(int x, int z)
        {
            var type = GetRoom(x, z);
            return type != RoomType.None && RoomCatalog.Get(type).PlayerBuildable;
        }

        // ---------------------------------------------------------------- building

        /// <summary>Pays for and places one room tile. Returns false when it was not allowed.</summary>
        public bool Build(int x, int z, RoomType type)
        {
            if (!CanBuild(x, z, type, out _)) return false;
            if (!TryWithdraw(RoomCatalog.CostOf(type))) return false;

            SetRoom(x, z, type);
            return true;
        }

        /// <summary>
        /// Tears one room tile out, refunding half its cost. Gold stored on the tile is pushed
        /// into whatever storage is left; anything that does not fit is lost, which is the
        /// player's problem for selling a full vault.
        /// </summary>
        public bool Sell(int x, int z, out int lost)
        {
            lost = 0;
            if (!CanSell(x, z)) return false;

            var type = _rooms[x, z];

            int loose = _stored[x, z];
            _stored[x, z] = 0;
            _gold -= loose;

            SetRoom(x, z, RoomType.None);

            int owed = loose + RoomCatalog.RefundOf(type);
            lost = owed - DepositAnywhere(owed);
            return true;
        }

        public bool Sell(int x, int z) => Sell(x, z, out _);

        void SetRoom(int x, int z, RoomType type)
        {
            var cell = new Vector2Int(x, z);
            var previous = _rooms[x, z];
            if (previous == type) return;

            if (RoomCatalog.CapacityOf(previous) > 0)
            {
                _storageTiles.Remove(cell);
                _capacity -= RoomCatalog.CapacityOf(previous);
            }

            if (previous == RoomType.Lair) ReleaseLair(cell);

            _rooms[x, z] = type;

            if (RoomCatalog.CapacityOf(type) > 0)
            {
                _storageTiles.Add(cell);
                _capacity += RoomCatalog.CapacityOf(type);
            }

            if (type == RoomType.Lair) _lairTiles.Add(cell);

            UpdateSlab(x, z);
            UpdatePile(x, z);

            RoomChanged?.Invoke(x, z);
            RaiseStorageChanged();
        }

        // ---------------------------------------------------------------- gold

        /// <summary>Drops gold on one specific storage tile. Returns how much actually fit.</summary>
        public int Deposit(Vector2Int cell, int amount)
        {
            if (amount <= 0 || !_grid.InBounds(cell.x, cell.y)) return 0;

            int capacity = RoomCatalog.CapacityOf(_rooms[cell.x, cell.y]);
            int free = capacity - _stored[cell.x, cell.y];
            if (free <= 0) return 0;

            int put = Mathf.Min(free, amount);
            _stored[cell.x, cell.y] += put;
            _gold += put;

            UpdatePile(cell.x, cell.y);
            RaiseStorageChanged();
            return put;
        }

        /// <summary>
        /// Spreads gold over any storage with room, nearest-to-the-heart first. Used for
        /// refunds and for the seed capital — imps always use <see cref="Deposit"/> instead,
        /// because they have to walk there.
        /// </summary>
        public int DepositAnywhere(int amount)
        {
            int left = amount;

            for (int i = 0; i < _storageTiles.Count && left > 0; i++)
                left -= Deposit(_storageTiles[i], left);

            return amount - left;
        }

        /// <summary>Takes gold out of storage. All or nothing — returns false if short.</summary>
        public bool TryWithdraw(int amount)
        {
            if (amount <= 0) return true;
            if (_gold < amount) return false;

            // Treasuries drain before the heart: the heart is the reserve of last resort, so a
            // player who built a vault does not find their starting capital gone first.
            int left = amount;
            left = DrainTiles(left, heart: false);
            left = DrainTiles(left, heart: true);

            _gold -= amount - left;
            RaiseStorageChanged();
            return left == 0;
        }

        int DrainTiles(int amount, bool heart)
        {
            for (int i = 0; i < _storageTiles.Count && amount > 0; i++)
            {
                var cell = _storageTiles[i];
                bool isHeart = _rooms[cell.x, cell.y] == RoomType.DungeonHeart;
                if (isHeart != heart) continue;

                int taken = Mathf.Min(_stored[cell.x, cell.y], amount);
                if (taken == 0) continue;

                _stored[cell.x, cell.y] -= taken;
                amount -= taken;
                UpdatePile(cell.x, cell.y);
            }

            return amount;
        }

        /// <summary>
        /// Nearest storage tile with space left, by manhattan distance. The caller still has to
        /// prove it can path there — this only narrows the search.
        /// </summary>
        public bool TryFindDepositCell(Vector2Int from, out Vector2Int cell)
        {
            cell = default;
            int best = int.MaxValue;

            for (int i = 0; i < _storageTiles.Count; i++)
            {
                var candidate = _storageTiles[i];
                if (_stored[candidate.x, candidate.y] >= RoomCatalog.CapacityOf(_rooms[candidate.x, candidate.y]))
                    continue;

                int distance = Mathf.Abs(candidate.x - from.x) + Mathf.Abs(candidate.y - from.y);
                if (distance >= best) continue;

                best = distance;
                cell = candidate;
            }

            return best != int.MaxValue;
        }

        /// <summary>Every storage tile with free space, nearest first. Used when the closest is unreachable.</summary>
        public void CollectDepositCells(Vector2Int from, List<Vector2Int> result)
        {
            result.Clear();

            for (int i = 0; i < _storageTiles.Count; i++)
            {
                var candidate = _storageTiles[i];
                if (_stored[candidate.x, candidate.y] < RoomCatalog.CapacityOf(_rooms[candidate.x, candidate.y]))
                    result.Add(candidate);
            }

            result.Sort((a, b) =>
                (Mathf.Abs(a.x - from.x) + Mathf.Abs(a.y - from.y))
                .CompareTo(Mathf.Abs(b.x - from.x) + Mathf.Abs(b.y - from.y)));
        }

        void RaiseStorageChanged() => StorageChanged?.Invoke(_gold, _capacity);

        // ---------------------------------------------------------------- lairs

        /// <summary>Tells the dungeon a creature exists, and where it stands when it has no lair.</summary>
        public void RegisterWorker(object worker, Vector2Int fallbackHome)
        {
            _workerFallback[worker] = fallbackHome;
        }

        public void UnregisterWorker(object worker)
        {
            if (_workerLair.TryGetValue(worker, out var cell))
            {
                _lairOwner.Remove(cell);
                _workerLair.Remove(worker);
            }

            _workerFallback.Remove(worker);
        }

        /// <summary>
        /// Where this creature rests. Claims a free lair tile on the way if it has none, so
        /// building a lair is enough to move an imp in — there is no assignment step.
        /// </summary>
        public Vector2Int HomeFor(object worker)
        {
            if (_workerLair.TryGetValue(worker, out var lair))
            {
                if (GetRoom(lair) == RoomType.Lair) return lair;
                ReleaseLair(lair);
            }

            for (int i = 0; i < _lairTiles.Count; i++)
            {
                var cell = _lairTiles[i];
                if (_lairOwner.ContainsKey(cell)) continue;

                _lairOwner[cell] = worker;
                _workerLair[worker] = cell;
                return cell;
            }

            return _workerFallback.TryGetValue(worker, out var fallback) ? fallback : _grid.BaseCell;
        }

        /// <summary>True once this creature owns a lair tile. A rested imp digs faster.</summary>
        public bool HasLair(object worker) =>
            _workerLair.TryGetValue(worker, out var cell) && GetRoom(cell) == RoomType.Lair;

        public int LairCount => _lairTiles.Count;

        void ReleaseLair(Vector2Int cell)
        {
            _lairTiles.Remove(cell);

            if (!_lairOwner.TryGetValue(cell, out var owner)) return;

            _lairOwner.Remove(cell);
            _workerLair.Remove(owner);
        }

        // ---------------------------------------------------------------- visuals

        void UpdateSlab(int x, int z)
        {
            var type = _rooms[x, z];

            if (type == RoomType.None)
            {
                if (_slabs[x, z] != null) Destroy(_slabs[x, z]);
                _slabs[x, z] = null;
                _slabRenderers[x, z] = null;
                return;
            }

            if (_slabs[x, z] == null)
            {
                var slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
                slab.name = $"Room_{x}_{z}";
                Destroy(slab.GetComponent<Collider>());
                slab.transform.SetParent(_roomsRoot, false);
                slab.transform.localScale = new Vector3(GridManager.TileSize * 0.94f, 0.06f, GridManager.TileSize * 0.94f);
                slab.transform.localPosition = _grid.CellToWorld(x, z) + Vector3.up * 0.03f;

                _slabs[x, z] = slab;
                _slabRenderers[x, z] = slab.GetComponent<Renderer>();
            }

            _slabRenderers[x, z].sharedMaterial = FloorMaterial(type);
        }

        Material FloorMaterial(RoomType type)
        {
            if (_floorMaterials.TryGetValue(type, out var material)) return material;

            material = MaterialLibrary.CreateLit($"DK_Room_{type}", RoomCatalog.Get(type).FloorColor);
            _floorMaterials[type] = material;
            return material;
        }

        void UpdatePile(int x, int z)
        {
            int capacity = RoomCatalog.CapacityOf(_rooms[x, z]);
            int stored = _stored[x, z];

            if (capacity <= 0 || stored <= 0)
            {
                if (_piles[x, z] != null) Destroy(_piles[x, z].gameObject);
                _piles[x, z] = null;
                return;
            }

            if (_piles[x, z] == null)
            {
                var pile = GameObject.CreatePrimitive(PrimitiveType.Cube);
                pile.name = $"Gold_{x}_{z}";
                Destroy(pile.GetComponent<Collider>());
                pile.transform.SetParent(_roomsRoot, false);
                pile.GetComponent<Renderer>().sharedMaterial = _pileMaterial;
                _piles[x, z] = pile.transform;
            }

            // The pile grows with the tile's fill level, so a glance across the vault reads as
            // a gold bar chart.
            float fill = Mathf.Clamp(stored / (float)capacity, 0f, 1f);
            float height = 0.08f + 0.42f * fill;
            float width = 0.34f + 0.28f * fill;

            _piles[x, z].localScale = new Vector3(width, height, width);
            _piles[x, z].localPosition = _grid.CellToWorld(x, z) + Vector3.up * (0.06f + height * 0.5f);
        }
    }
}
